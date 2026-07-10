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

test("'$' continues an identifier run instead of starting a parameter", () => {
  // Pinned by docs/errors.md: a '$' immediately preceded by an
  // identifier character continues that identifier; '$v' at a token
  // boundary is a parameter.
  assert.deepStrictEqual(scanNamedParameters("SELECT a$b FROM t"), []);
  assert.deepStrictEqual(
    scanNamedParameters("SELECT foo$bar, :real FROM t"),
    ["real"],
  );
  assert.deepStrictEqual(scanNamedParameters("SELECT $v"), ["v"]);
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

test("CTE-prefixed INSERT into an undeclared call table is caught", () => {
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: [],
    steps: [
      {
        id: "cte-write",
        statements: [
          {
            sql: "WITH x AS (SELECT 1) INSERT INTO call_set_value (call_id, input_key, input_value) VALUES ('c-1', 'k', 1)",
          },
        ],
      },
    ],
  };
  const findings = lintScript(payload, manifest);
  assert.ok(codes(findings).includes("undeclared-method-use"));
});

test("CTE-prefixed INSERT into a declared call table counts as a use", () => {
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["setValue"],
    steps: [
      {
        id: "cte-write",
        statements: [
          {
            sql: "WITH x AS (SELECT 1) INSERT INTO call_set_value (call_id, input_key, input_value) VALUES ('c-1', 'k', 1)",
          },
        ],
      },
    ],
  };
  const findings = lintScript(payload, manifest);
  assert.ok(!codes(findings).includes("unused-required-method"));
  assert.deepStrictEqual(codes(findings), []);
});

test("list-child-without-parent is skipped when the parent call_id is computed", () => {
  // The parent insert's call_id is a computed expression, so it is not
  // statically resolvable — the child check must not false-positive
  // (mirrors the Java engine's computed-emit guard).
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValues"],
    steps: [
      {
        id: "computed-parent",
        statements: [
          {
            sql: "INSERT INTO call_get_values (call_id, input_default_value) SELECT 'q-' || name, 0 FROM script_inputs",
          },
          {
            sql: "INSERT INTO call_get_values__input_keys (call_id, item_index, input_key) VALUES ('q-x', 0, 'k')",
          },
        ],
      },
    ],
  };
  const findings = lintScript(payload, manifest);
  assert.deepStrictEqual(
    findings.filter((f) => f.severity === "error"),
    [],
    `expected zero errors, got ${JSON.stringify(findings)}`,
  );
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
