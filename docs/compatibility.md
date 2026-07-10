# Compatibility targets

## SQLite — minimum 3.19.3

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
