import { strict as assert } from "node:assert";
import { spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import {
  deriveCallTable,
  deriveFunctionName,
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
// Non-sample smoke IR: different naming prefixes, custom shared workspace
// table names, fully renamed shared columns, optional bytes field, + one
// inline-exposed method under a custom functionPrefix.
// ---------------------------------------------------------------------------

function smokeIr(): HostLibraryIr {
  const naming: NamingIr = {
    callTablePrefix: "hc_",
    resultTablePrefix: "hr_",
    inputColumnPrefix: "in_",
    resultColumnPrefix: "out_",
    inputListTableInfix: "__in_",
    resultListTableInfix: "__out_",
    functionPrefix: "udf_",
  };
  const methodName = "archiveReport";
  const inlineMethodName = "lookupScore";
  return {
    manifestVersion: 1,
    engine: "sqlite-host-v1",
    library: {
      namespace: "Acme.Tools",
      interfaceName: "ToolHostMethods",
      apiLevel: 2,
      minSqliteVersionNumber: 3008011,
      features: [
        "typedNamedBindings",
        "splitResultTables",
        "scriptInputs",
        "inlineFunctions",
      ],
    },
    naming,
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
        mutates: true,
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
        inline: null,
      },
      {
        operationName: "LookupScore",
        methodName: inlineMethodName,
        handlerName: "LookupScore",
        apiLevel: 2,
        mutates: false,
        callTable: deriveCallTable(naming, inlineMethodName),
        resultTable: deriveResultTable(naming, inlineMethodName),
        queueTrigger: deriveQueueTrigger(naming, inlineMethodName),
        input: {
          modelName: "LookupScoreInput",
          fields: [
            {
              propertyName: "reportId",
              sqlName: "report_id",
              column: deriveInputColumn(naming, "report_id"),
              scalarType: "string",
              optional: false,
            },
            {
              propertyName: "slot",
              sqlName: "slot",
              column: deriveInputColumn(naming, "slot"),
              scalarType: "int32",
              optional: true,
            },
          ],
          listFields: [],
        },
        result: {
          modelName: "LookupScoreResult",
          fields: [
            {
              propertyName: "score",
              sqlName: "score",
              column: deriveResultColumn(naming, "score"),
              scalarType: "float64",
              optional: false,
            },
          ],
          listFields: [],
        },
        inline: {
          functionName: deriveFunctionName(naming, inlineMethodName),
          minArgs: 1,
          maxArgs: 2,
          args: [
            {
              propertyName: "reportId",
              sqlName: "report_id",
              scalarType: "string",
              optional: false,
            },
            {
              propertyName: "slot",
              sqlName: "slot",
              scalarType: "int32",
              optional: true,
            },
          ],
          returns: {
            propertyName: "score",
            sqlName: "score",
            scalarType: "float64",
          },
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

test("smoke IR: inline methods emit .Inline between .Results and .Handler, others nothing", () => {
  const specs = smokeFile("GeneratedHostMethodSpecs.g.cs");
  // The inline method's spec carries the custom-prefix function name.
  assert.match(
    specs,
    /\.Double\("score", x => x\.Score\)\)\n\s+\.Inline\("udf_lookup_score"\)\n\s+\.Handler\(\(handlers, input\) => handlers\.LookupScore\(input\)\)/,
  );
  // The ineligible (mutating, list-carrying) method emits no .Inline.
  const archiveSpec = specs.slice(
    specs.indexOf("BuildArchiveReportSpec()"),
    specs.indexOf("BuildLookupScoreSpec()"),
  );
  assert.doesNotMatch(archiveSpec, /\.Inline\(/);
});

test("smoke IR: host definition reproduces the IR naming prefixes and table names", () => {
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
  // The shared workspace table names always follow the six prefixes,
  // in queue/inputs/vars/control order; the function prefix closes the
  // Naming block after .ControlTable (docs/csharp-api.md).
  assert.match(
    definition,
    /\.ResultListTableInfix\("__out_"\)\n\s+\.QueueTable\("host_queue"\)\n\s+\.InputsTable\("script_params"\)\n\s+\.VarsTable\("script_scratch"\)\n\s+\.ControlTable\("script_ctl"\)\n\s+\.FunctionPrefix\("udf_"\)\)/,
  );
});

test("smoke IR: host definition emits all fourteen column values explicitly", () => {
  const definition = smokeFile("GeneratedHostDefinition.g.cs");
  // The .Columns block sits between .Naming and .Methods and lists all
  // fourteen setters in SqliteHostColumns property order.
  assert.match(
    definition,
    new RegExp(
      [
        /\.FunctionPrefix\("udf_"\)\)\n\s+\.Columns\(c => c/,
        /\.CallId\("cid"\)/,
        /\.ItemIndex\("idx"\)/,
        /\.Status\("state"\)/,
        /\.DoneValue\("ok"\)/,
        /\.QueueId\("qid"\)/,
        /\.Method\("verb"\)/,
        /\.Name\("param"\)/,
        /\.ValueType\("kind"\)/,
        /\.IntValue\("ival"\)/,
        /\.RealValue\("rval"\)/,
        /\.TextValue\("tval"\)/,
        /\.BlobValue\("bval"\)/,
        /\.Action\("cmd"\)/,
        /\.Message\("note"\)\)\n\s+\.Methods\(/,
      ]
        .map((r) => r.source)
        .join("\\n\\s+"),
    ),
  );
});

test("smoke IR: schema constant embeds the IR-derived DDL", () => {
  const schema = smokeFile("GeneratedSchemaSql.g.cs");
  assert.match(schema, /CREATE TABLE hc_archive_report \(/);
  assert.match(schema, /CREATE TABLE hr_archive_report__out_tags \(/);
  // Custom shared workspace table names flow into the DDL and the
  // queue-trigger body — with the renamed columns.
  assert.match(schema, /CREATE TABLE host_queue \(/);
  assert.match(schema, /CREATE TABLE script_params \(/);
  assert.match(schema, /CREATE TABLE script_scratch \(/);
  assert.match(schema, /CREATE TABLE script_ctl \(/);
  assert.match(schema, /qid INTEGER PRIMARY KEY AUTOINCREMENT/);
  assert.match(schema, /cid TEXT NOT NULL UNIQUE/);
  assert.match(schema, /verb TEXT NOT NULL/);
  assert.match(schema, /state TEXT NOT NULL DEFAULT 'pending'/);
  assert.match(schema, /param TEXT NOT NULL PRIMARY KEY/);
  assert.match(schema, /kind TEXT NOT NULL/);
  assert.match(schema, /ival INTEGER/);
  assert.match(schema, /cmd TEXT NOT NULL/);
  assert.match(schema, /note TEXT\\n/);
  assert.match(schema, /INSERT INTO host_queue \(cid, verb\)/);
  assert.match(schema, /VALUES \(NEW\.cid, 'archiveReport'\)/);
  // Parent/child row-identity columns and the done literal are renamed.
  assert.match(schema, /cid TEXT NOT NULL PRIMARY KEY/);
  assert.match(schema, /state TEXT NOT NULL DEFAULT 'ok'/);
  assert.match(schema, /idx INTEGER NOT NULL/);
  assert.match(schema, /PRIMARY KEY \(cid, idx\)/);
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
    "fn_",
  ];
  for (const file of emitCSharp(smokeIr())) {
    for (const name of defaults) {
      assert.ok(
        !file.contents.includes(name),
        `${file.path} still mentions default name ${name}`,
      );
    }
  }
});

test("smoke IR: emission is deterministic", () => {
  assert.deepEqual(emitCSharp(smokeIr()), emitCSharp(smokeIr()));
});
