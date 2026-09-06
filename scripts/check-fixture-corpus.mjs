#!/usr/bin/env node
// Structural checks on the validator conformance corpus
// (fixtures/payloads/). The runners in java/ and typescript/ prove that
// each implementation reports exactly the codes a case expects; nothing
// proved anything about the corpus itself — that every fixture is
// reachable, that every case is single-fault, that every pinned code has
// a fixture at all. Wycheproof's linter has the same hole and ships nine
// orphaned flags today. Node >= 22, no dependencies.
//
// Usage:
//   node scripts/check-fixture-corpus.mjs [--fixtures <dir>]
//   node scripts/check-fixture-corpus.mjs --self-test

import { readdirSync, readFileSync, mkdtempSync, mkdirSync, writeFileSync, cpSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/**
 * Codes with no `invalid/` fixture, each with the reason there is none.
 * The list is the point: a code in here that a fixture DOES cover fails
 * the check and must be removed, the way Protobuf's conformance runner
 * fails when a test on its failure list starts passing. A gap that is
 * merely inconvenient does not belong here — write the fixture.
 */
const knownUncovered = [
  // Empty, and worth keeping that way. `method-api-level-too-high` was
  // the last entry: it had no fixture because every case bound to the
  // sample host, whose methods are all apiLevel 1. The per-case
  // `manifest` key and the one-method high-api host closed it.
];

/**
 * Codes the TypeScript authoring lint cannot report, with the reason.
 * Everything else must exist in all three registries below.
 */
const javaOnlyCodes = {
  "sql-prepare-error":
    "layer 3 opens a real SQLite and prepares each statement; the " +
    "authoring SDK has no engine (docs/validation.md)",
};

/**
 * Who reads the corpus. `expectations.json` names the two VALIDATORS on
 * each finding, because a finding is a validator verdict — the C# runtime
 * is not a validator and reports no codes. But it IS a consumer, and the
 * consumer nobody was counting: before the csharp checks below, a payload
 * added for a contract change was validated twice and executed zero times
 * unless someone remembered to name it in a C# test. Six valid payloads
 * had accumulated that way, both float ones among them.
 */
const VALIDATOR_IDS = ["java", "typescript"];
const CONSUMER_IDS = [...VALIDATOR_IDS, "csharp"];
const CASE_KEYS = new Set(["payload", "valid", "errors", "warnings", "manifest"]);
const REQUIRED_CASE_KEYS = ["payload", "valid", "errors", "warnings"];
const FINDING_KEYS = new Set(["code", "validators"]);

/** `| `code` | error | rule |` rows across every table in docs/validation.md. */
function codesFromDocs(text) {
  const codes = new Map();
  const row = /^\|\s*`([a-z0-9-]+)`\s*\|\s*(error|warning)\s*\|/gm;
  for (const m of text.matchAll(row)) codes.set(m[1], m[2]);
  return codes;
}

/** `public static final String X = "code";` in ValidationCodes.java. */
function codesFromJava(text) {
  const decl = /public\s+static\s+final\s+String\s+\w+\s*=\s*"([a-z0-9-]+)"\s*;/g;
  const wrapped = /public\s+static\s+final\s+String\s+\w+\s*=\s*\n\s*"([a-z0-9-]+)"\s*;/g;
  return new Set([...text.matchAll(decl), ...text.matchAll(wrapped)].map((m) => m[1]));
}

/** The `export type LintCode = "a" | "b" | …;` union in lint.ts. */
function codesFromTypeScript(text) {
  const block = /export type LintCode\s*=([^;]*);/.exec(text);
  if (!block) throw new Error("lint.ts: no `export type LintCode` union found");
  return new Set([...block[1].matchAll(/"([a-z0-9-]+)"/g)].map((m) => m[1]));
}

function diff(a, b) {
  return [...a].filter((x) => !b.has(x)).sort();
}

/**
 * @returns {string[]} one message per violation; empty means the corpus is sound.
 */
export function checkCorpus({
  uncovered = knownUncovered,
  fixturesDir = join(REPO_ROOT, "fixtures"),
  docsFile = join(REPO_ROOT, "docs/validation.md"),
  javaFile = join(
    REPO_ROOT,
    "java/sqlite-host-validator/src/main/java/io/sqlitehost/validator/ValidationCodes.java",
  ),
  typescriptFile = join(REPO_ROOT, "typescript/authoring-sdk/src/lint.ts"),
  csharpCoverageFile = join(REPO_ROOT, "csharp/SqliteHost.Tests/Fixtures/FixtureCoverage.cs"),
  csharpRunnerFiles = [
    join(REPO_ROOT, "csharp/SqliteHost.Tests/IntegrationFixtureTests.cs"),
    join(REPO_ROOT, "csharp/SqliteHost.Tests/InvalidFixtureEnvelopeTests.cs"),
  ],
} = {}) {
  const violations = [];
  const fail = (message) => violations.push(message);

  // ---- 4a. One registry, three spellings of it. -------------------------
  const documented = codesFromDocs(readFileSync(docsFile, "utf8"));
  const javaCodes = codesFromJava(readFileSync(javaFile, "utf8"));
  const tsCodes = codesFromTypeScript(readFileSync(typescriptFile, "utf8"));
  const docCodes = new Set(documented.keys());

  if (docCodes.size === 0) fail(`${docsFile}: no code tables found`);
  for (const code of diff(docCodes, javaCodes)) {
    fail(`registry parity: '${code}' is documented but missing from ValidationCodes.java`);
  }
  for (const code of diff(javaCodes, docCodes)) {
    fail(`registry parity: '${code}' is in ValidationCodes.java but has no row in docs/validation.md`);
  }
  const expectedTs = new Set([...docCodes].filter((c) => !(c in javaOnlyCodes)));
  for (const code of diff(expectedTs, tsCodes)) {
    fail(`registry parity: '${code}' is documented but missing from the LintCode union in lint.ts`);
  }
  for (const code of diff(tsCodes, expectedTs)) {
    const why = javaOnlyCodes[code];
    fail(
      why
        ? `registry parity: '${code}' is listed Java-only (${why}) but appears in the LintCode union`
        : `registry parity: '${code}' is in the LintCode union but has no row in docs/validation.md`,
    );
  }

  // ---- 1. Shape. -------------------------------------------------------
  const payloadsDir = join(fixturesDir, "payloads");
  const expectations = JSON.parse(readFileSync(join(payloadsDir, "expectations.json"), "utf8"));
  const cases = expectations.cases;
  if (!Array.isArray(cases) || cases.length === 0) {
    fail("expectations.json: `cases` must be a non-empty array");
    return violations;
  }

  const referenced = new Set();
  const coveredErrors = new Set();
  const coveredWarnings = new Set();

  for (const entry of cases) {
    const where = `expectations.json[${entry.payload ?? "?"}]`;
    for (const key of Object.keys(entry)) {
      if (!CASE_KEYS.has(key)) fail(`${where}: unknown key '${key}'`);
    }
    for (const key of REQUIRED_CASE_KEYS) {
      if (!(key in entry)) fail(`${where}: missing required key '${key}'`);
    }
    if (typeof entry.payload !== "string" || !/^(valid|invalid)\/[\w.-]+\.json$/.test(entry.payload ?? "")) {
      fail(`${where}: 'payload' must be a path under valid/ or invalid/`);
      continue;
    }
    if (typeof entry.valid !== "boolean") fail(`${where}: 'valid' must be a boolean`);
    if (entry.valid !== entry.payload.startsWith("valid/")) {
      fail(`${where}: 'valid' contradicts the directory the fixture lives in`);
    }
    referenced.add(entry.payload);

    let payloadOk = true;
    try {
      JSON.parse(readFileSync(join(payloadsDir, entry.payload), "utf8"));
    } catch (e) {
      payloadOk = false;
      fail(`${where}: payload does not resolve or is not JSON (${e.message})`);
    }
    if (entry.manifest !== undefined) {
      if (typeof entry.manifest !== "string") {
        fail(`${where}: 'manifest' must be a string path`);
      } else {
        try {
          JSON.parse(readFileSync(resolve(payloadsDir, entry.manifest), "utf8"));
        } catch (e) {
          fail(`${where}: 'manifest' does not resolve or is not JSON (${e.message})`);
        }
      }
    }

    for (const [severity, list] of [
      ["error", entry.errors],
      ["warning", entry.warnings],
    ]) {
      if (!Array.isArray(list)) {
        fail(`${where}: '${severity}s' must be an array`);
        continue;
      }
      for (const finding of list) {
        for (const key of Object.keys(finding)) {
          if (!FINDING_KEYS.has(key)) fail(`${where}: unknown key '${key}' on a ${severity} entry`);
        }
        const code = finding.code;
        if (!documented.has(code)) {
          fail(`${where}: unknown ${severity} code '${code}' — not in docs/validation.md`);
        } else if (documented.get(code) !== severity) {
          fail(
            `${where}: '${code}' is documented as a ${documented.get(code)} but listed under '${severity}s'`,
          );
        }
        const validators = finding.validators;
        if (!Array.isArray(validators) || validators.length === 0) {
          fail(`${where}: '${code}' must name at least one validator`);
          continue;
        }
        for (const id of validators) {
          if (!VALIDATOR_IDS.includes(id)) fail(`${where}: unknown validator id '${id}'`);
        }
        if (new Set(validators).size !== validators.length) {
          fail(`${where}: '${code}' repeats a validator id`);
        }
        (severity === "error" ? coveredErrors : coveredWarnings).add(code);
      }
    }

    // ---- 3. Single fault. ---------------------------------------------
    if (!payloadOk) continue;
    if (entry.valid) {
      if (entry.errors.length > 0) fail(`${where}: a valid case must expect no errors`);
    } else {
      // `sql-prepare-error` corroborates a lint finding rather than being
      // a second fault: layer 3 compiles the very statement the lint just
      // rejected. Anything beyond one lint code plus that means the
      // fixture carries two faults — split it (docs/validation.md).
      const prepare = entry.errors.filter((f) => f.code === "sql-prepare-error");
      const lint = entry.errors.filter((f) => f.code !== "sql-prepare-error");
      if (entry.errors.length === 0) {
        fail(`${where}: an invalid case must expect at least one error`);
      } else if (lint.length > 1) {
        fail(
          `${where}: invalid fixtures are single-fault, but this expects ` +
            `${lint.length} lint codes (${lint.map((f) => f.code).join(", ")}) — split the fixture`,
        );
      } else if (prepare.length > 1) {
        fail(`${where}: 'sql-prepare-error' is expected ${prepare.length} times`);
      }
    }
  }

  // ---- 2. Bidirectional orphans. ---------------------------------------
  for (const dir of ["valid", "invalid"]) {
    let files;
    try {
      files = readdirSync(join(payloadsDir, dir)).filter((f) => f.endsWith(".json"));
    } catch {
      fail(`fixtures/payloads/${dir}/ does not exist`);
      continue;
    }
    for (const file of files.sort()) {
      const rel = `${dir}/${file}`;
      if (!referenced.has(rel)) {
        fail(`orphan fixture: ${rel} has no entry in expectations.json`);
      }
    }
  }
  if (new Set(cases.map((c) => c.payload)).size !== cases.length) {
    fail("expectations.json: two cases name the same payload");
  }

  // ---- 4b. Every code has a fixture. ------------------------------------
  const uncoveredReasons = new Map(uncovered.map((e) => [e.code, e.reason]));
  for (const entry of uncovered) {
    if (!entry || typeof entry.code !== "string" || typeof entry.reason !== "string") {
      fail("knownUncovered entries must be { code, reason } with string values");
      continue;
    }
    if (!documented.has(entry.code)) {
      fail(`knownUncovered names '${entry.code}', which is not a documented code`);
    }
  }
  for (const [code, severity] of documented) {
    // An error code is covered by an invalid case; a warning code by any
    // case, since warnings are what valid fixtures are there to pin.
    const covered =
      severity === "error" ? coveredErrors.has(code) : coveredWarnings.has(code);
    if (covered && uncoveredReasons.has(code)) {
      fail(
        `'${code}' is listed in knownUncovered but a fixture now covers it — ` +
          `remove it from knownUncovered`,
      );
    } else if (!covered && !uncoveredReasons.has(code)) {
      fail(
        `'${code}' is pinned in docs/validation.md but no fixture expects it — ` +
          `add one, or add it to knownUncovered with a reason`,
      );
    }
  }

  // ---- 5. The runtime consumer. -----------------------------------------
  checkRuntimeConsumer({ fail, payloadsDir, csharpCoverageFile, csharpRunnerFiles });

  return violations;
}

/**
 * `csharp`, the third consumer: does the runtime that has to EXECUTE these
 * payloads actually see each one?
 *
 * The evidence is the C# test source, because that is where the decision
 * lives. `FixtureCoverage.cs` holds marker-delimited tables and the two
 * suites enumerate the fixture directories, so coverage is opt-OUT: a new
 * payload runs unless a table excuses it. This function reads the tables
 * back and refuses the two ways that can rot — a fixture no table decided,
 * and a table naming a fixture that is gone — plus the way the whole
 * mechanic can be quietly undone, a runner that stopped reading the
 * directory.
 */
function parseCsharpTable(source, marker, where, fail) {
  const open = source.indexOf(`>>> ${marker}`);
  const close = source.indexOf(`<<< ${marker}`);
  if (open === -1 || close === -1 || close < open) {
    fail(`${where}: the '${marker}' table markers are missing or out of order`);
    return null;
  }
  const body = source.slice(open, close);
  const entries = new Map();
  for (const m of body.matchAll(/\{\s*"([\w.-]+\.json)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\}/g)) {
    if (entries.has(m[1])) fail(`${where}: '${marker}' names ${m[1]} twice`);
    entries.set(m[1], m[2]);
  }
  return entries;
}

function checkRuntimeConsumer({ fail, payloadsDir, csharpCoverageFile, csharpRunnerFiles }) {
  let source;
  try {
    source = readFileSync(csharpCoverageFile, "utf8");
  } catch (e) {
    fail(`csharp coverage: ${csharpCoverageFile} does not resolve (${e.message})`);
    return;
  }

  const engineFloors = (() => {
    const open = source.indexOf(">>> valid-engine-floors");
    const close = source.indexOf("<<< valid-engine-floors");
    if (open === -1 || close === -1 || close < open) {
      fail(`${csharpCoverageFile}: the 'valid-engine-floors' table markers are missing or out of order`);
      return new Map();
    }
    return new Map(
      [...source.slice(open, close).matchAll(/\{\s*"([\w.-]+\.json)"\s*,\s*(\d+)\s*\}/g)]
        .map((m) => [m[1], Number(m[2])]),
    );
  })();

  const tables = {
    "valid-skip-list": parseCsharpTable(source, "valid-skip-list", csharpCoverageFile, fail),
    "invalid-envelope-refusals": parseCsharpTable(source, "invalid-envelope-refusals", csharpCoverageFile, fail),
    "invalid-reader-refusals": parseCsharpTable(source, "invalid-reader-refusals", csharpCoverageFile, fail),
    "invalid-not-envelope": parseCsharpTable(source, "invalid-not-envelope", csharpCoverageFile, fail),
  };
  if (Object.values(tables).some((t) => t === null)) return;

  const listing = (dir) => {
    try {
      return new Set(readdirSync(join(payloadsDir, dir)).filter((f) => f.endsWith(".json")));
    } catch {
      return null;
    }
  };

  // valid/: running it is the default, so the only rot is a stale excuse.
  const validFiles = listing("valid");
  if (validFiles) {
    for (const name of [...tables["valid-skip-list"].keys()].sort()) {
      if (!validFiles.has(name)) {
        fail(`csharp coverage: valid-skip-list names '${name}', which no longer exists in fixtures/payloads/valid/`);
      }
    }
    for (const name of [...engineFloors.keys()].sort()) {
      if (!validFiles.has(name)) {
        fail(`csharp coverage: valid-engine-floors names '${name}', which no longer exists in fixtures/payloads/valid/`);
      }
    }
    if (tables["valid-skip-list"].size >= validFiles.size) {
      fail("csharp coverage: the valid skip list excuses every valid fixture — the runtime executes none of them");
    }
  }

  // invalid/: every fixture needs a decision, in exactly one table.
  const invalidFiles = listing("invalid");
  if (invalidFiles) {
    const decided = new Map();
    for (const key of ["invalid-envelope-refusals", "invalid-reader-refusals", "invalid-not-envelope"]) {
      for (const [name, reason] of tables[key]) {
        if (decided.has(name)) {
          fail(`csharp coverage: '${name}' appears in both ${decided.get(name)} and ${key}`);
          continue;
        }
        decided.set(name, key);
        if (!invalidFiles.has(name)) {
          fail(`csharp coverage: ${key} names '${name}', which no longer exists in fixtures/payloads/invalid/`);
        }
        if (!reason.trim()) fail(`csharp coverage: '${name}' in ${key} has a blank reason`);
      }
    }
    for (const name of [...invalidFiles].sort()) {
      if (!decided.has(name)) {
        fail(
          `csharp coverage: invalid/${name} has no entry in csharp/SqliteHost.Tests/Fixtures/FixtureCoverage.cs — ` +
            `add it to EnvelopeRefusals with the ErrorCode the runtime reports, or to NotEnvelopeFaults ` +
            `with the reason the envelope layer cannot see the fault`,
        );
      }
    }
    // An envelope refusal names a runtime error code, which docs/errors.md
    // pins. A typo there would make the C# test assert a code nothing emits.
    const runtimeCodes = new Set(
      [...readFileSync(join(REPO_ROOT, "docs/errors.md"), "utf8").matchAll(
        /^\|\s*`([a-z0-9-]+)`\s*\|\s*(?:Completed|Skipped\w+|Failed\w+)\s*\|/gm,
      )].map((m) => m[1]),
    );
    for (const [name, code] of tables["invalid-envelope-refusals"]) {
      if (!runtimeCodes.has(code)) {
        fail(`csharp coverage: '${name}' expects ErrorCode '${code}', which has no row in docs/errors.md`);
      }
    }
  }

  // The mechanic itself: a runner that stopped enumerating the directory
  // would keep every check above green while covering nothing new.
  for (const file of csharpRunnerFiles) {
    let runner;
    try {
      runner = readFileSync(file, "utf8");
    } catch (e) {
      fail(`csharp coverage: ${file} does not resolve (${e.message})`);
      continue;
    }
    if (!runner.includes("FixtureCoverage.ListPayloads(")) {
      fail(
        `csharp coverage: ${file} no longer enumerates the fixture directory ` +
          `(FixtureCoverage.ListPayloads) — coverage would be opt-in again`,
      );
    }
  }
}

// ---------------------------------------------------------------------------
// Self-test: each of the four checks must actually fire.
// ---------------------------------------------------------------------------

function selfTest() {
  const scratch = mkdtempSync(join(tmpdir(), "sqlitehost-corpus-"));
  const results = [];

  const scenario = (name, mutate, expectFragment) => {
    const dir = join(scratch, name);
    mkdirSync(dir, { recursive: true });
    cpSync(join(REPO_ROOT, "fixtures"), dir, { recursive: true });
    const expectationsPath = join(dir, "payloads/expectations.json");
    const expectations = JSON.parse(readFileSync(expectationsPath, "utf8"));
    mutate(expectations, dir);
    writeFileSync(expectationsPath, JSON.stringify(expectations, null, 2) + "\n");
    const violations = checkCorpus({ fixturesDir: dir });
    const hit = violations.some((v) => v.includes(expectFragment));
    results.push({ name, hit, violations });
    console.log(`${hit ? "ok  " : "FAIL"} ${name}`);
    if (!hit) console.log(`     expected a violation containing: ${expectFragment}\n     got: ${JSON.stringify(violations, null, 2)}`);
  };

  scenario("1. shape: an unknown key is rejected", (e) => {
    e.cases[0].severity = "error";
  }, "unknown key 'severity'");

  scenario("1. shape: an unknown validator id is rejected", (e) => {
    const c = e.cases.find((x) => !x.valid);
    c.errors[0].validators = ["kotlin"];
  }, "unknown validator id 'kotlin'");

  scenario("1. shape: an unknown code is rejected", (e) => {
    const c = e.cases.find((x) => !x.valid);
    c.errors[0].code = "not-a-real-code";
  }, "unknown error code 'not-a-real-code'");

  scenario("1. shape: a payload that does not resolve is rejected", (e) => {
    e.cases[0].payload = "valid/does-not-exist.json";
  }, "does not resolve");

  scenario("2. orphans: a fixture with no entry is rejected", (e, dir) => {
    writeFileSync(join(dir, "payloads/invalid/orphaned.json"), "{}\n");
  }, "orphan fixture: invalid/orphaned.json");

  scenario("2. orphans: an entry with no fixture is rejected", (e) => {
    e.cases.push({ payload: "invalid/never-written.json", valid: false, errors: [], warnings: [] });
  }, "invalid/never-written.json");

  scenario("3. single fault: two lint codes on one invalid case", (e) => {
    const c = e.cases.find((x) => !x.valid && x.errors.length === 1);
    c.errors.push({ code: "duplicate-step-id", validators: ["java", "typescript"] });
  }, "single-fault");

  scenario("3. single fault: an invalid case expecting no error", (e) => {
    e.cases.find((x) => !x.valid).errors = [];
  }, "must expect at least one error");

  scenario("3. single fault: a valid case expecting an error", (e) => {
    e.cases.find((x) => x.valid).errors = [
      { code: "duplicate-step-id", validators: ["java"] },
    ];
  }, "must expect no errors");

  scenario("4. coverage: a code every fixture stopped expecting", (e) => {
    for (const c of e.cases) {
      c.errors = c.errors.filter((f) => f.code !== "embedded-nul");
    }
  }, "'embedded-nul' is pinned in docs/validation.md but no fixture expects it");

  // The Protobuf mechanic: an entry that IS covered must fail loudly, so
  // the list can never quietly outlive the gap it describes.
  {
    const name = "4. coverage: a knownUncovered entry a fixture covers";
    const violations = checkCorpus({
      uncovered: [...knownUncovered, { code: "embedded-nul", reason: "deliberately stale" }],
    });
    const hit = violations.some((v) => v.includes("remove it from knownUncovered"));
    results.push({ name, hit, violations });
    console.log(`${hit ? "ok  " : "FAIL"} ${name}`);
    if (!hit) console.log(`     got: ${JSON.stringify(violations, null, 2)}`);
  }

  // ---- 5. the runtime consumer ------------------------------------------
  // These need a mutated COPY of the C# sources rather than of
  // expectations.json, so they build their own scenario.
  const coverageFile = join(REPO_ROOT, "csharp/SqliteHost.Tests/Fixtures/FixtureCoverage.cs");
  const runnerFiles = [
    join(REPO_ROOT, "csharp/SqliteHost.Tests/IntegrationFixtureTests.cs"),
    join(REPO_ROOT, "csharp/SqliteHost.Tests/InvalidFixtureEnvelopeTests.cs"),
  ];
  const csharpScenario = (name, { fixtures, coverage, runner }, expectFragment) => {
    const dir = join(scratch, name.replace(/[^\w.-]+/g, "_"));
    mkdirSync(dir, { recursive: true });
    cpSync(join(REPO_ROOT, "fixtures"), dir, { recursive: true });
    if (fixtures) fixtures(dir);
    const coveragePath = join(dir, "FixtureCoverage.cs");
    writeFileSync(coveragePath, (coverage ?? ((t) => t))(readFileSync(coverageFile, "utf8")));
    const runnerPaths = runnerFiles.map((f, i) => {
      const out = join(dir, `runner-${i}.cs`);
      writeFileSync(out, (runner ?? ((t) => t))(readFileSync(f, "utf8")));
      return out;
    });
    const violations = checkCorpus({
      fixturesDir: dir,
      csharpCoverageFile: coveragePath,
      csharpRunnerFiles: runnerPaths,
    });
    const hit = violations.some((v) => v.includes(expectFragment));
    results.push({ name, hit, violations });
    console.log(`${hit ? "ok  " : "FAIL"} ${name}`);
    if (!hit) {
      console.log(`     expected a violation containing: ${expectFragment}\n     got: ${JSON.stringify(violations, null, 2)}`);
    }
  };

  csharpScenario(
    "5. csharp: a new invalid fixture no C# table decided",
    {
      fixtures: (dir) => {
        // Orphan-safe: give it an expectations entry, so the ONLY thing
        // missing is the runtime decision.
        const p = join(dir, "payloads/expectations.json");
        const e = JSON.parse(readFileSync(p, "utf8"));
        e.cases.push({
          payload: "invalid/brand-new-rule.json",
          valid: false,
          errors: [{ code: "duplicate-step-id", validators: ["java", "typescript"] }],
          warnings: [],
        });
        writeFileSync(p, JSON.stringify(e, null, 2) + "\n");
        writeFileSync(join(dir, "payloads/invalid/brand-new-rule.json"), "{}\n");
      },
    },
    "invalid/brand-new-rule.json has no entry",
  );

  csharpScenario(
    "5. csharp: a table naming a fixture that is gone",
    { coverage: (t) => t.replace('"empty-statements.json"', '"deleted-long-ago.json"') },
    "names 'deleted-long-ago.json', which no longer exists",
  );

  csharpScenario(
    "5. csharp: an envelope refusal expecting a code docs/errors.md does not pin",
    { coverage: (t) => t.replace('{ "empty-statements.json", "invalid-script" }', '{ "empty-statements.json", "invalid-scrpt" }') },
    "which has no row in docs/errors.md",
  );

  csharpScenario(
    "5. csharp: a runner that stopped enumerating the directory",
    { runner: (t) => t.replace(/FixtureCoverage\.ListPayloads\(/g, "HardCodedList(") },
    "no longer enumerates the fixture directory",
  );

  rmSync(scratch, { recursive: true, force: true });

  const failed = results.filter((r) => !r.hit);
  if (failed.length > 0) {
    console.error(`\nSELF-TEST FAILED (${failed.length} scenario(s) did not fire)`);
    process.exit(1);
  }
  console.log(`\nSELF-TEST GREEN (${results.length} scenarios)`);
}

// ---------------------------------------------------------------------------

const args = process.argv.slice(2);
if (args.includes("--self-test")) {
  selfTest();
} else {
  const at = args.indexOf("--fixtures");
  const options = at === -1 ? {} : { fixturesDir: resolve(args[at + 1]) };
  const violations = checkCorpus(options);
  if (violations.length > 0) {
    console.error("FIXTURE CORPUS CHECK FAILED\n");
    for (const v of violations) console.error(`  - ${v}`);
    console.error(`\n${violations.length} violation(s).`);
    process.exit(1);
  }
  console.log(`FIXTURE CORPUS OK (consumers: ${CONSUMER_IDS.join(", ")})`);
}
