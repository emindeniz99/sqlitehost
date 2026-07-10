import assert from "node:assert/strict";
import { test } from "node:test";

import {
  lintScript,
  parseHostManifest,
  scanNamedParameters,
  tokenizeSql,
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

test("INSERT OR REPLACE INTO a call table still lints as a call-table insert", () => {
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: [],
    steps: [
      {
        id: "s1",
        statements: [
          {
            sql: "INSERT OR REPLACE INTO call_get_value (call_id, input_key) VALUES ('c-1', 'k')",
          },
        ],
      },
    ],
  };
  const findings = lintScript(payload, manifest);
  // Recognized as an insert into the getValue call table…
  assert.ok(codes(findings).includes("undeclared-method-use"));
  // …with the explicit column list parsed (OR REPLACE must not shift it).
  assert.ok(!codes(findings).includes("implicit-column-list"));
});

test("script_vars inserts (plain and OR REPLACE) produce no findings", () => {
  // script_vars is not a call table: writing variables — including via
  // INSERT OR REPLACE — is plain workspace SQL, not a host-call emit.
  const payload = JSON.parse(readFixture("payloads/valid/example-008-variables.json"));
  const findings = lintScript(payload, manifest);
  assert.deepStrictEqual(findings, []);
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

function singleStatementScript(sql: string): Record<string, unknown> {
  return {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    steps: [
      {
        id: "s1",
        statements: [{ sql, bindings: { v: { type: "int64", value: 1 } } }],
      },
    ],
  };
}

test("mixed-prefix-binding: :v and $v in one statement warns once", () => {
  const findings = lintScript(
    singleStatementScript("UPDATE t SET a = :v WHERE b = $v"),
    manifest,
  );
  const mixed = findings.filter((f) => f.code === "mixed-prefix-binding");
  assert.equal(mixed.length, 1);
  assert.equal(mixed[0].severity, "warning");
  assert.equal(mixed[0].stepId, "s1");
  assert.equal(mixed[0].statementIndex, 0);
  // The binding still feeds both forms by bare name: no binding errors.
  assert.deepStrictEqual(
    findings.filter((f) => f.severity === "error"),
    [],
    `expected zero errors, got ${JSON.stringify(findings)}`,
  );
});

test("mixed-prefix-binding: the same prefix twice is silent", () => {
  const findings = lintScript(
    singleStatementScript("UPDATE t SET a = :v WHERE b = :v"),
    manifest,
  );
  assert.ok(!codes(findings).includes("mixed-prefix-binding"));
});

test("mixed-prefix-binding: @v and $v in one statement warns", () => {
  const findings = lintScript(
    singleStatementScript("UPDATE t SET a = @v WHERE b = $v"),
    manifest,
  );
  assert.equal(codes(findings).filter((c) => c === "mixed-prefix-binding").length, 1);
});

test("scanner retains the prefix of each parameter occurrence", () => {
  // Regression: prefix info is additive — bare-name matching for
  // missing-binding/unused-binding and lineage must be unchanged.
  const tokens = tokenizeSql("SELECT :v, $v, @w").filter((t) => t.kind === "parameter");
  assert.deepStrictEqual(
    tokens.map((t) => [t.prefix, t.value]),
    [
      [":", "v"],
      ["$", "v"],
      ["@", "w"],
    ],
  );
  assert.deepStrictEqual(scanNamedParameters("SELECT :v, $v, @w"), ["v", "w"]);
});

// -- custom column names -----------------------------------------------------
// The call-id column is host-configurable (manifest columns block,
// docs/naming.md): the lint must resolve call ids through the
// manifest's name, so renaming it re-targets every call-id check.

function cidManifest() {
  const base = JSON.parse(readFixture("manifests/sample-host.manifest.json"));
  return parseHostManifest({
    ...base,
    columns: { ...base.columns, callId: "cid" },
  });
}

test("custom call-id column: lineage resolves via cid comparisons", () => {
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValue"],
    steps: [
      {
        id: "emit",
        statements: [
          { sql: "INSERT INTO call_get_value (cid, input_key) VALUES ('r-1', 'k')" },
        ],
      },
      {
        id: "read",
        statements: [
          { sql: "SELECT result_value FROM result_get_value WHERE cid = 'r-1' AND status = 'done'" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, cidManifest());
  assert.deepStrictEqual(findings, [], `expected no findings, got ${JSON.stringify(findings)}`);
});

test("custom call-id column: call_id comparisons are no longer call-id filters", () => {
  // Under the cid manifest, `cid = …` is a call-id filter (the same-step
  // read errors) while `call_id = …` is an ordinary column comparison
  // (unresolvable — skipped, no finding).
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValue"],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO call_get_value (cid, input_key) VALUES ('r-1', 'k')" },
          { sql: "SELECT result_value FROM result_get_value WHERE cid = 'r-1'" },
          { sql: "SELECT result_value FROM result_get_value WHERE call_id = 'r-1'" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, cidManifest());
  const lineage = findings.filter((f) => f.code === "result-read-not-after-call");
  assert.equal(lineage.length, 1, JSON.stringify(findings));
  assert.equal(lineage[0].statementIndex, 1);
});

test("custom call-id column: duplicate-call-id resolves via cid, not call_id", () => {
  const statements = (column: string) => [
    {
      sql: `INSERT INTO call_get_value (${column}, input_key) VALUES ('r-1', 'a'), ('r-1', 'b')`,
    },
  ];
  const script = (column: string) => ({
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValue"],
    steps: [{ id: "s1", statements: statements(column) }],
  });
  // cid is the call-id column: the repeated id is caught…
  const viaCid = lintScript(script("cid"), cidManifest());
  assert.ok(codes(viaCid).includes("duplicate-call-id"), JSON.stringify(viaCid));
  // …while a column literally named call_id resolves nothing.
  const viaCallId = lintScript(script("call_id"), cidManifest());
  assert.ok(!codes(viaCallId).includes("duplicate-call-id"), JSON.stringify(viaCallId));
});

test("custom call-id column: list child/parent matching uses cid", () => {
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValues"],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO call_get_values (cid, input_default_value) VALUES ('q-1', 0)" },
        ],
      },
      {
        id: "s2",
        statements: [
          {
            sql: "INSERT INTO call_get_values__input_keys (cid, item_index, input_key) VALUES ('q-1', 0, 'k')",
          },
        ],
      },
    ],
  };
  // Parent and child ids only match through the cid column — the
  // colocation rule must still see them and flag the later step.
  const findings = lintScript(payload, cidManifest());
  assert.ok(codes(findings).includes("list-child-later-step"), JSON.stringify(findings));
});

test("duplicate-input-name: two inputs sharing a name is an error", () => {
  const payload = JSON.parse(readFixture("payloads/invalid/duplicate-input-name.json"));
  const findings = lintScript(payload, manifest);
  const duplicate = findings.find((f) => f.code === "duplicate-input-name");
  assert.equal(duplicate?.severity, "error");
});

test("unique input names produce no duplicate-input-name finding", () => {
  const payload = JSON.parse(readFixture("payloads/valid/example-003-runtime-inputs.json"));
  const findings = lintScript(payload, manifest);
  assert.ok(!codes(findings).includes("duplicate-input-name"));
});

test("findings carry step and statement locations", () => {
  const payload = JSON.parse(readFixture("payloads/invalid/missing-binding.json"));
  const finding = lintScript(payload, manifest).find((f) => f.code === "missing-binding");
  assert.equal(finding?.stepId, "read");
  assert.equal(finding?.statementIndex, 0);
});
