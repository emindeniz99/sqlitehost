import assert from "node:assert/strict";
import { test } from "node:test";

import { isPublishable, lintScript, parseHostManifest, type LintFinding } from "../index.js";
import { readFixture } from "./helpers.js";

/**
 * sqlite-version-too-low-for-syntax (docs/validation.md), the TypeScript
 * mirror of SyntaxVersionLintTest.java.
 *
 * The sibling rule for functions has existed since the version lint landed;
 * this one covers the half of the above-floor surface that is grammar. The
 * gap was not theoretical: `example-011-insert-alias` shipped in the valid
 * corpus, is a syntax error on 3.19.3, and both validators passed it —
 * layer 3 prepares against whatever engine the validator links, which is
 * always far newer than the floor.
 *
 * Every case here is a pair: the construct at the default floor (3.19.3)
 * must be an error, and the SAME construct under a host that declares
 * 3.39.0 must be silent. A detector that fires unconditionally would pass
 * half of this file and make the supported unlock path — raise
 * minSqliteVersion — a lie.
 */

const manifestJson = readFixture("manifests/sample-host.manifest.json");
const manifest = parseHostManifest(manifestJson);

/** The same host with its declared floor raised to `versionNumber`. */
function manifestAtFloor(versionNumber: number) {
  const parsed = JSON.parse(manifestJson);
  parsed.library.minSqliteVersionNumber = versionNumber;
  return parseHostManifest(JSON.stringify(parsed));
}

const raisedFloor = manifestAtFloor(3039000);

const CODE = "sqlite-version-too-low-for-syntax";

function findings(sql: string, host = manifest): LintFinding[] {
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredFeatures: [],
    requiredMethods: [],
    steps: [{ id: "s", statements: [{ sql, bindings: {} }] }],
  };
  return lintScript(payload, host).filter((f) => f.code === CODE);
}

/**
 * One statement per feature id, chosen so the ONLY above-floor thing in it
 * is the syntax under test: `count`, `max` and `abs` are all pre-floor, so
 * nothing here can be caught by the function rule instead and score a false
 * green.
 */
const CASES: ReadonlyArray<readonly [string, string, string]> = [
  [
    "insert-alias",
    "INSERT INTO script_vars AS v (name, value_type, int_value) VALUES ('a', 'int64', 1)",
    "3.24.0",
  ],
  [
    "upsert",
    "INSERT INTO script_vars (name, value_type, int_value) VALUES ('a', 'int64', 1)" +
      " ON CONFLICT(name) DO NOTHING",
    "3.24.0",
  ],
  ["window-functions", "SELECT count(*) OVER () FROM script_vars", "3.25.0"],
  [
    "aggregate-filter",
    "SELECT count(*) FILTER (WHERE int_value > 0) FROM script_vars",
    "3.30.0",
  ],
  ["nulls-first-last", "SELECT name FROM script_vars ORDER BY name NULLS LAST", "3.30.0"],
  [
    "update-from",
    "UPDATE script_vars SET int_value = 1 FROM script_inputs WHERE script_vars.name = 'a'",
    "3.33.0",
  ],
  ["returning", "DELETE FROM script_vars WHERE name = 'a' RETURNING name", "3.35.0"],
  [
    "materialized-cte",
    "WITH c AS MATERIALIZED (SELECT 1 AS x) SELECT x FROM c",
    "3.35.0",
  ],
  ["json-arrow-operators", "SELECT text_value ->> '$.a' FROM script_vars", "3.38.0"],
  [
    "right-full-join",
    "SELECT a.name FROM script_vars a RIGHT JOIN script_inputs b ON a.name = b.name",
    "3.39.0",
  ],
  [
    "is-distinct-from",
    "SELECT name FROM script_vars WHERE int_value IS NOT DISTINCT FROM 1",
    "3.39.0",
  ],
];

test("every syntax feature above the host floor is an error", () => {
  for (const [feature, sql, version] of CASES) {
    const found = findings(sql);
    assert.equal(found.length, 1, `${feature}: ${sql} -> ${JSON.stringify(found)}`);
    assert.equal(found[0].severity, "error", feature);
    assert.ok(found[0].message.includes(version), `${feature}: ${found[0].message}`);
  }
});

test("the same syntax under a raised floor is silent", () => {
  // The whole point of the rule is that raising minSqliteVersion is the
  // supported unlock path (docs/sqlite-surface.md). A detector that cannot
  // be satisfied is a ban, not a version check.
  for (const [feature, sql] of CASES) {
    assert.deepStrictEqual(findings(sql, raisedFloor), [], feature);
  }
});

test("the message names the required version and the host's floor", () => {
  // Naming only one of the two leaves the author unable to decide between
  // the two fixes, exactly as the function rule's message argued.
  const message = findings(CASES[6][1])[0].message;
  assert.ok(message.includes("3.35.0"), message);
  assert.ok(message.includes("3.19.3"), message);
  assert.ok(message.includes("RETURNING"), message);
});

test("a delimited identifier is never the keyword", () => {
  // The tokenizer-level false positives the rule was designed against. Each
  // statement below is legal on 3.19.3 and must stay silent; a detector that
  // matched on text alone would reject all four, and an ERROR that blocks
  // publication is as damaging false as it is useful true.
  for (const sql of [
    // a table alias that happens to be spelled `full`, followed by a JOIN
    'SELECT * FROM script_vars "full" JOIN script_inputs ON 1',
    // a column spelled `returning`, in a write statement, at depth 0
    'UPDATE script_vars SET int_value = 1 WHERE name = "returning"',
    // a quoted name in call position — SQLite resolves `"over"(1)` as a
    // function call, and the tokenizer keeps the quoting
    'SELECT "over"(1) FROM script_vars',
    // `->` living inside a string literal
    "SELECT 'a->>b' FROM script_vars",
    // `NULLS` as an ordinary quoted column name
    'SELECT "nulls" FROM script_vars',
  ]) {
    assert.deepStrictEqual(findings(sql), [], sql);
  }
});

test("statement-anchored detectors do not fire on other statement kinds", () => {
  for (const sql of [
    // `conflict` is an ordinary column name; UPSERT only exists on an INSERT
    "SELECT a.name FROM script_vars a JOIN script_inputs b ON conflict = 1",
    // a subquery FROM belongs to the subquery, not to the UPDATE
    "UPDATE script_vars SET int_value = (SELECT max(int_value) FROM script_inputs)",
    // `returning` at depth 0 of a SELECT is a column, and SELECT has no
    // RETURNING clause to confuse it with
    "SELECT returning FROM script_vars",
  ]) {
    assert.deepStrictEqual(findings(sql), [], sql);
  }
});

test("one finding per feature per statement, and features are independent", () => {
  // Two uses of one construct are one problem with one fix. Two DIFFERENT
  // constructs are two, because raising the floor to the lower one still
  // leaves the other broken.
  assert.equal(
    findings("SELECT count(*) OVER (), max(int_value) OVER () FROM script_vars").length,
    1,
  );
  const both = findings(
    "INSERT INTO script_vars AS v (name, value_type, int_value)" +
      " VALUES ('a', 'int64', 1) ON CONFLICT(name) DO NOTHING RETURNING name",
  );
  assert.equal(both.length, 3, JSON.stringify(both));
});

test("these findings block publishing", () => {
  // docs/validation.md makes a payload publishable on zero errors, and
  // shipping any of these is a hard SQL failure on every device at the floor.
  const payload = {
    engine: "sqlite-host-v1",
    requiredApiLevel: 1,
    requiredFeatures: [],
    requiredMethods: [],
    steps: [
      {
        id: "s",
        statements: [{ sql: "SELECT count(*) OVER () FROM script_vars", bindings: {} }],
      },
    ],
  };
  assert.ok(!isPublishable(lintScript(payload, manifest)));
});

test("a floor exactly at the feature's release accepts it", () => {
  // The comparison is `>` and not `>=`: a host that declares 3.24.0 is
  // promising 3.24.0, so UPSERT is inside its contract, not above it.
  const exact = manifestAtFloor(3024000);
  assert.deepStrictEqual(findings(CASES[1][1], exact), []);
  // ...and one release below it is still an error.
  assert.equal(findings(CASES[1][1], manifestAtFloor(3023000)).length, 1);
});
