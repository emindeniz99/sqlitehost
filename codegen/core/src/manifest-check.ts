/**
 * Structural validation of a manifest before it becomes an IR.
 *
 * A manifest is a committed, hand-editable, merge-conflict-prone file
 * that `docs/guides/getting-started.md` tells users to feed to all three
 * language emitters. Every emitter funnels through `parseManifest`, so
 * this one check covers all of them.
 *
 * What it does NOT do, deliberately: re-derive physical names. A
 * manifest carries resolved names precisely so a host can carry a legacy
 * table or column name the naming conventions would never produce
 * (see the csharp-emitter's resolved-name tests). Only structure,
 * types, ranges and uniqueness are checked here — the rules whose
 * violation makes an emitter write something no compiler accepts.
 *
 * The TypeSpec-side rules live in validate.ts and cannot be reused: they
 * walk a compiled Program, not an IR. What IS shared is the source of
 * the constants — the scalar type list and the identifier patterns come
 * from ir.ts, so the two paths cannot drift.
 */

import type { HostLibraryIr, ScalarTypeIr } from "./ir.js";

const SCALAR_TYPES: readonly ScalarTypeIr[] = [
  "int32",
  "int64",
  "boolean",
  "string",
  "bytes",
  "float32",
  "float64",
];

const TOP_LEVEL_KEYS = [
  "manifestVersion",
  "engine",
  "library",
  "naming",
  "columns",
  "queueTable",
  "inputsTable",
  "varsTable",
  "controlTable",
  "scriptEnvelope",
  "methods",
] as const;

const LIBRARY_KEYS = [
  "namespace",
  "interfaceName",
  "apiLevel",
  "minSqliteVersionNumber",
  "features",
] as const;

const NAMING_KEYS = [
  "callTablePrefix",
  "resultTablePrefix",
  "inputColumnPrefix",
  "resultColumnPrefix",
  "inputListTableInfix",
  "resultListTableInfix",
  "functionPrefix",
] as const;

const COLUMN_KEYS = [
  "callId",
  "itemIndex",
  "status",
  "doneValue",
  "queueId",
  "method",
  "name",
  "valueType",
  "intValue",
  "realValue",
  "textValue",
  "blobValue",
  "action",
  "message",
] as const;

/** Every problem found in one manifest, with the JSON path of each. */
export class ManifestValidationError extends Error {
  readonly problems: readonly string[];

  constructor(problems: readonly string[]) {
    super(
      `manifest is not a valid host library IR (${problems.length} problem${
        problems.length === 1 ? "" : "s"
      }):\n  ${problems.join("\n  ")}`,
    );
    this.name = "ManifestValidationError";
    this.problems = problems;
  }
}

class Checker {
  readonly problems: string[] = [];

  fail(path: string, detail: string): void {
    this.problems.push(`${path}: ${detail}`);
  }

  /** Returns the value when it is a plain object, else records a problem. */
  object(value: unknown, path: string): Record<string, unknown> | undefined {
    if (typeof value !== "object" || value === null || Array.isArray(value)) {
      this.fail(path, `expected an object, got ${describe(value)}`);
      return undefined;
    }
    return value as Record<string, unknown>;
  }

  array(value: unknown, path: string): unknown[] | undefined {
    if (!Array.isArray(value)) {
      this.fail(path, `expected an array, got ${describe(value)}`);
      return undefined;
    }
    return value;
  }

  string(value: unknown, path: string): string | undefined {
    if (typeof value !== "string") {
      this.fail(path, `expected a string, got ${describe(value)}`);
      return undefined;
    }
    return value;
  }

  nonEmptyString(value: unknown, path: string): string | undefined {
    const s = this.string(value, path);
    if (s !== undefined && s.length === 0) {
      this.fail(path, "expected a non-empty string");
      return undefined;
    }
    return s;
  }

  integer(value: unknown, path: string): number | undefined {
    if (typeof value !== "number" || !Number.isInteger(value)) {
      this.fail(path, `expected an integer, got ${describe(value)}`);
      return undefined;
    }
    return value;
  }

  boolean(value: unknown, path: string): boolean | undefined {
    if (typeof value !== "boolean") {
      this.fail(path, `expected a boolean, got ${describe(value)}`);
      return undefined;
    }
    return value;
  }

  stringArray(value: unknown, path: string): void {
    const items = this.array(value, path);
    if (items === undefined) {
      return;
    }
    items.forEach((item, i) => this.string(item, `${path}[${i}]`));
  }

  /** Every declared key present and a non-empty string; no others. */
  stringRecord(value: unknown, path: string, keys: readonly string[]): void {
    const obj = this.object(value, path);
    if (obj === undefined) {
      return;
    }
    for (const key of keys) {
      this.nonEmptyString(obj[key], `${path}.${key}`);
    }
  }

  table(value: unknown, path: string): void {
    const obj = this.object(value, path);
    if (obj === undefined) {
      return;
    }
    this.nonEmptyString(obj.name, `${path}.name`);
    this.stringArray(obj.columns, `${path}.columns`);
  }
}

function describe(value: unknown): string {
  if (value === undefined) {
    return "undefined";
  }
  if (value === null) {
    return "null";
  }
  if (Array.isArray(value)) {
    return "an array";
  }
  return `${typeof value} ${JSON.stringify(value)}`;
}

/** Fields of one input/result shape; returns nothing, records problems. */
function checkShape(c: Checker, value: unknown, path: string): string[] {
  const tables: string[] = [];
  const shape = c.object(value, path);
  if (shape === undefined) {
    return tables;
  }
  c.nonEmptyString(shape.modelName, `${path}.modelName`);

  // sqlNames name the columns of one table, so they must be unique
  // within the shape — the parent fields and the list fields share that
  // namespace exactly as they do in the frontend.
  const sqlNames = new Set<string>();
  const claimSqlName = (name: string | undefined, at: string) => {
    if (name === undefined) {
      return;
    }
    if (sqlNames.has(name)) {
      c.fail(at, `duplicate sqlName "${name}" within this shape`);
    } else {
      sqlNames.add(name);
    }
  };

  const fields = c.array(shape.fields, `${path}.fields`);
  fields?.forEach((field, i) => {
    claimSqlName(checkScalarField(c, field, `${path}.fields[${i}]`), `${path}.fields[${i}].sqlName`);
  });

  const listFields = c.array(shape.listFields, `${path}.listFields`);
  listFields?.forEach((value, i) => {
    const at = `${path}.listFields[${i}]`;
    const list = c.object(value, at);
    if (list === undefined) {
      return;
    }
    c.nonEmptyString(list.propertyName, `${at}.propertyName`);
    claimSqlName(c.nonEmptyString(list.sqlName, `${at}.sqlName`), `${at}.sqlName`);
    const childTable = c.nonEmptyString(list.childTable, `${at}.childTable`);
    if (childTable !== undefined) {
      tables.push(childTable);
    }
    c.nonEmptyString(list.itemModelName, `${at}.itemModelName`);
    const itemFields = c.array(list.itemFields, `${at}.itemFields`);
    const itemSqlNames = new Set<string>();
    itemFields?.forEach((item, j) => {
      const itemAt = `${at}.itemFields[${j}]`;
      const sqlName = checkScalarField(c, item, itemAt);
      if (sqlName === undefined) {
        return;
      }
      if (itemSqlNames.has(sqlName)) {
        c.fail(`${itemAt}.sqlName`, `duplicate sqlName "${sqlName}" within this item model`);
      } else {
        itemSqlNames.add(sqlName);
      }
    });
  });

  return tables;
}

/** Returns the field's sqlName when it is usable, else undefined. */
function checkScalarField(c: Checker, value: unknown, path: string): string | undefined {
  const field = c.object(value, path);
  if (field === undefined) {
    return undefined;
  }
  c.nonEmptyString(field.propertyName, `${path}.propertyName`);
  const sqlName = c.nonEmptyString(field.sqlName, `${path}.sqlName`);
  c.nonEmptyString(field.column, `${path}.column`);
  checkScalarType(c, field.scalarType, `${path}.scalarType`);
  c.boolean(field.optional, `${path}.optional`);
  return sqlName;
}

function checkScalarType(c: Checker, value: unknown, path: string): void {
  if (typeof value !== "string" || !SCALAR_TYPES.includes(value as ScalarTypeIr)) {
    c.fail(
      path,
      `expected one of ${SCALAR_TYPES.join(", ")}, got ${describe(value)}`,
    );
  }
}

function checkInline(c: Checker, value: unknown, path: string): void {
  if (value === null) {
    return;
  }
  const inline = c.object(value, path);
  if (inline === undefined) {
    return;
  }
  c.nonEmptyString(inline.functionName, `${path}.functionName`);
  const args = c.array(inline.args, `${path}.args`);
  args?.forEach((arg, i) => {
    const at = `${path}.args[${i}]`;
    const obj = c.object(arg, at);
    if (obj === undefined) {
      return;
    }
    c.nonEmptyString(obj.propertyName, `${at}.propertyName`);
    c.nonEmptyString(obj.sqlName, `${at}.sqlName`);
    checkScalarType(c, obj.scalarType, `${at}.scalarType`);
    c.boolean(obj.optional, `${at}.optional`);
  });

  const minArgs = c.integer(inline.minArgs, `${path}.minArgs`);
  const maxArgs = c.integer(inline.maxArgs, `${path}.maxArgs`);
  if (minArgs !== undefined && minArgs < 0) {
    c.fail(`${path}.minArgs`, `expected a non-negative integer, got ${minArgs}`);
  }
  // An inverted arity is not a cosmetic error: the runtime registers the
  // pair verbatim and the authoring lint then rejects EVERY call to the
  // function, which is indistinguishable from the function not existing.
  if (minArgs !== undefined && maxArgs !== undefined && minArgs > maxArgs) {
    c.fail(`${path}.minArgs`, `minArgs ${minArgs} exceeds maxArgs ${maxArgs}`);
  }
  if (maxArgs !== undefined && args !== undefined && maxArgs > args.length) {
    c.fail(
      `${path}.maxArgs`,
      `maxArgs ${maxArgs} exceeds the ${args.length} declared argument(s)`,
    );
  }

  const returns = c.object(inline.returns, `${path}.returns`);
  if (returns !== undefined) {
    c.nonEmptyString(returns.propertyName, `${path}.returns.propertyName`);
    c.nonEmptyString(returns.sqlName, `${path}.returns.sqlName`);
    checkScalarType(c, returns.scalarType, `${path}.returns.scalarType`);
  }
}

/**
 * Validate a parsed manifest against the IR shape. Throws a single
 * ManifestValidationError listing every problem, each with its JSON
 * path — a hand-edited manifest usually has more than one, and fixing
 * them one exception at a time is the slow way to find that out.
 */
export function checkManifest(value: unknown): asserts value is HostLibraryIr {
  const c = new Checker();
  const root = c.object(value, "$");
  if (root === undefined) {
    throw new ManifestValidationError(c.problems);
  }

  // Unknown top-level keys are rejected rather than ignored: a manifest
  // is generated, so an unrecognized key is a typo or a version skew,
  // and silently dropping it is how a hand-edit gets believed.
  for (const key of Object.keys(root)) {
    if (!(TOP_LEVEL_KEYS as readonly string[]).includes(key)) {
      c.fail(`$.${key}`, "unknown top-level key");
    }
  }

  if (root.manifestVersion !== 1) {
    c.fail("$.manifestVersion", `expected 1, got ${describe(root.manifestVersion)}`);
  }
  c.nonEmptyString(root.engine, "$.engine");

  let libraryApiLevel: number | undefined;
  const library = c.object(root.library, "$.library");
  if (library !== undefined) {
    for (const key of LIBRARY_KEYS) {
      if (!(key in library)) {
        c.fail(`$.library.${key}`, "required key is missing");
      }
    }
    c.string(library.namespace, "$.library.namespace");
    c.nonEmptyString(library.interfaceName, "$.library.interfaceName");
    libraryApiLevel = c.integer(library.apiLevel, "$.library.apiLevel");
    if (libraryApiLevel !== undefined && libraryApiLevel < 1) {
      c.fail("$.library.apiLevel", `expected a positive integer, got ${libraryApiLevel}`);
    }
    c.integer(library.minSqliteVersionNumber, "$.library.minSqliteVersionNumber");
    c.stringArray(library.features, "$.library.features");
  }

  c.stringRecord(root.naming, "$.naming", NAMING_KEYS);
  c.stringRecord(root.columns, "$.columns", COLUMN_KEYS);
  c.table(root.queueTable, "$.queueTable");
  c.table(root.inputsTable, "$.inputsTable");
  c.table(root.varsTable, "$.varsTable");
  c.table(root.controlTable, "$.controlTable");

  const envelope = c.object(root.scriptEnvelope, "$.scriptEnvelope");
  if (envelope !== undefined) {
    c.nonEmptyString(envelope.engine, "$.scriptEnvelope.engine");
    c.stringArray(envelope.bindingTypes, "$.scriptEnvelope.bindingTypes");
  }

  const methods = c.array(root.methods, "$.methods");
  const methodNames = new Set<string>();
  // Table names resolve case-insensitively in SQLite, so uniqueness
  // compares lowercased — the same rule the frontend applies to derived
  // names.
  const tableNames = new Set<string>();
  const claimTable = (table: string, path: string) => {
    const lower = table.toLowerCase();
    if (tableNames.has(lower)) {
      c.fail(path, `duplicate table name "${table}"`);
    } else {
      tableNames.add(lower);
    }
  };

  methods?.forEach((value, i) => {
    const path = `$.methods[${i}]`;
    const method = c.object(value, path);
    if (method === undefined) {
      return;
    }
    c.nonEmptyString(method.operationName, `${path}.operationName`);
    const methodName = c.nonEmptyString(method.methodName, `${path}.methodName`);
    // handlerName lands as a C# interface member and a call site; an
    // absent one emitted the literal `undefined` as a member name.
    c.nonEmptyString(method.handlerName, `${path}.handlerName`);
    c.boolean(method.mutates, `${path}.mutates`);

    if (methodName !== undefined) {
      if (methodNames.has(methodName)) {
        c.fail(`${path}.methodName`, `duplicate method name "${methodName}"`);
      } else {
        methodNames.add(methodName);
      }
    }

    // A method may not require a newer API level than its library: the
    // Java validator and the C# runtime both gate a payload's
    // requiredApiLevel against the LIBRARY level only, so a method above
    // it is either unreachable or a silent gate bypass. The frontend
    // rejects it; nothing re-checked it after a manifest edit.
    const apiLevel = c.integer(method.apiLevel, `${path}.apiLevel`);
    if (apiLevel !== undefined) {
      if (apiLevel < 1) {
        c.fail(`${path}.apiLevel`, `expected a positive integer, got ${apiLevel}`);
      } else if (libraryApiLevel !== undefined && apiLevel > libraryApiLevel) {
        c.fail(
          `${path}.apiLevel`,
          `method apiLevel ${apiLevel} exceeds the library apiLevel ${libraryApiLevel}`,
        );
      }
    }

    for (const key of ["callTable", "resultTable", "queueTrigger"] as const) {
      const name = c.nonEmptyString(method[key], `${path}.${key}`);
      if (name !== undefined && key !== "queueTrigger") {
        claimTable(name, `${path}.${key}`);
      }
    }

    for (const table of checkShape(c, method.input, `${path}.input`)) {
      claimTable(table, `${path}.input.listFields`);
    }
    for (const table of checkShape(c, method.result, `${path}.result`)) {
      claimTable(table, `${path}.result.listFields`);
    }

    if (!("inline" in method)) {
      c.fail(`${path}.inline`, "required key is missing (use null when not exposed)");
    } else {
      checkInline(c, method.inline, `${path}.inline`);
    }
  });

  if (c.problems.length > 0) {
    throw new ManifestValidationError(c.problems);
  }
}
