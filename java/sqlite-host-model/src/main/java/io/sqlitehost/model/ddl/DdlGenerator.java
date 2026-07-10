package io.sqlitehost.model.ddl;

import io.sqlitehost.model.manifest.ListField;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.model.manifest.MethodDescriptor;
import io.sqlitehost.model.manifest.ScalarField;
import io.sqlitehost.model.manifest.ScalarType;

import java.util.ArrayList;
import java.util.List;

/**
 * Canonical SQLite DDL generation from the manifest. Port of the
 * canonical implementation (codegen/core/src/ddl.ts):
 * {@link #generateSchemaScript} is byte-identical to the committed
 * snapshot (fixtures/schemas/*.ddl.sql). Every statement stays inside
 * the SQLite 3.19.3 feature set: no JSON1, no window functions, no
 * UPSERT, no RETURNING, no STRICT tables.
 */
public final class DdlGenerator {

    private DdlGenerator() {
    }

    /** SQLite column type for an IR scalar type (docs/workspace-schema.md). */
    public static String sqlColumnType(ScalarType scalarType) {
        switch (scalarType) {
            case INT32:
            case INT64:
            case BOOLEAN:
                return "INTEGER";
            case STRING:
                return "TEXT";
            case BYTES:
                return "BLOB";
            default:
                throw new IllegalArgumentException("unknown scalar type " + scalarType);
        }
    }

    private static String scalarColumnLine(ScalarField field) {
        String notNull = field.optional() ? "" : " NOT NULL";
        return "    " + field.column() + " " + sqlColumnType(field.scalarType()) + notNull;
    }

    private static String queueTableDdl(Manifest manifest) {
        return String.join("\n",
                "CREATE TABLE " + manifest.queueTable().name() + " (",
                "    queue_id INTEGER PRIMARY KEY AUTOINCREMENT,",
                "    call_id TEXT NOT NULL UNIQUE,",
                "    method TEXT NOT NULL,",
                "    status TEXT NOT NULL DEFAULT 'pending'",
                ");");
    }

    private static String inputsTableDdl(Manifest manifest) {
        return String.join("\n",
                "CREATE TABLE " + manifest.inputsTable().name() + " (",
                "    name TEXT NOT NULL PRIMARY KEY,",
                "    value_type TEXT NOT NULL,",
                "    int_value INTEGER,",
                "    text_value TEXT,",
                "    blob_value BLOB",
                ");");
    }

    private static String parentTableDdl(
            String tableName, List<ScalarField> scalarFields, boolean isResultTable) {
        List<String> columnLines = new ArrayList<>();
        columnLines.add("    call_id TEXT NOT NULL PRIMARY KEY");
        if (isResultTable) {
            columnLines.add("    status TEXT NOT NULL DEFAULT 'done'");
        }
        for (ScalarField field : scalarFields) {
            columnLines.add(scalarColumnLine(field));
        }
        return "CREATE TABLE " + tableName + " (\n"
                + String.join(",\n", columnLines)
                + "\n);";
    }

    private static String childTableDdl(ListField listField) {
        List<String> columnLines = new ArrayList<>();
        columnLines.add("    call_id TEXT NOT NULL");
        columnLines.add("    item_index INTEGER NOT NULL");
        for (ScalarField field : listField.itemFields()) {
            columnLines.add(scalarColumnLine(field));
        }
        columnLines.add("    PRIMARY KEY (call_id, item_index)");
        return "CREATE TABLE " + listField.childTable() + " (\n"
                + String.join(",\n", columnLines)
                + "\n);";
    }

    private static String queueTriggerDdl(Manifest manifest, MethodDescriptor method) {
        return String.join("\n",
                "CREATE TRIGGER " + method.queueTrigger(),
                "AFTER INSERT ON " + method.callTable(),
                "BEGIN",
                "    INSERT INTO " + manifest.queueTable().name() + " (call_id, method)",
                "    VALUES (NEW.call_id, '" + method.methodName() + "');",
                "END;");
    }

    /** Generate the ordered list of DDL statements for a host library. */
    public static List<String> generateSchemaStatements(Manifest manifest) {
        List<String> statements = new ArrayList<>();
        statements.add(queueTableDdl(manifest));
        statements.add(inputsTableDdl(manifest));
        for (MethodDescriptor method : manifest.methods()) {
            statements.add(parentTableDdl(method.callTable(), method.input().fields(), false));
            for (ListField listField : method.input().listFields()) {
                statements.add(childTableDdl(listField));
            }
            statements.add(parentTableDdl(method.resultTable(), method.result().fields(), true));
            for (ListField listField : method.result().listFields()) {
                statements.add(childTableDdl(listField));
            }
            statements.add(queueTriggerDdl(manifest, method));
        }
        return statements;
    }

    /** Generate the full schema script — byte-identical to the DDL snapshot fixture. */
    public static String generateSchemaScript(Manifest manifest) {
        return String.join("\n\n", generateSchemaStatements(manifest)) + "\n";
    }
}
