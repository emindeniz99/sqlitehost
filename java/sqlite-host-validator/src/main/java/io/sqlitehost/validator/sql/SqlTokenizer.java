package io.sqlitehost.validator.sql;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/**
 * Hand-written SQL tokenizer — the shared scanner algorithm pinned by
 * docs/errors.md: it skips string literals ({@code '…'} with {@code ''}
 * escapes) and quoted identifiers — double-quoted ({@code "…"} with
 * {@code ""} escapes), bracket ({@code [...]}, ends at the first
 * {@code ]}, no escape) and backtick ({@code `…`} with doubled-backtick
 * escapes) — plus line comments ({@code --}) and block comments
 * ({@code /* *}{@code /}), and recognizes named parameters written
 * {@code :name}, {@code @name}, or {@code $name}.
 * Positional ({@code ?}) parameters are not supported in v1 and come
 * out as punctuation.
 */
public final class SqlTokenizer {

    private SqlTokenizer() {
    }

    /** Two-character operators recognized as a single PUNCT token. */
    private static final String[] TWO_CHAR_OPERATORS = {"||", "<>", "<=", ">=", "==", "!=", ">>", "<<"};

    public static List<SqlToken> tokenize(String sql) {
        List<SqlToken> tokens = new ArrayList<>();
        int i = 0;
        int n = sql.length();
        while (i < n) {
            char c = sql.charAt(i);

            if (Character.isWhitespace(c)) {
                i++;
                continue;
            }

            // -- line comment
            if (c == '-' && i + 1 < n && sql.charAt(i + 1) == '-') {
                i += 2;
                while (i < n && sql.charAt(i) != '\n') {
                    i++;
                }
                continue;
            }

            // /* block comment */ (unterminated runs to end of input)
            if (c == '/' && i + 1 < n && sql.charAt(i + 1) == '*') {
                int end = sql.indexOf("*/", i + 2);
                i = end < 0 ? n : end + 2;
                continue;
            }

            // '…' string literal with '' escapes
            if (c == '\'') {
                StringBuilder value = new StringBuilder();
                i++;
                while (i < n) {
                    char ch = sql.charAt(i);
                    if (ch == '\'') {
                        if (i + 1 < n && sql.charAt(i + 1) == '\'') {
                            value.append('\'');
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    value.append(ch);
                    i++;
                }
                tokens.add(new SqlToken(SqlToken.Kind.STRING, value.toString()));
                continue;
            }

            // "…" quoted identifier with "" escapes
            if (c == '"') {
                StringBuilder value = new StringBuilder();
                i++;
                while (i < n) {
                    char ch = sql.charAt(i);
                    if (ch == '"') {
                        if (i + 1 < n && sql.charAt(i + 1) == '"') {
                            value.append('"');
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    value.append(ch);
                    i++;
                }
                tokens.add(new SqlToken(SqlToken.Kind.IDENT, value.toString()));
                continue;
            }

            // `…` backtick-quoted identifier (MySQL compat) with `` escapes
            if (c == '`') {
                StringBuilder value = new StringBuilder();
                i++;
                while (i < n) {
                    char ch = sql.charAt(i);
                    if (ch == '`') {
                        if (i + 1 < n && sql.charAt(i + 1) == '`') {
                            value.append('`');
                            i += 2;
                            continue;
                        }
                        i++;
                        break;
                    }
                    value.append(ch);
                    i++;
                }
                tokens.add(new SqlToken(SqlToken.Kind.IDENT, value.toString()));
                continue;
            }

            // [...] bracket-quoted identifier (MS Access/SQL Server
            // compat): no escape mechanism — ends at the first ']'
            if (c == '[') {
                StringBuilder value = new StringBuilder();
                i++;
                while (i < n && sql.charAt(i) != ']') {
                    value.append(sql.charAt(i));
                    i++;
                }
                if (i < n) {
                    i++; // consume ']'
                }
                tokens.add(new SqlToken(SqlToken.Kind.IDENT, value.toString()));
                continue;
            }

            // :name / @name / $name named parameter
            if (c == ':' || c == '@' || c == '$') {
                int start = i + 1;
                int end = start;
                while (end < n && isParamChar(sql.charAt(end))) {
                    end++;
                }
                if (end > start) {
                    tokens.add(new SqlToken(SqlToken.Kind.PARAM, sql.substring(start, end), c));
                    i = end;
                } else {
                    tokens.add(new SqlToken(SqlToken.Kind.PUNCT, String.valueOf(c)));
                    i++;
                }
                continue;
            }

            // numeric literal
            if (isDigit(c) || (c == '.' && i + 1 < n && isDigit(sql.charAt(i + 1)))) {
                int end = i;
                while (end < n && (isDigit(sql.charAt(end)) || sql.charAt(end) == '.')) {
                    end++;
                }
                // optional exponent: e / E [+|-] digits
                if (end < n && (sql.charAt(end) == 'e' || sql.charAt(end) == 'E')) {
                    int expStart = end + 1;
                    if (expStart < n && (sql.charAt(expStart) == '+' || sql.charAt(expStart) == '-')) {
                        expStart++;
                    }
                    if (expStart < n && isDigit(sql.charAt(expStart))) {
                        end = expStart;
                        while (end < n && isDigit(sql.charAt(end))) {
                            end++;
                        }
                    }
                }
                tokens.add(new SqlToken(SqlToken.Kind.NUMBER, sql.substring(i, end)));
                i = end;
                continue;
            }

            // bare identifier / keyword
            if (isIdentStart(c)) {
                int end = i + 1;
                while (end < n && isIdentPart(sql.charAt(end))) {
                    end++;
                }
                tokens.add(new SqlToken(SqlToken.Kind.IDENT, sql.substring(i, end)));
                i = end;
                continue;
            }

            // operators / punctuation
            String twoChar = i + 1 < n ? sql.substring(i, i + 2) : null;
            boolean matchedTwo = false;
            if (twoChar != null) {
                for (String op : TWO_CHAR_OPERATORS) {
                    if (op.equals(twoChar)) {
                        tokens.add(new SqlToken(SqlToken.Kind.PUNCT, op));
                        i += 2;
                        matchedTwo = true;
                        break;
                    }
                }
            }
            if (!matchedTwo) {
                tokens.add(new SqlToken(SqlToken.Kind.PUNCT, String.valueOf(c)));
                i++;
            }
        }
        return tokens;
    }

    /**
     * The named parameters referenced by the SQL, in first-appearance
     * order, with the prefix character stripped — the lexical scan
     * shared with the C# runtime and the TypeScript authoring lint.
     */
    public static Set<String> parameterNames(List<SqlToken> tokens) {
        Set<String> names = new LinkedHashSet<>();
        for (SqlToken token : tokens) {
            if (token.kind() == SqlToken.Kind.PARAM) {
                names.add(token.text());
            }
        }
        return names;
    }

    private static boolean isDigit(char c) {
        return c >= '0' && c <= '9';
    }

    private static boolean isIdentStart(char c) {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    private static boolean isIdentPart(char c) {
        return isIdentStart(c) || isDigit(c) || c == '$';
    }

    private static boolean isParamChar(char c) {
        return isIdentStart(c) || isDigit(c);
    }
}
