import { strict as assert } from "node:assert";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { generateSchemaScript } from "../ddl.js";
import { compileHostLibrary } from "../frontend.js";
import {
  COLUMNS_V1,
  CONTROL_TABLE_V1,
  DEFAULT_MIN_SQLITE_VERSION_NUMBER,
  INPUTS_TABLE_V1,
  QUEUE_TABLE_V1,
  VARS_TABLE_V1,
} from "../ir.js";
import { DEFAULT_NAMING } from "../naming.js";
import {
  assertDiagnostic,
  compileSource,
  compileSourceAll,
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
    functionPrefix: "fn_",
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

test("minSqliteVersion parses to the SQLITE_VERSION_NUMBER form", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1, minSqliteVersion: "3.9.0" })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  assert.equal(result.ir.library.minSqliteVersionNumber, 3009000);
});

test("a fourth minSqliteVersion component is ignored per SQLITE_VERSION_NUMBER", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1, minSqliteVersion: "3.8.11.1" })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  assert.equal(result.ir.library.minSqliteVersionNumber, 3008011);
});

test("an unparseable minSqliteVersion reports a diagnostic", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1, minSqliteVersion: "abc" })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assertDiagnostic(result, "invalid-min-sqlite-version");
});

test("minSqliteVersionNumber defaults to 3019003 when minSqliteVersion is absent", async () => {
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
  assert.equal(result.ir.library.minSqliteVersionNumber, 3019003);
  assert.equal(
    result.ir.library.minSqliteVersionNumber,
    DEFAULT_MIN_SQLITE_VERSION_NUMBER,
  );
});

test("queueTable/inputsTable/varsTable overrides rename the shared tables only", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({
      apiLevel: 1,
      queueTable: "host_queue",
      inputsTable: "script_params",
      varsTable: "script_scratch"
    })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  const ir = result.ir;
  assert.equal(ir.queueTable.name, "host_queue");
  assert.equal(ir.inputsTable.name, "script_params");
  assert.equal(ir.varsTable.name, "script_scratch");
  // Column shapes are protocol constants and never change with the name.
  assert.deepEqual(ir.queueTable.columns, QUEUE_TABLE_V1.columns);
  assert.deepEqual(ir.inputsTable.columns, INPUTS_TABLE_V1.columns);
  assert.deepEqual(ir.varsTable.columns, VARS_TABLE_V1.columns);
  // Derived per-method naming is unaffected.
  assert.deepEqual(ir.naming, DEFAULT_NAMING);
  assert.equal(ir.methods[0].callTable, "call_get_value");
});

test("shared table names default to the protocol v1 names when absent", async () => {
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
  assert.deepEqual(result.ir.queueTable, QUEUE_TABLE_V1);
  assert.deepEqual(result.ir.inputsTable, INPUTS_TABLE_V1);
  assert.deepEqual(result.ir.varsTable, VARS_TABLE_V1);
  assert.deepEqual(result.ir.controlTable, CONTROL_TABLE_V1);
  assert.deepEqual(result.ir.columns, COLUMNS_V1);
});

test("controlTable override renames the control table only", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1, controlTable: "script_ctl" })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  assert.equal(result.ir.controlTable.name, "script_ctl");
  // The column list stays derived from the (default) columns config.
  assert.deepEqual(result.ir.controlTable.columns, CONTROL_TABLE_V1.columns);
  assert.deepEqual(result.ir.queueTable, QUEUE_TABLE_V1);
  assert.match(generateSchemaScript(result.ir), /CREATE TABLE script_ctl \(/);
});

test("column options resolve into ir.columns and every table's column list", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({
      apiLevel: 1,
      callIdColumn: "cid",
      itemIndexColumn: "idx",
      doneStatusValue: "ok",
      actionColumn: "cmd"
    })
    interface Methods {
      @hostMethod({ name: "getValues", handler: "GetValues" })
      op GetValues(input: In): Out;
    }

    model Item { key: string; }
    model In { keys: Item[]; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  const ir = result.ir;
  assert.deepEqual(ir.columns, {
    ...COLUMNS_V1,
    callId: "cid",
    itemIndex: "idx",
    doneValue: "ok",
    action: "cmd",
  });
  // Renamed columns flow into every runtime-managed table's column list.
  assert.deepEqual(ir.queueTable.columns, ["queue_id", "cid", "method", "status"]);
  assert.deepEqual(ir.inputsTable.columns, INPUTS_TABLE_V1.columns);
  assert.deepEqual(ir.controlTable.columns, ["cmd", "message"]);
  // ... and into the DDL: table bodies, trigger body, done literal.
  const ddl = generateSchemaScript(ir);
  assert.match(ddl, /cid TEXT NOT NULL UNIQUE/);
  assert.match(ddl, /cmd TEXT NOT NULL/);
  assert.match(ddl, /cid TEXT NOT NULL PRIMARY KEY/);
  assert.match(ddl, /status TEXT NOT NULL DEFAULT 'ok'/);
  assert.match(ddl, /idx INTEGER NOT NULL/);
  assert.match(ddl, /PRIMARY KEY \(cid, idx\)/);
  assert.match(ddl, /INSERT INTO pending_host_calls \(cid, method\)/);
  assert.match(ddl, /VALUES \(NEW\.cid, 'getValues'\)/);
  assert.doesNotMatch(ddl, /call_id/);
  assert.doesNotMatch(ddl, /item_index/);
  assert.doesNotMatch(ddl, /DEFAULT 'done'/);
});

test("done status value with an embedded quote is escaped in the DDL", async () => {
  // doneStatusValue is data, not an identifier — validators only require
  // it non-empty, so an embedded quote must not break the generated
  // DEFAULT '...' literal (the schema would fail before any script runs).
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1, doneStatusValue: "do'ne" })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  const ddl = generateSchemaScript(result.ir);
  assert.match(ddl, /status TEXT NOT NULL DEFAULT 'do''ne'/);
});

// Two @hostLibrary interfaces in one compilation. Both declare a
// "getValue" method deriving the same call_get_value/result_get_value
// tables — that is FINE across libraries, because each library is an
// independent runtime definition with its own workspace
// (docs/manifest.md); table-name uniqueness is a per-library rule.
const TWO_LIBRARY_SOURCE = `
  import "@sqlite-host/typespec";
  using SqliteHost;
  namespace Test;

  @hostLibrary({ apiLevel: 1 })
  interface GameHostMethods {
    @hostMethod({ name: "getValue", handler: "GetValue" })
    op GetValue(input: KeyInput): ValueResult;
  }

  @hostLibrary({ apiLevel: 2, varsTable: "admin_vars" })
  interface AdminHostMethods {
    @hostMethod({ name: "getValue", handler: "GetValue" })
    op GetValue(input: KeyInput): ValueResult;

    @hostMethod({ name: "resetValue", handler: "ResetValue" })
    op ResetValue(input: KeyInput): ValueResult;
  }

  model KeyInput { key: string; }
  model ValueResult { value: int64; }
`;

test("compileHostLibraries returns one IR per library in declaration order", async () => {
  const result = await compileSourceAll(TWO_LIBRARY_SOURCE);
  assert.ok(
    result.irs,
    JSON.stringify(result.diagnostics.map((d) => d.message)),
  );
  assert.deepEqual(
    result.irs.map((ir) => ir.library.interfaceName),
    ["GameHostMethods", "AdminHostMethods"],
  );
  const [game, admin] = result.irs;
  assert.equal(game.library.apiLevel, 1);
  assert.equal(admin.library.apiLevel, 2);
  // Cross-library derived-table "collision": both libraries own a
  // call_get_value in their own workspace — no diagnostic.
  assert.equal(game.methods[0].callTable, "call_get_value");
  assert.equal(admin.methods[0].callTable, "call_get_value");
  // Per-library shared-table overrides apply independently.
  assert.equal(game.varsTable.name, "script_vars");
  assert.equal(admin.varsTable.name, "admin_vars");
  // The shared KeyInput/ValueResult models resolve into both IRs.
  assert.equal(game.methods[0].input.modelName, "KeyInput");
  assert.equal(admin.methods[0].input.modelName, "KeyInput");
  assert.deepEqual(game.methods[0].input, admin.methods[0].input);
  assert.deepEqual(game.methods[0].result, admin.methods[0].result);
});

test("the single-library API rejects multi-library programs, pointing to the plural API", async () => {
  const result = await compileSource(TWO_LIBRARY_SOURCE);
  assertDiagnostic(result, "multiple-host-libraries");
  const diagnostic = result.diagnostics.find(
    (d) => d.code === "@sqlite-host/typespec/multiple-host-libraries",
  );
  assert.match(diagnostic!.message, /compileHostLibraries/);
});

test("compileHostLibraries still works for a single library", async () => {
  const result = await compileSourceAll(`
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
  assert.ok(result.irs, JSON.stringify(result.diagnostics.map((d) => d.message)));
  assert.equal(result.irs.length, 1);
  assert.equal(result.irs[0].library.interfaceName, "Methods");
});

// ---------------------------------------------------------------------------
// Inline scalar functions (docs/proposals/inline-host-functions.md).
// ---------------------------------------------------------------------------

test("mutates defaults to true and methods stay non-inline (back-compat)", async () => {
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
  const [method] = result.ir.methods;
  assert.equal(method.mutates, true);
  assert.equal(method.inline, null);
  assert.equal(result.ir.naming.functionPrefix, "fn_");
  assert.ok(!result.ir.library.features.includes("inlineFunctions"));
});

test("an eligible mutates:false method is inline-exposed automatically", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "lookupScore", handler: "LookupScore", mutates: false })
      op LookupScore(input: In): Out;
    }

    model In {
      key: string;
      slot: int32;
      fallback?: int64;
      scale?: float32;
    }
    model Out { score: float64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  const [method] = result.ir.methods;
  assert.equal(method.mutates, false);
  assert.deepEqual(method.inline, {
    functionName: "fn_lookup_score",
    minArgs: 2,
    maxArgs: 4,
    args: [
      { propertyName: "key", sqlName: "key", scalarType: "string", optional: false },
      { propertyName: "slot", sqlName: "slot", scalarType: "int32", optional: false },
      { propertyName: "fallback", sqlName: "fallback", scalarType: "int64", optional: true },
      { propertyName: "scale", sqlName: "scale", scalarType: "float32", optional: true },
    ],
    returns: { propertyName: "score", sqlName: "score", scalarType: "float64" },
  });
  assert.deepEqual(result.ir.library.features, [
    "typedNamedBindings",
    "splitResultTables",
    "scriptInputs",
    "scriptVars",
    "scriptControl",
    "inlineFunctions",
  ]);
});

test("functionPrefix and functionName overrides shape the inline function name", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1, functionPrefix: "udf_" })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue", mutates: false })
      op GetValue(input: In): Out;

      @hostMethod({
        name: "peekValue",
        handler: "PeekValue",
        mutates: false,
        functionName: "peek"
      })
      op PeekValue(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  assert.equal(result.ir.naming.functionPrefix, "udf_");
  assert.deepEqual(
    result.ir.methods.map((m) => m.inline?.functionName),
    ["udf_get_value", "peek"],
  );
});

test("inline: false opts an eligible method out and drops the feature", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue", mutates: false, inline: false })
      op GetValue(input: In): Out;
    }

    model In { key: string; }
    model Out { value: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  const [method] = result.ir.methods;
  assert.equal(method.mutates, false);
  assert.equal(method.inline, null);
  assert.ok(!result.ir.library.features.includes("inlineFunctions"));
});

test("ineligible mutates:false methods are silently not exposed when not requested", async () => {
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "getValues", handler: "GetValues", mutates: false })
      op GetValues(input: ListIn): Out;

      @hostMethod({ name: "getPair", handler: "GetPair", mutates: false })
      op GetPair(input: In): PairOut;

      @hostMethod({ name: "getGap", handler: "GetGap", mutates: false })
      op GetGap(input: GapIn): Out;
    }

    model Item { key: string; }
    model ListIn { keys: Item[]; }
    model In { key: string; }
    model GapIn {
      fallback?: int64;
      key: string;
    }
    model Out { value: int64; }
    model PairOut { value: int64; other: int64; }
  `);
  assert.ok(result.ir, JSON.stringify(result.diagnostics.map((d) => d.message)));
  assert.deepEqual(
    result.ir.methods.map((m) => m.inline),
    [null, null, null],
  );
  assert.ok(!result.ir.library.features.includes("inlineFunctions"));
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
