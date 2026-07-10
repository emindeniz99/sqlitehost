import { strict as assert } from "node:assert";
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import {
  compile,
  NodeHost,
  type Interface,
  type Model,
  type Program,
  type Scalar,
  type Union,
} from "@typespec/compiler";
import {
  getHostLibraryInterfaces,
  getHostLibraryOptions,
  getHostMethodOptions,
  getSqlName,
} from "../index.js";

const packageRoot = resolve(fileURLToPath(import.meta.url), "../../..");

let counter = 0;

async function compileSource(source: string): Promise<Program> {
  const dir = join(packageRoot, ".tsp-output", "library-tests");
  mkdirSync(dir, { recursive: true });
  const file = join(dir, `case-${process.pid}-${counter++}.tsp`);
  writeFileSync(file, source);
  try {
    return await compile(NodeHost, file, { emit: [] });
  } finally {
    rmSync(file, { force: true });
  }
}

function diagnosticCodes(program: Program): string[] {
  return program.diagnostics.map((d) => d.code);
}

function assertDiagnostic(program: Program, code: string): void {
  const full = `@sqlite-host/typespec/${code}`;
  assert.ok(
    diagnosticCodes(program).includes(full),
    `expected ${full}, got: ${diagnosticCodes(program).join(", ") || "(none)"}`,
  );
}

const VALID_LIBRARY = `
  import "@sqlite-host/typespec";
  using SqliteHost;
  namespace Test;

  @hostLibrary({ apiLevel: 2, callTablePrefix: "req_" })
  interface Methods {
    @hostMethod({ name: "getValue", handler: "GetValue", apiLevel: 1 })
    op GetValue(input: In): Out;
  }

  model In {
    @sqlName("the_key")
    key: string;
  }

  model Out { value: int64; }
`;

test("@hostLibrary records apiLevel and only the provided naming keys", async () => {
  const program = await compileSource(VALID_LIBRARY);
  assert.deepEqual(diagnosticCodes(program), []);
  const interfaces = getHostLibraryInterfaces(program);
  assert.equal(interfaces.length, 1);
  const options = getHostLibraryOptions(program, interfaces[0]);
  assert.deepEqual(options, { apiLevel: 2, callTablePrefix: "req_" });
});

test("@hostMethod records name, handler, and optional apiLevel", async () => {
  const program = await compileSource(VALID_LIBRARY);
  const [iface] = getHostLibraryInterfaces(program);
  const op = iface.operations.get("GetValue")!;
  assert.deepEqual(getHostMethodOptions(program, op), {
    name: "getValue",
    handler: "GetValue",
    apiLevel: 1,
  });
});

test("@sqlName records the override and leaves other properties untouched", async () => {
  const program = await compileSource(VALID_LIBRARY);
  const [model] = program.resolveTypeReference("Test.In");
  assert.ok(model);
  const key = (model as Model).properties.get("key")!;
  assert.equal(getSqlName(program, key), "the_key");
  const [out] = program.resolveTypeReference("Test.Out");
  const value = (out as Model).properties.get("value")!;
  assert.equal(getSqlName(program, value), undefined);
});

test("rejects a non-integer or non-positive api level", async () => {
  const program = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 0 })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }
    model In { key: string; }
    model Out { value: int64; }
  `);
  assertDiagnostic(program, "invalid-api-level");
});

test("rejects an invalid protocol method name", async () => {
  const program = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "get value", handler: "GetValue" })
      op GetValue(input: In): Out;
    }
    model In { key: string; }
    model Out { value: int64; }
  `);
  assertDiagnostic(program, "invalid-method-name");
});

test("rejects an invalid handler identifier", async () => {
  const program = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "Get-Value" })
      op GetValue(input: In): Out;
    }
    model In { key: string; }
    model Out { value: int64; }
  `);
  assertDiagnostic(program, "invalid-handler-name");
});

test("rejects a non-snake_case @sqlName", async () => {
  const program = await compileSource(`
    import "@sqlite-host/typespec";
    using SqliteHost;
    namespace Test;

    @hostLibrary({ apiLevel: 1 })
    interface Methods {
      @hostMethod({ name: "getValue", handler: "GetValue" })
      op GetValue(input: In): Out;
    }
    model In {
      @sqlName("TheKey")
      key: string;
    }
    model Out { value: int64; }
  `);
  assertDiagnostic(program, "invalid-sql-name");
});

test("script envelope models are defined under SqliteHost.Protocol", async () => {
  const program = await compileSource(`
    import "@sqlite-host/typespec";
  `);
  assert.deepEqual(diagnosticCodes(program), []);

  const [script] = program.resolveTypeReference("SqliteHost.Protocol.Script");
  assert.ok(script, "Script model missing");
  const scriptModel = script as Model;
  assert.deepEqual(
    [...scriptModel.properties.keys()],
    [
      "engine",
      "scriptId",
      "requiredApiLevel",
      "requiredFeatures",
      "requiredMethods",
      "inputs",
      "steps",
    ],
  );
  assert.equal(scriptModel.properties.get("engine")!.optional, false);
  assert.equal(scriptModel.properties.get("scriptId")!.optional, true);
  assert.equal(scriptModel.properties.get("steps")!.optional, false);

  for (const name of ["RuntimeInput", "Step", "Statement"]) {
    const [model] = program.resolveTypeReference(`SqliteHost.Protocol.${name}`);
    assert.ok(model, `${name} model missing`);
  }

  // Float bindings carry a finite JSON number typed as the matching
  // TypeSpec float scalar (docs/script-envelope.md: no string form).
  for (const scalar of ["float32", "float64"]) {
    const name = `Float${scalar.slice(5)}Binding`;
    const [model] = program.resolveTypeReference(`SqliteHost.Protocol.${name}`);
    assert.ok(model, `${name} model missing`);
    const value = (model as Model).properties.get("value")!;
    assert.equal(value.type.kind, "Scalar");
    assert.equal((value.type as Scalar).name, scalar);
  }

  const [binding] = program.resolveTypeReference(
    "SqliteHost.Protocol.BindingValue",
  );
  assert.ok(binding, "BindingValue union missing");
  assert.equal(binding!.kind, "Union");
  assert.deepEqual(
    [...(binding as Union).variants.keys()],
    [
      "nullValue",
      "int32Value",
      "int64Value",
      "boolValue",
      "textValue",
      "blobValue",
      "float32Value",
      "float64Value",
    ],
  );
});

test("the authoritative sample compiles without diagnostics", async () => {
  const sample = resolve(
    packageRoot,
    "../examples/sample-host-methods.tsp",
  );
  const program = await compile(NodeHost, sample, { emit: [] });
  assert.deepEqual(diagnosticCodes(program), []);
  const interfaces = getHostLibraryInterfaces(program);
  assert.equal(interfaces.length, 1);
  assert.equal((interfaces[0] as Interface).name, "GameHostMethods");
});
