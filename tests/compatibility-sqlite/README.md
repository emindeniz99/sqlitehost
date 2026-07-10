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

Versions tested: pinned `3.9.2`, `3.19.3` (the documented floor), `3.28.0`,
plus the **newest** amalgamation resolved at runtime from
`https://sqlite.org/download.html` (fallback pin if the page is unreachable:
3.53.3, the newest at the time this harness was written).

The script exits non-zero if any supported-floor version (>= 3.19.3) fails;
3.9.2 is below the floor and runs for information only.

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

Adapter scoping: the override only governs SQLitePCLRaw-based code, so
matrix runs execute the integration fixtures **only on the
Microsoft.Data.Sqlite adapter**. The System.Data.SQLite adapter has its own
interop + bundled native and never sees the override; the sqlite-net adapter
technically shares the SQLitePCLRaw provider but is skipped too so each
matrix cell exercises exactly one adapter against exactly one known native
build. Those 18 tests (9 fixture scenarios x 2 adapters) report as
*skipped* with an explicit reason — hence 86 passed / 18 skipped per cell,
versus 103 passed / 1 skipped in a normal `dotnet test` run.

Canary meta-tests (`VersionCanaryTests`, always run) prove the harness can
detect version differences: UPSERT (`ON CONFLICT ... DO UPDATE`, introduced
3.24) and `RETURNING` (introduced 3.35) are asserted to **throw** below
their introducing version and **succeed** at/above it, branching on the real
runtime `sqlite_version()`. Both features remain banned from SqliteHost's
generated SQL.

## Measured results (2026-07-10, linux-x64, gcc -O2 default amalgamation)

| SQLite  | `sqlite_version()` confirmed | Tests               | UPSERT canary | RETURNING canary |
|---------|------------------------------|---------------------|---------------|------------------|
| 3.9.2   | yes (equality test passed)   | PASS — 86 passed, 18 skipped, 0 failed | threw (as expected, < 3.24) | threw (as expected, < 3.35) |
| 3.19.3  | yes                          | PASS — 86 passed, 18 skipped, 0 failed | threw (< 3.24) | threw (< 3.35) |
| 3.28.0  | yes                          | PASS — 86 passed, 18 skipped, 0 failed | succeeded (>= 3.24) | threw (< 3.35) |
| 3.53.3 (newest) | yes                  | PASS — 86 passed, 18 skipped, 0 failed | succeeded | succeeded (>= 3.35) |

## Conclusion

The documented **3.19.3 floor holds by measurement**, not just by policy:
the entire suite passes on a real 3.19.3 build. In fact every test also
passes on **3.9.2** — unsurprising, since the generated SQL only uses
constructs that are ancient (triggers, composite PKs, multi-row `VALUES`
from 3.7.11, named parameters). So the effective compatibility today is even
older than the documented floor; 3.19.3 remains the *supported* floor (the
one the matrix enforces with a non-zero exit), and 3.9.2 is an informational
data point, not a promise.
