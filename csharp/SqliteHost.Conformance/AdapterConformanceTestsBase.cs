using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SqliteHost.Conformance
{
    /// <summary>
    /// Reusable adapter conformance suite (docs/adapter-contract.md): every
    /// ISqliteHostConnection implementation must surface errors instead of
    /// swallowing them, resolve bare binding names across all three prefix
    /// forms, and round-trip every binding type with full fidelity. Adapter
    /// authors (including private Unity wrapper forks) subclass it in their
    /// xunit test project with their adapter factory; the SqliteHost repo
    /// runs it against all three built-in adapters the same way.
    ///
    /// The suite is self-contained: the runtime-conformance tests drive the
    /// adapter through a minimal single-method host defined here via the
    /// public fluent API, so no generated sample host is required.
    /// </summary>
    public abstract class AdapterConformanceTestsBase
    {
        /// <summary>Opens one in-memory workspace on the adapter under test.</summary>
        protected abstract ISqliteHostConnection OpenAdapterConnection();

        /// <summary>
        /// When non-null, every test in the suite is skipped with this
        /// reason. Override for adapters that must not run in a particular
        /// environment (the SqliteHost repo uses it to scope
        /// SQLITEHOST_NATIVE_SQLITE matrix runs to one adapter).
        /// </summary>
        protected virtual string SkipEntireSuiteReason => null;

        private void SkipIfSuiteDisabled()
        {
            string reason = SkipEntireSuiteReason;
            Skip.If(reason != null, reason);
        }

        private ISqliteHostConnection Open()
        {
            SkipIfSuiteDisabled();
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

        // ---- lifecycle and result shape ---------------------------------

        [SkippableFact]
        public void Query_WithNoRows_ReturnsEmptyList_NeverNull()
        {
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER)", null);
            var rows = connection.Query("SELECT a FROM scratch", null, row => row.GetInt64(0));
            Assert.NotNull(rows);
            Assert.Empty(rows);
        }

        [SkippableFact]
        public void Dispose_IsIdempotent()
        {
            ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER)", null);
            connection.Dispose();
            connection.Dispose();   // second Dispose must not throw
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

        [SkippableFact]
        public void UnicodeParameterName_ResolvesAndBinds()
        {
            // SQLite's tokenizer accepts non-ASCII identifier characters in
            // parameter names (measured on real builds 3.9.0 through 3.53.x);
            // adapters must resolve them without mangling the name.
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a TEXT)", null);
            connection.Execute(
                "INSERT INTO scratch (a) VALUES (:anahtarİsmi)",
                Bind(("anahtarİsmi", SqliteHostBindingValue.Text("değer"))));
            AssertSingleRow(connection, "SELECT a FROM scratch", row => row.GetText(0), "değer");
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
        public void Int64_RoundTrip_AtBothBoundaries()
        {
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a INTEGER, b INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (a, b) VALUES (:min, :max)",
                Bind(
                    ("min", SqliteHostBindingValue.Int64(long.MinValue)),
                    ("max", SqliteHostBindingValue.Int64(long.MaxValue))));
            var rows = connection.Query(
                "SELECT a, b FROM scratch", null,
                row => new { Min = row.GetInt64(0), Max = row.GetInt64(1) });
            var row0 = Assert.Single(rows);
            Assert.Equal(long.MinValue, row0.Min);
            Assert.Equal(long.MaxValue, row0.Max);
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
        public void Text_RoundTrip_OneMegabyte()
        {
            const int size = 1024 * 1024;   // 1 MiB of ASCII chars, patterned
            var builder = new StringBuilder(size + 32);
            while (builder.Length < size)
            {
                builder.Append("sqlite-host-").Append(builder.Length).Append(';');
            }
            builder.Length = size;
            string large = builder.ToString();
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (a TEXT)", null);
            connection.Execute(
                "INSERT INTO scratch (a) VALUES (:v)",
                Bind(("v", SqliteHostBindingValue.Text(large))));
            var rows = connection.Query(
                "SELECT length(a), a FROM scratch", null,
                row => new { Length = row.GetInt64(0), Text = row.GetText(1) });
            var row0 = Assert.Single(rows);
            Assert.Equal(size, row0.Length);
            Assert.Equal(large, row0.Text);
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
        public void Float64_RoundTrip_SpecialValues()
        {
            // Columns with no declared type (BLOB affinity) so the IEEE 754
            // bits are stored verbatim as an 8-byte REAL record. In a REAL
            // column, SQLite's record format stores any value that equals an
            // integer AS that integer and converts it back on read — which
            // erases the sign of -0.0 (asserted at the end). Both behaviors
            // measured on real builds 3.9.0 through 3.53.x, all three
            // built-in adapters.
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (id INTEGER, a)", null);
            connection.Execute(
                "INSERT INTO scratch (id, a) VALUES (1, :max), (2, :min), (3, :tiny), (4, :negZero)",
                Bind(
                    ("max", SqliteHostBindingValue.Float64(double.MaxValue)),
                    ("min", SqliteHostBindingValue.Float64(double.MinValue)),
                    ("tiny", SqliteHostBindingValue.Float64(1e-300)),
                    ("negZero", SqliteHostBindingValue.Float64(-0.0))));
            var rows = connection.Query(
                "SELECT a FROM scratch ORDER BY id", null, row => row.GetFloat64(0));
            Assert.Equal(4, rows.Count);
            Assert.Equal(double.MaxValue, rows[0]);
            Assert.Equal(double.MinValue, rows[1]);
            Assert.Equal(1e-300, rows[2]);
            // Bit-level: plain Equal would also accept +0.0 (they compare ==).
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(-0.0),
                BitConverter.DoubleToInt64Bits(rows[3]));

            // Documenting assertion (engine record format, not the adapter):
            // through a REAL column the same -0.0 reads back as +0.0.
            connection.Execute("CREATE TABLE scratch_real (a REAL)", null);
            connection.Execute(
                "INSERT INTO scratch_real (a) VALUES (:negZero)",
                Bind(("negZero", SqliteHostBindingValue.Float64(-0.0))));
            var normalized = connection.Query(
                "SELECT a, typeof(a) FROM scratch_real", null,
                row => new { Value = row.GetFloat64(0), Type = row.GetText(1) });
            var normalized0 = Assert.Single(normalized);
            Assert.Equal("real", normalized0.Type);
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(0.0),
                BitConverter.DoubleToInt64Bits(normalized0.Value));
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

        [SkippableFact]
        public void UnknownBinding_FailsMissingBinding_WithBindingName()
        {
            SkipIfSuiteDisabled();
            SqliteHostRunResult result = RunScript(
                NewScript(
                    Step("only",
                        Statement(
                            "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, :ghost)",
                            ("callId", SqliteHostBindingValue.Text("c-1"))))),
                new RecordingProbeHandlers());

            Assert.Equal(SqliteHostRunStatus.FailedBinding, result.Status);
            Assert.Equal("missing-binding", result.ErrorCode);
            Assert.Equal("ghost", result.BindingName);
        }

        [SkippableFact]
        public void ExtraBinding_FailsUnusedBinding_WithBindingName()
        {
            SkipIfSuiteDisabled();
            SqliteHostRunResult result = RunScript(
                NewScript(
                    Step("only",
                        Statement(
                            "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'k')",
                            ("callId", SqliteHostBindingValue.Text("c-1")),
                            ("leftover", SqliteHostBindingValue.Int64(9))))),
                new RecordingProbeHandlers());

            Assert.Equal(SqliteHostRunStatus.FailedBinding, result.Status);
            Assert.Equal("unused-binding", result.ErrorCode);
            Assert.Equal("leftover", result.BindingName);
        }

        [SkippableFact]
        public void ErrorMidStep_AbortsTheStep_NoLaterStatement_NoHandlerForEarlierInsert()
        {
            SkipIfSuiteDisabled();
            var handlers = new RecordingProbeHandlers();
            using var factory = new AdapterWorkspaceFactory(OpenAdapterConnection, retainWorkspace: true);

            SqliteHostRunResult result = RunScript(
                NewScript(
                    Step("broken",
                        Statement(
                            "INSERT INTO call_get_value (call_id, input_key) VALUES ('g-1', 'k1')"),
                        Statement("INSERT INTO no_such_table (x) VALUES (1)"),
                        Statement(
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

        // ---- minimal probe host (self-contained; public fluent API) ------

        private sealed class ProbeInput
        {
            public string Key { get; set; }
        }

        private sealed class ProbeResult
        {
            public long Value { get; set; }
        }

        private interface IProbeHandlers
        {
            ProbeResult GetValue(ProbeInput input);
        }

        private sealed class RecordingProbeHandlers : IProbeHandlers
        {
            /// <summary>"getValue:&lt;key&gt;" entries in call order.</summary>
            public List<string> Log { get; } = new List<string>();

            public ProbeResult GetValue(ProbeInput input)
            {
                Log.Add("getValue:" + input.Key);
                return new ProbeResult { Value = 0 };
            }
        }

        /// <summary>
        /// Single-method host ("getValue": text key in, long value out).
        /// Schema tables: call_get_value / result_get_value plus the
        /// engine-level pending_host_calls and script_inputs.
        /// </summary>
        private static SqliteHostDefinition<IProbeHandlers> BuildProbeHost()
        {
            return SqliteHostDefinition
                .ForHandlers<IProbeHandlers>()
                .ApiLevel(1)
                .Methods(new IHostMethodSpec<IProbeHandlers>[]
                {
                    HostMethod
                        .For<IProbeHandlers, ProbeInput, ProbeResult>("getValue")
                        .ApiLevel(1)
                        .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                        .Results(r => r.Long("value", x => x.Value))
                        .Handler((handlers, input) => handlers.GetValue(input))
                        .Build()
                });
        }

        private SqliteHostRunResult RunScript(
            SqliteHostScript script,
            RecordingProbeHandlers handlers,
            AdapterWorkspaceFactory factory = null)
        {
            var runtime = new SqliteHostRuntime<IProbeHandlers>(
                connectionFactory: factory ?? new AdapterWorkspaceFactory(OpenAdapterConnection),
                hostDefinition: BuildProbeHost(),
                handlers: handlers,
                options: null);
            return runtime.Run(script);
        }

        private static SqliteHostScript NewScript(params SqliteHostStep[] steps)
        {
            return new SqliteHostScript
            {
                Engine = "sqlite-host-v1",
                ScriptId = "conformance-script",
                RequiredApiLevel = 1,
                Steps = new List<SqliteHostStep>(steps)
            };
        }

        private static SqliteHostStep Step(string id, params SqliteHostStatement[] statements)
        {
            return new SqliteHostStep
            {
                Id = id,
                Statements = new List<SqliteHostStatement>(statements)
            };
        }

        private static SqliteHostStatement Statement(
            string sql,
            params (string Name, SqliteHostBindingValue Value)[] bindings)
        {
            var statement = new SqliteHostStatement { Sql = sql };
            if (bindings.Length > 0)
            {
                statement.Bindings = new Dictionary<string, SqliteHostBindingValue>();
                foreach ((string name, SqliteHostBindingValue value) in bindings)
                {
                    statement.Bindings.Add(name, value);
                }
            }
            return statement;
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
}
