import assert from "node:assert/strict";
import { test } from "node:test";

import { isPublishable, lintScript, parseHostManifest, type LintFinding } from "../index.js";
import { readFixture } from "./helpers.js";

/**
 * Engine-portability lints (docs/validation.md —
 * sqlite-version-too-low-for-function and nonportable-function), the
 * TypeScript mirror of EnginePortabilityLintTest.java.
 *
 * Why they matter here in particular: the TypeScript validator has no SQLite
 * at all, so it can never gain the prepare-only layer. These two lints are
 * the *only* protection an authoring-time TS check has against a script that
 * uses SQL newer than the devices the host promises to run on — and the
 * promise is data the manifest has always carried
 * (`library.minSqliteVersionNumber`) but that no validator read before.
 */

const manifest = parseHostManifest(readFixture("manifests/sample-host.manifest.json"));

/** Findings of one code for a single-statement script. */
function findings(sql: string, code: string): LintFinding[] {
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredFeatures: [],
    requiredMethods: [],
    steps: [{ id: "s", statements: [{ sql, bindings: {} }] }],
  };
  return lintScript(payload, manifest).filter((f) => f.code === code);
}

const versionFindings = (sql: string): LintFinding[] =>
  findings(sql, "sqlite-version-too-low-for-function");
const portabilityFindings = (sql: string): LintFinding[] =>
  findings(sql, "nonportable-function");

test("builtins added after the host floor are errors", () => {
  // The sample host declares the plan's default floor, 3.19.3. Each of these
  // shipped later, so each is a device-side crash waiting to happen:
  // row_number 3.25.0, iif 3.32.0, format/unixepoch 3.38.0,
  // octet_length 3.43.0, concat/string_agg 3.44.0.
  for (const sql of [
    "SELECT iif(1, 2, 3)",
    "SELECT format('%d', 1)",
    "SELECT unixepoch()",
    "SELECT octet_length('a')",
    "SELECT concat('a', 'b')",
    "SELECT string_agg(k, ',')",
    "SELECT row_number() OVER ()",
  ]) {
    const found = versionFindings(sql);
    assert.equal(found.length, 1, `${sql}: ${JSON.stringify(found)}`);
    assert.equal(found[0].severity, "error", sql);
  }
});

test("the message names both versions so the fix is obvious", () => {
  // An author who only learns "this is too new" cannot act. Naming the
  // required version and the host's floor makes the two possible fixes
  // (raise the floor / drop the function) decidable without a lookup.
  const message = versionFindings("SELECT iif(1, 2, 3)")[0].message;
  assert.ok(message.includes("3.32.0"), message);
  assert.ok(message.includes("3.19.3"), message);
});

test("builtins at or below the floor stay silent", () => {
  // The floor is a promise that these work everywhere, so flagging them
  // would be a false positive that trains authors to ignore the lint.
  // printf is the sharp case: format() is its post-3.38 rename, but printf
  // itself is 3.8.3 and must stay legal.
  for (const sql of [
    "SELECT printf('%d', 1)",
    "SELECT abs(-1)",
    "SELECT substr('abc', 1, 2)",
    "SELECT ltrim('  a')",
    "SELECT rtrim('a  ')",
    "SELECT trim(' a ')",
    "SELECT instr('ab', 'b')",
    "SELECT group_concat(k, ',')",
    "SELECT coalesce(a, b)",
  ]) {
    assert.deepStrictEqual(versionFindings(sql), [], sql);
    assert.deepStrictEqual(portabilityFindings(sql), [], sql);
  }
});

test("the json family resolves by longest prefix", () => {
  // json_* is treated as 3.38.0 because that is the first release where it is
  // a built-in rather than a compile-gated extension; jsonb_* is 3.45.0.
  // Longest-prefix resolution is what keeps jsonb_extract from being
  // under-reported as the older, weaker json floor.
  assert.ok(versionFindings("SELECT json_extract(d, '$.a')")[0].message.includes("3.38.0"));
  assert.ok(versionFindings("SELECT jsonb_extract(d, '$.a')")[0].message.includes("3.45.0"));
});

test("compile-gated builtins are reported separately from version gaps", () => {
  // Math functions arrived in 3.35.0 but are only present when the engine was
  // built with -DSQLITE_ENABLE_MATH_FUNCTIONS. Reporting them as a version
  // problem would point the author at raising minSqliteVersion, which does not
  // fix anything — hence a distinct code, and no version finding alongside it.
  for (const sql of [
    "SELECT sqrt(2)",
    "SELECT pow(2, 8)",
    "SELECT ceil(1.5)",
    "SELECT log10(100)",
    "SELECT PI()",
  ]) {
    const found = portabilityFindings(sql);
    assert.equal(found.length, 1, `${sql}: ${JSON.stringify(found)}`);
    assert.equal(found[0].severity, "error", sql);
    assert.ok(found[0].message.includes("SQLITE_ENABLE_MATH_FUNCTIONS"), found[0].message);
    assert.deepStrictEqual(versionFindings(sql), [], sql);
  }
});

test("host inline functions are never judged against the engine", () => {
  // An inline function is registered by the host adapter through
  // sqlite3_create_function, so neither the engine's version nor its compile
  // options decide whether it exists.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredFeatures: ["inlineFunctions"],
    requiredMethods: [],
    steps: [{ id: "s", statements: [{ sql: "SELECT fn_get_value('k')", bindings: {} }] }],
  };
  const found = lintScript(payload, manifest);
  assert.ok(
    !found.some(
      (f) => f.code === "sqlite-version-too-low-for-function" || f.code === "nonportable-function",
    ),
    JSON.stringify(found),
  );
});

test("only call syntax counts and the report is deduplicated", () => {
  // A bare identifier is a column reference, not a call: `ORDER BY rank` is
  // ordinary SQL on any engine and flagging it would make the lint unusable.
  // A string literal spelling a call is collapsed by the tokenizer.
  assert.deepStrictEqual(versionFindings("SELECT rank FROM t ORDER BY rank"), []);
  assert.deepStrictEqual(versionFindings("SELECT 'iif(1,2,3)' AS label"), []);
  // Repeats of one name in a statement collapse to a single finding.
  assert.equal(versionFindings("SELECT iif(1, 2, 3), iif(4, 5, 6)").length, 1);
});

test("these findings block publishing", () => {
  // Severity is pinned as error: docs/validation.md makes a payload
  // publishable on zero errors, and shipping either of these means a hard SQL
  // failure on some fraction of devices.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredFeatures: [],
    requiredMethods: [],
    steps: [{ id: "s", statements: [{ sql: "SELECT iif(1, sqrt(4), 3)", bindings: {} }] }],
  };
  assert.ok(!isPublishable(lintScript(payload, manifest)));
});
