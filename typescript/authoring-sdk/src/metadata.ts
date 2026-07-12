/**
 * Editor/autocomplete metadata derived from a canonical manifest:
 * tables, columns, and method descriptors. Works with ANY manifest —
 * the generated per-application const (e.g. SAMPLE_HOST_METADATA) is
 * the precomputed result for one manifest and is golden-tested to equal
 * loadHostMetadata(manifestJson).
 *
 * Physical names are read from the manifest as-is (never re-derived);
 * only the shared structural columns (callId, status, itemIndex — the
 * manifest's `columns` block, docs/workspace-schema.md) are added to
 * the table column lists.
 */

import type {
  HostManifest,
  ManifestColumns,
  ManifestInline,
  ManifestListField,
} from "./manifest.js";
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
  /** The manifest inline block, mirrored exactly (null when not exposed). */
  inline: ManifestInline | null;
}

export interface HostMetadata {
  engine: string;
  namespace: string;
  interfaceName: string;
  apiLevel: number;
  minSqliteVersionNumber: number;
  features: string[];
  /** Prefix of derived inline function names (manifest naming block). */
  functionPrefix: string;
  /** Shared SQL-visible column names + done literal (manifest columns block). */
  columns: ManifestColumns;
  queueTable: TableMetadata;
  inputsTable: TableMetadata;
  varsTable: TableMetadata;
  controlTable: TableMetadata;
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

function childTableMetadata(field: ManifestListField, columns: ManifestColumns): TableMetadata {
  return {
    name: field.childTable,
    columns: [columns.callId, columns.itemIndex, ...field.itemFields.map((f) => f.column)],
  };
}

/** Copy the manifest inline block as-is (null/absent -> null). */
function inlineMetadata(inline: ManifestInline | null | undefined): ManifestInline | null {
  if (inline === null || inline === undefined) {
    return null;
  }
  return {
    functionName: inline.functionName,
    minArgs: inline.minArgs,
    maxArgs: inline.maxArgs,
    args: inline.args.map((arg) => ({ ...arg })),
    returns: { ...inline.returns },
  };
}

/** Build autocomplete metadata from a manifest (JSON text or parsed value). */
export function loadHostMetadata(manifest: HostManifest | string | unknown): HostMetadata {
  const m = parseHostManifest(manifest);

  const tables: TableMetadata[] = [
    { name: m.queueTable.name, columns: [...m.queueTable.columns] },
    { name: m.inputsTable.name, columns: [...m.inputsTable.columns] },
    { name: m.varsTable.name, columns: [...m.varsTable.columns] },
    { name: m.controlTable.name, columns: [...m.controlTable.columns] },
  ];
  const methods: MethodMetadata[] = [];

  for (const method of m.methods) {
    tables.push({
      name: method.callTable,
      columns: [m.columns.callId, ...method.input.fields.map((f) => f.column)],
    });
    for (const listField of method.input.listFields) {
      tables.push(childTableMetadata(listField, m.columns));
    }
    tables.push({
      name: method.resultTable,
      columns: [m.columns.callId, m.columns.status, ...method.result.fields.map((f) => f.column)],
    });
    for (const listField of method.result.listFields) {
      tables.push(childTableMetadata(listField, m.columns));
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
      inline: inlineMetadata(method.inline),
    });
  }

  return {
    engine: m.engine,
    namespace: m.library.namespace,
    interfaceName: m.library.interfaceName,
    apiLevel: m.library.apiLevel,
    minSqliteVersionNumber: m.library.minSqliteVersionNumber,
    features: [...m.library.features],
    functionPrefix: m.naming.functionPrefix,
    columns: { ...m.columns },
    queueTable: { name: m.queueTable.name, columns: [...m.queueTable.columns] },
    inputsTable: { name: m.inputsTable.name, columns: [...m.inputsTable.columns] },
    varsTable: { name: m.varsTable.name, columns: [...m.varsTable.columns] },
    controlTable: { name: m.controlTable.name, columns: [...m.controlTable.columns] },
    methods,
    tables,
  };
}
