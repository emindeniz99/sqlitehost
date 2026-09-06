package io.sqlitehost.model.json;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Random;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
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

    /**
     * The 46 positive powers of two a differential probe against Node's
     * {@code String(x)} caught: 92 of 210,188 finite doubles disagreed,
     * every one of them a power of two, each spelled one digit too long.
     *
     * <p>At a power of two the round-tripping interval is asymmetric —
     * the neighbouring double below is half an ulp away where the one
     * above is a full ulp — so the nearest 16-digit decimal falls outside
     * it while the 16-digit decimal just above the value round-trips. A
     * search that rounds once, to nearest, never sees that second
     * candidate and falls through to 17 digits, which is a different
     * envelope byte string than the TypeScript writer produces.
     */
    @ParameterizedTest(name = "{1}")
    @CsvSource({
            "0060000000000000, 7.120236347223045e-307",
            "0100000000000000, 7.291122019556398e-304",
            "0420000000000000, 8.209073602596753e-289",
            "0660000000000000, 5.641232424577593e-278",
            "0d70000000000000, 5.858190679279809e-244",
            "0e80000000000000, 7.678447687145631e-239",
            "0eb0000000000000, 6.142758149716505e-238",
            "0f50000000000000, 6.290184345309701e-235",
            "13e0000000000000, 5.940911144672375e-213",
            "1480000000000000, 6.083493012144512e-210",
            "1690000000000000, 5.225680706521042e-200",
            "1730000000000000, 5.351097043477547e-197",
            "1da0000000000000, 5.426657103235053e-166",
            "2020000000000000, 5.966672584960166e-154",
            "20f0000000000000, 4.887898181599368e-150",
            "2160000000000000, 6.256509672447191e-148",
            "2800000000000000, 5.075883674631299e-116",
            "2910000000000000, 6.653062250012736e-111",
            "2d70000000000000, 7.854549544476363e-90",
            "3730000000000000, 7.174648137343064e-43",
            "39e0000000000000, 6.310887241768095e-30",
            "3b20000000000000, 6.617444900424222e-24",
            "3d30000000000000, 5.684341886080802e-14",
            "3e70000000000000, 5.960464477539063e-8",
            "4580000000000000, 6.189700196426902e+26",
            "4790000000000000, 5.316911983139664e+36",
            "4830000000000000, 5.444517870735016e+39",
            "4ab0000000000000, 5.986310706507379e+51",
            "4b50000000000000, 6.129982163463556e+54",
            "5120000000000000, 6.070840288205404e+82",
            "5300000000000000, 6.518515124270356e+91",
            "5580000000000000, 7.167183174968974e+103",
            "5790000000000000, 6.156563468186638e+113",
            "58d0000000000000, 6.455624695217272e+119",
            "5940000000000000, 8.263199609878108e+121",
            "5e00000000000000, 6.243497100631985e+144",
            "6150000000000000, 5.623642243178996e+160",
            "61f0000000000000, 5.758609657015292e+163",
            "6290000000000000, 5.896816288783659e+166",
            "63d0000000000000, 6.183260036827614e+172",
            "6510000000000000, 6.483618076376552e+178",
            "6c50000000000000, 5.386379163185535e+213",
            "7220000000000000, 5.334411546303884e+241",
            "75e0000000000000, 6.150157786156811e+259",
            "77f0000000000000, 5.282945311356653e+269",
            "7cf0000000000000, 6.386688990511104e+293",
    })
    void powersOfTwoUseTheShorterOfTheTwoBracketingDecimals(
            String hexBits, String expected) {
        double value = Double.longBitsToDouble(Long.parseUnsignedLong(hexBits, 16));
        assertEquals(expected, CanonicalFloatText.of(value));
        assertEquals("-" + expected, CanonicalFloatText.of(-value));
    }

    /**
     * Every power of two the type has, from the smallest subnormal to the
     * largest finite one, against Node. The table is generated once by
     * {@code node -e 'for (let e = -1074; e <= 1023; e++) …'} and committed
     * as {@code powers-of-two.tsv}: recomputing the expectation in Java
     * would only prove Java agrees with itself, and ECMAScript is the
     * fixed side of this contract.
     */
    @Test
    void everyPowerOfTwoMatchesEcmascript() throws IOException {
        List<String> lines = readPowersOfTwo();
        assertEquals(2098, lines.size(), "expected one row per exponent -1074..1023");
        for (String line : lines) {
            String[] row = line.split("\t");
            int exponent = Integer.parseInt(row[0]);
            assertEquals(row[1], CanonicalFloatText.of(Math.scalb(1.0, exponent)),
                    "2^" + exponent);
        }
    }

    private static List<String> readPowersOfTwo() throws IOException {
        try (InputStream in = CanonicalFloatTextTest.class
                .getResourceAsStream("powers-of-two.tsv")) {
            assertNotNull(in, "powers-of-two.tsv must be on the test classpath");
            try (BufferedReader reader = new BufferedReader(
                    new InputStreamReader(in, StandardCharsets.UTF_8))) {
                return reader.lines().toList();
            }
        }
    }
}
