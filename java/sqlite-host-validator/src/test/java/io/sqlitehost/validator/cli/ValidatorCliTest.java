package io.sqlitehost.validator.cli;

import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;

import static org.junit.jupiter.api.Assertions.assertEquals;

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
