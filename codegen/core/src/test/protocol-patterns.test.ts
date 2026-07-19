import { strict as assert } from "node:assert";
import { test } from "node:test";
import { IDENTIFIER, METHOD_NAME, SQL_NAME } from "@sqlite-host/typespec";
import { IDENTIFIER_PATTERN, METHOD_NAME_PATTERN, SQL_NAME_PATTERN } from "../ir.js";

/**
 * The identifier-shape patterns are single-sourced in ir.ts as strings
 * (docs/proposals/rule-parameters-as-data.md) and projected per language.
 * The TypeSpec library enforces them at authoring time with compiled
 * RegExps but cannot import codegen-core (that would form a workspace
 * cycle), so it keeps its own literals. These tests pin those literals to
 * the single source — both by exact pattern text and by verdict over a
 * probe set — so the two definitions can never silently drift.
 */

const PROBES = [
  "getValue",
  "get_value",
  "A",
  "z",
  "_leading",
  "with_1_and_UPPER",
  "lower_snake_1",
  "UPPER",
  "get-value",
  "bad'name",
  "1st",
  "has space",
  "dollar$sign",
  "",
];

for (const [name, regex, pattern] of [
  ["IDENTIFIER", IDENTIFIER, IDENTIFIER_PATTERN],
  ["METHOD_NAME", METHOD_NAME, METHOD_NAME_PATTERN],
  ["SQL_NAME", SQL_NAME, SQL_NAME_PATTERN],
] as const) {
  test(`${name}: TypeSpec regex source equals the ir.ts pattern string`, () => {
    assert.equal(regex.source, pattern);
  });

  test(`${name}: TypeSpec regex and ir.ts pattern agree on every probe`, () => {
    const compiled = new RegExp(pattern);
    for (const probe of PROBES) {
      assert.equal(
        regex.test(probe),
        compiled.test(probe),
        `verdict diverges for ${JSON.stringify(probe)}`,
      );
    }
  });
}
