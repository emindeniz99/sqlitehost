import assert from "node:assert/strict";
import { test } from "node:test";

import { parseScript, serializeScript } from "../index.js";
import { listValidPayloads, readFixture } from "./helpers.js";

/**
 * Payloads carrying a float32 the wire spelling cannot represent exactly.
 * `float32` is round-to-nearest on parse (docs/script-envelope.md), so the
 * first serialize writes the single the engine will store
 * (0.1 -> 0.10000000149011612) instead of the author's digits. Byte-identity
 * is therefore the property for payloads whose float32 values are already
 * representable as singles; for these, stability from the first write on is.
 */
const NON_SINGLE_REPRESENTABLE_FLOAT32 = new Set([
  "example-017-float32-rounding.json",
]);

test("valid payload fixtures exist", () => {
  assert.ok(listValidPayloads().length >= 5);
});

for (const name of listValidPayloads()) {
  if (NON_SINGLE_REPRESENTABLE_FLOAT32.has(name)) continue;
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

test("float32 0.1 parses to the single the engine stores", () => {
  // WHY: the same envelope must mean the same number in every SDK. Java's
  // ScriptJsonReader and the C# reader both narrow through a 32-bit float,
  // so a wire 0.1 is 0.10000000149011612 there; leaving the JSON double
  // alone here made a signed, byte-identical payload bind a different value
  // depending on which SDK opened it.
  const script = parseScript(readFixture("payloads/valid/example-017-float32-rounding.json"));
  const bindings = script.steps[0].statements[0].bindings ?? {};
  const weight = bindings["weight"];
  assert.equal(weight.type, "float32");
  assert.equal(weight.type === "float32" ? weight.value : undefined, 0.10000000149011612);
  // float64 keeps the double it was written as — only float32 narrows.
  const score = bindings["score"];
  assert.equal(score.type === "float64" ? score.value : undefined, 0.1);
});

test("a normalized float32 payload is stable under further round-trips", () => {
  const once = serializeScript(
    parseScript(readFixture("payloads/valid/example-017-float32-rounding.json")),
  );
  assert.match(once, /0\.10000000149011612/);
  assert.equal(serializeScript(parseScript(once)), once);
});

test("round-trip preserves an empty bindings object", () => {
  const json = readFixture("payloads/invalid/unknown-required-method.json");
  // Structurally fine (the problem is semantic); bindings: {} must survive.
  const script = parseScript(json);
  assert.equal(serializeScript(script), json);
});
