# Testing strategy

What each suite covers, and which of them CI runs. The C# legs run on
Linux and Windows; everything else runs on Linux, plus a Unity editor
job for the UPM package.

## C# (`csharp/SqliteHost.Tests`, `dotnet test`)

Unit: scalar required/optional mapping, all five binding types, named
parameter prefixes (`:` `@` `$`), naming convention derivation, schema
generation (golden vs `fixtures/schemas/sample-host.ddl.sql`), queue
order, handler success/failure, generated interface invocation, list
input/output mapping incl. empty lists and `item_index` ordering,
unsupported engine/API/feature/method clean skips, binding validation
(missing/unused), statement/pending-call limits.

Integration (real `Microsoft.Data.Sqlite` adapter, in-memory
workspace): execute the parsed fixture scripts end-to-end with fake
handlers — read → conditional write → read-after-write, list roundtrip,
runtime inputs, blob roundtrip — verifying result rows feed later SQL
and diagnostics are populated.

## TypeSpec/codegen (`pnpm -r test`, node:test)

Decorator parsing, tsp→IR normalization (golden IR), manifest emission
byte-equal to the committed manifest, DDL emission byte-equal to the
committed snapshot, sqlName overrides, naming-prefix changes
propagating, unsupported-model diagnostics, deterministic output
(emit twice → identical), emitter goldens byte-equal to the committed
generated sources in `csharp/`, `java/`, `typescript/`.

## Java (`mvn test`)

Envelope/manifest JSON model round-trips; DDL generator golden vs the
snapshot; prepare-only validation catches bad table/column/syntax;
binding lint (missing/unused/type-mismatch); lineage lints; list
colocation lints; required-methods lints; explicit-column-list lint —
all driven by `fixtures/payloads/expectations.json` (the `java`
validator entries).

## TypeScript (`pnpm -r test`)

Envelope type guards + JSON round-trip; authoring builder produces
fixture-identical payloads; static lint subset driven by
`expectations.json` (`typescript` entries); metadata/autocomplete
tables match the manifest.

## Cross-language golden (`tests/cross-language-golden`)

One runner that rebuilds all emitter outputs and diffs against the
committed fixtures + vendored sources; fails on any byte difference.
Identical manifest, identical DDL, identical envelope contract,
identical table/column names, identical optional/required semantics,
identical API-level metadata.

## Script delivery (`tests/delivery-golden`)

Envelopes signed by the TypeScript signer are verified byte-for-byte by
`SqliteHost.Delivery` under .NET, against the committed
`fixtures/delivery/` matrix.

## Vendoring (`unity/sync.mjs --check`, `tests/vendor-trim`)

The UPM copies under `unity/com.sqlitehost.runtime/` must match
`csharp/`, and each vendoring profile must compile on its own.

## Engine matrix (`tests/compatibility-sqlite/run-matrix.sh`)

Compiles real SQLite amalgamations (3.9.0 through the newest release)
and runs the full C# suite against each binary. It needs gcc, curl and
unzip plus a local amalgamation cache, so it runs on demand rather than
in CI — see `docs/compatibility.md` for the measured results.

## Playground browser tests (`typescript/playground`)

Playwright end-to-end tests over the built web bundle. They need a
browser download, so they are outside the main matrix too; run them
locally per `CONTRIBUTING.md`.

## End-to-end (`tests/end-to-end`)

Orchestrates the full matrix locally: `dotnet test`, `mvn -q test`,
`pnpm -r test`, then the cross-language golden runner.

## What CI runs

`.github/workflows/ci.yml` on every push and pull request:

| Job | What |
|---|---|
| `node 20/22/24/26` | `pnpm -r test` across the declared Node lines |
| `jdk 17/21/25` | `mvn -q test` across the LTS lines at or above the pom floor |
| `dotnet (ubuntu-latest, windows-latest)` | `dotnet test` — runtime, adapters, integration fixtures |
| `goldens` | emitter goldens, delivery goldens, `unity/sync.mjs --check`, vendor-trim |
| `zizmor` | workflow security lint |

`.github/workflows/unity-ci.yml` compiles `com.sqlitehost.runtime`
inside a Unity 2021.3.45f2 editor and runs its EditMode tests; it needs
the licence secrets, which GitHub does not pass to fork pull requests.

Neither workflow runs the engine matrix or the playground browser
tests. Run those two locally.
