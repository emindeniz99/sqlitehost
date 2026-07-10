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

The script exits non-zero if any supported-floor version (>= 3.19.3) fails;
3.9.0 and 3.9.2 are below the floor and run for information only.

## How it works

For each version the script sets two environment variables and runs
`dotnet test`:

- `SQLITEHOST_NATIVE_SQLITE=<path>/libsqlite3-<ver>.so` — a
  `[ModuleInitializer]` in the test assembly
  (`csharp/SqliteHost.Tests/Adapter/NativeSqliteOverride.cs`) installs a
  `SQLitePCLRaw.provider.dynamic_cdecl` provider that resolves every
  `sqlite3_*` export from that library via
  `System.Runtime.InteropServices.NativeLibrary`, then calls
  `raw.FreezeProvider()` so the `SQLitePCL.Batteries_V2.Init()` that
  `Microsoft.Data.Sqlite` / sqlite-net trigger later becomes a no-op.
- `SQLITEHOST_EXPECTED_SQLITE_VERSION=<ver>` —
  `SqliteVersionTests.SqliteVersion_MatchesExpectedVersion_WhenNativeOverrideIsActive`
  then asserts `SELECT sqlite_version()` **equals** this value, proving the
  dynamic provider actually loaded the requested binary (this test passed in
  every matrix cell below).
- `SQLITEHOST_EXPECTED_SQLITE_VERSION_NUMBER=<n>` — the same identity in
  the `sqlite3_libversion_number` encoding
  (`major*1000000 + minor*1000 + patch`), derived deterministically by the
  script from the dotted version;
  `SqliteVersionTests.SqliteVersionNumber_MatchesExpectedNumber_WhenNativeOverrideIsActive`
  derives the number from the `sqlite_version()` string the same way and
  asserts equality (also passed in every cell).

Adapter scoping: the override only governs SQLitePCLRaw-based code, so
matrix runs execute the integration fixtures and the adapter conformance
suite **only on the Microsoft.Data.Sqlite adapter**. The System.Data.SQLite
adapter has its own interop + bundled native and never sees the override;
the sqlite-net adapter technically shares the SQLitePCLRaw provider but is
skipped too so each matrix cell exercises exactly one adapter against
exactly one known native build. Those 52 tests (9 fixture scenarios + 17
conformance tests, x 2 adapters) report as *skipped* with an explicit
reason — hence 122 passed / 52 skipped per cell, versus 172 passed / 2
skipped in a normal `dotnet test` run (the 2 normal-run skips are the two
override-only version-identity tests).

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

## Measured results (2026-07-10, linux-x64, gcc -O2 default amalgamation)

Every cell: `sqlite_version()` string **and** derived numeric version
confirmed via the two identity tests; full run
`bash tests/compatibility-sqlite/run-matrix.sh` exited 0.

| SQLite  | Tests | UPSERT (3.24) | RETURNING (3.35) | OVER (3.25) | iif() (3.32) | json_valid (build) | positive prepare |
|---------|-------|---------------|------------------|-------------|--------------|--------------------|------------------|
| 3.9.0   | PASS — 122 passed, 52 skipped, 0 failed | threw | threw | threw | threw | threw (no JSON1) | all prepared |
| 3.9.2   | PASS — 122 passed, 52 skipped, 0 failed | threw | threw | threw | threw | threw (no JSON1) | all prepared |
| 3.19.3  | PASS — 122 passed, 52 skipped, 0 failed | threw | threw | threw | threw | threw (no JSON1) | all prepared |
| 3.28.0  | PASS — 122 passed, 52 skipped, 0 failed | succeeded | threw | succeeded | threw | threw (no JSON1) | all prepared |
| 3.53.3 (newest) | PASS — 122 passed, 52 skipped, 0 failed | succeeded | succeeded | succeeded | succeeded | succeeded (built-in) | all prepared |

## Conclusion

The documented **3.19.3 floor holds by measurement**, not just by policy:
the entire suite passes on a real 3.19.3 build. In fact every test also
passes on **3.9.2 and 3.9.0** — unsurprising, since the generated SQL only
uses constructs that are ancient (triggers, composite PKs, multi-row
`VALUES` from 3.7.11, named parameters). So the effective compatibility
today is even older than the documented floor; 3.19.3 remains the
*supported* floor (the one the matrix enforces with a non-zero exit), and
the 3.9.x rows are informational data points, not a promise.
