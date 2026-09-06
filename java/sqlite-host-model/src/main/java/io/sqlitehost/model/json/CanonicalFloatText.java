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
 * borrowed: for 1, 2, ... 17 significant digits, take the two decimals of
 * that length which bracket the value's exact decimal expansion, keep the
 * ones that parse back to the identical double, and stop at the first
 * length where one does. That is deliberately the slow,
 * obviously-correct formulation; envelopes are written at authoring time,
 * not in a hot loop.
 *
 * <p>Both brackets have to be tried, not just the nearest decimal. At a
 * power of two the round-tripping interval is asymmetric — the gap to the
 * neighbouring double below is half the gap above — so the nearest
 * 16-digit decimal can fall outside the narrow lower half while the
 * 16-digit decimal just above the value round-trips. Rounding once with
 * {@code HALF_EVEN} misses that one and spells the value with 17 digits;
 * ECMAScript spells {@code 2^-1018} {@code 7.120236347223045e-307}, not
 * {@code 7.1202363472230444e-307}.
 */
public final class CanonicalFloatText {

    /** Every finite double round-trips through 17 significant digits. */
    private static final int MAX_SIGNIFICANT_DIGITS = 17;

    /**
     * The two roundings that bracket a positive value: {@code FLOOR}
     * gives the largest decimal of that length at or below it,
     * {@code CEILING} the smallest at or above. Any other decimal of the
     * same length is further away than one of these, and the closest
     * round-tripping one is what ECMAScript asks for, so no other
     * candidate can win.
     */
    private static final RoundingMode[] BRACKETS = {
            RoundingMode.FLOOR, RoundingMode.CEILING};

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
        for (int digits = 1; digits <= MAX_SIGNIFICANT_DIGITS; digits++) {
            BigDecimal best = null;
            for (RoundingMode bracket : BRACKETS) {
                BigDecimal candidate = exact.round(new MathContext(digits, bracket));
                if (Double.parseDouble(format(candidate)) == value
                        && (best == null || closerToExact(candidate, best, exact))) {
                    best = candidate;
                }
            }
            if (best != null) {
                return format(best);
            }
        }
        throw new AssertionError(
                "no round-tripping spelling within " + MAX_SIGNIFICANT_DIGITS
                        + " significant digits: " + value);
    }

    /**
     * Whether {@code candidate} is the spelling ECMAScript prefers over
     * {@code incumbent}: the one closer to the exact value, and on a tie —
     * the value sits exactly between the two brackets — the one whose last
     * digit is even.
     */
    private static boolean closerToExact(
            BigDecimal candidate, BigDecimal incumbent, BigDecimal exact) {
        int byDistance = candidate.subtract(exact).abs()
                .compareTo(incumbent.subtract(exact).abs());
        if (byDistance != 0) {
            return byDistance < 0;
        }
        return !candidate.stripTrailingZeros().unscaledValue().testBit(0);
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
