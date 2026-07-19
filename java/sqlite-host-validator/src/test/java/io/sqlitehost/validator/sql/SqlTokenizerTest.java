package io.sqlitehost.validator.sql;

import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Set;

import static org.junit.jupiter.api.Assertions.assertEquals;
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
        assertEquals(new SqlToken(SqlToken.Kind.IDENT, "weird \"name\""), tokens.get(1));
    }

    @Test
    void lexesBracketQuotedIdentifiers() {
        // [id] (MS Access/SQL Server compat) lexes as an IDENT with the
        // inner text — no escape mechanism, so it ends at the first ']'.
        List<SqlToken> tokens = SqlTokenizer.tokenize("[call_get_value]");
        assertEquals(new SqlToken(SqlToken.Kind.IDENT, "call_get_value"), tokens.get(0));
        List<SqlToken> weird = SqlTokenizer.tokenize("[weird ]x]");
        assertEquals(new SqlToken(SqlToken.Kind.IDENT, "weird "), weird.get(0));
    }

    @Test
    void lexesBacktickQuotedIdentifiersWithDoubledEscapes() {
        // `id` (MySQL compat) lexes as an IDENT; `` is one literal backtick.
        List<SqlToken> tokens = SqlTokenizer.tokenize("`call_get_value`");
        assertEquals(new SqlToken(SqlToken.Kind.IDENT, "call_get_value"), tokens.get(0));
        List<SqlToken> escaped = SqlTokenizer.tokenize("`a``b`");
        assertEquals(new SqlToken(SqlToken.Kind.IDENT, "a`b"), escaped.get(0));
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
    void lonePrefixCharacterIsPunctuation() {
        List<SqlToken> tokens = SqlTokenizer.tokenize("SELECT a : b");
        assertTrue(tokens.contains(new SqlToken(SqlToken.Kind.PUNCT, ":")));
    }
}
