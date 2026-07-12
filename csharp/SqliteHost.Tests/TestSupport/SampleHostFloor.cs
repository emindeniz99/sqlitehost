using System;
using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using Xunit;

namespace SqliteHost.Tests.TestSupport
{
    /// <summary>
    /// Below-floor engine detection for runtime-driven tests. The sample
    /// host definition pins MinSqliteVersion 3019003 (3.19.3, the
    /// documented floor), so on an older engine (the matrix's 3.9.x rows)
    /// every runtime-driven test fails through the designed
    /// sqlite-version-too-low gate before measuring anything. Those tests
    /// call <see cref="SkipBelowFloor"/> and skip with an explicit reason
    /// instead; the gate itself stays covered in every matrix cell by
    /// FloorGateTests (both the refusal on below-floor engines and the
    /// lowered-floor end-to-end run).
    ///
    /// The active engine version comes from
    /// SQLITEHOST_EXPECTED_SQLITE_VERSION_NUMBER (set by
    /// tests/compatibility-sqlite/run-matrix.sh for every cell) or, when
    /// unset, a one-time sqlite_version() probe on the Microsoft.Data.Sqlite
    /// adapter — the engine the adapter-agnostic runtime tests run on. If
    /// neither yields a version the tests run and let the gate speak for
    /// itself.
    /// </summary>
    internal static class SampleHostFloor
    {
        /// <summary>The sample host definition's floor (3019003 = 3.19.3).</summary>
        internal static readonly int FloorVersionNumber =
            GeneratedHostDefinition.Build().MinSqliteVersionNumber;

        private static readonly Lazy<int> ActiveEngineVersionNumberLazy =
            new Lazy<int>(ResolveActiveEngineVersionNumber);

        /// <summary>Active engine version, sqlite3_libversion_number encoding.</summary>
        internal static int ActiveEngineVersionNumber => ActiveEngineVersionNumberLazy.Value;

        internal static bool IsBelowFloor => ActiveEngineVersionNumber < FloorVersionNumber;

        internal static string SkipReason =>
            "engine " + ActiveEngineVersionNumber + " is below the sample host's floor "
            + FloorVersionNumber + ": the runtime's sqlite-version-too-low gate refuses every run"
            + " by design; gate behavior is covered by FloorGateTests.";

        /// <summary>
        /// Skips the calling [SkippableFact] test when the active engine is
        /// below the sample host's floor.
        /// </summary>
        internal static void SkipBelowFloor() => Skip.If(IsBelowFloor, SkipReason);

        private static int ResolveActiveEngineVersionNumber()
        {
            string expected = NativeSqliteOverride.ExpectedVersionNumber;
            if (!string.IsNullOrEmpty(expected) && int.TryParse(expected, out int fromEnvironment))
            {
                return fromEnvironment;
            }
            return SqliteVersionParser.TryParse(SqliteVersionTests.QueryRuntimeVersion(), out int probed)
                ? probed
                : FloorVersionNumber;   // unknown: treat as at-floor and run everything
        }
    }
}
