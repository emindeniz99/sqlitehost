package io.sqlitehost.validator.sql;

import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Deque;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Statement-shape analysis over the token stream: INSERT parsing,
 * {@code identifier(...)} function-call extraction, and
 * {@code <callIdColumn> = <atom>} comparison extraction (the call-id
 * column name comes from the manifest columns block). Best-effort
 * static analysis for lint purposes (docs/validation.md) — not a SQL
 * parser.
 */
public final class SqlAnalyzer {

    private static final Set<String> SELECT_ITEM_TERMINATORS =
            Set.of("from", "where", "group", "order", "limit", "union", "except", "intersect");

    private SqlAnalyzer() {
    }

    /**
     * Whether the token may stand where SQLite requires a <b>name</b> (a
     * table, a column): an identifier in any of its quoting forms, or a
     * single-quoted string.
     *
     * <p>The string case is SQLite's documented MySQL-compatibility
     * misfeature — "If a keyword in single quotes is used in a context where
     * an identifier is allowed but where a string literal is not allowed,
     * then the token is understood to be an identifier"
     * (sqlite.org/lang_keywords.html). Verified against the sqlite3 CLI
     * 3.51.0: {@code DELETE FROM 'pending_host_calls'},
     * {@code UPDATE 'result_get_value' SET …},
     * {@code INSERT INTO 'result_get_value' (…)} and
     * {@code DELETE FROM main.'pending_host_calls'} all compile and run.
     * Requiring {@link SqlToken.Kind#IDENT} in name position left every one
     * of them invisible to protocol-table-write, the INSERT analysis and
     * result-read lineage — a one-character bypass of the whole denylist.</p>
     *
     * <p>Deliberately NOT used in <em>value</em> position: {@link #isAtom}
     * and the call-id resolution must keep reading {@code 'x'} as the literal
     * it is there, or static call-id resolution changes meaning. Mirrors the
     * TypeScript {@code isNameToken}.</p>
     */
    public static boolean isName(SqlToken token) {
        return token.kind() == SqlToken.Kind.IDENT || token.kind() == SqlToken.Kind.STRING;
    }

    /**
     * Parse the statement as an INSERT, or return {@code null} when it
     * is not one. Handles {@code INSERT [OR …] INTO <table>} with an
     * optional column list, followed by {@code VALUES (…) [, (…)]…},
     * {@code SELECT …} (first top-level items map to the column list),
     * or {@code DEFAULT VALUES}.
     */
    public static InsertStatement parseInsert(List<SqlToken> tokens) {
        int i = indexOfIdent(tokens, "insert");
        if (i < 0) {
            return null;
        }
        // Skip conflict clause idents (OR REPLACE / OR IGNORE / …) up to INTO.
        int into = -1;
        for (int j = i + 1; j < tokens.size() && j <= i + 3; j++) {
            if (tokens.get(j).isIdent("into")) {
                into = j;
                break;
            }
            if (tokens.get(j).kind() != SqlToken.Kind.IDENT) {
                return null;
            }
        }
        if (into < 0 || into + 1 >= tokens.size()) {
            return null;
        }
        int pos = into + 1;
        if (!isName(tokens.get(pos))) {
            return null;
        }
        String table = tokens.get(pos).text();
        pos++;
        // Schema-qualified name: keep the last component.
        while (pos + 1 < tokens.size()
                && tokens.get(pos).isPunct(".")
                && isName(tokens.get(pos + 1))) {
            table = tokens.get(pos + 1).text();
            pos += 2;
        }

        // Optional `AS <alias>` between the table name and the column
        // list (valid SQLite >= 3.24.0, e.g. INSERT INTO t AS c (...) …).
        // Skip it so the explicit column list is still recognized. Only
        // the `AS <ident>` form is handled — a bare alias is a syntax
        // error for INSERT targets, and VALUES/SELECT/DEFAULT are idents
        // that must not be swallowed.
        if (pos < tokens.size() && tokens.get(pos).isIdent("as")) {
            pos++;
            if (pos < tokens.size() && tokens.get(pos).kind() == SqlToken.Kind.IDENT) {
                pos++;
            }
        }

        List<String> columns = null;
        if (pos < tokens.size() && tokens.get(pos).isPunct("(")) {
            columns = new ArrayList<>();
            pos++;
            while (pos < tokens.size() && !tokens.get(pos).isPunct(")")) {
                if (isName(tokens.get(pos))) {
                    columns.add(tokens.get(pos).text());
                }
                pos++;
            }
            if (pos < tokens.size()) {
                pos++; // consume ')'
            }
        }

        if (pos < tokens.size() && tokens.get(pos).isIdent("values")) {
            return new InsertStatement(table, columns, parseValuesRows(tokens, pos + 1), null);
        }
        if (pos < tokens.size() && tokens.get(pos).isIdent("select")) {
            return new InsertStatement(table, columns, null, parseSelectItems(tokens, pos + 1));
        }
        if (pos + 1 < tokens.size()
                && tokens.get(pos).isIdent("default")
                && tokens.get(pos + 1).isIdent("values")) {
            return new InsertStatement(table, columns, null, null);
        }
        return new InsertStatement(table, columns, null, null);
    }

    /** Parse {@code (expr, …) [, (expr, …)]…} rows after VALUES. */
    private static List<List<ValueExpr>> parseValuesRows(List<SqlToken> tokens, int start) {
        List<List<ValueExpr>> rows = new ArrayList<>();
        int pos = start;
        while (pos < tokens.size() && tokens.get(pos).isPunct("(")) {
            List<ValueExpr> row = new ArrayList<>();
            List<SqlToken> current = new ArrayList<>();
            int depth = 1;
            pos++;
            while (pos < tokens.size() && depth > 0) {
                SqlToken token = tokens.get(pos);
                if (token.isPunct("(")) {
                    depth++;
                } else if (token.isPunct(")")) {
                    depth--;
                    if (depth == 0) {
                        pos++;
                        break;
                    }
                } else if (token.isPunct(",") && depth == 1) {
                    row.add(ValueExpr.classify(current));
                    current = new ArrayList<>();
                    pos++;
                    continue;
                }
                current.add(token);
                pos++;
            }
            if (!current.isEmpty()) {
                row.add(ValueExpr.classify(current));
            }
            rows.add(row);
            // Another row?
            if (pos < tokens.size() && tokens.get(pos).isPunct(",")) {
                pos++;
                continue;
            }
            break;
        }
        return rows;
    }

    /** Parse top-level SELECT items until FROM/WHERE/… at depth 0. */
    private static List<ValueExpr> parseSelectItems(List<SqlToken> tokens, int start) {
        List<ValueExpr> items = new ArrayList<>();
        List<SqlToken> current = new ArrayList<>();
        int depth = 0;
        for (int pos = start; pos < tokens.size(); pos++) {
            SqlToken token = tokens.get(pos);
            if (token.isPunct("(")) {
                depth++;
            } else if (token.isPunct(")")) {
                depth--;
            } else if (depth == 0 && token.kind() == SqlToken.Kind.IDENT
                    && SELECT_ITEM_TERMINATORS.contains(token.text().toLowerCase(Locale.ROOT))) {
                break;
            } else if (depth == 0 && token.isPunct(",")) {
                items.add(ValueExpr.classify(current));
                current = new ArrayList<>();
                continue;
            }
            current.add(token);
        }
        if (!current.isEmpty()) {
            items.add(ValueExpr.classify(current));
        }
        return items;
    }

    /**
     * Extract every {@code identifier(...)} function call: an
     * {@link SqlToken.Kind#IDENT} immediately followed by {@code '('},
     * with the argument count taken by a top-level comma scan to the
     * matching {@code ')'}. String literals and comments never confuse
     * the scan — the tokenizer already collapsed them. Calls nested in
     * another call's arguments are extracted as their own entries.
     *
     * <p>One pass, one stack of open parens. The obvious shape — find each
     * {@code identifier(}, then scan forward for its matching {@code ')'} —
     * is quadratic in the nesting depth, and SQL nests:
     * {@code abs(abs(abs(…)))} a hundred thousand deep is 10^10 token
     * visits, and the engine walks the call list twice per statement. The
     * single scan below is proportional to the token count whatever the
     * shape.</p>
     */
    public static List<FunctionCall> functionCalls(List<SqlToken> tokens) {
        List<FunctionCall> calls = new ArrayList<>();
        Deque<ParenFrame> stack = new ArrayDeque<>();
        for (int i = 0; i < tokens.size(); i++) {
            SqlToken token = tokens.get(i);
            ParenFrame top = stack.peek();
            if (token.isPunct("(")) {
                if (top != null) {
                    noteNestedToken(top);
                }
                // A call frame remembers WHERE its result goes, so the
                // finished list stays in opening-token order however the
                // nesting closes.
                int slot = -1;
                if (i > 0 && tokens.get(i - 1).kind() == SqlToken.Kind.IDENT) {
                    slot = calls.size();
                    calls.add(new FunctionCall(tokens.get(i - 1).text(),
                            FunctionCall.UNKNOWN_ARGS, false));
                }
                stack.push(new ParenFrame(slot));
                continue;
            }
            if (token.isPunct(")")) {
                ParenFrame frame = stack.poll();
                if (frame == null) {
                    continue; // stray ')' — closes nothing
                }
                if (frame.slot >= 0) {
                    calls.set(frame.slot, new FunctionCall(calls.get(frame.slot).name(),
                            frame.sawArgToken ? frame.commas + 1 : 0,
                            frame.nowSeen || (frame.argTokens == 1 && frame.argIsNow)));
                }
                ParenFrame parent = stack.peek();
                if (parent != null) {
                    noteNestedToken(parent);
                }
                continue;
            }
            if (top == null) {
                continue; // outside every paren — nothing to count
            }
            if (token.isPunct(",")) {
                top.commas++;
                top.sawArgToken = true;
                if (top.argTokens == 1 && top.argIsNow) {
                    top.nowSeen = true;
                }
                top.argTokens = 0;
                top.argIsNow = false;
                continue;
            }
            if (top.argTokens == 0) {
                top.argIsNow = isNowLiteral(token);
            }
            top.argTokens++;
            top.sawArgToken = true;
        }
        // Frames still open at end of input have no matching ')': arity is
        // unknowable, but a 'now' already closed off by a top-level comma
        // was seen for certain.
        for (ParenFrame frame : stack) {
            if (frame.slot >= 0 && frame.nowSeen) {
                calls.set(frame.slot, new FunctionCall(calls.get(frame.slot).name(),
                        FunctionCall.UNKNOWN_ARGS, true));
            }
        }
        return calls;
    }

    /** One open {@code '('} — a call when {@code slot} indexes the result. */
    private static final class ParenFrame {
        private final int slot;
        private int commas;
        private boolean sawArgToken;
        /** Tokens in the current top-level argument, CLAMPED at 2. */
        private int argTokens;
        private boolean argIsNow;
        /** A completed top-level argument was exactly the literal 'now'. */
        private boolean nowSeen;

        private ParenFrame(int slot) {
            this.slot = slot;
        }
    }

    /**
     * Account for a whole nested paren group in the enclosing frame with
     * O(1) work — the step that makes the pass linear.
     *
     * <p>Walking every enclosing frame per token would keep the cost
     * quadratic in the nesting depth, which is the bug this replaced. It is
     * unnecessary because an enclosing frame only ever asks two questions:
     * whether its current argument holds any token at all, and whether that
     * argument is EXACTLY one token which is {@code 'now'}. An argument
     * containing a nested group already fails the second test — the group's
     * own {@code '('} and {@code ')'} are two tokens — so clamping the count
     * at 2 is not an approximation: no reachable read can tell the
     * difference. Commas inside the group belong to the group, never to the
     * enclosing frame, so they need no propagation at all.</p>
     */
    private static void noteNestedToken(ParenFrame frame) {
        frame.sawArgToken = true;
        frame.argTokens = 2;
    }

    /**
     * Every {@link SqlToken.Kind#IDENT} token NOT immediately followed by
     * {@code '('} — a name used bare, which for a table-valued function is
     * the argument-less spelling.
     *
     * <p>{@link #functionCalls} only ever saw {@code identifier(}, so a TVF
     * written without an argument list was invisible to every rule that
     * reads the call list: {@code SELECT * FROM pragma_optimize} runs
     * ANALYZE, and {@code SELECT * FROM pragma_table_list} needs 3.37, and
     * neither was seen. Both spellings are legal SQLite
     * ({@code pragma_optimize(0xfffe)} too), so both have to be scanned.
     * Callers filter by name — this returns every bare identifier,
     * including ordinary tables and columns.</p>
     */
    public static List<String> bareIdentifiers(List<SqlToken> tokens) {
        List<String> names = new ArrayList<>();
        for (int i = 0; i < tokens.size(); i++) {
            if (tokens.get(i).kind() == SqlToken.Kind.IDENT
                    && (i + 1 >= tokens.size() || !tokens.get(i + 1).isPunct("("))) {
                names.add(tokens.get(i).text());
            }
        }
        return names;
    }

    /**
     * A BARE keyword: an {@link SqlToken.Kind#IDENT} written without any
     * quoting form.
     *
     * <p>Every syntax detector below tests keywords through this and never
     * through {@link SqlToken#isIdent}, and that distinction is the whole
     * false-positive story. A SQLite keyword cannot be written delimited —
     * {@code "full"} is a column or table name (or, under the double-quote
     * fallback, a string), never the FULL of {@code FULL JOIN} — so
     * {@code SELECT * FROM t "full" JOIN u ON 1} is ordinary 3.19.3 SQL that
     * a text-level match would reject. Since this lint is an ERROR that
     * blocks publication, a false positive costs what a miss costs. The
     * determinism lint draws the same line for {@code CURRENT_DATE}.</p>
     */
    private static boolean isBareKeyword(List<SqlToken> tokens, int index, String word) {
        if (index < 0 || index >= tokens.size()) {
            return false;
        }
        SqlToken token = tokens.get(index);
        return token.kind() == SqlToken.Kind.IDENT && !token.delimited()
                && token.text().equalsIgnoreCase(word);
    }

    private static boolean isPunctAt(List<SqlToken> tokens, int index, String symbol) {
        return index >= 0 && index < tokens.size() && tokens.get(index).isPunct(symbol);
    }

    /** Statement verbs that may carry a RETURNING clause. */
    private static final Set<String> RETURNING_VERBS =
            Set.of("insert", "replace", "update", "delete");

    /**
     * The statement's verb after an optional {@code WITH …} CTE prefix,
     * lowercased, or {@code null} when the statement does not start with an
     * identifier — the anchor the statement-scoped detectors (upsert,
     * returning, update-from) need. Same anchoring as {@link #writeTarget}:
     * scanning for the verb anywhere in the stream would read
     * {@code SELECT "update" FROM t} as an UPDATE.
     */
    private static String anchoredVerb(List<SqlToken> tokens) {
        int pos = skipCtePrefix(tokens);
        if (pos >= tokens.size() || tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
            return null;
        }
        return tokens.get(pos).text().toLowerCase(Locale.ROOT);
    }

    /**
     * Whether the statement writes {@code INSERT INTO <table> AS <alias>} —
     * SQLite 3.24.0 syntax, which arrived with UPSERT so the DO UPDATE clause
     * could name the target.
     *
     * <p>Anchored exactly like {@link #writeTarget}: verb, optional
     * {@code OR …} conflict clause, {@code INTO}, the qualified name, and only
     * then the {@code AS}. Matching {@code AS} anywhere after an INSERT would
     * catch every {@code INSERT INTO t SELECT x AS y FROM u}.</p>
     */
    private static boolean hasInsertAlias(List<SqlToken> tokens) {
        int pos = skipCtePrefix(tokens);
        if (pos >= tokens.size()
                || !(tokens.get(pos).isIdent("insert") || tokens.get(pos).isIdent("replace"))) {
            return false;
        }
        pos++;
        for (int guard = 0; guard < 2 && pos < tokens.size()
                && !tokens.get(pos).isIdent("into"); guard++) {
            if (tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
                return false;
            }
            pos++;
        }
        if (pos >= tokens.size() || !tokens.get(pos).isIdent("into")) {
            return false;
        }
        pos++;
        if (pos >= tokens.size() || !isName(tokens.get(pos))) {
            return false;
        }
        pos++;
        while (isPunctAt(tokens, pos, ".") && pos + 1 < tokens.size()
                && isName(tokens.get(pos + 1))) {
            pos += 2;
        }
        return isBareKeyword(tokens, pos, "as");
    }

    /**
     * Whether a bare {@code returning} at {@code index} is the RETURNING
     * clause rather than a column that happens to be spelled that way. SQLite
     * keeps RETURNING usable as an identifier, so position is all there is:
     *
     * <ul>
     *   <li>it is preceded by the END of the preceding clause — a {@code )},
     *       or any non-punctuation token (a name, literal or parameter).
     *       Anything else ({@code SET x = returning}, {@code (returning},
     *       {@code a || returning}) is an expression operand;</li>
     *   <li>it is followed by the START of a result list — {@code *},
     *       {@code (}, or a non-punctuation token, which excludes
     *       {@code WHERE returning > 0} and {@code SET returning = 1}.</li>
     * </ul>
     *
     * <p>The residual is documented in docs/validation.md rather than papered
     * over: a column literally named {@code returning}, used bare at depth 0
     * of a write statement in a spot that passes both tests
     * ({@code WHERE returning IS NULL}), is still reported. Spelling it
     * {@code "returning"} is the escape, the same one the determinism lint
     * gives {@code "current_date"}.</p>
     */
    private static boolean isReturningClause(List<SqlToken> tokens, int index) {
        if (index > 0) {
            SqlToken before = tokens.get(index - 1);
            if (before.kind() == SqlToken.Kind.PUNCT && !before.isPunct(")")) {
                return false;
            }
        }
        if (index + 1 < tokens.size()) {
            SqlToken after = tokens.get(index + 1);
            if (after.kind() == SqlToken.Kind.PUNCT
                    && !after.isPunct("*") && !after.isPunct("(")) {
                return false;
            }
        }
        return true;
    }

    /**
     * The {@code SYNTAX_MIN_VERSION} feature ids the statement uses, first
     * occurrence first, deduplicated — the input to the
     * sqlite-version-too-low-for-syntax lint (docs/validation.md).
     *
     * <p>Token patterns, not a grammar. The version half of the SQLite
     * contract cannot be enforced by preparing the statement
     * (docs/validation.md §3: the validator's engine is always far newer than
     * the floor), so the only thing left is to recognize the constructs
     * lexically. Each detector is written to be wrong in the SILENT direction
     * wherever the token stream is ambiguous, because the finding is an
     * error.</p>
     *
     * <p>Deliberately not detected, for one reason each:
     * {@code GENERATED ALWAYS AS} (3.31.0), {@code STRICT} /
     * {@code WITHOUT ROWID} (3.37.0) and {@code VACUUM INTO} (3.27.0) only
     * occur in statements forbidden-statement already refuses, so a detector
     * could only pile a second finding onto a rejected statement;
     * {@code TRUE} / {@code FALSE} (3.23.0) tokenize identically to a column
     * reference, which is exactly what a pre-3.23 engine parses them as.</p>
     *
     * <p>Mirrors the TypeScript {@code syntaxFeatures} token for token.</p>
     */
    public static List<String> syntaxFeatures(List<SqlToken> tokens) {
        List<String> features = new ArrayList<>();
        Set<String> seen = new HashSet<>();

        if (hasInsertAlias(tokens) && seen.add("insert-alias")) {
            features.add("insert-alias");
        }

        String verb = anchoredVerb(tokens);
        boolean isInsert = "insert".equals(verb) || "replace".equals(verb);
        boolean canReturn = verb != null && RETURNING_VERBS.contains(verb);
        int depth = 0;
        for (int i = 0; i < tokens.size(); i++) {
            SqlToken token = tokens.get(i);
            // `->` and `->>`. The two tokenizers split them differently —
            // this one folds `>>` into a single operator token, the
            // TypeScript one emits every unrecognized character on its own —
            // so the pattern both agree on is `-` followed by a punct token
            // that STARTS with `>`. Whitespace is gone by now, so `a - > b`
            // matches too; that is a syntax error on every engine, which
            // makes over-matching there the harmless direction.
            if (token.isPunct("-") && i + 1 < tokens.size()
                    && tokens.get(i + 1).kind() == SqlToken.Kind.PUNCT
                    && tokens.get(i + 1).text().startsWith(">")
                    && seen.add("json-arrow-operators")) {
                features.add("json-arrow-operators");
            }
            if (token.isPunct("(")) {
                depth++;
                continue;
            }
            if (token.isPunct(")")) {
                depth--;
                continue;
            }
            if (isBareKeyword(tokens, i, "over") && isPunctAt(tokens, i + 1, "(")
                    && seen.add("window-functions")) {
                features.add("window-functions");
            }
            if (isBareKeyword(tokens, i, "filter") && isPunctAt(tokens, i + 1, "(")
                    && isBareKeyword(tokens, i + 2, "where")
                    && seen.add("aggregate-filter")) {
                features.add("aggregate-filter");
            }
            if (isBareKeyword(tokens, i, "nulls")
                    && (isBareKeyword(tokens, i + 1, "first")
                            || isBareKeyword(tokens, i + 1, "last"))
                    && seen.add("nulls-first-last")) {
                features.add("nulls-first-last");
            }
            // `AS [NOT] MATERIALIZED (`. The trailing `(` is what separates
            // the CTE hint from a result column aliased `materialized`.
            if (isBareKeyword(tokens, i, "as")) {
                int skipNot = isBareKeyword(tokens, i + 1, "not") ? 1 : 0;
                if (isBareKeyword(tokens, i + 1 + skipNot, "materialized")
                        && isPunctAt(tokens, i + 2 + skipNot, "(")
                        && seen.add("materialized-cte")) {
                    features.add("materialized-cte");
                }
            }
            if ((isBareKeyword(tokens, i, "right") || isBareKeyword(tokens, i, "full"))
                    && (isBareKeyword(tokens, i + 1, "join")
                            || isBareKeyword(tokens, i + 1, "outer"))
                    && seen.add("right-full-join")) {
                features.add("right-full-join");
            }
            if (isBareKeyword(tokens, i, "is")) {
                int skipNot = isBareKeyword(tokens, i + 1, "not") ? 1 : 0;
                if (isBareKeyword(tokens, i + 1 + skipNot, "distinct")
                        && isBareKeyword(tokens, i + 2 + skipNot, "from")
                        && seen.add("is-distinct-from")) {
                    features.add("is-distinct-from");
                }
            }
            // UPSERT is an INSERT clause. The other `ON CONFLICT` in SQLite
            // is a column/table CONSTRAINT clause, which is pre-floor syntax
            // living in a CREATE TABLE the statement denylist already
            // refuses.
            if (isInsert && isBareKeyword(tokens, i, "on")
                    && isBareKeyword(tokens, i + 1, "conflict")
                    && seen.add("upsert")) {
                features.add("upsert");
            }
            if (canReturn && depth == 0 && isBareKeyword(tokens, i, "returning")
                    && isReturningClause(tokens, i) && seen.add("returning")) {
                features.add("returning");
            }
            // A top-level FROM in an UPDATE. Every pre-3.33 FROM inside an
            // UPDATE belongs to a subquery, and a subquery is parenthesized.
            if ("update".equals(verb) && depth == 0 && isBareKeyword(tokens, i, "from")
                    && seen.add("update-from")) {
                features.add("update-from");
            }
        }
        return features;
    }

    private static boolean isNowLiteral(SqlToken token) {
        return token.kind() == SqlToken.Kind.STRING && "now".equalsIgnoreCase(token.text());
    }

    /**
     * Extract {@code <callIdColumn> = <atom>} (and
     * {@code <atom> = <callIdColumn>}) comparisons where the atom is a
     * single string literal or named parameter. {@code callIdColumn} is
     * the manifest's call-id column name. Concatenations and other
     * computed expressions are not atoms — they are skipped by static
     * call-id resolution.
     */
    public static List<ValueExpr> callIdComparisons(List<SqlToken> tokens, String callIdColumn) {
        List<ValueExpr> comparisons = new ArrayList<>();
        for (int i = 0; i < tokens.size(); i++) {
            if (!tokens.get(i).isIdent(callIdColumn)) {
                continue;
            }
            // forward form: <callIdColumn> = <atom>
            if (i + 2 < tokens.size() && tokens.get(i + 1).isPunct("=")) {
                SqlToken value = tokens.get(i + 2);
                if (isAtom(value) && !continuesExpression(tokens, i + 3)) {
                    comparisons.add(atom(value));
                }
            }
            // reverse form: <atom> = <callIdColumn>
            if (i >= 2 && tokens.get(i - 1).isPunct("=")) {
                SqlToken value = tokens.get(i - 2);
                if (isAtom(value) && (i - 3 < 0 || !tokens.get(i - 3).isPunct("||"))) {
                    comparisons.add(atom(value));
                }
            }
        }
        return comparisons;
    }

    private static boolean isAtom(SqlToken token) {
        return token.kind() == SqlToken.Kind.STRING || token.kind() == SqlToken.Kind.PARAM;
    }

    private static boolean continuesExpression(List<SqlToken> tokens, int index) {
        return index < tokens.size()
                && (tokens.get(index).isPunct("||") || tokens.get(index).isPunct("."));
    }

    private static ValueExpr atom(SqlToken token) {
        return token.kind() == SqlToken.Kind.STRING
                ? new ValueExpr(ValueExpr.Kind.STRING, token.text())
                : new ValueExpr(ValueExpr.Kind.PARAM, token.text());
    }

    /**
     * The statement's first meaningful token, lowercased, when that token is
     * an identifier — the anchor of the forbidden-statement lint
     * (docs/validation.md). The tokenizer has already dropped whitespace and
     * both comment forms, so token 0 <em>is</em> the first meaningful token;
     * nothing extra is needed to be comment-aware. Returns {@code null} when
     * the statement is empty or starts with a non-identifier (a string
     * literal, punctuation), so a leading {@code 'PRAGMA'} literal is never
     * mistaken for the PRAGMA statement.
     */
    public static String leadingKeyword(List<SqlToken> tokens) {
        if (tokens.isEmpty() || tokens.get(0).kind() != SqlToken.Kind.IDENT) {
            return null;
        }
        return tokens.get(0).text().toLowerCase(Locale.ROOT);
    }

    /**
     * Whether the token stream holds more than one SQL statement: a top-level
     * (paren depth 0) {@code ';'} punctuation token followed by at least one
     * further token — the anchor of the multiple-statements lint
     * (docs/validation.md). A trailing {@code ';'} that merely terminates a
     * single statement (nothing follows it) is legal and not flagged. Comments
     * and string literals never trigger it: the tokenizer already collapsed
     * them, so a {@code ';'} inside {@code '…'} or a {@code --} line comment is
     * not a punctuation token here.
     *
     * <p>This matters because the protocol contract is one statement per
     * {@code sql} field: adapters disagree about the tail (the native adapter
     * rejects a trailing statement without stepping anything, ADO.NET adapters
     * execute it). Without this check a leading
     * no-op — {@code SELECT 1; PRAGMA writable_schema = ON} — anchors
     * {@link #leadingKeyword} / {@link #writeTarget} on the harmless
     * {@code SELECT}, bypassing the forbidden-statement and protocol-table-write
     * denylists entirely.</p>
     */
    public static boolean hasTrailingStatement(List<SqlToken> tokens) {
        int depth = 0;
        for (int i = 0; i < tokens.size(); i++) {
            SqlToken token = tokens.get(i);
            if (token.isPunct("(")) {
                depth++;
            } else if (token.isPunct(")")) {
                depth--;
            } else if (depth == 0 && token.isPunct(";")) {
                return i + 1 < tokens.size();
            }
        }
        return false;
    }

    /**
     * The single table an INSERT / UPDATE / DELETE writes, or {@code null}
     * when the statement is not a write — the anchor of the
     * protocol-table-write lint (docs/validation.md).
     *
     * <p>Unlike {@link #parseInsert}, the verb is anchored at the start of the
     * statement (after an optional {@code WITH …} CTE prefix) instead of being
     * matched anywhere in the token stream. That matters because this lint
     * raises an ERROR that blocks publication: a scan-anywhere match would
     * read {@code SELECT "delete" FROM result_x} as a DELETE against
     * {@code result_x} and reject the single most important legal pattern —
     * reading a result table. Skipping the CTE prefix rather than only
     * looking at token 0 is equally load-bearing in the other direction: a
     * bare {@code WITH d AS (SELECT 1) INSERT INTO result_x …} would
     * otherwise slip past the lint entirely.</p>
     *
     * <p>Schema-qualified names keep their last component, mirroring
     * {@link #parseInsert}.</p>
     */
    public static String writeTarget(List<SqlToken> tokens) {
        int pos = skipCtePrefix(tokens);
        if (pos >= tokens.size() || tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
            return null;
        }
        if (tokens.get(pos).isIdent("insert") || tokens.get(pos).isIdent("replace")) {
            pos++;
            // INSERT OR REPLACE / OR IGNORE / … — at most two idents before INTO.
            for (int guard = 0; guard < 2 && pos < tokens.size()
                    && !tokens.get(pos).isIdent("into"); guard++) {
                if (tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
                    return null;
                }
                pos++;
            }
            if (pos >= tokens.size() || !tokens.get(pos).isIdent("into")) {
                return null;
            }
            return qualifiedName(tokens, pos + 1);
        }
        if (tokens.get(pos).isIdent("update")) {
            pos++;
            // UPDATE OR ROLLBACK / OR ABORT / … — one conflict-clause ident.
            if (pos + 1 < tokens.size() && tokens.get(pos).isIdent("or")) {
                pos += 2;
            }
            return qualifiedName(tokens, pos);
        }
        if (tokens.get(pos).isIdent("delete")) {
            pos++;
            if (pos >= tokens.size() || !tokens.get(pos).isIdent("from")) {
                return null;
            }
            return qualifiedName(tokens, pos + 1);
        }
        return null;
    }

    /**
     * Index of the statement verb after an optional {@code WITH [RECURSIVE]}
     * CTE prefix ({@code name [(cols)] AS [[NOT] MATERIALIZED] (body) [, …]}),
     * or 0 when the statement has no such prefix. Each parenthesized group is
     * skipped by a balanced scan, so a CTE body containing its own commas,
     * subqueries, or the word {@code begin} never confuses the walk.
     */
    private static int skipCtePrefix(List<SqlToken> tokens) {
        if (tokens.isEmpty() || !tokens.get(0).isIdent("with")) {
            return 0;
        }
        int pos = 1;
        if (pos < tokens.size() && tokens.get(pos).isIdent("recursive")) {
            pos++;
        }
        while (pos < tokens.size()) {
            if (tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
                return tokens.size(); // unrecognized shape — no write target
            }
            pos++; // CTE name
            if (pos < tokens.size() && tokens.get(pos).isPunct("(")) {
                pos = skipBalanced(tokens, pos); // optional column list
            }
            if (pos < tokens.size() && tokens.get(pos).isIdent("as")) {
                pos++;
            }
            if (pos < tokens.size() && tokens.get(pos).isIdent("not")) {
                pos++;
            }
            if (pos < tokens.size() && tokens.get(pos).isIdent("materialized")) {
                pos++;
            }
            if (pos >= tokens.size() || !tokens.get(pos).isPunct("(")) {
                return tokens.size(); // unrecognized shape — no write target
            }
            pos = skipBalanced(tokens, pos); // CTE body
            if (pos < tokens.size() && tokens.get(pos).isPunct(",")) {
                pos++;
                continue; // another CTE
            }
            return pos;
        }
        return pos;
    }

    /** Index just past the ')' matching the '(' at {@code open}. */
    private static int skipBalanced(List<SqlToken> tokens, int open) {
        int depth = 0;
        for (int pos = open; pos < tokens.size(); pos++) {
            if (tokens.get(pos).isPunct("(")) {
                depth++;
            } else if (tokens.get(pos).isPunct(")")) {
                depth--;
                if (depth == 0) {
                    return pos + 1;
                }
            }
        }
        return tokens.size();
    }

    /**
     * Read {@code [schema.]table} at {@code start}, keeping the last
     * component. Both components accept every name token, single-quoted
     * included — SQLite resolves {@code main.'pending_host_calls'} as a name,
     * and stopping at {@code main} reported the harmless schema as the write
     * target.
     */
    private static String qualifiedName(List<SqlToken> tokens, int start) {
        int pos = start;
        if (pos >= tokens.size() || !isName(tokens.get(pos))) {
            return null;
        }
        String name = tokens.get(pos).text();
        pos++;
        while (pos + 1 < tokens.size()
                && tokens.get(pos).isPunct(".")
                && isName(tokens.get(pos + 1))) {
            name = tokens.get(pos + 1).text();
            pos += 2;
        }
        return name;
    }

    private static int indexOfIdent(List<SqlToken> tokens, String name) {
        for (int i = 0; i < tokens.size(); i++) {
            if (tokens.get(i).isIdent(name)) {
                return i;
            }
        }
        return -1;
    }
}
