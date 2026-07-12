package io.sqlitehost.jdbc;

import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.validator.Severity;
import io.sqlitehost.validator.ValidationCodes;
import io.sqlitehost.validator.ValidationFinding;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.nio.file.Files;
import java.sql.SQLException;
import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Prepare-only validation catches what only SQLite can know — bad
 * grammar, missing tables, missing columns — without executing
 * anything (no stepping, so no rows are ever written).
 */
class PrepareOnlySqliteValidatorTest {

    private static Manifest manifest;

    @BeforeAll
    static void loadManifest() throws IOException {
        manifest = ManifestJsonReader.read(Files.readString(
                Fixtures.fixturesDir().resolve("manifests/sample-host.manifest.json")));
    }

    private static List<ValidationFinding> prepare(String scriptJson)
            throws IOException, SQLException {
        Script script = ScriptJsonReader.read(scriptJson);
        return new PrepareOnlySqliteValidator().validate(manifest, script);
    }

    private static String script(String sql) {
        return "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                + "\"steps\":[{\"id\":\"s\",\"statements\":[{\"sql\":\"" + sql
                + "\",\"bindings\":{}}]}]}";
    }

    @Test
    void validStatementsPrepareCleanly() throws Exception {
        assertEquals(List.of(), prepare(script(
                "INSERT INTO call_get_value (call_id, input_key) VALUES (:c, 'k')")));
    }

    @Test
    void unknownColumnFailsToPrepare() throws Exception {
        List<ValidationFinding> findings = prepare(script(
                "INSERT INTO call_get_value (call_id, input_wrong) VALUES (:c, 'k')"));
        assertEquals(1, findings.size());
        ValidationFinding finding = findings.get(0);
        assertEquals(ValidationCodes.SQL_PREPARE_ERROR, finding.code());
        assertEquals(Severity.ERROR, finding.severity());
        assertEquals("s", finding.stepId());
        assertEquals(0, finding.statementIndex());
        assertTrue(finding.message().contains("input_wrong"), finding.message());
    }

    @Test
    void unknownTableFailsToPrepare() throws Exception {
        List<ValidationFinding> findings = prepare(script("SELECT * FROM no_such_table"));
        assertEquals(1, findings.size());
        assertEquals(ValidationCodes.SQL_PREPARE_ERROR, findings.get(0).code());
    }

    @Test
    void grammarErrorFailsToPrepare() throws Exception {
        List<ValidationFinding> findings = prepare(script("SELEKT 1"));
        assertEquals(1, findings.size());
        assertEquals(ValidationCodes.SQL_PREPARE_ERROR, findings.get(0).code());
    }

    @Test
    void inlineFunctionCallsPrepareAgainstRegisteredStubs() throws Exception {
        // fn_get_value is registered as a NULL-returning stub for every
        // arity in minArgs..maxArgs before preparing, so the function
        // form compiles (docs/proposals/inline-host-functions.md).
        assertEquals(List.of(), prepare(script(
                "INSERT INTO call_set_value (call_id, input_key, input_value)"
                        + " SELECT :c, 'k', fn_get_value('k') * 2"
                        + " WHERE fn_get_value('k') <> 42")));
    }

    @Test
    void unknownFunctionStillFailsToPrepare() throws Exception {
        List<ValidationFinding> findings = prepare(script("SELECT fn_get_price('k')"));
        assertEquals(1, findings.size());
        assertEquals(ValidationCodes.SQL_PREPARE_ERROR, findings.get(0).code());
        assertTrue(findings.get(0).message().contains("fn_get_price"),
                findings.get(0).message());
    }

    @Test
    void wrongArityInlineCallFailsToPrepare() throws Exception {
        // Only the declared arities are registered — a two-argument
        // fn_get_value does not exist.
        List<ValidationFinding> findings = prepare(script(
                "SELECT fn_get_value('k', 'extra')"));
        assertEquals(1, findings.size());
        assertEquals(ValidationCodes.SQL_PREPARE_ERROR, findings.get(0).code());
    }

    @Test
    void prepareDoesNotExecuteTheStatement() throws Exception {
        // A duplicate-PK pair prepares fine twice: nothing is stepped,
        // so the UNIQUE violation that execution would hit never fires.
        String json = "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key) VALUES ('x', 'k')\",\"bindings\":{}},"
                + "{\"sql\":\"INSERT INTO call_get_value (call_id, input_key) VALUES ('x', 'k')\",\"bindings\":{}}"
                + "]}]}";
        assertEquals(List.of(), prepare(json));
    }
}
