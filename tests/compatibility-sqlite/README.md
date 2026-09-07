# Real-SQLite compatibility matrix (C# suite)

Runs the full C# test suite (`csharp/SqliteHost.sln`) against **real SQLite
builds** compiled from the official amalgamations, instead of the modern
e_sqlite3 that SQLitePCLRaw bundles. This turns the "3.19.3 floor by
banned-feature policy" of `docs/compatibility.md` into a measured result.

## How to run

```bash
bash tests/compatibility-sqlite/run-matrix.sh            # every version
bash tests/compatibility-sqlite/run-matrix.sh 3.19.3     # just the floor
bash tests/compatibility-sqlite/run-matrix.sh latest     # just the newest
```

Requirements: `gcc`, `unzip`, `curl`, and `dotnet` (found on `PATH` or at
`/opt/dotnet`). Amalgamation zips and compiled `libsqlite3-<ver>.so` files
are cached under `.cache/` (gitignored via the local `.gitignore`); delete
that directory to force fresh downloads/builds.

Versions tested: pinned `3.9.0`, `3.9.2`, `3.19.3` (the documented floor),
`3.28.0`, plus the **newest** amalgamation resolved at runtime from
`https://sqlite.org/download.html` (fallback pin if the page is unreachable:
3.53.3, the newest at the time this harness was written).

Each pinned version also carries the SHA-256 of its amalgamation zip, and
the script verifies it on every run — including after a cache restore. This
downloads C source and then compiles and *executes* it, so the bytes are
pinned, not just the URL. The runtime-resolved newest version has no
checksum by construction; that is why CI treats it as an advisory leg.

## In CI

`.github/workflows/engine-matrix.yml` runs this nightly, on any pull
request touching `csharp/` or this directory, and on demand. One matrix leg
per version — that is what the version argument above exists for — so a red
run names the engine. Only the source zip is cached, per version, and never
the compiled `.so`: a cache entry proves nothing about its own contents, and
this harness loads that library into the test process. Compiling from an
archive that just passed its pin costs about a minute per leg and is what
the pins are for. The advisory "latest" leg is not cached at all, because
its key would have to change on exactly the upstream release that makes the
cache useless.

The script exits non-zero if **any** version fails. 3.9.0 and 3.9.2 are
below the documented floor, but their rows are expected green too: on those
engines the runtime-driven suites skip with an explicit reason and
`FloorGateTests` asserts the version gate itself (see "Below-floor rows"
below).

## How it works

For each version the script sets three environment variables and runs
`dotnet test`:

- `SQLITEHOST_NATIVE_SQLITE=<path>/libsqlite3-<ver>.so` — two independent
  `[ModuleInitializer]`s in the test assembly honor it:
  - `csharp/SqliteHost.Tests/Adapter/NativeSqliteOverride.cs` installs a
    `SQLitePCLRaw.provider.dynamic_cdecl` provider that resolves every
    `sqlite3_*` export from that library via
    `System.Runtime.InteropServices.NativeLibrary`, then calls
    `raw.FreezeProvider()` so the `SQLitePCL.Batteries_V2.Init()` that
    `Microsoft.Data.Sqlite` / sqlite-net trigger later becomes a no-op;
  - `csharp/SqliteHost.Tests/Adapter/NativeAdapterLibraryResolver.cs`
    installs a `NativeLibrary.SetDllImportResolver` on the
    `SqliteHost.Adapters.Native` package's assembly that maps its
    `DllImport("sqlite3")` to the same library, so the direct-P/Invoke
    adapter runs against the identical pinned build (without the variable
    it falls back to the newest cached matrix build under `.cache/`, then
    the system `libsqlite3.so.0`, then default loader resolution).
- `SQLITEHOST_EXPECTED_SQLITE_VERSION=<ver>` —
  `SqliteVersionTests.SqliteVersion_MatchesExpectedVersion_WhenNativeOverrideIsActive`
  then asserts `SELECT sqlite_version()` **equals** this value, proving the
  dynamic provider actually loaded the requested binary, and
  `NativeSqliteVersionTests.SqliteLibVersion_MatchesExpectedVersion_WhenNativeOverrideIsActive`
  asserts the native adapter's `sqlite3_libversion()` equals it too (both
  passed in every matrix cell below).
- `SQLITEHOST_EXPECTED_SQLITE_VERSION_NUMBER=<n>` — the same identity in
  the `sqlite3_libversion_number` encoding
  (`major*1000000 + minor*1000 + patch`), derived deterministically by the
  script from the dotted version; `SqliteVersionTests` and
  `NativeSqliteVersionTests` each derive the number from their version
  string the same way and assert equality (also passed in every cell).

Adapter scoping: matrix runs execute the integration fixtures, the inline
function matrix, and the adapter conformance suite on the **two adapters
that honor the override** — Microsoft.Data.Sqlite (SQLitePCLRaw dynamic
provider) and SqliteHost.Adapters.Native (DllImportResolver). The
System.Data.SQLite adapter has its own interop + bundled native and never
sees either mechanism; the sqlite-net adapter technically shares the
SQLitePCLRaw provider but is skipped too so each matrix cell exercises the
overridable adapters against exactly one known native build. Each excluded
mirror carries 87 tests — 35 integration-fixture cases, 40 adapter
conformance tests and 12 inline-function cases — and reports them as
*skipped* with an explicit reason, bar the two `CleanSkip_*` inline cases
that never open a connection. The per-leg totals are in the skip budget
below. A normal `dotnet test` run is 775 tests: 769 pass and 6 skip, the
6 being the override-only version-identity tests, two per overridable
adapter, plus the two below-floor-direction `FloorGateTests` that only run
on engines older than the sample floor.

### Negative canaries (banned features)

Canary meta-tests (`VersionCanaryTests`, always run) prove the harness can
detect version differences by branching on the real runtime
`sqlite_version()`:

- UPSERT (`ON CONFLICT ... DO UPDATE`, introduced 3.24) and `RETURNING`
  (introduced 3.35) must **throw** below their introducing version and
  **succeed** at/above it.
- Window functions (`COUNT(*) OVER ()`, introduced 3.25) and `iif()`
  (introduced 3.32) — prepare-level canaries, same throw/succeed branching.
- `json_valid('{}')` is a **compile-option** canary, not a pure version
  gate: before 3.38, JSON1 was opt-in (`-DSQLITE_ENABLE_JSON1`), and our
  matrix `gcc` builds compile the plain amalgamation with no `-D` flags, so
  under a matrix-built native the canary asserts **fail below 3.38** and
  **success at/above 3.38** (where JSON became built-in by default). Under
  the bundled e_sqlite3 (no override) the build flags are not ours to pin,
  so the test only probes and accepts either outcome — a strict assert
  there would be capability-dishonest.

All these features remain banned from SqliteHost's generated SQL.

### Positive prepare canaries (allowed constructs)

`PositivePrepareTests` prepares (never executes) allowed constructs against
the generated workspace schema on the current engine: recursive CTE
(`WITH RECURSIVE`, 3.8.3), `CASE`, scalar subquery, `EXISTS`, multi-row
`VALUES` (3.7.11), and `printf()` (3.8.3). All must prepare on every matrix
version — measured: they prepared successfully on every cell **including
3.9.0**. (The generated DDL + trigger are executed, not just prepared, by
the integration fixtures in the same cells.)

### Inline scalar functions (feature inlineFunctions)

Both overridable adapters implement the optional scalar-function
capability, so every matrix cell also measures
`sqlite3_create_function`: Microsoft.Data.Sqlite through
`SqliteConnection.CreateFunction`, and SqliteHost.Adapters.Native through
raw `sqlite3_create_function_v2` (available since SQLite 3.7.3; the
underlying `sqlite3_create_function` predates 3.0). The adapter-level
capability section of the conformance suite — result round-trips for
every binding type, per-arity registration, UTF-8 arguments, and the
`SQLITEHOST_HANDLER_ERROR:` marker path — **passed on every engine
including 3.9.0**; user-defined functions are ancient SQLite surface. The
runtime-driven inline scenarios pass from the 3.19.3 floor upward (below
it they skip via the below-floor policy like everything runtime-driven,
see below).

## Measured results (2026-07-12, linux-x64, gcc -O2 default amalgamation)

Every cell: `sqlite_version()` string **and** derived numeric version
confirmed via the identity tests on both overridable adapters; full run
`bash tests/compatibility-sqlite/run-matrix.sh` exited 0.

| SQLite  | Tests | UPSERT (3.24) | RETURNING (3.35) | OVER (3.25) | iif() (3.32) | json_valid (build) | positive prepare |
|---------|-------|---------------|------------------|-------------|--------------|--------------------|------------------|
| 3.9.0   | PASS — 347 passed, 0 failed, 196 skipped (of 543 at that date; the skip budget below carries today's arithmetic) | threw | threw | threw | threw | threw (no JSON1) | all prepared |
| 3.9.2   | PASS — 347 passed, 0 failed, 196 skipped (of 543 at that date; the skip budget below carries today's arithmetic) | threw | threw | threw | threw | threw (no JSON1) | all prepared |
| 3.19.3  | PASS — 449 passed, 0 failed, 110 skipped | threw | threw | threw | threw | threw (no JSON1) | all prepared |
| 3.28.0  | PASS — 449 passed, 0 failed, 110 skipped | succeeded | threw | succeeded | threw | threw (no JSON1) | all prepared |
| 3.53.3 (newest) | PASS — 449 passed, 0 failed, 110 skipped | succeeded | succeeded | succeeded | succeeded | succeeded (built-in) | all prepared |

### Below-floor rows: skip policy + FloorGateTests

The sample host definition pins `MinSqliteVersion(3019003)`, so on an
engine older than 3.19.3 the runtime's workspace version gate
(`sqlite-version-too-low`, docs/errors.md) refuses every run before any
DDL. That is designed behavior, and it used to surface as an informational
failure in every runtime-driven test of a 3.9.x cell, all tripping the
same gate. Those rows are now meaningfully green instead:

- **Runtime-driven tests skip with a reason.** The integration fixtures,
  the drain/mapping/float/list/control/validation/naming/columns suites,
  and the runtime-driven inline scenarios call the shared
  `SampleHostFloor` helper (csharp/SqliteHost.Tests/TestSupport) and skip
  with *"engine 3009000 is below the sample host's floor 3019003: the
  runtime's sqlite-version-too-low gate refuses every run by design; gate
  behavior is covered by FloorGateTests."* The active engine version comes
  from `SQLITEHOST_EXPECTED_SQLITE_VERSION_NUMBER` (the script sets it for
  every cell) or, when unset, a one-time `sqlite_version()` probe.
- **FloorGateTests assert the gate itself, in every cell, on both
  overridable adapters** (csharp/SqliteHost.Tests/FloorGateTests.cs):
  - *Sample floor:* below the floor, a run against the real engine must
    return `FailedSchema`/`sqlite-version-too-low` with zero handler calls
    and an empty workspace (no DDL ran) — the one intentional assertion
    that stands in for that pile of accidental failures. At/above the floor the
    same script must instead complete end to end.
  - *Lowered floor:* a definition built from the same sample method specs
    with `.MinSqliteVersion(3009000)` runs a real
    call → drain → result-read script (queued `getValue`, its drained
    result row read back by the next step to drive a `setValue`) on the
    actual engine and must **complete in every cell, including 3.9.0 and
    3.9.2** — measured on both the SQLitePCLRaw provider path and the
    direct-P/Invoke adapter.
- **Adapter-level tests keep passing on 3.9.x.** The full
  error-surfacing/binding/fidelity conformance sections, the
  scalar-function capability section, prepare canaries, version canaries,
  and the version identity tests all still run and pass below the floor,
  for both overridable adapters.
- **The four runtime-driven conformance tests are excluded by filter in
  below-floor cells.** `UnknownBinding_FailsMissingBinding`,
  `ExtraBinding_FailsUnusedBinding`, `ErrorMidStep_AbortsTheStep`, and
  `ScalarFunction_NullForRequiredArg` live in the shippable
  `SqliteHost.Conformance` base class, whose only skip hook
  (`SkipEntireSuiteReason`) disables the *whole* suite — including the
  adapter-level sections that measurably pass on 3.9.x. Rather than lose
  that coverage, run-matrix.sh passes a `dotnet test --filter` excluding
  exactly those four methods in below-floor cells (16 test cases across
  the four adapter mirrors — which is why those cells report **759** total
  instead of **775**).

The script exits non-zero if **any** row fails, below-floor rows included.

## The skip budget

A leg that skips most of the suite and reports PASS tells you nothing,
and until this budget existed nothing in the matrix looked at the skip
count. An inverted skip predicate — `SkipUnderNativeOverride`,
`SampleHostFloor.IsBelowFloor` — would have emptied a cell and left it
green. run-matrix.sh now fails a leg whose `Skipped` exceeds a ceiling or
whose `Passed` falls below a floor, and prints what to recompute.

Where the skips come from (measured 2026-09-07 from CI run
[`34071163849`](https://github.com/emindeniz99/sqlitehost/actions/runs/34071163849)
on commit `b3eb429` — the current `main` tip, already past PR #43
(`4a96b78`) and PR #44 (`b3eb429`) — via `gh run view 34071163849 --repo
emindeniz99/sqlitehost --job <job-id> --log` for each of the five leg job
IDs from `gh run view 34071163849 --repo emindeniz99/sqlitehost`. The
suite holds 775 tests at or above the floor, 759 below it once the
four-method filter applies; counts are platform-independent because every
skip below is a `Skip.If`, not a load failure):

| leg | Passed | Skipped | = System.Data.SQLite | + sqlite-net | + engine-specific |
|---|---|---|---|---|---|
| latest / 3.53.4 (≥ 3.39, advisory) | 603 | 172 | 85 | 85 | 2 |
| 3.28.0 (≥ 3.24, < 3.39) | 601 | 174 | 85 | 85 | 4 |
| 3.19.3 (floor, < 3.24) | 599 | 176 | 85 | 85 | 6 |
| 3.9.0 / 3.9.2 (below floor, Total 759) | 422 | 337 | 81 | 81 | 175 |

- **System.Data.SQLite and sqlite-net skip whole.** Both bundle or load
  their own SQLite, so `SQLITEHOST_NATIVE_SQLITE` never reaches them and
  a cell that ran them would be measuring the wrong engine. That is 170
  of every at-or-above-floor leg's skips (85 apiece: two adapter mirrors
  out of four); below the floor it is 162 (81 apiece), because the
  four-method filter below removes 4 tests from each adapter's count
  before either skip or pass is possible.
- **2 more on every leg:** `FloorGateTests`' below-floor branch, on the
  two overridable adapters. Its at/above-floor branch runs instead.
- **2 more below 3.24:** `example-011-insert-alias` needs the UPSERT-era
  `INSERT INTO t AS alias`, which 3.19.3 cannot parse
  (`FixtureCoverage.ValidEngineFloors`).
- **2 more below 3.39, plus 2 already counted above:**
  `example-021-above-floor-syntax`, the payload that exercises every
  construct `sqlite-version-too-low-for-syntax` knows about, needs the
  newest of them (`RIGHT JOIN`, `IS DISTINCT FROM`). It runs on all four
  adapter mirrors (4 tests): the 2 on System.Data.SQLite and sqlite-net
  are already inside the 170/162 above; the 2 on the overridable adapters
  pass at/above 3.39 and skip below it — which is why `3.28.0` and
  `3.19.3` carry 2 more engine-specific skips than `latest`, and why
  `latest` alone gained 2 Passed instead.

- **175 more below the floor:** every runtime-driven test on the two
  overridable adapters, skipping itself through `SampleHostFloor` because
  the `sqlite-version-too-low` gate would refuse the run anyway — 173 of
  those pre-date `example-021` (68 are the fixture corpus, 34 payload
  cases × 2 adapters), plus the 2 the new fixture adds on the same two
  adapters, which skip below the floor regardless of the 3.39 threshold.

The other three of the seven new tests since the suite last stood at 768
are the `blank-binding-name`, `non-canonical-base64` and
`null-optional-field` fixtures, which moved out of
`FixtureCoverage.NotEnvelopeFaults` once the precheck and the reader
started refusing them; they run once each, on no adapter, so they add 3
Passed to every leg regardless of floor or the 3.39 threshold — the whole
reason `3.9.0`/`3.9.2` Passed also rose by 3 (419 → 422) even though
they're below both gates. Everything stays far inside the ceilings below.

The ceilings are those numbers plus room for ordinary test growth, and
they sit far below what an inversion produces — an at-floor leg that
started skipping everything runtime-driven would land near the
below-floor 337, past a ceiling of 220:

| band | max Skipped | min Passed |
|---|---|---|
| at or above the floor | 220 | 520 |
| below the floor | 400 | 360 |

Adding adapter-parameterized tests moves the measured numbers. When a leg
breaches, check that the skip predicates still say what they should, then
update the four constants at the top of `run-matrix.sh` **and** this
section together.

## Conclusion

The documented **3.19.3 floor holds by measurement**, not just by policy:
the entire suite passes on a real 3.19.3 build, for the SQLitePCLRaw
provider path and the direct-P/Invoke adapter alike. Below the floor the
runtime *enforces* the boundary, and the matrix now asserts that
enforcement instead of tripping over it: on 3.9.x, FloorGateTests measures
the gate refusing before any DDL, while every runtime-driven suite skips
with a reason pointing there.

The runtime itself is **engine-verified down to 3.9.0**: the lowered-floor
FloorGateTests row shows that a host which explicitly opts down with
`.MinSqliteVersion(3009000)` completes a real call → drain → result-read
run on actual 3.9.0/3.9.2 builds, on both overridable adapters — and the
adapter-level surface (including `sqlite3_create_function`) measurably
works there too. 3.19.3 remains the *supported* floor — it is the default
gate every sample-host run enforces; the 3.9.x rows document, green and
loudly, both the gate firing and how far down the runtime and raw SQL/UDF
surface actually reach.
