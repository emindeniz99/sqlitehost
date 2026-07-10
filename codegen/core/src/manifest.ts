/**
 * Canonical manifest serialization. The manifest is the serialized IR with
 * a pinned key order, 2-space indentation, LF line endings, and a trailing
 * newline. Cross-language golden tests compare this output byte-for-byte
 * against fixtures/manifests/*.manifest.json.
 */

import type {
  HostLibraryIr,
  HostMethodIr,
  ListFieldIr,
  ObjectShapeIr,
  ScalarFieldIr,
} from "./ir.js";

function scalarFieldJson(field: ScalarFieldIr): object {
  return {
    propertyName: field.propertyName,
    sqlName: field.sqlName,
    column: field.column,
    scalarType: field.scalarType,
    optional: field.optional,
  };
}

function listFieldJson(field: ListFieldIr): object {
  return {
    propertyName: field.propertyName,
    sqlName: field.sqlName,
    childTable: field.childTable,
    itemModelName: field.itemModelName,
    itemFields: field.itemFields.map(scalarFieldJson),
  };
}

function shapeJson(shape: ObjectShapeIr): object {
  return {
    modelName: shape.modelName,
    fields: shape.fields.map(scalarFieldJson),
    listFields: shape.listFields.map(listFieldJson),
  };
}

function methodJson(method: HostMethodIr): object {
  return {
    operationName: method.operationName,
    methodName: method.methodName,
    handlerName: method.handlerName,
    apiLevel: method.apiLevel,
    callTable: method.callTable,
    resultTable: method.resultTable,
    queueTrigger: method.queueTrigger,
    input: shapeJson(method.input),
    result: shapeJson(method.result),
  };
}

/** Serialize the IR to canonical manifest JSON (with trailing newline). */
export function serializeManifest(ir: HostLibraryIr): string {
  const manifest = {
    manifestVersion: ir.manifestVersion,
    engine: ir.engine,
    library: {
      namespace: ir.library.namespace,
      interfaceName: ir.library.interfaceName,
      apiLevel: ir.library.apiLevel,
      minSqliteVersionNumber: ir.library.minSqliteVersionNumber,
      features: ir.library.features,
    },
    naming: {
      callTablePrefix: ir.naming.callTablePrefix,
      resultTablePrefix: ir.naming.resultTablePrefix,
      inputColumnPrefix: ir.naming.inputColumnPrefix,
      resultColumnPrefix: ir.naming.resultColumnPrefix,
      inputListTableInfix: ir.naming.inputListTableInfix,
      resultListTableInfix: ir.naming.resultListTableInfix,
    },
    columns: {
      callId: ir.columns.callId,
      itemIndex: ir.columns.itemIndex,
      status: ir.columns.status,
      doneValue: ir.columns.doneValue,
      queueId: ir.columns.queueId,
      method: ir.columns.method,
      name: ir.columns.name,
      valueType: ir.columns.valueType,
      intValue: ir.columns.intValue,
      realValue: ir.columns.realValue,
      textValue: ir.columns.textValue,
      blobValue: ir.columns.blobValue,
      action: ir.columns.action,
      message: ir.columns.message,
    },
    queueTable: {
      name: ir.queueTable.name,
      columns: ir.queueTable.columns,
    },
    inputsTable: {
      name: ir.inputsTable.name,
      columns: ir.inputsTable.columns,
    },
    varsTable: {
      name: ir.varsTable.name,
      columns: ir.varsTable.columns,
    },
    controlTable: {
      name: ir.controlTable.name,
      columns: ir.controlTable.columns,
    },
    scriptEnvelope: {
      engine: ir.scriptEnvelope.engine,
      bindingTypes: ir.scriptEnvelope.bindingTypes,
    },
    methods: ir.methods.map(methodJson),
  };
  return JSON.stringify(manifest, null, 2) + "\n";
}

/** Parse a manifest JSON string back into the IR shape (no validation). */
export function parseManifest(json: string): HostLibraryIr {
  return JSON.parse(json) as HostLibraryIr;
}
