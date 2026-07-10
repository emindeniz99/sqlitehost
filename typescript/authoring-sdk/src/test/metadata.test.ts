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

test("metadata exposes the control table and the shared columns block", () => {
  const metadata = loadHostMetadata(readFixture("manifests/sample-host.manifest.json"));
  assert.deepStrictEqual(metadata.controlTable, {
    name: "script_control",
    columns: ["action", "message"],
  });
  // controlTable joins the autocomplete tables right after varsTable.
  const tableNames = metadata.tables.map((t) => t.name);
  assert.equal(tableNames.indexOf("script_control"), tableNames.indexOf("script_vars") + 1);
  assert.equal(metadata.columns.callId, "call_id");
  assert.equal(metadata.columns.itemIndex, "item_index");
  assert.equal(metadata.columns.doneValue, "done");
});

test("structural table columns come from the manifest columns block", () => {
  // Hosts may rename SQL-visible columns (docs/naming.md); the derived
  // table metadata must follow the manifest, not hardcode call_id.
  const base = JSON.parse(readFixture("manifests/sample-host.manifest.json"));
  const metadata = loadHostMetadata({
    ...base,
    columns: { ...base.columns, callId: "cid", itemIndex: "idx", status: "state" },
  });
  const callTable = metadata.tables.find((t) => t.name === "call_get_value");
  assert.deepStrictEqual(callTable?.columns, ["cid", "input_key"]);
  const resultTable = metadata.tables.find((t) => t.name === "result_get_value");
  assert.deepStrictEqual(resultTable?.columns, ["cid", "state", "result_value"]);
  const childTable = metadata.tables.find((t) => t.name === "call_get_values__input_keys");
  assert.deepStrictEqual(childTable?.columns, ["cid", "idx", "input_key"]);
});

test("loadHostMetadata accepts parsed JSON and rejects non-manifests", () => {
  const parsed = JSON.parse(readFixture("manifests/sample-host.manifest.json"));
  assert.deepStrictEqual(loadHostMetadata(parsed), SAMPLE_HOST_METADATA);
  assert.throws(() => loadHostMetadata("{}"), TypeError);
  assert.throws(() => loadHostMetadata(null), TypeError);
});
