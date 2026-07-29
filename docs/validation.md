# Validation layers

Four layers (plan §24). The validator is an authoring correctness tool,
not a security sandbox.

## Where enforcement lives — and where it deliberately does not

The engine-portability and statement-denylist rules below are
**authoring-time only**: they are implemented in the Java validator and
the TypeScript authoring lint, and in **nothing else**. The C# runtime
and the Unity package contain no check for any of them and did not grow
a byte for this.

That is a deliberate trade-off, and it is only sound because of the
stated threat model: *our backend authors the scripts, and the validator
gates them before publication.* A script reaches a device only after
passing the validator, so the gate is the publication pipeline, not the
client. Spending client bytes to re-check what the pipeline already
proved would buy nothing against the threat we actually have, while
costing exactly the binary budget the whole project exists to protect
(`docs/reports/il2cpp-size-report.md`).

The cost of that choice, stated plainly:

- **A payload that never passed the validator is not covered.** The
  runtime will happily execute `PRAGMA writable_schema=ON`, `ATTACH`, a
  `BEGIN`/`ROLLBACK` pair, or an `INSERT` into a result table — nothing
  in `csharp/` rejects any of them. Anyone building a path that feeds
  scripts to the runtime without validating first is outside this
  design, and `docs/adapter-contract.md` is where that obligation
  belongs.
- **These lints are not a sandbox.** They raise the cost of an accident,
  not of an attack; a hostile author who controls the payload *and* the
  delivery path is not in scope for v1.

If the threat model ever changes — third-party or player-supplied
scripts, a file-backed workspace exposed to untrusted payloads — the
honest fix is a runtime authorizer (`sqlite3_set_authorizer`), not a
bigger lint table. That is a different project with a real binary cost,
and it should be decided as such rather than arrived at by accretion.

## 1. TypeSpec model validation

The `@sqlite-host/typespec` library + frontend reject at compile time:
unsupported top-level method shapes (input/output must be objects),
unsupported scalar types, nested objects, nested lists, unions/maps,
duplicate method names, duplicate SQL names, duplicate derived
table/column names, duplicate DTO/model simple names across namespaces,
non-snake_case or case-colliding column names, a doneStatusValue equal
to the reserved `pending` queue sentinel, missing/invalid api level, a
method apiLevel exceeding the library apiLevel, invalid handler names,
invalid or empty list item shapes, host interfaces declared outside
any namespace.

## 2. Cross-language golden validation

The manifest emitter serializes the IR canonically; C#, Java, and
TypeScript artifacts are tested against the same committed fixtures
(`fixtures/manifests`, `fixtures/schemas`). See `docs/testing.md`.

## 3. Prepare-only SQLite validation (Java, `sqlite-host-jdbc`)

Opens an in-memory SQLite database, creates the generated schema,
**prepares** every script statement (compile only — catches grammar
errors, missing tables/columns, unsupported functions), and finalizes
without stepping. Reported as `sql-prepare-error`.

### Prepare-only is not a floor check — and cannot become one

This layer runs on whatever engine `org.xerial:sqlite-jdbc` bundles
(pinned in `java/pom.xml`), currently **3.45.3**, while the plan's
default contract floor is **3.19.3**. Everything added across those 26
minor versions therefore *compiles clean here* and fails on a device at
the floor. Pinning the driver down was considered and rejected, on three
counts:

1. **The floor is per-host data, the driver is one build constant.**
   `library.minSqliteVersionNumber` is a manifest field: a host may
   raise it with `@hostLibrary({minSqliteVersion: "3.35.0"})`. One Maven
   pin cannot track a value that differs per validated manifest, and the
   validator is a single artifact that must validate all of them.
2. **Two engines cannot coexist on one classpath.** Old xerial releases
   are published (`3.19.3` exists on Maven Central), but every one of
   them ships the same `org.sqlite.JDBC` class and its own bundled
   native library — so "prepare on the manifest's floor" cannot be done
   by adding versions, only by spawning an out-of-process matrix.
3. **Pinning down would trade a known gap for an unknown one.** A 2017
   driver predates current platform natives and JDK support; the CI
   surface it validates would no longer be the surface CI runs on.

The compatibility matrix (`tests/compatibility-sqlite/run-matrix.sh`)
does build and cache pinned amalgamations, but it drives the **C#**
suite through a native dynamic-provider override — it is not a JDBC
path, and it measures the runtime and generated SQL, not arbitrary
script SQL.

So the version half of the contract is enforced **statically** instead,
by the engine-portability lints below, which compare script SQL against
`library.minSqliteVersionNumber` with no engine at all. Read this
layer's guarantee precisely: *prepare-only proves the SQL compiles
somewhere, never that it compiles on the floor.* The enumerated
above-floor surface lives in `docs/sqlite-surface.md`.

## 4. Script semantic lint

Static rules over the parsed script + manifest. Error codes are pinned
here and asserted by `fixtures/payloads/expectations.json`; the
`validators` field there says which implementations must catch each
code (`java` = full engine, `typescript` = static authoring subset).

### Structural

| Code | Severity | Rule |
|---|---|---|
| `invalid-envelope` | error | missing/empty required envelope fields (including a step whose `statements` list is empty or missing) |
| `duplicate-step-id` | error | step ids must be unique |
| `required-api-level-too-high` | error | `requiredApiLevel` > manifest apiLevel |
| `method-api-level-too-high` | error | a used method (call-table INSERT or inline function invocation) has `apiLevel` > the script's `requiredApiLevel` — the script under-declares the API level it depends on |
| `unknown-required-feature` | error | feature not in manifest `library.features` |
| `unknown-required-method` | error | method not in manifest |
| `duplicate-input-name` | error | two `inputs` entries share a name |

### Bindings

| Code | Severity | Rule |
|---|---|---|
| `missing-binding` | error | SQL parameter with no binding |
| `unused-binding` | error | binding not referenced in SQL |
| `binding-type-mismatch` | error | for `INSERT INTO <call table or input list child table> (cols…) VALUES (…)` where a parameter feeds a known column: binding type must be compatible with the column's scalar type (`string`←text, `bytes`←blob, `boolean`←bool, `int32`←int32, `int64`←int32/int64, `float32`←float32, `float64`←float64/float32, `call_id`←text; integer bindings do NOT coerce into float columns and vice versa; optional columns also accept null) |
| `mixed-prefix-binding` | warning | the same bare name is used through more than one prefix form (`:v` and `$v`) in one statement — supported by the runtime (one binding feeds all forms) but usually an authoring accident; use `:name` consistently |
| `positional-parameter` | error | SQL uses a positional (`?` / `?N`) parameter; v1 supports named parameters only (`:name` / `@name` / `$name`, docs/script-envelope.md) |

### Host-call usage

| Code | Severity | Rule |
|---|---|---|
| `implicit-column-list` | error | INSERT into a call table or call/result child table must have an explicit column list |
| `undeclared-method-use` | error | script INSERTs into a call table whose method is not in `requiredMethods` |
| `unused-required-method` | warning | `requiredMethods` entry whose call table is never written AND whose inline function is never invoked |
| `duplicate-call-id` | error | two INSERTs emit the same statically-resolvable `call_id` for the same call table |
| `list-child-later-step` | error | child list rows emitted in a different (later) step than their parent call row; parents and children must be colocated (an intentionally empty list is fine) |
| `list-child-without-parent` | error | child list rows whose `call_id` has no parent insert |

### Inline functions (feature `inlineFunctions`)

| Code | Severity | Rule |
|---|---|---|
| `undeclared-feature-use` | error | the script invokes a manifest inline function but `requiredFeatures` lacks `inlineFunctions` |
| `unknown-function` | error | an identifier call matching the host's `functionPrefix` does not correspond to any manifest inline function |
| `function-arity-mismatch` | error | an inline function is called with an argument count outside `minArgs..maxArgs` |

### Determinism

| Code | Severity | Rule |
|---|---|---|
| `nondeterministic-function` | warning | the SQL calls a nondeterministic SQLite built-in, so replaying the payload would diverge from the original run: `random`/`randomblob` on every call, and `date`/`time`/`datetime`/`julianday`/`strftime` only when they read the wall clock — called with no arguments, or with a top-level `'now'` string literal (case-insensitive). Reproducible forms (`date(:day)`, `datetime('2020-01-01')`) are not flagged. One warning per offending call; the lists are single-sourced in `codegen/core/src/ir.ts` (docs/proposals/rule-parameters-as-data.md) |

### Engine portability

The host declares the oldest SQLite it supports as
`library.minSqliteVersionNumber` (default 3019003 = 3.19.3). The runtime
already gates a workspace on it; these two lints extend the same promise
to the SQL a script writes, so an above-floor construct fails in CI
instead of on a player's device.

| Code | Severity | Rule |
|---|---|---|
| `sqlite-version-too-low-for-function` | error | the SQL calls a built-in introduced *after* the host's `minSqliteVersionNumber`. Resolved from an exact-name table first, then the longest matching family prefix, both single-sourced in `codegen/core/src/ir.ts` (`FUNCTION_MIN_VERSION`, `FUNCTION_PREFIX_MIN_VERSION`). Fix by raising the host's `minSqliteVersion` or dropping the function. One finding per distinct name per statement |
| `nonportable-function` | error | the SQL calls a built-in whose presence is decided by the engine's **compile options**, not its version — the math functions (`sqrt`, `pow`, `ceil`, …), which need `-DSQLITE_ENABLE_MATH_FUNCTIONS`. Kept a separate code from the version lint precisely because raising `minSqliteVersion` does **not** fix it (`NONPORTABLE_FUNCTIONS` in `ir.ts`) |

Every version in the table is sourced from the sqlite.org changelog for
that release: window functions 3.25.0, `iif` 3.32.0, `format` and
`unixepoch` 3.38.0, `octet_length` and `timediff` 3.43.0, `concat`,
`concat_ws` and `string_agg` 3.44.0. Functions at or below the floor are
deliberately absent and never flagged — `printf` is the one to watch,
since `format()` is its 3.38 rename but `printf` itself (3.8.3) stays
legal forever.

`json_*` is treated as **3.38.0**, which is not its introduction
version: JSON1 existed long before, but until 3.38 it was compile-gated
behind `-DSQLITE_ENABLE_JSON1` and absent from stock builds. 3.38.0 is
the first release at which a *version* floor alone makes the family
safe, so that is the number a version lint can honestly use.
`jsonb_*` is 3.45.0, and longest-prefix resolution is what keeps
`jsonb_extract` from being under-reported at the weaker `json` floor.

A manifest inline function is never judged by either lint: it is
registered by the host adapter through `sqlite3_create_function`, so
neither the engine's version nor its compile options decide whether it
exists.

**Known limit — keyword-level syntax is not detected.** Above-floor
*syntax* (`RETURNING` 3.35, `ON CONFLICT … DO UPDATE` 3.24, `TRUE`/
`FALSE` literals 3.23, `STRICT` 3.37, `RIGHT JOIN` 3.39, generated
columns 3.31) is **not** flagged. These validators tokenize; they do not
parse a grammar, and every cheap token-level test for them is
false-positive-prone: a column or alias legitimately named `returning`
tokenizes identically to the keyword, and `ON CONFLICT` is *pre-floor*
syntax inside a `CREATE TABLE` constraint. Since both codes here are
errors that block publication, a false positive is as damaging as a
miss, so these are documented rather than guessed at. The enumerated
list lives in `docs/sqlite-surface.md`.

### Statement denylist

| Code | Severity | Rule |
|---|---|---|
| `multiple-statements` | error | the `sql` field holds more than one statement: a **top-level** (paren depth 0) `;` with more SQL after it. A bare trailing `;` (single statement, terminated) is legal; a `;` inside a string literal or a comment does not count (the tokenizer collapses both). This is what anchors the two rules below on the **real** statement — without it a leading no-op (`SELECT 1; PRAGMA …`) hides the denied statement from `forbidden-statement`/`protocol-table-write` |
| `forbidden-statement` | error | the statement's **first meaningful token** is a denied statement keyword: `BEGIN`/`COMMIT`/`END`/`ROLLBACK`/`SAVEPOINT`/`RELEASE` (transaction control), `ATTACH`/`DETACH` (filesystem escape), `PRAGMA`/`VACUUM`/`ANALYZE`/`REINDEX` (engine state). Single-sourced as `FORBIDDEN_LEADING_KEYWORDS` in `ir.ts` |
| `protocol-table-write` | error | an `INSERT`/`UPDATE`/`DELETE` targets a runtime-owned table: any `result_*` table or result list child table, the host-call queue table, or the runtime inputs table. Targets are resolved from the **manifest**, never from a name prefix, because all of these names are host-configurable (docs/naming.md) |

Why these are errors rather than warnings:

- **One statement per `sql` field is the contract, and a second statement
  is a silent-drop hazard.** The native adapter's `prepare_v2` compiles the
  first statement and discards the rest without error, so a script that
  writes two statements loses the tail with no diagnostic — and, worse, a
  harmless leading statement (`SELECT 1; …`) becomes a denylist bypass,
  because `forbidden-statement` and `protocol-table-write` both anchor on the
  first statement's tokens.
- **Transaction control is a silent-data-loss shape.** The unit of
  atomicity is the *step* (its statements plus the drain), not a
  transaction. A script that opens a transaction and rolls it back
  discards the drain's result rows and queue updates *after* the host
  handlers have already run with real-world side effects — and the run
  still reports `Completed`.
- **`ATTACH` is the only filesystem escape.** Default workspaces are
  private in-memory, but the connection factory also takes a database
  path; on a file-backed workspace `ATTACH` grants read **and** write
  access to any reachable database file, including the app's own saves.
- **`PRAGMA` changes semantics under the runtime.** `foreign_keys`,
  `recursive_triggers` and `case_sensitive_like` alter behaviour the
  other lints cannot see, and `writable_schema=ON` lets a script rewrite
  `sqlite_master` — redefining the queue trigger or dropping constraints.
- **Protocol tables have exactly one writer.** The drain and the
  result-write policy both assume it. A script can otherwise forge a
  result the host never produced, or delete queued calls so they never
  drain while the run reports success.

What stays legal, and is pinned by tests in both validators:

- `WITH … INSERT` — a CTE prefix is not a denied keyword, and the write
  target is read *after* walking the CTE prefix, so a dummy CTE can
  neither trip the lint nor smuggle a protocol write past it.
- `pragma_table_info(...)` and the other `pragma_*` table-valued
  functions inside a `SELECT`; a table named `pragma_helper`; a column
  named `begin`; the string literal `'PRAGMA …'`; `CASE … END`. Only the
  first token is treated as a statement keyword, and only identifier
  tokens — a leading `'PRAGMA'` literal is a string, not a statement.
- **Reading** any runtime-owned table. Only writes are denied.
- Writing **call tables** and their input list child tables (that is how
  a script makes a host call), `script_vars`, and `script_control`.

### Result-read lineage (java validator only in v1)

| Code | Severity | Rule |
|---|---|---|
| `result-read-unknown-call` | error | a statement reads `result_<method>` (or its child tables) filtered on a `call_id` that no statement emits for that method |
| `result-read-not-after-call` | error | the read happens in the same or an earlier step than the emitting insert — results only exist after the emitting step's drain |

Static `call_id` resolution covers literals and bindings with text
values (`call_id = :x` where `x` is bound); computed ids (e.g.
`'w-' || result_key`) are skipped by lineage/duplicate checks. This is
documented best-effort linting, not proof.

## Validity

A payload is **publishable** when it has zero errors; warnings don't
block. Implementations may report extra findings on invalid payloads,
but must produce no errors and exactly the expected warnings on valid
fixtures.
