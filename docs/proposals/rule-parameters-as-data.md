# Proposal: rule parameters as shared data (single-sourced constants)

Status: **design-only, awaiting owner decision.** No code change is
proposed here — this documents the problem, an inventory, and a
recommended mechanism so an implementation plan can be made later.

## Motivation

Three consecutive review rounds on the same PR surfaced 14 validation
bugs, and most of them shared one structural cause: the *parameter* of a
validation rule — a reserved string, a name list, an identifier regex, a
comparison threshold — was hardcoded separately in each of the three
language implementations, and the copies drifted. Examples fixed in those
rounds: the reserved `pending` queue status, the `SQL_NAME`
identifier shape, `call_id` uniqueness scope, the reserved-word/case
rules for columns.

Full logic-sharing (compile one validator to WASM, or transpile a shared
core) is off the table: it fights the toolkit's tiny-binary and Unity
IL2CPP goals (a WASM runtime or a transpiled native blob is exactly the
kind of weight this project avoids). But the recurring bug class is not
"we re-implement the *algorithm* three times" — it is "we hardcode the
same *parameter* in N places and they drift." The algorithm (a SQL
tokenizer, a control-flow analyzer) genuinely must stay per-language; the
parameter does not. This proposal moves only the parameters to a single
machine-readable source, leaving each language's loop where it is.

## Why a TypeScript constant isn't already enough

`codegen/core/src/ir.ts` already single-sources several of these values
(`PENDING_STATUS`, `ENGINE_V1`, …). But that file is **TypeScript**. The
Java and C# ports are separate programs in separate runtimes; they cannot
`import` a TypeScript constant, so each keeps its own copy — and those
copies drift. The only artifact all three languages already share is the
emitted output of the codegen toolchain (the manifest JSON today). "Put
it in the IR as data" means: make the constant part of that shared,
emitted surface, and have every language read it from there instead of
hardcoding — a *different place* from a TS-only constant, namely the
cross-language boundary itself.

### Terminology (this is the part that's easy to conflate)

- **TypeSpec (`.tsp`)** is the authoring DSL a host application writes to
  describe *its own* host methods. It is a per-host, user-facing surface.
  It does **not** hold these constants and is not involved in generating
  them.
- **`codegen/core/src/ir.ts`** is hand-written *TypeScript* (toolchain
  code, not TypeSpec). It already holds the protocol constants as `const`
  declarations. This is the one place a maintainer edits `pending`.
- **The emitters** (`codegen/csharp-emitter`, `java-emitter`,
  `manifest-emitter`, …) are TypeScript programs that read the IR and
  generate output — the C# DTOs, the Java model, the manifest JSON are
  *already* produced this way.

So "the toolkit generates these" does **not** mean TypeSpec generates
them. The source is `ir.ts`; an emitter projects it to each language.

#### Why `ir.ts` and not the `.tsp` DSL

A reasonable question is whether these constants should live in TypeSpec
itself. They should not:

1. Only the TS frontend reads `.tsp`. Java/C# would still have to extract
   the values into the IR and emit them — so `.tsp` *adds* a compile step
   without removing one.
2. `.tsp` is the per-host user surface; these constants are
   host-independent, system-level protocol values a user never touches.
3. A flat string / list / regex / lookup-table needs no type system —
   TypeSpec's decorators and checker buy nothing here.
4. The protocol constants already live in `ir.ts`
   (`ENGINE_V1`, `COLUMNS_V1`, `PENDING_STATUS`); moving them to `.tsp`
   would be inconsistent and a larger change.

The clean split: the binding-type **vocabulary** (`int64`, `float32`, …)
is already TypeSpec-native because the user writes those types in `.tsp`;
the binding-type **compatibility matrix** is validation data and belongs
in `ir.ts`. Types in the DSL, rules in `ir.ts`.

## Inventory

Already single-sourced today (Java and TS both read the emitted manifest
JSON): `engine`, `scriptEnvelope.bindingTypes`, `library.apiLevel`,
`minSqliteVersionNumber`, `library.features`. (The C# runtime bakes even
these into source — see the C# nuance below.)

Movable vs not, by value (locations are current as of this writing):

| Rule parameter | Class | Where it lives today | Value |
|---|---|---|---|
| reserved `pending` status | declarative (string) | TS single-sources it (`ir.ts` `PENDING_STATUS`); C# hardcodes it as **3 raw literals** (`SchemaGenerator.cs`, `SqliteHostRuntimeCore.cs` drain, `SqliteHostDefinitionCore.cs` guard); Java once (`DdlGenerator.java`) | **High** — 3 langs, 4+ copies |
| SQLite built-in function list (37) | declarative (list) | TS `validate.ts` `SQLITE_BUILTIN_FUNCTIONS`; C# `SqliteHostDefinitionCore.cs` `SqliteBuiltinFunctions` (identical 37, hand-synced); Java: none | **High** — manual sync |
| binding-type compatibility matrix | declarative (table) | **Only** Java `ValidationEngine.compatible()`; TS and C# have **no** copy; documented in `docs/validation.md` | **Highest** — sharing gives TS/C# a check they lack |
| `halt` / `fail` control verbs | declarative (string) | TS `ir.ts`; C# `SqliteHostRuntimeCore.cs` literals | Medium |
| identifier / method / sql-name regexes | declarative (regex string) | TS `decorators.ts` (`IDENTIFIER`, `METHOD_NAME`, `SQL_NAME`); C# a hand-rolled char-scan (`IsValidMethodName`); Java: none | Medium — see the regex caveat |
| `requiredApiLevel >= 1` + apiLevel ceilings | declarative (the constant `1`) | TS/Java/C#, identical, no drift | Low — trivial inline |
| SQL tokenizer / analyzer | **imperative (algorithm)** | TS `sql.ts`, Java `SqlTokenizer`/`SqlAnalyzer`, C# `SqlParameterScanner` (param-scan half only) | **Not movable** — see non-goals |

### The regex caveat

Regex *flavors* differ across engines in general, but these specific
patterns (`^[a-z][a-z0-9_]*$` and friends) use only anchors and simple
character classes — the common subset of JS, Java, and .NET regex, so the
pattern string itself is portable. The real asymmetry is that the C#
runtime deliberately does **not** depend on `System.Text.RegularExpressions`
(netstandard2.0 / Unity size), so it hand-scans. Sharing the pattern
therefore means: Java and TS consume it as a real regex; C# keeps its
char-scan plus an equivalence test asserting the scan matches the shared
pattern.

## Recommended mechanism — emit, don't load

The source of truth stays `codegen/core/src/ir.ts`. A codegen step
projects the movable constants into a generated file per language — the
same "emit + byte-golden" pattern that already produces the DTOs, the
Java model, and the manifest. No new runtime loader:

- **TypeScript** already imports `ir.ts` directly — no change.
- **Java** gets a generated `Protocol` constants class. The existing
  Jackson manifest reader is untouched; no new loader is introduced.
- **C#** gets a generated, byte-golden `ProtocolConstants.g.cs` (plus its
  Unity mirror); the hand-written runtime references it. The C# runtime
  does **not** read a manifest at runtime — it is built from the fluent /
  generated definition — so generated code is the only way to reach it.
  Regexes stay as the C# char-scan (no `Regex` dependency), guarded by an
  equivalence test against the shared pattern.

The **drift test is the byte-golden check**: re-run the emitter and
compare to the committed `Protocol.java` / `ProtocolConstants.g.cs`, in
`tests/cross-language-golden/`, exactly like the existing generated
sources. Because the constants are emitter output, a separate
`protocol-v1.json` file is not required; it could be added as a neutral
intermediate if useful, but `ir.ts` as the single source plus generated
projections is sufficient.

## Phasing (implementation left to a future owner-approved plan)

- **Phase 1 (highest value):** the binding-type compatibility matrix, the
  built-in function list, and the `pending` sentinel → generated constant
  files; this also *adds* the missing binding-type check to TS and C#.
- **Phase 2:** the `halt`/`fail` verbs, feature vocabulary, and the
  identifier / method / sql-name regex strings (Java + TS use them as real
  regexes; C# keeps its char-scan + equivalence test).
- **Out of scope:** the `requiredApiLevel >= 1` threshold (trivial inline,
  no drift) and the whole SQL tokenizer / analyzer — see non-goals.

## Non-goals

- **The SQL tokenizer / analyzer stays per-language.** It is an imperative
  lexer/parser (quote and comment state, paren-depth counting, alias
  skipping) — not reducible to a value, list, or regex. Its cross-language
  drift is instead caught by the shared conformance corpus
  (`fixtures/payloads/expectations.json`), which every validator runs
  against; that fixture set is the "executable spec" that keeps the three
  hand-written ports aligned without shared code.
- **No shared runtime / WASM / transpile.** Only parameters move; every
  loop stays where it is, so the tiny-binary and IL2CPP story is unchanged.

## What this buys

One edit site per parameter instead of N; the byte-golden test turns any
future drift into a red build instead of a review-round bug; and TS/C#
gain the binding-type check they currently lack. The cost is a small
codegen addition (one emitter output + one golden per language) and,
for C#, the existing generated-code discipline it already follows.
