package io.sqlitehost.validator.sql;

/**
 * One lexical token of a SQL statement. {@code text} holds the bare
 * parameter name for {@link Kind#PARAM} (prefix stripped), the literal
 * content for {@link Kind#STRING} (quotes removed, {@code ''} escapes
 * resolved), and the identifier text for {@link Kind#IDENT} (unquoted
 * for double-quoted, bracket and backtick identifiers). {@code prefix}
 * is the parameter
 * prefix character ({@code ':'}, {@code '@'}, or {@code '$'}) for
 * {@link Kind#PARAM} and {@code '\0'} for every other kind.
 *
 * <p>{@code delimited} distinguishes {@code "x"} / {@code [x]} / {@code `x`}
 * from a bare {@code x}. Both are {@link Kind#IDENT} because SQLite resolves
 * them to the same name, so almost every rule wants them treated alike — but
 * a KEYWORD cannot be written delimited: {@code CURRENT_DATE} reads the
 * clock while {@code "current_date"} is a column reference (or, under
 * SQLite's double-quote fallback, a string literal). The determinism lint is
 * the one place that has to tell them apart.</p>
 */
public record SqlToken(Kind kind, String text, char prefix, boolean delimited) {

    public SqlToken(Kind kind, String text) {
        this(kind, text, '\0', false);
    }

    public SqlToken(Kind kind, String text, char prefix) {
        this(kind, text, prefix, false);
    }

    /** A delimited identifier: double-quoted, bracketed or backtick-quoted. */
    public static SqlToken delimitedIdent(String text) {
        return new SqlToken(Kind.IDENT, text, '\0', true);
    }

    public enum Kind {
        /** Bare, double-quoted, bracket or backtick identifier / keyword. */
        IDENT,
        /** Named parameter ({@code :name}, {@code @name}, {@code $name}). */
        PARAM,
        /** Single-quoted string literal. */
        STRING,
        /** Numeric literal. */
        NUMBER,
        /** Operator or punctuation ({@code ( ) , = || <> …}). */
        PUNCT
    }

    /** Case-insensitive identifier/keyword match (SQL identifiers are). */
    public boolean isIdent(String name) {
        return kind == Kind.IDENT && text.equalsIgnoreCase(name);
    }

    public boolean isPunct(String symbol) {
        return kind == Kind.PUNCT && text.equals(symbol);
    }
}
