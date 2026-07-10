/**
 * TypeSpec model validation (docs/validation.md §1, plan §24.1). Walks a
 * @hostLibrary interface and rejects unsupported shapes before the
 * frontend builds the IR: non-model top-level input/output, unsupported
 * scalars, nested models, nested lists, optional list fields, unions and
 * maps, duplicate method names, duplicate SQL names per shape, duplicate
 * derived table names, and missing @hostMethod. Diagnostics are reported
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
} from "@sqlite-host/typespec";
import type { NamingIr, ScalarTypeIr } from "./ir.js";
import {
  deriveCallTable,
  deriveInputListTable,
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

/**
 * Validate one @hostLibrary interface against the v1 model rules.
 * Reports diagnostics into the program; returns false when any error
 * was reported.
 */
export function validateHostLibraryInterface(
  program: Program,
  iface: Interface,
  naming: NamingIr,
): boolean {
  const ctx: ValidationContext = { program, ok: true };
  const methodNames = new Set<string>();
  const tableNames = new Set<string>();

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
      const listSqlNames = validateShape(ctx, inputModel);
      if (claimTables) {
        for (const [sqlName, target] of listSqlNames) {
          claimTable(deriveInputListTable(naming, methodName, sqlName), target);
        }
      }
    }

    const resultModel = checkResultModel(ctx, op);
    if (resultModel !== undefined) {
      const listSqlNames = validateShape(ctx, resultModel);
      if (claimTables) {
        for (const [sqlName, target] of listSqlNames) {
          claimTable(deriveResultListTable(naming, methodName, sqlName), target);
        }
      }
    }
  }

  return ctx.ok;
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
 */
function validateShape(
  ctx: ValidationContext,
  model: Model,
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
    if (validateField(ctx, prop)) {
      listFields.push([sqlName, prop]);
    }
  }
  return listFields;
}

/** Validate one field. Returns true when the field is a (valid) list field. */
function validateField(ctx: ValidationContext, prop: ModelProperty): boolean {
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
      validateItemModel(ctx, element, prop);
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

/** List item models may contain only supported scalar fields. */
function validateItemModel(
  ctx: ValidationContext,
  model: Model,
  listProp: ModelProperty,
): void {
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
