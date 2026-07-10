package io.sqlitehost.model.ddl;

import io.sqlitehost.model.manifest.ListField;
import io.sqlitehost.model.manifest.Manifest;
import io.sqlitehost.model.manifest.ManifestColumns;
import io.sqlitehost.model.manifest.MethodDescriptor;
import io.sqlitehost.model.manifest.ScalarField;
import io.sqlitehost.model.manifest.ScalarType;

import java.util.ArrayList;
import java.util.List;

/**
 * Canonical SQLite DDL generation from the manifest. Port of the
 * canonical implementation (codegen/core/src/ddl.ts):
 * {@link #generateSchemaScript} is byte-identical to the committed
 * snapshot (fixtures/schemas/*.ddl.sql). Every SQL-visible column name
 * flows from the manifest {@code columns} block; every statement stays
 * inside the SQLite 3.19.3 feature set: no JSON1, no window functions,
 * no UPSERT, no RETURNING, no STRICT tables.
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
            case FLOAT32:
            case FLOAT64:
                return "REAL";
            default:
                throw new IllegalArgumentException("unknown scalar type " + scalarType);
        }
    }

    private static String scalarColumnLine(ScalarField field) {
        String notNull = field.optional() ? "" : " NOT NULL";
        return "    " + field.column() + " " + sqlColumnType(field.scalarType()) + notNull;
    }

    private static String queueTableDdl(Manifest manifest) {
        ManifestColumns columns = manifest.columns();
        return String.join("\n",
                "CREATE TABLE " + manifest.queueTable().name() + " (",
                "    " + columns.queueId() + " INTEGER PRIMARY KEY AUTOINCREMENT,",
                "    " + columns.callId() + " TEXT NOT NULL UNIQUE,",
                "    " + columns.method() + " TEXT NOT NULL,",
                "    " + columns.status() + " TEXT NOT NULL DEFAULT 'pending'",
                ");");
    }

    /** Shared name/value table shape used by the inputs and vars tables (names come from the manifest). */
    private static String namedValueTableDdl(String tableName, ManifestColumns columns) {
        return String.join("\n",
                "CREATE TABLE " + tableName + " (",
                "    " + columns.name() + " TEXT NOT NULL PRIMARY KEY,",
                "    " + columns.valueType() + " TEXT NOT NULL,",
                "    " + columns.intValue() + " INTEGER,",
                "    " + columns.realValue() + " REAL,",
                "    " + columns.textValue() + " TEXT,",
                "    " + columns.blobValue() + " BLOB",
                ");");
    }

    private static String controlTableDdl(Manifest manifest) {
        ManifestColumns columns = manifest.columns();
        return String.join("\n",
                "CREATE TABLE " + manifest.controlTable().name() + " (",
                "    " + columns.action() + " TEXT NOT NULL,",
                "    " + columns.message() + " TEXT",
                ");");
    }

    private static String parentTableDdl(
            String tableName, List<ScalarField> scalarFields, boolean isResultTable,
            ManifestColumns columns) {
        List<String> columnLines = new ArrayList<>();
        columnLines.add("    " + columns.callId() + " TEXT NOT NULL PRIMARY KEY");
        if (isResultTable) {
            columnLines.add("    " + columns.status() + " TEXT NOT NULL DEFAULT '"
                    + columns.doneValue() + "'");
        }
        for (ScalarField field : scalarFields) {
            columnLines.add(scalarColumnLine(field));
        }
        return "CREATE TABLE " + tableName + " (\n"
                + String.join(",\n", columnLines)
                + "\n);";
    }

    private static String childTableDdl(ListField listField, ManifestColumns columns) {
        List<String> columnLines = new ArrayList<>();
        columnLines.add("    " + columns.callId() + " TEXT NOT NULL");
        columnLines.add("    " + columns.itemIndex() + " INTEGER NOT NULL");
        for (ScalarField field : listField.itemFields()) {
            columnLines.add(scalarColumnLine(field));
        }
        columnLines.add("    PRIMARY KEY (" + columns.callId() + ", "
                + columns.itemIndex() + ")");
        return "CREATE TABLE " + listField.childTable() + " (\n"
                + String.join(",\n", columnLines)
                + "\n);";
    }

    private static String queueTriggerDdl(Manifest manifest, MethodDescriptor method) {
        ManifestColumns columns = manifest.columns();
        return String.join("\n",
                "CREATE TRIGGER " + method.queueTrigger(),
                "AFTER INSERT ON " + method.callTable(),
                "BEGIN",
                "    INSERT INTO " + manifest.queueTable().name() + " ("
                        + columns.callId() + ", " + columns.method() + ")",
                "    VALUES (NEW." + columns.callId() + ", '" + method.methodName() + "');",
                "END;");
    }

    /** Generate the ordered list of DDL statements for a host library. */
    public static List<String> generateSchemaStatements(Manifest manifest) {
        ManifestColumns columns = manifest.columns();
        List<String> statements = new ArrayList<>();
        statements.add(queueTableDdl(manifest));
        statements.add(namedValueTableDdl(manifest.inputsTable().name(), columns));
        statements.add(namedValueTableDdl(manifest.varsTable().name(), columns));
        statements.add(controlTableDdl(manifest));
        for (MethodDescriptor method : manifest.methods()) {
            statements.add(parentTableDdl(
                    method.callTable(), method.input().fields(), false, columns));
            for (ListField listField : method.input().listFields()) {
                statements.add(childTableDdl(listField, columns));
            }
            statements.add(parentTableDdl(
                    method.resultTable(), method.result().fields(), true, columns));
            for (ListField listField : method.result().listFields()) {
                statements.add(childTableDdl(listField, columns));
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
