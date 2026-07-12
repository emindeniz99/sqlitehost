# Real-SQLite compatibility matrix (C# suite)

Runs the full C# test suite (`csharp/SqliteHost.sln`) against **real SQLite
builds** compiled from the official amalgamations, instead of the modern
e_sqlite3 that SQLitePCLRaw bundles. This turns the "3.19.3 floor by
banned-feature policy" of `docs/compatibility.md` into a measured result.

## How to run

```bash
bash tests/compatibility-sqlite/run-matrix.sh
```

Requirements: `gcc`, `unzip`, `curl`, and `dotnet` (found on `PATH` or at
`/opt/dotnet`). Amalgamation zips and compiled `libsqlite3-<ver>.so` files
are cached under `.cache/` (gitignored via the local `.gitignore`); delete
that directory to force fresh downloads/builds.

Versions tested: pinned `3.9.0`, `3.9.2`, `3.19.3` (the documented floor),
`3.28.0`, plus the **newest** amalgamation resolved at runtime from
`https://sqlite.org/download.html` (fallback pin if the page is unreachable:
3.53.3, the newest at the time this harness was written).

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
overridable adapters against exactly one known native build. Those 98
tests (14 fixture scenarios + 29 conformance tests + 6 inline-function
scenarios, x 2 excluded adapters) report as *skipped* with an explicit
reason — hence 291 passed / 100 skipped per supported-floor cell, versus
385 passed / 6 skipped in a normal `dotnet test` run (the normal-run skips
are the override-only version-identity tests, two per overridable adapter,
plus the two below-floor-direction `FloorGateTests` that only run on
engines older than the sample floor).

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
| 3.9.0   | PASS — 200 passed, 0 failed, 175 skipped (of 375, see below) | threw | threw | threw | threw | threw (no JSON1) | all prepared |
| 3.9.2   | PASS — 200 passed, 0 failed, 175 skipped (of 375, see below) | threw | threw | threw | threw | threw (no JSON1) | all prepared |
| 3.19.3  | PASS — 291 passed, 0 failed, 100 skipped | threw | threw | threw | threw | threw (no JSON1) | all prepared |
| 3.28.0  | PASS — 291 passed, 0 failed, 100 skipped | succeeded | threw | succeeded | threw | threw (no JSON1) | all prepared |
| 3.53.3 (newest) | PASS — 291 passed, 0 failed, 100 skipped | succeeded | succeeded | succeeded | succeeded | succeeded (built-in) | all prepared |

### Below-floor rows: skip policy + FloorGateTests

The sample host definition pins `MinSqliteVersion(3019003)`, so on an
engine older than 3.19.3 the runtime's workspace version gate
(`sqlite-version-too-low`, docs/errors.md) refuses every run before any
DDL. That is designed behavior, and it used to surface as 91 informational
failures per 3.9.x cell — every runtime-driven test tripping the same
gate. Those rows are now meaningfully green instead:

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
    that stands in for the 91 accidental failures. At/above the floor the
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
  the four adapter mirrors — which is why those cells report 375 total
  instead of 391).

The script exits non-zero if **any** row fails, below-floor rows included.

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
