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
 * Engine-portability lints (docs/validation.md —
 * sqlite-version-too-low-for-function and nonportable-function).
 *
 * <p>Why they matter: layer 3 prepares script SQL on the validator's own
 * engine, which is many minor versions newer than the contract floor
 * (3.19.3). Every construct added in between therefore compiles clean in CI
 * and then fails on the player's device with a runtime SQL error — the
 * failure is discovered by the player, not the author. These two lints move
 * that discovery to authoring time by comparing the SQL against the host's
 * declared floor, which is data the manifest has always carried
 * ({@code library.minSqliteVersionNumber}) and which no validator read
 * before. The severity is ERROR rather than warning precisely because the
 * consequence is an unrecoverable failure on a shipped build.</p>
 *
 * <p>The two codes are distinct because the remedies differ: a version gap
 * is fixed by raising {@code minSqliteVersion}, while a compile-gated
 * built-in can never be made safe that way. Reporting the second as the
 * first would send authors down a fix that does not work. The mirror of
 * these cases lives in the TypeScript lint tests; the shared fixture matrix
 * pins that both implementations agree.</p>
 */
class EnginePortabilityLintTest {

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

    private static List<ValidationFinding> versionFindings(String sql) throws IOException {
        return findings(sql, ValidationCodes.SQLITE_VERSION_TOO_LOW_FOR_FUNCTION);
    }

    private static List<ValidationFinding> portabilityFindings(String sql) throws IOException {
        return findings(sql, ValidationCodes.NONPORTABLE_FUNCTION);
    }

    @Test
    void builtinsAddedAfterTheHostFloorAreErrors() throws IOException {
        // The sample host declares the default floor, 3.19.3. Each of
        // these shipped later, so each one is a device-side crash waiting to
        // happen: iif 3.32.0, format/unixepoch 3.38.0, octet_length 3.43.0,
        // concat/string_agg 3.44.0, row_number 3.25.0.
        for (String sql : List.of("SELECT iif(1, 2, 3)", "SELECT format('%d', 1)",
                "SELECT unixepoch()", "SELECT octet_length('a')",
                "SELECT concat('a', 'b')", "SELECT string_agg(k, ',')",
                "SELECT row_number() OVER ()")) {
            List<ValidationFinding> found = versionFindings(sql);
            assertEquals(1, found.size(), sql + ": " + found);
            assertEquals(Severity.ERROR, found.get(0).severity(), sql);
        }
    }

    @Test
    void theMessageNamesBothVersionsSoTheFixIsObvious() throws IOException {
        // An author who only learns "this is too new" cannot act. Naming the
        // required version and the host's floor makes the two possible fixes
        // (raise the floor / drop the function) decidable without a lookup.
        ValidationFinding found = versionFindings("SELECT iif(1, 2, 3)").get(0);
        assertTrue(found.message().contains("3.32.0"), found.message());
        assertTrue(found.message().contains("3.19.3"), found.message());
    }

    @Test
    void builtinsAtOrBelowTheFloorStaySilent() throws IOException {
        // The floor is a promise that these work everywhere, so flagging them
        // would be a false positive that trains authors to ignore the lint.
        // printf is the sharp case: format() is its post-3.38 rename, but
        // printf itself is 3.8.3 and must stay legal.
        for (String sql : List.of("SELECT printf('%d', 1)", "SELECT abs(-1)",
                "SELECT substr('abc', 1, 2)", "SELECT ltrim('  a')",
                "SELECT rtrim('a  ')", "SELECT trim(' a ')", "SELECT instr('ab', 'b')",
                "SELECT group_concat(k, ',')", "SELECT coalesce(a, b)")) {
            assertEquals(List.of(), versionFindings(sql), sql);
            assertEquals(List.of(), portabilityFindings(sql), sql);
        }
    }

    @Test
    void theJsonFamilyResolvesByLongestPrefix() throws IOException {
        // json_* is treated as 3.38.0 because that is the first release where
        // it is a built-in rather than a compile-gated extension; jsonb_* is
        // 3.45.0. Longest-prefix resolution is what keeps jsonb_extract from
        // being under-reported as the older, weaker json floor.
        assertTrue(versionFindings("SELECT json_extract(d, '$.a')").get(0)
                .message().contains("3.38.0"));
        assertTrue(versionFindings("SELECT jsonb_extract(d, '$.a')").get(0)
                .message().contains("3.45.0"));
    }

    @Test
    void compileGatedBuiltinsAreReportedSeparately() throws IOException {
        // Math functions arrived in 3.35.0 but are only present when the
        // engine was built with -DSQLITE_ENABLE_MATH_FUNCTIONS. Reporting
        // them as a version problem would point the author at raising
        // minSqliteVersion, which does not fix anything — hence a distinct
        // code, and no version finding alongside it.
        for (String sql : List.of("SELECT sqrt(2)", "SELECT pow(2, 8)",
                "SELECT ceil(1.5)", "SELECT log10(100)", "SELECT PI()")) {
            List<ValidationFinding> found = portabilityFindings(sql);
            assertEquals(1, found.size(), sql + ": " + found);
            assertEquals(Severity.ERROR, found.get(0).severity(), sql);
            assertTrue(found.get(0).message().contains("SQLITE_ENABLE_MATH_FUNCTIONS"),
                    found.get(0).message());
            assertEquals(List.of(), versionFindings(sql), sql);
        }
    }

    @Test
    void hostInlineFunctionsAreNeverJudgedAgainstTheEngine() throws IOException {
        // An inline function is registered by the host adapter through
        // sqlite3_create_function, so neither the engine's version nor its
        // compile options decide whether it exists. Were that not skipped, a
        // host whose functionPrefix produced a colliding name would be
        // permanently unable to publish.
        Script script = ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                        + "\"requiredFeatures\":[\"inlineFunctions\"],"
                        + "\"requiredMethods\":[],\"steps\":[{\"id\":\"s\",\"statements\":"
                        + "[{\"sql\":\"SELECT fn_get_value('k')\",\"bindings\":{}}]}]}");
        List<ValidationFinding> found = new ValidationEngine()
                .validate(manifest, script).findings();
        assertTrue(found.stream().noneMatch(f ->
                        ValidationCodes.SQLITE_VERSION_TOO_LOW_FOR_FUNCTION.equals(f.code())
                                || ValidationCodes.NONPORTABLE_FUNCTION.equals(f.code())),
                found.toString());
    }

    @Test
    void onlyCallSyntaxCountsAndTheReportIsDeduplicated() throws IOException {
        // A bare identifier is a column reference, not a call: `ORDER BY rank`
        // is ordinary SQL on any engine and flagging it would make the lint
        // unusable. A string literal spelling a call is collapsed by the
        // tokenizer for the same reason.
        assertEquals(List.of(), versionFindings("SELECT rank FROM t ORDER BY rank"));
        assertEquals(List.of(), versionFindings("SELECT 'iif(1,2,3)' AS label"));
        // Repeats of one name in a statement collapse to a single finding —
        // one fix, one message.
        assertEquals(1, versionFindings("SELECT iif(1, 2, 3), iif(4, 5, 6)").size());
    }

    @Test
    void theseFindingsBlockPublishing() throws IOException {
        // Severity is pinned as ERROR: docs/validation.md makes a payload
        // publishable on zero errors, and shipping either of these means a
        // hard SQL failure on some fraction of devices.
        Script script = ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                        + "\"requiredFeatures\":[],\"requiredMethods\":[],"
                        + "\"steps\":[{\"id\":\"s\",\"statements\":"
                        + "[{\"sql\":\"SELECT iif(1, sqrt(4), 3)\",\"bindings\":{}}]}]}");
        assertFalse(new ValidationEngine().validate(manifest, script).isValid());
    }
}
