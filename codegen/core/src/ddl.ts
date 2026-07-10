/**
 * Canonical SQLite DDL generation from the IR. The output of
 * generateSchemaScript() is byte-identical to the committed snapshot
 * (fixtures/schemas/*.ddl.sql) and to what the C# runtime's schema
 * generator and the Java DDL generator produce. Every statement stays
 * inside the SQLite 3.19.3 feature set: no JSON1, no window functions,
 * no UPSERT, no RETURNING, no STRICT tables.
 */

import type {
  HostLibraryIr,
  HostMethodIr,
  ListFieldIr,
  ScalarFieldIr,
  ScalarTypeIr,
} from "./ir.js";

export function sqlColumnType(scalarType: ScalarTypeIr): string {
  switch (scalarType) {
    case "int32":
    case "int64":
    case "boolean":
      return "INTEGER";
    case "string":
      return "TEXT";
    case "bytes":
      return "BLOB";
    case "float32":
    case "float64":
      return "REAL";
  }
}

function scalarColumnLine(field: ScalarFieldIr): string {
  const notNull = field.optional ? "" : " NOT NULL";
  return `    ${field.column} ${sqlColumnType(field.scalarType)}${notNull}`;
}

function queueTableDdl(ir: HostLibraryIr): string {
  return [
    `CREATE TABLE ${ir.queueTable.name} (`,
    "    queue_id INTEGER PRIMARY KEY AUTOINCREMENT,",
    "    call_id TEXT NOT NULL UNIQUE,",
    "    method TEXT NOT NULL,",
    "    status TEXT NOT NULL DEFAULT 'pending'",
    ");",
  ].join("\n");
}

function inputsTableDdl(ir: HostLibraryIr): string {
  return [
    `CREATE TABLE ${ir.inputsTable.name} (`,
    "    name TEXT NOT NULL PRIMARY KEY,",
    "    value_type TEXT NOT NULL,",
    "    int_value INTEGER,",
    "    real_value REAL,",
    "    text_value TEXT,",
    "    blob_value BLOB",
    ");",
  ].join("\n");
}

function parentTableDdl(
  tableName: string,
  scalarFields: ScalarFieldIr[],
  isResultTable: boolean,
): string {
  const lines: string[] = [];
  lines.push(`CREATE TABLE ${tableName} (`);
  const columnLines: string[] = ["    call_id TEXT NOT NULL PRIMARY KEY"];
  if (isResultTable) {
    columnLines.push("    status TEXT NOT NULL DEFAULT 'done'");
  }
  for (const field of scalarFields) {
    columnLines.push(scalarColumnLine(field));
  }
  lines.push(columnLines.join(",\n"));
  lines.push(");");
  return lines.join("\n");
}

function childTableDdl(listField: ListFieldIr): string {
  const lines: string[] = [];
  lines.push(`CREATE TABLE ${listField.childTable} (`);
  const columnLines: string[] = [
    "    call_id TEXT NOT NULL",
    "    item_index INTEGER NOT NULL",
  ];
  for (const field of listField.itemFields) {
    columnLines.push(scalarColumnLine(field));
  }
  columnLines.push("    PRIMARY KEY (call_id, item_index)");
  lines.push(columnLines.join(",\n"));
  lines.push(");");
  return lines.join("\n");
}

function queueTriggerDdl(ir: HostLibraryIr, method: HostMethodIr): string {
  return [
    `CREATE TRIGGER ${method.queueTrigger}`,
    `AFTER INSERT ON ${method.callTable}`,
    "BEGIN",
    `    INSERT INTO ${ir.queueTable.name} (call_id, method)`,
    `    VALUES (NEW.call_id, '${method.methodName}');`,
    "END;",
  ].join("\n");
}

/** Generate the ordered list of DDL statements for a host library. */
export function generateSchemaStatements(ir: HostLibraryIr): string[] {
  const statements: string[] = [queueTableDdl(ir), inputsTableDdl(ir)];
  for (const method of ir.methods) {
    statements.push(parentTableDdl(method.callTable, method.input.fields, false));
    for (const listField of method.input.listFields) {
      statements.push(childTableDdl(listField));
    }
    statements.push(parentTableDdl(method.resultTable, method.result.fields, true));
    for (const listField of method.result.listFields) {
      statements.push(childTableDdl(listField));
    }
    statements.push(queueTriggerDdl(ir, method));
  }
  return statements;
}

/** Generate the full schema script — byte-identical to the DDL snapshot fixture. */
export function generateSchemaScript(ir: HostLibraryIr): string {
  return generateSchemaStatements(ir).join("\n\n") + "\n";
}
