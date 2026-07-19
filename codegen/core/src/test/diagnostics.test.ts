import { test } from "node:test";
import {
  assertDiagnostic,
  assertLibrariesDiagnostic,
  compileSource,
  compileSourceAll,
} from "./helpers.js";

/** Wrap interface/model snippets in a valid host-library shell. */
function shell(body: string): string {
  return `
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;
    ${body}
  `;
}

test("rejects top-level primitive input", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: string): Out;
      }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-method-shape");
});

test("rejects operations with more than one parameter", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In, extra: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-method-shape");
});

test("rejects non-model return types", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): int64;
      }
      model In { key: string; }
    `),
  );
  assertDiagnostic(result, "invalid-method-shape");
});

test("rejects nested model fields", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model Child { value: string; }
      model In { child: Child; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "nested-model");
});

test("rejects nested lists", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model Item { value: string; }
      model In { matrix: Item[][]; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "nested-list");
});

test("rejects lists inside list item models", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model Sub { value: string; }
      model Item { subs: Sub[]; }
      model In { items: Item[]; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "nested-list");
});

test("rejects unsupported scalars", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { ratio: float; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "unsupported-scalar");
});

test("rejects decimal scalars (floats are float32/float64 only)", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { amount: decimal; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "unsupported-scalar");
});

test("rejects primitive lists", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { keys: string[]; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-list-item");
});

test("rejects empty list item models", async () => {
  // An empty item model must be rejected here — TypeSpec validation is
  // the single choke point ahead of every emitter. If it slips through,
  // the C# emitter leaves the list-field builder chain unterminated
  // (shapeCalls closes the wrong paren) and the runtime issues a
  // projection-less "SELECT  FROM" child-table read at drain time
  // (ErasedHostMethodSpec.LoadInputListRows).
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model Item {}
      model In { items: Item[]; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "empty-list-item");
});

test("rejects empty list item models on the result side", async () => {
  // validateItemModel serves result shapes too; an empty item model on
  // the output side emits the same broken C# builder chain.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model Item {}
      model In { key: string; }
      model Out { entries: Item[]; }
    `),
  );
  assertDiagnostic(result, "empty-list-item");
});

test("rejects optional list fields", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model Item { key: string; }
      model In { items?: Item[]; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "optional-list");
});

test("rejects union fields", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { value: string | int64; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "unsupported-field-type");
});

test("rejects map/Record fields", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { extras: Record<string>; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "unsupported-field-type");
});

test("rejects duplicate method names", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;

        @hostMethod({ name: "getValue", handler: "GetValueAgain" })
        op GetValueAgain(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-method-name");
});

test("rejects duplicate sqlNames within a shape", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In {
        @sqlName("key")
        keyOne: string;

        @sqlName("key")
        keyTwo: string;
      }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-sql-name");
});

test("rejects distinct method names that derive the same tables", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;

        @hostMethod({ name: "get_value", handler: "GetValueSnake" })
        op GetValueSnake(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-table-name");
});

test("rejects operations without @hostMethod", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "missing-host-method");
});

test("rejects invalid handler identifiers", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "9NotAnIdentifier" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-handler-name");
});

test("rejects invalid api levels", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 0 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-api-level");
});

test("reports when no @hostLibrary interface exists", async () => {
  const result = await compileSource(
    shell(`
      model Lonely { key: string; }
    `),
  );
  assertDiagnostic(result, "no-host-library");
});

test("rejects an empty shared workspace table name", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, queueTable: "" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-shared-table-name");
});

test("rejects shared workspace table names that are not mutually distinct", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, inputsTable: "shared_kv", varsTable: "shared_kv" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-shared-table-name");
});

test("rejects a shared table name colliding with a default-name workspace table", async () => {
  // Renaming the queue table onto the inputs table's default collides
  // with the resolved (defaulted) inputs table name.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, queueTable: "script_inputs" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-shared-table-name");
});

test("rejects a shared table name colliding with a derived call table", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, queueTable: "call_get_value" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "shared-table-name-collision");
});

test("rejects a shared table name colliding with a derived list child table", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, varsTable: "call_get_values__input_keys" })
      interface Methods {
        @hostMethod({ name: "getValues", handler: "GetValues" })
        op GetValues(input: In): Out;
      }
      model Item { key: string; }
      model In { keys: Item[]; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "shared-table-name-collision");
});

test("rejects an empty control table name", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, controlTable: "" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-shared-table-name");
});

test("rejects a control table name duplicating another shared table", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, controlTable: "script_vars" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-shared-table-name");
});

test("rejects a control table name colliding with a derived call table", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, controlTable: "call_get_value" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "shared-table-name-collision");
});

test("rejects an empty column name", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, callIdColumn: "" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-column-name");
});

test("rejects an empty doneStatusValue", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, doneStatusValue: "" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-done-status-value");
});

test("rejects duplicate column names within the queue table set", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, methodColumn: "status" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-column-name");
});

test("rejects duplicate column names within the named-value table set", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, intValueColumn: "name" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-column-name");
});

test("rejects duplicate column names within the control table set", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, actionColumn: "message" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-column-name");
});

test("rejects itemIndex duplicating callId (list child row identity)", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, itemIndexColumn: "call_id" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-column-name");
});

test("rejects a callId column colliding with a derived input field column", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, callIdColumn: "input_key" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "column-name-collision");
});

test("rejects a status column colliding with a derived list item result column", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, statusColumn: "result_key" })
      interface Methods {
        @hostMethod({ name: "getValues", handler: "GetValues" })
        op GetValues(input: In): Out;
      }
      model In { key: string; }
      model Item { key: string; }
      model Out { entries: Item[]; }
    `),
  );
  assertDiagnostic(result, "column-name-collision");
});

test("rejects inline exposure requested on a mutating method", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "setValue", handler: "SetValue", inline: true })
        op SetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "inline-mutating-method");
});

test("rejects inline exposure requested with a list field", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValues", handler: "GetValues", mutates: false, inline: true })
        op GetValues(input: In): Out;
      }
      model Item { key: string; }
      model In { keys: Item[]; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "inline-list-field");
});

test("rejects a functionName override on a multi-scalar result (functionName counts as a request)", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getPair", handler: "GetPair", mutates: false, functionName: "fn_pair" })
        op GetPair(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; other: int64; }
    `),
  );
  assertDiagnostic(result, "inline-result-not-single-scalar");
});

test("rejects inline exposure requested with a zero-scalar result", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "touchValue", handler: "TouchValue", mutates: false, inline: true })
        op TouchValue(input: In): Out;
      }
      model In { key: string; }
      model Out {}
    `),
  );
  assertDiagnostic(result, "inline-result-not-single-scalar");
});

test("rejects inline exposure requested with a required input field after an optional one", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue", mutates: false, inline: true })
        op GetValue(input: In): Out;
      }
      model In {
        fallback?: int64;
        key: string;
      }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "inline-required-after-optional");
});

test("rejects two methods claiming the same inline function name", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue", mutates: false, functionName: "fn_same" })
        op GetValue(input: In): Out;

        @hostMethod({ name: "peekValue", handler: "PeekValue", mutates: false, functionName: "FN_SAME" })
        op PeekValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "duplicate-function-name");
});

test("rejects an inline function name colliding with a derived table name", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue", mutates: false, functionName: "call_get_value" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "function-name-collision");
});

test("rejects an inline function name colliding with a SQLite built-in", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue", mutates: false, functionName: "coalesce" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "builtin-function-collision");
});

test("rejects a derived inline function name colliding with a SQLite built-in", async () => {
  // An empty prefix is itself invalid, so the derived-name collision is
  // exercised through a prefix that lands exactly on a built-in name.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, functionPrefix: "group_" })
      interface Methods {
        @hostMethod({ name: "concat", handler: "Concat", mutates: false })
        op Concat(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "builtin-function-collision");
});

test("rejects an empty functionPrefix", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, functionPrefix: "" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "invalid-function-prefix");
});

test("rejects duplicate @hostLibrary interface names across libraries", async () => {
  const result = await compileSourceAll(`
    import "@sqlite-host/typespec";
    using SqliteHost;

    namespace A {
      @SqliteHost.hostLibrary({ apiLevel: 1 })
      interface Methods {
        @SqliteHost.hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    }

    namespace B {
      @SqliteHost.hostLibrary({ apiLevel: 1 })
      interface Methods {
        @SqliteHost.hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    }
  `);
  assertLibrariesDiagnostic(result, "duplicate-host-library-name");
});
