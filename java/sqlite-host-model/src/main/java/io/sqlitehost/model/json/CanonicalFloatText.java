package io.sqlitehost.model.json;

import java.math.BigDecimal;
import java.math.MathContext;
import java.math.RoundingMode;

/**
 * Canonical wire text for a {@code float32}/{@code float64} value
 * (docs/script-envelope.md, "Canonical float text").
 *
 * <p>The canonical spelling is the one ECMAScript's {@code Number::toString}
 * produces, because {@code JSON.stringify} in the TypeScript SDK is the
 * fixed side of the contract and an envelope is signed bytes: shortest
 * round-tripping digits, plain decimal notation while the decimal
 * exponent {@code n} satisfies {@code -6 < n <= 21} and {@code e}
 * notation with an always-signed exponent otherwise, {@code -0} spelled
 * {@code 0}, and no {@code .0} tail on an integral value.
 *
 * <p>{@link Double#toString} cannot be used for any of this. It emits
 * Java's own grammar ({@code 1.0E21}, {@code 4.9E-324}) and — worse for
 * a byte contract — it does not even agree with itself across the JDKs
 * this project supports: JDK 19 replaced its digit selection with the
 * shortest round-tripping one (JDK-4511638), so {@code 1e23} prints
 * {@code 9.999999999999999E22} on the JDK 17 floor and {@code 1.0E23} on
 * JDK 19 and later. CI builds on 17, 21 and 25.
 *
 * <p>The shortest digit string is therefore found here rather than
 * borrowed: round the value's exact decimal expansion to 1, 2, ... 17
 * significant digits and take the first that parses back to the identical
 * double. That is deliberately the slow, obviously-correct formulation;
 * envelopes are written at authoring time, not in a hot loop.
 */
public final class CanonicalFloatText {

    /** Every finite double round-trips through 17 significant digits. */
    private static final int MAX_SIGNIFICANT_DIGITS = 17;

    private CanonicalFloatText() {
    }

    /**
     * Renders a finite double as canonical envelope JSON number text.
     *
     * @throws IllegalArgumentException if the value is NaN or infinite —
     *     JSON has no literal for either, so they never reach the wire
     *     (the {@code BindingValue} factories reject them first).
     */
    public static String of(double value) {
        if (!Double.isFinite(value)) {
            throw new IllegalArgumentException(
                    "non-finite floats have no JSON representation: " + value);
        }
        if (value == 0.0) {
            // Covers -0.0: ECMAScript spells both zeros "0".
            return "0";
        }
        if (value < 0.0) {
            return "-" + of(-value);
        }
        BigDecimal exact = new BigDecimal(value);
        for (int digits = 1; digits < MAX_SIGNIFICANT_DIGITS; digits++) {
            String candidate = format(exact.round(
                    new MathContext(digits, RoundingMode.HALF_EVEN)));
            if (Double.parseDouble(candidate) == value) {
                return candidate;
            }
        }
        return format(exact.round(
                new MathContext(MAX_SIGNIFICANT_DIGITS, RoundingMode.HALF_EVEN)));
    }

    /**
     * ECMAScript {@code Number::toString} step 5 onwards for a positive
     * value already rounded to its shortest digits: {@code s} is the digit
     * string, {@code k} its length, and the value is {@code s × 10^(n−k)}.
     */
    private static String format(BigDecimal rounded) {
        BigDecimal stripped = rounded.stripTrailingZeros();
        String s = stripped.unscaledValue().toString();
        int k = s.length();
        int n = k - stripped.scale();

        if (k <= n && n <= 21) {
            return s + "0".repeat(n - k);
        }
        if (0 < n && n <= 21) {
            return s.substring(0, n) + "." + s.substring(n);
        }
        if (-6 < n && n <= 0) {
            return "0." + "0".repeat(-n) + s;
        }
        String mantissa = k == 1 ? s : s.charAt(0) + "." + s.substring(1);
        int exponent = n - 1;
        return mantissa + "e" + (exponent < 0 ? "-" : "+") + Math.abs(exponent);
    }
}
