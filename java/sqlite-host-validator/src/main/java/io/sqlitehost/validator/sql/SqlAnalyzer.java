package io.sqlitehost.validator.sql;

import java.util.ArrayList;
import java.util.List;
import java.util.Set;

/**
 * Statement-shape analysis over the token stream: INSERT parsing and
 * {@code <callIdColumn> = <atom>} comparison extraction (the call-id
 * column name comes from the manifest columns block). Best-effort
 * static analysis for lint purposes (docs/validation.md) — not a SQL
 * parser.
 */
public final class SqlAnalyzer {

    private static final Set<String> SELECT_ITEM_TERMINATORS =
            Set.of("from", "where", "group", "order", "limit", "union", "except", "intersect");

    private SqlAnalyzer() {
    }

    /**
     * Parse the statement as an INSERT, or return {@code null} when it
     * is not one. Handles {@code INSERT [OR …] INTO <table>} with an
     * optional column list, followed by {@code VALUES (…) [, (…)]…},
     * {@code SELECT …} (first top-level items map to the column list),
     * or {@code DEFAULT VALUES}.
     */
    public static InsertStatement parseInsert(List<SqlToken> tokens) {
        int i = indexOfIdent(tokens, "insert");
        if (i < 0) {
            return null;
        }
        // Skip conflict clause idents (OR REPLACE / OR IGNORE / …) up to INTO.
        int into = -1;
        for (int j = i + 1; j < tokens.size() && j <= i + 3; j++) {
            if (tokens.get(j).isIdent("into")) {
                into = j;
                break;
            }
            if (tokens.get(j).kind() != SqlToken.Kind.IDENT) {
                return null;
            }
        }
        if (into < 0 || into + 1 >= tokens.size()) {
            return null;
        }
        int pos = into + 1;
        if (tokens.get(pos).kind() != SqlToken.Kind.IDENT) {
            return null;
        }
        String table = tokens.get(pos).text();
        pos++;
        // Schema-qualified name: keep the last component.
        while (pos + 1 < tokens.size()
                && tokens.get(pos).isPunct(".")
                && tokens.get(pos + 1).kind() == SqlToken.Kind.IDENT) {
            table = tokens.get(pos + 1).text();
            pos += 2;
        }

        List<String> columns = null;
        if (pos < tokens.size() && tokens.get(pos).isPunct("(")) {
            columns = new ArrayList<>();
            pos++;
            while (pos < tokens.size() && !tokens.get(pos).isPunct(")")) {
                if (tokens.get(pos).kind() == SqlToken.Kind.IDENT) {
                    columns.add(tokens.get(pos).text());
                }
                pos++;
            }
            if (pos < tokens.size()) {
                pos++; // consume ')'
            }
        }

        if (pos < tokens.size() && tokens.get(pos).isIdent("values")) {
            return new InsertStatement(table, columns, parseValuesRows(tokens, pos + 1), null);
        }
        if (pos < tokens.size() && tokens.get(pos).isIdent("select")) {
            return new InsertStatement(table, columns, null, parseSelectItems(tokens, pos + 1));
        }
        if (pos + 1 < tokens.size()
                && tokens.get(pos).isIdent("default")
                && tokens.get(pos + 1).isIdent("values")) {
            return new InsertStatement(table, columns, null, null);
        }
        return new InsertStatement(table, columns, null, null);
    }

    /** Parse {@code (expr, …) [, (expr, …)]…} rows after VALUES. */
    private static List<List<ValueExpr>> parseValuesRows(List<SqlToken> tokens, int start) {
        List<List<ValueExpr>> rows = new ArrayList<>();
        int pos = start;
        while (pos < tokens.size() && tokens.get(pos).isPunct("(")) {
            List<ValueExpr> row = new ArrayList<>();
            List<SqlToken> current = new ArrayList<>();
            int depth = 1;
            pos++;
            while (pos < tokens.size() && depth > 0) {
                SqlToken token = tokens.get(pos);
                if (token.isPunct("(")) {
                    depth++;
                } else if (token.isPunct(")")) {
                    depth--;
                    if (depth == 0) {
                        pos++;
                        break;
                    }
                } else if (token.isPunct(",") && depth == 1) {
                    row.add(ValueExpr.classify(current));
                    current = new ArrayList<>();
                    pos++;
                    continue;
                }
                current.add(token);
                pos++;
            }
            if (!current.isEmpty()) {
                row.add(ValueExpr.classify(current));
            }
            rows.add(row);
            // Another row?
            if (pos < tokens.size() && tokens.get(pos).isPunct(",")) {
                pos++;
                continue;
            }
            break;
        }
        return rows;
    }

    /** Parse top-level SELECT items until FROM/WHERE/… at depth 0. */
    private static List<ValueExpr> parseSelectItems(List<SqlToken> tokens, int start) {
        List<ValueExpr> items = new ArrayList<>();
        List<SqlToken> current = new ArrayList<>();
        int depth = 0;
        for (int pos = start; pos < tokens.size(); pos++) {
            SqlToken token = tokens.get(pos);
            if (token.isPunct("(")) {
                depth++;
            } else if (token.isPunct(")")) {
                depth--;
            } else if (depth == 0 && token.kind() == SqlToken.Kind.IDENT
                    && SELECT_ITEM_TERMINATORS.contains(token.text().toLowerCase())) {
                break;
            } else if (depth == 0 && token.isPunct(",")) {
                items.add(ValueExpr.classify(current));
                current = new ArrayList<>();
                continue;
            }
            current.add(token);
        }
        if (!current.isEmpty()) {
            items.add(ValueExpr.classify(current));
        }
        return items;
    }

    /**
     * Extract {@code <callIdColumn> = <atom>} (and
     * {@code <atom> = <callIdColumn>}) comparisons where the atom is a
     * single string literal or named parameter. {@code callIdColumn} is
     * the manifest's call-id column name. Concatenations and other
     * computed expressions are not atoms — they are skipped by static
     * call-id resolution.
     */
    public static List<ValueExpr> callIdComparisons(List<SqlToken> tokens, String callIdColumn) {
        List<ValueExpr> comparisons = new ArrayList<>();
        for (int i = 0; i < tokens.size(); i++) {
            if (!tokens.get(i).isIdent(callIdColumn)) {
                continue;
            }
            // forward form: <callIdColumn> = <atom>
            if (i + 2 < tokens.size() && tokens.get(i + 1).isPunct("=")) {
                SqlToken value = tokens.get(i + 2);
                if (isAtom(value) && !continuesExpression(tokens, i + 3)) {
                    comparisons.add(atom(value));
                }
            }
            // reverse form: <atom> = <callIdColumn>
            if (i >= 2 && tokens.get(i - 1).isPunct("=")) {
                SqlToken value = tokens.get(i - 2);
                if (isAtom(value) && (i - 3 < 0 || !tokens.get(i - 3).isPunct("||"))) {
                    comparisons.add(atom(value));
                }
            }
        }
        return comparisons;
    }

    private static boolean isAtom(SqlToken token) {
        return token.kind() == SqlToken.Kind.STRING || token.kind() == SqlToken.Kind.PARAM;
    }

    private static boolean continuesExpression(List<SqlToken> tokens, int index) {
        return index < tokens.size()
                && (tokens.get(index).isPunct("||") || tokens.get(index).isPunct("."));
    }

    private static ValueExpr atom(SqlToken token) {
        return token.kind() == SqlToken.Kind.STRING
                ? new ValueExpr(ValueExpr.Kind.STRING, token.text())
                : new ValueExpr(ValueExpr.Kind.PARAM, token.text());
    }

    private static int indexOfIdent(List<SqlToken> tokens, String name) {
        for (int i = 0; i < tokens.size(); i++) {
            if (tokens.get(i).isIdent(name)) {
                return i;
            }
        }
        return -1;
    }
}
