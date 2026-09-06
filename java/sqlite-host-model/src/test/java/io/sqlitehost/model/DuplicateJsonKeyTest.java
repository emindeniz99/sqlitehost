package io.sqlitehost.model;

import io.sqlitehost.model.json.ScriptJsonReader;
import org.junit.jupiter.api.Test;

import java.io.IOException;

import static org.junit.jupiter.api.Assertions.assertThrows;

/**
 * An object may not repeat a key (docs/script-envelope.md).
 *
 * <p>WHY it is a rejection and not a last-wins rule: the whole security
 * model of the delivery path is "the validator judged the same document
 * the device runs". A repeated key has no single meaning across readers
 * -- last-wins is only a convention, and a streaming reader that keeps
 * the first value is equally conforming to JSON itself -- so a payload
 * carrying one is a payload two implementations may legitimately read
 * differently. {@code {"sql":"ATTACH ...","sql":"SELECT 1"}} is the
 * shape that matters: a validator reading the second and an engine
 * reading the first is a bypass with no tampering anywhere.</p>
 *
 * <p>Rejecting is the only choice that cannot differ between
 * implementations, which is why the spec picks it over pinning
 * last-wins.</p>
 */
class DuplicateJsonKeyTest {

    private static final String VALID_TAIL =
            "\"steps\":[{\"id\":\"s\",\"statements\":[{\"sql\":\"SELECT 1\"}]}]}";

    @Test
    void duplicateTopLevelKeyIsRejected() {
        assertThrows(IOException.class, () -> ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"engine\":\"sqlite-host-v1\"," + VALID_TAIL));
    }

    @Test
    void duplicateStatementSqlIsRejected() {
        // The differential that motivates the rule.
        assertThrows(IOException.class, () -> ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"steps\":[{\"id\":\"s\",\"statements\":"
                        + "[{\"sql\":\"ATTACH 'x' AS y\",\"sql\":\"SELECT 1\"}]}]}"));
    }

    @Test
    void duplicateKeyInsideABindingIsRejected() {
        assertThrows(IOException.class, () -> ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"steps\":[{\"id\":\"s\",\"statements\":"
                        + "[{\"sql\":\"SELECT :a\",\"bindings\":{\"a\":"
                        + "{\"type\":\"int32\",\"value\":1,\"value\":2}}}]}]}"));
    }

    @Test
    void duplicateBindingNameIsRejected() {
        assertThrows(IOException.class, () -> ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"steps\":[{\"id\":\"s\",\"statements\":"
                        + "[{\"sql\":\"SELECT :a\",\"bindings\":{"
                        + "\"a\":{\"type\":\"int32\",\"value\":1},"
                        + "\"a\":{\"type\":\"int32\",\"value\":2}}}]}]}"));
    }

    @Test
    void distinctKeysStillRead() throws IOException {
        // Guard against over-tightening: the check is per object, so the
        // same key name in two different objects is ordinary.
        ScriptJsonReader.read(
                "{\"engine\":\"sqlite-host-v1\",\"steps\":["
                        + "{\"id\":\"a\",\"statements\":[{\"sql\":\"SELECT 1\"}]},"
                        + "{\"id\":\"b\",\"statements\":[{\"sql\":\"SELECT 2\"}]}]}");
    }
}
