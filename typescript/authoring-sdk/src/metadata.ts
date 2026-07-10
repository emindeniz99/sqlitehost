/**
 * Editor/autocomplete metadata derived from a canonical manifest:
 * tables, columns, and method descriptors. Works with ANY manifest —
 * the generated per-application const (e.g. SAMPLE_HOST_METADATA) is
 * the precomputed result for one manifest and is golden-tested to equal
 * loadHostMetadata(manifestJson).
 *
 * Physical names are read from the manifest as-is (never re-derived);
 * only the fixed structural columns from docs/workspace-schema.md
 * (call_id, status, item_index) are added to the table column lists.
 */

import type { HostManifest, ManifestListField } from "./manifest.js";
import { parseHostManifest } from "./manifest.js";

export interface TableMetadata {
  name: string;
  columns: string[];
}

export interface ListFieldMetadata {
  propertyName: string;
  childTable: string;
  /** item propertyName -> physical column name */
  columns: Record<string, string>;
}

export interface MethodMetadata {
  methodName: string;
  operationName: string;
  handlerName: string;
  apiLevel: number;
  callTable: string;
  resultTable: string;
  queueTrigger: string;
  /** input propertyName -> physical call-table column */
  inputColumns: Record<string, string>;
  /** result propertyName -> physical result-table column */
  resultColumns: Record<string, string>;
  inputListFields: ListFieldMetadata[];
  resultListFields: ListFieldMetadata[];
}

export interface HostMetadata {
  engine: string;
  namespace: string;
  interfaceName: string;
  apiLevel: number;
  minSqliteVersionNumber: number;
  features: string[];
  queueTable: TableMetadata;
  inputsTable: TableMetadata;
  varsTable: TableMetadata;
  methods: MethodMetadata[];
  /** Every workspace table with its columns, in canonical DDL order. */
  tables: TableMetadata[];
}

function columnMap(fields: { propertyName: string; column: string }[]): Record<string, string> {
  const map: Record<string, string> = {};
  for (const field of fields) {
    map[field.propertyName] = field.column;
  }
  return map;
}

function listFieldMetadata(field: ManifestListField): ListFieldMetadata {
  return {
    propertyName: field.propertyName,
    childTable: field.childTable,
    columns: columnMap(field.itemFields),
  };
}

function childTableMetadata(field: ManifestListField): TableMetadata {
  return {
    name: field.childTable,
    columns: ["call_id", "item_index", ...field.itemFields.map((f) => f.column)],
  };
}

/** Build autocomplete metadata from a manifest (JSON text or parsed value). */
export function loadHostMetadata(manifest: HostManifest | string | unknown): HostMetadata {
  const m = parseHostManifest(manifest);

  const tables: TableMetadata[] = [
    { name: m.queueTable.name, columns: [...m.queueTable.columns] },
    { name: m.inputsTable.name, columns: [...m.inputsTable.columns] },
    { name: m.varsTable.name, columns: [...m.varsTable.columns] },
  ];
  const methods: MethodMetadata[] = [];

  for (const method of m.methods) {
    tables.push({
      name: method.callTable,
      columns: ["call_id", ...method.input.fields.map((f) => f.column)],
    });
    for (const listField of method.input.listFields) {
      tables.push(childTableMetadata(listField));
    }
    tables.push({
      name: method.resultTable,
      columns: ["call_id", "status", ...method.result.fields.map((f) => f.column)],
    });
    for (const listField of method.result.listFields) {
      tables.push(childTableMetadata(listField));
    }

    methods.push({
      methodName: method.methodName,
      operationName: method.operationName,
      handlerName: method.handlerName,
      apiLevel: method.apiLevel,
      callTable: method.callTable,
      resultTable: method.resultTable,
      queueTrigger: method.queueTrigger,
      inputColumns: columnMap(method.input.fields),
      resultColumns: columnMap(method.result.fields),
      inputListFields: method.input.listFields.map(listFieldMetadata),
      resultListFields: method.result.listFields.map(listFieldMetadata),
    });
  }

  return {
    engine: m.engine,
    namespace: m.library.namespace,
    interfaceName: m.library.interfaceName,
    apiLevel: m.library.apiLevel,
    minSqliteVersionNumber: m.library.minSqliteVersionNumber,
    features: [...m.library.features],
    queueTable: { name: m.queueTable.name, columns: [...m.queueTable.columns] },
    inputsTable: { name: m.inputsTable.name, columns: [...m.inputsTable.columns] },
    varsTable: { name: m.varsTable.name, columns: [...m.varsTable.columns] },
    methods,
    tables,
  };
}
