/**
 * Parity: the playground pipeline running over the in-memory host must
 * produce exactly what the on-disk codegen produces. The oracle is the
 * same committed golden set tests/cross-language-golden/run.mjs pins,
 * so a browser/CLI divergence fails here first.
 *
 * The pipeline is run unmodified — the only thing this test changes is
 * that Node, rather than a browser, is the runtime.
 */

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

import { parseManifest } from "@sqlite-host/codegen-core";
import { SAMPLE_SOURCE } from "../browser-host.js";
import { CSHARP_PROFILES, runPipeline } from "../pipeline.js";
import type { PlaygroundFile, PlaygroundResult } from "../pipeline.js";

/** projects/sqlitehost, from dist/test/. */
function projectFile(relative: string): string {
  return readFileSync(fileURLToPath(new URL(`../../../../${relative}`, import.meta.url)), "utf8");
}

const manifestGolden = projectFile("fixtures/manifests/sample-host.manifest.json");
const ddlGolden = projectFile("fixtures/schemas/sample-host.ddl.sql");

// run.mjs mapping: per-app files live in the sample project, the
// protocol envelope and constants in the shared abstractions/runtime.
const CSHARP_GOLDENS: Record<string, string> = {
  "HostMethodDtos.g.cs": "csharp/SqliteHost.Generated.Sample/HostMethodDtos.g.cs",
  "IGeneratedHostHandlers.g.cs": "csharp/SqliteHost.Generated.Sample/IGeneratedHostHandlers.g.cs",
  "GeneratedHostMethodSpecs.g.cs":
    "csharp/SqliteHost.Generated.Sample/GeneratedHostMethodSpecs.g.cs",
  "GeneratedHostDefinition.g.cs":
    "csharp/SqliteHost.Generated.Sample/GeneratedHostDefinition.g.cs",
  "GeneratedSchemaSql.g.cs": "csharp/SqliteHost.Generated.Sample/GeneratedSchemaSql.g.cs",
  "envelope/ScriptEnvelope.g.cs": "csharp/SqliteHost.Abstractions/ScriptEnvelope.g.cs",
  "runtime/ProtocolConstants.g.cs": "csharp/SqliteHost.Runtime/ProtocolConstants.g.cs",
};

const JAVA_MAIN = "java/sqlite-host-model/src/main/java";
const JAVA_TEST = "java/sqlite-host-model/src/test/java";
const JAVA_ENVELOPE_DIR = "io/sqlitehost/model/envelope";
const JAVA_PROTOCOL_FILE = "io/sqlitehost/model/Protocol.java";

const compiled: PlaygroundResult = await runPipeline(SAMPLE_SOURCE);

function assertOk(result: PlaygroundResult): asserts result is Extract<
  PlaygroundResult,
  { ok: true }
> {
  assert.ok(
    result.ok,
    `sample host definition failed to compile: ${JSON.stringify(result.diagnostics, null, 2)}`,
  );
}

test("sample host definition compiles cleanly over the in-memory host", () => {
  assertOk(compiled);
  assert.deepEqual(compiled.diagnostics, []);
});

test("in-memory compile yields the canonical manifest IR", () => {
  assertOk(compiled);
  // The strongest parity statement: every emitter is a pure function of
  // the IR, so an identical IR means identical bytes for any emitter
  // option, including the C# profiles asserted below by file set only.
  assert.deepEqual(compiled.ir, parseManifest(manifestGolden));
});

test("manifest is byte-identical to the committed fixture", () => {
  assertOk(compiled);
  assert.equal(compiled.output.manifest, manifestGolden);
});

test("DDL is byte-identical to the committed snapshot", () => {
  assertOk(compiled);
  assert.equal(compiled.output.ddl, ddlGolden);
});

test("C# (classic) is byte-identical to the committed sources", () => {
  assertOk(compiled);
  const files = compiled.output.csharp.classic;
  assert.deepEqual(
    files.map((file) => file.path).sort(),
    Object.keys(CSHARP_GOLDENS).sort(),
  );
  for (const file of files) {
    assert.equal(file.contents, projectFile(CSHARP_GOLDENS[file.path]), file.path);
  }
});

test("every C# profile emits its committed file set", () => {
  assertOk(compiled);
  // Ultra drops the DTO file; the committed Sample.Ultra project shows
  // the same set. Bytes for the non-default profiles are pinned by
  // tests/cross-language-golden/run.mjs, which emits them with the
  // namespace override those committed projects use.
  assert.deepEqual(Object.keys(compiled.output.csharp).sort(), [...CSHARP_PROFILES].sort());
  assert.deepEqual(
    compiled.output.csharp.ultra.map((file) => file.path).sort(),
    Object.keys(CSHARP_GOLDENS)
      .filter((path) => path !== "HostMethodDtos.g.cs")
      .sort(),
  );
  assert.deepEqual(
    compiled.output.csharp.compact.map((file) => file.path).sort(),
    Object.keys(CSHARP_GOLDENS).sort(),
  );
});

test("Java is byte-identical to the committed sources", () => {
  assertOk(compiled);
  const files: PlaygroundFile[] = compiled.output.java;
  assert.ok(files.length > 0, "java emitter emitted nothing");
  for (const file of files) {
    const inMainTree =
      file.path.startsWith(`${JAVA_ENVELOPE_DIR}/`) || file.path === JAVA_PROTOCOL_FILE;
    const base = inMainTree ? JAVA_MAIN : JAVA_TEST;
    assert.equal(file.contents, projectFile(`${base}/${file.path}`), file.path);
  }
});

test("TypeScript is byte-identical to the committed sources", () => {
  assertOk(compiled);
  const files: PlaygroundFile[] = compiled.output.typescript;
  assert.ok(files.length > 0, "typescript emitter emitted nothing");
  for (const file of files) {
    assert.equal(file.contents, projectFile(`typescript/${file.path}`), file.path);
  }
});
