package io.sqlitehost.model;

import io.sqlitehost.model.ddl.DdlGenerator;
import io.sqlitehost.model.json.ManifestJsonReader;
import io.sqlitehost.model.manifest.Manifest;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;

/**
 * The Java DDL generator must reproduce the committed DDL snapshot
 * byte-for-byte — the cross-language golden keystone. If this test
 * fails, the generator diverged from codegen/core/src/ddl.ts.
 */
class DdlGeneratorGoldenTest {

    @Test
    void schemaScriptIsByteIdenticalToCommittedSnapshot() throws IOException {
        Path fixtures = Fixtures.fixturesDir();
        Manifest manifest = ManifestJsonReader.read(
                Files.readString(fixtures.resolve("manifests/sample-host.manifest.json")));
        String generated = DdlGenerator.generateSchemaScript(manifest);

        Path snapshot = fixtures.resolve("schemas/sample-host.ddl.sql");
        String expected = new String(Files.readAllBytes(snapshot), StandardCharsets.UTF_8);
        assertEquals(expected, generated, "generated DDL must match the committed snapshot");
        assertArrayEquals(Files.readAllBytes(snapshot),
                generated.getBytes(StandardCharsets.UTF_8),
                "generated DDL bytes must match the committed snapshot bytes");
    }

    @Test
    void statementOrderFollowsTheCanon() throws IOException {
        Path fixtures = Fixtures.fixturesDir();
        Manifest manifest = ManifestJsonReader.read(
                Files.readString(fixtures.resolve("manifests/sample-host.manifest.json")));
        var statements = DdlGenerator.generateSchemaStatements(manifest);

        // pending_host_calls, script_inputs, script_vars, then per method
        // (declaration order): call table, input child tables, result
        // table, result child tables, trigger (docs/workspace-schema.md).
        assertEquals(20, statements.size());
        assertEquals("CREATE TABLE pending_host_calls", firstLine(statements.get(0)).substring(0, 31));
        assertEquals("CREATE TABLE script_inputs (", firstLine(statements.get(1)));
        assertEquals("CREATE TABLE script_vars (", firstLine(statements.get(2)));
        assertEquals("CREATE TABLE call_get_values (", firstLine(statements.get(9)));
        assertEquals("CREATE TABLE call_get_values__input_keys (", firstLine(statements.get(10)));
        assertEquals("CREATE TABLE result_get_values (", firstLine(statements.get(11)));
        assertEquals("CREATE TABLE result_get_values__result_entries (", firstLine(statements.get(12)));
        assertEquals("CREATE TRIGGER trg_call_get_values_queue", firstLine(statements.get(13)));
    }

    private static String firstLine(String statement) {
        int newline = statement.indexOf('\n');
        return newline < 0 ? statement : statement.substring(0, newline);
    }
}
