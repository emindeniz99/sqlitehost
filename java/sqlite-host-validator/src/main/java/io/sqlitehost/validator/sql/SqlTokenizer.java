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

            if (isSqlWhitespace(c)) {
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

            // :name / @name / $name named parameter — SQLite's variable
            // grammar (see the isParamChar javadoc for the two suffix forms)
            if (c == ':' || c == '@' || c == '$') {
                int end = i + 1;
                int idChars = 0;
                boolean illegal = false;
                while (end < n) {
                    char ch = sql.charAt(end);
                    if (isParamChar(ch)) {
                        idChars++;
                        end++;
                    } else if (ch == '(' && idChars > 0) {
                        // A single trailing '(...)' group closes the name.
                        end++;
                        while (end < n && sql.charAt(end) != ')' && !isSqlWhitespace(sql.charAt(end))) {
                            end++;
                        }
                        if (end < n && sql.charAt(end) == ')') {
                            end++;
                        } else {
                            illegal = true;
                        }
                        break;
                    } else if (ch == ':' && end + 1 < n && sql.charAt(end + 1) == ':') {
                        end += 2;
                    } else {
                        break;
                    }
                }
                if (illegal) {
                    // SQLite lexes an unterminated '(' group as one illegal
                    // token; consume the same span so the stray '(' cannot
                    // unbalance the paren depth the statement analysis tracks.
                    tokens.add(new SqlToken(SqlToken.Kind.PUNCT, sql.substring(i, end)));
                    i = end;
                } else if (idChars > 0) {
                    tokens.add(new SqlToken(SqlToken.Kind.PARAM, sql.substring(i + 1, end), c));
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

    /**
     * The characters that separate tokens: C's {@code isspace()} set —
     * space, {@code \t}, {@code \n}, {@code U+000B}, {@code \f}, {@code \r}.
     * The TypeScript tokenizer enumerates the identical set, and that
     * agreement is the point: whitespace decides where one token ends, so a
     * byte one validator skips and the other does not makes the whole
     * statement analysis (the denylist included) diverge between them.
     * {@link Character#isWhitespace} was the earlier spelling and is wider —
     * it also accepts {@code U+001C..U+001F} and the Unicode separators,
     * which the TypeScript scanner has no equivalent for.
     *
     * <p>The set is deliberately one character wider than SQLite's own
     * {@code sqlite3Isspace}, which omits {@code U+000B} — verified against
     * the sqlite3 CLI 3.51.0, where an INSERT split by a vertical tab is a
     * parse error while the form-feed version runs. Over-skipping is the
     * fail-safe direction: the extra character can only appear in SQL SQLite
     * refuses to prepare, so treating it as a separator costs no valid
     * script a false positive, while not skipping it hides a denied
     * statement from the lint.</p>
     */
    private static boolean isSqlWhitespace(char c) {
        return c == ' ' || c == '\t' || c == '\n' || c == 0x0b || c == '\f' || c == '\r';
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

    /**
     * SQLite's IdChar, which a parameter name runs over: letters, digits,
     * '_', '$', and any character above 0x7f. Cutting the name at its ASCII
     * head would report missing-binding for a name the author never wrote,
     * and the adapter conformance suite requires non-ASCII names to bind.
     *
     * <p>Two further characters can appear <em>inside</em> a name and are
     * handled by the scan loop rather than here, because they are only legal
     * in position: a doubled colon ({@code :a::b}) and one trailing
     * parenthesised group ({@code $a(1)}). Both come from SQLite's TCL
     * variable syntax, are compiled in by default, and apply to every prefix
     * — verified against the sqlite3 CLI 3.51.0, where {@code :a::b} and
     * {@code $a(1)} each bind as a <em>single</em> parameter whose name
     * carries the suffix. Splitting them, as this scanner used to, accepts
     * the payload that fails on every device and rejects the only one that
     * runs.</p>
     */
    private static boolean isParamChar(char c) {
        return isIdentPart(c) || c > 0x7f;
    }
}
