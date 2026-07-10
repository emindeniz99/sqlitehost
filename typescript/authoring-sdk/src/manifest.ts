/**
 * Vendored TypeScript mirror of the canonical manifest shape
 * (docs/manifest.md, codegen/core/src/ir.ts). The authoring SDK never
 * re-derives physical names — it reads the resolved names from a
 * manifest produced by the manifest emitter.
 */

export type ManifestScalarType = "int32" | "int64" | "boolean" | "string" | "bytes";

export interface ManifestScalarField {
  propertyName: string;
  sqlName: string;
  column: string;
  scalarType: ManifestScalarType;
  optional: boolean;
}

export interface ManifestListField {
  propertyName: string;
  sqlName: string;
  childTable: string;
  itemModelName: string;
  itemFields: ManifestScalarField[];
}

export interface ManifestShape {
  modelName: string;
  fields: ManifestScalarField[];
  listFields: ManifestListField[];
}

export interface ManifestMethod {
  operationName: string;
  methodName: string;
  handlerName: string;
  apiLevel: number;
  callTable: string;
  resultTable: string;
  queueTrigger: string;
  input: ManifestShape;
  result: ManifestShape;
}

export interface ManifestTable {
  name: string;
  columns: string[];
}

export interface HostManifest {
  manifestVersion: number;
  engine: string;
  library: {
    namespace: string;
    interfaceName: string;
    apiLevel: number;
    features: string[];
  };
  naming: {
    callTablePrefix: string;
    resultTablePrefix: string;
    inputColumnPrefix: string;
    resultColumnPrefix: string;
    inputListTableInfix: string;
    resultListTableInfix: string;
  };
  queueTable: ManifestTable;
  inputsTable: ManifestTable;
  scriptEnvelope: {
    engine: string;
    bindingTypes: string[];
  };
  methods: ManifestMethod[];
}

/**
 * Parse a canonical manifest from JSON text or an already-parsed value,
 * with a light structural check. Throws TypeError when the value is not
 * a manifest.
 */
export function parseHostManifest(json: string | unknown): HostManifest {
  const value: unknown = typeof json === "string" ? JSON.parse(json) : json;
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new TypeError("manifest must be a JSON object");
  }
  const manifest = value as Partial<HostManifest>;
  if (manifest.manifestVersion !== 1) {
    throw new TypeError("manifest.manifestVersion must be 1");
  }
  if (typeof manifest.engine !== "string") {
    throw new TypeError("manifest.engine must be a string");
  }
  if (
    typeof manifest.library !== "object" ||
    manifest.library === null ||
    typeof manifest.library.apiLevel !== "number" ||
    !Array.isArray(manifest.library.features)
  ) {
    throw new TypeError("manifest.library must carry apiLevel and features");
  }
  if (!Array.isArray(manifest.methods)) {
    throw new TypeError("manifest.methods must be an array");
  }
  return manifest as HostManifest;
}
