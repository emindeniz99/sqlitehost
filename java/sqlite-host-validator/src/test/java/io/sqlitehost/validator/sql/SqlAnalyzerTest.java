package io.sqlitehost.validator.sql;

import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
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
