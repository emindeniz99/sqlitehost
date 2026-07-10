package io.sqlitehost.validator;

import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Engine unit tests for the pinned codes that have no committed
 * payload fixture (invalid-envelope, list-child-without-parent) plus
 * intent checks that the full fixture matrix (run in sqlite-host-jdbc)
 * cannot express, e.g. that colocated children are accepted.
 */
class ValidationEngineTest {

    private static Manifest manifest;

    @BeforeAll
    static void loadManifest() throws IOException {
        Path dir = Paths.get("").toAbsolutePath();
        while (dir != null && !Files.isRegularFile(
                dir.resolve("fixtures/manifests/sample-host.manifest.json"))) {
            dir = dir.getParent();
        }
        if (dir == null) {
            throw new IllegalStateException("fixtures directory not found");
        }
        manifest = ManifestJsonReader.read(Files.readString(
                dir.resolve("fixtures/manifests/sample-host.manifest.json")));
    }

    private static ValidationReport validate(String scriptJson) throws IOException {
        Script script = ScriptJsonReader.read(scriptJson);
        return new ValidationEngine().validate(manifest, script);
    }

    private static List<String> errorCodes(ValidationReport report) {
        return report.errors().stream().map(ValidationFinding::code).toList();
    }

    @Test
    void missingEngineAndStepsAreInvalidEnvelope() throws IOException {
        ValidationReport report = validate("{\"requiredApiLevel\":1}");
        assertFalse(report.isValid());
        List<String> codes = errorCodes(report);
        assertTrue(codes.contains(ValidationCodes.INVALID_ENVELOPE), codes.toString());
    }

    @Test
    void missingRequiredApiLevelIsInvalidEnvelope() throws IOException {
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"steps\":[{\"id\":\"s\",\"statements\":[{\"sql\":\"SELECT 1\","
                + "\"bindings\":{}}]}]}");
        assertTrue(errorCodes(report).contains(ValidationCodes.INVALID_ENVELOPE));
    }

    @Test
    void emptyStepIdAndEmptySqlAreInvalidEnvelope() throws IOException {
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,"
                + "\"steps\":[{\"id\":\"\",\"statements\":[{\"sql\":\"\",\"bindings\":{}}]}]}");
        long invalidEnvelopeCount = errorCodes(report).stream()
                .filter(ValidationCodes.INVALID_ENVELOPE::equals).count();
        assertEquals(2, invalidEnvelopeCount,
                "empty step id and empty sql are separate findings");
    }

    @Test
    void emptyStatementsListIsInvalidEnvelope() throws IOException {
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,"
                + "\"steps\":[{\"id\":\"does-nothing\",\"statements\":[]}]}");
        assertFalse(report.isValid());
        ValidationFinding finding = report.errors().get(0);
        assertEquals(ValidationCodes.INVALID_ENVELOPE, finding.code());
        assertEquals("does-nothing", finding.stepId());
        assertEquals(-1, finding.statementIndex());
    }

    @Test
    void childRowsWithoutAnyParentInsertAreAnError() throws IOException {
        // Children for call_id 'orphan-1' but no parent call row anywhere.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValues\"],"
                + "\"steps\":[{\"id\":\"children-only\",\"statements\":[{"
                + "\"sql\":\"INSERT INTO call_get_values__input_keys (call_id, item_index, input_key)"
                + " VALUES (:c, 0, 'alpha')\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"orphan-1\"}}}]}]}");
        assertTrue(errorCodes(report).contains(ValidationCodes.LIST_CHILD_WITHOUT_PARENT),
                errorCodes(report).toString());
    }

    @Test
    void colocatedParentAndChildrenAreAccepted() throws IOException {
        // Same step: parent + children — the intent behind
        // list-child-later-step is colocation, not ordering paranoia.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValues\"],"
                + "\"steps\":[{\"id\":\"together\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_values (call_id, input_default_value)"
                + " VALUES (:c, NULL)\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"list-1\"}}},"
                + "{\"sql\":\"INSERT INTO call_get_values__input_keys (call_id, item_index, input_key)"
                + " VALUES (:c, 0, 'alpha')\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"list-1\"}}}]}]}");
        assertTrue(report.isValid(), report.findings().toString());
    }

    @Test
    void intentionallyEmptyListIsFine() throws IOException {
        // A parent insert with no child rows must not warn or error —
        // the plan explicitly allows intentionally empty lists.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValues\"],"
                + "\"steps\":[{\"id\":\"empty-list\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_values (call_id, input_default_value)"
                + " VALUES (:c, NULL)\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"list-1\"}}}]}]}");
        assertTrue(report.isValid(), report.findings().toString());
    }

    @Test
    void optionalColumnAcceptsNullBindingButRequiredDoesNot() throws IOException {
        // input_note is optional (accepts null); input_key is required.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"putBlob\"],"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_put_blob (call_id, input_key, input_data, input_note)"
                + " VALUES (:c, :key, :data, :note)\","
                + "\"bindings\":{"
                + "\"c\":{\"type\":\"text\",\"value\":\"b-1\"},"
                + "\"key\":{\"type\":\"null\"},"
                + "\"data\":{\"type\":\"blob\",\"value\":\"3q2+7w==\"},"
                + "\"note\":{\"type\":\"null\"}}}]}]}");
        List<String> codes = errorCodes(report);
        assertEquals(List.of(ValidationCodes.BINDING_TYPE_MISMATCH), codes,
                "only the required column fed with null mismatches: " + codes);
    }

    @Test
    void int32BindingIsAcceptedForInt64Column() throws IOException {
        // Compatibility table: int64 column accepts int32 or int64.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"setValue\"],"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_set_value (call_id, input_key, input_value)"
                + " VALUES (:c, 'k', :v)\","
                + "\"bindings\":{"
                + "\"c\":{\"type\":\"text\",\"value\":\"w-1\"},"
                + "\"v\":{\"type\":\"int32\",\"value\":7}}}]}]}");
        assertTrue(report.isValid(), report.findings().toString());
    }

    @Test
    void float64ColumnAcceptsFloat64AndFloat32Bindings() throws IOException {
        // Compatibility table: float64 column ← float64 | float32.
        for (String scoreBinding : List.of(
                "{\"type\":\"float64\",\"value\":98.5}",
                "{\"type\":\"float32\",\"value\":0.75}")) {
            ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                    + "\"requiredApiLevel\":1,\"requiredMethods\":[\"recordScore\"],"
                    + "\"steps\":[{\"id\":\"s\",\"statements\":["
                    + "{\"sql\":\"INSERT INTO call_record_score (call_id, input_key, input_score)"
                    + " VALUES (:c, 'k', :score)\","
                    + "\"bindings\":{"
                    + "\"c\":{\"type\":\"text\",\"value\":\"r-1\"},"
                    + "\"score\":" + scoreBinding + "}}]}]}");
            assertTrue(report.isValid(), report.findings().toString());
        }
    }

    @Test
    void float32ColumnRejectsFloat64Binding() throws IOException {
        // input_weight is float32 — a float64 binding does not narrow.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"recordScore\"],"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_record_score (call_id, input_key, input_score, input_weight)"
                + " VALUES (:c, 'k', :score, :weight)\","
                + "\"bindings\":{"
                + "\"c\":{\"type\":\"text\",\"value\":\"r-1\"},"
                + "\"score\":{\"type\":\"float64\",\"value\":98.5},"
                + "\"weight\":{\"type\":\"float64\",\"value\":0.75}}}]}]}");
        assertEquals(List.of(ValidationCodes.BINDING_TYPE_MISMATCH), errorCodes(report),
                "only the float32 column fed with float64 mismatches");
    }

    @Test
    void integerAndFloatBindingsDoNotCoerce() throws IOException {
        // int64 column ← float64 is a mismatch…
        ValidationReport intColumn = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"setValue\"],"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_set_value (call_id, input_key, input_value)"
                + " VALUES (:c, 'k', :v)\","
                + "\"bindings\":{"
                + "\"c\":{\"type\":\"text\",\"value\":\"w-1\"},"
                + "\"v\":{\"type\":\"float64\",\"value\":42.5}}}]}]}");
        assertTrue(errorCodes(intColumn).contains(ValidationCodes.BINDING_TYPE_MISMATCH),
                errorCodes(intColumn).toString());

        // …and so is float64 column ← int64, even for an integral value.
        ValidationReport floatColumn = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"recordScore\"],"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_record_score (call_id, input_key, input_score)"
                + " VALUES (:c, 'k', :score)\","
                + "\"bindings\":{"
                + "\"c\":{\"type\":\"text\",\"value\":\"r-1\"},"
                + "\"score\":{\"type\":\"int64\",\"value\":98}}}]}]}");
        assertTrue(errorCodes(floatColumn).contains(ValidationCodes.BINDING_TYPE_MISMATCH),
                errorCodes(floatColumn).toString());
    }

    @Test
    void optionalFloat32ColumnAcceptsNullBinding() throws IOException {
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"recordScore\"],"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_record_score (call_id, input_key, input_score, input_weight)"
                + " VALUES (:c, 'k', :score, :weight)\","
                + "\"bindings\":{"
                + "\"c\":{\"type\":\"text\",\"value\":\"r-1\"},"
                + "\"score\":{\"type\":\"float64\",\"value\":98.5},"
                + "\"weight\":{\"type\":\"null\"}}}]}]}");
        assertTrue(report.isValid(), report.findings().toString());
    }

    @Test
    void callIdColumnRequiresTextBinding() throws IOException {
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\"],"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key) VALUES (:c, 'k')\","
                + "\"bindings\":{\"c\":{\"type\":\"int64\",\"value\":1}}}]}]}");
        assertTrue(errorCodes(report).contains(ValidationCodes.BINDING_TYPE_MISMATCH));
    }

    @Test
    void reportValidityIsDrivenByErrorsNotWarnings() throws IOException {
        // Declared-but-unused method: warning only, still publishable.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\",\"setValue\"],"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key) VALUES (:c, 'k')\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"r-1\"}}}]}]}");
        assertTrue(report.isValid());
        assertEquals(1, report.warnings().size());
        assertEquals(ValidationCodes.UNUSED_REQUIRED_METHOD, report.warnings().get(0).code());
    }

    @Test
    void computedCallIdsAreSkippedByLineageChecks() throws IOException {
        // 'w-' || result_key is computed — duplicate/lineage checks skip it.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValues\",\"setValue\"],"
                + "\"steps\":["
                + "{\"id\":\"a\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_values (call_id, input_default_value)"
                + " VALUES (:c, NULL)\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"list-1\"}}}]},"
                + "{\"id\":\"b\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_set_value (call_id, input_key, input_value)"
                + " SELECT 'w-' || result_key, result_key, 7"
                + " FROM result_get_values__result_entries WHERE call_id = :l\","
                + "\"bindings\":{\"l\":{\"type\":\"text\",\"value\":\"list-1\"}}}]}]}");
        assertTrue(report.isValid(), report.findings().toString());
    }

    @Test
    void insertOrReplaceIntoACallTableIsStillACallTableInsert() throws IOException {
        // The OR REPLACE conflict clause must not hide the call-table
        // lints: implicit column list and undeclared method use.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT OR REPLACE INTO call_get_value VALUES (:c, 'k')\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"r-1\"}}}]}]}");
        List<String> codes = errorCodes(report);
        assertTrue(codes.contains(ValidationCodes.IMPLICIT_COLUMN_LIST), codes.toString());
        assertTrue(codes.contains(ValidationCodes.UNDECLARED_METHOD_USE), codes.toString());
    }

    @Test
    void scriptVarsInsertsAreNotCallTableInserts() throws IOException {
        // script_vars is script scratch space (docs/workspace-schema.md):
        // declare with INSERT, reassign with INSERT OR REPLACE — neither
        // is a call-table insert, so no column-list/undeclared-method or
        // usage findings may fire.
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredFeatures\":[\"scriptVars\"],"
                + "\"steps\":[{\"id\":\"declare\",\"statements\":["
                + "{\"sql\":\"INSERT INTO script_vars (name, value_type, int_value)"
                + " VALUES ('aa', 'int64', 55), ('kh', 'int64', 4)\",\"bindings\":{}},"
                + "{\"sql\":\"INSERT OR REPLACE INTO script_vars (name, value_type, int_value)"
                + " SELECT 'total', 'int64',"
                + " (SELECT int_value FROM script_vars WHERE name = 'aa')\","
                + "\"bindings\":{}}]}]}");
        assertTrue(report.isValid(), report.findings().toString());
        assertEquals(List.of(), report.warnings(), report.warnings().toString());
    }

    /** Emits getValue call 'g1' and setValue call 's1' in one step, then joins. */
    private static String joinScript(String joinStepJson) {
        return "{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\",\"setValue\"],"
                + "\"steps\":[{\"id\":\"calls\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key)"
                + " VALUES ('g1', 'k')\",\"bindings\":{}},"
                + "{\"sql\":\"INSERT INTO call_set_value (call_id, input_key, input_value)"
                + " VALUES ('s1', 'k', 7)\",\"bindings\":{}}"
                + joinStepJson + "]}";
    }

    @Test
    void joinAcrossResultTablesOfTwoMethodsIsAccepted() throws IOException {
        // Each resolved call_id belongs to one method of the joined set —
        // cross-checking g1 against setValue (and s1 against getValue)
        // must not false-positive result-read-unknown-call.
        ValidationReport report = validate(joinScript("]},"
                + "{\"id\":\"join\",\"statements\":["
                + "{\"sql\":\"SELECT a.result_value, b.result_success"
                + " FROM result_get_value a, result_set_value b"
                + " WHERE a.call_id = 'g1' AND b.call_id = 's1'\",\"bindings\":{}}]}"));
        assertTrue(report.isValid(), report.findings().toString());
    }

    @Test
    void joinReadOfUnknownCallIdIsStillFlagged() throws IOException {
        // 'zz' is emitted by neither joined method — still an error.
        ValidationReport report = validate(joinScript("]},"
                + "{\"id\":\"join\",\"statements\":["
                + "{\"sql\":\"SELECT a.result_value, b.result_success"
                + " FROM result_get_value a, result_set_value b"
                + " WHERE a.call_id = 'zz' AND b.call_id = 's1'\",\"bindings\":{}}]}"));
        assertTrue(errorCodes(report).contains(ValidationCodes.RESULT_READ_UNKNOWN_CALL),
                errorCodes(report).toString());
    }

    @Test
    void joinReadInSameStepAsEmitIsStillFlagged() throws IOException {
        // Join statement colocated with the emitting inserts — results
        // only exist after the emitting step's drain.
        ValidationReport report = validate(joinScript(","
                + "{\"sql\":\"SELECT a.result_value, b.result_success"
                + " FROM result_get_value a, result_set_value b"
                + " WHERE a.call_id = 'g1' AND b.call_id = 's1'\",\"bindings\":{}}]}"));
        List<String> codes = errorCodes(report);
        assertTrue(codes.contains(ValidationCodes.RESULT_READ_NOT_AFTER_CALL), codes.toString());
        assertFalse(codes.contains(ValidationCodes.RESULT_READ_UNKNOWN_CALL), codes.toString());
    }

    @Test
    void duplicateInputNamesAreAnError() throws IOException {
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\"],"
                + "\"inputs\":["
                + "{\"name\":\"targetValue\",\"value\":{\"type\":\"int64\",\"value\":1}},"
                + "{\"name\":\"targetValue\",\"value\":{\"type\":\"int64\",\"value\":2}}],"
                + "\"steps\":[{\"id\":\"read\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key) VALUES (:c, 'k')\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"r-1\"}}}]}]}");
        assertFalse(report.isValid());
        ValidationFinding finding = report.errors().get(0);
        assertEquals(ValidationCodes.DUPLICATE_INPUT_NAME, finding.code());
        assertEquals(null, finding.stepId());
        assertEquals(-1, finding.statementIndex());
        assertTrue(finding.message().contains("targetValue"), finding.message());
    }

    @Test
    void uniqueInputNamesAreAccepted() throws IOException {
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\"],"
                + "\"inputs\":["
                + "{\"name\":\"targetValue\",\"value\":{\"type\":\"int64\",\"value\":1}},"
                + "{\"name\":\"otherValue\",\"value\":{\"type\":\"int64\",\"value\":2}}],"
                + "\"steps\":[{\"id\":\"read\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key) VALUES (:c, 'k')\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"r-1\"}}}]}]}");
        assertTrue(report.isValid(), report.findings().toString());
    }

    /** getValue insert whose call_id cell is the given SQL expression. */
    private static String prefixScript(String callIdExpr) {
        return "{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\"],"
                + "\"steps\":[{\"id\":\"read\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key)"
                + " SELECT :c, 'k' WHERE " + callIdExpr + "\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"r-1\"}}}]}]}";
    }

    @Test
    void mixedPrefixFormsWarnOncePerStatement() throws IOException {
        // :c and $c in one statement — supported (one binding feeds
        // both) but flagged once as a likely authoring accident.
        ValidationReport report = validate(prefixScript("$c = :c"));
        assertTrue(report.isValid(), report.findings().toString());
        assertEquals(1, report.warnings().size(), report.warnings().toString());
        ValidationFinding warning = report.warnings().get(0);
        assertEquals(ValidationCodes.MIXED_PREFIX_BINDING, warning.code());
        assertEquals("read", warning.stepId());
        assertEquals(0, warning.statementIndex());
    }

    @Test
    void repeatedSamePrefixDoesNotWarn() throws IOException {
        ValidationReport report = validate(prefixScript(":c = :c"));
        assertTrue(report.isValid(), report.findings().toString());
        assertEquals(0, report.warnings().size(), report.warnings().toString());
    }

    @Test
    void atAndDollarPrefixesAlsoWarn() throws IOException {
        ValidationReport report = validate(prefixScript("@c = $c"));
        assertTrue(report.isValid(), report.findings().toString());
        assertEquals(List.of(ValidationCodes.MIXED_PREFIX_BINDING),
                report.warnings().stream().map(ValidationFinding::code).toList());
    }

    @Test
    void prefixRetentionDoesNotDisturbBindingChecks() throws IOException {
        // Regression: mixed prefix forms still match bindings by bare
        // name (no missing/unused-binding), and a genuinely missing or
        // unused binding is still reported alongside the warning.
        ValidationReport mixed = validate(prefixScript("$c = :c"));
        List<String> mixedCodes = errorCodes(mixed);
        assertFalse(mixedCodes.contains(ValidationCodes.MISSING_BINDING), mixedCodes.toString());
        assertFalse(mixedCodes.contains(ValidationCodes.UNUSED_BINDING), mixedCodes.toString());

        ValidationReport broken = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\"],"
                + "\"steps\":[{\"id\":\"read\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key)"
                + " SELECT :c, 'k' WHERE $c = :absent\","
                + "\"bindings\":{"
                + "\"c\":{\"type\":\"text\",\"value\":\"r-1\"},"
                + "\"orphan\":{\"type\":\"text\",\"value\":\"x\"}}}]}]}");
        List<String> brokenCodes = errorCodes(broken);
        assertTrue(brokenCodes.contains(ValidationCodes.MISSING_BINDING), brokenCodes.toString());
        assertTrue(brokenCodes.contains(ValidationCodes.UNUSED_BINDING), brokenCodes.toString());
    }

    @Test
    void findingsCarryStatementContext() throws IOException {
        ValidationReport report = validate("{\"engine\":\"sqlite-host-v1\","
                + "\"requiredApiLevel\":1,\"requiredMethods\":[\"getValue\"],"
                + "\"steps\":[{\"id\":\"read\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key) VALUES (:c, :missing)\","
                + "\"bindings\":{\"c\":{\"type\":\"text\",\"value\":\"r-1\"}}}]}]}");
        ValidationFinding finding = report.errors().get(0);
        assertEquals(ValidationCodes.MISSING_BINDING, finding.code());
        assertEquals("read", finding.stepId());
        assertEquals(0, finding.statementIndex());
    }
}
