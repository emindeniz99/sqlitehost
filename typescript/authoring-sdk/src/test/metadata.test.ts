import assert from "node:assert/strict";
import { test } from "node:test";

import { loadHostMetadata, SAMPLE_HOST_METADATA } from "../index.js";
import { readFixture } from "./helpers.js";

test("loadHostMetadata(sample manifest) equals the generated const", () => {
  const metadata = loadHostMetadata(readFixture("manifests/sample-host.manifest.json"));
  assert.deepStrictEqual(metadata, SAMPLE_HOST_METADATA);
});

test("metadata exposes autocomplete tables, columns, and methods", () => {
  const metadata = loadHostMetadata(readFixture("manifests/sample-host.manifest.json"));
  assert.deepStrictEqual(
    metadata.methods.map((m) => m.methodName),
    ["getValue", "setValue", "getValues", "putBlob", "recordScore"],
  );
  const tableNames = metadata.tables.map((t) => t.name);
  assert.ok(tableNames.includes("pending_host_calls"));
  assert.ok(tableNames.includes("call_get_values__input_keys"));
  assert.ok(tableNames.includes("result_get_values__result_entries"));
  const resultGetValue = metadata.tables.find((t) => t.name === "result_get_value");
  assert.deepStrictEqual(resultGetValue?.columns, ["call_id", "status", "result_value"]);
  const callRecordScore = metadata.tables.find((t) => t.name === "call_record_score");
  assert.deepStrictEqual(callRecordScore?.columns, [
    "call_id",
    "input_key",
    "input_score",
    "input_weight",
  ]);
});

test("loadHostMetadata accepts parsed JSON and rejects non-manifests", () => {
  const parsed = JSON.parse(readFixture("manifests/sample-host.manifest.json"));
  assert.deepStrictEqual(loadHostMetadata(parsed), SAMPLE_HOST_METADATA);
  assert.throws(() => loadHostMetadata("{}"), TypeError);
  assert.throws(() => loadHostMetadata(null), TypeError);
});
