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

test("rejects a host library declared in the global namespace", async () => {
  // The emitters derive Java package and C# namespace names from the
  // library namespace; the global namespace's empty name would emit
  // invalid code (e.g. Java "package .generated;").
  const result = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }
    model In { key: string; }
    model Out { value: int64; }
  `);
  assertDiagnostic(result, "missing-namespace");
});

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

test("rejects two referenced input models sharing a simple name across namespaces", async () => {
  // All three emitters flatten every namespace into ONE C#/Java/TS
  // namespace/package and key DTOs by simple name, so two DISTINCT `Foo`
  // shapes collapse/overwrite into one while the other method's generated
  // specs reference fields the surviving DTO lacks -> uncompilable
  // generated code (C# CS1061 from the emit dedup, Java same-path file
  // overwrite, TS duplicate `export interface`). Only distinct
  // declarations sharing a simple name are rejected.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "first", handler: "First" })
        op First(input: A.Foo): Out;

        @hostMethod({ name: "second", handler: "Second" })
        op Second(input: B.Foo): Out;
      }
      model Out { value: int64; }
      namespace A { model Foo { x: string; } }
      namespace B { model Foo { y: int64; } }
    `),
  );
  assertDiagnostic(result, "duplicate-model-name");
});

test("rejects two referenced result models sharing a simple name across namespaces", async () => {
  // Same collapse as the input case, exercised through the result DTO
  // source: two distinct `Res` shapes flatten into one generated class.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "first", handler: "First" })
        op First(input: In): A.Res;

        @hostMethod({ name: "second", handler: "Second" })
        op Second(input: In): B.Res;
      }
      model In { key: string; }
      namespace A { model Res { x: string; } }
      namespace B { model Res { y: int64; } }
    `),
  );
  assertDiagnostic(result, "duplicate-model-name");
});

test("rejects two list-item models sharing a simple name across namespaces", async () => {
  // List item element models become DTOs too (itemModelName), so two
  // distinct `Item` shapes in different namespaces collapse the same way.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "first", handler: "First" })
        op First(input: FirstIn): Out;

        @hostMethod({ name: "second", handler: "Second" })
        op Second(input: SecondIn): Out;
      }
      model FirstIn { items: A.Item[]; }
      model SecondIn { items: B.Item[]; }
      model Out { value: int64; }
      namespace A { model Item { x: string; } }
      namespace B { model Item { y: int64; } }
    `),
  );
  assertDiagnostic(result, "duplicate-model-name");
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

test("rejects a method apiLevel above the library apiLevel", async () => {
  // A method may not require a newer API level than its library: both the
  // Java validator (ValidationEngine.checkCompatibility) and the C#
  // runtime (SqliteHostRuntimeCore.Precheck) gate a payload's
  // requiredApiLevel against the LIBRARY level only. So a level-2 method
  // under a level-1 library is either dead (a correct requiredApiLevel:2
  // payload is skipped since 2 > 1) or a gate bypass (an under-declared
  // requiredApiLevel:1 payload runs it since 1 <= 1). The test fails if
  // that class of nonsensical manifest is ever allowed to compile again.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1 })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue", apiLevel: 2 })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "method-api-level-too-high");
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

test("rejects shared workspace table names that differ only by case", async () => {
  // SQLite resolves table names case-insensitively: "Shared_KV" and
  // "shared_kv" are the same table, so the second CREATE TABLE would
  // fail before any script runs.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, inputsTable: "Shared_KV", varsTable: "shared_kv" })
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

test("rejects a shared table name colliding with a derived call table case-insensitively", async () => {
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, queueTable: "CALL_GET_VALUE" })
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

test("rejects a column name that is not a snake_case identifier", async () => {
  // Configurable column identifiers are interpolated UNQUOTED into the
  // generated DDL (ddl.ts queue/parent/child tables), so a hyphenated name
  // like "call-id" yields a CREATE TABLE that fails at schema-creation time
  // in every language runtime. The validator fails loud at compile time
  // (docs/naming.md snake_case rule) instead of shipping a corrupt
  // manifest; the test fails if the SQL_NAME shape guard is removed.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, callIdColumn: "call-id" })
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

test("rejects an uppercase column name (guard covers the whole options list)", async () => {
  // The shape guard applies to every ...Column option, not just callId: an
  // uppercase messageColumn is not a valid snake_case SQL identifier and
  // would emit an invalid unquoted DDL identifier.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, messageColumn: "MessageBody" })
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

test("rejects a doneStatusValue of the reserved 'pending' queue status", async () => {
  // The queue defaults new rows to 'pending' (ddl.ts queue DDL) and the
  // runtime drain selects status='pending' (SqliteHostRuntimeCore
  // DrainPendingCalls) while marking done rows with status=doneValue, so a
  // doneValue of 'pending' leaves drained rows selectable -> re-drain /
  // duplicate handler execution, and the "done" mark never sticks. This
  // test fails if the check is ever weakened back to emptiness-only.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, doneStatusValue: "pending" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { value: int64; }
    `),
  );
  assertDiagnostic(result, "done-status-value-collision");
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

test("rejects column names differing only by case within a table set", async () => {
  // SQLite resolves column names case-insensitively, so methodColumn
  // "Status" and the default statusColumn "status" become ONE queue-table
  // column and the CREATE TABLE would fail before any script runs. The
  // exact-case distinctness set missed this; the test fails if the
  // distinctness comparison is reverted to exact case. ("Status" is also
  // not snake_case, so invalid-column-name fires too — this asserts the
  // case-insensitive distinctness specifically.)
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, methodColumn: "Status" })
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

test("rejects a row-identity column colliding case-insensitively with a derived field column", async () => {
  // A configured row-identity column that collides case-insensitively with
  // a derived field column also breaks CREATE TABLE: statusColumn
  // "RESULT_KEY" resolves to the same column as the derived result_key
  // (model Out { key } -> result_key). The exact-case row-identity check
  // missed this; the test fails if the lookup is reverted to exact case.
  const result = await compileSource(
    shell(`
      @hostLibrary({ apiLevel: 1, statusColumn: "RESULT_KEY" })
      interface Methods {
        @hostMethod({ name: "getValue", handler: "GetValue" })
        op GetValue(input: In): Out;
      }
      model In { key: string; }
      model Out { key: string; }
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
