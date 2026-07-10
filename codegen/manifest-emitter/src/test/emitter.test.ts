import { strict as assert } from "node:assert";
import { spawnSync } from "node:child_process";
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import type { EmitContext } from "@typespec/compiler";
import { parseManifest } from "@sqlite-host/codegen-core";
import {
  compileHostLibraries,
  compileHostLibrary,
} from "@sqlite-host/codegen-core/frontend";
import { $onEmit } from "../emitter.js";
import { emitDdl, emitManifest, libraryBaseName } from "../emit.js";
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

// ---------------------------------------------------------------------------
// Multi-library compilations: one manifest + DDL pair per @hostLibrary.
// ---------------------------------------------------------------------------

const TWO_LIBRARY_SOURCE = `
  import "@sqlite-host/typespec";
  using SqliteHost;
  namespace Test;

  @hostLibrary({ apiLevel: 1 })
  interface GameHostMethods {
    @hostMethod({ name: "getValue", handler: "GetValue" })
    op GetValue(input: KeyInput): ValueResult;
  }

  @hostLibrary({ apiLevel: 2, queueTable: "admin_queue" })
  interface AdminHostMethods {
    @hostMethod({ name: "getValue", handler: "GetValue" })
    op GetValue(input: KeyInput): ValueResult;
  }

  model KeyInput { key: string; }
  model ValueResult { value: int64; }
`;

function writeTwoLibraryTsp(dir: string): string {
  const path = join(dir, "two-libraries.tsp");
  writeFileSync(path, TWO_LIBRARY_SOURCE);
  return path;
}

test("libraryBaseName derives kebab-case from the interface name", async () => {
  const outDir = scratchDir("kebab");
  const result = await compileHostLibraries(writeTwoLibraryTsp(outDir));
  assert.ok(
    result.irs,
    JSON.stringify(result.diagnostics.map((d) => d.message)),
  );
  assert.deepEqual(result.irs.map(libraryBaseName), [
    "game-host-methods",
    "admin-host-methods",
  ]);
  rmSync(outDir, { recursive: true, force: true });
});

test("$onEmit writes one artifact set per library, kebab-case named", async () => {
  const outDir = scratchDir("multi-onemit");
  const { program, irs } = await compileHostLibraries(writeTwoLibraryTsp(outDir));
  assert.ok(irs);
  const context = {
    program,
    emitterOutputDir: outDir,
    options: {},
  } as EmitContext<ManifestEmitterOptions>;
  await $onEmit(context);
  const game = parseManifest(
    readFileSync(join(outDir, "game-host-methods.manifest.json"), "utf8"),
  );
  assert.equal(game.library.interfaceName, "GameHostMethods");
  const admin = parseManifest(
    readFileSync(join(outDir, "admin-host-methods.manifest.json"), "utf8"),
  );
  assert.equal(admin.library.interfaceName, "AdminHostMethods");
  assert.equal(admin.queueTable.name, "admin_queue");
  assert.match(
    readFileSync(join(outDir, "admin-host-methods.ddl.sql"), "utf8"),
    /CREATE TABLE admin_queue \(/,
  );
  assert.match(
    readFileSync(join(outDir, "game-host-methods.ddl.sql"), "utf8"),
    /CREATE TABLE pending_host_calls \(/,
  );
  rmSync(outDir, { recursive: true, force: true });
});

test("$onEmit rejects the base-name option for multi-library programs", async () => {
  const outDir = scratchDir("multi-basename");
  const { program } = await compileHostLibraries(writeTwoLibraryTsp(outDir));
  const context = {
    program,
    emitterOutputDir: outDir,
    options: { "base-name": "example-game" },
  } as EmitContext<ManifestEmitterOptions>;
  await $onEmit(context);
  assert.ok(
    program.diagnostics.some(
      (d) => d.code === "@sqlite-host/emitter-manifest/base-name-multiple-libraries",
    ),
    `expected base-name-multiple-libraries, got: ${program.diagnostics
      .map((d) => d.code)
      .join(", ")}`,
  );
  rmSync(outDir, { recursive: true, force: true });
});

test("CLI emits per-library files for a multi-library .tsp", () => {
  const outDir = scratchDir("multi-cli");
  const tsp = writeTwoLibraryTsp(outDir);
  const run = spawnSync(
    process.execPath,
    [join(packageRoot, "dist/cli.js"), tsp, join(outDir, "out")],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 0, `stderr: ${run.stderr}`);
  for (const name of [
    "game-host-methods.manifest.json",
    "game-host-methods.ddl.sql",
    "admin-host-methods.manifest.json",
    "admin-host-methods.ddl.sql",
  ]) {
    assert.ok(run.stdout.includes(name), `stdout missing ${name}`);
    readFileSync(join(outDir, "out", name), "utf8");
  }
  rmSync(outDir, { recursive: true, force: true });
});

test("CLI rejects --base-name for multi-library compilations", () => {
  const outDir = scratchDir("multi-cli-basename");
  const tsp = writeTwoLibraryTsp(outDir);
  const run = spawnSync(
    process.execPath,
    [
      join(packageRoot, "dist/cli.js"),
      tsp,
      join(outDir, "out"),
      "--base-name",
      "example-game",
    ],
    { encoding: "utf8" },
  );
  assert.equal(run.status, 2);
  assert.match(run.stderr, /--base-name applies to single-library compilations only/);
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
