package io.sqlitehost.model;

import com.fasterxml.jackson.databind.ObjectMapper;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.json.ScriptJsonWriter;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.TestFactory;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertEquals;

/**
 * Envelope JSON round-trip over every committed payload fixture:
 * parse → write → parse must preserve the model, and the re-written
 * JSON must be semantically identical to the original document. This
 * pins the reader/writer pair to the cross-language contract, not just
 * to each other.
 */
class EnvelopeRoundTripTest {

    private static final ObjectMapper MAPPER = new ObjectMapper();

    @TestFactory
    Stream<DynamicTest> everyPayloadFixtureRoundTrips() throws IOException {
        Path payloads = Fixtures.fixturesDir().resolve("payloads");
        List<Path> files = new ArrayList<>();
        try (var valid = Files.list(payloads.resolve("valid"));
             var invalid = Files.list(payloads.resolve("invalid"))) {
            valid.sorted().forEach(files::add);
            invalid.sorted().forEach(files::add);
        }
        return files.stream().map(file -> DynamicTest.dynamicTest(
                file.getParent().getFileName() + "/" + file.getFileName(),
                () -> assertRoundTrips(file)));
    }

    private void assertRoundTrips(Path file) throws IOException {
        String original = Files.readString(file);
        Script parsed = ScriptJsonReader.read(original);
        String written = ScriptJsonWriter.write(parsed);
        Script reparsed = ScriptJsonReader.read(written);

        assertEquals(parsed, reparsed, "model must survive write→read");
        assertEquals(MAPPER.readTree(original), MAPPER.readTree(written),
                "re-written JSON must be semantically identical to the fixture");
    }
}
