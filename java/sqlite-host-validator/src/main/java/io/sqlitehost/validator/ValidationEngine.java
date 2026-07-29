package io.sqlitehost.validator;

import io.sqlitehost.model.Protocol;
import io.sqlitehost.model.envelope.BindingValue;
import io.sqlitehost.model.envelope.RuntimeInput;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.envelope.Statement;
import io.sqlitehost.model.envelope.Step;
import io.sqlitehost.model.manifest.InlineFunction;
import io.sqlitehost.model.manifest.ListField;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.model.manifest.MethodDescriptor;
import io.sqlitehost.model.manifest.ScalarField;
import io.sqlitehost.model.manifest.ScalarType;
import io.sqlitehost.validator.sql.FunctionCall;
import io.sqlitehost.validator.sql.InsertStatement;
import io.sqlitehost.validator.sql.SqlAnalyzer;
import io.sqlitehost.validator.sql.SqlToken;
import io.sqlitehost.validator.sql.SqlTokenizer;
import io.sqlitehost.validator.sql.ValueExpr;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

/**
 * Script semantic lint (docs/validation.md layer 4) over a manifest
 * and a parsed script. Implements the pinned Structural, Bindings,
 * Host-call usage, Inline functions, and Result-read lineage codes.
 * All SQL-visible
 * column names (call id, item index, …) come from the manifest columns
 * block. Static call-id resolution covers literals and text bindings;
 * computed ids are skipped by lineage/duplicate checks — documented
 * best-effort linting, not proof.
 */
public final class ValidationEngine {

    /**
     * Feature a script must declare to call inline functions
     * (docs/validation.md); single-sourced in ir.ts and projected into
     * {@link Protocol#FEATURE_INLINE_FUNCTIONS}.
     */
    private static final String FEATURE_INLINE_FUNCTIONS = Protocol.FEATURE_INLINE_FUNCTIONS;

    public ValidationReport validate(Manifest manifest, Script script) {
        List<ValidationFinding> findings = new ArrayList<>();
        checkEnvelope(manifest, script, findings);
        checkDuplicateStepIds(script, findings);
        checkDuplicateInputNames(script, findings);
        checkCompatibility(manifest, script, findings);

        SchemaIndex schema = new SchemaIndex(manifest);
        Analysis analysis = analyzeStatements(schema, script, findings);

        checkUnusedRequiredMethods(manifest, script, analysis, findings);
        checkDuplicateCallIds(schema, analysis, findings);
        checkListChildColocation(schema, analysis, findings);
        checkResultReadLineage(schema, analysis, findings);
        return new ValidationReport(findings);
    }

    // ---------------------------------------------------------------
    // Structural
    // ---------------------------------------------------------------

    private static void checkEnvelope(
            Manifest manifest, Script script, List<ValidationFinding> findings) {
        if (isBlank(script.engine())) {
            findings.add(ValidationFinding.error(ValidationCodes.INVALID_ENVELOPE,
                    "envelope is missing its engine"));
        } else if (!script.engine().equals(manifest.scriptEnvelope().engine())) {
            findings.add(ValidationFinding.error(ValidationCodes.INVALID_ENVELOPE,
                    "envelope engine '" + script.engine() + "' is not '"
                            + manifest.scriptEnvelope().engine() + "'"));
        }
        if (script.requiredApiLevel() == null || script.requiredApiLevel() < 1) {
            findings.add(ValidationFinding.error(ValidationCodes.INVALID_ENVELOPE,
                    "envelope requiredApiLevel must be an integer >= 1"));
        }
        if (script.steps().isEmpty()) {
            findings.add(ValidationFinding.error(ValidationCodes.INVALID_ENVELOPE,
                    "envelope must declare at least one step"));
        }
        for (RuntimeInput input : script.inputs()) {
            if (isBlank(input.name()) || input.value() == null) {
                findings.add(ValidationFinding.error(ValidationCodes.INVALID_ENVELOPE,
                        "runtime inputs must have a non-empty name and a value"));
            }
        }
        for (int s = 0; s < script.steps().size(); s++) {
            Step step = script.steps().get(s);
            if (isBlank(step.id())) {
                findings.add(ValidationFinding.error(ValidationCodes.INVALID_ENVELOPE,
                        "step " + s + " has an empty id"));
            }
            if (step.statements().isEmpty()) {
                findings.add(ValidationFinding.error(ValidationCodes.INVALID_ENVELOPE,
                        step.id(), -1, "step " + s + " has an empty statements list"));
            }
            for (int i = 0; i < step.statements().size(); i++) {
                if (isBlank(step.statements().get(i).sql())) {
                    findings.add(ValidationFinding.error(ValidationCodes.INVALID_ENVELOPE,
                            step.id(), i, "statement has empty sql"));
                }
            }
        }
    }

    private static void checkDuplicateStepIds(Script script, List<ValidationFinding> findings) {
        Set<String> seen = new HashSet<>();
        for (Step step : script.steps()) {
            if (isBlank(step.id())) {
                continue; // already invalid-envelope
            }
            if (!seen.add(step.id())) {
                findings.add(ValidationFinding.error(ValidationCodes.DUPLICATE_STEP_ID,
                        step.id(), -1, "step id '" + step.id() + "' is used more than once"));
            }
        }
    }

    private static void checkDuplicateInputNames(Script script, List<ValidationFinding> findings) {
        Set<String> seen = new HashSet<>();
        for (RuntimeInput input : script.inputs()) {
            if (isBlank(input.name())) {
                continue; // already invalid-envelope
            }
            if (!seen.add(input.name())) {
                findings.add(ValidationFinding.error(ValidationCodes.DUPLICATE_INPUT_NAME,
                        "input name '" + input.name() + "' is used more than once"));
            }
        }
    }

    private static void checkCompatibility(
            Manifest manifest, Script script, List<ValidationFinding> findings) {
        Integer requiredApiLevel = script.requiredApiLevel();
        if (requiredApiLevel != null && requiredApiLevel > manifest.library().apiLevel()) {
            findings.add(ValidationFinding.error(ValidationCodes.REQUIRED_API_LEVEL_TOO_HIGH,
                    "requiredApiLevel " + requiredApiLevel + " exceeds the host apiLevel "
                            + manifest.library().apiLevel()));
        }
        for (String feature : script.requiredFeatures()) {
            if (!manifest.library().features().contains(feature)) {
                findings.add(ValidationFinding.error(ValidationCodes.UNKNOWN_REQUIRED_FEATURE,
                        "required feature '" + feature + "' is not in the manifest features"));
            }
        }
        for (String method : script.requiredMethods()) {
            if (manifest.methodByName(method) == null) {
                findings.add(ValidationFinding.error(ValidationCodes.UNKNOWN_REQUIRED_METHOD,
                        "required method '" + method + "' is not in the manifest"));
            }
        }
    }

    // ---------------------------------------------------------------
    // Per-statement analysis (bindings, host-call usage collection)
    // ---------------------------------------------------------------

    private static Analysis analyzeStatements(
            SchemaIndex schema, Script script, List<ValidationFinding> findings) {
        Analysis analysis = new Analysis();
        for (int s = 0; s < script.steps().size(); s++) {
            Step step = script.steps().get(s);
            for (int i = 0; i < step.statements().size(); i++) {
                Statement statement = step.statements().get(i);
                if (isBlank(statement.sql())) {
                    continue; // already invalid-envelope
                }
                analyzeStatement(schema, script, statement, s, step.id(), i, analysis, findings);
            }
        }
        return analysis;
    }

    private static void analyzeStatement(
            SchemaIndex schema, Script script, Statement statement,
            int stepIndex, String stepId, int statementIndex,
            Analysis analysis, List<ValidationFinding> findings) {
        List<SqlToken> tokens = SqlTokenizer.tokenize(statement.sql());
        Map<String, BindingValue> bindings = statement.bindings();

        // Bindings: missing / unused (shared lexical scan, docs/errors.md).
        Set<String> parameters = SqlTokenizer.parameterNames(tokens);
        for (String parameter : parameters) {
            if (!bindings.containsKey(parameter)) {
                findings.add(ValidationFinding.error(ValidationCodes.MISSING_BINDING,
                        stepId, statementIndex,
                        "SQL references named parameter '" + parameter + "' with no binding"));
            }
        }
        for (String binding : bindings.keySet()) {
            if (!parameters.contains(binding)) {
                findings.add(ValidationFinding.error(ValidationCodes.UNUSED_BINDING,
                        stepId, statementIndex,
                        "binding '" + binding + "' is not referenced by the SQL"));
            }
        }

        // positional-parameter: '?' / '?N' placeholders are forbidden by
        // protocol v1 (docs/script-envelope.md — named parameters only);
        // the shared scanner lexes them as '?' punctuation, so one
        // finding per statement on the first occurrence.
        for (SqlToken token : tokens) {
            if (token.kind() == SqlToken.Kind.PUNCT && "?".equals(token.text())) {
                findings.add(ValidationFinding.error(ValidationCodes.POSITIONAL_PARAMETER,
                        stepId, statementIndex,
                        "SQL uses a positional parameter '?' — positional parameters are"
                                + " not supported in v1; use a named parameter (':name')"));
                break;
            }
        }

        // mixed-prefix-binding: the same bare name written through more
        // than one prefix form in this statement (supported — one
        // binding feeds all forms — but usually an authoring accident).
        Map<String, Set<Character>> prefixesByName = new LinkedHashMap<>();
        for (SqlToken token : tokens) {
            if (token.kind() == SqlToken.Kind.PARAM) {
                prefixesByName.computeIfAbsent(token.text(), k -> new LinkedHashSet<>())
                        .add(token.prefix());
            }
        }
        for (Map.Entry<String, Set<Character>> entry : prefixesByName.entrySet()) {
            if (entry.getValue().size() > 1) {
                StringBuilder forms = new StringBuilder();
                for (char prefix : entry.getValue()) {
                    if (forms.length() > 0) {
                        forms.append(", ");
                    }
                    forms.append(prefix).append(entry.getKey());
                }
                findings.add(ValidationFinding.warning(ValidationCodes.MIXED_PREFIX_BINDING,
                        stepId, statementIndex,
                        "parameter '" + entry.getKey()
                                + "' is written through more than one prefix form (" + forms
                                + ") — use ':" + entry.getKey() + "' consistently"));
            }
        }

        // multiple-statements: exactly one SQL statement per `sql` field is
        // the protocol contract — the native adapter's prepare_v2 compiles the
        // first statement and silently drops the tail. That silent drop is both
        // a general hazard and a denylist bypass: a leading no-op like
        // `SELECT 1; PRAGMA …` would otherwise anchor forbidden-statement and
        // protocol-table-write on the harmless first statement. Detected as a
        // top-level `;` with more SQL after it (a bare trailing `;` is legal).
        if (SqlAnalyzer.hasTrailingStatement(tokens)) {
            findings.add(ValidationFinding.error(ValidationCodes.MULTIPLE_STATEMENTS,
                    stepId, statementIndex,
                    "statement contains more than one SQL statement (a top-level ';'"
                            + " is followed by more SQL) — each 'sql' field must hold"
                            + " exactly one statement; the runtime compiles only the"
                            + " first and silently drops the rest (docs/validation.md)"));
        }

        // forbidden-statement: the statement's leading keyword names a
        // statement kind outside the script surface — transaction control,
        // ATTACH/DETACH, PRAGMA/VACUUM/ANALYZE/REINDEX (docs/validation.md).
        // Matching only the FIRST token is what keeps `pragma_table_info(...)`
        // in a SELECT, a `WITH … INSERT`, and the literal 'PRAGMA' legal.
        String leading = SqlAnalyzer.leadingKeyword(tokens);
        if (leading != null && Protocol.FORBIDDEN_LEADING_KEYWORDS.contains(leading)) {
            findings.add(ValidationFinding.error(ValidationCodes.FORBIDDEN_STATEMENT,
                    stepId, statementIndex,
                    "statement starts with '" + leading.toUpperCase(Locale.ROOT)
                            + "' — transaction control, ATTACH/DETACH and"
                            + " PRAGMA/VACUUM/ANALYZE/REINDEX are not part of the"
                            + " script surface (docs/validation.md)"));
        }

        // protocol-table-write: a write against a runtime-owned table. The
        // protected names are resolved from the manifest, never from a
        // prefix guess, because every one of them is host-configurable.
        String writeTarget = SqlAnalyzer.writeTarget(tokens);
        if (writeTarget != null) {
            String role = schema.protocolTables.get(lower(writeTarget));
            if (role != null) {
                findings.add(ValidationFinding.error(ValidationCodes.PROTOCOL_TABLE_WRITE,
                        stepId, statementIndex,
                        "statement writes '" + writeTarget + "' (" + role
                                + ") — the runtime owns that table; scripts read it,"
                                + " never write it (docs/validation.md)"));
            }
        }

        InsertStatement insert = SqlAnalyzer.parseInsert(tokens);
        if (insert != null) {
            analyzeInsert(schema, script, insert, bindings,
                    stepIndex, stepId, statementIndex, analysis, findings);
        }

        analyzeFunctionCalls(schema, script, tokens, stepId, statementIndex,
                analysis, findings);

        // Result-read lineage collection: result tables referenced +
        // statically resolvable call-id filters (manifest columns.callId).
        Set<String> readMethods = new LinkedHashSet<>();
        for (SqlToken token : tokens) {
            if (token.kind() != SqlToken.Kind.IDENT) {
                continue;
            }
            MethodDescriptor method = schema.resultTables.get(lower(token.text()));
            if (method == null) {
                method = schema.resultChildTables.get(lower(token.text()));
            }
            if (method != null) {
                readMethods.add(method.methodName());
            }
        }
        if (!readMethods.isEmpty()) {
            for (ValueExpr comparison : SqlAnalyzer.callIdComparisons(
                    tokens, schema.callIdColumn)) {
                String callId = resolveStatic(comparison, bindings);
                if (callId != null) {
                    analysis.resultReads.add(new ResultRead(
                            readMethods, callId, stepIndex, stepId, statementIndex));
                }
            }
        }
    }

    private static void analyzeInsert(
            SchemaIndex schema, Script script, InsertStatement insert,
            Map<String, BindingValue> bindings,
            int stepIndex, String stepId, int statementIndex,
            Analysis analysis, List<ValidationFinding> findings) {
        String tableLc = lower(insert.table());
        MethodDescriptor callMethod = schema.callTables.get(tableLc);
        ChildTable callChild = schema.callChildTables.get(tableLc);
        boolean isResultChild = schema.resultChildTables.containsKey(tableLc);

        // implicit-column-list: call table or call/result child table.
        if ((callMethod != null || callChild != null || isResultChild)
                && !insert.hasExplicitColumns()) {
            findings.add(ValidationFinding.error(ValidationCodes.IMPLICIT_COLUMN_LIST,
                    stepId, statementIndex,
                    "INSERT INTO " + insert.table() + " must use an explicit column list"));
        }

        if (callMethod != null) {
            if (!script.requiredMethods().contains(callMethod.methodName())) {
                findings.add(ValidationFinding.error(ValidationCodes.UNDECLARED_METHOD_USE,
                        stepId, statementIndex,
                        "call table " + insert.table() + " belongs to method '"
                                + callMethod.methodName()
                                + "' which is not declared in requiredMethods"));
            }
            // The script depends on this method's apiLevel: an
            // under-declared requiredApiLevel would let an older host
            // silently fail to clean-skip (mirrors checkCompatibility's
            // required-api-level-too-high, but per method).
            if (script.requiredApiLevel() != null
                    && callMethod.apiLevel() > script.requiredApiLevel()) {
                findings.add(ValidationFinding.error(ValidationCodes.METHOD_API_LEVEL_TOO_HIGH,
                        stepId, statementIndex,
                        "method '" + callMethod.methodName() + "' requires apiLevel "
                                + callMethod.apiLevel()
                                + " which exceeds the script's requiredApiLevel "
                                + script.requiredApiLevel()));
            }
            checkBindingTypes(schema, insert, tableLc, bindings,
                    stepId, statementIndex, findings);
            List<List<ValueExpr>> rows = insert.valueRows();
            if (rows.isEmpty() || !insert.hasExplicitColumns()) {
                // Still counts as a write for usage tracking; id unresolvable.
                analysis.callEmits.add(new CallEmit(callMethod.methodName(), tableLc,
                        null, stepIndex, stepId, statementIndex));
            } else {
                for (List<ValueExpr> row : rows) {
                    String callId = resolveStatic(
                            insert.valueFor(row, schema.callIdColumn), bindings);
                    analysis.callEmits.add(new CallEmit(callMethod.methodName(), tableLc,
                            callId, stepIndex, stepId, statementIndex));
                }
            }
        } else if (callChild != null) {
            checkBindingTypes(schema, insert, tableLc, bindings,
                    stepId, statementIndex, findings);
            List<List<ValueExpr>> rows = insert.valueRows();
            for (List<ValueExpr> row : rows) {
                String callId = insert.hasExplicitColumns()
                        ? resolveStatic(insert.valueFor(row, schema.callIdColumn), bindings)
                        : null;
                analysis.childWrites.add(new ChildWrite(callChild.method.methodName(),
                        callChild.listField.childTable(), callId,
                        stepIndex, stepId, statementIndex));
            }
        }
    }

    /**
     * Inline function lint (docs/validation.md — feature
     * inlineFunctions) over the {@code identifier(...)} calls of one
     * statement: a manifest inline function must be declared through
     * requiredFeatures (undeclared-feature-use) and called with an
     * argument count inside minArgs..maxArgs (function-arity-mismatch);
     * an unmatched identifier is unknown-function only when it carries
     * the host's functionPrefix — non-prefix identifiers (max(...),
     * abs(...)) are SQLite's business, not the lint's.
     *
     * <p>The same pass raises the determinism warning
     * (nondeterministic-function), which is about built-ins rather than
     * inline functions but reads the identical call list.</p>
     */
    private static void analyzeFunctionCalls(
            SchemaIndex schema, Script script, List<SqlToken> tokens,
            String stepId, int statementIndex,
            Analysis analysis, List<ValidationFinding> findings) {
        Set<String> reported = new HashSet<>();
        Set<String> reportedPortability = new HashSet<>();
        for (FunctionCall call : SqlAnalyzer.functionCalls(tokens)) {
            String nameLc = lower(call.name());
            // Engine portability is only meaningful for SQLite's own
            // built-ins: a manifest inline function is supplied by the host
            // adapter through sqlite3_create_function, so neither the engine
            // version nor its compile options decide whether it exists.
            if (!schema.inlineFunctions.containsKey(nameLc)) {
                checkFunctionPortability(schema, nameLc, call, stepId, statementIndex,
                        reportedPortability, findings);
            }
            if (isNondeterministic(nameLc, call)) {
                findings.add(ValidationFinding.warning(
                        ValidationCodes.NONDETERMINISTIC_FUNCTION,
                        stepId, statementIndex,
                        "SQL calls the nondeterministic built-in '" + call.name()
                                + "' — replaying this script would diverge from the"
                                + " original run; compute the value in the host and"
                                + " bind it instead"));
            }
            MethodDescriptor method = schema.inlineFunctions.get(nameLc);
            if (method == null) {
                if (nameLc.startsWith(schema.functionPrefixLc) && reported.add(nameLc)) {
                    findings.add(ValidationFinding.error(ValidationCodes.UNKNOWN_FUNCTION,
                            stepId, statementIndex,
                            "function '" + call.name() + "' matches the functionPrefix '"
                                    + schema.functionPrefix
                                    + "' but is not an inline function of the manifest"));
                }
                continue;
            }
            analysis.inlineInvokedMethods.add(method.methodName());
            if (!script.requiredFeatures().contains(FEATURE_INLINE_FUNCTIONS)
                    && reported.add(nameLc)) {
                findings.add(ValidationFinding.error(ValidationCodes.UNDECLARED_FEATURE_USE,
                        stepId, statementIndex,
                        "inline function '" + call.name() + "' requires feature '"
                                + FEATURE_INLINE_FUNCTIONS
                                + "' which is not declared in requiredFeatures"));
            }
            // Inline invocation is not gated by requiredMethods, so the
            // apiLevel dependency must be checked here too: a level-1 host
            // that supports inlineFunctions but lacks this method would
            // raise "no such function" at runtime instead of clean-skipping.
            if (script.requiredApiLevel() != null
                    && method.apiLevel() > script.requiredApiLevel()
                    && reported.add(nameLc)) {
                findings.add(ValidationFinding.error(ValidationCodes.METHOD_API_LEVEL_TOO_HIGH,
                        stepId, statementIndex,
                        "inline function '" + call.name() + "' (method '"
                                + method.methodName() + "') requires apiLevel "
                                + method.apiLevel()
                                + " which exceeds the script's requiredApiLevel "
                                + script.requiredApiLevel()));
            }
            InlineFunction inline = method.inline();
            if (call.argCount() != FunctionCall.UNKNOWN_ARGS
                    && (call.argCount() < inline.minArgs()
                            || call.argCount() > inline.maxArgs())) {
                findings.add(ValidationFinding.error(ValidationCodes.FUNCTION_ARITY_MISMATCH,
                        stepId, statementIndex,
                        "inline function '" + call.name() + "' is called with "
                                + call.argCount() + " argument(s) but takes "
                                + inline.minArgs()
                                + (inline.maxArgs() == inline.minArgs()
                                        ? "" : ".." + inline.maxArgs())));
            }
        }
    }

    /**
     * Engine-portability lints for one built-in call (docs/validation.md):
     *
     * <ul>
     *   <li>nonportable-function — the built-in is compile-gated, so no
     *       {@code minSqliteVersion} can make it safe. Checked first: a
     *       compile-gated function must not be reported as a mere version
     *       problem, because raising the floor would not fix it.</li>
     *   <li>sqlite-version-too-low-for-function — the built-in was introduced
     *       after the host's declared floor
     *       ({@code manifest.library.minSqliteVersionNumber}). This is the
     *       check that turns a crash on a player's device into a CI failure;
     *       the prepare-only layer cannot see it because it compiles on a much
     *       newer engine than the floor (docs/validation.md §3).</li>
     * </ul>
     *
     * <p>One finding per distinct function name per statement.</p>
     */
    private static void checkFunctionPortability(
            SchemaIndex schema, String nameLc, FunctionCall call,
            String stepId, int statementIndex,
            Set<String> reported, List<ValidationFinding> findings) {
        if (Protocol.NONPORTABLE_FUNCTIONS.contains(nameLc)) {
            if (reported.add(nameLc)) {
                findings.add(ValidationFinding.error(ValidationCodes.NONPORTABLE_FUNCTION,
                        stepId, statementIndex,
                        "'" + call.name() + "' is only present when the device's SQLite"
                                + " was compiled with -DSQLITE_ENABLE_MATH_FUNCTIONS —"
                                + " its availability is a compile option, not a version,"
                                + " so raising minSqliteVersion cannot make it safe;"
                                + " compute the value in the host and bind it instead"));
            }
            return;
        }
        int minVersion = minVersionFor(nameLc);
        if (minVersion > schema.minSqliteVersionNumber && reported.add(nameLc)) {
            findings.add(ValidationFinding.error(
                    ValidationCodes.SQLITE_VERSION_TOO_LOW_FOR_FUNCTION,
                    stepId, statementIndex,
                    "built-in '" + call.name() + "' requires SQLite "
                            + formatVersion(minVersion) + " but the host declares a floor of "
                            + formatVersion(schema.minSqliteVersionNumber)
                            + " — raise the host's minSqliteVersion or avoid the function"));
        }
    }

    /**
     * Minimum SQLITE_VERSION_NUMBER for a built-in: the exact-name table
     * first, then the longest matching family prefix (so {@code jsonb_extract}
     * resolves to the JSONB floor, not the older JSON one). Returns 0 for
     * anything at or below the plan's floor — i.e. "always safe".
     */
    private static int minVersionFor(String nameLc) {
        Integer exact = Protocol.FUNCTION_MIN_VERSION.get(nameLc);
        if (exact != null) {
            return exact;
        }
        int best = 0;
        int bestLength = -1;
        for (Map.Entry<String, Integer> entry : Protocol.FUNCTION_PREFIX_MIN_VERSION.entrySet()) {
            if (nameLc.startsWith(entry.getKey()) && entry.getKey().length() > bestLength) {
                bestLength = entry.getKey().length();
                best = entry.getValue();
            }
        }
        return best;
    }

    /** Render a SQLITE_VERSION_NUMBER (MAJ*1000000 + MIN*1000 + PATCH) as M.N.P. */
    private static String formatVersion(int versionNumber) {
        return (versionNumber / 1000000) + "." + ((versionNumber / 1000) % 1000)
                + "." + (versionNumber % 1000);
    }

    /**
     * nondeterministic-function (docs/validation.md): the ALWAYS list is
     * flagged on every call; a date/time built-in only when it reads the
     * wall clock — zero arguments, or a top-level {@code 'now'} literal.
     * Both lists are single-sourced in ir.ts and projected into
     * {@link Protocol} (docs/proposals/rule-parameters-as-data.md).
     */
    private static boolean isNondeterministic(String nameLc, FunctionCall call) {
        if (Protocol.NONDETERMINISTIC_FUNCTIONS_ALWAYS.contains(nameLc)) {
            return true;
        }
        return Protocol.NONDETERMINISTIC_TIME_FUNCTIONS.contains(nameLc)
                && (call.argCount() == 0 || call.hasNowArg());
    }

    /**
     * binding-type-mismatch: for inserts into a call table (or a call
     * list child table) with an explicit column list, a parameter that
     * feeds a known column must be compatible with the column's scalar
     * type; optional columns also accept null (docs/validation.md).
     */
    private static void checkBindingTypes(
            SchemaIndex schema, InsertStatement insert, String tableLc,
            Map<String, BindingValue> bindings,
            String stepId, int statementIndex, List<ValidationFinding> findings) {
        Map<String, ColumnType> columns = schema.insertableColumns.get(tableLc);
        if (columns == null || !insert.hasExplicitColumns()) {
            return;
        }
        for (List<ValueExpr> row : insert.valueRows()) {
            int cells = Math.min(insert.columns().size(), row.size());
            for (int i = 0; i < cells; i++) {
                ValueExpr value = row.get(i);
                if (value.kind() != ValueExpr.Kind.PARAM) {
                    continue;
                }
                BindingValue binding = bindings.get(value.text());
                if (binding == null) {
                    continue; // missing-binding already reported
                }
                ColumnType column = columns.get(lower(insert.columns().get(i)));
                if (column == null) {
                    continue; // unknown column — prepare-only validation reports it
                }
                if (!compatible(column, binding.type())) {
                    findings.add(ValidationFinding.error(ValidationCodes.BINDING_TYPE_MISMATCH,
                            stepId, statementIndex,
                            "binding '" + value.text() + "' of type "
                                    + binding.type().jsonName()
                                    + " is not compatible with column "
                                    + insert.table() + "." + insert.columns().get(i)
                                    + " (" + column.describe() + ")"));
                }
            }
        }
    }

    private static boolean compatible(ColumnType column, BindingValue.Type type) {
        // A null binding is accepted iff the column is optional — orthogonal
        // to the per-type table. The type→accepted-types matrix itself is
        // single-sourced in ir.ts and projected into Protocol.BINDING_TYPE_COMPAT
        // (docs/proposals/rule-parameters-as-data.md): int64 widens from int32,
        // float64 from float32, integers never coerce into float columns.
        if (type == BindingValue.Type.NULL) {
            return column.optional();
        }
        Set<String> accepted = Protocol.BINDING_TYPE_COMPAT.get(column.scalarType().jsonName());
        return accepted != null && accepted.contains(type.jsonName());
    }

    // ---------------------------------------------------------------
    // Cross-statement checks
    // ---------------------------------------------------------------

    private static void checkUnusedRequiredMethods(
            Manifest manifest, Script script, Analysis analysis,
            List<ValidationFinding> findings) {
        for (String methodName : script.requiredMethods()) {
            if (manifest.methodByName(methodName) == null) {
                continue; // unknown-required-method already reported
            }
            boolean written = analysis.callEmits.stream()
                    .anyMatch(emit -> emit.methodName.equals(methodName));
            if (!written && !analysis.inlineInvokedMethods.contains(methodName)) {
                findings.add(ValidationFinding.warning(ValidationCodes.UNUSED_REQUIRED_METHOD,
                        "required method '" + methodName
                                + "' is declared but its call table is never written"
                                + " and its inline function is never invoked"));
            }
        }
    }

    private static void checkDuplicateCallIds(
            SchemaIndex schema, Analysis analysis, List<ValidationFinding> findings) {
        // Keyed by call id alone: the queue table declares the call-id
        // column UNIQUE and every call-table trigger inserts into it, so
        // call ids are global across call tables — not per table.
        Map<String, CallEmit> firstByCallId = new LinkedHashMap<>();
        for (CallEmit emit : analysis.callEmits) {
            if (emit.callId == null) {
                continue; // computed ids are skipped by duplicate checks
            }
            CallEmit first = firstByCallId.putIfAbsent(emit.callId, emit);
            if (first != null) {
                findings.add(ValidationFinding.error(ValidationCodes.DUPLICATE_CALL_ID,
                        emit.stepId, emit.statementIndex,
                        schema.callIdColumn + " '" + emit.callId
                                + "' is emitted more than once (call ids are unique"
                                + " across all call tables; first emitted in step '"
                                + first.stepId + "' into '" + first.callTableLc + "')"));
            }
        }
    }

    private static void checkListChildColocation(
            SchemaIndex schema, Analysis analysis, List<ValidationFinding> findings) {
        for (ChildWrite write : analysis.childWrites) {
            if (write.callId == null) {
                continue; // computed ids are skipped
            }
            CallEmit parent = null;
            for (CallEmit emit : analysis.callEmits) {
                if (emit.methodName.equals(write.methodName)
                        && write.callId.equals(emit.callId)) {
                    parent = emit;
                    break;
                }
            }
            if (parent != null) {
                if (parent.stepIndex != write.stepIndex) {
                    findings.add(ValidationFinding.error(ValidationCodes.LIST_CHILD_LATER_STEP,
                            write.stepId, write.statementIndex,
                            "child list rows for " + schema.callIdColumn + " '"
                                    + write.callId + "' ("
                                    + write.childTable + ") are emitted in step '" + write.stepId
                                    + "' but their parent call row is in step '" + parent.stepId
                                    + "' — parents and children must be colocated"));
                }
                continue;
            }
            boolean methodHasComputedEmit = analysis.callEmits.stream()
                    .anyMatch(emit -> emit.methodName.equals(write.methodName)
                            && emit.callId == null);
            if (!methodHasComputedEmit) {
                findings.add(ValidationFinding.error(ValidationCodes.LIST_CHILD_WITHOUT_PARENT,
                        write.stepId, write.statementIndex,
                        "child list rows reference " + schema.callIdColumn + " '"
                                + write.callId
                                + "' (" + write.childTable + ") but no statement inserts"
                                + " that parent call row"));
            }
        }
    }

    private static void checkResultReadLineage(
            SchemaIndex schema, Analysis analysis, List<ValidationFinding> findings) {
        // method -> statically emitted call id -> earliest emitting step.
        Map<String, Map<String, Integer>> staticEmits = new HashMap<>();
        // method -> earliest step with a computed (unresolvable) emit.
        Map<String, Integer> computedEmits = new HashMap<>();
        for (CallEmit emit : analysis.callEmits) {
            if (emit.callId == null) {
                computedEmits.merge(emit.methodName, emit.stepIndex, Math::min);
            } else {
                staticEmits.computeIfAbsent(emit.methodName, k -> new HashMap<>())
                        .merge(emit.callId, emit.stepIndex, Math::min);
            }
        }
        // A statement can join result tables of several methods (set M)
        // while each resolved call id belongs to only one of them, so a
        // finding is reported only when NO method in M can satisfy the
        // read: unknown-call when no method emits the id (computed emits
        // count as possible matches — skip), not-after-call when every
        // emitting method violates the ordering.
        for (ResultRead read : analysis.resultReads) {
            boolean satisfied = false;
            List<String> unknownMethods = new ArrayList<>();
            List<String> notAfterMethods = new ArrayList<>();
            for (String method : read.methods) {
                Integer emitStep = staticEmits
                        .getOrDefault(method, Map.of())
                        .get(read.callId);
                Integer computedStep = computedEmits.get(method);
                if (emitStep == null) {
                    // Best-effort: a computed emit for this method could
                    // produce the id — skip rather than false-positive.
                    if (computedStep != null) {
                        satisfied = true;
                    } else {
                        unknownMethods.add(method);
                    }
                    continue;
                }
                boolean earlierComputed = computedStep != null && computedStep < read.stepIndex;
                if (emitStep < read.stepIndex || earlierComputed) {
                    satisfied = true;
                } else {
                    notAfterMethods.add(method);
                }
            }
            if (satisfied) {
                continue;
            }
            if (!notAfterMethods.isEmpty()) {
                for (String method : notAfterMethods) {
                    findings.add(ValidationFinding.error(
                            ValidationCodes.RESULT_READ_NOT_AFTER_CALL,
                            read.stepId, read.statementIndex,
                            "statement reads results of method '" + method
                                    + "' for " + schema.callIdColumn + " '" + read.callId
                                    + "' in the same or an earlier step than the emitting"
                                    + " insert — results only exist after the emitting"
                                    + " step's drain"));
                }
            } else {
                for (String method : unknownMethods) {
                    findings.add(ValidationFinding.error(
                            ValidationCodes.RESULT_READ_UNKNOWN_CALL,
                            read.stepId, read.statementIndex,
                            "statement reads results of method '" + method
                                    + "' for " + schema.callIdColumn + " '" + read.callId
                                    + "' but no statement emits that call"));
                }
            }
        }
    }

    // ---------------------------------------------------------------
    // Helpers and internal model
    // ---------------------------------------------------------------

    private static String resolveStatic(ValueExpr value, Map<String, BindingValue> bindings) {
        if (value == null) {
            return null;
        }
        if (value.kind() == ValueExpr.Kind.STRING) {
            return value.text();
        }
        if (value.kind() == ValueExpr.Kind.PARAM) {
            BindingValue binding = bindings.get(value.text());
            if (binding != null && binding.type() == BindingValue.Type.TEXT) {
                return binding.asText();
            }
        }
        return null;
    }

    private static boolean isBlank(String value) {
        return value == null || value.isBlank();
    }

    private static String lower(String value) {
        return value.toLowerCase(Locale.ROOT);
    }

    /** Column typing for insert checks; the call-id/item-index columns use pinned types. */
    private record ColumnType(ScalarType scalarType, boolean optional, String role) {
        String describe() {
            return role != null ? role : scalarType.jsonName() + (optional ? ", optional" : "");
        }
    }

    private record ChildTable(MethodDescriptor method, ListField listField) {
    }

    private static final class CallEmit {
        final String methodName;
        final String callTableLc;
        final String callId; // null when not statically resolvable
        final int stepIndex;
        final String stepId;
        final int statementIndex;

        CallEmit(String methodName, String callTableLc, String callId,
                 int stepIndex, String stepId, int statementIndex) {
            this.methodName = methodName;
            this.callTableLc = callTableLc;
            this.callId = callId;
            this.stepIndex = stepIndex;
            this.stepId = stepId;
            this.statementIndex = statementIndex;
        }
    }

    private static final class ChildWrite {
        final String methodName;
        final String childTable;
        final String callId; // null when not statically resolvable
        final int stepIndex;
        final String stepId;
        final int statementIndex;

        ChildWrite(String methodName, String childTable, String callId,
                   int stepIndex, String stepId, int statementIndex) {
            this.methodName = methodName;
            this.childTable = childTable;
            this.callId = callId;
            this.stepIndex = stepIndex;
            this.stepId = stepId;
            this.statementIndex = statementIndex;
        }
    }

    private static final class ResultRead {
        final Set<String> methods;
        final String callId;
        final int stepIndex;
        final String stepId;
        final int statementIndex;

        ResultRead(Set<String> methods, String callId,
                   int stepIndex, String stepId, int statementIndex) {
            this.methods = methods;
            this.callId = callId;
            this.stepIndex = stepIndex;
            this.stepId = stepId;
            this.statementIndex = statementIndex;
        }
    }

    private static final class Analysis {
        final List<CallEmit> callEmits = new ArrayList<>();
        final List<ChildWrite> childWrites = new ArrayList<>();
        final List<ResultRead> resultReads = new ArrayList<>();
        /** Methods whose inline function is invoked somewhere in the script. */
        final Set<String> inlineInvokedMethods = new HashSet<>();
    }

    /** Physical-name lookups derived from the manifest (names are resolved). */
    private static final class SchemaIndex {
        final String callIdColumn;
        final String itemIndexColumn;
        final String functionPrefix;
        final String functionPrefixLc;
        final Map<String, MethodDescriptor> callTables = new HashMap<>();
        final Map<String, ChildTable> callChildTables = new HashMap<>();
        final Map<String, MethodDescriptor> resultTables = new HashMap<>();
        final Map<String, MethodDescriptor> resultChildTables = new HashMap<>();
        final Map<String, Map<String, ColumnType>> insertableColumns = new HashMap<>();
        final Map<String, MethodDescriptor> inlineFunctions = new HashMap<>();
        /**
         * The host's declared SQLite floor, the comparison value of the
         * sqlite-version-too-low-for-function lint. Parsed from every
         * manifest since v1 but, before this lint, read by no validator.
         */
        final int minSqliteVersionNumber;
        /**
         * Runtime-owned tables (lowercased) mapped to a human role, for
         * protocol-table-write. Resolved from the manifest rather than from
         * a `result_`/`call_` prefix guess, because every one of these names
         * is host-configurable (docs/naming.md). Deliberately EXCLUDES the
         * call tables and their child tables (writing them is how a script
         * makes a host call), plus script_vars and script_control (the
         * script's own scratch and control surfaces).
         */
        final Map<String, String> protocolTables = new HashMap<>();

        SchemaIndex(Manifest manifest) {
            callIdColumn = manifest.columns().callId();
            itemIndexColumn = manifest.columns().itemIndex();
            functionPrefix = manifest.naming().functionPrefix();
            functionPrefixLc = lower(functionPrefix);
            minSqliteVersionNumber = manifest.library().minSqliteVersionNumber();
            protocolTables.put(lower(manifest.queueTable().name()),
                    "the host-call queue table");
            protocolTables.put(lower(manifest.inputsTable().name()),
                    "the runtime inputs table");
            for (MethodDescriptor method : manifest.methods()) {
                protocolTables.put(lower(method.resultTable()),
                        "the result table of method '" + method.methodName() + "'");
                for (ListField listField : method.result().listFields()) {
                    protocolTables.put(lower(listField.childTable()),
                            "a result list table of method '" + method.methodName() + "'");
                }
            }
            for (MethodDescriptor method : manifest.methods()) {
                callTables.put(lower(method.callTable()), method);
                resultTables.put(lower(method.resultTable()), method);
                if (method.inline() != null) {
                    inlineFunctions.put(lower(method.inline().functionName()), method);
                }

                Map<String, ColumnType> callColumns = new HashMap<>();
                callColumns.put(lower(callIdColumn),
                        new ColumnType(ScalarType.STRING, false, callIdColumn));
                for (ScalarField field : method.input().fields()) {
                    callColumns.put(lower(field.column()),
                            new ColumnType(field.scalarType(), field.optional(), null));
                }
                insertableColumns.put(lower(method.callTable()), callColumns);

                for (ListField listField : method.input().listFields()) {
                    callChildTables.put(lower(listField.childTable()),
                            new ChildTable(method, listField));
                    Map<String, ColumnType> childColumns = new HashMap<>();
                    childColumns.put(lower(callIdColumn),
                            new ColumnType(ScalarType.STRING, false, callIdColumn));
                    childColumns.put(lower(itemIndexColumn),
                            new ColumnType(ScalarType.INT64, false, itemIndexColumn));
                    for (ScalarField field : listField.itemFields()) {
                        childColumns.put(lower(field.column()),
                                new ColumnType(field.scalarType(), field.optional(), null));
                    }
                    insertableColumns.put(lower(listField.childTable()), childColumns);
                }
                for (ListField listField : method.result().listFields()) {
                    resultChildTables.put(lower(listField.childTable()), method);
                }
            }
        }
    }
}
