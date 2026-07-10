export * from "./generated/sample-host.js";
export * from "./manifest.js";
export * from "./metadata.js";
export * from "./sql.js";
export * from "./lint.js";
export * from "./builder.js";

// Re-export the envelope types and binding helpers so authoring code
// only needs one import.
export {
  BINDING_TYPES,
  SCRIPT_ENGINE_V1,
  ScriptParseError,
  blob,
  bool,
  float32,
  float64,
  int32,
  int64,
  nullValue,
  parseScript,
  serializeScript,
  text,
  validateScript,
} from "@sqlite-host/runtime-types";
export type {
  BindingType,
  BindingValue,
  Int32Value,
  Int64Value,
  RuntimeInput,
  Script,
  Statement,
  Step,
} from "@sqlite-host/runtime-types";
