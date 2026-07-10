#!/usr/bin/env node
// Cross-language golden runner (plan §24.2): recompiles the sample host
// from TypeSpec, re-runs every emitter, and byte-compares the output
// against the committed fixtures and vendored generated sources. Any
// difference fails the run — one contract, identical bytes everywhere.
import { execSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");

console.log("==> building workspace packages");
execSync("pnpm -r --silent run build", { cwd: root, stdio: "inherit" });

const core = await import(join(root, "codegen/core/dist/index.js"));
const frontend = await import(join(root, "codegen/core/dist/frontend.js"));
const manifestEmitter = await import(join(root, "codegen/manifest-emitter/dist/emit.js"));
const csharpEmitter = await import(join(root, "codegen/csharp-emitter/dist/emit.js"));
const javaEmitter = await import(join(root, "codegen/java-emitter/dist/emit.js"));
const tsEmitter = await import(join(root, "codegen/typescript-emitter/dist/emit.js"));

const manifestBytes = readFileSync(join(root, "fixtures/manifests/sample-host.manifest.json"), "utf8");
const ddlBytes = readFileSync(join(root, "fixtures/schemas/sample-host.ddl.sql"), "utf8");
const ir = core.parseManifest(manifestBytes);

let checks = 0;
function check(name, fn) {
  fn();
  checks++;
  console.log(`ok  ${name}`);
}

// 1. TypeSpec frontend: sample .tsp normalizes to the canonical IR.
const compiled = await frontend.compileHostLibrary(
  join(root, "typespec/examples/sample-host-methods.tsp"),
);
const errors = compiled.diagnostics.filter((d) => d.severity === "error");
assert.equal(errors.length, 0, "sample .tsp compiled with errors: " + JSON.stringify(errors, null, 2));
check("frontend: sample-host-methods.tsp -> IR equals canonical manifest IR", () => {
  assert.deepEqual(compiled.ir, ir);
});

// 2. Neutral artifacts.
check("manifest emitter: byte-identical manifest", () => {
  assert.equal(manifestEmitter.emitManifest(compiled.ir), manifestBytes);
});
check("manifest emitter: byte-identical DDL snapshot", () => {
  assert.equal(manifestEmitter.emitDdl(compiled.ir), ddlBytes);
});
check("core: DDL from IR equals snapshot", () => {
  assert.equal(core.generateSchemaScript(ir), ddlBytes);
});

// 3. C# emitter vs vendored sources.
const csharpGoldens = {
  "HostMethodDtos.g.cs": "csharp/SqliteHost.Generated.Sample/HostMethodDtos.g.cs",
  "IGeneratedHostHandlers.g.cs": "csharp/SqliteHost.Generated.Sample/IGeneratedHostHandlers.g.cs",
  "GeneratedHostMethodSpecs.g.cs": "csharp/SqliteHost.Generated.Sample/GeneratedHostMethodSpecs.g.cs",
  "GeneratedHostDefinition.g.cs": "csharp/SqliteHost.Generated.Sample/GeneratedHostDefinition.g.cs",
  "GeneratedSchemaSql.g.cs": "csharp/SqliteHost.Generated.Sample/GeneratedSchemaSql.g.cs",
  "envelope/ScriptEnvelope.g.cs": "csharp/SqliteHost.Abstractions/ScriptEnvelope.g.cs",
};
const csharpFiles = csharpEmitter.emitCSharp(compiled.ir);
check("csharp emitter: emits exactly the pinned file set", () => {
  assert.deepEqual(csharpFiles.map((f) => f.path).sort(), Object.keys(csharpGoldens).sort());
});
for (const file of csharpFiles) {
  check(`csharp emitter: ${file.path} byte-identical`, () => {
    assert.equal(file.contents, readFileSync(join(root, csharpGoldens[file.path]), "utf8"));
  });
}

// 4. Java emitter vs vendored sources (envelope in main tree, generated
//    sample package in the test tree).
const javaMain = "java/sqlite-host-model/src/main/java";
const javaTest = "java/sqlite-host-model/src/test/java";
const envelopeDir = javaEmitter.ENVELOPE_PACKAGE.split(".").join("/");
const generatedDir = javaEmitter.generatedPackageName(compiled.ir).split(".").join("/");
const javaFiles = javaEmitter.emitJava(compiled.ir);
assert.ok(javaFiles.length > 0, "java emitter emitted nothing");
for (const file of javaFiles) {
  const base = file.path.startsWith(envelopeDir + "/") ? javaMain : javaTest;
  assert.ok(
    file.path.startsWith(envelopeDir + "/") || file.path.startsWith(generatedDir + "/"),
    `unexpected java emit path ${file.path}`,
  );
  check(`java emitter: ${file.path} byte-identical`, () => {
    assert.equal(file.contents, readFileSync(join(root, base, file.path), "utf8"));
  });
}

// 5. TypeScript emitter vs vendored sources.
const tsFiles = tsEmitter.emitTypeScript(compiled.ir);
check("typescript emitter: emits exactly the pinned file set", () => {
  assert.deepEqual(
    tsFiles.map((f) => f.path).sort(),
    [tsEmitter.ENVELOPE_FILE_PATH, tsEmitter.hostTypesFilePath()].sort(),
  );
});
for (const file of tsFiles) {
  check(`typescript emitter: ${file.path} byte-identical`, () => {
    assert.equal(file.contents, readFileSync(join(root, "typescript", file.path), "utf8"));
  });
}

console.log(`\nCROSS-LANGUAGE GOLDENS GREEN (${checks} checks)`);
