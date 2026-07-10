using System;
using Microsoft.Data.Sqlite;
using Xunit;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Version visibility through the Microsoft.Data.Sqlite adapter — proves
    /// which native SQLite the SQLitePCLRaw provider actually loaded. The
    /// compatibility matrix (tests/compatibility-sqlite/run-matrix.sh) sets
    /// both SQLITEHOST_NATIVE_SQLITE and SQLITEHOST_EXPECTED_SQLITE_VERSION,
    /// turning the equality test into the proof that the dynamic provider
    /// override won over the bundled e_sqlite3.
    /// </summary>
    public class SqliteVersionTests
    {
        internal static string QueryRuntimeVersion()
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var adapter = new MicrosoftDataSqliteConnection(connection);
            return adapter.Query("SELECT sqlite_version()", null, row => row.GetText(0))[0];
        }

        internal static Version ParseRuntimeVersion()
            => Version.Parse(QueryRuntimeVersion());

        [Fact]
        public void SqliteVersion_IsQueryableThroughTheAdapter()
        {
            string version = QueryRuntimeVersion();
            Assert.False(string.IsNullOrEmpty(version));
            // "3.x.y" — parseable and major version 3.
            Assert.Equal(3, Version.Parse(version).Major);
        }

        [SkippableFact]
        public void SqliteVersion_MatchesExpectedVersion_WhenNativeOverrideIsActive()
        {
            Skip.If(
                !NativeSqliteOverride.IsActive || NativeSqliteOverride.ExpectedVersion == null,
                "Only meaningful when the matrix script sets SQLITEHOST_NATIVE_SQLITE"
                + " and SQLITEHOST_EXPECTED_SQLITE_VERSION.");

            Assert.Equal(NativeSqliteOverride.ExpectedVersion, QueryRuntimeVersion());
        }

        [SkippableFact]
        public void SqliteVersionNumber_MatchesExpectedNumber_WhenNativeOverrideIsActive()
        {
            // Numeric identity in the sqlite3_libversion_number encoding
            // (major*1000000 + minor*1000 + patch), derived deterministically
            // from the sqlite_version() string; the matrix script derives the
            // expected value from the requested version the same way.
            Skip.If(
                !NativeSqliteOverride.IsActive || NativeSqliteOverride.ExpectedVersionNumber == null,
                "Only meaningful when the matrix script sets SQLITEHOST_NATIVE_SQLITE"
                + " and SQLITEHOST_EXPECTED_SQLITE_VERSION_NUMBER.");

            Version runtime = ParseRuntimeVersion();
            int number = runtime.Major * 1000000 + runtime.Minor * 1000 + runtime.Build;
            Assert.Equal(NativeSqliteOverride.ExpectedVersionNumber, number.ToString());
        }
    }
}
