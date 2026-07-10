package io.sqlitehost.model;

import io.sqlitehost.model.envelope.BindingValue;
import io.sqlitehost.model.envelope.Script;
import io.sqlitehost.model.json.JsonReadException;
import io.sqlitehost.model.json.ScriptJsonReader;
import org.junit.jupiter.api.Test;

import java.io.IOException;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Binding value wire rules (docs/script-envelope.md): int64 accepted
 * as number or decimal string, blob as base64, unknown type is a hard
 * reader error, typed accessors are type-safe.
 */
class BindingValueJsonTest {

    private static Script scriptWithBinding(String bindingJson) throws IOException {
        return ScriptJsonReader.read("{\"steps\":[{\"id\":\"s\",\"statements\":[{"
                + "\"sql\":\"SELECT :x\",\"bindings\":{\"x\":" + bindingJson + "}}]}]}");
    }

    private static BindingValue onlyBinding(Script script) {
        return script.steps().get(0).statements().get(0).bindings().get("x");
    }

    @Test
    void int64AcceptsJsonNumber() throws IOException {
        BindingValue value = onlyBinding(scriptWithBinding("{\"type\":\"int64\",\"value\":42}"));
        assertEquals(BindingValue.Type.INT64, value.type());
        assertEquals(42L, value.asInt64());
    }

    @Test
    void int64AcceptsDecimalStringBeyondDoublePrecision() throws IOException {
        // 2^53 + 1 is not representable as a double, hence the string form.
        BindingValue value = onlyBinding(
                scriptWithBinding("{\"type\":\"int64\",\"value\":\"9007199254740993\"}"));
        assertEquals(9007199254740993L, value.asInt64());
    }

    @Test
    void int32AcceptsNumberOrDecimalStringAndEnforcesRange() throws IOException {
        assertEquals(5, onlyBinding(
                scriptWithBinding("{\"type\":\"int32\",\"value\":5}")).asInt32());
        assertEquals(-7, onlyBinding(
                scriptWithBinding("{\"type\":\"int32\",\"value\":\"-7\"}")).asInt32());
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"int32\",\"value\":2147483648}"));
    }

    @Test
    void nonIntegralIntegerValueIsRejected() {
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"int64\",\"value\":1.5}"));
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"int64\",\"value\":\"abc\"}"));
    }

    @Test
    void blobDecodesStandardBase64() throws IOException {
        BindingValue value = onlyBinding(
                scriptWithBinding("{\"type\":\"blob\",\"value\":\"3q2+7w==\"}"));
        assertArrayEquals(new byte[] {(byte) 0xDE, (byte) 0xAD, (byte) 0xBE, (byte) 0xEF},
                value.asBlob());
    }

    @Test
    void invalidBase64IsRejected() {
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"blob\",\"value\":\"!!not-base64!!\"}"));
    }

    @Test
    void unknownBindingTypeIsAReaderError() {
        JsonReadException e = assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"float\",\"value\":1.0}"));
        assertTrue(e.getMessage().contains("unknown envelope binding type 'float'"),
                e.getMessage());
    }

    @Test
    void nullBindingCarriesNoValue() throws IOException {
        BindingValue value = onlyBinding(scriptWithBinding("{\"type\":\"null\"}"));
        assertEquals(BindingValue.Type.NULL, value.type());
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"null\",\"value\":1}"));
    }

    @Test
    void typedAccessorsThrowOnWrongType() {
        BindingValue text = BindingValue.text("hello");
        assertEquals("hello", text.asText());
        assertThrows(IllegalStateException.class, text::asInt64);
        assertThrows(IllegalStateException.class, text::asBlob);

        BindingValue flag = BindingValue.bool(true);
        assertTrue(flag.asBool());
        assertThrows(IllegalStateException.class, flag::asText);
    }

    @Test
    void factoriesAndEqualityAreValueBased() {
        assertEquals(BindingValue.int64(42), BindingValue.int64(42));
        assertEquals(BindingValue.blob(new byte[] {1, 2}), BindingValue.blob(new byte[] {1, 2}));
        assertEquals(BindingValue.nullValue(), BindingValue.nullValue());
    }
}
