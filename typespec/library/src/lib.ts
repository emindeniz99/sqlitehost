import { createTypeSpecLibrary, paramMessage } from "@typespec/compiler";

/**
 * Library definition: name + the pinned diagnostic codes for TypeSpec
 * model validation (docs/validation.md §1). Decorator-argument rules
 * are reported by the decorator implementations in this package;
 * model-shape rules are reported by the codegen frontend
 * (codegen/core/src/validate.ts) using these same codes.
 */
export const $lib = createTypeSpecLibrary({
  name: "@sqlite-host/typespec",
  diagnostics: {
    "invalid-api-level": {
      severity: "error",
      messages: {
        default: paramMessage`apiLevel must be a positive integer, got ${"value"}.`,
      },
    },
    "invalid-min-sqlite-version": {
      severity: "error",
      messages: {
        default: paramMessage`minSqliteVersion "${"value"}" is not a dotted SQLite version string of up to four numbers (e.g. "3.19.3" or "3.8.11.1").`,
      },
    },
    "invalid-method-name": {
      severity: "error",
      messages: {
        default: paramMessage`Host method name "${"name"}" is not a valid identifier ([A-Za-z][A-Za-z0-9_]*).`,
      },
    },
    "invalid-handler-name": {
      severity: "error",
      messages: {
        default: paramMessage`Handler name "${"name"}" is not a valid identifier ([A-Za-z_][A-Za-z0-9_]*).`,
      },
    },
    "invalid-sql-name": {
      severity: "error",
      messages: {
        default: paramMessage`SQL name "${"name"}" must be snake_case ([a-z][a-z0-9_]*).`,
      },
    },
    "no-host-library": {
      severity: "error",
      messages: {
        default: "No interface with @hostLibrary was found in the compiled program.",
      },
    },
    "multiple-host-libraries": {
      severity: "error",
      messages: {
        default: paramMessage`This compilation defines ${"count"} @hostLibrary interfaces but the single-library API compiles exactly one; use the multi-library API (compileHostLibraries), which emits one artifact set per library.`,
      },
    },
    "duplicate-host-library-name": {
      severity: "error",
      messages: {
        default: paramMessage`Duplicate @hostLibrary interface name "${"name"}"; interface names must be unique within a compilation because they name the emitted artifacts.`,
      },
    },
    "missing-host-method": {
      severity: "error",
      messages: {
        default: paramMessage`Operation "${"operation"}" in a @hostLibrary interface must carry @hostMethod.`,
      },
    },
    "invalid-method-shape": {
      severity: "error",
      messages: {
        default: paramMessage`Operation "${"operation"}": ${"detail"} Host method input and output must each be a single named model.`,
      },
    },
    "unsupported-scalar": {
      severity: "error",
      messages: {
        default: paramMessage`Type "${"type"}" of field "${"field"}" is not a supported scalar (int32, int64, boolean, string, bytes, float32, float64).`,
      },
    },
    "nested-model": {
      severity: "error",
      messages: {
        default: paramMessage`Field "${"field"}" is a nested model; nested object fields are not supported in v1.`,
      },
    },
    "nested-list": {
      severity: "error",
      messages: {
        default: paramMessage`Field "${"field"}" contains a nested list; nested lists are not supported in v1.`,
      },
    },
    "optional-list": {
      severity: "error",
      messages: {
        default: paramMessage`List field "${"field"}" cannot be optional; use an empty list instead.`,
      },
    },
    "invalid-list-item": {
      severity: "error",
      messages: {
        default: paramMessage`List field "${"field"}" must contain a named model of scalar fields (primitive lists are not supported in v1).`,
      },
    },
    "unsupported-field-type": {
      severity: "error",
      messages: {
        default: paramMessage`Field "${"field"}" has an unsupported type kind "${"kind"}" (unions, maps, enums, and tuples are not supported in v1).`,
      },
    },
    "duplicate-method-name": {
      severity: "error",
      messages: {
        default: paramMessage`Duplicate host method name "${"name"}".`,
      },
    },
    "duplicate-sql-name": {
      severity: "error",
      messages: {
        default: paramMessage`Duplicate SQL name "${"name"}" in model "${"model"}".`,
      },
    },
    "duplicate-table-name": {
      severity: "error",
      messages: {
        default: paramMessage`Derived table name "${"table"}" is used more than once; method or list field names collide after naming derivation.`,
      },
    },
    "invalid-shared-table-name": {
      severity: "error",
      messages: {
        default: paramMessage`${"option"} must be a non-empty table name.`,
      },
    },
    "duplicate-shared-table-name": {
      severity: "error",
      messages: {
        default: paramMessage`Shared workspace table name "${"table"}" is used by more than one of queueTable/inputsTable/varsTable/controlTable; the four names must be distinct.`,
      },
    },
    "shared-table-name-collision": {
      severity: "error",
      messages: {
        default: paramMessage`${"option"} "${"table"}" collides with a derived call/result/child table name; pick a name no host method derives.`,
      },
    },
    "invalid-column-name": {
      severity: "error",
      messages: {
        default: paramMessage`${"option"} must be a non-empty column name.`,
      },
    },
    "invalid-done-status-value": {
      severity: "error",
      messages: {
        default: "doneStatusValue must be a non-empty status literal.",
      },
    },
    "duplicate-column-name": {
      severity: "error",
      messages: {
        default: paramMessage`Column name "${"column"}" is configured for more than one ${"table"} table column; column names must be mutually distinct within each table.`,
      },
    },
    "column-name-collision": {
      severity: "error",
      messages: {
        default: paramMessage`${"option"} "${"column"}" collides with a derived input/result field column; pick a name no host method field derives.`,
      },
    },
  },
});

export const { reportDiagnostic, createDiagnostic } = $lib;

/**
 * Decorator state keys. Deliberately created with Symbol.for so the
 * frontend and tests read the same program state even if module
 * resolution ever loads two copies of this package's JS.
 */
export const stateKeys = {
  hostLibrary: Symbol.for("@sqlite-host/typespec.hostLibrary"),
  hostMethod: Symbol.for("@sqlite-host/typespec.hostMethod"),
  sqlName: Symbol.for("@sqlite-host/typespec.sqlName"),
} as const;
