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
    features: string[];
  };
  naming: NamingIr;
  queueTable: QueueTableIr;
  inputsTable: InputsTableIr;
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
] as const;

export const QUEUE_TABLE_V1: QueueTableIr = {
  name: "pending_host_calls",
  columns: ["queue_id", "call_id", "method", "status"],
};

export const INPUTS_TABLE_V1: InputsTableIr = {
  name: "script_inputs",
  columns: ["name", "value_type", "int_value", "real_value", "text_value", "blob_value"],
};
