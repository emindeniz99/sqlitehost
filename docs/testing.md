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
unzip plus an amalgamation cache; the four pinned versions are SHA-256
pinned, because the script compiles and executes source it downloaded.
`engine-matrix.yml` runs it nightly and on any pull request touching
`csharp/` or the harness — one leg per version, so a failure names the
engine. Pass a version to run one leg locally:
`bash tests/compatibility-sqlite/run-matrix.sh 3.19.3`. See
`docs/compatibility.md` for the measured results.

## App size (`tests/app-size-bench`)

`generate.mjs` writes two synthetic hosts (50 and 5 methods) through the
repo's own emitters; `measure-nativeaot.mjs` publishes every row under
.NET 8 NativeAOT and checks the size claims in `docs/compatibility.md`.
What it enforces are ratios computed in one run — profile ordering,
falling per-method cost, DTO fields as a no-op, `SQLITEHOST_SLIM` a net
win, and the reflection-free build still running — so it is immune to
SDK and architecture drift. Byte-for-byte regression against
`baseline.json` also fails the job: the recorded deltas were measured on
an ubuntu-latest runner, and a change that is supposed to move bytes
re-records them with `UPDATE_SIZE_BASELINE=1` on a runner. The Unity IL2CPP half is a monthly
measurement, not a gate: `il2cpp-size-bench.yml` builds the 12-row
matrix in a real editor and publishes a table. `ios-size-bench.yml` is
written to build the same rows for iOS and publish a second table whose
bytes are not comparable to the Android one, but it has never run and no
iOS number exists yet — `docs/guides/il2cpp-size-protocol.md` §7 says why
the two platforms are not comparable, and lists what only a first run can
settle.

## Playground browser tests (`typescript/playground`)

Playwright end-to-end tests over the built web bundle, Chromium only.
They need a browser download, which is why they are a separate workflow
(`playground-e2e.yml`) rather than a job in the main matrix — but they
do run on every pull request. To run them locally, see
`CONTRIBUTING.md`; CI is actually the more reliable path, because
`playwright install` derives the browser revision from the pinned
`@playwright/test` instead of whatever is already on the machine.

## Packaging (`packaging.yml`)

The steps that used to execute for the first time on a release tag:
`mvn -P central verify` (sources + javadoc, GPG skipped — javadoc is
strict and a failure at tag time means re-cutting the release),
`scripts/check-nupkg-shape.sh` (`dotnet pack` the five NuGet projects
and assert the nuspec metadata nuget.org requires), and
`scripts/check-pack-shape.mjs` (`pnpm pack` the three npm packages and
assert every path `package.json` promises is in the tarball). None needs
a credential. They run weekly and on pull requests that touch what they
guard.

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
| `goldens` | emitter goldens, delivery goldens, `unity/sync.mjs --check`, vendor-trim, version lockstep |
| `app size (NativeAOT)` | every bench row published and measured; the size claims that are ratios |
| `zizmor` | workflow security lint |

`.github/workflows/unity-ci.yml` compiles `com.sqlitehost.runtime`
inside eight real Unity editors, one after another, and runs its EditMode
tests in each: the 2021.3.45f2 floor, 2022.3.62f3, and the six Unity 6
lines (6000.0.82f1, 6000.1.17f1, 6000.2.15f1, 6000.3.22f1, 6000.4.12f1,
6000.5.9f1). Those are the versions a free personal licence can activate;
`docs/compatibility.md` lists the lines it therefore cannot reach. The job
needs the licence secrets, which GitHub does not pass to fork pull
requests, so a fork gets the licence-free scaffold-guard instead.

Five more workflows carry the suites that do not belong in the main
matrix, each at the cadence its cost justifies:

| Workflow | Cadence | What |
|---|---|---|
| `playground-e2e.yml` | per-PR | the 13 Playwright tests, after installing exactly one Chromium |
| `packaging.yml` | per-PR on the paths it guards, plus weekly | maven `central` profile, `dotnet pack`, `pnpm pack` shape checks |
| `engine-matrix.yml` | nightly, plus per-PR on `csharp/**` | the real-SQLite matrix, one leg per engine version |
| `il2cpp-size-bench.yml` | monthly + on demand | the Unity IL2CPP app-size matrix on Android (a measurement, never a gate) |
| `ios-size-bench.yml` | monthly + on demand | the same rows on iOS, in two stages (Unity emits an Xcode project, a Mac compiles it) — a measurement, and it has never run |

So everything in `tests/end-to-end/run-all.sh` now runs in CI — but not
all of it on every push. A change outside `csharp/` does not wait for the
engine matrix, and nothing waits for the IL2CPP matrix.

One check deliberately stays out of pull-request CI:
`scripts/check-npm-publishable.mjs` exits 1 today by design, because the
three publishable manifests still carry `"private": true` as the
registry-bootstrap gate. It runs in `release.yml`, where failing is the
point, and as an advisory weekly job in `packaging.yml`.
