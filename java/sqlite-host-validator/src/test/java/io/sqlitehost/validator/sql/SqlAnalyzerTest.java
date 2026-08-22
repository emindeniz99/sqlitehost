package io.sqlitehost.validator.sql;

import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Locale;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * INSERT-shape parsing, function-call extraction, and static call_id
 * comparison extraction.
 */
class SqlAnalyzerTest {

    private static InsertStatement insert(String sql) {
        return SqlAnalyzer.parseInsert(SqlTokenizer.tokenize(sql));
    }

    @Test
    void parsesInsertWithColumnsAndValues() {
        InsertStatement parsed = insert(
                "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'k')");
        assertEquals("call_get_value", parsed.table());
        assertEquals(List.of("call_id", "input_key"), parsed.columns());
        assertEquals(1, parsed.rows().size());
        assertEquals(new ValueExpr(ValueExpr.Kind.PARAM, "callId"), parsed.rows().get(0).get(0));
        assertEquals(new ValueExpr(ValueExpr.Kind.STRING, "k"), parsed.rows().get(0).get(1));
    }

    @Test
    void parsesMultiRowValues() {
        InsertStatement parsed = insert(
                "INSERT INTO t (call_id, item_index, input_key)"
                        + " VALUES (:c, 0, 'alpha'), (:c, 1, 'beta')");
        assertEquals(2, parsed.rows().size());
        assertEquals(new ValueExpr(ValueExpr.Kind.NUMBER, "1"), parsed.rows().get(1).get(1));
        assertEquals(new ValueExpr(ValueExpr.Kind.STRING, "beta"), parsed.rows().get(1).get(2));
    }

    @Test
    void parsesInsertOrReplaceIntoWithColumnList() {
        // The OR REPLACE conflict clause sits between INSERT and INTO —
        // the target table and column list must still be located.
        InsertStatement parsed = insert(
                "INSERT OR REPLACE INTO call_get_value (call_id, input_key)"
                        + " VALUES (:callId, 'k')");
        assertEquals("call_get_value", parsed.table());
        assertEquals(List.of("call_id", "input_key"), parsed.columns());
        assertEquals(new ValueExpr(ValueExpr.Kind.PARAM, "callId"), parsed.rows().get(0).get(0));
    }

    @Test
    void parsesInsertOrReplaceIntoScriptVarsSelect() {
        // script_vars reassignment form (docs/workspace-schema.md):
        // INSERT OR REPLACE INTO … SELECT with scalar subqueries.
        InsertStatement parsed = insert(
                "INSERT OR REPLACE INTO script_vars (name, value_type, int_value)"
                        + " SELECT 'total', 'int64',"
                        + " (SELECT int_value FROM script_vars WHERE name = 'aa')");
        assertEquals("script_vars", parsed.table());
        assertEquals(List.of("name", "value_type", "int_value"), parsed.columns());
        assertEquals(3, parsed.selectItems().size());
        assertEquals(new ValueExpr(ValueExpr.Kind.STRING, "total"), parsed.selectItems().get(0));
        assertEquals(ValueExpr.Kind.OTHER, parsed.selectItems().get(2).kind());
    }

    @Test
    void detectsImplicitColumnList() {
        InsertStatement parsed = insert("INSERT INTO call_get_value VALUES (:c, 'k')");
        assertNull(parsed.columns());
        assertEquals(1, parsed.rows().size());
    }

    @Test
    void parsesInsertIntoBracketQuotedTable() {
        // [call_get_value] (MS Access/SQL Server compat) resolves to the
        // same table name so the host-call checks are not bypassed.
        InsertStatement parsed = insert(
                "INSERT INTO [call_get_value] (call_id, input_key) VALUES (:callId, 'k')");
        assertEquals("call_get_value", parsed.table());
        assertEquals(List.of("call_id", "input_key"), parsed.columns());
        assertEquals(new ValueExpr(ValueExpr.Kind.PARAM, "callId"), parsed.rows().get(0).get(0));
    }

    @Test
    void parsesInsertIntoBacktickQuotedTable() {
        // `call_get_value` (MySQL compat) resolves the same way.
        InsertStatement parsed = insert(
                "INSERT INTO `call_get_value` (call_id, input_key) VALUES (:callId, 'k')");
        assertEquals("call_get_value", parsed.table());
        assertEquals(List.of("call_id", "input_key"), parsed.columns());
    }

    @Test
    void recognizesBracketQuotedCallIdColumnInComparisons() {
        // [call_id] = :x lexes as an IDENT, so the call-id comparison is
        // still extracted for result-read lineage.
        List<ValueExpr> comparisons = SqlAnalyzer.callIdComparisons(SqlTokenizer.tokenize(
                "SELECT 1 FROM result_get_value WHERE [call_id] = :x"), "call_id");
        assertEquals(List.of(new ValueExpr(ValueExpr.Kind.PARAM, "x")), comparisons);
    }

    @Test
    void parsesInsertWithAsAliasBeforeColumnList() {
        // SQLite >= 3.24.0 INSERT INTO t AS c (cols) …: the alias must be
        // skipped so the explicit column list (and its values) survive.
        InsertStatement parsed = insert(
                "INSERT INTO call_get_value AS c (call_id, input_key) VALUES (:callId, 'k')");
        assertEquals("call_get_value", parsed.table());
        assertEquals(List.of("call_id", "input_key"), parsed.columns());
        assertEquals(new ValueExpr(ValueExpr.Kind.PARAM, "callId"), parsed.rows().get(0).get(0));
        assertEquals(new ValueExpr(ValueExpr.Kind.STRING, "k"), parsed.rows().get(0).get(1));
    }

    @Test
    void parsesSchemaQualifiedInsertWithAsAlias() {
        InsertStatement parsed = insert(
                "INSERT INTO main.call_get_value AS c (call_id, input_key) VALUES (:callId, 'k')");
        assertEquals("call_get_value", parsed.table());
        assertEquals(List.of("call_id", "input_key"), parsed.columns());
    }

    @Test
    void mapsFirstSelectItemsToColumns() {
        InsertStatement parsed = insert(
                "INSERT INTO call_set_value (call_id, input_key, input_value)"
                        + " SELECT :callId, 'k', 42 WHERE EXISTS"
                        + " (SELECT 1 FROM result_get_value WHERE call_id = :read)");
        assertEquals(List.of(
                new ValueExpr(ValueExpr.Kind.PARAM, "callId"),
                new ValueExpr(ValueExpr.Kind.STRING, "k"),
                new ValueExpr(ValueExpr.Kind.NUMBER, "42")), parsed.selectItems());
        // The subquery's :read must not leak into the select items.
        assertEquals(3, parsed.selectItems().size());
    }

    @Test
    void selectItemTerminatorsSurviveATurkishLocale() {
        // "UNION" lowercases to "unıon" (dotless i) under a Turkish
        // locale, so a locale-sensitive toLowerCase() misses the clause
        // boundary, folds the second SELECT into the item list, and the
        // lints that read the items (duplicate call ids, list-child
        // colocation, result-read lineage) fail open.
        Locale previous = Locale.getDefault();
        Locale.setDefault(Locale.forLanguageTag("tr-TR"));
        try {
            InsertStatement parsed = insert(
                    "INSERT INTO call_set_value (call_id, input_key)"
                            + " SELECT :callId, 'k' UNION SELECT :other, 'j'");
            assertEquals(List.of(
                    new ValueExpr(ValueExpr.Kind.PARAM, "callId"),
                    new ValueExpr(ValueExpr.Kind.STRING, "k")), parsed.selectItems());
        } finally {
            Locale.setDefault(previous);
        }
    }

    @Test
    void computedSelectItemIsOther() {
        InsertStatement parsed = insert(
                "INSERT INTO call_set_value (call_id, input_key, input_value)"
                        + " SELECT 'w-' || result_key, result_key, :f FROM result_child");
        assertEquals(ValueExpr.Kind.OTHER, parsed.selectItems().get(0).kind());
        assertEquals(ValueExpr.Kind.PARAM, parsed.selectItems().get(2).kind());
    }

    @Test
    void nullLiteralIsRecognized() {
        InsertStatement parsed = insert("INSERT INTO t (call_id, x) VALUES (:c, NULL)");
        assertEquals(ValueExpr.Kind.NULL, parsed.rows().get(0).get(1).kind());
    }

    @Test
    void nonInsertReturnsNull() {
        assertNull(insert("SELECT 1"));
    }

    @Test
    void extractsFunctionCallsWithArgCounts() {
        List<FunctionCall> calls = SqlAnalyzer.functionCalls(SqlTokenizer.tokenize(
                "SELECT fn_get_value('k') * 2 WHERE fn_get_value('k') <> 42"));
        assertEquals(List.of(new FunctionCall("fn_get_value", 1),
                new FunctionCall("fn_get_value", 1)), calls);
    }

    @Test
    void countsTopLevelArgsThroughNestedParensAndStrings() {
        // The nested max(...) call is extracted as its own entry; its
        // commas and the comma inside the string literal don't count
        // toward fn_check's top-level argument scan.
        List<FunctionCall> calls = SqlAnalyzer.functionCalls(SqlTokenizer.tokenize(
                "SELECT fn_check(1, max(2, 3), 'a,b')"));
        assertTrue(calls.contains(new FunctionCall("fn_check", 3)), calls.toString());
        assertTrue(calls.contains(new FunctionCall("max", 2)), calls.toString());
    }

    @Test
    void stringArgumentWithCommasIsOneArgument() {
        assertEquals(List.of(new FunctionCall("fn_x", 1)),
                SqlAnalyzer.functionCalls(SqlTokenizer.tokenize("SELECT fn_x('a, b, c')")));
    }

    @Test
    void zeroArgCallHasZeroArgs() {
        assertEquals(List.of(new FunctionCall("fn_now", 0)),
                SqlAnalyzer.functionCalls(SqlTokenizer.tokenize("SELECT fn_now()")));
    }

    @Test
    void bareIdentifiersAreNotCalls() {
        assertEquals(List.of(),
                SqlAnalyzer.functionCalls(SqlTokenizer.tokenize(
                        "SELECT result_value FROM result_get_value")));
    }

    @Test
    void unterminatedCallHasUnknownArgs() {
        assertEquals(List.of(new FunctionCall("fn_x", FunctionCall.UNKNOWN_ARGS)),
                SqlAnalyzer.functionCalls(SqlTokenizer.tokenize("SELECT fn_x(1, 2")));
    }

    @Test
    void hasNowArgIsSetOnlyByABareTopLevelNowLiteral() {
        // The determinism lint keys on this flag, so it must distinguish
        // the wall-clock form from a reproducible one: 'now' as a whole
        // argument (in any position, any case) reads the clock; a
        // parameter, a different literal, or a 'now' buried in a larger
        // expression or a nested call does not.
        assertTrue(nowArg("SELECT datetime('now')"));
        assertTrue(nowArg("SELECT strftime('%Y', 'NOW')"));
        assertTrue(nowArg("SELECT datetime('now', '+1 day')"));
        assertFalse(nowArg("SELECT datetime('2020-01-01')"));
        assertFalse(nowArg("SELECT date(:day)"));
        assertFalse(nowArg("SELECT date('now' || '')"));
        assertFalse(nowArg("SELECT date(coalesce('now', ''))"));
    }

    private static boolean nowArg(String sql) {
        return SqlAnalyzer.functionCalls(SqlTokenizer.tokenize(sql)).get(0).hasNowArg();
    }

    @Test
    void extractsCallIdComparisons() {
        List<ValueExpr> comparisons = SqlAnalyzer.callIdComparisons(SqlTokenizer.tokenize(
                "SELECT 1 FROM result_get_value WHERE call_id = 'read-1' AND status = 'done'"),
                "call_id");
        assertEquals(List.of(new ValueExpr(ValueExpr.Kind.STRING, "read-1")), comparisons);
    }

    @Test
    void extractsParameterComparisonAndReverseForm() {
        List<ValueExpr> comparisons = SqlAnalyzer.callIdComparisons(SqlTokenizer.tokenize(
                "SELECT 1 FROM r WHERE call_id = :x OR 'y-1' = call_id"), "call_id");
        assertTrue(comparisons.contains(new ValueExpr(ValueExpr.Kind.PARAM, "x")));
        assertTrue(comparisons.contains(new ValueExpr(ValueExpr.Kind.STRING, "y-1")));
    }

    @Test
    void concatenatedComparisonIsNotStatic() {
        List<ValueExpr> comparisons = SqlAnalyzer.callIdComparisons(SqlTokenizer.tokenize(
                "SELECT 1 FROM r WHERE call_id = 'w-' || result_key"), "call_id");
        assertTrue(comparisons.isEmpty());
    }

    @Test
    void hasTrailingStatementDetectsOnlyATopLevelSeparatorWithSqlAfterIt() {
        // A top-level ';' with another statement after it is multi-statement…
        assertTrue(trailing("SELECT 1; PRAGMA writable_schema = ON"));
        assertTrue(trailing("SELECT 1; SELECT 2"));
        assertTrue(trailing("SELECT (SELECT 1); SELECT 2"));
        // …but a bare trailing ';' terminates a single statement — legal.
        assertFalse(trailing("SELECT 1"));
        assertFalse(trailing("SELECT 1;"));
        // The tokenizer already collapsed strings and comments, so a ';'
        // living inside either is not a separator token here.
        assertFalse(trailing("SELECT ';'"));
        assertFalse(trailing("SELECT ';;; not sql'"));
        assertFalse(trailing("SELECT 1 -- ; x"));
        assertFalse(trailing("SELECT 1 /* ; */ + 1"));
    }

    private static boolean trailing(String sql) {
        return SqlAnalyzer.hasTrailingStatement(SqlTokenizer.tokenize(sql));
    }

    @Test
    void comparisonExtractionKeysOnTheManifestCallIdColumn() {
        // Custom call-id column 'cid': cid comparisons are extracted…
        List<ValueExpr> custom = SqlAnalyzer.callIdComparisons(SqlTokenizer.tokenize(
                "SELECT 1 FROM result_get_value WHERE cid = 'read-1'"), "cid");
        assertEquals(List.of(new ValueExpr(ValueExpr.Kind.STRING, "read-1")), custom);
        // …and a literal 'call_id' identifier is no longer special.
        List<ValueExpr> defaultName = SqlAnalyzer.callIdComparisons(SqlTokenizer.tokenize(
                "SELECT 1 FROM result_get_value WHERE call_id = 'read-1'"), "cid");
        assertTrue(defaultName.isEmpty());
    }
}
