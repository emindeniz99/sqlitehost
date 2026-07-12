# Proposal: optional inline host functions (scalar UDFs)

Status: **implemented** (codegen + C# runtime/adapters + Java/TS
validators; see docs/adapter-contract.md and docs/csharp-api.md for
the shipped surface). Future tiers (`tableFunctions`, idempotent
mutations) remain design-only.

## Motivation

Read-shaped host calls currently need two steps: insert into
`call_get_value` in step N, read `result_get_value` in step N+1. On
adapters whose wrapper can register SQL functions
(`sqlite3_create_function` — e.g. DllImport-based wrappers,
Microsoft.Data.Sqlite `CreateFunction`, System.Data.SQLite
`BindFunction`, sqlite-net via its raw handle + SQLitePCL.raw), a
non-mutating method can additionally be exposed as an inline scalar
function:

```sql
INSERT INTO call_set_value (call_id, input_key, input_value)
SELECT :c, 'k', fn_get_value('k') * 2 WHERE fn_get_value('k') <> 42
```

This is **additive**: the call/result tables always exist and always
work; the function form is an extra surface for capable adapters.
The original constraint stands and is the reason this is optional:
the toolkit exists precisely because some wrappers cannot register
functions — so this can never be a floor requirement.

## Decisions (owner-approved)

1. **Exposure: automatic with opt-out.** Every eligible method is
   inline-exposed by default; `inline: false` on `@hostMethod` opts a
   method out. (`functionName: "..."` overrides the derived name.)
2. **Eligibility (all required):**
   - `mutates: false` (new `@hostMethod` flag, **default `true`** —
     conservative: nothing is inline-eligible unless declared
     non-mutating),
   - input has only scalar fields (no lists),
   - result has **exactly one scalar field** (no lists).
   TSP diagnostics enforce: `inline: true`-style overrides on an
   ineligible method are compile errors.
3. **Mutating methods: closed in v1, door documented open.** See
   "Future: idempotent mutations" below.
4. **`idempotent` flag: not added** (YAGNI). Reserved as a future axis
   for retry/replay policies; nothing in v1 would read it.

## Why only single-scalar results (the "obj/list" question)

A SQLite scalar UDF returns exactly one SQL value. Alternatives
considered:

- **Per-field functions** (`fn_get_user_name(id)`, `fn_get_user_age(id)`):
  rejected — every call is a separate handler invocation (reading 3
  fields runs the handler 3×), surface explodes, and per-statement
  caching to fix it adds real complexity.
- **Table-valued functions** (`SELECT * FROM fn_get_user(5)`): the
  genuine answer for object/row/list returns — but requires
  `sqlite3_create_module` (virtual table API), which far fewer wrappers
  expose and which is much heavier to implement. Deferred as a distinct
  future capability tier (`tableFunctions`), stacked on the same
  capability/feature machinery.
- **JSON-encoded text return**: rejected — consuming it needs JSON1,
  which is outside the 3.19.3 floor.

## Naming

New host-level naming option `functionPrefix` (default `fn_`,
configurable like every SQL-visible name):
`fn_` + snake(methodName) → `fn_get_value`. Diagnostics: collisions
with other derived names and with SQLite built-in function names.

## Signature mapping

- Arguments are the input's scalar fields **in declaration order**;
  required fields first bar none — a required field declared after an
  optional one is a TSP diagnostic for inline-eligible methods.
- Optional trailing fields → the function registers every arity from
  minArgs (required count) to maxArgs (all fields); omitted trailing
  args = null.
- SQL NULL for a required field → SQL error (mirrors the NOT NULL
  call-table contract).
- Return: the single result field's value, standard type mapping
  (boolean → 0/1, bytes → blob, floats → REAL).

## Capability model

- **Factory-level, static**: a new optional interface (working shape:
  `ISqliteHostFunctionCapableFactory` with a
  `RegisterScalarFunction(...)`-capable connection contract) — the
  runtime knows the capability **without opening a workspace**, so the
  clean-skip precheck stays workspace-free.
- Feature vocabulary gains `inlineFunctions`:
  - the **manifest** advertises that the host *defines* inline
    functions (per-method `inline` block: functionName, minArgs,
    maxArgs, args, returns — absent when not exposed);
  - the **runtime's** SupportedFeatures includes `inlineFunctions`
    only when the factory is capable;
  - scripts that call the function form declare
    `requiredFeatures: ["inlineFunctions"]` → hosts/adapters without
    the capability **clean-skip** (`missing-feature`), exactly the
    existing compat machinery.
- Registration happens once per workspace open, before schema DDL.

## Error contract (critical for IL2CPP)

A handler exception inside a UDF must **never cross the native
frames**. The capability contract pins: the adapter-side wrapper
catches everything and reports via `sqlite3_result_error` with the
marker prefix `SQLITEHOST_HANDLER_ERROR:`; the runtime maps a failed
statement whose adapter error carries the marker to
`FailedHandler`/`handler-error` (Method set), otherwise normal
`sql-error`. Conformance suite gains an optional capability section
(exception wrapping, arity matrix, NULL handling, unicode args,
0-call/N-call planner behavior smoke) — skipped with a reason on
incapable adapters.

## Determinism flag

Registered **without** SQLITE_DETERMINISTIC in v1: the host app may
mutate storage concurrently, and a stale-cached value inside one
statement is worse than the small optimizer win. A per-method
`deterministic` hint can be added later if a consumer needs it.

## Validator plan

- **Java prepare-only**: before preparing, register NULL-returning stub
  functions for every manifest inline entry (name + each arity) so
  `fn_*` compiles; otherwise prepare fails with "no such function".
- **New lint codes** (both Java and TS where static):
  - `undeclared-feature-use` — script calls a manifest inline function
    but `requiredFeatures` lacks `inlineFunctions`;
  - `unknown-function` — identifier(...) call matching the
    functionPrefix but not defined in the manifest;
  - `function-arity-mismatch` — static arg-count check against
    minArgs..maxArgs.
- Lineage: inline reads bypass result tables by design — no lineage
  tracking applies (the value is consumed in place).

## Interaction with pinned semantics

- Only non-mutating methods → a statement that later fails cannot have
  leaked effects through inline calls; drain-after-step and
  `fail`-suppresses-drain stay intact.
- Inline calls do NOT enqueue into `pending_host_calls` and do NOT
  count in `ExecutedCallCount`; a separate `InlineCallCount` diagnostic
  field is proposed.
- `script_control` checks are per-statement and unaffected.

## Future: idempotent mutations through functions (door open, closed in v1)

If ever opened, all of these must be answered first (documented risks):

- the SQLite planner may evaluate a function **0..N times per row** —
  N handler invocations actually execute (idempotent end-state does not
  make N invocations free: cost, logs, telemetry);
- 0-call short-circuits mean "the script ran the statement" no longer
  implies "the effect happened";
- effects would bypass the drain-after-step model — the `fail` action
  and mid-step abort guarantees would not cover them;
- exactly-once accounting (queue rows, ExecutedCallCount, audit) is
  lost for those calls.

Preconditions sketched: explicit `inlineMutation: true` opt-in +
`idempotent: true` (the flag would land then), a documented
"at-least-zero-times" semantic, and validator warnings on any use.

## Implementation phases (when we start — NOT now)

1. Keystone: this doc folded into adapter-contract/naming/manifest/
   errors/validation docs; TSP flags (`mutates`, `inline`,
   `functionName`, `functionPrefix`); IR/manifest `inline` block +
   feature; sample host marks `getValue` (and `recordScore`?) eligible;
   fixtures: example-010-inline payload + expectations (+ lint-code
   cases); pinned C# surface (capability interfaces, InlineCallCount).
2. Codegen: frontend/validation/emitters + regenerated vendored files.
3. C#: runtime registration + error mapping; capability implemented on
   all three reference adapters (CreateFunction / BindFunction /
   SQLitePCL.raw); conformance capability section; dual-mode tests
   (same method via table and via function in one script).
4. Java: manifest model, prepare stubs, lint codes, conformance.
5. TS: model/metadata/lints, conformance.
6. Docs/goldens/e2e; measured-footprint re-check (UDF wrappers add a
   little native code on capable adapters).
