import { strict as assert } from "node:assert";
import { spawnSync } from "node:child_process";
import { readdirSync, readFileSync, rmSync } from "node:fs";
import { basename, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import { parseManifest, type HostLibraryIr } from "@sqlite-host/codegen-core";
import {
  ENVELOPE_PACKAGE,
  emitEnvelopeModel,
  emitJava,
  generatedPackageName,
} from "../emit.js";

const packageRoot = resolve(fileURLToPath(import.meta.url), "../../..");
const projectRoot = resolve(packageRoot, "../..");
const manifestPath = join(
  projectRoot,
  "fixtures/manifests/sample-host.manifest.json",
);
const manifestFixture = readFileSync(manifestPath, "utf8");

/** Source roots the committed golden files live under. */
const mainRoot = join(projectRoot, "java/sqlite-host-model/src/main/java");
const testRoot = join(projectRoot, "java/sqlite-host-model/src/test/java");

const envelopeDir = ENVELOPE_PACKAGE.split(".").join("/");

function sampleIr(): HostLibraryIr {
  return parseManifest(manifestFixture);
}

test("envelope model files are byte-identical to the committed sources", () => {
  const files = emitEnvelopeModel(sampleIr());
  assert.equal(files.length, 5);
  for (const file of files) {
    assert.ok(file.path.startsWith(`${envelopeDir}/`), file.path);
    const golden = readFileSync(join(mainRoot, file.path), "utf8");
    assert.equal(file.contents, golden, `bytes differ for ${file.path}`);
  }
});

test("generated sample package is byte-identical to the committed sources", () => {
  const ir = sampleIr();
  const generatedDir = generatedPackageName(ir).split(".").join("/");
  const files = emitJava(ir).filter((f) => f.path.startsWith(`${generatedDir}/`));
  assert.ok(files.length > 0);
  for (const file of files) {
    const golden = readFileSync(join(testRoot, file.path), "utf8");
    assert.equal(file.contents, golden, `bytes differ for ${file.path}`);
  }
  // Completeness both ways: every committed file is emitted, no extras.
  const committed = readdirSync(join(testRoot, generatedDir)).sort();
  const emitted = files.map((f) => basename(f.path)).sort();
  assert.deepEqual(emitted, committed);
});

test("every emitted file maps to exactly one committed golden", () => {
  const ir = sampleIr();
  const generatedDir = generatedPackageName(ir).split(".").join("/");
  for (const file of emitJava(ir)) {
    assert.ok(
      file.path.startsWith(`${envelopeDir}/`) ||
        file.path.startsWith(`${generatedDir}/`),
      `unexpected emit path: ${file.path}`,
    );
  }
});

test("emitting twice from independently parsed manifests is deterministic", () => {
  assert.deepEqual(emitJava(sampleIr()), emitJava(sampleIr()));
});

// ---------------------------------------------------------------------------
// Non-sample smoke IR: different naming prefixes, optional bytes field.
// ---------------------------------------------------------------------------

function smokeIr(): HostLibraryIr {
  return {
    manifestVersion: 1,
    engine: "sqlite-host-v1",
    library: {
      namespace: "Acme.Cache",
      interfaceName: "CacheHostMethods",
      apiLevel: 3,
      features: ["typedNamedBindings", "splitResultTables", "scriptInputs"],
    },
    naming: {
      callTablePrefix: "hostcall_",
      resultTablePrefix: "hostresult_",
      inputColumnPrefix: "in_",
      resultColumnPrefix: "out_",
      inputListTableInfix: "__in_",
      resultListTableInfix: "__out_",
    },
    queueTable: {
      name: "pending_host_calls",
      columns: ["queue_id", "call_id", "method", "status"],
    },
    inputsTable: {
      name: "script_inputs",
      columns: ["name", "value_type", "int_value", "text_value", "blob_value"],
    },
    scriptEnvelope: {
      engine: "sqlite-host-v1",
      bindingTypes: ["null", "int32", "int64", "bool", "text", "blob"],
    },
    methods: [
      {
        operationName: "StoreEntry",
        methodName: "storeEntry",
        handlerName: "StoreEntry",
        apiLevel: 3,
        callTable: "hostcall_store_entry",
        resultTable: "hostresult_store_entry",
        queueTrigger: "trg_hostcall_store_entry_queue",
        input: {
          modelName: "StoreEntryInput",
          fields: [
            {
              propertyName: "cacheKey",
              sqlName: "cache_key",
              column: "in_cache_key",
              scalarType: "string",
              optional: false,
            },
            {
              propertyName: "payload",
              sqlName: "payload",
              column: "in_payload",
              scalarType: "bytes",
              optional: true,
            },
            {
              propertyName: "ttlSeconds",
              sqlName: "ttl_seconds",
              column: "in_ttl_seconds",
              scalarType: "int32",
              optional: true,
            },
          ],
          listFields: [
            {
              propertyName: "tags",
              sqlName: "tags",
              childTable: "hostcall_store_entry__in_tags",
              itemModelName: "TagItem",
              itemFields: [
                {
                  propertyName: "tag",
                  sqlName: "tag",
                  column: "in_tag",
                  scalarType: "string",
                  optional: false,
                },
              ],
            },
          ],
        },
        result: {
          modelName: "StoreEntryResult",
          fields: [
            {
              propertyName: "generation",
              sqlName: "generation",
              column: "out_generation",
              scalarType: "int64",
              optional: false,
            },
          ],
          listFields: [],
        },
      },
    ],
  };
}

function fileByName(files: { path: string; contents: string }[], name: string) {
  const file = files.find((f) => basename(f.path) === name);
  assert.ok(file, `missing emitted file ${name}`);
  return file!;
}

test("smoke IR: package, model names, and naming prefixes come from the IR", () => {
  const files = emitJava(smokeIr());

  const input = fileByName(files, "StoreEntryInput.java");
  assert.equal(input.path, "acme/cache/generated/StoreEntryInput.java");
  assert.match(input.contents, /^package acme\.cache\.generated;$/m);
  assert.match(input.contents, /String cacheKey/);
  // Optional bytes stays byte[]; optional int32 boxes to Integer.
  assert.match(input.contents, /byte\[\] payload/);
  assert.match(input.contents, /Integer ttlSeconds/);
  assert.match(input.contents, /List<TagItem> tags/);
  assert.match(input.contents, /call table \{@code hostcall_store_entry\}/);

  const item = fileByName(files, "TagItem.java");
  assert.match(item.contents, /public record TagItem\(String tag\) \{/);
  assert.match(item.contents, /child table \{@code hostcall_store_entry__in_tags\}/);

  const result = fileByName(files, "StoreEntryResult.java");
  assert.match(result.contents, /public record StoreEntryResult\(long generation\) \{/);
  assert.match(result.contents, /result table \{@code hostresult_store_entry\}/);

  const descriptors = fileByName(files, "MethodDescriptors.java");
  assert.match(descriptors.contents, /public static final Method STORE_ENTRY = new Method\(/);
  assert.match(descriptors.contents, /"hostcall_store_entry"/);
  assert.match(descriptors.contents, /"hostresult_store_entry"/);
  assert.match(descriptors.contents, /"trg_hostcall_store_entry_queue"/);
  assert.match(
    descriptors.contents,
    /List\.of\("in_cache_key", "in_payload", "in_ttl_seconds"\)/,
  );
  assert.match(descriptors.contents, /List\.of\("hostcall_store_entry__in_tags"\)/);
  assert.match(descriptors.contents, /List\.of\("out_generation"\)/);
  assert.match(descriptors.contents, /public static final int API_LEVEL = 3;/);

  // Envelope files are protocol-shaped and unaffected by library naming.
  const script = fileByName(files, "Script.java");
  assert.equal(script.path, `${envelopeDir}/Script.java`);
  assert.match(script.contents, /ENGINE_V1 = "sqlite-host-v1";/);
});

test("smoke IR: emitted DTO list has no duplicates and reuses shared items", () => {
  const files = emitJava(smokeIr());
  const paths = files.map((f) => f.path);
  assert.equal(new Set(paths).size, paths.length);
});

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------

const scratchRoot = join(packageRoot, ".test-output");

test("CLI writes files identical to the emit API output", () => {
  const outDir = join(scratchRoot, `cli-${process.pid}`);
  rmSync(outDir, { recursive: true, force: true });
  const run = spawnSync(
    process.execPath,
    [join(packageRoot, "dist/cli.js"), manifestPath, outDir],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 0, `stderr: ${run.stderr}`);
  for (const file of emitJava(sampleIr())) {
    assert.equal(readFileSync(join(outDir, file.path), "utf8"), file.contents);
  }
  rmSync(outDir, { recursive: true, force: true });
});

test("CLI exits with usage error when arguments are missing", () => {
  const run = spawnSync(process.execPath, [join(packageRoot, "dist/cli.js")], {
    encoding: "utf8",
  });
  assert.equal(run.status, 2);
  assert.match(run.stderr, /usage: sqlite-host-emit-java/);
});

test("CLI exits non-zero for an unreadable manifest", () => {
  const outDir = join(scratchRoot, `cli-missing-${process.pid}`);
  const run = spawnSync(
    process.execPath,
    [join(packageRoot, "dist/cli.js"), join(scratchRoot, "nope.manifest.json"), outDir],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 1);
  assert.match(run.stderr, /sqlite-host-emit-java:/);
});
