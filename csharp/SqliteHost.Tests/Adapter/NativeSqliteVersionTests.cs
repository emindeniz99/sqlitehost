using System;
using SqliteHost.Adapters.Native;
using Xunit;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Version visibility through the SqliteHost.Adapters.Native P/Invoke
    /// adapter — proves which native SQLite the test-side DllImportResolver
    /// (NativeAdapterLibraryResolver) actually loaded. In matrix runs
    /// (tests/compatibility-sqlite/run-matrix.sh) the equality tests are the
    /// proof that the native adapter honored SQLITEHOST_NATIVE_SQLITE, the
    /// same identity SqliteVersionTests proves for the SQLitePCLRaw
    /// provider override.
    /// </summary>
    public class NativeSqliteVersionTests
    {
        internal static string QueryRuntimeVersion()
        {
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            return connection.Query("SELECT sqlite_version()", null, row => row.GetText(0))[0];
        }

        [Fact]
        public void SqliteLibVersion_IsQueryable_AndMatchesTheEngineExecutingSql()
        {
            string libVersion = NativeSqliteHostConnection.SqliteLibVersion;
            Assert.False(string.IsNullOrEmpty(libVersion));
            Assert.Equal(3, Version.Parse(libVersion).Major);
            // sqlite3_libversion and the engine answering SQL are the same
            // library — one resolver governs every P/Invoke in the package.
            Assert.Equal(libVersion, QueryRuntimeVersion());
        }

        [SkippableFact]
        public void SqliteLibVersion_MatchesExpectedVersion_WhenNativeOverrideIsActive()
        {
            Skip.If(
                !NativeSqliteOverride.IsActive || NativeSqliteOverride.ExpectedVersion == null,
                "Only meaningful when the matrix script sets SQLITEHOST_NATIVE_SQLITE"
                + " and SQLITEHOST_EXPECTED_SQLITE_VERSION.");

            Assert.Equal(NativeSqliteOverride.ExpectedVersion, NativeSqliteHostConnection.SqliteLibVersion);
        }

        [SkippableFact]
        public void SqliteVersionNumber_MatchesExpectedNumber_WhenNativeOverrideIsActive()
        {
            // Same numeric identity as SqliteVersionTests, derived from the
            // native adapter's sqlite3_libversion string
            // (major*1000000 + minor*1000 + patch).
            Skip.If(
                !NativeSqliteOverride.IsActive || NativeSqliteOverride.ExpectedVersionNumber == null,
                "Only meaningful when the matrix script sets SQLITEHOST_NATIVE_SQLITE"
                + " and SQLITEHOST_EXPECTED_SQLITE_VERSION_NUMBER.");

            Version runtime = Version.Parse(NativeSqliteHostConnection.SqliteLibVersion);
            int number = runtime.Major * 1000000 + runtime.Minor * 1000 + runtime.Build;
            Assert.Equal(NativeSqliteOverride.ExpectedVersionNumber, number.ToString());
        }
    }
}
