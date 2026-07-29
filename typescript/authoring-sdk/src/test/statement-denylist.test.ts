import assert from "node:assert/strict";
import { test } from "node:test";

import { isPublishable, lintScript, parseHostManifest, type LintFinding } from "../index.js";
import { readFixture } from "./helpers.js";

/**
 * Statement denylist (docs/validation.md — forbidden-statement and
 * protocol-table-write), the TypeScript mirror of StatementDenylistTest.java.
 *
 * Why forbidden-statement matters: the runtime's atomicity unit is the step
 * (its statements plus the drain), not a transaction. A script that issues
 * BEGIN and later ROLLBACK erases the drain's result rows and queue updates
 * *after* the host handlers have already run with real-world side effects,
 * and the run still reports success — a silent-data-loss shape. ATTACH is the
 * only filesystem escape a script has. PRAGMA can change semantics under the
 * runtime's feet or, via writable_schema=ON, rewrite the queue triggers.
 * None of these is caught by prepare-only validation: all of them compile.
 *
 * Why protocol-table-write matters: the drain and the result-write policy
 * both assume they are the only writers of the queue and result tables. A
 * script that inserts into a result table forges a result the host never
 * produced; one that deletes from the queue makes calls silently vanish
 * while the run still reports Completed.
 *
 * The negative cases below are the load-bearing half: both codes are errors
 * that block publication, so a false positive is as damaging as a miss.
 */

const manifest = parseHostManifest(readFixture("manifests/sample-host.manifest.json"));

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

const forbidden = (sql: string): LintFinding[] => findings(sql, "forbidden-statement");
const protocolWrite = (sql: string): LintFinding[] => findings(sql, "protocol-table-write");
const multipleStatements = (sql: string): LintFinding[] => findings(sql, "multiple-statements");

function lintOne(sql: string): LintFinding[] {
  return lintScript(
    {
      engine: "sqlite-host-v1",
      requiredApiLevel: 1,
      requiredFeatures: [],
      requiredMethods: [],
      steps: [{ id: "s", statements: [{ sql, bindings: {} }] }],
    },
    manifest,
  );
}

test("every denied leading keyword is an error", () => {
  for (const sql of [
    "BEGIN",
    "BEGIN IMMEDIATE",
    "COMMIT",
    "END",
    "ROLLBACK",
    "SAVEPOINT sp1",
    "RELEASE sp1",
    "ATTACH DATABASE '/tmp/x.db' AS x",
    "DETACH DATABASE x",
    "PRAGMA foreign_keys = ON",
    "VACUUM",
    "ANALYZE",
    "REINDEX",
  ]) {
    const found = forbidden(sql);
    assert.equal(found.length, 1, `${sql}: ${JSON.stringify(found)}`);
    assert.equal(found[0].severity, "error", sql);
  }
});

test("the keyword match is case-insensitive", () => {
  // SQLite keywords are case-insensitive, so a denylist that only saw upper
  // case would be bypassed by typing `pragma` in lower case.
  for (const sql of ["pragma foreign_keys = ON", "Attach DATABASE 'x' AS y", "beGIN"]) {
    assert.equal(forbidden(sql).length, 1, sql);
  }
});

test("only the first token is a keyword", () => {
  // These are the false positives that would make the lint unusable. A table
  // whose name merely starts with a denied word, the pragma_* table-valued
  // functions inside a SELECT (explicitly still legal), a denied word as a
  // column, and a string literal spelling one — none is such a statement.
  for (const sql of [
    "SELECT * FROM pragma_helper",
    "INSERT INTO pragma_helper (a) VALUES (1)",
    "SELECT name FROM pragma_table_info('script_vars')",
    "SELECT t.begin FROM t",
    "SELECT 'PRAGMA writable_schema = ON' AS label",
    "SELECT CASE WHEN a THEN 1 ELSE 2 END FROM t",
    "SELECT analyze_id FROM t",
  ]) {
    assert.deepStrictEqual(forbidden(sql), [], sql);
  }
});

test("comments and CTEs do not hide the leading keyword", () => {
  // The tokenizer already drops comments, so a leading comment cannot be used
  // to push the real first token out of view.
  assert.equal(forbidden("-- harmless\nPRAGMA writable_schema = ON").length, 1);
  assert.equal(forbidden("/* harmless */ ATTACH DATABASE 'x' AS y").length, 1);
  // A CTE prefix is legal SQL and must not be mistaken for a denied
  // statement, even when the CTE body mentions one as a value.
  assert.deepStrictEqual(forbidden("WITH q(v) AS (SELECT 'begin') SELECT v FROM q"), []);
  assert.deepStrictEqual(
    forbidden(
      "WITH q(v) AS (SELECT 1) INSERT INTO script_vars (name, value_type, int_value)" +
        " SELECT 'n', 'int64', v FROM q",
    ),
    [],
  );
});

test("writes to runtime-owned tables are errors", () => {
  // Forging a result, marking a queued call done, and dropping a queued call
  // are the three concrete attacks on the drain protocol.
  for (const sql of [
    "INSERT INTO result_get_value (call_id, status, result_value) VALUES ('x', 'done', 1)",
    "UPDATE pending_host_calls SET status = 'done'",
    "DELETE FROM pending_host_calls",
    "INSERT INTO result_get_values__result_entries (call_id, item_index, result_key," +
      " result_value, result_found) VALUES ('x', 0, 'k', 1, 1)",
    "DELETE FROM script_inputs",
  ]) {
    const found = protocolWrite(sql);
    assert.equal(found.length, 1, `${sql}: ${JSON.stringify(found)}`);
    assert.equal(found[0].severity, "error", sql);
  }
});

test("reading a runtime-owned table stays legal", () => {
  // Reading result tables and script_inputs is the entire point of the
  // protocol — only writes are denied. A scan-anywhere verb match would break
  // exactly this, so it is pinned.
  for (const sql of [
    "SELECT result_value FROM result_get_value WHERE call_id = 'x'",
    "SELECT * FROM pending_host_calls",
    "SELECT int_value FROM script_inputs WHERE name = 'n'",
    "INSERT INTO script_vars (name, value_type, int_value)" +
      " SELECT 'n', 'int64', result_value FROM result_get_value",
  ]) {
    assert.deepStrictEqual(protocolWrite(sql), [], sql);
  }
});

test("script-owned and call tables stay writable", () => {
  // Writing a call table IS how a script makes a host call, and script_vars /
  // script_control are the script's own scratch and control surfaces. Denying
  // any of these would break every existing valid payload.
  for (const sql of [
    "INSERT INTO call_get_value (call_id, input_key) VALUES ('c1', 'k')",
    "INSERT INTO call_get_values__input_keys (call_id, item_index, input_key)" +
      " VALUES ('c1', 0, 'k')",
    "INSERT INTO script_vars (name, value_type, int_value) VALUES ('n', 'int64', 1)",
    "UPDATE script_vars SET int_value = 2 WHERE name = 'n'",
    "DELETE FROM script_vars WHERE name = 'n'",
    "INSERT INTO script_control (action, message) VALUES ('halt', 'done')",
  ]) {
    assert.deepStrictEqual(protocolWrite(sql), [], sql);
  }
});

test("a CTE prefix cannot smuggle a protocol write", () => {
  // Anchoring the verb at token 0 alone would let a one-line dummy CTE bypass
  // the rule entirely; the analyzer therefore walks the CTE prefix before
  // reading the verb. Quoted target forms must resolve the same way, since
  // the fixture corpus already uses them.
  assert.equal(
    protocolWrite(
      "WITH d AS (SELECT 1) INSERT INTO result_get_value (call_id, status, result_value)" +
        " SELECT 'x', 'done', 1",
    ).length,
    1,
  );
  assert.equal(
    protocolWrite("WITH RECURSIVE d(v) AS (SELECT 1) DELETE FROM pending_host_calls").length,
    1,
  );
  assert.equal(protocolWrite("DELETE FROM [pending_host_calls]").length, 1);
  assert.equal(protocolWrite("DELETE FROM main.pending_host_calls").length, 1);
  assert.equal(
    protocolWrite(
      "INSERT OR REPLACE INTO result_get_value (call_id, status, result_value)" +
        " VALUES ('x', 'done', 1)",
    ).length,
    1,
  );
});

test("a second statement after a top-level ; is an error", () => {
  // One statement per `sql` field is the protocol contract: prepare_v2
  // compiles the first statement and silently drops the tail. A top-level
  // `;` with more SQL after it is that second, dropped statement.
  for (const sql of [
    "SELECT 1; PRAGMA writable_schema = ON",
    "SELECT 1; INSERT INTO result_get_value (call_id, status, result_value)" +
      " VALUES ('x', 'done', 1)",
    "SELECT 1; DELETE FROM pending_host_calls",
    "SELECT (SELECT 1); SELECT 2", // the `;` is top-level, after a subquery's ')'
  ]) {
    const found = multipleStatements(sql);
    assert.equal(found.length, 1, `${sql}: ${JSON.stringify(found)}`);
    assert.equal(found[0].severity, "error", sql);
  }
});

test("multiple-statements closes the leading-no-op denylist bypass", () => {
  // The core of the reported bug: leadingKeyword and writeTarget anchor on
  // the FIRST statement, so these two payloads sail past forbidden-statement
  // and protocol-table-write — the SELECT is all those rules ever see. Before
  // this rule lintScript returned ZERO findings (publishable); now the
  // multiple-statements error catches the drop and blocks publication.
  const pragmaBypass = "SELECT 1; PRAGMA writable_schema = ON";
  const writeBypass =
    "SELECT 1; INSERT INTO result_get_value (call_id, status, result_value)" +
    " VALUES ('x', 'done', 1)";

  // The old denylist rules still don't fire — they only see `SELECT 1`.
  assert.deepStrictEqual(forbidden(pragmaBypass), []);
  assert.deepStrictEqual(protocolWrite(writeBypass), []);

  // …but the payload is no longer publishable, on the multiple-statements code.
  for (const sql of [pragmaBypass, writeBypass]) {
    const all = lintOne(sql);
    assert.ok(!isPublishable(all), `${sql} must not be publishable`);
    assert.ok(
      all.some((f) => f.code === "multiple-statements" && f.severity === "error"),
      `${sql}: ${JSON.stringify(all)}`,
    );
  }
});

test("a single statement, terminated or not, is not multiple-statements", () => {
  // A bare trailing `;` terminates one statement — legal. And the tokenizer
  // collapses strings and comments, so a `;` inside a literal or a line
  // comment is not a statement separator and must never be flagged.
  for (const sql of [
    "SELECT 1", // no terminator
    "SELECT 1;", // trailing terminator only
    "SELECT result_value FROM result_get_value WHERE call_id = 'x';",
    "SELECT ';'", // `;` inside a string literal
    "SELECT ';;; not sql'", // several `;` inside a string literal
    "SELECT 1 -- ; x", // `;` inside a line comment
    "SELECT 1 /* ; */ + 1", // `;` inside a block comment
  ]) {
    assert.deepStrictEqual(multipleStatements(sql), [], sql);
  }
});

test("the message names the table and its role", () => {
  // "protocol-table-write" alone does not tell an author which of the several
  // runtime-owned tables they touched, or why it is owned.
  const message = protocolWrite("DELETE FROM pending_host_calls")[0].message;
  assert.ok(message.includes("pending_host_calls"), message);
  assert.ok(message.includes("queue"), message);
});
