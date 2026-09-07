import { strict as assert } from "node:assert";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { parseManifest, parseManifestUnchecked } from "../manifest.js";
import { serializeManifest } from "../manifest.js";
import { manifestFixturePath } from "./helpers.js";

/**
 * A manifest is a committed, hand-editable file that
 * docs/guides/getting-started.md tells users to feed to all three
 * language emitters, and every emitter funnels through parseManifest.
 * Each case here is a single edit that reached a different broken
 * artifact with no error at all before this check existed.
 */

const golden = readFileSync(manifestFixturePath, "utf8");

/** The committed manifest, parsed unchecked, as a mutable base. */
function base(): Record<string, any> {
  return JSON.parse(golden);
}

function expectProblem(manifest: unknown, fragment: string): void {
  assert.throws(
    () => parseManifest(JSON.stringify(manifest)),
    (error: Error) => {
      assert.equal(error.name, "ManifestValidationError");
      assert.ok(
        error.message.includes(fragment),
        `expected a problem mentioning ${JSON.stringify(fragment)}, got:\n${error.message}`,
      );
      return true;
    },
  );
}

test("the committed manifest passes the check unchanged", () => {
  const ir = parseManifest(golden);
  assert.equal(serializeManifest(ir), golden);
});

test("rejects a method with no handlerName", () => {
  // The C# emitter wrote `GetValuesResult undefined(GetValuesInput input);`
  // — a JS undefined as an interface member name.
  const m = base();
  delete m.methods[2].handlerName;
  expectProblem(m, "$.methods[2].handlerName");
});

test("rejects a non-integer library apiLevel", () => {
  // .ApiLevel(1.5) against a builder that takes an int: CS1503.
  const m = base();
  m.library.apiLevel = 1.5;
  expectProblem(m, "$.library.apiLevel");
});

test("rejects a duplicated method entry", () => {
  // The C# handler interface declared the same member twice, and the
  // runtime throws "Duplicate method name" at registration.
  const m = base();
  m.methods.push(JSON.parse(JSON.stringify(m.methods[0])));
  expectProblem(m, "duplicate method name");
});

test("rejects a method apiLevel above the library apiLevel", () => {
  // The frontend rejects this and says why: both the Java validator and
  // the C# runtime gate requiredApiLevel against the LIBRARY level only,
  // so a method above it is unreachable or a silent gate bypass. A
  // manifest edit re-introduced exactly that with nothing re-checking.
  const m = base();
  m.methods[1].apiLevel = 99;
  expectProblem(m, "exceeds the library apiLevel");
});

test("rejects an inverted inline arity", () => {
  // .Inline("fn_get_value", 5, 1) registers verbatim and the authoring
  // lint then rejects every call — the function is unusable, not absent.
  const m = base();
  const method = m.methods.find((x: any) => x.inline !== null);
  assert.ok(method, "fixture has no inline method");
  method.inline.minArgs = 5;
  method.inline.maxArgs = 1;
  expectProblem(m, "exceeds maxArgs");
});

test("rejects an inline maxArgs above the declared argument count", () => {
  const m = base();
  const method = m.methods.find((x: any) => x.inline !== null);
  method.inline.maxArgs = method.inline.args.length + 1;
  expectProblem(m, "declared argument(s)");
});

test("rejects two methods deriving the same call table", () => {
  const m = base();
  m.methods[1].callTable = m.methods[0].callTable;
  expectProblem(m, "duplicate table name");
});

test("rejects duplicate sqlNames within one shape", () => {
  const m = base();
  const shape = m.methods.find((x: any) => x.input.fields.length > 1).input;
  shape.fields[1].sqlName = shape.fields[0].sqlName;
  expectProblem(m, "duplicate sqlName");
});

test("rejects a shared table name SQLite refuses as an identifier", () => {
  // Every DDL generator interpolates this name unquoted, so
  // `CREATE TABLE select (...)` is a syntax error and the schema never
  // creates. The frontend rejects it at authoring time; a hand-edited
  // manifest reaches the emitters without passing the frontend at all.
  const m = base();
  m.queueTable.name = "select";
  expectProblem(m, "$.queueTable.name");
});

test("rejects a column name SQLite refuses as an identifier", () => {
  const m = base();
  m.columns.status = "order";
  expectProblem(m, "$.columns.status");
});

test("rejects a resolved method table SQLite refuses as an identifier", () => {
  // Method tables are derived from a prefix at authoring time, but the
  // manifest carries them resolved — and a prefix that JOINS into a
  // keyword ("in" + "dex") produces exactly this manifest.
  const m = base();
  m.methods[0].callTable = "index";
  expectProblem(m, "$.methods[0].callTable");
});

test("keeps a keyword SQLite does allow in identifier position", () => {
  // "action" is a SQLite keyword and the protocol's own default
  // actionColumn: the rule is the unusable subset, not the keyword list.
  const m = base();
  m.columns.message = "action";
  const ir = parseManifest(JSON.stringify(m));
  assert.equal(ir.columns.message, "action");
});

test("rejects an unknown scalar type", () => {
  const m = base();
  m.methods[0].input.fields[0].scalarType = "int128";
  expectProblem(m, "scalarType");
});

test("rejects an unknown top-level key", () => {
  const m = base();
  m.extra = true;
  expectProblem(m, "unknown top-level key");
});

test("rejects a wrong manifestVersion", () => {
  const m = base();
  m.manifestVersion = 2;
  expectProblem(m, "$.manifestVersion");
});

test("rejects an empty object", () => {
  expectProblem({}, "$.library");
});

test("reports every problem at once, each with its JSON path", () => {
  const m = base();
  m.library.apiLevel = 1.5;
  delete m.methods[2].handlerName;
  m.methods.push(JSON.parse(JSON.stringify(m.methods[0])));
  try {
    parseManifest(JSON.stringify(m));
    assert.fail("expected the check to throw");
  } catch (error) {
    const problems = (error as { problems: string[] }).problems;
    assert.ok(problems.length >= 3, `expected at least 3 problems, got ${problems.length}`);
    for (const path of ["$.library.apiLevel", "$.methods[2].handlerName"]) {
      assert.ok(
        problems.some((p) => p.startsWith(path)),
        `no problem for ${path} in:\n${problems.join("\n")}`,
      );
    }
  }
});

test("malformed JSON still fails as a JSON syntax error", () => {
  assert.throws(() => parseManifest("{ not json"), SyntaxError);
});

test("rejects a manifest object that repeats a key", () => {
  // JSON.parse resolves a duplicate as last-wins, so a merge conflict
  // resolved by keeping both `"callTablePrefix"` lines parsed clean and
  // emitted whichever one came second — while a reader that keeps the
  // first (or refuses) sees a different host. The manifest is the one
  // artifact all three languages agree on; it cannot have two readings.
  const withDuplicate = golden.replace(
    '"callTablePrefix": "call_",',
    '"callTablePrefix": "call_", "callTablePrefix": "legacy_",',
  );
  assert.notEqual(withDuplicate, golden);
  assert.throws(() => parseManifest(withDuplicate), SyntaxError);
});

test("rejects a duplicate key spelled with an escape", () => {
  const withDuplicate = golden.replace(
    '"callTablePrefix": "call_",',
    '"callTablePrefix": "call_", "\\u0063allTablePrefix": "legacy_",',
  );
  assert.notEqual(withDuplicate, golden);
  assert.throws(() => parseManifest(withDuplicate), SyntaxError);
});

test("parseManifestUnchecked keeps the old unvalidated behaviour", () => {
  const m = base();
  delete m.methods[2].handlerName;
  const ir = parseManifestUnchecked(JSON.stringify(m));
  assert.equal(ir.methods[2].handlerName, undefined);
});
