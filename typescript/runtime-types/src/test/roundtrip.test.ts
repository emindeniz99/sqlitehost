import assert from "node:assert/strict";
import { test } from "node:test";

import { parseScript, serializeScript } from "../index.js";
import { listValidPayloads, readFixture } from "./helpers.js";

test("valid payload fixtures exist", () => {
  assert.ok(listValidPayloads().length >= 5);
});

for (const name of listValidPayloads()) {
  test(`round-trips ${name} byte-for-byte`, () => {
    const original = readFixture(`payloads/valid/${name}`);
    const script = parseScript(original);
    assert.equal(serializeScript(script), original);
  });
}

test("float payload example-006 round-trips byte-for-byte", () => {
  // Pinned float contract: the dyadic-exact fixture values (98.5, 0.75)
  // must reproduce the golden bytes through parse + canonical serialize.
  const original = readFixture("payloads/valid/example-006-floats.json");
  assert.equal(serializeScript(parseScript(original)), original);
});

test("round-trip preserves an empty bindings object", () => {
  const json = readFixture("payloads/invalid/unknown-required-method.json");
  // Structurally fine (the problem is semantic); bindings: {} must survive.
  const script = parseScript(json);
  assert.equal(serializeScript(script), json);
});
