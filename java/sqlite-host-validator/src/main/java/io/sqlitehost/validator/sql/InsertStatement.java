package io.sqlitehost.validator.sql;

import java.util.Collections;
import java.util.List;

/**
 * A parsed {@code INSERT} statement, as far as the lint rules need:
 * target table, explicit column list (or {@code null} when implicit),
 * and the value expressions feeding the columns — either VALUES rows
 * (possibly multi-row) or the first top-level SELECT items
 * (INSERT…SELECT maps the first select items to the column list).
 */
public record InsertStatement(
        String table,
        List<String> columns,
        List<List<ValueExpr>> rows,
        List<ValueExpr> selectItems) {

    public InsertStatement {
        columns = columns == null ? null : List.copyOf(columns);
        rows = rows == null ? Collections.emptyList() : List.copyOf(rows);
        selectItems = selectItems == null ? null : List.copyOf(selectItems);
    }

    public boolean hasExplicitColumns() {
        return columns != null;
    }

    /**
     * The value rows feeding the columns: the VALUES rows, or the
     * single SELECT-item row for INSERT…SELECT. Empty when the values
     * could not be modeled.
     */
    public List<List<ValueExpr>> valueRows() {
        if (!rows.isEmpty()) {
            return rows;
        }
        if (selectItems != null) {
            return List.of(selectItems);
        }
        return Collections.emptyList();
    }

    /**
     * The value expression feeding {@code column}, for one value row.
     * Returns {@code null} when there is no explicit column list or the
     * row is shorter than the column list.
     */
    public ValueExpr valueFor(List<ValueExpr> row, String column) {
        if (columns == null) {
            return null;
        }
        for (int i = 0; i < columns.size(); i++) {
            if (columns.get(i).equalsIgnoreCase(column)) {
                return i < row.size() ? row.get(i) : null;
            }
        }
        return null;
    }
}
