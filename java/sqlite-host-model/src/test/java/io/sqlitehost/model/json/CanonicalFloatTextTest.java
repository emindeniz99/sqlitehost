package io.sqlitehost.model.json;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import java.util.Random;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

/**
 * The canonical float spelling is ECMAScript's, because an envelope is
 * signed bytes and {@code JSON.stringify} in the TypeScript SDK is the
 * fixed side of the contract (docs/script-envelope.md). Every expected
 * string below is what {@code String(x)} prints in Node.
 *
 * <p>The cases are chosen around the two places the grammar switches —
 * the plain/exponential boundaries at 10^21 and 10^-6 — plus the values
 * where {@link Double#toString} visibly disagrees, either with
 * ECMAScript ({@code 1.0E21} for {@code 1e+21}) or with itself across
 * JDKs ({@code 1e23} prints {@code 9.999999999999999E22} on 17 and
 * {@code 1.0E23} from 19 on, JDK-4511638). CI builds on 17, 21 and 25,
 * so a writer that leaned on {@code Double.toString} would emit
 * different bytes per matrix leg.
 */
class CanonicalFloatTextTest {

    @ParameterizedTest(name = "{1}")
    @CsvSource({
            // Exponent boundary: 10^21 is the first value past plain form.
            "1e20,                     100000000000000000000",
            "1e21,                     1e+21",
            "1e23,                     1e+23",
            // 17 significant digits still inside the plain range.
            "1.2345678901234568e20,    123456789012345680000",
            // Small-magnitude boundary: plain down to 10^-6, then e-form.
            "1e-6,                     0.000001",
            "1e-7,                     1e-7",
            "0.1,                      0.1",
            "0.30000000000000004,      0.30000000000000004",
            // Extremes of the type.
            "4.9e-324,                 5e-324",
            "1.7976931348623157e308,   1.7976931348623157e+308",
            // Integral values carry no fraction, and the sign is a prefix.
            "3.0,                      3",
            "-2.5,                     -2.5",
            "-1e-7,                    -1e-7",
            "-1e21,                    -1e+21",
            // Dyadic-exact fixture values (example-006).
            "98.5,                     98.5",
            "0.75,                     0.75",
    })
    void writesTheEcmascriptSpelling(double value, String expected) {
        assertEquals(expected, CanonicalFloatText.of(value));
    }

    @Test
    void negativeZeroLosesItsSign() {
        // JSON.stringify(-0) is "0"; there is no signed zero on the wire.
        assertEquals("0", CanonicalFloatText.of(-0.0));
        assertEquals("0", CanonicalFloatText.of(0.0));
    }

    @Test
    void aFloat32IsWrittenThroughTheDoubleItWidensTo() {
        // The wire has one float grammar. A float32 0.1 is the single
        // 0.1f, and widening it gives the digits every SDK writes for
        // this binding — the value example-017 pins across TypeScript,
        // Java and C#.
        assertEquals("0.10000000149011612", CanonicalFloatText.of(0.1f));
    }

    @Test
    void nonFiniteValuesAreRefused() {
        // JSON has no NaN/Infinity literal, so there is nothing to write.
        assertThrows(IllegalArgumentException.class,
                () -> CanonicalFloatText.of(Double.NaN));
        assertThrows(IllegalArgumentException.class,
                () -> CanonicalFloatText.of(Double.POSITIVE_INFINITY));
        assertThrows(IllegalArgumentException.class,
                () -> CanonicalFloatText.of(Double.NEGATIVE_INFINITY));
    }

    @Test
    void everyDoubleRoundTripsThroughItsCanonicalText() {
        // The shortest-digits search is only correct if the text it
        // stops at still parses back to the identical bits. A fixed seed
        // keeps a failure reproducible.
        Random random = new Random(20260906L);
        for (int i = 0; i < 20000; i++) {
            double value = Double.longBitsToDouble(random.nextLong());
            if (!Double.isFinite(value) || value == 0.0) {
                continue; // signed zero is covered above, and loses its sign
            }
            String text = CanonicalFloatText.of(value);
            assertEquals(Double.doubleToLongBits(value),
                    Double.doubleToLongBits(Double.parseDouble(text)),
                    "canonical text must round-trip: " + value + " -> " + text);
        }
    }
}
