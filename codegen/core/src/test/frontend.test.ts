import { strict as assert } from "node:assert";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { compileHostLibrary } from "../frontend.js";
import { DEFAULT_NAMING } from "../naming.js";
import {
  compileSource,
  manifestFixturePath,
  samplePath,
} from "./helpers.js";

test("sample host library normalizes to exactly the committed manifest IR", async () => {
  const result = await compileHostLibrary(samplePath);
  assert.deepEqual(
    result.diagnostics.map((d) => `${d.code}: ${d.message}`),
    [],
  );
  assert.ok(result.ir, "expected IR");
  const fixture = JSON.parse(readFileSync(manifestFixturePath, "utf8"));
  assert.deepEqual(result.ir, fixture);
});

test("compiling twice produces identical IR (deterministic frontend)", async () => {
  const first = await compileHostLibrary(samplePath);
  const second = await compileHostLibrary(samplePath);
  assert.deepEqual(first.ir, second.ir);
});

test("sqlName defaults to snake_case of the property name when @sqlName is absent", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "putThing", handler: "PutThing" })
      op PutThing(input: PutThingInput): PutThingResult;
    }

    model PutThingInput {
      targetValue: int64;
      someHTTPUrl?: string;
    }

    model PutThingResult {
      ok: boolean;
    }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  const [method] = result.ir.methods;
  assert.deepEqual(
    method.input.fields.map((f) => [f.propertyName, f.sqlName, f.column, f.optional]),
    [
      ["targetValue", "target_value", "input_target_value", false],
      ["someHTTPUrl", "some_http_url", "input_some_http_url", true],
    ],
  );
  assert.deepEqual(
    method.result.fields.map((f) => [f.sqlName, f.column]),
    [["ok", "result_ok"]],
  );
});

test("float32/float64 scalars map to the IR float scalar types", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "recordScore", handler: "RecordScore" })
      op RecordScore(input: RecordScoreInput): RecordScoreResult;
    }

    model RecordScoreInput {
      score: float64;
      weight?: float32;
    }

    model RecordScoreResult {
      average: float64;
    }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  const [method] = result.ir.methods;
  assert.deepEqual(
    method.input.fields.map((f) => [f.propertyName, f.scalarType, f.optional]),
    [
      ["score", "float64", false],
      ["weight", "float32", true],
    ],
  );
  assert.deepEqual(
    method.result.fields.map((f) => [f.propertyName, f.scalarType, f.optional]),
    [["average", "float64", false]],
  );
});

test("@sqlName override is respected over the snake_case default", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "putThing", handler: "PutThing" })
      op PutThing(input: PutThingInput): PutThingResult;
    }

    model PutThingInput {
      @sqlName("custom_target")
      targetValue: int64;
    }

    model PutThingResult {
      @sqlName("was_stored")
      ok: boolean;
    }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  const [method] = result.ir.methods;
  assert.deepEqual(
    method.input.fields.map((f) => [f.sqlName, f.column]),
    [["custom_target", "input_custom_target"]],
  );
  assert.deepEqual(
    method.result.fields.map((f) => [f.sqlName, f.column]),
    [["was_stored", "result_was_stored"]],
  );
});

test("naming-prefix overrides propagate to every derived physical name", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({
      apiLevel: 2,
      callTablePrefix: "req_",
      resultTablePrefix: "res_",
      inputColumnPrefix: "in_",
      resultColumnPrefix: "out_",
      inputListTableInfix: "__in_",
      resultListTableInfix: "__out_"
    })
    interface Methods {
      @hostMethod({ name: "getValues", handler: "GetValues" })
      op GetValues(input: GetValuesInput): GetValuesResult;
    }

    model GetValuesInput {
      defaultValue?: int64;
      keys: KeyItem[];
    }

    model KeyItem {
      key: string;
    }

    model GetValuesResult {
      entries: EntryItem[];
    }

    model EntryItem {
      key: string;
      found: boolean;
    }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  const ir = result.ir;
  assert.deepEqual(ir.naming, {
    callTablePrefix: "req_",
    resultTablePrefix: "res_",
    inputColumnPrefix: "in_",
    resultColumnPrefix: "out_",
    inputListTableInfix: "__in_",
    resultListTableInfix: "__out_",
  });
  const [method] = ir.methods;
  assert.equal(method.callTable, "req_get_values");
  assert.equal(method.resultTable, "res_get_values");
  assert.equal(method.queueTrigger, "trg_req_get_values_queue");
  assert.equal(method.input.fields[0].column, "in_default_value");
  assert.equal(method.input.listFields[0].childTable, "req_get_values__in_keys");
  assert.equal(method.input.listFields[0].itemFields[0].column, "in_key");
  assert.equal(method.result.listFields[0].childTable, "res_get_values__out_entries");
  assert.deepEqual(
    method.result.listFields[0].itemFields.map((f) => f.column),
    ["out_key", "out_found"],
  );
});

test("naming keys are optional and default to protocol v1 conventions", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  assert.deepEqual(result.ir.naming, DEFAULT_NAMING);
  assert.equal(result.ir.methods[0].callTable, "call_get_value");
});

test("method apiLevel defaults to the library apiLevel and can be overridden", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 3 })
    interface Methods {
      @hostMethod({ name: "first", handler: "First" })
      op First(input: In): Out;

      @hostMethod({ name: "second", handler: "Second", apiLevel: 5 })
      op Second(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  assert.equal(result.ir.library.apiLevel, 3);
  assert.deepEqual(
    result.ir.methods.map((m) => [m.methodName, m.apiLevel]),
    [
      ["first", 3],
      ["second", 5],
    ],
  );
});

test("methods appear in declaration order and library metadata is resolved", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Some.Nested.Space;

    @hostLibrary({ apiLevel: 1 })
    interface HostApi {
      @hostMethod({ name: "zebra", handler: "Zebra" })
      op Zebra(input: In): Out;

      @hostMethod({ name: "alpha", handler: "Alpha" })
      op Alpha(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  assert.equal(result.ir.library.namespace, "Some.Nested.Space");
  assert.equal(result.ir.library.interfaceName, "HostApi");
  assert.deepEqual(
    result.ir.methods.map((m) => m.methodName),
    ["zebra", "alpha"],
  );
});
