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
    void decimalStringMustBeStrict() throws IOException {
        // No whitespace, no '+' — only ^-?[0-9]+$ (docs/script-envelope.md).
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"int64\",\"value\":\" 42 \"}"));
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"int64\",\"value\":\"+42\"}"));
        assertEquals(-42L, onlyBinding(
                scriptWithBinding("{\"type\":\"int64\",\"value\":\"-42\"}")).asInt64());
        assertEquals(Long.MAX_VALUE, onlyBinding(
                scriptWithBinding("{\"type\":\"int64\",\"value\":\"9223372036854775807\"}"))
                .asInt64());
    }

    @Test
    void int64NumberFormBeyondSafeJsonIntegerIsRejected() {
        // Writers must use the string form when |v| > 2^53−1.
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"int64\",\"value\":9223372036854775807}"));
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
    void nonCanonicalBase64IsRejected() throws IOException {
        // Strict per docs/script-envelope.md: standard alphabet, padded,
        // no whitespace — a lenient decoder must not accept these.
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"blob\",\"value\":\"abc\"}"));
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"blob\",\"value\":\"3q2+7w\"}"));
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"blob\",\"value\":\"3q2\\n+7w==\"}"));
        assertArrayEquals(new byte[] {(byte) 0xDE, (byte) 0xAD, (byte) 0xBE, (byte) 0xEF},
                onlyBinding(scriptWithBinding("{\"type\":\"blob\",\"value\":\"3q2+7w==\"}"))
                        .asBlob());
    }

    @Test
    void floatsAcceptFiniteJsonNumbers() throws IOException {
        BindingValue score = onlyBinding(
                scriptWithBinding("{\"type\":\"float64\",\"value\":98.5}"));
        assertEquals(BindingValue.Type.FLOAT64, score.type());
        assertEquals(98.5, score.asFloat64());

        BindingValue weight = onlyBinding(
                scriptWithBinding("{\"type\":\"float32\",\"value\":0.75}"));
        assertEquals(BindingValue.Type.FLOAT32, weight.type());
        assertEquals(0.75f, weight.asFloat32());
    }

    @Test
    void integralJsonNumbersAreValidFloatValues() throws IOException {
        // Only the string form is banned — an integral number is a
        // perfectly good float value (docs/script-envelope.md).
        assertEquals(42.0, onlyBinding(
                scriptWithBinding("{\"type\":\"float64\",\"value\":42}")).asFloat64());
        assertEquals(3.0f, onlyBinding(
                scriptWithBinding("{\"type\":\"float32\",\"value\":3}")).asFloat32());
    }

    @Test
    void floatStringFormIsRejected() {
        // Unlike int64, floats never need a string form: every IEEE-754
        // double round-trips through a JSON number.
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"float64\",\"value\":\"98.5\"}"));
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"float32\",\"value\":\"0.75\"}"));
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"float64\",\"value\":\"42\"}"));
    }

    @Test
    void float32MustStayFiniteAfterRoundToNearestSingle() throws IOException {
        // 1e39 is a finite double but overflows an IEEE-754 single.
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"float32\",\"value\":1e39}"));
        // Parsed via round-to-nearest, so inexact singles are fine.
        assertEquals(0.1f, onlyBinding(
                scriptWithBinding("{\"type\":\"float32\",\"value\":0.1}")).asFloat32());
    }

    @Test
    void nonFiniteOrNonNumericFloatValuesAreRejected() {
        // JSON has no NaN/Infinity literal; 1e309 parses to a non-finite
        // double and must be rejected, as must non-number values.
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"float64\",\"value\":1e309}"));
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"float64\",\"value\":true}"));
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"float32\"}"));
    }

    @Test
    void factoriesRejectNonFiniteFloats() {
        // JSON has no NaN/Infinity literal (docs/script-envelope.md), so
        // a non-finite value can never serialize; without this guard
        // ScriptJsonWriter silently emits the banned string form ("NaN")
        // that every conforming reader rejects.
        assertThrows(IllegalArgumentException.class,
                () -> BindingValue.float64(Double.NaN));
        assertThrows(IllegalArgumentException.class,
                () -> BindingValue.float64(Double.POSITIVE_INFINITY));
        assertThrows(IllegalArgumentException.class,
                () -> BindingValue.float32(Float.NaN));
        assertThrows(IllegalArgumentException.class,
                () -> BindingValue.float32(Float.NEGATIVE_INFINITY));
        // Every finite value stays accepted, including signed zero and
        // the extremes of each type's range.
        assertEquals(-0.0, BindingValue.float64(-0.0).asFloat64());
        assertEquals(Double.MAX_VALUE, BindingValue.float64(Double.MAX_VALUE).asFloat64());
        assertEquals(Float.MAX_VALUE, BindingValue.float32(Float.MAX_VALUE).asFloat32());
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
        // A present `value` violates the "null = absent value" contract
        // (docs/script-envelope.md) — both a non-null value and an
        // explicit JSON null, which the old hasNonNull guard wrongly
        // accepted. This pins the Java reader to the same key-presence
        // rule the TS parser already enforces (parse.test.ts) so the two
        // cross-language readers agree; without the null case the test
        // would pass while the readers diverge.
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"null\",\"value\":1}"));
        assertThrows(JsonReadException.class,
                () -> scriptWithBinding("{\"type\":\"null\",\"value\":null}"));
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
