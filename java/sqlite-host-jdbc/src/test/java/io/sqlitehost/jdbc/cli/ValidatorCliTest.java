package io.sqlitehost.jdbc.cli;

import org.junit.jupiter.api.Test;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The CLI's exit codes are a CI contract: 0 publishable, 1 the payload
 * has errors, 2 no verdict was reached. A payload the strict reader
 * refuses is a verdict about the payload -- reporting it as 2 told a
 * gate that the tooling was broken and left the bad payload unjudged.
 */
class ValidatorCliTest {

    private static Path fixtures() {
        Path dir = Paths.get("").toAbsolutePath();
        while (dir != null && !Files.isRegularFile(
                dir.resolve("fixtures/manifests/sample-host.manifest.json"))) {
            dir = dir.getParent();
        }
        if (dir == null) {
            throw new IllegalStateException("fixtures directory not found");
        }
        return dir.resolve("fixtures");
    }

    private static int run(String payload) {
        Path fixtures = fixtures();
        return ValidatorCli.run(new String[] {
                fixtures.resolve("manifests/sample-host.manifest.json").toString(),
                fixtures.resolve("payloads/" + payload).toString()});
    }

    /** Run the CLI, capturing stdout; returns "<exit>\n<stdout>". */
    private static String runCapturingStdout(String payload) {
        PrintStream original = System.out;
        ByteArrayOutputStream buffer = new ByteArrayOutputStream();
        int exit;
        try {
            System.setOut(new PrintStream(buffer, true, StandardCharsets.UTF_8));
            exit = run(payload);
        } finally {
            System.setOut(original);
        }
        return exit + "\n" + buffer.toString(StandardCharsets.UTF_8);
    }

    @Test
    void publishablePayloadExitsZero() {
        assertEquals(0, run("valid/example-001-read-then-conditional-write.json"));
    }

    @Test
    void payloadWithErrorsExitsOne() {
        assertEquals(1, run("invalid/missing-binding.json"));
    }

    @Test
    void payloadTheStrictReaderRefusesExitsOne() {
        // int32 written as 1.0 and int64 as 1e3: only the JSON text shows
        // this, so the reader is the only thing that can report it, and it
        // reports by throwing. That must still be exit 1.
        assertEquals(1, run("invalid/non-integral-int.json"));
    }

    @Test
    void aFaultOnlyTheCompilerSeesExitsOneAndPrintsIt() {
        // The gate is the publication pipeline, so the artifact a pipeline
        // runs must be the whole gate. invalid/unknown-column.json is the
        // corpus case whose ONLY expected finding is sql-prepare-error: a
        // CLI that skipped layer 3 exited 0 on a payload the corpus calls
        // invalid, and printed nothing at all.
        String result = runCapturingStdout("invalid/unknown-column.json");
        assertTrue(result.startsWith("1\n"), result);
        assertTrue(result.contains("sql-prepare-error"), result);
        assertTrue(result.contains("input_wrong"), result);
    }

    @Test
    void unreadableFileExitsTwo() throws IOException {
        Path missing = Files.createTempDirectory("sqlite-host-cli").resolve("absent.json");
        assertEquals(2, ValidatorCli.run(new String[] {
                fixtures().resolve("manifests/sample-host.manifest.json").toString(),
                missing.toString()}));
    }

    @Test
    void wrongArgumentCountExitsTwo() {
        assertEquals(2, ValidatorCli.run(new String[] {"only-one"}));
    }
}
