import { strict as assert } from "node:assert";
import { spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import type { EmitContext } from "@typespec/compiler";
import { compileHostLibrary } from "@sqlite-host/codegen-core/frontend";
import { $onEmit } from "../emitter.js";
import { emitDdl, emitManifest } from "../emit.js";
import type { ManifestEmitterOptions } from "../lib.js";

const packageRoot = resolve(fileURLToPath(import.meta.url), "../../..");
const projectRoot = resolve(packageRoot, "../..");
const samplePath = join(
  projectRoot,
  "typespec/examples/sample-host-methods.tsp",
);
const manifestFixture = readFileSync(
  join(projectRoot, "fixtures/manifests/sample-host.manifest.json"),
  "utf8",
);
const ddlFixture = readFileSync(
  join(projectRoot, "fixtures/schemas/sample-host.ddl.sql"),
  "utf8",
);
const scratchRoot = join(packageRoot, ".tsp-output", "emitter-tests");

function scratchDir(name: string): string {
  const dir = join(scratchRoot, `${name}-${process.pid}`);
  rmSync(dir, { recursive: true, force: true });
  mkdirSync(dir, { recursive: true });
  return dir;
}

async function compileSampleIr() {
  const result = await compileHostLibrary(samplePath);
  assert.ok(
    result.ir,
    `sample compile failed: ${result.diagnostics.map((d) => d.message).join("; ")}`,
  );
  return result;
}

test("emitManifest output is byte-identical to the committed manifest fixture", async () => {
  const { ir } = await compileSampleIr();
  assert.equal(emitManifest(ir!), manifestFixture);
});

test("emitDdl output is byte-identical to the committed DDL fixture", async () => {
  const { ir } = await compileSampleIr();
  assert.equal(emitDdl(ir!), ddlFixture);
});

test("emitting twice from independent compiles is deterministic", async () => {
  const first = await compileSampleIr();
  const second = await compileSampleIr();
  assert.equal(emitManifest(first.ir!), emitManifest(second.ir!));
  assert.equal(emitDdl(first.ir!), emitDdl(second.ir!));
});

test("$onEmit writes sample-host.manifest.json and sample-host.ddl.sql", async () => {
  const { program } = await compileSampleIr();
  const outDir = scratchDir("onemit");
  const context = {
    program,
    emitterOutputDir: outDir,
    options: {},
  } as EmitContext<ManifestEmitterOptions>;
  await $onEmit(context);
  assert.equal(
    readFileSync(join(outDir, "sample-host.manifest.json"), "utf8"),
    manifestFixture,
  );
  assert.equal(
    readFileSync(join(outDir, "sample-host.ddl.sql"), "utf8"),
    ddlFixture,
  );
  rmSync(outDir, { recursive: true, force: true });
});

test("$onEmit honors the base-name option", async () => {
  const { program } = await compileSampleIr();
  const outDir = scratchDir("basename");
  const context = {
    program,
    emitterOutputDir: outDir,
    options: { "base-name": "example-game" },
  } as EmitContext<ManifestEmitterOptions>;
  await $onEmit(context);
  assert.equal(
    readFileSync(join(outDir, "example-game.manifest.json"), "utf8"),
    manifestFixture,
  );
  assert.equal(
    readFileSync(join(outDir, "example-game.ddl.sql"), "utf8"),
    ddlFixture,
  );
  rmSync(outDir, { recursive: true, force: true });
});

test("CLI compiles a .tsp path and writes fixture-identical files", () => {
  const outDir = scratchDir("cli");
  const run = spawnSync(
    process.execPath,
    [join(packageRoot, "dist/cli.js"), samplePath, outDir],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 0, `stderr: ${run.stderr}`);
  assert.equal(
    readFileSync(join(outDir, "sample-host.manifest.json"), "utf8"),
    manifestFixture,
  );
  assert.equal(
    readFileSync(join(outDir, "sample-host.ddl.sql"), "utf8"),
    ddlFixture,
  );
  rmSync(outDir, { recursive: true, force: true });
});

test("CLI exits non-zero and emits nothing for an invalid model", () => {
  const outDir = scratchDir("cli-invalid");
  const badTsp = join(outDir, "bad.tsp");
  writeFileSync(
    badTsp,
    `
      import "@sqlite-host/typespec";
      using SqliteHost;
      namespace Test;

      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: string): Out;
      }
      model Out { value: int64; }
    `,
  );
  const run = spawnSync(
    process.execPath,
    [join(packageRoot, "dist/cli.js"), badTsp, join(outDir, "out")],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 1);
  assert.match(run.stderr, /invalid-method-shape/);
  rmSync(outDir, { recursive: true, force: true });
});
