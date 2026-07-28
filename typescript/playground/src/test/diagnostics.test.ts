/**
 * A half-typed editor buffer is the normal state of a playground, not
 * an error path: every kind of broken source must come back as
 * diagnostics the UI can render, never as a thrown exception. These
 * tests encode that contract — if the pipeline ever starts throwing,
 * the page goes blank instead of pointing at the offending line.
 */

import assert from "node:assert/strict";
import { test } from "node:test";

import { SAMPLE_SOURCE } from "../browser-host.js";
import { runPipeline } from "../pipeline.js";

test("a syntax error reports diagnostics with a 1-based line and column", async () => {
  const result = await runPipeline("this is not typespec at all {{{");
  assert.equal(result.ok, false);
  assert.ok(result.diagnostics.length > 0, "expected at least one diagnostic");
  const first = result.diagnostics[0];
  assert.equal(first.severity, "error");
  assert.equal(first.line, 1);
  assert.equal(first.column, 1);
});

test("an unresolved model reference points at the offending line", async () => {
  const result = await runPipeline(
    [
      'import "@sqlite-host/typespec";',
      "using SqliteHost;",
      "namespace Example.Game;",
      "@hostLibrary({ apiLevel: 1 })",
      "interface GameHostMethods {",
      '  @hostMethod({ name: "getValue", handler: "GetValue" })',
      "  op GetValue(input: MissingInput): MissingResult;",
      "}",
    ].join("\n"),
  );
  assert.equal(result.ok, false);
  const unresolved = result.diagnostics.filter((d) => d.code === "invalid-ref");
  assert.equal(unresolved.length, 2, JSON.stringify(result.diagnostics, null, 2));
  // Both references sit on the op declaration, line 7 (1-based).
  for (const diagnostic of unresolved) {
    assert.equal(diagnostic.line, 7);
    assert.ok(diagnostic.column !== undefined && diagnostic.column > 0);
  }
});

test("a SqliteHost validation error is reported, not thrown", async () => {
  // Structurally valid TypeSpec with no @hostLibrary interface: the
  // failure comes from SqliteHost's own frontend validation rather than
  // the TypeSpec compiler.
  const result = await runPipeline('import "@sqlite-host/typespec";\nmodel Unused { a: string; }');
  assert.equal(result.ok, false);
  assert.ok(
    result.diagnostics.some((d) => d.code.includes("no-host-library")),
    `expected a no-host-library diagnostic, got ${JSON.stringify(result.diagnostics)}`,
  );
});

test("an empty buffer reports diagnostics instead of throwing", async () => {
  const result = await runPipeline("");
  assert.equal(result.ok, false);
  assert.ok(result.diagnostics.length > 0);
});

test("a fixed source recompiles cleanly after a broken one", async () => {
  // Each run builds a fresh host, so a failed compile must not poison
  // the next one — that is what makes live typing usable.
  await runPipeline("{{{ broken");
  const result = await runPipeline(SAMPLE_SOURCE);
  assert.equal(result.ok, true);
  assert.deepEqual(result.diagnostics, []);
});
