import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { fileURLToPath } from "node:url";

// The web bundle is produced by scripts/build-web.mjs (part of the
// package test script). Importing it under Node exercises the bundled
// logic module; DOM wiring is lazy so the import has no side effects.
const BUNDLE_URL = new URL("../../web-dist/bundle.js", import.meta.url);

function readFixture(relative: string): string {
  return readFileSync(
    fileURLToPath(new URL(`../../../../fixtures/${relative}`, import.meta.url)),
    "utf8",
  );
}

const manifestJson = readFixture("manifests/sample-host.manifest.json");

test("web bundle ships index.html alongside bundle.js", () => {
  const html = readFileSync(fileURLToPath(new URL("../../web-dist/index.html", import.meta.url)), "utf8");
  assert.match(html, /<script type="module" src="\.\/bundle\.js">/);
  assert.doesNotMatch(html, /https?:\/\//, "page must not reference external URLs");
});

test("bundled logic lints example-001 as publishable", async () => {
  const { analyzePayload } = await import(BUNDLE_URL.href);
  const result = analyzePayload(
    manifestJson,
    readFixture("payloads/valid/example-001-read-then-conditional-write.json"),
  );
  assert.equal(result.publishable, true);
  assert.deepEqual(result.findings, []);
  assert.equal(result.metadata.interfaceName, "GameHostMethods");
  assert.ok(result.metadata.methods.some((m: { methodName: string }) => m.methodName === "getValue"));
});

test("bundled logic reports errors for an invalid fixture", async () => {
  const { analyzePayload } = await import(BUNDLE_URL.href);
  const result = analyzePayload(
    manifestJson,
    readFixture("payloads/invalid/missing-binding.json"),
  );
  assert.equal(result.publishable, false);
  assert.ok(
    result.findings.some(
      (f: { code: string; severity: string }) =>
        f.code === "missing-binding" && f.severity === "error",
    ),
  );
});
