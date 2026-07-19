import assert from "node:assert/strict";
import { test } from "node:test";

import {
  analyzeInsert,
  functionCalls,
  isPublishable,
  lintScript,
  parseHostManifest,
  scanNamedParameters,
  tokenizeSql,
  UNKNOWN_ARGS,
  type BindingValue,
  type LintFinding,
  type ManifestMethod,
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

// -- positional parameters ---------------------------------------------------
// Protocol v1 is named-parameters-only (docs/script-envelope.md).
// Without this lint "SELECT ?" passes the named-only binding checks and
// SQLite's grammar (prepare succeeds), then fails adapter-dependently at
// runtime — so the v1-forbidden construct must block publish here.

function bareStatementScript(sql: string): Record<string, unknown> {
  return {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    steps: [{ id: "s1", statements: [{ sql, bindings: {} }] }],
  };
}

test("positional-parameter: '?' is an error that blocks publish", () => {
  const findings = lintScript(bareStatementScript("SELECT ?"), manifest);
  const positional = findings.filter((f) => f.code === "positional-parameter");
  assert.equal(positional.length, 1, JSON.stringify(findings));
  assert.equal(positional[0].severity, "error");
  assert.equal(positional[0].stepId, "s1");
  assert.equal(positional[0].statementIndex, 0);
  assert.ok(!isPublishable(findings));
});

test("positional-parameter: '?N' placeholders and repeats flag once per statement", () => {
  // The scanner splits '?1' into '?' + '1' — the rule is about the
  // placeholder, not the digit — and dedupes within the statement.
  const findings = lintScript(bareStatementScript("SELECT ?1, ?"), manifest);
  assert.equal(codes(findings).filter((c) => c === "positional-parameter").length, 1);
});

test("positional-parameter: '?' in literals and comments is data, not a parameter", () => {
  // The check rides the shared lexical scanner, not a regex.
  const findings = lintScript(bareStatementScript("SELECT 'a?b' -- ?"), manifest);
  assert.deepStrictEqual(findings, [], JSON.stringify(findings));
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

// -- manifest table-name casing ----------------------------------------------
// SQLite resolves table names case-insensitively, so manifest casing
// (e.g. `@hostLibrary({ callTablePrefix: "Call_" })` or a hand-written
// manifest) must never disable publish-blocking checks — parity with
// the Java ValidationEngine's lower()-keyed table maps.

function upperManifest() {
  const base = JSON.parse(readFixture("manifests/sample-host.manifest.json"));
  const mangle = (table: string) =>
    table.replace(/^call_/, "Call_").replace(/^result_/, "Result_");
  return parseHostManifest({
    ...base,
    methods: base.methods.map((method: ManifestMethod) => ({
      ...method,
      callTable: mangle(method.callTable),
      resultTable: mangle(method.resultTable),
      input: {
        ...method.input,
        listFields: method.input.listFields.map((field) => ({
          ...field,
          childTable: mangle(field.childTable),
        })),
      },
      result: {
        ...method.result,
        listFields: method.result.listFields.map((field) => ({
          ...field,
          childTable: mangle(field.childTable),
        })),
      },
    })),
  });
}

test("manifest table casing does not hide call-table writes", () => {
  // SQLite executes `INSERT INTO call_get_value` against the host table
  // Call_get_value as a genuine host call, so isPublishable must not
  // approve a payload the Java validator rejects.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: [],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO call_get_value (call_id, input_key) VALUES ('c-1', 'k')" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, upperManifest());
  assert.ok(codes(findings).includes("undeclared-method-use"), JSON.stringify(findings));
});

test("case-differing required-method use is not reported unused", () => {
  // A false unused-required-method warning would push authors to delete
  // a genuinely required declaration.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValue"],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO call_get_value (call_id, input_key) VALUES ('c-1', 'k')" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, upperManifest());
  assert.deepStrictEqual(findings, [], JSON.stringify(findings));
});

test("manifest table casing does not disable result-read lineage", () => {
  // Results only exist after the emitting step's drain — the ordering
  // guarantee must survive manifest casing.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValue"],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO call_get_value (call_id, input_key) VALUES ('r-1', 'k')" },
          { sql: "SELECT result_value FROM result_get_value WHERE call_id = 'r-1'" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, upperManifest());
  assert.ok(codes(findings).includes("result-read-not-after-call"), JSON.stringify(findings));
});

test("manifest table casing does not disable duplicate-call-id", () => {
  // Two emits of one call id are one queue collision no matter how the
  // manifest spells the table.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValue"],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO call_get_value (call_id, input_key) VALUES ('d-1', 'a')" },
          { sql: "INSERT INTO call_get_value (call_id, input_key) VALUES ('d-1', 'b')" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, upperManifest());
  assert.ok(codes(findings).includes("duplicate-call-id"), JSON.stringify(findings));
});

// -- inline functions ----------------------------------------------------------
// Inline function lint (docs/validation.md — feature inlineFunctions):
// unknown-function keys on the manifest's functionPrefix, arity is
// checked statically against minArgs..maxArgs, and an invoked inline
// function both requires the feature declaration and exempts its
// method from unused-required-method. The fixture matrix runs in
// lint-conformance.test.ts; these tests pin what it cannot express
// (arity edges, builtin calls, custom prefixes, case-insensitivity) —
// mirroring the Java InlineFunctionLintTest.

function inlineScript(features: string[], methods: string[], sql: string) {
  return {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredFeatures: features,
    requiredMethods: methods,
    steps: [{ id: "s", statements: [{ sql }] }],
  };
}

test("declared inline call is accepted with zero findings", () => {
  const findings = lintScript(
    inlineScript(["inlineFunctions"], ["getValue"], "SELECT fn_get_value('k')"),
    manifest,
  );
  assert.deepStrictEqual(findings, [], JSON.stringify(findings));
});

test("example-010 inline payload produces zero findings", () => {
  const payload = JSON.parse(readFixture("payloads/valid/example-010-inline.json"));
  const findings = lintScript(payload, manifest);
  assert.deepStrictEqual(findings, [], JSON.stringify(findings));
});

test("undeclared-feature-use: inline call without the feature, reported once per statement", () => {
  const findings = lintScript(
    inlineScript([], ["getValue"], "SELECT fn_get_value('a'), fn_get_value('b')"),
    manifest,
  );
  const undeclared = findings.filter((f) => f.code === "undeclared-feature-use");
  assert.equal(undeclared.length, 1, JSON.stringify(findings));
  assert.equal(undeclared[0].severity, "error");
  assert.equal(undeclared[0].stepId, "s");
  assert.equal(undeclared[0].statementIndex, 0);
});

test("unknown-function: prefix-matching identifier not in the manifest", () => {
  const findings = lintScript(
    inlineScript(["inlineFunctions"], [], "SELECT fn_get_price('k')"),
    manifest,
  );
  assert.ok(codes(findings).includes("unknown-function"), JSON.stringify(findings));
});

test("non-prefixed builtins are SQLite's business, not the lint's", () => {
  const findings = lintScript(
    inlineScript([], [], "SELECT max(1, 2), abs(-1), coalesce(NULL, 0)"),
    manifest,
  );
  assert.deepStrictEqual(findings, [], JSON.stringify(findings));
});

test("function-arity-mismatch: too many and too few arguments", () => {
  const tooMany = lintScript(
    inlineScript(["inlineFunctions"], ["getValue"], "SELECT fn_get_value('k', 'extra')"),
    manifest,
  );
  assert.ok(codes(tooMany).includes("function-arity-mismatch"), JSON.stringify(tooMany));
  const tooFew = lintScript(
    inlineScript(["inlineFunctions"], ["getValue"], "SELECT fn_get_value()"),
    manifest,
  );
  assert.ok(codes(tooFew).includes("function-arity-mismatch"), JSON.stringify(tooFew));
});

test("arity counts top-level commas only: nested parens and strings with commas", () => {
  // max(1, 2) nests inside the argument; the literal carries a comma —
  // both are one top-level argument, so no arity mismatch (and the
  // nested max(...) is extracted as its own non-prefixed call).
  const nested = lintScript(
    inlineScript(["inlineFunctions"], ["getValue"], "SELECT fn_get_value(max(1, 2))"),
    manifest,
  );
  assert.deepStrictEqual(nested, [], JSON.stringify(nested));
  const commaInString = lintScript(
    inlineScript(["inlineFunctions"], ["getValue"], "SELECT fn_get_value('a,b')"),
    manifest,
  );
  assert.deepStrictEqual(commaInString, [], JSON.stringify(commaInString));
});

test("function names match case-insensitively", () => {
  // SQL identifiers are case-insensitive: FN_GET_VALUE is the
  // manifest's fn_get_value, not an unknown function.
  const findings = lintScript(
    inlineScript(["inlineFunctions"], ["getValue"], "SELECT FN_GET_VALUE('k')"),
    manifest,
  );
  assert.deepStrictEqual(findings, [], JSON.stringify(findings));
});

test("inline invocation exempts unused-required-method; no invocation still warns", () => {
  // getValue's call table is never written, but its inline function is
  // invoked — no unused-required-method warning.
  const invoked = lintScript(
    inlineScript(["inlineFunctions"], ["getValue"], "SELECT fn_get_value('k')"),
    manifest,
  );
  assert.ok(!codes(invoked).includes("unused-required-method"), JSON.stringify(invoked));
  const notInvoked = lintScript(
    inlineScript(["inlineFunctions"], ["getValue"], "SELECT 1"),
    manifest,
  );
  assert.ok(codes(notInvoked).includes("unused-required-method"), JSON.stringify(notInvoked));
});

test("custom functionPrefix drives the matching", () => {
  // A host with functionPrefix 'udf_': 'fn_*' identifiers are no
  // longer special, and unknown 'udf_*' identifiers are flagged.
  const base = JSON.parse(readFixture("manifests/sample-host.manifest.json"));
  const custom = parseHostManifest({
    ...base,
    naming: { ...base.naming, functionPrefix: "udf_" },
    methods: base.methods.map((method: { methodName: string; inline: object }) =>
      method.methodName === "getValue"
        ? { ...method, inline: { ...method.inline, functionName: "udf_get_value" } }
        : method,
    ),
  });
  const known = lintScript(
    inlineScript(["inlineFunctions"], ["getValue"], "SELECT udf_get_value('k')"),
    custom,
  );
  assert.deepStrictEqual(known, [], JSON.stringify(known));
  const unknownUdf = lintScript(
    inlineScript(["inlineFunctions"], [], "SELECT udf_bogus('k')"),
    custom,
  );
  assert.ok(codes(unknownUdf).includes("unknown-function"), JSON.stringify(unknownUdf));
  // 'fn_get_value' does not carry this host's prefix — no
  // unknown-function; prepare-only validation is what fails it.
  const fnIsNotSpecial = lintScript(
    inlineScript(["inlineFunctions"], [], "SELECT fn_get_value('k')"),
    custom,
  );
  assert.ok(!codes(fnIsNotSpecial).includes("unknown-function"), JSON.stringify(fnIsNotSpecial));
});

test("functionCalls: identifier-then-paren extraction with top-level arg counts", () => {
  const tokens = tokenizeSql(
    "SELECT fn_get_value(max(1, 2), 'a,b'), count(*), 'lit(', t.col FROM t WHERE fn_open('x'",
  );
  assert.deepStrictEqual(functionCalls(tokens), [
    { name: "fn_get_value", argCount: 2 },
    { name: "max", argCount: 2 },
    { name: "count", argCount: 1 },
    // ')' never closes: arity is unknowable — UNKNOWN_ARGS, skipped by lint.
    { name: "fn_open", argCount: UNKNOWN_ARGS },
  ]);
  assert.deepStrictEqual(functionCalls(tokenizeSql("SELECT fn_noargs()")), [
    { name: "fn_noargs", argCount: 0 },
  ]);
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

// -- bracket / backtick quoted identifiers -----------------------------------
// SQLite accepts [id] (MS Access/SQL Server compat) and `id` (MySQL
// compat) as identifiers. The shared tokenizer must lex them as the same
// quoted-identifier kind as "id", so analyzeInsert/lineage/call-id checks
// fire on quoted-form statements instead of being silently skipped
// (docs/errors.md pins the shared scanner; the Java tokenizer mirrors it).

test("tokenizer: bracket identifier is one quoted-identifier token, ends at first ]", () => {
  assert.deepStrictEqual(tokenizeSql("[call_get_value]"), [
    { kind: "quoted-identifier", value: "call_get_value" },
  ]);
  // No escape mechanism: the identifier ends at the FIRST ']'.
  const tokens = tokenizeSql("[weird ]x]");
  assert.deepStrictEqual(tokens[0], { kind: "quoted-identifier", value: "weird " });
});

test("tokenizer: backtick identifier unescapes doubled backticks", () => {
  assert.deepStrictEqual(tokenizeSql("`call_get_value`"), [
    { kind: "quoted-identifier", value: "call_get_value" },
  ]);
  // `` inside a backtick identifier is one literal backtick.
  assert.deepStrictEqual(tokenizeSql("`a``b`"), [
    { kind: "quoted-identifier", value: "a`b" },
  ]);
});

test("analyzeInsert recognizes bracket- and backtick-quoted target tables", () => {
  // WHY: a quoted target table must resolve to the same table name so
  // the host-call checks (undeclared-method-use, duplicate-call-id,
  // lineage) are not silently bypassed by the quoting form.
  const bindings: Record<string, BindingValue> = { c: { type: "text", value: "c-1" } };
  const bracket = analyzeInsert(
    tokenizeSql("INSERT INTO [call_get_value] (call_id) VALUES (:c)"),
    bindings,
    "call_id",
  );
  assert.equal(bracket?.table, "call_get_value");
  assert.equal(bracket?.rows.length, 1);
  assert.equal(bracket?.rows[0].callId, "c-1");
  const backtick = analyzeInsert(
    tokenizeSql("INSERT INTO `call_get_value` (call_id) VALUES (:c)"),
    bindings,
    "call_id",
  );
  assert.equal(backtick?.table, "call_get_value");
});

test("bracket-quoted call table still lints as a call-table insert", () => {
  // Recognized as the getValue call table → undeclared-method-use fires
  // (the quoting must not disable the host-call check).
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: [],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO [call_get_value] (call_id, input_key) VALUES ('c-1', 'k')" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, manifest);
  assert.ok(codes(findings).includes("undeclared-method-use"), JSON.stringify(findings));
  assert.ok(!codes(findings).includes("implicit-column-list"), JSON.stringify(findings));
});

// -- INSERT ... AS <alias> (explicit column list) ----------------------------
// SQLite >= 3.24.0 accepts `INSERT INTO t AS c (cols) …` (UPSERT syntax).
// The alias must be skipped so the explicit column list is still parsed —
// otherwise a valid aliased INSERT is wrongly reported implicit-column-list
// and its call id is dropped, defeating duplicate/lineage resolution.

test("analyzeInsert skips an AS alias before the column list", () => {
  const info = analyzeInsert(
    tokenizeSql("INSERT INTO call_get_value AS c (call_id, input_key) VALUES ('c-1', 'k')"),
    {},
    "call_id",
  );
  assert.equal(info?.table, "call_get_value");
  assert.deepStrictEqual(info?.columns, ["call_id", "input_key"]);
  assert.equal(info?.rows[0].callId, "c-1");
  // schema-qualified target + alias resolves to the same table name.
  const qualified = analyzeInsert(
    tokenizeSql("INSERT INTO main.call_get_value AS c (call_id, input_key) VALUES ('c-1', 'k')"),
    {},
    "call_id",
  );
  assert.equal(qualified?.table, "call_get_value");
  assert.deepStrictEqual(qualified?.columns, ["call_id", "input_key"]);
});

test("aliased INSERT keeps its explicit column list (no implicit-column-list)", () => {
  // WHY: the column list WAS explicit — the alias must not fabricate an
  // implicit-column-list error that wrongly blocks publish.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValue"],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO call_get_value AS c (call_id, input_key) VALUES ('c-1', 'k')" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, manifest);
  assert.ok(!codes(findings).includes("implicit-column-list"), JSON.stringify(findings));
  assert.deepStrictEqual(findings, [], JSON.stringify(findings));
});

test("aliased INSERTs still resolve call ids (duplicate-call-id survives)", () => {
  // WHY: rows/call-ids must not be lost to the alias — two aliased
  // inserts of the same id must still collide.
  const stmt = {
    sql: "INSERT INTO call_get_value AS c (call_id, input_key) VALUES ('dup-1', 'k')",
  };
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValue"],
    steps: [{ id: "s1", statements: [stmt, stmt] }],
  };
  const findings = lintScript(payload, manifest);
  assert.ok(codes(findings).includes("duplicate-call-id"), JSON.stringify(findings));
});

// -- method-api-level-too-high -----------------------------------------------
// A method's apiLevel is the API level a script that uses it depends on.
// requiredApiLevel only bounds the library apiLevel, so a script can list
// a level-2 method (or invoke its inline function) under requiredApiLevel
// 1 — understating what it needs. On an older level-1 host the call table
// or inline function is absent; the clean-skip contract (requiredApiLevel)
// is what should protect against that, so under-declaring it is an error.

function level2Manifest() {
  const base = JSON.parse(readFixture("manifests/sample-host.manifest.json"));
  return parseHostManifest({
    ...base,
    library: { ...base.library, apiLevel: 2 },
    methods: base.methods.map((method: ManifestMethod) =>
      method.methodName === "getValue" ? { ...method, apiLevel: 2 } : method,
    ),
  });
}

test("method-api-level-too-high: call-table insert of a higher-apiLevel method", () => {
  // WHY: the script under-declares the API level it depends on, so an
  // older host would fail to clean-skip instead of running correctly.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValue"],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO call_get_value (call_id, input_key) VALUES ('c-1', 'k')" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, level2Manifest());
  assert.ok(codes(findings).includes("method-api-level-too-high"), JSON.stringify(findings));
  assert.ok(!isPublishable(findings));
});

test("method-api-level-too-high: inline invocation of a higher-apiLevel method", () => {
  // WHY: inline invocation is not gated by requiredMethods, so a level-1
  // host that supports inlineFunctions but lacks this method would raise
  // "no such function" at runtime instead of clean-skipping.
  const payload = inlineScript(["inlineFunctions"], [], "SELECT fn_get_value('k')");
  const findings = lintScript(payload, level2Manifest());
  assert.ok(codes(findings).includes("method-api-level-too-high"), JSON.stringify(findings));
});

test("method-api-level-too-high: silent when requiredApiLevel covers the method", () => {
  // WHY: the check keys on the apiLevel relationship, not mere method
  // presence — it must stay silent when the script declares enough, so
  // it cannot pass vacuously when business logic changes.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 2,
    requiredMethods: ["getValue"],
    steps: [
      {
        id: "s1",
        statements: [
          { sql: "INSERT INTO call_get_value (call_id, input_key) VALUES ('c-1', 'k')" },
        ],
      },
    ],
  };
  const findings = lintScript(payload, level2Manifest());
  assert.ok(!codes(findings).includes("method-api-level-too-high"), JSON.stringify(findings));
  assert.deepStrictEqual(findings, [], JSON.stringify(findings));
});

// -- binding-type-mismatch ---------------------------------------------------
// A parameter feeding a known call-table column must be wire-compatible
// with that column's scalar type, over the shared ir.ts BINDING_TYPE_COMPAT
// table (docs/proposals/rule-parameters-as-data.md). Until this landed the
// matrix lived only in the Java engine, so the TS lint approved payloads
// Java rejected. The fixture matrix pins blob→int64 and float64→int64; these
// pin what it cannot express — the widening/NULL/child-table/float edges.
// setValue's input_value is int64, so it drives most cases.

function setValueWrite(value: BindingValue) {
  return {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["setValue"],
    steps: [
      {
        id: "s1",
        statements: [
          {
            sql: "INSERT INTO call_set_value (call_id, input_key, input_value) VALUES (:callId, :key, :value)",
            bindings: {
              callId: { type: "text", value: "c-1" },
              key: { type: "text", value: "k" },
              value,
            },
          },
        ],
      },
    ],
  };
}

test("binding-type-mismatch: int32 widens into an int64 column (no over-fire)", () => {
  // WHY: the matrix accepts int32 into an int64 column — the check must
  // not flag a legal widening, or it would block valid payloads.
  const findings = lintScript(setValueWrite({ type: "int32", value: 1 }), manifest);
  assert.deepStrictEqual(findings, [], JSON.stringify(findings));
});

test("binding-type-mismatch: integers never coerce into a float column", () => {
  // WHY: recordScore's input_score is float64, which accepts only
  // float32/float64 — an int64 there is a real mismatch (Java rejects it),
  // so the TS lint must too, or the two validators disagree.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["recordScore"],
    steps: [
      {
        id: "s1",
        statements: [
          {
            sql: "INSERT INTO call_record_score (call_id, input_key, input_score) VALUES (:callId, :key, :score)",
            bindings: {
              callId: { type: "text", value: "c-1" },
              key: { type: "text", value: "k" },
              score: { type: "int64", value: 7 },
            },
          },
        ],
      },
    ],
  };
  const mismatch = lintScript(payload, manifest).filter((f) => f.code === "binding-type-mismatch");
  assert.equal(mismatch.length, 1, JSON.stringify(mismatch));
  assert.equal(mismatch[0].severity, "error");
});

test("binding-type-mismatch: a null binding is accepted only for an optional column", () => {
  // WHY: the NULL→optional rule is orthogonal to the type matrix — a null
  // binding stands in for any type but only where the column permits it.
  // input_value is required, so null is a mismatch…
  const required = lintScript(setValueWrite({ type: "null" }), manifest);
  assert.ok(codes(required).includes("binding-type-mismatch"), JSON.stringify(required));
  // …while getValues' input_default_value is optional, so null is fine.
  const optional = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValues"],
    steps: [
      {
        id: "s1",
        statements: [
          {
            sql: "INSERT INTO call_get_values (call_id, input_default_value) VALUES (:callId, :dv)",
            bindings: {
              callId: { type: "text", value: "c-1" },
              dv: { type: "null" },
            },
          },
        ],
      },
    ],
  };
  assert.ok(
    !codes(lintScript(optional, manifest)).includes("binding-type-mismatch"),
    JSON.stringify(lintScript(optional, manifest)),
  );
});

test("binding-type-mismatch: the check covers input list child tables", () => {
  // WHY: child-table columns are writable too — the shared matrix must
  // guard them, not just top-level call tables. input_key is a string
  // column, so an int64 binding there is a mismatch.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredMethods: ["getValues"],
    steps: [
      {
        id: "s1",
        statements: [
          {
            sql: "INSERT INTO call_get_values (call_id, input_default_value) VALUES ('q-1', 0)",
          },
          {
            sql: "INSERT INTO call_get_values__input_keys (call_id, item_index, input_key) VALUES ('q-1', 0, :k)",
            bindings: { k: { type: "int64", value: 5 } },
          },
        ],
      },
    ],
  };
  const mismatch = lintScript(payload, manifest).filter((f) => f.code === "binding-type-mismatch");
  assert.equal(mismatch.length, 1, JSON.stringify(mismatch));
  assert.equal(mismatch[0].statementIndex, 1);
});
