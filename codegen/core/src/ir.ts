/**
 * Language-neutral intermediate representation (IR) for a SqliteHost
 * host library. The TypeSpec frontend produces this IR; every emitter
 * (manifest, C#, Java, TypeScript) consumes it. The canonical manifest
 * JSON (fixtures/manifests/*.manifest.json) is the serialized form of
 * this IR — see manifest.ts for the canonical serialization.
 */

export type ScalarTypeIr =
  | "int32"
  | "int64"
  | "boolean"
  | "string"
  | "bytes"
  | "float32"
  | "float64";

export interface NamingIr {
  callTablePrefix: string;
  resultTablePrefix: string;
  inputColumnPrefix: string;
  resultColumnPrefix: string;
  inputListTableInfix: string;
  resultListTableInfix: string;
}

export interface ScalarFieldIr {
  /** TypeSpec/C#/Java/TS property name, camelCase (e.g. "defaultValue"). */
  propertyName: string;
  /** Logical SQL name, snake_case (e.g. "default_value"). */
  sqlName: string;
  /** Physical column name including prefix (e.g. "input_default_value"). */
  column: string;
  scalarType: ScalarTypeIr;
  optional: boolean;
}

export interface ListFieldIr {
  propertyName: string;
  sqlName: string;
  /** Physical child table name (e.g. "call_get_values__input_keys"). */
  childTable: string;
  itemModelName: string;
  itemFields: ScalarFieldIr[];
}

export interface ObjectShapeIr {
  modelName: string;
  fields: ScalarFieldIr[];
  listFields: ListFieldIr[];
}

export interface HostMethodIr {
  /** TypeSpec operation name (e.g. "GetValue"). */
  operationName: string;
  /** Logical method name used in the protocol (e.g. "getValue"). */
  methodName: string;
  /** Handler member name on the generated handler interface. */
  handlerName: string;
  apiLevel: number;
  callTable: string;
  resultTable: string;
  queueTrigger: string;
  input: ObjectShapeIr;
  result: ObjectShapeIr;
}

export interface QueueTableIr {
  name: string;
  columns: string[];
}

export interface InputsTableIr {
  name: string;
  columns: string[];
}

export interface VarsTableIr {
  name: string;
  columns: string[];
}

export interface ControlTableIr {
  name: string;
  columns: string[];
}

/**
 * Configurable column identifiers and the done-status literal — every
 * SQL-visible name a script author may want to rename. The halt/fail
 * action verbs, the engine string, and the trigger derivation rule
 * stay protocol constants (see docs/naming.md).
 */
export interface ColumnsIr {
  callId: string;
  itemIndex: string;
  status: string;
  doneValue: string;
  queueId: string;
  method: string;
  name: string;
  valueType: string;
  intValue: string;
  realValue: string;
  textValue: string;
  blobValue: string;
  action: string;
  message: string;
}

export interface ScriptEnvelopeIr {
  engine: string;
  bindingTypes: string[];
}

export interface HostLibraryIr {
  manifestVersion: 1;
  engine: string;
  library: {
    namespace: string;
    interfaceName: string;
    apiLevel: number;
    /** SQLITE_VERSION_NUMBER-style minimum (major*1000000 + minor*1000 + patch). */
    minSqliteVersionNumber: number;
    features: string[];
  };
  naming: NamingIr;
  columns: ColumnsIr;
  queueTable: QueueTableIr;
  inputsTable: InputsTableIr;
  varsTable: VarsTableIr;
  controlTable: ControlTableIr;
  scriptEnvelope: ScriptEnvelopeIr;
  methods: HostMethodIr[];
}

export const ENGINE_V1 = "sqlite-host-v1";

export const BINDING_TYPES_V1 = [
  "null",
  "int32",
  "int64",
  "bool",
  "text",
  "blob",
  "float32",
  "float64",
] as const;

export const FEATURES_V1 = [
  "typedNamedBindings",
  "splitResultTables",
  "scriptInputs",
  "scriptVars",
  "scriptControl",
] as const;

export const COLUMNS_V1: ColumnsIr = {
  callId: "call_id",
  itemIndex: "item_index",
  status: "status",
  doneValue: "done",
  queueId: "queue_id",
  method: "method",
  name: "name",
  valueType: "value_type",
  intValue: "int_value",
  realValue: "real_value",
  textValue: "text_value",
  blobValue: "blob_value",
  action: "action",
  message: "message",
};

/** Protocol verbs for the control table's action column (NOT configurable). */
export const CONTROL_ACTION_HALT = "halt";
export const CONTROL_ACTION_FAIL = "fail";

/** Build the column list of each runtime-managed table from the columns config. */
export function queueTableColumns(c: ColumnsIr): string[] {
  return [c.queueId, c.callId, c.method, c.status];
}
export function namedValueTableColumns(c: ColumnsIr): string[] {
  return [c.name, c.valueType, c.intValue, c.realValue, c.textValue, c.blobValue];
}
export function controlTableColumns(c: ColumnsIr): string[] {
  return [c.action, c.message];
}

/** Default per-host minimum SQLite version (the plan's floor, 3.19.3). */
export const DEFAULT_MIN_SQLITE_VERSION_NUMBER = 3019003;

/** The library's own engine-verified minimum (measured in the CI matrix). */
export const LIBRARY_ENGINE_VERIFIED_MINIMUM = 3009000;

export const QUEUE_TABLE_V1: QueueTableIr = {
  name: "pending_host_calls",
  columns: ["queue_id", "call_id", "method", "status"],
};

export const INPUTS_TABLE_V1: InputsTableIr = {
  name: "script_inputs",
  columns: ["name", "value_type", "int_value", "real_value", "text_value", "blob_value"],
};

export const VARS_TABLE_V1: VarsTableIr = {
  name: "script_vars",
  columns: ["name", "value_type", "int_value", "real_value", "text_value", "blob_value"],
};

export const CONTROL_TABLE_V1: ControlTableIr = {
  name: "script_control",
  columns: ["action", "message"],
};
