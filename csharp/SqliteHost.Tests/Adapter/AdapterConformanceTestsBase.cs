using System;
using System.Collections.Generic;
using Example.Game.Generated;
using Microsoft.Data.Sqlite;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Reusable adapter conformance suite (docs/adapter-contract.md): every
    /// ISqliteHostConnection implementation must surface errors instead of
    /// swallowing them, resolve bare binding names across all three prefix
    /// forms, and round-trip every binding type with full fidelity. The repo
    /// runs it against all three built-in adapters via the concrete
    /// subclasses at the bottom of this file; adapter authors (including
    /// private Unity wrapper forks) should subclass it with their factory.
    ///
    /// Native-override runs (SQLITEHOST_NATIVE_SQLITE): scoped to the
    /// Microsoft.Data.Sqlite adapter, same policy as IntegrationFixtureTests.
    /// </summary>
    public abstract class AdapterConformanceTestsBase
    {
        /// <summary>Opens one in-memory workspace on the adapter under test.</summary>
        protected abstract ISqliteHostConnection OpenAdapterConnection();

        /// <summary>
        /// True for adapters that must not run when SQLITEHOST_NATIVE_SQLITE
        /// pins a specific native build (see IntegrationFixtureTestsBase).
        /// </summary>
        protected virtual bool SkipUnderNativeOverride => false;

        private ISqliteHostConnection Open()
        {
            Skip.If(
                SkipUnderNativeOverride && NativeSqliteOverride.IsActive,
                "SQLITEHOST_NATIVE_SQLITE is set: the dynamic-provider override is scoped to the "
                + "Microsoft.Data.Sqlite adapter; this adapter bundles/loads its own native SQLite.");
            return OpenAdapterConnection();
        }

        // ---- error surfacing --------------------------------------------

        [SkippableFact]
        public void MalformedSql_Throws_AsAdapterException()
        {
            using ISqliteHostConnection connection = Open();
            var ex = Assert.ThrowsAny<SqliteHostAdapterException>(
                () => connection.Execute("SELECT FROM WHERE (", null));
            Assert.Equal(1, ex.SqliteErrorCode);   // SQLITE_ERROR
        }

        [SkippableFact]
        public void MissingTable_Throws_AsAdapterException()
        {
            using ISqliteHostConnection connection = Open();
            var ex = Assert.ThrowsAny<SqliteHostAdapterException>(
                () => connection.Execute("INSERT INTO no_such_table (x) VALUES (1)", null));
            Assert.Equal(1, ex.SqliteErrorCode);   // SQLITE_ERROR
        }

        [SkippableFact]
        public void MissingColumn_QueryThrows_NeverLooksLikeZeroRows()
        {
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER)", null);
            Assert.ThrowsAny<SqliteHostAdapterException>(
                () => connection.Query("SELECT no_such_column FROM scratch", null, row => row.GetInt64(0)));
        }

        [SkippableFact]
        public void NoSilentNullSemantics_UnboundParameterErrors_InsteadOfBindingNull()
        {
            // A parameter the payload did not provide must NEVER execute as
            // an implicit NULL, even with runtime binding validation off —
            // the adapter itself has to refuse (docs/adapter-contract.md).
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER)", null);
            Assert.ThrowsAny<Exception>(() => connection.Execute(
                "INSERT INTO scratch (a) VALUES (:v)", new List<SqliteHostBinding>()));
            var rows = connection.Query("SELECT a FROM scratch", null, row => row.IsNull(0));
            Assert.Empty(rows);   // and nothing was silently inserted
        }

        // ---- binding resolution -----------------------------------------

        [SkippableFact]
        public void MixedPrefixSameName_OneBindingFeedsAllOccurrences()
        {
            // Documented supported behavior: one bare binding feeds :v and $v
            // in the same statement (docs/adapter-contract.md).
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER, b INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (a, b) VALUES (:v, $v)",
                Bind(("v", SqliteHostBindingValue.Int32(21))));
            AssertSingleRow(connection, "SELECT a, b FROM scratch",
                row => row.GetInt64(0) + "," + row.GetInt64(1), "21,21");
        }

        [SkippableFact]
        public void AllThreePrefixes_ResolveDistinctBareNames()
        {
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER, b INTEGER, c INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (a, b, c) VALUES (:one, @two, $three)",
                Bind(
                    ("one", SqliteHostBindingValue.Int64(1)),
                    ("two", SqliteHostBindingValue.Int64(2)),
                    ("three", SqliteHostBindingValue.Int64(3))));
            AssertSingleRow(connection, "SELECT a, b, c FROM scratch",
                row => row.GetInt64(0) + "," + row.GetInt64(1) + "," + row.GetInt64(2), "1,2,3");
        }

        [SkippableFact]
        public void SameNamedParameter_UsedTwiceInOneStatement_BindsBothOccurrences()
        {
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER, b INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (a, b) VALUES (:v, :v + 1)",
                Bind(("v", SqliteHostBindingValue.Int64(41))));
            AssertSingleRow(connection, "SELECT a, b FROM scratch",
                row => row.GetInt64(0) + "," + row.GetInt64(1), "41,42");
        }

        // ---- value fidelity ---------------------------------------------

        [SkippableFact]
        public void Int32_RoundTrip()
        {
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER, b INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (a, b) VALUES (:min, :max)",
                Bind(
                    ("min", SqliteHostBindingValue.Int32(int.MinValue)),
                    ("max", SqliteHostBindingValue.Int32(int.MaxValue))));
            var rows = connection.Query(
                "SELECT a, b FROM scratch", null,
                row => new { A = row.GetInt32(0), B = row.GetInt32(1) });
            var row0 = Assert.Single(rows);
            Assert.Equal(int.MinValue, row0.A);
            Assert.Equal(int.MaxValue, row0.B);
        }

        [SkippableFact]
        public void Int64_RoundTrip_WithValueAboveTwoToThe31()
        {
            const long big = 5_000_000_123L;   // > 2^31
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (a) VALUES (:v)",
                Bind(("v", SqliteHostBindingValue.Int64(big))));
            var rows = connection.Query("SELECT a FROM scratch", null, row => row.GetInt64(0));
            Assert.Equal(new[] { big }, rows);
        }

        [SkippableFact]
        public void Bool_RoundTrip_AsZeroAndOne()
        {
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (id INTEGER, a INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (id, a) VALUES (1, :t), (2, :f)",
                Bind(
                    ("t", SqliteHostBindingValue.Bool(true)),
                    ("f", SqliteHostBindingValue.Bool(false))));
            var rows = connection.Query(
                "SELECT a, typeof(a), a FROM scratch ORDER BY id", null,
                row => row.GetInt64(0) + "|" + row.GetText(1) + "|" + row.GetBool(2));
            Assert.Equal(new[] { "1|integer|True", "0|integer|False" }, rows);
        }

        [SkippableFact]
        public void Text_RoundTrip_EmptyAndNonAscii()
        {
            const string turkish = "İstanbul'da şöğüçı ĞÜŞİÖÇ";
            const string emoji = "🎮 sqlite host 🚀✨";
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (id INTEGER, a TEXT)", null);
            connection.Execute(
                "INSERT INTO scratch (id, a) VALUES (1, :empty), (2, :turkish), (3, :emoji)",
                Bind(
                    ("empty", SqliteHostBindingValue.Text("")),
                    ("turkish", SqliteHostBindingValue.Text(turkish)),
                    ("emoji", SqliteHostBindingValue.Text(emoji))));
            var rows = connection.Query(
                "SELECT a FROM scratch ORDER BY id", null, row => row.GetText(0));
            Assert.Equal(new[] { "", turkish, emoji }, rows);
        }

        [SkippableFact]
        public void Blob_RoundTrip_EmptyAndLarge()
        {
            byte[] empty = new byte[0];
            var large = new byte[1024 * 1024];   // 1 MiB, patterned
            for (int i = 0; i < large.Length; i++)
            {
                large[i] = (byte)(i * 31);
            }
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (id INTEGER, a BLOB)", null);
            connection.Execute(
                "INSERT INTO scratch (id, a) VALUES (1, :empty), (2, :large)",
                Bind(
                    ("empty", SqliteHostBindingValue.Blob(empty)),
                    ("large", SqliteHostBindingValue.Blob(large))));
            var rows = connection.Query(
                "SELECT a FROM scratch ORDER BY id", null, row => row.GetBlob(0));
            Assert.Equal(2, rows.Count);
            Assert.Equal(empty, rows[0]);
            Assert.Equal(large, rows[1]);
        }

        [SkippableFact]
        public void Float32AndFloat64_RoundTrip()
        {
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a REAL, b REAL)", null);
            connection.Execute(
                "INSERT INTO scratch (a, b) VALUES (:f32, :f64)",
                Bind(
                    ("f32", SqliteHostBindingValue.Float32(0.75f)),
                    ("f64", SqliteHostBindingValue.Float64(-98.5))));
            var rows = connection.Query(
                "SELECT a, b, typeof(a), typeof(b) FROM scratch", null,
                row => new
                {
                    F32 = row.GetFloat32(0),
                    F64 = row.GetFloat64(1),
                    AType = row.GetText(2),
                    BType = row.GetText(3)
                });
            var row0 = Assert.Single(rows);
            Assert.Equal(0.75f, row0.F32);
            Assert.Equal(-98.5, row0.F64);
            Assert.Equal("real", row0.AType);
            Assert.Equal("real", row0.BType);
        }

        [SkippableFact]
        public void ExplicitNull_RoundTrip()
        {
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (a) VALUES (:v)",
                Bind(("v", SqliteHostBindingValue.Null())));
            var rows = connection.Query(
                "SELECT a, typeof(a) FROM scratch", null,
                row => row.IsNull(0) + "|" + row.GetText(1));
            Assert.Equal(new[] { "True|null" }, rows);
        }

        // ---- runtime conformance (adapter driven through the runtime) ----

        private SqliteHostRunResult RunScript(
            SqliteHostScript script,
            FakeGameHandlers handlers,
            AdapterWorkspaceFactory factory = null)
        {
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory ?? new AdapterWorkspaceFactory(OpenAdapterConnection),
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: null);
            return runtime.Run(script);
        }

        [SkippableFact]
        public void UnknownBinding_FailsMissingBinding_WithBindingName()
        {
            Skip.If(SkipUnderNativeOverride && NativeSqliteOverride.IsActive, "native override run");
            SqliteHostRunResult result = RunScript(
                Scripts.New(
                    Scripts.Step("only",
                        Scripts.Statement(
                            "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, :ghost)",
                            ("callId", SqliteHostBindingValue.Text("c-1"))))),
                new FakeGameHandlers());

            Assert.Equal(SqliteHostRunStatus.FailedBinding, result.Status);
            Assert.Equal("missing-binding", result.ErrorCode);
            Assert.Equal("ghost", result.BindingName);
        }

        [SkippableFact]
        public void ExtraBinding_FailsUnusedBinding_WithBindingName()
        {
            Skip.If(SkipUnderNativeOverride && NativeSqliteOverride.IsActive, "native override run");
            SqliteHostRunResult result = RunScript(
                Scripts.New(
                    Scripts.Step("only",
                        Scripts.Statement(
                            "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'k')",
                            ("callId", SqliteHostBindingValue.Text("c-1")),
                            ("leftover", SqliteHostBindingValue.Int64(9))))),
                new FakeGameHandlers());

            Assert.Equal(SqliteHostRunStatus.FailedBinding, result.Status);
            Assert.Equal("unused-binding", result.ErrorCode);
            Assert.Equal("leftover", result.BindingName);
        }

        [SkippableFact]
        public void ErrorMidStep_AbortsTheStep_NoLaterStatement_NoHandlerForEarlierInsert()
        {
            Skip.If(SkipUnderNativeOverride && NativeSqliteOverride.IsActive, "native override run");
            var handlers = new FakeGameHandlers();
            using var factory = new AdapterWorkspaceFactory(OpenAdapterConnection, retainWorkspace: true);

            SqliteHostRunResult result = RunScript(
                Scripts.New(
                    Scripts.Step("broken",
                        Scripts.Statement(
                            "INSERT INTO call_get_value (call_id, input_key) VALUES ('g-1', 'k1')"),
                        Scripts.Statement("INSERT INTO no_such_table (x) VALUES (1)"),
                        Scripts.Statement(
                            "INSERT INTO call_get_value (call_id, input_key) VALUES ('g-2', 'k2')"))),
                handlers,
                factory);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("sql-error", result.ErrorCode);
            Assert.Equal(1, result.StatementIndex);
            // No handler ran for statement 1's call insert...
            Assert.Empty(handlers.Log);
            Assert.Equal(0, result.ExecutedCallCount);
            // ...and statement 3 never executed.
            var callIds = factory.LastWorkspace.Query(
                "SELECT call_id FROM call_get_value ORDER BY call_id", null, row => row.GetText(0));
            Assert.Equal(new[] { "g-1" }, callIds);
        }

        // ---- helpers ------------------------------------------------------

        private static IReadOnlyList<SqliteHostBinding> Bind(
            params (string Name, SqliteHostBindingValue Value)[] bindings)
        {
            var list = new List<SqliteHostBinding>(bindings.Length);
            foreach ((string name, SqliteHostBindingValue value) in bindings)
            {
                list.Add(new SqliteHostBinding(name, value));
            }
            return list;
        }

        private static void AssertSingleRow(
            ISqliteHostConnection connection,
            string sql,
            Func<ISqliteHostRow, string> mapper,
            string expected)
        {
            var rows = connection.Query(sql, null, mapper);
            Assert.Equal(new[] { expected }, rows);
        }
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
        protected override bool SkipUnderNativeOverride => true;

        protected override ISqliteHostConnection OpenAdapterConnection()
            => SystemDataSqliteConnection.OpenInMemory();
    }

    /// <summary>Conformance suite on the sqlite-net (Unity-style wrapper) adapter.</summary>
    public class SqliteNetAdapterConformanceTests : AdapterConformanceTestsBase
    {
        protected override bool SkipUnderNativeOverride => true;

        protected override ISqliteHostConnection OpenAdapterConnection()
            => SqliteNetConnection.OpenInMemory();
    }
}
