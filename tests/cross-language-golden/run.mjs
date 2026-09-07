#!/usr/bin/env node
// Cross-language golden runner (docs/validation.md layer 2): recompiles the sample host
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

// 2b. The second, conformance-only host: one method above the lowest
//     API level a script may declare, so `method-api-level-too-high` can
//     have a payload fixture (fixtures/payloads/expectations.json pins it
//     through the per-case `manifest` key). Manifests are never
//     hand-written, so it is pinned here exactly like the sample host —
//     minus the language emitters, since nothing generates code from it.
const highApiManifestBytes = readFileSync(
  join(root, "fixtures/manifests/high-api-host.manifest.json"),
  "utf8",
);
const highApiDdlBytes = readFileSync(join(root, "fixtures/schemas/high-api-host.ddl.sql"), "utf8");
const highApiCompiled = await frontend.compileHostLibrary(
  join(root, "typespec/examples/high-api-host-methods.tsp"),
);
const highApiErrors = highApiCompiled.diagnostics.filter((d) => d.severity === "error");
assert.equal(
  highApiErrors.length,
  0,
  "high-api .tsp compiled with errors: " + JSON.stringify(highApiErrors, null, 2),
);
check("frontend: high-api-host-methods.tsp -> IR equals canonical manifest IR", () => {
  assert.deepEqual(highApiCompiled.ir, core.parseManifest(highApiManifestBytes));
});
check("manifest emitter: byte-identical high-api manifest", () => {
  assert.equal(manifestEmitter.emitManifest(highApiCompiled.ir), highApiManifestBytes);
});
check("manifest emitter: byte-identical high-api DDL snapshot", () => {
  assert.equal(manifestEmitter.emitDdl(highApiCompiled.ir), highApiDdlBytes);
});
check("core: high-api DDL from IR equals snapshot", () => {
  assert.equal(core.generateSchemaScript(core.parseManifest(highApiManifestBytes)), highApiDdlBytes);
});

// 2c. The third, conformance-only host: the sample host's smallest useful
//     slice under a RAISED SQLite floor (3.39.0), so the corpus can pin the
//     other half of sqlite-version-too-low-for-syntax — that raising
//     `minSqliteVersion` silences the rule in both validators. Pinned here
//     like the other two, minus the language emitters.
const syntaxFloorManifestBytes = readFileSync(
  join(root, "fixtures/manifests/syntax-floor-host.manifest.json"),
  "utf8",
);
const syntaxFloorDdlBytes = readFileSync(
  join(root, "fixtures/schemas/syntax-floor-host.ddl.sql"),
  "utf8",
);
const syntaxFloorCompiled = await frontend.compileHostLibrary(
  join(root, "typespec/examples/syntax-floor-host-methods.tsp"),
);
const syntaxFloorErrors = syntaxFloorCompiled.diagnostics.filter((d) => d.severity === "error");
assert.equal(
  syntaxFloorErrors.length,
  0,
  "syntax-floor .tsp compiled with errors: " + JSON.stringify(syntaxFloorErrors, null, 2),
);
check("frontend: syntax-floor-host-methods.tsp -> IR equals canonical manifest IR", () => {
  assert.deepEqual(syntaxFloorCompiled.ir, core.parseManifest(syntaxFloorManifestBytes));
});
check("manifest emitter: byte-identical syntax-floor manifest", () => {
  assert.equal(manifestEmitter.emitManifest(syntaxFloorCompiled.ir), syntaxFloorManifestBytes);
});
check("manifest emitter: byte-identical syntax-floor DDL snapshot", () => {
  assert.equal(manifestEmitter.emitDdl(syntaxFloorCompiled.ir), syntaxFloorDdlBytes);
});
check("core: syntax-floor DDL from IR equals snapshot", () => {
  assert.equal(
    core.generateSchemaScript(core.parseManifest(syntaxFloorManifestBytes)),
    syntaxFloorDdlBytes,
  );
});
check("the syntax-floor host actually declares a raised floor", () => {
  // The whole reason the host exists. If it ever drifts back to the default
  // 3.19.3, every "silent above the floor" fixture keeps passing for the
  // wrong reason, which is the failure mode a conformance corpus cannot see.
  assert.ok(
    syntaxFloorCompiled.ir.library.minSqliteVersionNumber >
      core.parseManifest(manifestBytes).library.minSqliteVersionNumber,
  );
});

// 2d. The fourth, conformance-only host: every configurable name
//     overridden. Each DDL generator carries its own copy of the naming
//     rules, and against a default-named host a copy that hardcoded
//     "input_" or read the queue table from the wrong block emits exactly
//     the same bytes as a correct one. This is the input that tells them
//     apart, and the Java and C# DDL golden tests read the same pair of
//     files. Pinned like the other extra hosts, minus the language
//     emitters.
const customNamingManifestBytes = readFileSync(
  join(root, "fixtures/manifests/custom-naming-host.manifest.json"),
  "utf8",
);
const customNamingDdlBytes = readFileSync(
  join(root, "fixtures/schemas/custom-naming-host.ddl.sql"),
  "utf8",
);
const customNamingCompiled = await frontend.compileHostLibrary(
  join(root, "typespec/examples/custom-naming-host-methods.tsp"),
);
const customNamingErrors = customNamingCompiled.diagnostics.filter(
  (d) => d.severity === "error",
);
assert.equal(
  customNamingErrors.length,
  0,
  "custom-naming .tsp compiled with errors: " + JSON.stringify(customNamingErrors, null, 2),
);
check("frontend: custom-naming-host-methods.tsp -> IR equals canonical manifest IR", () => {
  assert.deepEqual(customNamingCompiled.ir, core.parseManifest(customNamingManifestBytes));
});
check("manifest emitter: byte-identical custom-naming manifest", () => {
  assert.equal(manifestEmitter.emitManifest(customNamingCompiled.ir), customNamingManifestBytes);
});
check("manifest emitter: byte-identical custom-naming DDL snapshot", () => {
  assert.equal(manifestEmitter.emitDdl(customNamingCompiled.ir), customNamingDdlBytes);
});
check("core: custom-naming DDL from IR equals snapshot", () => {
  assert.equal(
    core.generateSchemaScript(core.parseManifest(customNamingManifestBytes)),
    customNamingDdlBytes,
  );
});
check("the custom-naming host actually overrides every configurable name", () => {
  // The whole reason the host exists. A value that drifts back to its
  // default silently stops distinguishing a correct generator from one
  // that hardcodes that default — the failure a golden cannot see,
  // because both sides then produce the same bytes.
  const sample = core.parseManifest(manifestBytes);
  const custom = customNamingCompiled.ir;
  for (const [block, key] of [
    ...Object.keys(sample.naming).map((k) => ["naming", k]),
    ...Object.keys(sample.columns).map((k) => ["columns", k]),
  ]) {
    assert.notEqual(
      custom[block][key],
      sample[block][key],
      `${block}.${key} is still the default ${JSON.stringify(sample[block][key])}`,
    );
  }
  for (const table of ["queueTable", "inputsTable", "varsTable", "controlTable"]) {
    assert.notEqual(custom[table].name, sample[table].name, `${table} is still the default`);
  }
});

// 3. C# emitter vs vendored sources.
const csharpGoldens = {
  "HostMethodDtos.g.cs": "csharp/SqliteHost.Generated.Sample/HostMethodDtos.g.cs",
  "IGeneratedHostHandlers.g.cs": "csharp/SqliteHost.Generated.Sample/IGeneratedHostHandlers.g.cs",
  "GeneratedHostMethodSpecs.g.cs": "csharp/SqliteHost.Generated.Sample/GeneratedHostMethodSpecs.g.cs",
  "GeneratedHostDefinition.g.cs": "csharp/SqliteHost.Generated.Sample/GeneratedHostDefinition.g.cs",
  "GeneratedSchemaSql.g.cs": "csharp/SqliteHost.Generated.Sample/GeneratedSchemaSql.g.cs",
  "envelope/ScriptEnvelope.g.cs": "csharp/SqliteHost.Abstractions/ScriptEnvelope.g.cs",
  "runtime/ProtocolConstants.g.cs": "csharp/SqliteHost.Runtime/ProtocolConstants.g.cs",
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

// 3b. C# emitter, compact and ultra size profiles vs vendored sources
//     (emitted with the namespace override the committed samples use;
//     ultra has no DTO file).
const csharpProfileGoldens = [
  {
    profile: "compact",
    namespaceOverride: "Example.Game.Generated.Compact",
    goldens: {
      "HostMethodDtos.g.cs": "csharp/SqliteHost.Generated.Sample.Compact/HostMethodDtos.g.cs",
      "IGeneratedHostHandlers.g.cs": "csharp/SqliteHost.Generated.Sample.Compact/IGeneratedHostHandlers.g.cs",
      "GeneratedHostMethodSpecs.g.cs": "csharp/SqliteHost.Generated.Sample.Compact/GeneratedHostMethodSpecs.g.cs",
      "GeneratedHostDefinition.g.cs": "csharp/SqliteHost.Generated.Sample.Compact/GeneratedHostDefinition.g.cs",
      "GeneratedSchemaSql.g.cs": "csharp/SqliteHost.Generated.Sample.Compact/GeneratedSchemaSql.g.cs",
      "envelope/ScriptEnvelope.g.cs": "csharp/SqliteHost.Abstractions/ScriptEnvelope.g.cs",
      "runtime/ProtocolConstants.g.cs": "csharp/SqliteHost.Runtime/ProtocolConstants.g.cs",
    },
  },
  {
    profile: "ultra",
    namespaceOverride: "Example.Game.Generated.Ultra",
    goldens: {
      "IGeneratedHostHandlers.g.cs": "csharp/SqliteHost.Generated.Sample.Ultra/IGeneratedHostHandlers.g.cs",
      "GeneratedHostMethodSpecs.g.cs": "csharp/SqliteHost.Generated.Sample.Ultra/GeneratedHostMethodSpecs.g.cs",
      "GeneratedHostDefinition.g.cs": "csharp/SqliteHost.Generated.Sample.Ultra/GeneratedHostDefinition.g.cs",
      "GeneratedSchemaSql.g.cs": "csharp/SqliteHost.Generated.Sample.Ultra/GeneratedSchemaSql.g.cs",
      "envelope/ScriptEnvelope.g.cs": "csharp/SqliteHost.Abstractions/ScriptEnvelope.g.cs",
      "runtime/ProtocolConstants.g.cs": "csharp/SqliteHost.Runtime/ProtocolConstants.g.cs",
    },
  },
];
for (const { profile, namespaceOverride, goldens } of csharpProfileGoldens) {
  const files = csharpEmitter.emitCSharp(compiled.ir, { profile, namespaceOverride });
  check(`csharp emitter (${profile}): emits exactly the pinned file set`, () => {
    assert.deepEqual(files.map((f) => f.path).sort(), Object.keys(goldens).sort());
  });
  for (const file of files) {
    check(`csharp emitter (${profile}): ${file.path} byte-identical`, () => {
      assert.equal(file.contents, readFileSync(join(root, goldens[file.path]), "utf8"));
    });
  }
}

// 4. Java emitter vs vendored sources (envelope in main tree, generated
//    sample package in the test tree).
const javaMain = "java/sqlite-host-model/src/main/java";
const javaTest = "java/sqlite-host-model/src/test/java";
const envelopeDir = javaEmitter.ENVELOPE_PACKAGE.split(".").join("/");
const generatedDir = javaEmitter.generatedPackageName(compiled.ir).split(".").join("/");
const protocolFile = javaEmitter.PROTOCOL_FILE; // host-independent, main tree
const javaFiles = javaEmitter.emitJava(compiled.ir);
assert.ok(javaFiles.length > 0, "java emitter emitted nothing");
for (const file of javaFiles) {
  const inMainTree = file.path.startsWith(envelopeDir + "/") || file.path === protocolFile;
  const base = inMainTree ? javaMain : javaTest;
  assert.ok(
    inMainTree || file.path.startsWith(generatedDir + "/"),
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
    [
      tsEmitter.ENVELOPE_FILE_PATH,
      tsEmitter.PROTOCOL_FILE_PATH,
      tsEmitter.hostTypesFilePath(),
    ].sort(),
  );
});
for (const file of tsFiles) {
  check(`typescript emitter: ${file.path} byte-identical`, () => {
    assert.equal(file.contents, readFileSync(join(root, "typescript", file.path), "utf8"));
  });
}

console.log(`\nCROSS-LANGUAGE GOLDENS GREEN (${checks} checks)`);
