package io.sqlitehost.validator.sql;

import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** INSERT-shape parsing and static call_id comparison extraction. */
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
