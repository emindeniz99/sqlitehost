# Validation layers

Four layers. The validator is an authoring correctness tool,
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
duplicate method names, duplicate SQL names, a property whose SQL name
is *derived* (no `@sqlName`) into something that is not snake_case,
duplicate derived table/column names, two libraries whose names derive
the same artifact base name, duplicate DTO/model simple names across
namespaces, non-snake_case or case-colliding column names, a
doneStatusValue equal to the reserved `pending` queue sentinel,
missing/invalid api level, a method apiLevel exceeding the library
apiLevel, invalid handler names, a handler name or namespace segment
that is a C# or Java keyword, a `functionName` that is not snake_case or
that collides with a name SQLite already owns, invalid or empty list
item shapes, host interfaces declared outside any namespace.

Manifests are checked too, on the way back in: `parseManifest`
(`codegen/core/src/manifest.ts`) validates structure, types, ranges and
uniqueness before any emitter sees an IR, so a hand-edited or
merge-conflicted manifest fails with every problem listed at once rather
than emitting code no compiler accepts. `docs/manifest.md` says what is
checked and what deliberately is not.

## 2. Cross-language golden validation

The manifest emitter serializes the IR canonically; C#, Java, and
TypeScript artifacts are tested against the same committed fixtures
(`fixtures/manifests`, `fixtures/schemas`). See `docs/testing.md`.

## 3. Prepare-only SQLite validation (Java, `sqlite-host-jdbc`)

Opens an in-memory SQLite database, creates the generated schema,
**prepares** every script statement (compile only — catches grammar
errors, missing tables/columns, unsupported functions), and finalizes
without stepping.

**Each statement is prepared in isolation**, on its own connection with
the schema re-created. Preparing a whole step on one connection made the
verdict on statement *n* depend on the *prepare-time* side effects of
statement *n-1*, and prepare-time side effects exist: SQLite's flag
pragmas are applied by the code generator rather than the VDBE, so a
leading `EXPLAIN PRAGMA writable_schema = ON` — which executes nothing —
turned the flag on inside this validator's own engine, after which the
`UPDATE sqlite_master` that followed compiled clean. A gate whose verdict
the payload can change is not a gate. The isolation costs little: the
generated schema is a couple of dozen small DDL statements against an
in-memory database, and the Java conformance matrix went from 0.51 s
(74 payloads, one connection per step) to 0.65 s (76 payloads, one per
statement).

| Code | Severity | Rule |
|---|---|---|
| `sql-prepare-error` | error | a statement failed to compile against the schema generated from the manifest. Java-only: the TypeScript authoring lint has no engine. It is the sole finding for a fault only a compiler can see (`invalid/unknown-column.json`) and a second, corroborating one wherever a lint-rejected statement is also uncompilable |

### Prepare-only is not a floor check — and cannot become one

This layer runs on whatever engine `org.xerial:sqlite-jdbc` bundles, while
the default contract floor is **3.19.3**. Read
`<sqlite-jdbc.version>` in `java/pom.xml` for the pin — xerial's first
three components are the bundled SQLite version, so the pin tells you the
top of the gap and the floor tells you the bottom. Every minor release
between the two adds syntax and functions that *compile clean here* and
fail on a device at the floor, and the gap only widens as the driver is
bumped. Pinning the driver down was considered and rejected, on three
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
Every code in the tables below has at least one fixture, and
`scripts/check-fixture-corpus.mjs` fails the build if one loses its last.

`method-api-level-too-high` was the exception until the corpus grew a
second manifest. Every case bound to `sample-host.manifest.json`, whose
methods are all `apiLevel` 1, and the envelope check rejects
`requiredApiLevel < 1` — so no payload against that manifest could put a
method above the script. A case may now name its own manifest with an
optional `"manifest"` key (relative to `fixtures/payloads/`, defaulting
to the top-level one), and `typespec/examples/high-api-host-methods.tsp`
is a one-method host at `apiLevel` 2 that exists for this rule.
Manifests are never hand-written, so it is emitted and byte-pinned by
`tests/cross-language-golden/run.mjs` like the sample host.

`typespec/examples/syntax-floor-host-methods.tsp` is a third host on the
same footing, and it is there for the mirror-image reason: every other
manifest declares the default 3.19.3 floor, so a payload using
above-floor SQL could only ever be an *error* case and nothing pinned the
other direction — that raising `minSqliteVersion` actually silences
`sqlite-version-too-low-for-syntax` in **both** validators. It declares
3.39.0, the newest version any `SYNTAX_MIN_VERSION` entry names, so one
host clears every detector.

One rule is **Java-only, and cannot be otherwise**: an `int32`/`int64`
whose JSON number is written non-integrally (`1.0`, `1e3`). Java's reader
sees the token and rejects it; TypeScript's lint runs on a value that
`JSON.parse` already produced, and `JSON.parse` collapses both spellings
to the integer 1 and 1000 before any check can look. Recovering the
distinction would mean parsing the JSON text a second time in the
authoring SDK, which buys nothing the publication gate does not already
have — Java is the gate, and `invalid/non-integral-int.json` carries
`"validators": ["java"]` for that reason. Note the consequence for the
CLI's exit code: a payload the strict reader refuses prints an
`invalid-envelope` finding and exits **1**, not 2. Exit 2 means no
verdict was reached (bad arguments, an unreadable file, or a workspace
layer 3 could not set up).

**The shipped CLI is the whole gate**, all four layers including
prepare-only. It ships from `sqlite-host-jdbc`
(`sqlite-host-jdbc-<version>-cli.jar`, see `java/README.md`) rather than
from `sqlite-host-validator`, because layer 3 lives in that module and
the dependency runs jdbc → validator. It ran layers 1/2/4 only until
this was fixed, and the gap was not theoretical: it exited **0**,
printing nothing, on `invalid/unknown-column.json` — a corpus case whose
only expected finding is `sql-prepare-error`.

### Structural

| Code | Severity | Rule |
|---|---|---|
| `invalid-envelope` | error | missing or blank required envelope fields (including a step whose `statements` list is empty or missing, a blank binding name, and a blank `requiredFeatures`/`requiredMethods` entry). A field whose *shape* is wrong is this code, never a lookup failure: a blank feature name is `invalid-envelope`, not `unknown-required-feature`, because nothing was named to look up |
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
| `nondeterministic-function` | warning | the SQL calls a nondeterministic SQLite built-in, so replaying the payload would diverge from the original run: `random`/`randomblob` on every call, and `date`/`time`/`datetime`/`julianday`/`strftime` only when they read the wall clock — called with no arguments, or with a top-level `'now'` string literal (case-insensitive). Reproducible forms (`date(:day)`, `datetime('2020-01-01')`) are not flagged. The three wall-clock KEYWORDS — `CURRENT_TIMESTAMP`, `CURRENT_DATE`, `CURRENT_TIME` — count too. SQLite spells them with no argument list (`current_timestamp()` is a syntax error), so they are matched against bare identifier tokens rather than parsed calls; a *delimited* spelling (`"current_date"`) is a column reference, not the keyword, and is never flagged. One warning per offending occurrence; the lists are single-sourced in `codegen/core/src/ir.ts` (`NONDETERMINISTIC_FUNCTIONS_ALWAYS`, `NONDETERMINISTIC_TIME_FUNCTIONS`, `NONDETERMINISTIC_TIME_KEYWORDS` — docs/proposals/rule-parameters-as-data.md) |

### Engine portability

The host declares the oldest SQLite it supports as
`library.minSqliteVersionNumber` (default 3019003 = 3.19.3). The runtime
already gates a workspace on it; these two lints extend the same promise
to the SQL a script writes, so an above-floor construct fails in CI
instead of on a player's device.

| Code | Severity | Rule |
|---|---|---|
| `sqlite-version-too-low-for-function` | error | the SQL calls a built-in introduced *after* the host's `minSqliteVersionNumber`. Resolved from an exact-name table first, then the longest matching family prefix, both single-sourced in `codegen/core/src/ir.ts` (`FUNCTION_MIN_VERSION`, `FUNCTION_PREFIX_MIN_VERSION`). Fix by raising the host's `minSqliteVersion` or dropping the function. One finding per distinct name per statement |
| `sqlite-version-too-low-for-syntax` | error | the SQL uses a **grammar** construct introduced after the host's `minSqliteVersionNumber` — the same promise as the row above, for the half of the surface that is not a function call. Each construct is a token pattern in the shared analyzers, keyed by a stable feature id whose version and wording are single-sourced in `ir.ts` (`SYNTAX_MIN_VERSION`); the table of ids is below. Fix by raising the host's `minSqliteVersion` or rewriting the statement. One finding per feature per statement, so a statement using two constructs reports twice |
| `nonportable-function` | error | the SQL calls a built-in whose presence is decided by the engine's **compile options**, not its version — the math functions (`sqrt`, `pow`, `ceil`, …), which need `-DSQLITE_ENABLE_MATH_FUNCTIONS`, and `load_extension`, which `-DSQLITE_OMIT_LOAD_EXTENSION` removes outright and which stays disabled per connection even where it is compiled in; `soundex` needs `-DSQLITE_SOUNDEX` and `sqlite_offset` needs `-DSQLITE_ENABLE_OFFSET_SQL_FUNC`, neither of which stock builds set. Kept a separate code from the version lint precisely because raising `minSqliteVersion` does **not** fix it (`NONPORTABLE_FUNCTIONS` in `ir.ts`) |

Every version in the table is sourced from the sqlite.org changelog for
that release: window functions 3.25.0, `pragma_table_xinfo` 3.26.0,
`pragma_function_list` and `pragma_module_list` 3.30.0, `iif` 3.32.0,
`substring` 3.34.0, `pragma_table_list` 3.37.0, `format` and
`unixepoch` 3.38.0, `unhex` 3.41.0, `octet_length` and `timediff`
3.43.0, `concat`, `concat_ws` and `string_agg` 3.44.0, `if` 3.48.0,
`unistr` and `unistr_quote` 3.50.0. Functions at or below the floor are
deliberately absent and never flagged — `printf` is the one to watch,
since `format()` is its 3.38 rename but `printf` itself (3.8.3) stays
legal forever.

Two spellings, not one. The `pragma_*` table-valued wrappers are
ordinarily written *without* an argument list (`SELECT * FROM
pragma_table_list`), so a scan that only recognises `name(` sees none
of them. Both validators therefore also check bare identifiers whose
name begins `pragma_`, which is narrow enough that an ordinary column
reference cannot be caught by it. `if` is the same trap from the other
side: it is the MySQL-compatible alias for `iif` added in 3.48, so
until it was listed the identical expression was an error under one
spelling and invisible under the other.

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

#### Syntax versions

`sqlite-version-too-low-for-syntax` covers these constructs. The version
is the release sqlite.org's changelog names for that feature, checked
entry by entry; the id is stable and appears in `SYNTAX_MIN_VERSION`
alongside the description the message quotes.

| Feature id | Introduced | Token pattern |
|---|---|---|
| `insert-alias` | 3.24.0 | `AS` immediately after the target name of an `INSERT`/`REPLACE` |
| `upsert` | 3.24.0 | `ON CONFLICT` inside an `INSERT`/`REPLACE` |
| `window-functions` | 3.25.0 | `OVER` followed by `(` |
| `aggregate-filter` | 3.30.0 | `FILTER` followed by `(` `WHERE` |
| `nulls-first-last` | 3.30.0 | `NULLS` followed by `FIRST`/`LAST` |
| `update-from` | 3.33.0 | `FROM` at paren depth 0 of an `UPDATE` |
| `returning` | 3.35.0 | `RETURNING` at paren depth 0 of a write, in clause position |
| `materialized-cte` | 3.35.0 | `AS [NOT] MATERIALIZED` followed by `(` |
| `json-arrow-operators` | 3.38.0 | `-` followed by a `>`-leading operator token (`->`, `->>`) |
| `right-full-join` | 3.39.0 | `RIGHT`/`FULL` followed by `JOIN`/`OUTER` |
| `is-distinct-from` | 3.39.0 | `IS [NOT] DISTINCT FROM` |

**A delimited identifier is never a keyword.** Every pattern above
matches only *bare* identifier tokens, because SQLite has no way to write
a keyword delimited: `"full"` is a name (or, under the double-quote
fallback, a string), so `SELECT * FROM t "full" JOIN u ON 1` is ordinary
3.19.3 SQL and stays silent. That single rule is what makes a
token-level test safe enough for a code that blocks publication, and it
is the same line the determinism lint draws for `"current_date"`.
The statement-scoped patterns add a second guard: `ON CONFLICT` is
*pre-floor* syntax in a `CREATE TABLE` constraint, so `upsert` only looks
inside an INSERT, and a top-level `FROM` is only an `UPDATE … FROM` when
the statement's verb is `UPDATE`.

**The residual false positive, stated rather than hidden.** SQLite keeps
`RETURNING` usable as an identifier, so a column literally named
`returning`, written bare at paren depth 0 of a write statement in a spot
that reads like clause position (`… WHERE returning IS NULL`), is
reported. Spelling it `"returning"` is the escape.

**Deliberately not detected**, one reason each: generated columns
(`GENERATED ALWAYS AS`, 3.31.0), `STRICT` and `WITHOUT ROWID` (3.37.0)
and `VACUUM INTO` (3.27.0) appear only inside statements
`forbidden-statement` already refuses, so a detector could only add a
second finding to an already-rejected statement; `TRUE`/`FALSE` (3.23.0)
tokenize identically to a column reference, which is exactly what a
pre-3.23 engine parses them as. The full above-floor inventory,
including the rows nothing enforces, is in `docs/sqlite-surface.md`.

### Statement denylist

| Code | Severity | Rule |
|---|---|---|
| `embedded-nul` | error | the `sql` field contains U+0000. SQLite's prepare takes a NUL-terminated string, so it compiles only the text before the first NUL and silently drops the rest — every other rule here reads the whole field, so the statement the validator judged is not the statement the device runs |
| `multiple-statements` | error | the `sql` field holds more than one statement: a **top-level** (paren depth 0) `;` with more SQL after it. A bare trailing `;` (single statement, terminated) is legal; a `;` inside a string literal or a comment does not count (the tokenizer collapses both). This is what anchors the two rules below on the **real** statement — without it a leading no-op (`SELECT 1; PRAGMA …`) hides the denied statement from `forbidden-statement`/`protocol-table-write` |
| `unrecognized-statement` | error | the statement's **first meaningful token** is not an identifier, so `forbidden-statement` and `protocol-table-write` have nothing to anchor on. Both rules used to skip such a statement silently, which is fail-**open**: every legal script statement starts with an identifier (`SELECT`/`INSERT`/`UPDATE`/`DELETE`/`REPLACE`/`WITH`/`VALUES`), so anything else is either unrunnable or a tokenizer/engine divergence — and one leading U+FEFF was exactly that. A parenthesised `(SELECT 1)` is not a counter-example: sqlite3 3.51.0 rejects it with `near "(": syntax error`. Blank `sql` is `invalid-envelope`, not this |
| `forbidden-statement` | error | the statement's **first meaningful token** is a denied statement keyword: `BEGIN`/`COMMIT`/`END`/`ROLLBACK`/`SAVEPOINT`/`RELEASE` (transaction control), `ATTACH`/`DETACH` (filesystem escape), `PRAGMA`/`VACUUM`/`ANALYZE`/`REINDEX` (engine state), `CREATE`/`ALTER`/`DROP` (schema DDL), `EXPLAIN` (a prefix to any of them — see below). Single-sourced as `FORBIDDEN_LEADING_KEYWORDS` in `ir.ts` |
| `protocol-table-write` | error | an `INSERT`/`UPDATE`/`DELETE` targets a runtime-owned **or** SQLite-owned table. Runtime-owned: any `result_*` table or result list child table, the host-call queue table, the runtime inputs table — resolved from the **manifest**, never from a name prefix, because all of those names are host-configurable (docs/naming.md). SQLite-owned: `sqlite_master`, `sqlite_schema`, `sqlite_temp_master`, `sqlite_temp_schema`, `sqlite_sequence` and `sqlite_stat1`..`sqlite_stat4` — a *fixed* list (`SYSTEM_TABLES` in `ir.ts`), because no manifest can rename them and a manifest-only resolution therefore missed every one |
| `forbidden-function` | error | the SQL calls a built-in whose *call* is the hazard, whatever the engine version: `pragma_optimize`, which executes `ANALYZE` — creating and populating `sqlite_stat1` in the workspace — from inside a `SELECT`. Matched wherever the identifier appears, bare (`FROM pragma_optimize`) or called (`pragma_optimize(0xfffe)`), both being legal SQLite. Single-sourced as `FORBIDDEN_FUNCTIONS` in `ir.ts`; one finding per name per statement |

Why these are errors rather than warnings:

- **A NUL truncates the statement without a diagnostic.** Verified against
  libsqlite3 3.51.0: `DELETE FROM t\u0000 WHERE name = :n` prepares
  cleanly as `DELETE FROM t` with zero bind parameters — an unrestricted
  delete where the author wrote a filtered one. Nothing in the engine
  reports it, prepare-only validation compiles the truncated form and
  passes, and the binding scan sees a `:n` the compiled statement does not
  have. The shipped native adapter rejects an embedded NUL at run time,
  which makes this an authoring-time error there and a silent rewrite on
  any adapter that does not.
- **One statement per `sql` field is the contract, and a second statement
  is a denylist bypass.** Adapters disagree about the tail: the shipped
  native adapter rejects a trailing statement outright and steps nothing
  (`docs/adapter-contract.md`), while an ADO.NET wrapper runs the whole
  batch, so one script means two different things on two hosts. Either
  way a harmless leading statement (`SELECT 1; …`) hides the real one,
  because `forbidden-statement` and `protocol-table-write` both anchor on
  the first statement's tokens.
- **A statement the anchor cannot read must fail closed.** Both denylist
  rules read token 0, and both used to do nothing when that token was not
  an identifier. A leading U+FEFF was enough: SQLite's tokenizer gives
  the UTF-8 BOM its own character class and returns `TK_SPACE` for it, so
  `<BOM>PRAGMA writable_schema = ON` compiles and runs as the PRAGMA it
  is, while the ASCII-only scanners made it punctuation at index 0 and
  skipped both denylists. Both scanners now treat U+FEFF as whitespace —
  measured on the sqlite3 CLI 3.51.0 and SQLite 3.53.4, where a BOM
  separates tokens wherever one may start (`SELECT <BOM>1`,
  `DELETE <BOM> FROM t`) and is an identifier character only when it
  continues one (`DELETE<BOM>FROM t` is a syntax error). Over-skipping is
  the fail-safe direction there, the same argument the vertical tab
  already carries. `unrecognized-statement` is the second, independent
  half: the next divergence of this shape is rejected instead of waved
  through.
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
  That path is scratch space rather than storage: the shipped native
  factory deletes whatever sits there, plus its `-wal` and `-shm`
  siblings, on every open (`docs/adapter-contract.md`, Workspace
  lifecycle).
- **`PRAGMA` changes semantics under the runtime.** `foreign_keys`,
  `recursive_triggers` and `case_sensitive_like` alter behaviour the
  other lints cannot see, and `writable_schema=ON` lets a script rewrite
  `sqlite_master` — redefining the queue trigger or dropping constraints.
- **Protocol tables have exactly one writer.** The drain and the
  result-write policy both assume it. A script can otherwise forge a
  result the host never produced, or delete queued calls so they never
  drain while the run reports success. SQLite's own tables are on the
  same list for a sharper reason: `UPDATE sqlite_master SET sql = …`
  rewrites the queue trigger itself, which is the DDL rule's failure
  shape reached without any DDL. Only layer 3 stood in front of it, and
  only while `writable_schema` was off.
- **`EXPLAIN` is a prefix, so it hid the statement it prefixes.** It
  anchored `leadingKeyword` on itself and left the real verb unread, so
  `EXPLAIN DELETE FROM pending_host_calls` passed both denylists. It is
  also not inert: SQLite applies the flag pragmas (`PragTyp_FLAG`) in the
  code generator, i.e. during `sqlite3_prepare`, so `EXPLAIN PRAGMA
  writable_schema = ON` sets the flag for real while executing nothing —
  verified on the sqlite3 CLI 3.51.0, and likewise for `foreign_keys`,
  `case_sensitive_like`, `recursive_triggers`, `trusted_schema`,
  `legacy_alter_table` and the `EXPLAIN QUERY PLAN` spelling. A script
  executes statements for effect and discards rows
  (`docs/sqlite-surface.md` §4), so denying the keyword costs nothing
  legal.
- **The runtime owns the schema, so a script has no DDL to do.**
  `DROP TRIGGER trg_call_<method>_queue` is the sharp case: with the queue
  trigger gone, inserts into that call table enqueue nothing, the drain
  finds nothing, and the run reports `Completed` with zero executed calls
  — the same silent-success shape as a rolled-back transaction.
  `ALTER TABLE result_<method> RENAME TO …` breaks the reader instead, and
  `CREATE TRIGGER` launders writes past `protocol-table-write`, which only
  ever reads a statement's own target and never a trigger body. `CREATE`
  is denied along with the other two because a script's scratch surface is
  `script_vars`, not tables of its own, so the whole class costs nothing
  legal. Denying the leading keyword is also the only cheap form this can
  take: a target check would have to model every DDL shape, and `CREATE`
  has no target to check at all.

What stays legal, and is pinned by tests in both validators:

- `WITH … INSERT` — a CTE prefix is not a denied keyword, and the write
  target is read *after* walking the CTE prefix, so a dummy CTE can
  neither trip the lint nor smuggle a protocol write past it.
- `pragma_table_info(...)` and the other **read-only** `pragma_*`
  table-valued functions inside a `SELECT`; a table named
  `pragma_helper`; a column named `begin` or `created_at`; the string
  literal `'PRAGMA …'`; `CASE … END`. Only the first token is treated as
  a statement keyword, and only identifier tokens — a leading `'PRAGMA'`
  literal is a string, not a statement. The exception is
  `pragma_optimize`, which is not a read at all (`forbidden-function`
  above).
- **Reading** any runtime-owned table. Only writes are denied.
- Writing **call tables** and their input list child tables (that is how
  a script makes a host call), `script_vars`, and `script_control`.

**Known limit — the DDL rule is keyword-shaped, not target-shaped.**
`CREATE`/`ALTER`/`DROP` are denied wherever they lead a statement, so the
rule cannot tell a script's own temporary table from a runtime-owned one
and does not try. That is the intended trade: a script has no sanctioned
schema of its own, and a target-aware version would need a DDL grammar
these tokenizers deliberately do not have. `protocol-table-write` still
resolves targets only for `INSERT`/`REPLACE`/`UPDATE`/`DELETE`, which is
now sound rather than a gap, because no statement that changes the schema
can reach it.

### Result-read lineage (both validators)

| Code | Severity | Rule |
|---|---|---|
| `result-read-unknown-call` | error | a statement reads `result_<method>` (or its child tables) filtered on a `call_id` that no statement emits for that method |
| `result-read-not-after-call` | error | the read happens in the same or an earlier step than the emitting insert — results only exist after the emitting step's drain |

Both codes carry `"validators": ["java", "typescript"]` in
`fixtures/payloads/expectations.json`, and the TypeScript rule lives in
`typescript/authoring-sdk/src/lint.ts`. Both tokenizers resolve all four
quoting forms for a result table — `"…"`, `` `…` ``, `[…]` and `'…'` — so
`invalid/result-read-unknown-call-bracket.json` expects both. That case
read `["java"]` until the matrix became exact: the TypeScript tokenizer
had grown bracket support and containment could not see that the
`validators` list had gone stale.

The fourth form is SQLite's MySQL-compatibility misfeature, and it is a
name only *by position*: "if a keyword in single quotes is used in a
context where an identifier is allowed but where a string literal is not
allowed, then the token is understood to be an identifier"
(sqlite.org/lang_keywords.html). So `DELETE FROM 'pending_host_calls'`,
`UPDATE 'result_get_value' SET …`, `INSERT INTO 'result_get_value' (…)`
and `DELETE FROM main.'pending_host_calls'` all name tables and are
resolved as such — while the identical token in *value* position stays
the string literal it looks like, which is what keeps static `call_id`
resolution reading `call_id = 'c1'` as the id `c1`. Both validators make
that distinction positionally (`isNameToken` / `SqlAnalyzer.isName`); a
token-kind gate is what let one quote character silence
`protocol-table-write`, the INSERT analysis and this lineage rule at
once.

Static `call_id` resolution covers literals and bindings with text
values (`call_id = :x` where `x` is bound); computed ids (e.g.
`'w-' || result_key`) are skipped by lineage/duplicate checks. This is
documented best-effort linting, not proof.

## Validity

A payload is **publishable** when it has zero errors; warnings don't
block.

**The conformance matrix is exact.** For every case in
`fixtures/payloads/expectations.json`, an implementation's reported
errors and reported warnings must each *equal* the codes listed for it —
an extra finding fails the suite exactly as a missing one does. The
comparison is over sorted lists rather than sets, so multiplicity is part
of the expectation: a code reported twice must be expected twice. Every
`invalid/` fixture is single-fault, and `scripts/check-fixture-corpus.mjs`
holds the corpus to that shape.

Containment was the rule until the exact match landed, and every
validator divergence found in two audit rounds walked through it: an
implementation could report anything at all as long as the expected code
was somewhere in the list, so a rule that fired on the wrong payload, a
stale `validators` list, and a fixture carrying two faults all read as
green.

The one finding that legitimately accompanies another is
`sql-prepare-error`: layer 3 compiles the same statement the semantic
lint just rejected, so a fixture whose SQL is also uncompilable
(`invalid/inline-unknown-function.json`,
`invalid/inline-arity-mismatch.json`,
`invalid/protocol-table-write-vertical-tab.json`) carries a second,
Java-only expected code. It corroborates the fault rather than being a
second one, which is why the corpus checker allows it alongside a lint
code and nothing else.
