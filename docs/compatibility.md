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

## App size (measured: NativeAOT, and Unity IL2CPP)

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
don't. The self-contained re-measurement protocol for a Unity-equipped
agent (bench kit, hypothesis ledger incl. the two findings that can
genuinely differ under IL2CPP, build matrix, report template) is
`docs/guides/il2cpp-size-protocol.md`; it has now been executed —
**`docs/reports/il2cpp-size-report.md`** pins the real IL2CPP numbers
(Unity 2022.3.9f1, Android/ARM64, Managed Stripping High), reproduced
below.

Measured deltas over the game-like baseline (managed core is ~80 KB of
IL; the cost is AOT type metadata + EH tables, **not** string literals
— total SQL-ish literal bytes in the binary measured under 0.5 KB, and
the unreferenced `GeneratedSchemaSql` constant strips):

| Stack | raw Δ | gzip Δ (download) |
|---|---|---|
| classic profile, 5 methods | 434 KB | 194 KB |
| **compact profile, 50 methods** | **215 KB** | **88 KB** |
| compact + `SQLITEHOST_SLIM`, 50 methods | 187 KB | 75 KB |
| ultra profile, 50 methods | 188 KB | 81 KB |
| **ultra + `SQLITEHOST_SLIM`, 50 methods** | **159 KB** | **66 KB** |

Measured under **real Unity IL2CPP** (2022.3.9f1, Android/ARM64,
Managed Stripping High, IL2CPP codegen "Faster (smaller) builds";
Δ = `libil2cpp.so` + `global-metadata.dat` over the same game-like
baseline — full matrix, validity checks and per-method costs in
`docs/reports/il2cpp-size-report.md`):

| Stack (50 methods) | IL2CPP raw Δ | IL2CPP gz Δ (download) |
|---|---|---|
| classic profile | 714 KB | 152 KB |
| **compact profile** | **476 KB** | **120 KB** |
| compact + `SQLITEHOST_SLIM` | 444 KB | 108 KB |
| ultra profile | 365 KB | 98 KB |
| **ultra + `SQLITEHOST_SLIM`** | **323 KB** | **84 KB** |

The 5/50-method pair separates fixed cost from marginal cost under
IL2CPP: marginal per-method is classic **9.1 KB** → compact **4.9 KB**
→ ultra **1.8 KB** raw (fixed runtime: 259 / 232 / 275 KB). Same
"unique-type count drives cost" mechanism as NativeAOT — but note the
crossover: **ultra's fixed cost is the largest, so under IL2CPP it only
beats compact above ~14 methods raw (~21 gzipped); small hosts
(≲15 methods) should prefer compact**. Full decomposition in
`docs/reports/il2cpp-size-report.md`.

Two structural findings drove the architecture here (an earlier
revision measured 474 KB raw / 204 KB gzip for the compact-50 stack):

1. **No generic virtual methods, anywhere on the hot path.** A generic
   method on an interface forces AOT compilers to carry their dynamic
   type loader; combined with generic runtime/definition types it cost
   ~250 KB (super-additive: removing either alone recovered ~8 KB,
   removing both collapsed the type-loader machinery to the
   baseline stub — 2898 → 296 symbols). Hence
   `ISqliteHostConnection.QueryRows` is non-generic by contract and the
   runtime/definition cores are non-generic internally (thin typed
   wrappers only). **IL2CPP footnote** (measured,
   `docs/reports/il2cpp-size-report.md`): under Unity IL2CPP the
   marginal cost of one generic virtual method is only **~2.9 KB raw /
   ~5.3 KB gz** — IL2CPP ships its generic-sharing/metadata machinery
   unconditionally, so the ~250 KB structural win is
   **NativeAOT-specific**. The non-generic contract stays (strictly ≤
   everywhere; NativeAOT and .NET-server consumers keep the large win).
2. **Per-method cost is type count.** Each unique generic
   instantiation/lambda class ≈ 700–900 B of AOT metadata: classic
   ≈ 10 KB raw / 4.6 KB gzip per method, compact ≈ 1.2 / 0.3, ultra
   ≈ 0.7 / 0.2.

Guidance: size-critical games generate with `--profile compact`
(identical typed public API, identical behavior — pinned by the
profile-equivalence tests); `--profile ultra` when the last tens of KB
matter more than compile-time payload typing; add `SQLITEHOST_SLIM`
(see `docs/csharp-api.md`) on final builds only. **Unity IL2CPP
targets should additionally pass `--dto-fields`** (public fields
instead of DTO auto-properties: ~32 KB raw / ~12 KB gz smaller on a
50-method host under IL2CPP, free under NativeAOT, identical usage
code). The engine itself can
cost zero additional bytes on iOS/Android by consuming the system
libsqlite3 through `SqliteHost.Adapters.Native`. Reflection-free
NativeAOT (`IlcDisableReflection=true`) builds and runs the full
runtime — consistent with the no-reflection source guard — so maximum
managed stripping settings are safe.

### At the floor: whole-app trim flags help the game, not us

The compact/ultra + SLIM numbers above are the floor for *SqliteHost's
own contribution*. A second empirical round confirmed there is no
DX-neutral generated-code win left: converting DTO auto-properties to
fields saved 0 bytes under NativeAOT (already inlined), and
data-driving the 50 registration bodies **grew** the binary (in
NativeAOT a delegate-array initializer is code, not data, and the
near-identical fluent bodies were already almost free under gzip's
window). **IL2CPP footnote**: the fields result does *not* transfer —
under IL2CPP the fields variant measured **~32 KB raw / ~12 KB gz
smaller** on the 50-method host (IL2CPP emits per-accessor C++ +
metadata that survives High stripping). Small next to the per-method
totals, and auto-properties remain the shipped default for DX; noted
here in case a future `--fields` generator switch is ever worth it for
size-critical Unity consumers (`docs/reports/il2cpp-size-report.md`). The one remaining
code-shape lever — collapsing the handler interface to a single
ordinal `Invoke(int, …)` dispatch — saves ~4.8 KB gzip but changes the
handler-authoring surface (you write a `switch` instead of named
methods), so it is deliberately **not** shipped; the typed profiles
keep their DX.

Aggressive whole-app AOT flags (`StackTraceSupport=false`,
`UseSystemResourceKeys=true`, `IlcOptimizationPreference=Size`,
`IlcFoldIdenticalMethodBodies=true` — bundled in
`csharp/SqliteHost.Publish.Nano.props` to import into your game's
publish project) cut ~57 KB gzip off a real game binary — but only
~3 KB of that is SqliteHost's delta (66 → 63.5 KB gzip ultra+slim,
75 → 72 KB compact+slim). The rest is the game's own exception/reflection
metadata. The takeaway is the honest one: **the runtime is already at
its AOT floor** — it is reflection-free and lean enough that maximal
stripping finds almost nothing more to remove from it. Those flags are
worth setting for the whole app's sake, and SqliteHost is fully
compatible with all of them; they are not a SqliteHost-specific win.

## Java — 17+

Generated/handwritten Java targets release 17 (records allowed,
standard collections, JDBC validation adapters). No Spring dependency
in core modules; a Spring Boot starter would be a separate module
(ROADMAP).

## TypeScript — 5+

Tooling/authoring only; the core runtime never requires Node.js.
