/**
 * TypeSpec model validation (docs/validation.md §1, plan §24.1). Walks a
 * @hostLibrary interface and rejects unsupported shapes before the
 * frontend builds the IR: non-model top-level input/output, unsupported
 * scalars, nested models, nested lists, optional list fields, empty list
 * item models, unions and maps, duplicate method names, duplicate SQL
 * names per shape, duplicate derived table names, missing @hostMethod,
 * and invalid shared table / column name configuration (docs/naming.md). Diagnostics are reported
 * with the codes declared by @sqlite-host/typespec.
 */

import {
  isArrayModelType,
  isRecordModelType,
  type DiagnosticTarget,
  type Interface,
  type Model,
  type ModelProperty,
  type Operation,
  type Program,
  type Scalar,
  type Type,
} from "@typespec/compiler";
import {
  getHostMethodOptions,
  getSqlName,
  reportDiagnostic,
  type HostMethodOptions,
} from "@sqlite-host/typespec";
import type { ColumnsIr, NamingIr, ScalarTypeIr } from "./ir.js";
import {
  controlTableColumns,
  namedValueTableColumns,
  queueTableColumns,
} from "./ir.js";
import {
  deriveCallTable,
  deriveFunctionName,
  deriveInputColumn,
  deriveInputListTable,
  deriveResultColumn,
  deriveResultListTable,
  deriveResultTable,
  toSnakeCase,
} from "./naming.js";

const SUPPORTED_SCALARS: Record<string, ScalarTypeIr> = {
  int32: "int32",
  int64: "int64",
  boolean: "boolean",
  string: "string",
  bytes: "bytes",
  float32: "float32",
  float64: "float64",
};

/**
 * SQLite built-in scalar/aggregate function names an inline function
 * name must not collide with (docs/naming.md). Function names are
 * case-insensitive in SQLite, so collision checks compare lowercased.
 */
export const SQLITE_BUILTIN_FUNCTIONS: ReadonlySet<string> = new Set([
  "abs",
  "coalesce",
  "count",
  "sum",
  "min",
  "max",
  "length",
  "lower",
  "upper",
  "printf",
  "random",
  "replace",
  "round",
  "substr",
  "trim",
  "date",
  "time",
  "datetime",
  "ifnull",
  "nullif",
  "instr",
  "hex",
  "quote",
  "total",
  "group_concat",
  "typeof",
  "unicode",
  "char",
  "likelihood",
  "likely",
  "unlikely",
  "last_insert_rowid",
  "changes",
  "sqlite_version",
  "glob",
  "like",
  "zeroblob",
]);

/** Map a std scalar to the IR scalar type; undefined when unsupported. */
export function mapSupportedScalar(
  program: Program,
  scalar: Scalar,
): ScalarTypeIr | undefined {
  if (!program.checker.isStdType(scalar)) {
    return undefined;
  }
  return SUPPORTED_SCALARS[scalar.name];
}

interface ValidationContext {
  program: Program;
  ok: boolean;
}

type DiagnosticReportArg = Parameters<typeof reportDiagnostic>[1];

function error(
  ctx: ValidationContext,
  code: DiagnosticReportArg["code"],
  format: Record<string, string>,
  target: DiagnosticTarget,
): void {
  ctx.ok = false;
  reportDiagnostic(ctx.program, { code, format, target } as DiagnosticReportArg);
}

/** Resolved shared workspace table names (defaults already applied). */
export interface SharedTableNames {
  queueTable: string;
  inputsTable: string;
  varsTable: string;
  controlTable: string;
}

/**
 * Configurable column names: non-empty, and mutually distinct within
 * each runtime-managed table's column set (docs/naming.md). The
 * doneValue literal is only checked for non-emptiness — it is data,
 * not an identifier. Row-identity columns are checked against derived
 * field columns by the caller (validateHostLibraryInterface), once
 * every field column is known.
 */
function validateColumns(
  ctx: ValidationContext,
  columns: ColumnsIr,
  target: DiagnosticTarget,
): void {
  const options: Array<[string, string]> = [
    ["callIdColumn", columns.callId],
    ["itemIndexColumn", columns.itemIndex],
    ["statusColumn", columns.status],
    ["queueIdColumn", columns.queueId],
    ["methodColumn", columns.method],
    ["nameColumn", columns.name],
    ["valueTypeColumn", columns.valueType],
    ["intValueColumn", columns.intValue],
    ["realValueColumn", columns.realValue],
    ["textValueColumn", columns.textValue],
    ["blobValueColumn", columns.blobValue],
    ["actionColumn", columns.action],
    ["messageColumn", columns.message],
  ];
  for (const [option, column] of options) {
    if (column.length === 0) {
      error(ctx, "invalid-column-name", { option }, target);
    }
  }
  if (columns.doneValue.length === 0) {
    error(ctx, "invalid-done-status-value", {}, target);
  }

  const checkDistinct = (table: string, set: string[]) => {
    const seen = new Set<string>();
    for (const column of set) {
      if (column.length === 0) {
        continue; // already reported as invalid-column-name
      }
      if (seen.has(column)) {
        error(ctx, "duplicate-column-name", { column, table }, target);
      } else {
        seen.add(column);
      }
    }
  };
  checkDistinct("queue", queueTableColumns(columns));
  checkDistinct("named-value", namedValueTableColumns(columns));
  checkDistinct("control", controlTableColumns(columns));
  // Method parent tables carry callId (+ status on the result side) —
  // callId/status distinctness is already covered by the queue set.
  // List child tables carry callId + itemIndex.
  checkDistinct("list child", [columns.callId, columns.itemIndex]);
}

/**
 * Validate one @hostLibrary interface against the v1 model rules.
 * Reports diagnostics into the program; returns false when any error
 * was reported.
 */
export function validateHostLibraryInterface(
  program: Program,
  iface: Interface,
  naming: NamingIr,
  sharedTables: SharedTableNames,
  columns: ColumnsIr,
): boolean {
  const ctx: ValidationContext = { program, ok: true };
  const methodNames = new Set<string>();
  const tableNames = new Set<string>();
  const fieldColumns = new Set<string>();
  const functionClaims: Array<[string, DiagnosticTarget]> = [];

  // functionPrefix must be non-empty (docs/naming.md). It is a prefix,
  // not a table, so it joins no other distinctness check.
  if (naming.functionPrefix.length === 0) {
    error(ctx, "invalid-function-prefix", {}, iface);
  }

  // Shared workspace table names: non-empty and mutually distinct
  // (docs/naming.md). Collisions with derived tables are checked after
  // the method loop, once every derived name is known.
  const shared: Array<[keyof SharedTableNames, string]> = [
    ["queueTable", sharedTables.queueTable],
    ["inputsTable", sharedTables.inputsTable],
    ["varsTable", sharedTables.varsTable],
    ["controlTable", sharedTables.controlTable],
  ];
  const seenShared = new Set<string>();
  for (const [option, table] of shared) {
    if (table.length === 0) {
      error(ctx, "invalid-shared-table-name", { option }, iface);
      continue;
    }
    if (seenShared.has(table)) {
      error(ctx, "duplicate-shared-table-name", { table }, iface);
    } else {
      seenShared.add(table);
    }
  }

  validateColumns(ctx, columns, iface);

  const claimTable = (table: string, target: DiagnosticTarget) => {
    if (tableNames.has(table)) {
      error(ctx, "duplicate-table-name", { table }, target);
    } else {
      tableNames.add(table);
    }
  };

  for (const op of iface.operations.values()) {
    const options = getHostMethodOptions(program, op);
    if (options === undefined) {
      error(ctx, "missing-host-method", { operation: op.name }, op);
      continue;
    }

    const methodName = options.name;
    let claimTables = true;
    if (methodNames.has(methodName)) {
      error(ctx, "duplicate-method-name", { name: methodName }, op);
      // The first occurrence already claimed the derived tables; skip
      // re-claiming to avoid a redundant duplicate-table-name cascade.
      claimTables = false;
    } else {
      methodNames.add(methodName);
    }

    if (claimTables) {
      claimTable(deriveCallTable(naming, methodName), op);
      claimTable(deriveResultTable(naming, methodName), op);
    }

    const inputModel = checkInputModel(ctx, op);
    if (inputModel !== undefined) {
      const listSqlNames = validateShape(
        ctx,
        inputModel,
        (sqlName) => deriveInputColumn(naming, sqlName),
        fieldColumns,
      );
      if (claimTables) {
        for (const [sqlName, target] of listSqlNames) {
          claimTable(deriveInputListTable(naming, methodName, sqlName), target);
        }
      }
    }

    const resultModel = checkResultModel(ctx, op);
    if (resultModel !== undefined) {
      const listSqlNames = validateShape(
        ctx,
        resultModel,
        (sqlName) => deriveResultColumn(naming, sqlName),
        fieldColumns,
      );
      if (claimTables) {
        for (const [sqlName, target] of listSqlNames) {
          claimTable(deriveResultListTable(naming, methodName, sqlName), target);
        }
      }
    }

    if (inputModel !== undefined && resultModel !== undefined) {
      const functionName = analyzeInlineExposure(
        ctx,
        naming,
        op,
        options,
        inputModel,
        resultModel,
      );
      // Duplicate method names already claimed their function name via
      // the first occurrence; skip re-claiming to avoid a redundant
      // duplicate-function-name cascade (mirrors claimTables above).
      if (functionName !== undefined && claimTables) {
        functionClaims.push([functionName, op]);
      }
    }
  }

  // Function-name collision checks (docs/naming.md) run once every
  // derived table name is known. SQLite resolves function names
  // case-insensitively, so all comparisons are lowercased.
  const tableNamesLower = new Set([...tableNames].map((t) => t.toLowerCase()));
  const seenFunctions = new Set<string>();
  for (const [name, target] of functionClaims) {
    const lower = name.toLowerCase();
    if (seenFunctions.has(lower)) {
      error(ctx, "duplicate-function-name", { name }, target);
    } else {
      seenFunctions.add(lower);
    }
    if (tableNamesLower.has(lower)) {
      error(ctx, "function-name-collision", { name }, target);
    }
    if (SQLITE_BUILTIN_FUNCTIONS.has(lower)) {
      error(ctx, "builtin-function-collision", { name }, target);
    }
  }

  for (const [option, table] of shared) {
    if (tableNames.has(table)) {
      error(ctx, "shared-table-name-collision", { option, table }, iface);
    }
  }

  // Row-identity columns (docs/naming.md) must not collide with any
  // derived input/result field column across all methods.
  const rowIdentity: Array<[string, string]> = [
    ["callIdColumn", columns.callId],
    ["itemIndexColumn", columns.itemIndex],
    ["statusColumn", columns.status],
  ];
  for (const [option, column] of rowIdentity) {
    if (column.length > 0 && fieldColumns.has(column)) {
      error(ctx, "column-name-collision", { option, column }, iface);
    }
  }

  return ctx.ok;
}

/**
 * Inline-exposure analysis for one method (docs/proposals/
 * inline-host-functions.md). Eligibility: mutates: false, scalar-only
 * input with trailing optionals, and exactly one scalar result field
 * (no lists on either side). Ineligible methods are silently not
 * exposed unless inline exposure was explicitly requested (inline: true
 * or functionName set), in which case each failed rule is a diagnostic.
 * Returns the function name the method claims, or undefined when the
 * method is not exposed.
 */
function analyzeInlineExposure(
  ctx: ValidationContext,
  naming: NamingIr,
  op: Operation,
  options: HostMethodOptions,
  inputModel: Model,
  resultModel: Model,
): string | undefined {
  const mutates = options.mutates ?? true;
  const requested = options.inline === true || options.functionName !== undefined;
  let eligible = !mutates;
  if (requested && mutates) {
    error(ctx, "inline-mutating-method", { operation: op.name }, op);
  }

  const listFields = (model: Model, side: "input" | "result") => {
    for (const prop of model.properties.values()) {
      if (prop.type.kind === "Model" && isArrayModelType(ctx.program, prop.type)) {
        eligible = false;
        if (requested) {
          error(
            ctx,
            "inline-list-field",
            { operation: op.name, side, field: prop.name },
            prop,
          );
        }
      }
    }
  };
  listFields(inputModel, "input");
  listFields(resultModel, "result");

  const resultScalarCount = [...resultModel.properties.values()].filter(
    (prop) => prop.type.kind === "Scalar",
  ).length;
  if (resultScalarCount !== 1) {
    eligible = false;
    if (requested) {
      error(
        ctx,
        "inline-result-not-single-scalar",
        { operation: op.name, count: String(resultScalarCount) },
        op,
      );
    }
  }

  // Function arguments are the input fields in declaration order, so
  // optional fields must be trailing (omitted trailing args = null).
  let optionalSeen = false;
  for (const prop of inputModel.properties.values()) {
    if (prop.type.kind !== "Scalar") {
      continue;
    }
    if (prop.optional) {
      optionalSeen = true;
    } else if (optionalSeen) {
      eligible = false;
      if (requested) {
        error(
          ctx,
          "inline-required-after-optional",
          { operation: op.name, field: prop.name },
          prop,
        );
      }
      break;
    }
  }

  if (!eligible || options.inline === false) {
    return undefined;
  }
  return options.functionName ?? deriveFunctionName(naming, options.name);
}

function isNamedPlainModel(program: Program, type: Type): type is Model {
  return (
    type.kind === "Model" &&
    type.name !== "" &&
    !isArrayModelType(program, type) &&
    !isRecordModelType(program, type)
  );
}

function checkInputModel(
  ctx: ValidationContext,
  op: Operation,
): Model | undefined {
  const params = [...op.parameters.properties.values()];
  if (params.length !== 1) {
    error(
      ctx,
      "invalid-method-shape",
      {
        operation: op.name,
        detail: `expected exactly one input parameter, got ${params.length}.`,
      },
      op,
    );
    return undefined;
  }
  const param = params[0];
  if (param.optional) {
    error(
      ctx,
      "invalid-method-shape",
      { operation: op.name, detail: "the input parameter cannot be optional." },
      param,
    );
    return undefined;
  }
  if (!isNamedPlainModel(ctx.program, param.type)) {
    error(
      ctx,
      "invalid-method-shape",
      { operation: op.name, detail: "the input parameter must be a named model." },
      param,
    );
    return undefined;
  }
  return param.type;
}

function checkResultModel(
  ctx: ValidationContext,
  op: Operation,
): Model | undefined {
  if (!isNamedPlainModel(ctx.program, op.returnType)) {
    error(
      ctx,
      "invalid-method-shape",
      { operation: op.name, detail: "the return type must be a named model." },
      op,
    );
    return undefined;
  }
  return op.returnType;
}

/**
 * Validate one input/result shape. Returns the list-field SQL names (with
 * their diagnostic targets) so the caller can claim derived child tables.
 * Every scalar field's derived physical column (parent and list item)
 * is added to `fieldColumns` for the row-identity collision check.
 */
function validateShape(
  ctx: ValidationContext,
  model: Model,
  deriveColumn: (sqlName: string) => string,
  fieldColumns: Set<string>,
): Array<[string, ModelProperty]> {
  const sqlNames = new Set<string>();
  const listFields: Array<[string, ModelProperty]> = [];

  for (const prop of model.properties.values()) {
    const sqlName = getSqlName(ctx.program, prop) ?? toSnakeCase(prop.name);
    if (sqlNames.has(sqlName)) {
      error(ctx, "duplicate-sql-name", { name: sqlName, model: model.name }, prop);
    } else {
      sqlNames.add(sqlName);
    }
    if (prop.type.kind === "Scalar") {
      fieldColumns.add(deriveColumn(sqlName));
    }
    if (validateField(ctx, prop, deriveColumn, fieldColumns)) {
      listFields.push([sqlName, prop]);
    }
  }
  return listFields;
}

/** Validate one field. Returns true when the field is a (valid) list field. */
function validateField(
  ctx: ValidationContext,
  prop: ModelProperty,
  deriveColumn: (sqlName: string) => string,
  fieldColumns: Set<string>,
): boolean {
  const program = ctx.program;
  const type = prop.type;

  if (type.kind === "Scalar") {
    if (mapSupportedScalar(program, type) === undefined) {
      error(
        ctx,
        "unsupported-scalar",
        { type: type.name, field: prop.name },
        prop,
      );
    }
    return false;
  }

  if (type.kind === "Model") {
    if (isArrayModelType(program, type)) {
      if (prop.optional) {
        error(ctx, "optional-list", { field: prop.name }, prop);
      }
      const element = type.indexer.value;
      if (element.kind === "Model" && isArrayModelType(program, element)) {
        error(ctx, "nested-list", { field: prop.name }, prop);
        return false;
      }
      if (!isNamedPlainModel(program, element)) {
        error(ctx, "invalid-list-item", { field: prop.name }, prop);
        return false;
      }
      validateItemModel(ctx, element, prop, deriveColumn, fieldColumns);
      return true;
    }
    if (isRecordModelType(program, type)) {
      error(
        ctx,
        "unsupported-field-type",
        { field: prop.name, kind: "Record" },
        prop,
      );
      return false;
    }
    error(ctx, "nested-model", { field: prop.name }, prop);
    return false;
  }

  error(ctx, "unsupported-field-type", { field: prop.name, kind: type.kind }, prop);
  return false;
}

/** List item models must declare at least one supported scalar field. */
function validateItemModel(
  ctx: ValidationContext,
  model: Model,
  listProp: ModelProperty,
  deriveColumn: (sqlName: string) => string,
  fieldColumns: Set<string>,
): void {
  if (model.properties.size === 0) {
    error(
      ctx,
      "empty-list-item",
      { field: listProp.name, model: model.name },
      listProp,
    );
  }
  const sqlNames = new Set<string>();
  for (const prop of model.properties.values()) {
    const sqlName = getSqlName(ctx.program, prop) ?? toSnakeCase(prop.name);
    if (sqlNames.has(sqlName)) {
      error(ctx, "duplicate-sql-name", { name: sqlName, model: model.name }, prop);
    } else {
      sqlNames.add(sqlName);
    }
    const type = prop.type;
    if (type.kind === "Scalar") {
      fieldColumns.add(deriveColumn(sqlName));
      if (mapSupportedScalar(ctx.program, type) === undefined) {
        error(
          ctx,
          "unsupported-scalar",
          { type: type.name, field: prop.name },
          prop,
        );
      }
      continue;
    }
    if (type.kind === "Model" && isArrayModelType(ctx.program, type)) {
      error(ctx, "nested-list", { field: `${listProp.name}.${prop.name}` }, prop);
      continue;
    }
    if (type.kind === "Model" && !isRecordModelType(ctx.program, type)) {
      error(ctx, "nested-model", { field: `${listProp.name}.${prop.name}` }, prop);
      continue;
    }
    error(
      ctx,
      "unsupported-field-type",
      { field: `${listProp.name}.${prop.name}`, kind: type.kind },
      prop,
    );
  }
}
