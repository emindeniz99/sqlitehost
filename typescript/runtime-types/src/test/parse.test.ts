import assert from "node:assert/strict";
import { test } from "node:test";

import {
  isBindingValue,
  isScript,
  parseScript,
  ScriptParseError,
  validateScript,
} from "../index.js";
import { readFixture } from "./helpers.js";

function baseScript(): Record<string, unknown> {
  return JSON.parse(readFixture("payloads/valid/example-001-read-then-conditional-write.json"));
}

function expectFindings(value: unknown, code: string, pathFragment: string): void {
  const findings = validateScript(value);
  const hit = findings.find((f) => f.code === code && f.path.includes(pathFragment));
  assert.ok(
    hit,
    `expected ${code} at ~${pathFragment}, got ${JSON.stringify(findings, null, 2)}`,
  );
}

test("parseScript rejects non-object payloads", () => {
  assert.throws(() => parseScript("[]"), ScriptParseError);
  assert.throws(() => parseScript("null"), ScriptParseError);
  assert.throws(() => parseScript("not json"), SyntaxError);
});

test("wrong engine type or value is invalid-envelope", () => {
  const s = baseScript();
  s["engine"] = 42;
  expectFindings(s, "invalid-envelope", "engine");
  s["engine"] = "some-other-engine";
  expectFindings(s, "invalid-envelope", "engine");
  delete s["engine"];
  expectFindings(s, "invalid-envelope", "engine");
});

test("missing or empty steps is invalid-envelope", () => {
  const s = baseScript();
  delete s["steps"];
  expectFindings(s, "invalid-envelope", "steps");
  s["steps"] = [];
  expectFindings(s, "invalid-envelope", "steps");
});

test("missing or non-integer requiredApiLevel is invalid-envelope", () => {
  const s = baseScript();
  delete s["requiredApiLevel"];
  expectFindings(s, "invalid-envelope", "requiredApiLevel");
  s["requiredApiLevel"] = 1.5;
  expectFindings(s, "invalid-envelope", "requiredApiLevel");
  s["requiredApiLevel"] = 0;
  expectFindings(s, "invalid-envelope", "requiredApiLevel");
});

test("empty step id is invalid-envelope", () => {
  const s = baseScript();
  (s["steps"] as Array<Record<string, unknown>>)[0]["id"] = "";
  expectFindings(s, "invalid-envelope", "steps[0].id");
});

test("duplicate step ids are reported as duplicate-step-id", () => {
  const s = JSON.parse(readFixture("payloads/invalid/duplicate-step-id.json"));
  expectFindings(s, "duplicate-step-id", "steps[1].id");
  assert.throws(
    () => parseScript(readFixture("payloads/invalid/duplicate-step-id.json")),
    (error: unknown) =>
      error instanceof ScriptParseError &&
      error.findings.some((f) => f.code === "duplicate-step-id"),
  );
});

test("step without statements is invalid-envelope", () => {
  const s = baseScript();
  (s["steps"] as Array<Record<string, unknown>>)[0]["statements"] = [];
  expectFindings(s, "invalid-envelope", "steps[0].statements");
});

test("statement without sql is invalid-envelope", () => {
  const s = baseScript();
  const statements = (s["steps"] as Array<Record<string, unknown>>)[0][
    "statements"
  ] as Array<Record<string, unknown>>;
  delete statements[0]["sql"];
  expectFindings(s, "invalid-envelope", "steps[0].statements[0].sql");
});

test("malformed binding values are invalid-envelope", () => {
  const s = baseScript();
  const statements = (s["steps"] as Array<Record<string, unknown>>)[0][
    "statements"
  ] as Array<Record<string, unknown>>;
  statements[0]["bindings"] = { callId: { type: "float", value: 1.25 } };
  expectFindings(s, "invalid-envelope", "bindings.callId.type");
  statements[0]["bindings"] = { callId: { type: "text", value: 7 } };
  expectFindings(s, "invalid-envelope", "bindings.callId.value");
  statements[0]["bindings"] = { callId: { type: "null", value: null } };
  expectFindings(s, "invalid-envelope", "bindings.callId.value");
  statements[0]["bindings"] = { callId: { type: "blob", value: "not base64!" } };
  expectFindings(s, "invalid-envelope", "bindings.callId.value");
});

test("malformed runtime inputs are invalid-envelope", () => {
  const s = baseScript();
  s["inputs"] = [{ name: "", value: { type: "int64", value: 1 } }];
  expectFindings(s, "invalid-envelope", "inputs[0].name");
  s["inputs"] = [{ name: "x", value: { type: "int64", value: "12x" } }];
  expectFindings(s, "invalid-envelope", "inputs[0].value");
});

test("type guards accept and reject binding values", () => {
  assert.ok(isBindingValue({ type: "null" }));
  assert.ok(isBindingValue({ type: "int64", value: "9223372036854775807" }));
  assert.ok(isBindingValue({ type: "blob", value: "3q2+7w==" }));
  assert.ok(!isBindingValue({ type: "int64", value: "9223372036854775808" }));
  assert.ok(!isBindingValue({ type: "bool", value: "true" }));
  assert.ok(!isBindingValue({ type: "null", value: null }));
});

test("isScript matches validateScript", () => {
  assert.ok(isScript(baseScript()));
  assert.ok(!isScript({}));
});
