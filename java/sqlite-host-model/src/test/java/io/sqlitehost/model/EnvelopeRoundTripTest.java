package io.sqlitehost.model;

import com.fasterxml.jackson.databind.ObjectMapper;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.JsonReadException;
import io.sqlitehost.model.json.ScriptJsonReader;
import io.sqlitehost.model.json.ScriptJsonWriter;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestFactory;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Envelope JSON round-trip over every committed payload fixture:
 * parse → write → parse must preserve the model, and the re-written
 * JSON must be semantically identical to the original document. This
 * pins the reader/writer pair to the cross-language contract, not just
 * to each other.
 */
class EnvelopeRoundTripTest {

    private static final ObjectMapper MAPPER = new ObjectMapper();

    /**
     * Payloads carrying a {@code float32} the wire spelling cannot
     * represent exactly. {@code float32} is round-to-nearest on parse
     * (docs/script-envelope.md), so the first write emits the single the
     * engine will store (0.1 &rarr; 0.10000000149011612) instead of the
     * author's digits. Reproducing the fixture bytes is therefore the
     * property for payloads whose {@code float32} values are already
     * representable as singles; for these it is stability from the first
     * write on.
     */
    private static final Set<String> NON_SINGLE_REPRESENTABLE_FLOAT32 =
            Set.of("example-017-float32-rounding.json");

    /**
     * Fixtures whose whole point is that the strict reader refuses them:
     * they violate the envelope contract in a way only the JSON text shows,
     * so there is no model to round-trip. The round trip asserts the
     * refusal instead, which is the stronger statement.
     */
    private static final Set<String> READER_REJECTS =
            Set.of("non-integral-int.json");

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

    @Test
    void dyadicExactFloatsKeepTheirWireBytes() throws IOException {
        // 98.5 and 0.75 are dyadic-exact, so the re-written float values
        // must use the exact same digits as the fixture (the same bytes
        // Java, JS, and C# all produce for these values).
        Path fixture = Fixtures.fixturesDir()
                .resolve("payloads/valid/example-006-floats.json");
        String written = ScriptJsonWriter.write(
                ScriptJsonReader.read(Files.readString(fixture)));
        assertTrue(written.contains("98.5"), written);
        assertTrue(written.contains("0.75"), written);
    }

    @Test
    void float32IsRoundedToTheSingleTheEngineStores() throws IOException {
        // WHY: the same envelope must mean the same number in every SDK.
        // A wire 0.1 is a double; every reader narrows it through a 32-bit
        // float, so the value that reaches SQLite is 0.10000000149011612.
        Path fixture = Fixtures.fixturesDir()
                .resolve("payloads/valid/example-017-float32-rounding.json");
        Script script = ScriptJsonReader.read(Files.readString(fixture));
        assertEquals(0.1f,
                script.steps().get(0).statements().get(0).bindings().get("weight").asFloat32());
        // float64 keeps the double it was written as — only float32 narrows.
        assertEquals(0.1d,
                script.steps().get(0).statements().get(0).bindings().get("score").asFloat64());
        assertTrue(ScriptJsonWriter.write(script).contains("0.10000000149011612"));
    }

    private void assertRoundTrips(Path file) throws IOException {
        String original = Files.readString(file);
        if (READER_REJECTS.contains(file.getFileName().toString())) {
            assertThrows(JsonReadException.class, () -> ScriptJsonReader.read(original),
                    file.getFileName() + ": the strict reader must refuse this payload");
            return;
        }
        Script parsed = ScriptJsonReader.read(original);
        String written = ScriptJsonWriter.write(parsed);
        Script reparsed = ScriptJsonReader.read(written);

        assertEquals(parsed, reparsed, "model must survive write→read");
        if (NON_SINGLE_REPRESENTABLE_FLOAT32.contains(file.getFileName().toString())) {
            assertEquals(MAPPER.readTree(written),
                    MAPPER.readTree(ScriptJsonWriter.write(reparsed)),
                    "the normalized document must be stable under further round-trips");
        } else {
            assertEquals(MAPPER.readTree(original), MAPPER.readTree(written),
                    "re-written JSON must be semantically identical to the fixture");
        }
    }
}
