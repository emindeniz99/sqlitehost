import assert from "node:assert/strict";
import { test } from "node:test";

import {
  lintScript,
  parseHostManifest,
  scanNamedParameters,
  type LintFinding,
} from "../index.js";
import { readFixture } from "./helpers.js";

const manifest = parseHostManifest(readFixture("manifests/sample-host.manifest.json"));

function codes(findings: LintFinding[]): string[] {
  return findings.map((f) => f.code);
}

test("scanner skips parameters in literals, quoted identifiers, and comments", () => {
  const sql = [
    "SELECT ':notAParam', \":alsoNot\", -- :lineComment",
    "/* :blockComment */ :real, @second, $third, 'it''s :escaped'",
  ].join("\n");
  assert.deepStrictEqual(scanNamedParameters(sql), ["real", "second", "third"]);
});

test("scanner deduplicates repeated parameters", () => {
  assert.deepStrictEqual(
    scanNamedParameters("INSERT INTO t (a, b) VALUES (:x, 0), (:x, 1)"),
    ["x"],
  );
});

test("list-child-without-parent: child rows with no parent insert anywhere", () => {
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValues"],
    steps: [
      {
        id: "children-only",
        statements: [
          {
            sql: "INSERT INTO call_get_values__input_keys (call_id, item_index, input_key) VALUES (:callId, 0, 'alpha')",
            bindings: { callId: { type: "text", value: "list-1" } },
          },
        ],
      },
    ],
  };
  const findings = lintScript(payload, manifest);
  assert.ok(codes(findings).includes("list-child-without-parent"));
});

test("child rows colocated with the parent produce no list findings", () => {
  const payload = JSON.parse(readFixture("payloads/valid/example-002-list-roundtrip.json"));
  const findings = lintScript(payload, manifest);
  assert.ok(!codes(findings).includes("list-child-later-step"));
  assert.ok(!codes(findings).includes("list-child-without-parent"));
});

test("computed call_id expressions are skipped by static resolution", () => {
  // Same statement twice with a computed call_id: not statically
  // resolvable, so no duplicate-call-id claim (documented best-effort).
  const statement = {
    sql: "INSERT INTO call_set_value (call_id, input_key, input_value) SELECT 'w-' || result_key, result_key, 1 FROM result_get_values__result_entries WHERE call_id = 'x'",
  };
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["setValue"],
    steps: [{ id: "s1", statements: [statement, statement] }],
  };
  const findings = lintScript(payload, manifest);
  assert.ok(!codes(findings).includes("duplicate-call-id"));
});

test("invalid-envelope payloads short-circuit semantic checks", () => {
  const findings = lintScript({ engine: "sqlite-host-v1" }, manifest);
  assert.ok(codes(findings).includes("invalid-envelope"));
  assert.ok(findings.every((f) => f.severity === "error"));
});

test("findings carry step and statement locations", () => {
  const payload = JSON.parse(readFixture("payloads/invalid/missing-binding.json"));
  const finding = lintScript(payload, manifest).find((f) => f.code === "missing-binding");
  assert.equal(finding?.stepId, "read");
  assert.equal(finding?.statementIndex, 0);
});
