import assert from "node:assert/strict";
import { test } from "node:test";

import {
  script,
  serializeScript,
  text,
  ScriptParseError,
} from "../index.js";
import { readFixture } from "./helpers.js";

test("builder reproduces example-001 byte-for-byte", () => {
  const built = script({
    scriptId: "example-001",
    requiredApiLevel: 1,
    requiredFeatures: ["typedNamedBindings", "splitResultTables"],
    requiredMethods: ["getValue", "setValue"],
  })
    .step("read-current")
    .statement(
      "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'example-key')",
      { callId: text("read-1") },
    )
    .step("write-value")
    .statement(
      "INSERT INTO call_set_value (call_id, input_key, input_value) SELECT :callId, 'example-key', 42 WHERE EXISTS (SELECT 1 FROM result_get_value WHERE call_id = :readCallId AND status = 'done' AND result_value <> 42)",
      { callId: text("write-1"), readCallId: text("read-1") },
    )
    .build();

  assert.equal(
    serializeScript(built),
    readFixture("payloads/valid/example-001-read-then-conditional-write.json"),
  );
});

test("build() validates the assembled envelope", () => {
  // no steps
  assert.throws(() => script({ requiredApiLevel: 1 }).build(), ScriptParseError);
  // duplicate step ids
  assert.throws(
    () =>
      script({ requiredApiLevel: 1 })
        .step("a")
        .statement("SELECT 1")
        .step("a")
        .statement("SELECT 2")
        .build(),
    (error: unknown) =>
      error instanceof ScriptParseError &&
      error.findings.some((f) => f.code === "duplicate-step-id"),
  );
  // step without statements
  assert.throws(
    () => script({ requiredApiLevel: 1 }).step("a").build(),
    ScriptParseError,
  );
});

test("statement without bindings omits the bindings key", () => {
  const built = script({ requiredApiLevel: 1 })
    .step("noop")
    .statement("SELECT 1")
    .build();
  assert.equal("bindings" in built.steps[0].statements[0], false);
});
