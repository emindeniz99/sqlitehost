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
 * sqlite-version-too-low-for-syntax (docs/validation.md), the Java mirror of
 * the TypeScript syntax-version lint tests.
 *
 * <p>The function version lint has always covered half the above-floor
 * surface. The other half is grammar, and nothing measured it: the corpus
 * fixture {@code example-011-insert-alias} spells
 * {@code INSERT INTO t AS alias}, which is 3.24.0 syntax and a parse error on
 * the 3.19.3 floor, and BOTH validators accepted it — layer 3 prepares on
 * whatever engine the validator links, which is never the floor.</p>
 *
 * <p>Every construct is asserted twice: an error under a host at the default
 * floor, and silence under a host that declares 3.39.0. The second half is
 * not decoration — raising {@code minSqliteVersion} is the documented unlock
 * path (docs/sqlite-surface.md), so a detector that fires unconditionally
 * would turn a version check into a ban.</p>
 */
class SyntaxVersionLintTest {

    private static String manifestJson;
    private static Manifest manifest;
    private static Manifest raisedFloor;

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
        manifestJson = Files.readString(
                dir.resolve("fixtures/manifests/sample-host.manifest.json"));
        manifest = ManifestJsonReader.read(manifestJson);
        raisedFloor = manifestAtFloor(3039000);
    }

    /** The sample host with its declared SQLite floor replaced. */
    private static Manifest manifestAtFloor(int versionNumber) throws IOException {
        String patched = manifestJson.replace("\"minSqliteVersionNumber\": 3019003",
                "\"minSqliteVersionNumber\": " + versionNumber);
        if (patched.equals(manifestJson)) {
            throw new IllegalStateException("manifest floor field not found — fixture changed?");
        }
        return ManifestJsonReader.read(patched);
    }

    private static List<ValidationFinding> findings(String sql, Manifest host) throws IOException {
        Script script = ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"requiredApiLevel\":1,"
                        + "\"requiredFeatures\":[],\"requiredMethods\":[],"
                        + "\"steps\":[{\"id\":\"s\",\"statements\":[{\"sql\":\""
                        + sql.replace("\"", "\\\"")
                        + "\",\"bindings\":{}}]}]}");
        return new ValidationEngine().validate(host, script).findings().stream()
                .filter(f -> ValidationCodes.SQLITE_VERSION_TOO_LOW_FOR_SYNTAX.equals(f.code()))
                .toList();
    }

    private static List<ValidationFinding> findings(String sql) throws IOException {
        return findings(sql, manifest);
    }

    /**
     * One statement per feature id: {feature, sql, required version}. Nothing
     * here calls a post-floor FUNCTION — {@code count}, {@code max} and
     * {@code abs} are all pre-floor — so a case cannot score a false green off
     * the sibling function rule.
     */
    private static final String[][] CASES = {
        {"insert-alias",
            "INSERT INTO script_vars AS v (name, value_type, int_value)"
                    + " VALUES ('a', 'int64', 1)", "3.24.0"},
        {"upsert",
            "INSERT INTO script_vars (name, value_type, int_value)"
                    + " VALUES ('a', 'int64', 1) ON CONFLICT(name) DO NOTHING", "3.24.0"},
        {"window-functions", "SELECT count(*) OVER () FROM script_vars", "3.25.0"},
        {"aggregate-filter",
            "SELECT count(*) FILTER (WHERE int_value > 0) FROM script_vars", "3.30.0"},
        {"nulls-first-last",
            "SELECT name FROM script_vars ORDER BY name NULLS LAST", "3.30.0"},
        {"update-from",
            "UPDATE script_vars SET int_value = 1 FROM script_inputs"
                    + " WHERE script_vars.name = 'a'", "3.33.0"},
        {"returning",
            "DELETE FROM script_vars WHERE name = 'a' RETURNING name", "3.35.0"},
        {"materialized-cte",
            "WITH c AS MATERIALIZED (SELECT 1 AS x) SELECT x FROM c", "3.35.0"},
        {"json-arrow-operators",
            "SELECT text_value ->> '$.a' FROM script_vars", "3.38.0"},
        {"right-full-join",
            "SELECT a.name FROM script_vars a RIGHT JOIN script_inputs b"
                    + " ON a.name = b.name", "3.39.0"},
        {"is-distinct-from",
            "SELECT name FROM script_vars WHERE int_value IS NOT DISTINCT FROM 1", "3.39.0"},
    };

    @Test
    void everySyntaxFeatureAboveTheHostFloorIsAnError() throws IOException {
        for (String[] testCase : CASES) {
            List<ValidationFinding> found = findings(testCase[1]);
            assertEquals(1, found.size(), testCase[0] + ": " + found);
            assertEquals(Severity.ERROR, found.get(0).severity(), testCase[0]);
            assertTrue(found.get(0).message().contains(testCase[2]),
                    testCase[0] + ": " + found.get(0).message());
        }
    }

    @Test
    void theSameSyntaxUnderARaisedFloorIsSilent() throws IOException {
        for (String[] testCase : CASES) {
            assertEquals(List.of(), findings(testCase[1], raisedFloor), testCase[0]);
        }
    }

    @Test
    void theMessageNamesTheRequiredVersionAndTheHostFloor() throws IOException {
        String message = findings(CASES[6][1]).get(0).message();
        assertTrue(message.contains("3.35.0"), message);
        assertTrue(message.contains("3.19.3"), message);
        assertTrue(message.contains("RETURNING"), message);
    }

    @Test
    void aDelimitedIdentifierIsNeverTheKeyword() throws IOException {
        // The tokenizer-level false positives the detectors were designed
        // against. Every statement here is legal on 3.19.3; a text-level
        // match would reject all of them, and this code is an ERROR that
        // blocks publication, so a false positive costs what a miss costs.
        for (String sql : new String[] {
            "SELECT * FROM script_vars \"full\" JOIN script_inputs ON 1",
            "UPDATE script_vars SET int_value = 1 WHERE name = \"returning\"",
            "SELECT \"over\"(1) FROM script_vars",
            "SELECT 'a->>b' FROM script_vars",
            "SELECT \"nulls\" FROM script_vars",
        }) {
            assertEquals(List.of(), findings(sql), sql);
        }
    }

    @Test
    void statementAnchoredDetectorsDoNotFireOnOtherStatementKinds() throws IOException {
        for (String sql : new String[] {
            "SELECT a.name FROM script_vars a JOIN script_inputs b ON conflict = 1",
            "UPDATE script_vars SET int_value = (SELECT max(int_value) FROM script_inputs)",
            "SELECT returning FROM script_vars",
        }) {
            assertEquals(List.of(), findings(sql), sql);
        }
    }

    @Test
    void oneFindingPerFeaturePerStatementAndFeaturesAreIndependent() throws IOException {
        // Two uses of one construct are one problem with one fix; two
        // different constructs are two, because clearing the lower one still
        // leaves the higher one broken.
        assertEquals(1, findings(
                "SELECT count(*) OVER (), max(int_value) OVER () FROM script_vars").size());
        assertEquals(3, findings(
                "INSERT INTO script_vars AS v (name, value_type, int_value)"
                        + " VALUES ('a', 'int64', 1) ON CONFLICT(name) DO NOTHING"
                        + " RETURNING name").size());
    }

    @Test
    void aFloorExactlyAtTheFeatureReleaseAcceptsIt() throws IOException {
        // The comparison is '>' and not '>=': a host declaring 3.24.0 is
        // promising 3.24.0, so UPSERT is inside its contract.
        assertEquals(List.of(), findings(CASES[1][1], manifestAtFloor(3024000)));
        assertEquals(1, findings(CASES[1][1], manifestAtFloor(3023000)).size());
    }
}
