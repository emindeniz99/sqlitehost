package io.sqlitehost.validator.sql;

/**
 * A classified value expression feeding one column of an INSERT (a
 * VALUES cell or a top-level SELECT item). Single-token parameters,
 * string literals, numbers, and NULL are recognized; anything else is
 * a computed expression ({@link Kind#OTHER}).
 */
public record ValueExpr(Kind kind, String text) {

    public enum Kind {
        /** A single named parameter; {@code text} is the bare name. */
        PARAM,
        /** A single string literal; {@code text} is the literal value. */
        STRING,
        /** A single numeric literal; {@code text} is the literal text. */
        NUMBER,
        /** The NULL keyword. */
        NULL,
        /** Any other (computed) expression — skipped by static resolution. */
        OTHER
    }

    static ValueExpr classify(java.util.List<SqlToken> tokens) {
        if (tokens.size() == 1) {
            SqlToken token = tokens.get(0);
            switch (token.kind()) {
                case PARAM:
                    return new ValueExpr(Kind.PARAM, token.text());
                case STRING:
                    return new ValueExpr(Kind.STRING, token.text());
                case NUMBER:
                    return new ValueExpr(Kind.NUMBER, token.text());
                case IDENT:
                    if (token.isIdent("NULL")) {
                        return new ValueExpr(Kind.NULL, "NULL");
                    }
                    break;
                default:
                    break;
            }
        }
        return new ValueExpr(Kind.OTHER, "");
    }
}
