package io.sqlitehost.validator.sql;

import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * The shared scanner semantics pinned by docs/errors.md: named
 * parameters with : @ $ prefixes are found, while string literals
 * (with '' escapes), double-quoted identifiers, and both comment
 * styles are skipped.
 */
class SqlTokenizerTest {

    private static Set<String> params(String sql) {
        return SqlTokenizer.parameterNames(SqlTokenizer.tokenize(sql));
    }

    @Test
    void findsAllThreeParameterPrefixes() {
        assertEquals(Set.of("a", "b", "c"),
                params("SELECT :a, @b, $c"));
    }

    @Test
    void bindingNamesAreBare() {
        List<SqlToken> tokens = SqlTokenizer.tokenize("SELECT :callId");
        assertEquals(new SqlToken(SqlToken.Kind.PARAM, "callId", ':'), tokens.get(1));
    }

    @Test
    void parameterTokensRetainTheirPrefixCharacter() {
        List<SqlToken> tokens = SqlTokenizer.tokenize("SELECT :a, @b, $c");
        assertEquals(List.of(':', '@', '$'), tokens.stream()
                .filter(t -> t.kind() == SqlToken.Kind.PARAM)
                .map(SqlToken::prefix)
                .toList());
    }

    @Test
    void skipsParametersInsideStringLiterals() {
        assertEquals(Set.of("real"),
                params("SELECT ':fake', :real, 'and :another'"));
    }

    @Test
    void handlesEscapedQuotesInsideStringLiterals() {
        // 'it''s :not a param' is one literal with an escaped quote.
        assertEquals(Set.of("yes"),
                params("SELECT 'it''s :not a param', :yes"));
        List<SqlToken> tokens = SqlTokenizer.tokenize("SELECT 'it''s'");
        assertEquals(new SqlToken(SqlToken.Kind.STRING, "it's"), tokens.get(1));
    }

    @Test
    void skipsParametersInsideDoubleQuotedIdentifiers() {
        assertEquals(Set.of("p"),
                params("SELECT \":notaparam\" FROM t WHERE x = :p"));
        List<SqlToken> tokens = SqlTokenizer.tokenize("SELECT \"weird \"\"name\"\"\"");
        assertEquals(SqlToken.delimitedIdent("weird \"name\""), tokens.get(1));
    }

    @Test
    void delimitedIdentifiersAreMarkedAsSuch() {
        // Both spellings resolve to the same NAME, so the kind stays IDENT
        // and every name-matching rule keeps treating them alike. The flag
        // exists for the one thing that differs: a KEYWORD cannot be
        // delimited. CURRENT_DATE reads the clock; "current_date" is a
        // column reference (or, under SQLite's double-quote fallback, a
        // string literal), and the determinism lint must not confuse them.
        for (String sql : List.of("\"current_date\"", "[current_date]", "`current_date`")) {
            SqlToken token = SqlTokenizer.tokenize(sql).get(0);
            assertEquals(SqlToken.Kind.IDENT, token.kind(), sql);
            assertEquals("current_date", token.text(), sql);
            assertTrue(token.delimited(), sql);
        }
        SqlToken bare = SqlTokenizer.tokenize("current_date").get(0);
        assertEquals(SqlToken.Kind.IDENT, bare.kind());
        assertFalse(bare.delimited());
    }

    @Test
    void lexesBracketQuotedIdentifiers() {
        // [id] (MS Access/SQL Server compat) lexes as an IDENT with the
        // inner text — no escape mechanism, so it ends at the first ']'.
        List<SqlToken> tokens = SqlTokenizer.tokenize("[call_get_value]");
        assertEquals(SqlToken.delimitedIdent("call_get_value"), tokens.get(0));
        List<SqlToken> weird = SqlTokenizer.tokenize("[weird ]x]");
        assertEquals(SqlToken.delimitedIdent("weird "), weird.get(0));
    }

    @Test
    void lexesBacktickQuotedIdentifiersWithDoubledEscapes() {
        // `id` (MySQL compat) lexes as an IDENT; `` is one literal backtick.
        List<SqlToken> tokens = SqlTokenizer.tokenize("`call_get_value`");
        assertEquals(SqlToken.delimitedIdent("call_get_value"), tokens.get(0));
        List<SqlToken> escaped = SqlTokenizer.tokenize("`a``b`");
        assertEquals(SqlToken.delimitedIdent("a`b"), escaped.get(0));
    }

    @Test
    void skipsParametersInsideBracketAndBacktickIdentifiers() {
        // A :name inside a quoted identifier is not a parameter — the
        // narrower binding-scan half of the shared-scanner fix.
        assertEquals(Set.of("real"), params("SELECT [:notaparam] FROM t WHERE x = :real"));
        assertEquals(Set.of("real"), params("SELECT `:notaparam` FROM t WHERE x = :real"));
    }

    @Test
    void skipsLineComments() {
        assertEquals(Set.of("used"),
                params("SELECT :used -- :ignored in comment\nFROM t"));
    }

    @Test
    void skipsBlockComments() {
        assertEquals(Set.of("used"),
                params("SELECT /* :ignored \n :also */ :used"));
    }

    @Test
    void unterminatedBlockCommentRunsToEnd() {
        assertEquals(Set.of(), params("SELECT /* :never"));
    }

    @Test
    void concatOperatorIsOneToken() {
        List<SqlToken> tokens = SqlTokenizer.tokenize("SELECT 'w-' || result_key");
        assertTrue(tokens.contains(new SqlToken(SqlToken.Kind.PUNCT, "||")));
    }

    @Test
    void numbersAndIdentifiersTokenize() {
        List<SqlToken> tokens = SqlTokenizer.tokenize("WHERE result_value <> 42");
        assertEquals(List.of(
                new SqlToken(SqlToken.Kind.IDENT, "WHERE"),
                new SqlToken(SqlToken.Kind.IDENT, "result_value"),
                new SqlToken(SqlToken.Kind.PUNCT, "<>"),
                new SqlToken(SqlToken.Kind.NUMBER, "42")), tokens);
    }

    @Test
    void parameterNamesRunOverSqlitesFullIdCharSet() {
        // SQLite's IdChar counts '$' and every character above 0x7f, and
        // the adapter conformance suite requires a non-ASCII parameter name
        // to bind as written. Cutting the name at its ASCII head made the
        // validator report missing-binding for a name nobody wrote.
        assertEquals(Set.of("anahtarİsmi", "слово"),
                params("INSERT INTO t (a, b) VALUES (:anahtarİsmi, @слово)"));
        assertEquals(Set.of("a$b"), params("SELECT :a$b"));
    }

    @Test
    void parameterNamesCarrySqlitesTclVariableSuffixes() {
        // SQLite's variable syntax admits a doubled colon inside the name
        // and one trailing '(...)' group, for every prefix — verified
        // against the sqlite3 CLI 3.51.0, where `SELECT :a::b` reports a
        // missing value for the binding parameter `:a::b`, not for `:a`.
        // Splitting them accepted the payload that fails on every device
        // and rejected (missing-binding x2 + unused-binding) the only one
        // that runs.
        assertEquals(Set.of("a::b"), params("SELECT :a::b"));
        assertEquals(Set.of("x::y"), params("SELECT @x::y"));
        assertEquals(Set.of("a::b::c"), params("SELECT :a::b::c"));
        assertEquals(Set.of("a::"), params("SELECT :a::"));
        assertEquals(Set.of("a(1)"), params("SELECT $a(1)"));
        assertEquals(Set.of("a(1)"), params("SELECT :a(1)"));
        assertEquals(Set.of("a()"), params("SELECT $a()"));
        assertEquals(Set.of("a::b(1)"), params("SELECT $a::b(1)"));
    }

    @Test
    void neighbouringVariableFormsSqliteRejectsStayRejected() {
        // Each of these is a tokenizer error in the engine, so no scanner
        // reading may invent a well-formed parameter out of them: `:a:b` is
        // two adjacent variables (a parse error), `::a` and `$(1)` have no
        // IdChar to name, and a '(' group that does not close on the same
        // token is illegal. Only `:a:b` yields parameters, exactly as the
        // engine's tokenizer does before the parser rejects the pair.
        assertEquals(Set.of("a", "b"), params("SELECT :a:b"));
        assertEquals(Set.of("a"), params("SELECT ::a"));
        assertEquals(Set.of(), params("SELECT $(1)"));
        assertEquals(Set.of(), params("SELECT $a( 1)"));
        assertEquals(Set.of(), params("SELECT $a(1"));
    }

    @Test
    void lonePrefixCharacterIsPunctuation() {
        List<SqlToken> tokens = SqlTokenizer.tokenize("SELECT a : b");
        assertTrue(tokens.contains(new SqlToken(SqlToken.Kind.PUNCT, ":")));
    }

    @Test
    void separatesTokensOnExactlyTheCIsspaceSet() {
        // WHY: whitespace decides where one token ends and the next begins,
        // so the Java and TypeScript tokenizers must skip the SAME bytes or
        // their whole statement analysis diverges. Both now enumerate C's
        // isspace() set rather than delegating to a library predicate:
        // Character.isWhitespace also accepts U+001C..U+001F and the Unicode
        // separators, which the TypeScript scanner has no equivalent for.
        for (char c : new char[] {' ', '\t', '\n', '\u000B', '\f', '\r'}) {
            assertEquals(
                    List.of(new SqlToken(SqlToken.Kind.IDENT, "DROP"),
                            new SqlToken(SqlToken.Kind.IDENT, "TABLE")),
                    SqlTokenizer.tokenize("DROP" + c + "TABLE"),
                    "U+" + String.format("%04X", (int) c) + " must separate tokens");
        }
        // Outside that set the character is not whitespace to SQLite and is
        // not silently swallowed here either: U+2028 becomes a PUNCT token,
        // exactly as it does in the TypeScript tokenizer. The leading
        // keyword — the anchor of the forbidden-statement lint — survives
        // either way, and the SQL itself can never prepare.
        assertEquals(
                List.of(new SqlToken(SqlToken.Kind.IDENT, "DROP"),
                        new SqlToken(SqlToken.Kind.PUNCT, "\u2028"),
                        new SqlToken(SqlToken.Kind.IDENT, "TABLE")),
                SqlTokenizer.tokenize("DROP\u2028TABLE"));
    }
}
