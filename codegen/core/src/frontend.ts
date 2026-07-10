/**
 * TypeSpec frontend: compiles a .tsp entrypoint (or walks an already
 * compiled Program), resolves the @sqlite-host/typespec decorators, and
 * normalizes each @hostLibrary interface into the language-neutral
 * HostLibraryIr. All physical names are resolved here via naming.ts —
 * emitters never derive names themselves (docs/manifest.md).
 *
 * One compilation may define multiple @hostLibrary interfaces, each an
 * independent runtime definition with its own workspace
 * (docs/manifest.md); use compileHostLibraries / buildHostLibraryIrs
 * for that. The single-library APIs (compileHostLibrary /
 * buildHostLibraryIr) keep their original semantics and error on
 * multi-library programs.
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
  COLUMNS_V1,
  CONTROL_TABLE_V1,
  controlTableColumns,
  DEFAULT_MIN_SQLITE_VERSION_NUMBER,
  ENGINE_V1,
  FEATURES_V1,
  INPUTS_TABLE_V1,
  namedValueTableColumns,
  QUEUE_TABLE_V1,
  queueTableColumns,
  VARS_TABLE_V1,
  type ColumnsIr,
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
import {
  mapSupportedScalar,
  validateHostLibraryInterface,
  type SharedTableNames,
} from "./validate.js";

export interface FrontendResult {
  program: Program;
  /** Undefined when compilation or model validation reported errors. */
  ir?: HostLibraryIr;
  diagnostics: readonly Diagnostic[];
}

export interface FrontendLibrariesResult {
  program: Program;
  /**
   * One IR per @hostLibrary interface, in declaration order. Undefined
   * when compilation or model validation reported errors.
   */
  irs?: HostLibraryIr[];
  diagnostics: readonly Diagnostic[];
}

/**
 * Compile a .tsp entrypoint with the Node host and normalize the
 * single @hostLibrary interface into the IR. Diagnostics (TypeSpec
 * compile errors plus SqliteHost model validation) are returned; `ir`
 * is only set when there are no errors. Errors when the compilation
 * defines more than one @hostLibrary interface — use
 * compileHostLibraries for multi-library programs.
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
 * Compile a .tsp entrypoint with the Node host and normalize every
 * @hostLibrary interface into one IR each, in declaration order. `irs`
 * is only set when there are no errors.
 */
export async function compileHostLibraries(
  entrypoint: string,
): Promise<FrontendLibrariesResult> {
  const program = await compile(NodeHost, resolve(entrypoint), { emit: [] });
  let irs: HostLibraryIr[] | undefined;
  if (!program.hasError()) {
    irs = buildHostLibraryIrs(program);
  }
  return { program, irs, diagnostics: program.diagnostics };
}

/**
 * Normalize an already compiled single-library Program (e.g. inside a
 * TypeSpec emitter's $onEmit). Reports diagnostics into the program and
 * returns undefined when validation fails. Kept single-library for
 * back-compat: a multi-library program reports multiple-host-libraries
 * (directing callers to the plural API) instead of silently picking one.
 */
export function buildHostLibraryIr(program: Program): HostLibraryIr | undefined {
  const interfaces = getHostLibraryInterfaces(program);
  if (interfaces.length > 1) {
    reportDiagnostic(program, {
      code: "multiple-host-libraries",
      format: { count: String(interfaces.length) },
      target: interfaces[1],
    });
    return undefined;
  }
  return buildHostLibraryIrs(program)?.[0];
}

/**
 * Normalize an already compiled Program into one IR per @hostLibrary
 * interface, in declaration order. Interface names must be unique
 * across the compilation (they name the emitted artifacts); derived
 * table names may collide *across* libraries because each library is an
 * independent workspace. Reports diagnostics into the program and
 * returns undefined when any library fails validation.
 */
export function buildHostLibraryIrs(
  program: Program,
): HostLibraryIr[] | undefined {
  const interfaces = getHostLibraryInterfaces(program);
  if (interfaces.length === 0) {
    reportDiagnostic(program, { code: "no-host-library", target: NoTarget });
    return undefined;
  }

  let ok = true;
  const names = new Set<string>();
  for (const iface of interfaces) {
    if (names.has(iface.name)) {
      reportDiagnostic(program, {
        code: "duplicate-host-library-name",
        format: { name: iface.name },
        target: iface,
      });
      ok = false;
    } else {
      names.add(iface.name);
    }
  }

  const irs: HostLibraryIr[] = [];
  for (const iface of interfaces) {
    const ir = buildLibraryIr(program, iface);
    if (ir === undefined) {
      ok = false;
    } else {
      irs.push(ir);
    }
  }
  return ok ? irs : undefined;
}

function buildLibraryIr(
  program: Program,
  iface: Interface,
): HostLibraryIr | undefined {
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
  // Shared workspace table names and column names are host-level naming
  // too: names resolve here (defaults from the protocol v1 constants)
  // and flow into every table's column list (docs/naming.md).
  const sharedTables: SharedTableNames = {
    queueTable: options.queueTable ?? QUEUE_TABLE_V1.name,
    inputsTable: options.inputsTable ?? INPUTS_TABLE_V1.name,
    varsTable: options.varsTable ?? VARS_TABLE_V1.name,
    controlTable: options.controlTable ?? CONTROL_TABLE_V1.name,
  };
  const columns: ColumnsIr = {
    callId: options.callIdColumn ?? COLUMNS_V1.callId,
    itemIndex: options.itemIndexColumn ?? COLUMNS_V1.itemIndex,
    status: options.statusColumn ?? COLUMNS_V1.status,
    doneValue: options.doneStatusValue ?? COLUMNS_V1.doneValue,
    queueId: options.queueIdColumn ?? COLUMNS_V1.queueId,
    method: options.methodColumn ?? COLUMNS_V1.method,
    name: options.nameColumn ?? COLUMNS_V1.name,
    valueType: options.valueTypeColumn ?? COLUMNS_V1.valueType,
    intValue: options.intValueColumn ?? COLUMNS_V1.intValue,
    realValue: options.realValueColumn ?? COLUMNS_V1.realValue,
    textValue: options.textValueColumn ?? COLUMNS_V1.textValue,
    blobValue: options.blobValueColumn ?? COLUMNS_V1.blobValue,
    action: options.actionColumn ?? COLUMNS_V1.action,
    message: options.messageColumn ?? COLUMNS_V1.message,
  };

  if (!validateHostLibraryInterface(program, iface, naming, sharedTables, columns)) {
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
    columns,
    queueTable: {
      name: sharedTables.queueTable,
      columns: queueTableColumns(columns),
    },
    inputsTable: {
      name: sharedTables.inputsTable,
      columns: namedValueTableColumns(columns),
    },
    varsTable: {
      name: sharedTables.varsTable,
      columns: namedValueTableColumns(columns),
    },
    controlTable: {
      name: sharedTables.controlTable,
      columns: controlTableColumns(columns),
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
