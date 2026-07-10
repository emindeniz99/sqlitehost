# Compatibility targets

## SQLite — required floor 3.19.3, engine-verified to 3.9.0

Two tiers, deliberately distinct:

- **Required floor: 3.19.3.** This is the contract. It bounds the
  runtime's own generated SQL **and** tells script authors which SQLite
  feature set they may assume in their payload SQL (e.g. row values,
  3.15+, are inside the floor; window functions, 3.25+, are not).
  Lowering the floor would shrink what script authors can rely on, so
  it stays at 3.19.3 even though the runtime itself needs less.
- **Engine-verified tier: 3.9.0.** The full C# suite passes on real
  3.9.0 and 3.9.2 builds and both stay in the CI matrix permanently.
  This is a measured fact consumers below the floor can use at their
  own judgment — their own script SQL, not ours, becomes the limiting
  factor. It is not a contractual promise.

Generated SQL and runtime features must work on SQLite 3.19.3. Do not
require JSON1, window functions, UPSERT, `RETURNING`, `STRICT` tables,
modern-only SQL functions, or a custom SQLite build. Constructs used
and their minimum versions: `AUTOINCREMENT`, `AFTER INSERT` triggers,
composite primary keys, multi-row `VALUES` (3.7.11), named parameters
(`:name`/`@name`/`$name`) — all well below 3.19.3.

Compatibility is enforced by policy **and by measurement**:
`tests/compatibility-sqlite/run-matrix.sh` compiles real SQLite
amalgamations and runs the full C# suite against each binary through a
SQLitePCLRaw dynamic provider (with `sqlite_version()` identity
assertions). Measured results:

| SQLite binary | suite result | UPSERT canary (3.24+) | RETURNING canary (3.35+) |
|---|---|---|---|
| 3.9.2 (below floor) | PASS | throws | throws |
| 3.19.3 (floor) | PASS | throws | throws |
| 3.28.0 | PASS | succeeds | throws |
| 3.53.3 (newest) | PASS | succeeds | succeeds |

The canary tests prove the harness actually detects version gates.
Honest note: the suite also passes on 3.9.2 because the generated SQL
uses only ancient constructs — the supported floor remains **3.19.3 by
policy**; older-version passes are informational, not a promise. See
`tests/compatibility-sqlite/README.md`.

The C# integration fixtures additionally run on three adapters:
Microsoft.Data.Sqlite (ADO.NET), System.Data.SQLite (ADO.NET), and
sqlite-net-pcl (wrapper `Handle` + SQLitePCL.raw — the Unity/
SQLite4Unity3d adapter pattern). The Java suite runs on xerial
sqlite-jdbc (bundled modern SQLite).

## C# / Unity — Unity 2021 LTS and newer

Pinned verification targets: **Unity 2021.3.55f1 and 2022.3.39f1**
(the latest LTS patch releases of each line at the time of writing) —
the in-editor spike (see `docs/guides/unity-2021-spike.md`) should be
run on both.

### Why netstandard2.0 (2.1 evaluated and declined)

Unity 2021.3/2022.3 support the .NET Standard 2.1 API compatibility
level, so targeting 2.1 would work — but it buys nothing here: the 2.1
additions (Span/Memory APIs, default interface members,
IAsyncEnumerable, Index/Range) are exactly the features the
Unity-safe subset policy already bans from this codebase, and a
netstandard2.0 library loads unchanged in a 2.1 profile. Staying on
2.0 keeps the packages consumable from older Unity (2018.1+) and
.NET Framework 4.6.2+ at zero cost. Revisit only if a concrete 2.1
API would simplify real code.

`SqliteHost.Abstractions` and `SqliteHost.Runtime` target
`netstandard2.0`, C# 8 subset: no records, no `required`, no `init`, no
default interface members, no `System.Text.Json`, no source generators,
no modern hosting abstractions. Ordinary classes, interfaces,
delegates, lists, explicit null checks. An in-Unity compile spike is a
ROADMAP item (no Unity available in this environment); the source is
kept vendorable (copy the two folders + generated sample).

### IL2CPP guardrail

The runtime is delegate/interface-based by construction: no
`Reflection.Emit`, no reflection-dependent row↔DTO mapping (generated
descriptors register compile-time delegates), no dynamic code
generation — enforced by a source-level guard test. Therefore
`[Preserve]` attributes and `link.xml` are **not required** for the
runtime under IL2CPP code stripping in v1. (The Unity sample's
`SmokeRunner` uses one reflection type-lookup for demo convenience —
that is sample-only; see `docs/guides/unity-2021-spike.md`.) If a
future implementation ever introduces reflection-based mapping,
Preserve/link.xml guidance becomes mandatory at that point.

## Java — 17+

Generated/handwritten Java targets release 17 (records allowed,
standard collections, JDBC validation adapters). No Spring dependency
in core modules; a Spring Boot starter would be a separate module
(ROADMAP).

## TypeScript — 5+

Tooling/authoring only; the core runtime never requires Node.js.
