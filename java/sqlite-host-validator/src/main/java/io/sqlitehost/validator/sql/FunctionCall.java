package io.sqlitehost.validator.sql;

/**
 * One {@code identifier(...)} call extracted from the token stream.
 * {@code argCount} is the number of top-level arguments, or
 * {@link #UNKNOWN_ARGS} when the matching {@code ')'} is missing
 * (malformed SQL — prepare-only validation reports it).
 * {@code hasNowArg} says whether some top-level argument is the string
 * literal {@code 'now'} (case-insensitive) — what makes a date/time
 * built-in read the wall clock (the determinism lint,
 * docs/validation.md).
 */
public record FunctionCall(String name, int argCount, boolean hasNowArg) {

    /** A call with no {@code 'now'} argument (the common case). */
    public FunctionCall(String name, int argCount) {
        this(name, argCount, false);
    }

    /** The closing {@code ')'} was never found — arity is unknowable. */
    public static final int UNKNOWN_ARGS = -1;
}
