/**
 * TypeSpec frontend: compiles a .tsp entrypoint (or walks an already
 * compiled Program), resolves the @sqlite-host/typespec decorators, and
 * normalizes the @hostLibrary interface into the language-neutral
 * HostLibraryIr. All physical names are resolved here via naming.ts —
 * emitters never derive names themselves (docs/manifest.md).
 */

import { resolve } from "node:path";
import {
  compile,
  getNamespaceFullName,
  isArrayModelType,
  NodeHost,
  NoTarget,
  type Diagnostic,
  type Interface,
  type Model,
  type Operation,
  type Program,
  type Scalar,
} from "@typespec/compiler";
import {
  getHostLibraryInterfaces,
  getHostLibraryOptions,
  getHostMethodOptions,
  getSqlName,
  parseSqliteVersionNumber,
  reportDiagnostic,
} from "@sqlite-host/typespec";
import {
  BINDING_TYPES_V1,
  DEFAULT_MIN_SQLITE_VERSION_NUMBER,
  ENGINE_V1,
  FEATURES_V1,
  INPUTS_TABLE_V1,
  QUEUE_TABLE_V1,
  VARS_TABLE_V1,
  type HostLibraryIr,
  type HostMethodIr,
  type ListFieldIr,
  type NamingIr,
  type ObjectShapeIr,
  type ScalarFieldIr,
} from "./ir.js";
import {
  DEFAULT_NAMING,
  deriveCallTable,
  deriveInputColumn,
  deriveInputListTable,
  deriveQueueTrigger,
  deriveResultColumn,
  deriveResultListTable,
  deriveResultTable,
  toSnakeCase,
} from "./naming.js";
import { mapSupportedScalar, validateHostLibraryInterface } from "./validate.js";

export interface FrontendResult {
  program: Program;
  /** Undefined when compilation or model validation reported errors. */
  ir?: HostLibraryIr;
  diagnostics: readonly Diagnostic[];
}

/**
 * Compile a .tsp entrypoint with the Node host and normalize the
 * @hostLibrary interface into the IR. Diagnostics (TypeSpec compile
 * errors plus SqliteHost model validation) are returned; `ir` is only
 * set when there are no errors.
 */
export async function compileHostLibrary(
  entrypoint: string,
): Promise<FrontendResult> {
  const program = await compile(NodeHost, resolve(entrypoint), { emit: [] });
  let ir: HostLibraryIr | undefined;
  if (!program.hasError()) {
    ir = buildHostLibraryIr(program);
  }
  return { program, ir, diagnostics: program.diagnostics };
}

/**
 * Normalize an already compiled Program (e.g. inside a TypeSpec emitter's
 * $onEmit). Reports diagnostics into the program and returns undefined
 * when validation fails.
 */
export function buildHostLibraryIr(program: Program): HostLibraryIr | undefined {
  const interfaces = getHostLibraryInterfaces(program);
  if (interfaces.length === 0) {
    reportDiagnostic(program, { code: "no-host-library", target: NoTarget });
    return undefined;
  }
  if (interfaces.length > 1) {
    reportDiagnostic(program, {
      code: "multiple-host-libraries",
      format: { count: String(interfaces.length) },
      target: interfaces[1],
    });
    return undefined;
  }

  const iface = interfaces[0];
  const options = getHostLibraryOptions(program, iface)!;
  const naming: NamingIr = {
    callTablePrefix: options.callTablePrefix ?? DEFAULT_NAMING.callTablePrefix,
    resultTablePrefix:
      options.resultTablePrefix ?? DEFAULT_NAMING.resultTablePrefix,
    inputColumnPrefix:
      options.inputColumnPrefix ?? DEFAULT_NAMING.inputColumnPrefix,
    resultColumnPrefix:
      options.resultColumnPrefix ?? DEFAULT_NAMING.resultColumnPrefix,
    inputListTableInfix:
      options.inputListTableInfix ?? DEFAULT_NAMING.inputListTableInfix,
    resultListTableInfix:
      options.resultListTableInfix ?? DEFAULT_NAMING.resultListTableInfix,
  };

  if (!validateHostLibraryInterface(program, iface, naming)) {
    return undefined;
  }

  const methods: HostMethodIr[] = [];
  for (const op of iface.operations.values()) {
    methods.push(buildMethod(program, naming, options.apiLevel, op));
  }

  // An invalid minSqliteVersion string was already rejected by the
  // decorator (invalid-min-sqlite-version); the program has errors and
  // callers never emit from it, so the default stands in here.
  const minSqliteVersionNumber =
    options.minSqliteVersion !== undefined
      ? parseSqliteVersionNumber(options.minSqliteVersion) ??
        DEFAULT_MIN_SQLITE_VERSION_NUMBER
      : DEFAULT_MIN_SQLITE_VERSION_NUMBER;

  return {
    manifestVersion: 1,
    engine: ENGINE_V1,
    library: {
      namespace: buildNamespaceName(iface),
      interfaceName: iface.name,
      apiLevel: options.apiLevel,
      minSqliteVersionNumber,
      features: [...FEATURES_V1],
    },
    naming,
    queueTable: {
      name: QUEUE_TABLE_V1.name,
      columns: [...QUEUE_TABLE_V1.columns],
    },
    inputsTable: {
      name: INPUTS_TABLE_V1.name,
      columns: [...INPUTS_TABLE_V1.columns],
    },
    varsTable: {
      name: VARS_TABLE_V1.name,
      columns: [...VARS_TABLE_V1.columns],
    },
    scriptEnvelope: {
      engine: ENGINE_V1,
      bindingTypes: [...BINDING_TYPES_V1],
    },
    methods,
  };
}

function buildNamespaceName(iface: Interface): string {
  return iface.namespace !== undefined
    ? getNamespaceFullName(iface.namespace)
    : "";
}

function buildMethod(
  program: Program,
  naming: NamingIr,
  libraryApiLevel: number,
  op: Operation,
): HostMethodIr {
  const options = getHostMethodOptions(program, op)!;
  const methodName = options.name;
  const inputModel = [...op.parameters.properties.values()][0].type as Model;
  const resultModel = op.returnType as Model;
  return {
    operationName: op.name,
    methodName,
    handlerName: options.handler,
    apiLevel: options.apiLevel ?? libraryApiLevel,
    callTable: deriveCallTable(naming, methodName),
    resultTable: deriveResultTable(naming, methodName),
    queueTrigger: deriveQueueTrigger(naming, methodName),
    input: buildShape(program, naming, methodName, inputModel, "input"),
    result: buildShape(program, naming, methodName, resultModel, "result"),
  };
}

function buildShape(
  program: Program,
  naming: NamingIr,
  methodName: string,
  model: Model,
  side: "input" | "result",
): ObjectShapeIr {
  const fields: ScalarFieldIr[] = [];
  const listFields: ListFieldIr[] = [];

  for (const prop of model.properties.values()) {
    const sqlName = getSqlName(program, prop) ?? toSnakeCase(prop.name);
    const type = prop.type;
    if (type.kind === "Scalar") {
      fields.push(buildScalarField(program, naming, side, prop.name, sqlName, type, prop.optional));
    } else if (type.kind === "Model" && isArrayModelType(program, type)) {
      const element = type.indexer.value as Model;
      const itemFields: ScalarFieldIr[] = [];
      for (const itemProp of element.properties.values()) {
        const itemSqlName =
          getSqlName(program, itemProp) ?? toSnakeCase(itemProp.name);
        itemFields.push(
          buildScalarField(
            program,
            naming,
            side,
            itemProp.name,
            itemSqlName,
            itemProp.type as Scalar,
            itemProp.optional,
          ),
        );
      }
      listFields.push({
        propertyName: prop.name,
        sqlName,
        childTable:
          side === "input"
            ? deriveInputListTable(naming, methodName, sqlName)
            : deriveResultListTable(naming, methodName, sqlName),
        itemModelName: element.name,
        itemFields,
      });
    }
  }

  return { modelName: model.name, fields, listFields };
}

function buildScalarField(
  program: Program,
  naming: NamingIr,
  side: "input" | "result",
  propertyName: string,
  sqlName: string,
  scalar: Scalar,
  optional: boolean,
): ScalarFieldIr {
  return {
    propertyName,
    sqlName,
    column:
      side === "input"
        ? deriveInputColumn(naming, sqlName)
        : deriveResultColumn(naming, sqlName),
    scalarType: mapSupportedScalar(program, scalar)!,
    optional,
  };
}
