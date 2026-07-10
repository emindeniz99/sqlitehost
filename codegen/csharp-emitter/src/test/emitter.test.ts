import { strict as assert } from "node:assert";
import { spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import {
  deriveCallTable,
  deriveInputColumn,
  deriveQueueTrigger,
  deriveResultColumn,
  deriveResultListTable,
  deriveResultTable,
  parseManifest,
  type HostLibraryIr,
  type NamingIr,
} from "@sqlite-host/codegen-core";
import { emitCSharp } from "../emit.js";

const packageRoot = resolve(fileURLToPath(import.meta.url), "../../..");
const projectRoot = resolve(packageRoot, "../..");
const manifestPath = join(
  projectRoot,
  "fixtures/manifests/sample-host.manifest.json",
);
const scratchRoot = join(packageRoot, ".out", "emitter-tests");

/** Committed C# sources are the byte-level goldens, keyed by emit path. */
const goldenByEmitPath: Record<string, string> = {
  "HostMethodDtos.g.cs":
    "csharp/SqliteHost.Generated.Sample/HostMethodDtos.g.cs",
  "IGeneratedHostHandlers.g.cs":
    "csharp/SqliteHost.Generated.Sample/IGeneratedHostHandlers.g.cs",
  "GeneratedHostMethodSpecs.g.cs":
    "csharp/SqliteHost.Generated.Sample/GeneratedHostMethodSpecs.g.cs",
  "GeneratedHostDefinition.g.cs":
    "csharp/SqliteHost.Generated.Sample/GeneratedHostDefinition.g.cs",
  "GeneratedSchemaSql.g.cs":
    "csharp/SqliteHost.Generated.Sample/GeneratedSchemaSql.g.cs",
  "envelope/ScriptEnvelope.g.cs":
    "csharp/SqliteHost.Abstractions/ScriptEnvelope.g.cs",
};

function sampleIr(): HostLibraryIr {
  return parseManifest(readFileSync(manifestPath, "utf8"));
}

test("emitCSharp emits exactly the six expected files", () => {
  const files = emitCSharp(sampleIr());
  assert.deepEqual(
    files.map((f) => f.path),
    Object.keys(goldenByEmitPath),
  );
});

for (const [emitPath, goldenPath] of Object.entries(goldenByEmitPath)) {
  test(`${emitPath} is byte-identical to the committed ${goldenPath}`, () => {
    const files = emitCSharp(sampleIr());
    const file = files.find((f) => f.path === emitPath);
    assert.ok(file, `emitCSharp did not emit ${emitPath}`);
    const golden = readFileSync(join(projectRoot, goldenPath), "utf8");
    assert.equal(file!.contents, golden);
  });
}

test("emitting twice from independently parsed IR is deterministic", () => {
  assert.deepEqual(emitCSharp(sampleIr()), emitCSharp(sampleIr()));
});

test("CLI writes fixture-identical files from a manifest", () => {
  const outDir = join(scratchRoot, `cli-${process.pid}`);
  rmSync(outDir, { recursive: true, force: true });
  mkdirSync(outDir, { recursive: true });
  const run = spawnSync(
    process.execPath,
    [join(packageRoot, "dist/cli.js"), manifestPath, outDir],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 0, `stderr: ${run.stderr}`);
  for (const [emitPath, goldenPath] of Object.entries(goldenByEmitPath)) {
    assert.equal(
      readFileSync(join(outDir, emitPath), "utf8"),
      readFileSync(join(projectRoot, goldenPath), "utf8"),
    );
  }
  rmSync(outDir, { recursive: true, force: true });
});

test("CLI exits non-zero on bad usage", () => {
  const run = spawnSync(
    process.execPath,
    [join(packageRoot, "dist/cli.js"), manifestPath],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 2);
  assert.match(run.stderr, /usage/);
});

// ---------------------------------------------------------------------------
// Non-sample smoke IR: different naming prefixes + optional bytes field.
// ---------------------------------------------------------------------------

function smokeIr(): HostLibraryIr {
  const naming: NamingIr = {
    callTablePrefix: "hc_",
    resultTablePrefix: "hr_",
    inputColumnPrefix: "in_",
    resultColumnPrefix: "out_",
    inputListTableInfix: "__in_",
    resultListTableInfix: "__out_",
  };
  const methodName = "archiveReport";
  return {
    manifestVersion: 1,
    engine: "sqlite-host-v1",
    library: {
      namespace: "Acme.Tools",
      interfaceName: "ToolHostMethods",
      apiLevel: 2,
      minSqliteVersionNumber: 3008011,
      features: ["typedNamedBindings", "splitResultTables", "scriptInputs"],
    },
    naming,
    queueTable: {
      name: "pending_host_calls",
      columns: ["queue_id", "call_id", "method", "status"],
    },
    inputsTable: {
      name: "script_inputs",
      columns: [
        "name",
        "value_type",
        "int_value",
        "real_value",
        "text_value",
        "blob_value",
      ],
    },
    varsTable: {
      name: "script_vars",
      columns: [
        "name",
        "value_type",
        "int_value",
        "real_value",
        "text_value",
        "blob_value",
      ],
    },
    scriptEnvelope: {
      engine: "sqlite-host-v1",
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
        operationName: "ArchiveReport",
        methodName,
        handlerName: "ArchiveReport",
        apiLevel: 2,
        callTable: deriveCallTable(naming, methodName),
        resultTable: deriveResultTable(naming, methodName),
        queueTrigger: deriveQueueTrigger(naming, methodName),
        input: {
          modelName: "ArchiveReportInput",
          fields: [
            {
              propertyName: "reportId",
              sqlName: "report_id",
              column: deriveInputColumn(naming, "report_id"),
              scalarType: "string",
              optional: false,
            },
            {
              propertyName: "payload",
              sqlName: "payload",
              column: deriveInputColumn(naming, "payload"),
              scalarType: "bytes",
              optional: true,
            },
            {
              propertyName: "retryCount",
              sqlName: "retry_count",
              column: deriveInputColumn(naming, "retry_count"),
              scalarType: "int32",
              optional: true,
            },
            {
              propertyName: "score",
              sqlName: "score",
              column: deriveInputColumn(naming, "score"),
              scalarType: "float64",
              optional: false,
            },
            {
              propertyName: "weight",
              sqlName: "weight",
              column: deriveInputColumn(naming, "weight"),
              scalarType: "float32",
              optional: true,
            },
          ],
          listFields: [],
        },
        result: {
          modelName: "ArchiveReportResult",
          fields: [
            {
              propertyName: "archived",
              sqlName: "archived",
              column: deriveResultColumn(naming, "archived"),
              scalarType: "boolean",
              optional: false,
            },
            {
              propertyName: "ratio",
              sqlName: "ratio",
              column: deriveResultColumn(naming, "ratio"),
              scalarType: "float32",
              optional: false,
            },
          ],
          listFields: [
            {
              propertyName: "tags",
              sqlName: "tags",
              childTable: deriveResultListTable(naming, methodName, "tags"),
              itemModelName: "TagItem",
              itemFields: [
                {
                  propertyName: "label",
                  sqlName: "label",
                  column: deriveResultColumn(naming, "label"),
                  scalarType: "string",
                  optional: false,
                },
              ],
            },
          ],
        },
      },
    ],
  };
}

function smokeFile(name: string): string {
  const file = emitCSharp(smokeIr()).find((f) => f.path === name);
  assert.ok(file, `missing ${name}`);
  return file!.contents;
}

test("smoke IR: DTOs use the library namespace and IR-driven types", () => {
  const dtos = smokeFile("HostMethodDtos.g.cs");
  assert.match(dtos, /namespace Acme\.Tools\.Generated/);
  assert.match(dtos, /public class ArchiveReportInput/);
  // Optional bytes stays a reference type; optional int32 becomes int?.
  assert.match(dtos, /public byte\[\] Payload \{ get; set; \}/);
  assert.match(dtos, /public int\? RetryCount \{ get; set; \}/);
  assert.match(dtos, /public string ReportId \{ get; set; \}/);
  // float64 maps to double; optional float32 becomes float?.
  assert.match(dtos, /public double Score \{ get; set; \}/);
  assert.match(dtos, /public float\? Weight \{ get; set; \}/);
  assert.match(dtos, /public float Ratio \{ get; set; \}/);
  assert.match(
    dtos,
    /public List<TagItem> Tags \{ get; set; \} = new List<TagItem>\(\);/,
  );
  assert.match(dtos, /public class TagItem/);
});

test("smoke IR: handler interface exposes one method per op", () => {
  const handlers = smokeFile("IGeneratedHostHandlers.g.cs");
  assert.match(handlers, /namespace Acme\.Tools\.Generated/);
  assert.match(
    handlers,
    /ArchiveReportResult ArchiveReport\(ArchiveReportInput input\);/,
  );
});

test("smoke IR: method specs carry optional/list field-builder calls", () => {
  const specs = smokeFile("GeneratedHostMethodSpecs.g.cs");
  assert.match(specs, /BuildArchiveReportSpec\(\)/);
  assert.match(specs, /\.For<IGeneratedHostHandlers, ArchiveReportInput, ArchiveReportResult>\("archiveReport"\)/);
  assert.match(specs, /\.ApiLevel\(2\)/);
  assert.match(specs, /\.Text\("report_id", \(x, v\) => x\.ReportId = v\)/);
  assert.match(specs, /\.OptionalBlob\("payload", \(x, v\) => x\.Payload = v\)/);
  assert.match(specs, /\.OptionalInt\("retry_count", \(x, v\) => x\.RetryCount = v\)/);
  assert.match(specs, /\.Double\("score", \(x, v\) => x\.Score = v\)/);
  assert.match(specs, /\.OptionalFloat\("weight", \(x, v\) => x\.Weight = v\)/);
  assert.match(specs, /\.Float\("ratio", x => x\.Ratio\)/);
  assert.match(specs, /\.List<TagItem>\("tags", x => x\.Tags, item => item/);
  assert.match(specs, /\.Text\("label", x => x\.Label\)\)\)/);
  assert.match(specs, /handlers\.ArchiveReport\(input\)/);
});

test("smoke IR: host definition reproduces the IR naming prefixes", () => {
  const definition = smokeFile("GeneratedHostDefinition.g.cs");
  assert.match(definition, /\.ApiLevel\(2\)/);
  // MinSqliteVersion is always emitted, between ApiLevel and Naming.
  assert.match(
    definition,
    /\.ApiLevel\(2\)\n\s+\.MinSqliteVersion\(3008011\)\n\s+\.Naming\(/,
  );
  assert.match(definition, /\.CallTablePrefix\("hc_"\)/);
  assert.match(definition, /\.ResultTablePrefix\("hr_"\)/);
  assert.match(definition, /\.InputColumnPrefix\("in_"\)/);
  assert.match(definition, /\.ResultColumnPrefix\("out_"\)/);
  assert.match(definition, /\.InputListTableInfix\("__in_"\)/);
  assert.match(definition, /\.ResultListTableInfix\("__out_"\)/);
});

test("smoke IR: schema constant embeds the IR-derived DDL", () => {
  const schema = smokeFile("GeneratedSchemaSql.g.cs");
  assert.match(schema, /CREATE TABLE hc_archive_report \(/);
  assert.match(schema, /CREATE TABLE hr_archive_report__out_tags \(/);
  // Optional bytes column: BLOB without NOT NULL.
  assert.match(schema, /in_payload BLOB,\\n/);
  assert.doesNotMatch(schema, /in_payload BLOB NOT NULL/);
  // Float columns map to REAL; optional float32 drops NOT NULL.
  assert.match(schema, /in_score REAL NOT NULL/);
  assert.match(schema, /in_weight REAL\\n/);
  assert.doesNotMatch(schema, /in_weight REAL NOT NULL/);
  assert.match(schema, /out_ratio REAL NOT NULL/);
  assert.match(schema, /CREATE TRIGGER trg_hc_archive_report_queue/);
});

test("smoke IR: emission is deterministic", () => {
  assert.deepEqual(emitCSharp(smokeIr()), emitCSharp(smokeIr()));
});
