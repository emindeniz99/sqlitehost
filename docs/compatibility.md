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

| SQLite binary | result |
|---|---|
| 3.9.0 / 3.9.2 (below floor) | PASS — every adapter-level test (full conformance incl. scalar functions) passes; runtime-driven suites skip by design (the default host floor 3019003 gate refuses the engine — itself asserted by FloorGateTests) and a lowered-floor host (`MinSqliteVersion(3009000)`) completes a real call→drain→result-read script end-to-end |
| 3.19.3 (floor) | PASS |
| 3.28.0 | PASS |
| 3.53.3 (newest) | PASS |

Canary tests (UPSERT 3.24+, RETURNING 3.35+, OVER 3.25+, iif 3.32+,
json_valid) prove the harness detects version gates; full measured
tables and the below-floor skip policy live in
`tests/compatibility-sqlite/README.md`. "Engine-verified down to
3.9.0" is thereby backed two ways: the adapter surface passes wholesale
on 3.9.0, and a host that declares `minSqliteVersion: "3.9.0"` runs
end-to-end on the real 3.9.0 binary.

The C# integration fixtures additionally run on four adapters:
Microsoft.Data.Sqlite (ADO.NET), System.Data.SQLite (ADO.NET),
sqlite-net-pcl (wrapper `Handle` + SQLitePCL.raw), and
SqliteHost.Adapters.Native (shippable pure-DllImport adapter — the
native-style reference, scalar functions included). The Java suite runs on xerial
sqlite-jdbc (bundled modern SQLite).

## C# / Unity — Unity 2021 LTS and newer

Pinned verification targets: **Unity 2021.3.55f1 and 2022.3.39f1**
(the latest LTS patch releases of each line at the time of writing) —
the in-editor spike (see `docs/guides/unity-2021-spike.md`) should be
run on both.

### C# language level: 8 (9 evaluated and declined)

The runtime packages pin `LangVersion 8`. netstandard2.0 does not
require any particular C# version (the language level is a compiler
setting, independent of the target framework), and lower is always
safe: C# 8 sources compile unchanged under the C# 9 compiler that
Unity 2021.3/2022.3 ship. Moving to C# 9 would buy nothing here — its
headline features are exactly what this codebase bans or can't use:
records and `init` setters (need an `IsExternalInit` shim on
netstandard2.0; banned by the Unity-safe policy anyway), covariant
returns (needs a .NET 5+ runtime, unavailable on netstandard2.0/Unity
Mono), function pointers (unsafe). Pattern-matching sugar alone does
not justify raising the floor. Revisit only if a concrete C# 9+
feature would simplify real code without a shim.

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

## App size (measured; NativeAOT proxy for IL2CPP)

For download-cap-constrained games (App Store cellular threshold:
compressed download size) the runtime's footprint was measured
empirically, not estimated. Method: a "game-like" baseline app that
heavily exercises the BCL (collections, interface dispatch, delegates,
StringBuilder + invariant parse/format, base64/UTF8, sorting, custom
exceptions) is published with .NET 8 **NativeAOT**
(`-p:PublishAot -p:StripSymbols -p:InvariantGlobalization`,
linux-x64), then the same app is republished with SqliteHost actually
executing a script. The delta is the honest marginal cost; gzip -9 of
the binary is the download proxy. NativeAOT is a *proxy* for IL2CPP
(both AOT + whole-program trim) — magnitudes transfer, exact bytes
don't; the Unity spike (ROADMAP) will pin the IL2CPP numbers.

Measured deltas over the game-like baseline (managed core is ~80 KB of
IL; the multiplier is AOT type metadata + EH tables, **not** string
literals — total SQL-ish literal bytes in the binary measured under
0.5 KB, and the unreferenced `GeneratedSchemaSql` constant strips):

| Stack | raw Δ | gzip Δ (download) |
|---|---|---|
| classic profile, 5 methods | 464 KB | 211 KB |
| classic profile, 50 methods | 914 KB | 411 KB |
| **compact profile, 50 methods** | **474 KB** | **204 KB** |
| **ultra profile, 50 methods** | **446 KB** | **198 KB** |

Per additional method: classic ≈ 10 KB raw / 4.6 KB gzip — the cost of
the per-method generic instantiations and lambda display classes the
classic fluent surface creates (each unique type ≈ 700–900 B of AOT
metadata). The compact profile (typed DTOs kept, accessors pre-erased
static methods) cuts that to ≈ 1.2 KB raw / ≈ 0.3 KB gzip; ultra
(no DTOs) to ≈ 0.7 KB raw / ≈ 0.2 KB gzip. At 50 methods the whole
compact stack costs less download than the 5-method classic stack.

Guidance: size-critical games generate with `--profile compact`
(identical typed public API, identical behavior — pinned by the
profile-equivalence tests); `--profile ultra` only when the last
~100 KB matter more than compile-time payload typing. The engine
itself can cost zero additional bytes on iOS/Android by consuming the
system libsqlite3 through `SqliteHost.Adapters.Native`. Reflection-free
NativeAOT (`IlcDisableReflection=true`) builds and runs the full
runtime — consistent with the no-reflection source guard — so maximum
managed stripping settings are safe.

## Java — 17+

Generated/handwritten Java targets release 17 (records allowed,
standard collections, JDBC validation adapters). No Spring dependency
in core modules; a Spring Boot starter would be a separate module
(ROADMAP).

## TypeScript — 5+

Tooling/authoring only; the core runtime never requires Node.js.
