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
        private static readonly Version WindowFunctionsIntroduced = new Version(3, 25, 0);
        private static readonly Version IifIntroduced = new Version(3, 32, 0);
        private static readonly Version ReturningIntroduced = new Version(3, 35, 0);
        private static readonly Version JsonAlwaysBuiltIn = new Version(3, 38, 0);

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

        [Fact]
        public void WindowFunctionOver_TracksThe325Boundary()
        {
            // Prepare-level canary: pre-3.25 parsers reject OVER () outright.
            Version runtime = SqliteVersionTests.ParseRuntimeVersion();
            using var workspace = OpenWorkspace();
            workspace.Execute("CREATE TABLE canary (k TEXT PRIMARY KEY, v INTEGER)", null);
            workspace.Execute("INSERT INTO canary (k, v) VALUES ('a', 1)", null);

            const string window = "SELECT k, COUNT(*) OVER () FROM canary";

            if (runtime >= WindowFunctionsIntroduced)
            {
                var counts = workspace.Query(window, null, row => row.GetInt64(1));
                Assert.Equal(new[] { 1L }, counts);
            }
            else
            {
                Assert.ThrowsAny<Exception>(() => workspace.Query(
                    window, null, row => row.GetInt64(1)));
            }
        }

        [Fact]
        public void Iif_TracksThe332Boundary()
        {
            // Prepare-level canary: iif() resolves at prepare time; pre-3.32
            // engines fail with "no such function: iif".
            Version runtime = SqliteVersionTests.ParseRuntimeVersion();
            using var workspace = OpenWorkspace();

            const string iif = "SELECT iif(1, 7, 9)";

            if (runtime >= IifIntroduced)
            {
                var values = workspace.Query(iif, null, row => row.GetInt64(0));
                Assert.Equal(new[] { 7L }, values);
            }
            else
            {
                Assert.ThrowsAny<Exception>(() => workspace.Query(
                    iif, null, row => row.GetInt64(0)));
            }
        }

        [Fact]
        public void JsonValid_IsCapabilityHonest_AcrossBuildOptions()
        {
            // json_valid() is a COMPILE-OPTION canary, not a pure version
            // gate: before 3.38 JSON1 was opt-in (-DSQLITE_ENABLE_JSON1);
            // from 3.38 it is built in unless explicitly omitted.
            //
            // - Matrix-built natives (SQLITEHOST_NATIVE_SQLITE set): our gcc
            //   builds compile the plain amalgamation with no -D flags, so
            //   json_valid must FAIL below 3.38 and SUCCEED at/above it —
            //   asserted strictly.
            // - Bundled provider (no override): e_sqlite3's build flags are
            //   not ours to pin, so this is probe-and-record only — either
            //   outcome is acceptable as long as the failure is a surfaced
            //   SQL error, not a harness crash.
            Version runtime = SqliteVersionTests.ParseRuntimeVersion();
            using var workspace = OpenWorkspace();

            const string json = "SELECT json_valid('{}')";

            if (NativeSqliteOverride.IsActive)
            {
                if (runtime >= JsonAlwaysBuiltIn)
                {
                    var values = workspace.Query(json, null, row => row.GetInt64(0));
                    Assert.Equal(new[] { 1L }, values);
                }
                else
                {
                    Assert.ThrowsAny<Exception>(() => workspace.Query(
                        json, null, row => row.GetInt64(0)));
                }
            }
            else
            {
                try
                {
                    var values = workspace.Query(json, null, row => row.GetInt64(0));
                    Assert.Equal(new[] { 1L }, values);   // JSON1 present in this build
                }
                catch (SqliteHostAdapterException)
                {
                    // JSON1 absent in this build — surfaced as a proper
                    // adapter error; nothing more to assert.
                }
            }
        }
    }
}
