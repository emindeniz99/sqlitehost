import { strict as assert } from "node:assert";
import { spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { parseManifest, type HostLibraryIr } from "@sqlite-host/codegen-core";
import { emitEnvelope } from "../emit-envelope.js";
import { emitHostTypes } from "../emit-host-types.js";
import {
  DEFAULT_BASE_NAME,
  ENVELOPE_FILE_PATH,
  emitTypeScript,
  hostTypesFilePath,
} from "../emit.js";

const packageRoot = resolve(fileURLToPath(import.meta.url), "../../..");
const projectRoot = resolve(packageRoot, "../..");
const manifestJson = readFileSync(
  join(projectRoot, "fixtures/manifests/sample-host.manifest.json"),
  "utf8",
);
const envelopeGolden = readFileSync(
  join(projectRoot, "typescript", ENVELOPE_FILE_PATH),
  "utf8",
);
const hostTypesGolden = readFileSync(
  join(projectRoot, "typescript", hostTypesFilePath()),
  "utf8",
);
const scratchRoot = join(packageRoot, ".tsp-output", "emitter-tests");

function scratchDir(name: string): string {
  const dir = join(scratchRoot, `${name}-${process.pid}`);
  rmSync(dir, { recursive: true, force: true });
  mkdirSync(dir, { recursive: true });
  return dir;
}

function sampleIr(): HostLibraryIr {
  return parseManifest(manifestJson);
}

/**
 * Hand-built non-sample IR: non-default naming prefixes (req_/resp_,
 * arg_/out_, udf_), non-default queue/inputs/vars/control table names,
 * fully renamed shared columns, an optional bytes field, int32 + int64
 * fields, a list field on each side of the first method, and a second
 * inline-exposed method under the custom functionPrefix.
 */
function smokeIr(): HostLibraryIr {
  return {
    manifestVersion: 1,
    engine: "acme-host-v1",
    library: {
      namespace: "Acme.Warehouse",
      interfaceName: "WarehouseHostMethods",
      apiLevel: 3,
      minSqliteVersionNumber: 3008011,
      features: ["typedNamedBindings", "inlineFunctions"],
    },
    naming: {
      callTablePrefix: "req_",
      resultTablePrefix: "resp_",
      inputColumnPrefix: "arg_",
      resultColumnPrefix: "out_",
      inputListTableInfix: "__arg_",
      resultListTableInfix: "__out_",
      functionPrefix: "udf_",
    },
    columns: {
      callId: "cid",
      itemIndex: "idx",
      status: "state",
      doneValue: "ok",
      queueId: "qid",
      method: "verb",
      name: "param",
      valueType: "kind",
      intValue: "ival",
      realValue: "rval",
      textValue: "tval",
      blobValue: "bval",
      action: "cmd",
      message: "note",
    },
    queueTable: {
      name: "host_queue",
      columns: ["qid", "cid", "verb", "state"],
    },
    inputsTable: {
      name: "script_params",
      columns: ["param", "kind", "ival", "rval", "tval", "bval"],
    },
    varsTable: {
      name: "script_scratch",
      columns: ["param", "kind", "ival", "rval", "tval", "bval"],
    },
    controlTable: {
      name: "script_ctl",
      columns: ["cmd", "note"],
    },
    scriptEnvelope: {
      engine: "acme-host-v1",
      bindingTypes: [
        "null",
        "int32",
        "int64",
        "bool",
        "text",
        "blob",
        "float32",
        "float64",
      ],
    },
    methods: [
      {
        operationName: "StoreAsset",
        methodName: "storeAsset",
        handlerName: "StoreAsset",
        apiLevel: 3,
        mutates: true,
        callTable: "req_store_asset",
        resultTable: "resp_store_asset",
        queueTrigger: "trg_req_store_asset_queue",
        input: {
          modelName: "StoreAssetInput",
          fields: [
            {
              propertyName: "assetId",
              sqlName: "asset_id",
              column: "arg_asset_id",
              scalarType: "int32",
              optional: false,
            },
            {
              propertyName: "thumbnail",
              sqlName: "thumbnail",
              column: "arg_thumbnail",
              scalarType: "bytes",
              optional: true,
            },
            {
              propertyName: "weight",
              sqlName: "weight",
              column: "arg_weight",
              scalarType: "float32",
              optional: true,
            },
          ],
          listFields: [
            {
              propertyName: "tags",
              sqlName: "tags",
              childTable: "req_store_asset__arg_tags",
              itemModelName: "AssetTagItem",
              itemFields: [
                {
                  propertyName: "label",
                  sqlName: "label",
                  column: "arg_label",
                  scalarType: "string",
                  optional: false,
                },
              ],
            },
          ],
        },
        result: {
          modelName: "StoreAssetResult",
          fields: [
            {
              propertyName: "revision",
              sqlName: "revision",
              column: "out_revision",
              scalarType: "int64",
              optional: false,
            },
            {
              propertyName: "confidence",
              sqlName: "confidence",
              column: "out_confidence",
              scalarType: "float64",
              optional: false,
            },
          ],
          listFields: [],
        },
        inline: null,
      },
      {
        operationName: "LookupAsset",
        methodName: "lookupAsset",
        handlerName: "LookupAsset",
        apiLevel: 3,
        mutates: false,
        callTable: "req_lookup_asset",
        resultTable: "resp_lookup_asset",
        queueTrigger: "trg_req_lookup_asset_queue",
        input: {
          modelName: "LookupAssetInput",
          fields: [
            {
              propertyName: "assetId",
              sqlName: "asset_id",
              column: "arg_asset_id",
              scalarType: "int32",
              optional: false,
            },
            {
              propertyName: "variant",
              sqlName: "variant",
              column: "arg_variant",
              scalarType: "string",
              optional: true,
            },
          ],
          listFields: [],
        },
        result: {
          modelName: "LookupAssetResult",
          fields: [
            {
              propertyName: "revision",
              sqlName: "revision",
              column: "out_revision",
              scalarType: "int64",
              optional: false,
            },
          ],
          listFields: [],
        },
        inline: {
          functionName: "udf_lookup_asset",
          minArgs: 1,
          maxArgs: 2,
          args: [
            {
              propertyName: "assetId",
              sqlName: "asset_id",
              scalarType: "int32",
              optional: false,
            },
            {
              propertyName: "variant",
              sqlName: "variant",
              scalarType: "string",
              optional: true,
            },
          ],
          returns: {
            propertyName: "revision",
            sqlName: "revision",
            scalarType: "int64",
          },
        },
      },
    ],
  };
}

test("emitEnvelope output is byte-identical to the vendored envelope.ts", () => {
  assert.equal(emitEnvelope(sampleIr()), envelopeGolden);
});

test("emitHostTypes output is byte-identical to the vendored sample-host.ts", () => {
  assert.equal(emitHostTypes(sampleIr(), DEFAULT_BASE_NAME), hostTypesGolden);
});

test("emitTypeScript returns both files under their vendored paths", () => {
  const files = emitTypeScript(sampleIr());
  assert.deepEqual(
    files.map((file) => file.path),
    [
      "runtime-types/src/generated/envelope.ts",
      "authoring-sdk/src/generated/sample-host.ts",
    ],
  );
  assert.equal(files[0].contents, envelopeGolden);
  assert.equal(files[1].contents, hostTypesGolden);
});

test("emitting twice from independent parses is deterministic", () => {
  const first = emitTypeScript(sampleIr());
  const second = emitTypeScript(sampleIr());
  assert.deepEqual(first, second);
  const smokeFirst = emitTypeScript(smokeIr(), { baseName: "acme-warehouse" });
  const smokeSecond = emitTypeScript(smokeIr(), { baseName: "acme-warehouse" });
  assert.deepEqual(smokeFirst, smokeSecond);
});

test("smoke IR: envelope derives engine and inputs table from the IR", () => {
  const envelope = emitEnvelope(smokeIr());
  assert.ok(envelope.includes('export const SCRIPT_ENGINE_V1 = "acme-host-v1";'));
  assert.ok(envelope.includes('/** Must be `"acme-host-v1"`. */'));
  assert.ok(envelope.includes("`script_params` table"));
  assert.ok(envelope.includes("`script_params` before step 1"));
  assert.ok(!envelope.includes("script_inputs"));
});

test("smoke IR: envelope union gains float members in binding-type order", () => {
  const envelope = emitEnvelope(smokeIr());
  assert.ok(
    envelope.includes(
      'export interface Float32BindingValue {\n  type: "float32";\n  value: number;\n}',
    ),
  );
  assert.ok(
    envelope.includes(
      'export interface Float64BindingValue {\n  type: "float64";\n  value: number;\n}',
    ),
  );
  assert.ok(
    envelope.includes(
      "  | BlobBindingValue\n  | Float32BindingValue\n  | Float64BindingValue;",
    ),
    "float union members follow blob in binding-type order",
  );
});

test("smoke IR: host types derive names, optionality, and scalar types", () => {
  const output = emitHostTypes(smokeIr(), "acme-warehouse");
  // Interfaces with resolved model names and scalar mappings.
  assert.ok(output.includes("export interface StoreAssetInput {"));
  assert.ok(output.includes("  assetId: Int32Value;"));
  assert.ok(
    output.includes("  /** base64-encoded bytes */\n  thumbnail?: string;"),
    "optional bytes field maps to `thumbnail?: string` with base64 doc",
  );
  assert.ok(
    output.includes("  weight?: number;"),
    "optional float32 field maps to `weight?: number`",
  );
  assert.ok(output.includes("  tags: AssetTagItem[];"));
  assert.ok(output.includes("export interface AssetTagItem {"));
  assert.ok(output.includes("  label: string;"));
  assert.ok(output.includes("export interface StoreAssetResult {"));
  assert.ok(output.includes("  revision: Int64Value;"));
  assert.ok(
    output.includes("  confidence: number;"),
    "float64 field maps to `confidence: number`",
  );
  // Imports follow field usage (int32 + int64 both present).
  assert.ok(
    output.includes(
      'import type { Int32Value, Int64Value } from "@sqlite-host/runtime-types";',
    ),
  );
  // Metadata const named after the base name, physical names from the IR.
  assert.ok(output.includes("export const ACME_WAREHOUSE_METADATA: HostMetadata ="));
  assert.ok(output.includes('namespace: "Acme.Warehouse"'));
  assert.ok(output.includes("apiLevel: 3"));
  assert.ok(output.includes("minSqliteVersionNumber: 3008011"));
  assert.ok(output.includes('callTable: "req_store_asset"'));
  assert.ok(output.includes('resultTable: "resp_store_asset"'));
  assert.ok(output.includes('queueTrigger: "trg_req_store_asset_queue"'));
  assert.ok(output.includes('childTable: "req_store_asset__arg_tags"'));
  assert.ok(
    output.includes(
      'inputColumns: {\n        assetId: "arg_asset_id",\n        thumbnail: "arg_thumbnail",\n        weight: "arg_weight",\n      }',
    ),
  );
  assert.ok(
    output.includes(
      'resultColumns: { revision: "out_revision", confidence: "out_confidence" }',
    ),
  );
  assert.ok(
    output.includes('name: "host_queue"'),
    "queue table name comes from the IR",
  );
  assert.ok(output.includes('name: "script_params"'));
  assert.ok(
    output.includes('name: "script_scratch"'),
    "vars table name comes from the IR",
  );
  // The control table mirrors varsTable, name and columns from the IR.
  assert.ok(
    output.includes('controlTable: { name: "script_ctl", columns: ["cmd", "note"] }'),
    "control table metadata comes from the IR",
  );
  // The columns block mirrors the manifest's columns block exactly.
  assert.ok(output.includes('callId: "cid"'));
  assert.ok(output.includes('itemIndex: "idx"'));
  assert.ok(output.includes('doneValue: "ok"'));
  assert.ok(output.includes('action: "cmd"'));
  // Child/result tables get the renamed structural columns.
  assert.ok(
    output.includes('columns: ["cid", "idx", "arg_label"]'),
    "list child table columns include the renamed callId + itemIndex",
  );
  assert.ok(
    output.includes('columns: ["cid", "state", "out_revision", "out_confidence"]'),
  );
});

test("smoke IR: metadata mirrors functionPrefix and the per-method inline blocks", () => {
  const output = emitHostTypes(smokeIr(), "acme-warehouse");
  // The custom function prefix sits between features and columns.
  assert.ok(output.includes('functionPrefix: "udf_"'));
  assert.ok(!output.includes("fn_"), "default function prefix leaked");
  // The inline method's block mirrors the manifest inline shape exactly.
  assert.ok(
    output.includes(
      [
        "      inline: {",
        '        functionName: "udf_lookup_asset",',
        "        minArgs: 1,",
        "        maxArgs: 2,",
        "        args: [",
        "          {",
        '            propertyName: "assetId",',
        '            sqlName: "asset_id",',
        '            scalarType: "int32",',
        "            optional: false,",
        "          },",
        "          {",
        '            propertyName: "variant",',
        '            sqlName: "variant",',
        '            scalarType: "string",',
        "            optional: true,",
        "          },",
        "        ],",
        "        returns: {",
        '          propertyName: "revision",',
        '          sqlName: "revision",',
        '          scalarType: "int64",',
        "        },",
        "      },",
      ].join("\n"),
    ),
    "inline block mirrors the manifest inline shape",
  );
  // Non-inline methods mirror the manifest's null (uniform shape).
  assert.ok(output.includes("inline: null"));
  assert.equal(output.split("inline: null").length - 1, 1, "exactly one non-inline method");
});

test("smoke IR: no emitted file mentions the default shared table or column names", () => {
  const defaults = [
    "pending_host_calls",
    "script_inputs",
    "script_vars",
    "script_control",
    "call_id",
    "item_index",
    "queue_id",
    "value_type",
    "int_value",
    "real_value",
    "text_value",
    "blob_value",
  ];
  for (const file of emitTypeScript(smokeIr(), { baseName: "acme-warehouse" })) {
    for (const name of defaults) {
      assert.ok(
        !file.contents.includes(name),
        `${file.path} still mentions default name ${name}`,
      );
    }
  }
});

test("emitEnvelope fails loud on an unknown binding type", () => {
  const ir = smokeIr();
  ir.scriptEnvelope.bindingTypes = ["null", "float16"];
  assert.throws(() => emitEnvelope(ir), /unknown binding type "float16"/);
});

test("CLI writes golden-identical files from a manifest", () => {
  const outDir = scratchDir("cli");
  const run = spawnSync(
    process.execPath,
    [
      join(packageRoot, "dist/cli.js"),
      join(projectRoot, "fixtures/manifests/sample-host.manifest.json"),
      outDir,
    ],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 0, `stderr: ${run.stderr}`);
  assert.equal(
    readFileSync(join(outDir, ENVELOPE_FILE_PATH), "utf8"),
    envelopeGolden,
  );
  assert.equal(
    readFileSync(join(outDir, hostTypesFilePath()), "utf8"),
    hostTypesGolden,
  );
  rmSync(outDir, { recursive: true, force: true });
});

test("CLI honors --base-name for the authoring module", () => {
  const outDir = scratchDir("cli-basename");
  const run = spawnSync(
    process.execPath,
    [
      join(packageRoot, "dist/cli.js"),
      join(projectRoot, "fixtures/manifests/sample-host.manifest.json"),
      outDir,
      "--base-name",
      "example-game",
    ],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 0, `stderr: ${run.stderr}`);
  const contents = readFileSync(
    join(outDir, hostTypesFilePath("example-game")),
    "utf8",
  );
  assert.ok(contents.includes("export const EXAMPLE_GAME_METADATA: HostMetadata ="));
  rmSync(outDir, { recursive: true, force: true });
});

test("CLI exits non-zero on bad usage", () => {
  const run = spawnSync(
    process.execPath,
    [join(packageRoot, "dist/cli.js"), "only-one-arg"],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 2);
  assert.match(run.stderr, /usage: sqlite-host-emit-typescript/);
});
