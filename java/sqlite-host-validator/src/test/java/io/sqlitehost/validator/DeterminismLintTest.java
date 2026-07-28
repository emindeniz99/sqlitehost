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
 * Determinism lint (docs/validation.md — nondeterministic-function).
 *
 * <p>Why it matters: a payload is a durable artifact that may be replayed
 * (re-run against a restored database) and its result is expected to match
 * the original run. A built-in that draws from the RNG or the wall clock
 * silently breaks that, so the author is warned — but only warned, because
 * a one-shot script may legitimately want a random id. The mirror of these
 * cases lives in the TypeScript lint tests; the shared fixture matrix in
 * sqlite-host-jdbc pins that both implementations agree.</p>
 */
class DeterminismLintTest {

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

    /** The nondeterministic-function findings of a one-statement script. */
    private static List<ValidationFinding> warnings(String sql) throws IOException {
        Script script = ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                        + "\"requiredFeatures\":[],\"requiredMethods\":[],"
                        + "\"steps\":[{\"id\":\"s\",\"statements\":[{\"sql\":\"" + sql
                        + "\",\"bindings\":{}}]}]}");
        return new ValidationEngine().validate(manifest, script).findings().stream()
                .filter(f -> ValidationCodes.NONDETERMINISTIC_FUNCTION.equals(f.code()))
                .toList();
    }

    @Test
    void rngBuiltinsWarnOnEveryCall() throws IOException {
        for (String sql : List.of("SELECT random()", "SELECT randomblob(16)",
                "SELECT RANDOM()")) {
            List<ValidationFinding> found = warnings(sql);
            assertEquals(1, found.size(), sql + ": " + found);
            assertEquals(Severity.WARNING, found.get(0).severity());
            assertTrue(found.get(0).message().contains("replay"), found.get(0).message());
        }
    }

    @Test
    void dateTimeBuiltinsWarnOnlyOnTheClockForms() throws IOException {
        for (String sql : List.of("SELECT date()", "SELECT datetime('now')",
                "SELECT julianday('NOW')", "SELECT strftime('%Y', 'now')")) {
            assertEquals(1, warnings(sql).size(), sql);
        }
        // An explicit instant or a bound parameter is reproducible and must
        // stay silent, or authors would learn to ignore the warning.
        for (String sql : List.of("SELECT datetime('2020-01-01')", "SELECT date(:day)",
                "SELECT strftime('%Y', '2020-01-01')")) {
            assertEquals(List.of(), warnings(sql), sql);
        }
    }

    @Test
    void onlyExactBuiltinNamesMatch() throws IOException {
        // A host method named randomize(...) is not SQLite's random(), and a
        // string literal that spells a call is collapsed by the tokenizer —
        // neither may be flagged.
        assertEquals(List.of(), warnings("SELECT randomize(1)"));
        assertEquals(List.of(), warnings("SELECT 'random()' AS label"));
    }

    @Test
    void oneWarningPerOffendingCallOccurrence() throws IOException {
        List<ValidationFinding> found =
                warnings("SELECT random(), datetime('now'), abs(random())");
        assertEquals(3, found.size(), found.toString());
        assertTrue(found.stream().allMatch(f -> "s".equals(f.stepId())), found.toString());
    }

    @Test
    void theWarningNeverBlocksPublishing() throws IOException {
        // Severity is pinned as warning: docs/validation.md makes a payload
        // publishable on zero errors, and a script may legitimately want a
        // random id.
        Script script = ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                        + "\"requiredFeatures\":[],\"requiredMethods\":[],"
                        + "\"steps\":[{\"id\":\"s\",\"statements\":"
                        + "[{\"sql\":\"SELECT random()\",\"bindings\":{}}]}]}");
        ValidationReport report = new ValidationEngine().validate(manifest, script);
        assertTrue(report.isValid(), report.findings().toString());
    }
}
