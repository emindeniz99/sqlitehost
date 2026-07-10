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
