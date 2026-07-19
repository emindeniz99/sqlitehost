/**
 * Canonical SQLite DDL generation from the IR. The output of
 * generateSchemaScript() is byte-identical to the committed snapshot
 * (fixtures/schemas/*.ddl.sql) and to what the C# runtime's schema
 * generator and the Java DDL generator produce. Every statement stays
 * inside the SQLite 3.19.3 feature set: no JSON1, no window functions,
 * no UPSERT, no RETURNING, no STRICT tables.
 *
 * Every SQL-visible identifier flows from ir.naming / ir.columns —
 * nothing here is hardcoded beyond SQL keywords and column types.
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
  const c = ir.columns;
  return [
    `CREATE TABLE ${ir.queueTable.name} (`,
    `    ${c.queueId} INTEGER PRIMARY KEY AUTOINCREMENT,`,
    `    ${c.callId} TEXT NOT NULL UNIQUE,`,
    `    ${c.method} TEXT NOT NULL,`,
    `    ${c.status} TEXT NOT NULL DEFAULT 'pending'`,
    ");",
  ].join("\n");
}

function namedValueTableDdl(ir: HostLibraryIr, tableName: string): string {
  const c = ir.columns;
  return [
    `CREATE TABLE ${tableName} (`,
    `    ${c.name} TEXT NOT NULL PRIMARY KEY,`,
    `    ${c.valueType} TEXT NOT NULL,`,
    `    ${c.intValue} INTEGER,`,
    `    ${c.realValue} REAL,`,
    `    ${c.textValue} TEXT,`,
    `    ${c.blobValue} BLOB`,
    ");",
  ].join("\n");
}

function controlTableDdl(ir: HostLibraryIr): string {
  const c = ir.columns;
  return [
    `CREATE TABLE ${ir.controlTable.name} (`,
    `    ${c.action} TEXT NOT NULL,`,
    `    ${c.message} TEXT`,
    ");",
  ].join("\n");
}

function parentTableDdl(
  ir: HostLibraryIr,
  tableName: string,
  scalarFields: ScalarFieldIr[],
  isResultTable: boolean,
): string {
  const c = ir.columns;
  const lines: string[] = [];
  lines.push(`CREATE TABLE ${tableName} (`);
  const columnLines: string[] = [`    ${c.callId} TEXT NOT NULL PRIMARY KEY`];
  if (isResultTable) {
    // doneValue is data, not an identifier: escape embedded quotes so a
    // value like "do'ne" cannot break the generated literal.
    columnLines.push(
      `    ${c.status} TEXT NOT NULL DEFAULT '${c.doneValue.replace(/'/g, "''")}'`,
    );
  }
  for (const field of scalarFields) {
    columnLines.push(scalarColumnLine(field));
  }
  lines.push(columnLines.join(",\n"));
  lines.push(");");
  return lines.join("\n");
}

function childTableDdl(ir: HostLibraryIr, listField: ListFieldIr): string {
  const c = ir.columns;
  const lines: string[] = [];
  lines.push(`CREATE TABLE ${listField.childTable} (`);
  const columnLines: string[] = [
    `    ${c.callId} TEXT NOT NULL`,
    `    ${c.itemIndex} INTEGER NOT NULL`,
  ];
  for (const field of listField.itemFields) {
    columnLines.push(scalarColumnLine(field));
  }
  columnLines.push(`    PRIMARY KEY (${c.callId}, ${c.itemIndex})`);
  lines.push(columnLines.join(",\n"));
  lines.push(");");
  return lines.join("\n");
}

function queueTriggerDdl(ir: HostLibraryIr, method: HostMethodIr): string {
  const c = ir.columns;
  return [
    `CREATE TRIGGER ${method.queueTrigger}`,
    `AFTER INSERT ON ${method.callTable}`,
    "BEGIN",
    `    INSERT INTO ${ir.queueTable.name} (${c.callId}, ${c.method})`,
    `    VALUES (NEW.${c.callId}, '${method.methodName}');`,
    "END;",
  ].join("\n");
}

/** Generate the ordered list of DDL statements for a host library. */
export function generateSchemaStatements(ir: HostLibraryIr): string[] {
  const statements: string[] = [
    queueTableDdl(ir),
    namedValueTableDdl(ir, ir.inputsTable.name),
    namedValueTableDdl(ir, ir.varsTable.name),
    controlTableDdl(ir),
  ];
  for (const method of ir.methods) {
    statements.push(parentTableDdl(ir, method.callTable, method.input.fields, false));
    for (const listField of method.input.listFields) {
      statements.push(childTableDdl(ir, listField));
    }
    statements.push(parentTableDdl(ir, method.resultTable, method.result.fields, true));
    for (const listField of method.result.listFields) {
      statements.push(childTableDdl(ir, listField));
    }
    statements.push(queueTriggerDdl(ir, method));
  }
  return statements;
}

/** Generate the full schema script — byte-identical to the DDL snapshot fixture. */
export function generateSchemaScript(ir: HostLibraryIr): string {
  return generateSchemaStatements(ir).join("\n\n") + "\n";
}
