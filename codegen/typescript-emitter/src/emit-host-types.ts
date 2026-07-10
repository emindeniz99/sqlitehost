/**
 * Emit the per-host authoring module (`<base-name>.ts`): one interface
 * per method input/result model (camelCase properties, optionality from
 * the IR, int64/int32 as the runtime-types value aliases, bytes as a
 * base64 string) plus the precomputed HostMetadata const. The metadata
 * literal mirrors typescript/authoring-sdk `loadHostMetadata` exactly:
 * physical names are read from the IR as-is and only the fixed
 * structural columns (call_id, status, item_index —
 * docs/workspace-schema.md) are added to the table column lists.
 */

import type {
  HostLibraryIr,
  HostMethodIr,
  ListFieldIr,
  ObjectShapeIr,
  ScalarFieldIr,
  ScalarTypeIr,
} from "@sqlite-host/codegen-core";
import {
  docComment,
  generatedHeader,
  renderLiteral,
  type Literal,
} from "./format.js";

const SCALAR_TS_TYPES: Record<ScalarTypeIr, string> = {
  int32: "Int32Value",
  int64: "Int64Value",
  boolean: "boolean",
  string: "string",
  bytes: "string",
};

/** Scalar types whose TS type is imported from @sqlite-host/runtime-types. */
const RUNTIME_VALUE_IMPORTS: Partial<Record<ScalarTypeIr, string>> = {
  int32: "Int32Value",
  int64: "Int64Value",
};

const MODELS_BANNER =
  "// -- Method input/result models -------------------------------------------";
const METADATA_BANNER =
  "// -- Host metadata ----------------------------------------------------------";

/** "sample-host" -> "sample" (used in prose: "the SqliteHost sample host"). */
function hostDisplayName(baseName: string): string {
  return baseName.replace(/-host$/, "").replace(/-/g, " ");
}

/** "sample-host" -> "SAMPLE_HOST_METADATA". */
function metadataConstName(baseName: string): string {
  return `${baseName.replace(/[^A-Za-z0-9]+/g, "_").toUpperCase()}_METADATA`;
}

function fieldLines(field: ScalarFieldIr): string[] {
  const lines: string[] = [];
  if (field.scalarType === "bytes") {
    lines.push(docComment("base64-encoded bytes", "  "));
  }
  const optional = field.optional ? "?" : "";
  lines.push(
    `  ${field.propertyName}${optional}: ${SCALAR_TS_TYPES[field.scalarType]};`,
  );
  return lines;
}

function interfaceBlock(
  modelName: string,
  fields: ScalarFieldIr[],
  listFields: ListFieldIr[],
): string {
  const lines = [`export interface ${modelName} {`];
  for (const field of fields) {
    lines.push(...fieldLines(field));
  }
  for (const listField of listFields) {
    lines.push(`  ${listField.propertyName}: ${listField.itemModelName}[];`);
  }
  lines.push("}");
  return lines.join("\n");
}

/** Item-model interfaces (each once) followed by the shape interface. */
function shapeBlocks(shape: ObjectShapeIr, emittedItems: Set<string>): string[] {
  const blocks: string[] = [];
  for (const listField of shape.listFields) {
    if (!emittedItems.has(listField.itemModelName)) {
      emittedItems.add(listField.itemModelName);
      blocks.push(interfaceBlock(listField.itemModelName, listField.itemFields, []));
    }
  }
  blocks.push(interfaceBlock(shape.modelName, shape.fields, shape.listFields));
  return blocks;
}

function columnMap(fields: ScalarFieldIr[]): Literal {
  const map: { [key: string]: Literal } = {};
  for (const field of fields) {
    map[field.propertyName] = field.column;
  }
  return map;
}

function listFieldMetadata(field: ListFieldIr): Literal {
  return {
    propertyName: field.propertyName,
    childTable: field.childTable,
    columns: columnMap(field.itemFields),
  };
}

function childTableMetadata(field: ListFieldIr): Literal {
  return {
    name: field.childTable,
    columns: [
      "call_id",
      "item_index",
      ...field.itemFields.map((item) => item.column),
    ],
  };
}

function methodMetadata(method: HostMethodIr): Literal {
  return {
    methodName: method.methodName,
    operationName: method.operationName,
    handlerName: method.handlerName,
    apiLevel: method.apiLevel,
    callTable: method.callTable,
    resultTable: method.resultTable,
    queueTrigger: method.queueTrigger,
    inputColumns: columnMap(method.input.fields),
    resultColumns: columnMap(method.result.fields),
    inputListFields: method.input.listFields.map(listFieldMetadata),
    resultListFields: method.result.listFields.map(listFieldMetadata),
  };
}

function hostMetadata(ir: HostLibraryIr): Literal {
  const tables: Literal[] = [
    { name: ir.queueTable.name, columns: [...ir.queueTable.columns] },
    { name: ir.inputsTable.name, columns: [...ir.inputsTable.columns] },
  ];
  for (const method of ir.methods) {
    tables.push({
      name: method.callTable,
      columns: ["call_id", ...method.input.fields.map((field) => field.column)],
    });
    for (const listField of method.input.listFields) {
      tables.push(childTableMetadata(listField));
    }
    tables.push({
      name: method.resultTable,
      columns: [
        "call_id",
        "status",
        ...method.result.fields.map((field) => field.column),
      ],
    });
    for (const listField of method.result.listFields) {
      tables.push(childTableMetadata(listField));
    }
  }
  return {
    engine: ir.engine,
    namespace: ir.library.namespace,
    interfaceName: ir.library.interfaceName,
    apiLevel: ir.library.apiLevel,
    features: [...ir.library.features],
    queueTable: { name: ir.queueTable.name, columns: [...ir.queueTable.columns] },
    inputsTable: {
      name: ir.inputsTable.name,
      columns: [...ir.inputsTable.columns],
    },
    methods: ir.methods.map(methodMetadata),
    tables,
  };
}

function usedValueImports(ir: HostLibraryIr): string[] {
  const used = new Set<string>();
  const collect = (fields: ScalarFieldIr[]) => {
    for (const field of fields) {
      const imported = RUNTIME_VALUE_IMPORTS[field.scalarType];
      if (imported !== undefined) {
        used.add(imported);
      }
    }
  };
  for (const method of ir.methods) {
    for (const shape of [method.input, method.result]) {
      collect(shape.fields);
      for (const listField of shape.listFields) {
        collect(listField.itemFields);
      }
    }
  }
  return [...used].sort();
}

/** Render the typed authoring module for one host library. */
export function emitHostTypes(ir: HostLibraryIr, baseName: string): string {
  const display = hostDisplayName(baseName);
  const constName = metadataConstName(baseName);

  const importLines: string[] = [];
  const valueImports = usedValueImports(ir);
  if (valueImports.length > 0) {
    importLines.push(
      `import type { ${valueImports.join(", ")} } from "@sqlite-host/runtime-types";`,
    );
  }
  importLines.push(`import type { HostMetadata } from "../metadata.js";`);

  const emittedItems = new Set<string>();
  const interfaces: string[] = [];
  for (const method of ir.methods) {
    interfaces.push(...shapeBlocks(method.input, emittedItems));
    interfaces.push(...shapeBlocks(method.result, emittedItems));
  }

  const declPrefix = `export const ${constName}: HostMetadata = `;
  const metadataDecl = [
    docComment(
      `Autocomplete/editor metadata for the ${display} host, mirroring the ` +
        `canonical manifest. Equal to loadHostMetadata(<${display} manifest>).`,
    ),
    `${declPrefix}${renderLiteral(hostMetadata(ir), "", declPrefix.length, 1)};`,
  ].join("\n");

  const parts = [
    generatedHeader(
      `Typed authoring surface for the SqliteHost ${display} host ` +
        `(typespec/examples/${baseName}-methods.tsp, manifest ` +
        `fixtures/manifests/${baseName}.manifest.json). Do not edit by ` +
        "hand — this vendored copy is golden-tested against the canonical " +
        "manifest.",
    ),
    importLines.join("\n"),
    MODELS_BANNER,
    ...interfaces,
    METADATA_BANNER,
    metadataDecl,
  ];
  return parts.join("\n\n") + "\n";
}
