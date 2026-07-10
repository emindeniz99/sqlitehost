using System;
using Microsoft.Data.Sqlite;
using Xunit;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// CANARY meta-tests of the compatibility matrix harness itself: they
    /// prove that swapping the native SQLite via SQLITEHOST_NATIVE_SQLITE
    /// actually changes observable SQL behavior. Each canary picks a feature
    /// with a known introduction version and asserts it THROWS below that
    /// version and SUCCEEDS at/above it, branching on the real runtime
    /// sqlite_version(). They always run (also against the bundled modern
    /// e_sqlite3, where both features succeed).
    ///
    /// These features are deliberately BANNED from SqliteHost's generated
    /// SQL (docs/compatibility.md, floor 3.19.3) — they exist here only to
    /// make version differences visible to the matrix.
    /// </summary>
    public class VersionCanaryTests
    {
        private static readonly Version UpsertIntroduced = new Version(3, 24, 0);
        private static readonly Version ReturningIntroduced = new Version(3, 35, 0);

        private static MicrosoftDataSqliteConnection OpenWorkspace()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return new MicrosoftDataSqliteConnection(connection);
        }

        [Fact]
        public void Upsert_OnConflictDoUpdate_TracksThe324Boundary()
        {
            Version runtime = SqliteVersionTests.ParseRuntimeVersion();
            using var workspace = OpenWorkspace();
            workspace.Execute("CREATE TABLE canary (k TEXT PRIMARY KEY, v INTEGER)", null);
            workspace.Execute("INSERT INTO canary (k, v) VALUES ('a', 1)", null);

            const string upsert =
                "INSERT INTO canary (k, v) VALUES ('a', 2)"
                + " ON CONFLICT(k) DO UPDATE SET v = excluded.v";

            if (runtime >= UpsertIntroduced)
            {
                workspace.Execute(upsert, null);
                var values = workspace.Query(
                    "SELECT v FROM canary WHERE k = 'a'", null, row => row.GetInt64(0));
                Assert.Equal(new[] { 2L }, values);
            }
            else
            {
                // Pre-3.24 parsers reject the UPSERT clause outright.
                Assert.ThrowsAny<Exception>(() => workspace.Execute(upsert, null));
            }
        }

        [Fact]
        public void Returning_TracksThe335Boundary()
        {
            Version runtime = SqliteVersionTests.ParseRuntimeVersion();
            using var workspace = OpenWorkspace();
            workspace.Execute("CREATE TABLE canary (k TEXT PRIMARY KEY, v INTEGER)", null);

            const string returning =
                "INSERT INTO canary (k, v) VALUES ('b', 7) RETURNING v";

            if (runtime >= ReturningIntroduced)
            {
                var returned = workspace.Query(returning, null, row => row.GetInt64(0));
                Assert.Equal(new[] { 7L }, returned);
            }
            else
            {
                // Pre-3.35 parsers reject the RETURNING clause outright.
                Assert.ThrowsAny<Exception>(() => workspace.Query(
                    returning, null, row => row.GetInt64(0)));
            }
        }
    }
}
