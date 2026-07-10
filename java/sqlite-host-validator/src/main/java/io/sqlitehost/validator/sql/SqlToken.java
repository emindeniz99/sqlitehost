package io.sqlitehost.validator.sql;

/**
 * One lexical token of a SQL statement. {@code text} holds the bare
 * parameter name for {@link Kind#PARAM} (prefix stripped), the literal
 * content for {@link Kind#STRING} (quotes removed, {@code ''} escapes
 * resolved), and the identifier text for {@link Kind#IDENT} (unquoted
 * for double-quoted identifiers).
 */
public record SqlToken(Kind kind, String text) {

    public enum Kind {
        /** Bare or double-quoted identifier / keyword. */
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
