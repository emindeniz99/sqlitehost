package io.sqlitehost.model;

import io.sqlitehost.model.json.JsonReadException;
import io.sqlitehost.model.json.ScriptJsonReader;
import org.junit.jupiter.api.Test;

import java.io.IOException;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;

/**
 * An explicit JSON {@code null} is a type error, never an absent field
 * (docs/script-envelope.md). The {@code null} binding type already made
 * that distinction -- a {@code {"type":"null","value":null}} is refused
 * because a present {@code value} is present even when it is null -- and
 * these tests hold the rest of the envelope to the same rule.
 *
 * <p>WHY it matters rather than being pedantry: serializers routinely
 * emit {@code null} for an absent optional, so the two spellings turn up
 * in real payloads. The TypeScript parser has always refused them; Java
 * silently read them as absence, so the same signed bytes were
 * publishable through one SDK and not the other.</p>
 */
class EnvelopeNullFieldTest {

    private static final String VALID_TAIL =
            "\"steps\":[{\"id\":\"s\",\"statements\":[{\"sql\":\"SELECT 1\"}]}]}";

    private static void assertRejected(String head) {
        assertThrows(JsonReadException.class,
                () -> ScriptJsonReader.read("{" + head + "," + VALID_TAIL));
    }

    @Test
    void absentOptionalScalarsStayAbsent() throws IOException {
        var script = ScriptJsonReader.read("{\"engine\":\"sqlite-host-v1\"," + VALID_TAIL);
        assertNull(script.scriptId());
        assertNull(script.requiredApiLevel());
        assertEquals(0, script.requiredFeatures().size());
        assertEquals(0, script.inputs().size());
    }

    @Test
    void explicitNullIsRejectedForEveryOptionalField() {
        assertRejected("\"engine\":null");
        assertRejected("\"scriptId\":null");
        assertRejected("\"requiredApiLevel\":null");
        assertRejected("\"requiredFeatures\":null");
        assertRejected("\"requiredMethods\":null");
        assertRejected("\"inputs\":null");
    }

    @Test
    void explicitNullIsRejectedInsideStepsAndStatements() {
        assertThrows(JsonReadException.class,
                () -> ScriptJsonReader.read("{\"steps\":null}"));
        assertThrows(JsonReadException.class,
                () -> ScriptJsonReader.read("{\"steps\":[{\"id\":null,"
                        + "\"statements\":[{\"sql\":\"SELECT 1\"}]}]}"));
        assertThrows(JsonReadException.class,
                () -> ScriptJsonReader.read("{\"steps\":[{\"id\":\"s\","
                        + "\"statements\":null}]}"));
        assertThrows(JsonReadException.class,
                () -> ScriptJsonReader.read("{\"steps\":[{\"id\":\"s\",\"statements\":"
                        + "[{\"sql\":null}]}]}"));
        assertThrows(JsonReadException.class,
                () -> ScriptJsonReader.read("{\"steps\":[{\"id\":\"s\",\"statements\":"
                        + "[{\"sql\":\"SELECT 1\",\"bindings\":null}]}]}"));
    }

    @Test
    void explicitNullIsRejectedForARuntimeInputValue() {
        assertThrows(JsonReadException.class,
                () -> ScriptJsonReader.read("{\"inputs\":[{\"name\":\"a\",\"value\":null}],"
                        + VALID_TAIL));
    }
}
