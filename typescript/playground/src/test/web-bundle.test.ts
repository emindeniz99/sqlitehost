/**
 * The shipped page. scripts/build-web.mjs runs as part of `npm test`
 * (see the package test script), so reaching these assertions already
 * proves the bundle built; what is checked here is that the artifacts
 * landed, that the page is self-contained, and that the bundled
 * pipeline — compiler and all — actually runs.
 */

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const WEB_DIST = new URL("../../web-dist/", import.meta.url);
const BUNDLE_URL = new URL("bundle.js", WEB_DIST);

/**
 * "No server, no network" is the page's load-bearing promise — it is
 * what makes pasting a private host definition into the page safe. The
 * e2e suite proves it in a real browser with an exact request list;
 * this file is the unit-level half, and it used to inspect index.html
 * only, never the bundle where a live `fetch()` to a hardcoded
 * https://registry.npmjs.org actually sat (unreachable, but shipped).
 *
 * Every network API is replaced with a throwing counter before the
 * bundle is imported below, so ANY test in this file that reaches the
 * network fails rather than succeeding quietly.
 */
let networkAttempts = 0;
for (const name of ["fetch", "XMLHttpRequest", "WebSocket", "EventSource"] as const) {
  Object.defineProperty(globalThis, name, {
    configurable: true,
    writable: true,
    value: (...args: unknown[]) => {
      networkAttempts += 1;
      throw new Error(`playground attempted ${name}(${String(args[0])})`);
    },
  });
}

test("build:web produced bundle.js and index.html", () => {
  const bundle = readFileSync(fileURLToPath(BUNDLE_URL), "utf8");
  const html = readFileSync(fileURLToPath(new URL("index.html", WEB_DIST)), "utf8");
  assert.ok(bundle.length > 0, "bundle.js is empty");
  assert.match(html, /<script type="module" src="\.\/bundle\.js">/);
});

test("the page makes no external requests", () => {
  const html = readFileSync(fileURLToPath(new URL("index.html", WEB_DIST)), "utf8");
  assert.doesNotMatch(html, /https?:\/\//, "page must not reference external URLs");
  assert.doesNotMatch(html, /<link[^>]+href/i, "page must not link external stylesheets");
});

test("the bundle inlines every dependency", () => {
  const bundle = readFileSync(fileURLToPath(BUNDLE_URL), "utf8");
  // A bare or node: import statement surviving into the bundle means a
  // dependency was left external and the page would fail to load.
  assert.doesNotMatch(bundle, /^import\s.*from\s*"(?!\.)/m, "bundle has an unresolved import");
  assert.doesNotMatch(bundle, /require\("node:/, "bundle requires a node builtin");
});

test("every element the bundle wires up exists in the page", () => {
  // Derived from the shipped bundle rather than restated here, so a
  // rename on either side of the main.ts / index.html contract fails
  // instead of silently producing a dead page.
  const bundle = readFileSync(fileURLToPath(BUNDLE_URL), "utf8");
  const html = readFileSync(fileURLToPath(new URL("index.html", WEB_DIST)), "utf8");
  const ids = [...bundle.matchAll(/getElementById\("([^"]+)"\)/g)].map((match) => match[1]);
  assert.ok(ids.length > 0, "no getElementById calls found in the bundle");
  for (const id of new Set(ids)) {
    assert.match(html, new RegExp(`id="${id}"`), `index.html is missing id="${id}"`);
  }
});

test("the bundle contains no network sink", () => {
  // Not "no https:// strings" — the bundle legitimately carries schema
  // ids and doc URLs. What must not be there is a call that reaches the
  // network, and the npm registry client is the one that was.
  const bundle = readFileSync(fileURLToPath(BUNDLE_URL), "utf8");
  for (const sink of ["fetch(", "XMLHttpRequest", "WebSocket(", "EventSource(", "sendBeacon"]) {
    assert.ok(!bundle.includes(sink), `bundle contains a network sink: ${sink}`);
  }
  assert.ok(
    !bundle.includes("registry.npmjs.org"),
    "bundle still embeds the npm registry endpoint",
  );
});

test("the bundled pipeline compiles the prefilled sample", async () => {
  const { runPipeline, SAMPLE_SOURCE, DEBOUNCE_MS } = await import(BUNDLE_URL.href);
  assert.equal(DEBOUNCE_MS, 400);
  const result = await runPipeline(SAMPLE_SOURCE);
  assert.equal(result.ok, true);
  assert.deepEqual(result.diagnostics, []);
  assert.equal(result.ir.library.interfaceName, "GameHostMethods");
  assert.deepEqual(Object.keys(result.output.csharp).sort(), ["classic", "compact", "ultra"]);
  assert.ok(result.output.java.length > 0);
  assert.ok(result.output.typescript.length > 0);
});

test("the pipeline ran without touching the network", () => {
  // Declared after the pipeline test so it observes that run: node:test
  // executes top-level tests in declaration order.
  assert.equal(networkAttempts, 0, "the bundled pipeline attempted a network call");
});
