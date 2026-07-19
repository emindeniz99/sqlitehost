# Validation layers

Four layers (plan §24). The validator is an authoring correctness tool,
not a security sandbox.

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
| `unknown-required-feature` | error | feature not in manifest `library.features` |
| `unknown-required-method` | error | method not in manifest |
| `duplicate-input-name` | error | two `inputs` entries share a name |

### Bindings

| Code | Severity | Rule |
|---|---|---|
| `missing-binding` | error | SQL parameter with no binding |
| `unused-binding` | error | binding not referenced in SQL |
| `binding-type-mismatch` | error | for `INSERT INTO <call table> (cols…) VALUES (…)` where a parameter feeds a known column: binding type must be compatible with the column's scalar type (`string`←text, `bytes`←blob, `boolean`←bool, `int32`←int32, `int64`←int32/int64, `float32`←float32, `float64`←float64/float32, `call_id`←text; integer bindings do NOT coerce into float columns and vice versa; optional columns also accept null) |
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
