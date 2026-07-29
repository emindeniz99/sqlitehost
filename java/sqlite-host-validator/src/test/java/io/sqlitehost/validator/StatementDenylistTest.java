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
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Statement denylist (docs/validation.md — forbidden-statement and
 * protocol-table-write).
 *
 * <p>Why forbidden-statement matters: the runtime's atomicity unit is the
 * step (its statements plus the drain), not a transaction. A script that
 * issues BEGIN and later ROLLBACK erases the drain's result rows and queue
 * updates <em>after</em> the host handlers have already run with real-world
 * side effects, and the run still reports success — a silent-data-loss shape.
 * ATTACH is the only filesystem escape a script has: on a file-backed
 * workspace it grants read and write access to any reachable database.
 * PRAGMA can change semantics under the runtime's feet or, via
 * {@code writable_schema=ON}, rewrite the queue triggers outright. None of
 * these is caught by prepare-only validation, because all of them compile
 * perfectly.</p>
 *
 * <p>Why protocol-table-write matters: the drain and the result-write policy
 * both assume they are the only writers of the queue and result tables. A
 * script that inserts into a result table forges a result the host never
 * produced, and one that deletes from the queue makes calls silently vanish
 * while the run still reports Completed.</p>
 *
 * <p>The negative cases below are the load-bearing half: both codes are
 * ERRORs that block publication, so a false positive is as damaging as a
 * miss. The mirror of these cases lives in the TypeScript lint tests.</p>
 */
class StatementDenylistTest {

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

    /** Findings of one code for a single-statement script (SQL must be JSON-safe). */
    private static List<ValidationFinding> findings(String sql, String code)
            throws IOException {
        Script script = ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                        + "\"requiredFeatures\":[],\"requiredMethods\":[],"
                        + "\"steps\":[{\"id\":\"s\",\"statements\":[{\"sql\":\"" + sql
                        + "\",\"bindings\":{}}]}]}");
        return new ValidationEngine().validate(manifest, script).findings().stream()
                .filter(f -> code.equals(f.code()))
                .toList();
    }

    private static List<ValidationFinding> forbidden(String sql) throws IOException {
        return findings(sql, ValidationCodes.FORBIDDEN_STATEMENT);
    }

    private static List<ValidationFinding> protocolWrite(String sql) throws IOException {
        return findings(sql, ValidationCodes.PROTOCOL_TABLE_WRITE);
    }

    private static List<ValidationFinding> multipleStatements(String sql) throws IOException {
        return findings(sql, ValidationCodes.MULTIPLE_STATEMENTS);
    }

    /** Every finding of a single-statement script, for publishability checks. */
    private static List<ValidationFinding> allFindings(String sql) throws IOException {
        Script script = ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                        + "\"requiredFeatures\":[],\"requiredMethods\":[],"
                        + "\"steps\":[{\"id\":\"s\",\"statements\":[{\"sql\":\"" + sql
                        + "\",\"bindings\":{}}]}]}");
        return new ValidationEngine().validate(manifest, script).findings();
    }

    @Test
    void everyDeniedLeadingKeywordIsAnError() throws IOException {
        for (String sql : List.of("BEGIN", "BEGIN IMMEDIATE", "COMMIT", "END",
                "ROLLBACK", "SAVEPOINT sp1", "RELEASE sp1",
                "ATTACH DATABASE '/tmp/x.db' AS x", "DETACH DATABASE x",
                "PRAGMA foreign_keys = ON", "VACUUM", "ANALYZE", "REINDEX")) {
            List<ValidationFinding> found = forbidden(sql);
            assertEquals(1, found.size(), sql + ": " + found);
            assertEquals(Severity.ERROR, found.get(0).severity(), sql);
        }
    }

    @Test
    void theKeywordMatchIsCaseInsensitive() throws IOException {
        // SQLite keywords are case-insensitive, so a denylist that only saw
        // upper case would be bypassed by typing `pragma` in lower case.
        for (String sql : List.of("pragma foreign_keys = ON", "Attach DATABASE 'x' AS y",
                "beGIN")) {
            assertEquals(1, forbidden(sql).size(), sql);
        }
    }

    @Test
    void onlyTheFirstTokenIsAKeyword() throws IOException {
        // These are the false positives that would make the lint unusable.
        // A table whose name merely starts with a denied word, the pragma_*
        // table-valued functions inside a SELECT (explicitly still legal), a
        // denied word appearing as a column, and a string literal spelling
        // one — none of them is a statement of that kind.
        for (String sql : List.of(
                "SELECT * FROM pragma_helper",
                "INSERT INTO pragma_helper (a) VALUES (1)",
                "SELECT name FROM pragma_table_info('script_vars')",
                "SELECT t.begin FROM t",
                "SELECT 'PRAGMA writable_schema = ON' AS label",
                "SELECT CASE WHEN a THEN 1 ELSE 2 END FROM t",
                "SELECT analyze_id FROM t")) {
            assertEquals(List.of(), forbidden(sql), sql);
        }
    }

    @Test
    void commentsAndCtesDoNotHideTheLeadingKeyword() throws IOException {
        // The tokenizer already drops comments, so a leading comment cannot
        // be used to push the real first token out of view.
        assertEquals(1, forbidden("-- harmless\\nPRAGMA writable_schema = ON").size());
        assertEquals(1, forbidden("/* harmless */ ATTACH DATABASE 'x' AS y").size());
        // A CTE prefix is legal SQL and must not be mistaken for a denied
        // statement, even when the CTE body mentions one as a value.
        assertEquals(List.of(), forbidden(
                "WITH q(v) AS (SELECT 'begin') SELECT v FROM q"));
        assertEquals(List.of(), forbidden(
                "WITH q(v) AS (SELECT 1) INSERT INTO script_vars (name, value_type, int_value)"
                        + " SELECT 'n', 'int64', v FROM q"));
    }

    @Test
    void writesToRuntimeOwnedTablesAreErrors() throws IOException {
        // Forging a result, marking a queued call done, and dropping a queued
        // call are the three concrete attacks on the drain protocol.
        for (String sql : List.of(
                "INSERT INTO result_get_value (call_id, status, result_value)"
                        + " VALUES ('x', 'done', 1)",
                "UPDATE pending_host_calls SET status = 'done'",
                "DELETE FROM pending_host_calls",
                "INSERT INTO result_get_values__result_entries (call_id, item_index,"
                        + " result_key, result_value, result_found)"
                        + " VALUES ('x', 0, 'k', 1, 1)",
                "DELETE FROM script_inputs")) {
            List<ValidationFinding> found = protocolWrite(sql);
            assertEquals(1, found.size(), sql + ": " + found);
            assertEquals(Severity.ERROR, found.get(0).severity(), sql);
        }
    }

    @Test
    void readingARuntimeOwnedTableStaysLegal() throws IOException {
        // Reading result tables and script_inputs is the entire point of the
        // protocol — only writes are denied. A scan-anywhere verb match would
        // break exactly this, so it is pinned.
        for (String sql : List.of(
                "SELECT result_value FROM result_get_value WHERE call_id = 'x'",
                "SELECT * FROM pending_host_calls",
                "SELECT int_value FROM script_inputs WHERE name = 'n'",
                "INSERT INTO script_vars (name, value_type, int_value)"
                        + " SELECT 'n', 'int64', result_value FROM result_get_value")) {
            assertEquals(List.of(), protocolWrite(sql), sql);
        }
    }

    @Test
    void scriptOwnedAndCallTablesStayWritable() throws IOException {
        // Writing a call table IS how a script makes a host call, and
        // script_vars / script_control are the script's own scratch and
        // control surfaces. Denying any of these would break every existing
        // valid payload.
        for (String sql : List.of(
                "INSERT INTO call_get_value (call_id, input_key) VALUES ('c1', 'k')",
                "INSERT INTO call_get_values__input_keys (call_id, item_index, input_key)"
                        + " VALUES ('c1', 0, 'k')",
                "INSERT INTO script_vars (name, value_type, int_value)"
                        + " VALUES ('n', 'int64', 1)",
                "UPDATE script_vars SET int_value = 2 WHERE name = 'n'",
                "DELETE FROM script_vars WHERE name = 'n'",
                "INSERT INTO script_control (action, message) VALUES ('halt', 'done')")) {
            assertEquals(List.of(), protocolWrite(sql), sql);
        }
    }

    @Test
    void aCtePrefixCannotSmuggleAProtocolWrite() throws IOException {
        // Anchoring the verb at token 0 alone would let a one-line dummy CTE
        // bypass the rule entirely; the analyzer therefore walks the CTE
        // prefix before reading the verb. Quoted target forms must resolve
        // the same way, since the fixture corpus already uses them.
        assertEquals(1, protocolWrite(
                "WITH d AS (SELECT 1) INSERT INTO result_get_value (call_id, status,"
                        + " result_value) SELECT 'x', 'done', 1").size());
        assertEquals(1, protocolWrite(
                "WITH RECURSIVE d(v) AS (SELECT 1) DELETE FROM pending_host_calls").size());
        assertEquals(1, protocolWrite("DELETE FROM [pending_host_calls]").size());
        assertEquals(1, protocolWrite("DELETE FROM main.pending_host_calls").size());
        assertEquals(1, protocolWrite(
                "INSERT OR REPLACE INTO result_get_value (call_id, status, result_value)"
                        + " VALUES ('x', 'done', 1)").size());
    }

    @Test
    void aSecondStatementAfterATopLevelSemicolonIsAnError() throws IOException {
        // One statement per `sql` field is the protocol contract: prepare_v2
        // compiles the first statement and silently drops the tail. A
        // top-level ';' with more SQL after it is that second, dropped
        // statement.
        for (String sql : List.of(
                "SELECT 1; PRAGMA writable_schema = ON",
                "SELECT 1; INSERT INTO result_get_value (call_id, status, result_value)"
                        + " VALUES ('x', 'done', 1)",
                "SELECT 1; DELETE FROM pending_host_calls",
                // the ';' is top-level, after a subquery's ')'
                "SELECT (SELECT 1); SELECT 2")) {
            List<ValidationFinding> found = multipleStatements(sql);
            assertEquals(1, found.size(), sql + ": " + found);
            assertEquals(Severity.ERROR, found.get(0).severity(), sql);
        }
    }

    @Test
    void multipleStatementsClosesTheLeadingNoOpDenylistBypass() throws IOException {
        // The core of the reported bug: leadingKeyword and writeTarget anchor
        // on the FIRST statement, so these two payloads sail past
        // forbidden-statement and protocol-table-write — the SELECT is all
        // those rules ever see. Before this rule the payloads produced ZERO
        // findings (publishable); now the multiple-statements error catches
        // the silent drop and blocks publication.
        String pragmaBypass = "SELECT 1; PRAGMA writable_schema = ON";
        String writeBypass = "SELECT 1; INSERT INTO result_get_value (call_id, status,"
                + " result_value) VALUES ('x', 'done', 1)";

        // The old denylist rules still don't fire — they only see `SELECT 1`.
        assertEquals(List.of(), forbidden(pragmaBypass));
        assertEquals(List.of(), protocolWrite(writeBypass));

        // …but each payload now carries a multiple-statements ERROR.
        for (String sql : List.of(pragmaBypass, writeBypass)) {
            List<ValidationFinding> all = allFindings(sql);
            assertTrue(all.stream().anyMatch(f ->
                            ValidationCodes.MULTIPLE_STATEMENTS.equals(f.code())
                                    && f.severity() == Severity.ERROR),
                    sql + ": " + all);
        }
    }

    @Test
    void aSingleStatementTerminatedOrNotIsNotMultipleStatements() throws IOException {
        // A bare trailing ';' terminates one statement — legal. And the
        // tokenizer collapses strings and comments, so a ';' inside a literal
        // or a comment is not a statement separator and must never be flagged.
        for (String sql : List.of(
                "SELECT 1",
                "SELECT 1;",
                "SELECT result_value FROM result_get_value WHERE call_id = 'x';",
                "SELECT ';'",
                "SELECT ';;; not sql'",
                "SELECT 1 -- ; x",
                "SELECT 1 /* ; */ + 1")) {
            assertEquals(List.of(), multipleStatements(sql), sql);
        }
    }

    @Test
    void theMessageNamesTheTableAndItsRole() throws IOException {
        // "protocol-table-write" alone does not tell an author which of the
        // several runtime-owned tables they touched, or why it is owned.
        ValidationFinding found = protocolWrite("DELETE FROM pending_host_calls").get(0);
        assertTrue(found.message().contains("pending_host_calls"), found.message());
        assertTrue(found.message().contains("queue"), found.message());
    }
}
