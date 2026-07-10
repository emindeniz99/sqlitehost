import { test } from "node:test";
import { assertDiagnostic, compileSource } from "./helpers.js";

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
      model In { ratio: float64; }
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
