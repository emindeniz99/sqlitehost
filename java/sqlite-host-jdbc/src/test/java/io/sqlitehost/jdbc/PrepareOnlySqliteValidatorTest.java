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
    void anEarlierStatementCannotDisarmTheVerdictOnALaterOne() throws Exception {
        // The audit's weaponised payload, minus its now-forbidden EXPLAIN
        // statements (forbidden-statement catches those in layer 4). What
        // is left is the part only this layer can answer: preparing the
        // whole step on ONE connection let statement 0 turn writable_schema
        // on inside the validator's own engine — SQLite applies flag
        // pragmas in the code generator, so `EXPLAIN PRAGMA
        // writable_schema = ON` sets the flag while executing nothing — and
        // the sqlite_master rewrite that follows then compiled clean. With
        // a connection per statement the UPDATE is rejected on its own
        // merits whatever precedes it.
        String masterRewrite = "UPDATE sqlite_master SET sql = 'CREATE TRIGGER"
                + " trg_call_get_value_queue AFTER INSERT ON call_get_value BEGIN SELECT 1;"
                + " END' WHERE name = 'trg_call_get_value_queue'";

        List<ValidationFinding> alone = prepare(script(masterRewrite));
        assertEquals(1, alone.size(), alone.toString());
        assertEquals(ValidationCodes.SQL_PREPARE_ERROR, alone.get(0).code());
        assertTrue(alone.get(0).message().contains("sqlite_master"), alone.get(0).message());

        String json = "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                + "\"steps\":[{\"id\":\"s\",\"statements\":["
                + "{\"sql\":\"EXPLAIN PRAGMA writable_schema = ON\",\"bindings\":{}},"
                + "{\"sql\":\"" + masterRewrite + "\",\"bindings\":{}}"
                + "]}]}";
        List<ValidationFinding> chained = prepare(json);
        assertEquals(1, chained.size(), chained.toString());
        assertEquals(ValidationCodes.SQL_PREPARE_ERROR, chained.get(0).code());
        assertEquals(1, chained.get(0).statementIndex(), "the UPDATE, not the EXPLAIN");
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
