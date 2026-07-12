package io.sqlitehost.validator.sql;

/**
 * One {@code identifier(...)} call extracted from the token stream.
 * {@code argCount} is the number of top-level arguments, or
 * {@link #UNKNOWN_ARGS} when the matching {@code ')'} is missing
 * (malformed SQL — prepare-only validation reports it).
 */
public record FunctionCall(String name, int argCount) {

    /** The closing {@code ')'} was never found — arity is unknowable. */
    public static final int UNKNOWN_ARGS = -1;
}
