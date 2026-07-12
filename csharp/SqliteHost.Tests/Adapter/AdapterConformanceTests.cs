using Microsoft.Data.Sqlite;
using SqliteHost.Adapters.Native;
using SqliteHost.Conformance;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Runs the shippable adapter conformance suite
    /// (SqliteHost.Conformance.AdapterConformanceTestsBase) against all
    /// four built-in adapters.
    ///
    /// Native-override runs (SQLITEHOST_NATIVE_SQLITE): scoped to the two
    /// adapters that honor the override — Microsoft.Data.Sqlite (SQLitePCLRaw
    /// dynamic provider) and SqliteHost.Adapters.Native (test-side
    /// DllImportResolver, see NativeAdapterLibraryResolver) — same policy as
    /// IntegrationFixtureTests.
    /// </summary>
    internal static class NativeOverrideSuiteSkip
    {
        /// <summary>Skip reason for adapters excluded from native-override matrix runs.</summary>
        internal static string ReasonOrNull =>
            NativeSqliteOverride.IsActive
                ? "SQLITEHOST_NATIVE_SQLITE is set: the override is scoped to the Microsoft.Data.Sqlite "
                  + "and SqliteHost.Adapters.Native adapters; this adapter bundles/loads its own native SQLite."
                : null;
    }

    /// <summary>Conformance suite on the Microsoft.Data.Sqlite adapter (SQLitePCLRaw; honors SQLITEHOST_NATIVE_SQLITE).</summary>
    public class MicrosoftDataSqliteAdapterConformanceTests : AdapterConformanceTestsBase
    {
        protected override ISqliteHostConnection OpenAdapterConnection()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return new MicrosoftDataSqliteConnection(connection);
        }
    }

    /// <summary>Conformance suite on the System.Data.SQLite ADO.NET adapter (own bundled native).</summary>
    public class SystemDataSqliteAdapterConformanceTests : AdapterConformanceTestsBase
    {
        protected override string SkipEntireSuiteReason => NativeOverrideSuiteSkip.ReasonOrNull;

        protected override ISqliteHostConnection OpenAdapterConnection()
            => SystemDataSqliteConnection.OpenInMemory();
    }

    /// <summary>Conformance suite on the sqlite-net (Unity-style wrapper) adapter.</summary>
    public class SqliteNetAdapterConformanceTests : AdapterConformanceTestsBase
    {
        protected override string SkipEntireSuiteReason => NativeOverrideSuiteSkip.ReasonOrNull;

        protected override ISqliteHostConnection OpenAdapterConnection()
            => SqliteNetConnection.OpenInMemory();
    }

    /// <summary>
    /// Conformance suite on the shippable SqliteHost.Adapters.Native
    /// P/Invoke adapter (honors SQLITEHOST_NATIVE_SQLITE via the test-side
    /// DllImportResolver, so it RUNS in every matrix cell).
    /// </summary>
    public class NativeAdapterConformanceTests : AdapterConformanceTestsBase
    {
        protected override ISqliteHostConnection OpenAdapterConnection()
            => NativeSqliteHostConnection.OpenInMemory();
    }
}
