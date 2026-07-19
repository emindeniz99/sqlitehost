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

        [SkippableFact]
        public void Execute_RowProducingStatement_RunsToCompletion_LaterRowErrorSurfaces()
        {
            // SQLite evaluates a SELECT only as it is stepped; an Execute
            // that stops at the first SQLITE_ROW reports success for a
            // statement that never finished running — exactly the silent
            // failure docs/adapter-contract.md forbids. abs(long.MinValue)
            // raises "integer overflow" only when row 2 is stepped, so this
            // test can only pass if Execute evaluates every row. No ORDER
            // BY on purpose: a sort would materialize every row into the
            // sorter on the FIRST step and mask a stop-at-first-row Execute;
            // a plain table scan steps rows lazily in rowid order.
            using ISqliteHostConnection connection = Open();
            connection.Execute("CREATE TABLE scratch (id INTEGER, a INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (id, a) VALUES (1, 1), (2, :min)",
                Bind(("min", SqliteHostBindingValue.Int64(long.MinValue))));
            Assert.ThrowsAny<SqliteHostAdapterException>(
                () => connection.Execute("SELECT abs(a) FROM scratch", null));
        }

        // ---- optional capability: inline scalar functions ----------------
        // Runs only against adapters implementing
        // ISqliteHostScalarFunctionConnection; skipped with a reason
        // everywhere else (docs/adapter-contract.md).

        /// <summary>
        /// Opens a workspace and returns its scalar-function surface;
        /// skips the test when the adapter does not implement the optional
        /// capability.
        /// </summary>
        private ISqliteHostScalarFunctionConnection OpenFunctionCapable()
        {
            ISqliteHostConnection connection = Open();
            var functions = connection as ISqliteHostScalarFunctionConnection;
            if (functions == null)
            {
                connection.Dispose();
                Skip.If(true, "Adapter does not implement ISqliteHostScalarFunctionConnection (optional capability).");
            }
            return functions;
        }

        private static SqliteHostScalarFunction Constant(
            string name, SqliteHostBindingValue result)
        {
            return new SqliteHostScalarFunction(name, 0, 0, args => result);
        }

        [SkippableFact]
        public void ScalarFunction_ResultRoundTrip_EveryBindingType()
        {
            using ISqliteHostScalarFunctionConnection connection = OpenFunctionCapable();
            connection.RegisterScalarFunction(Constant("fn_i32", SqliteHostBindingValue.Int32(-12)));
            connection.RegisterScalarFunction(Constant("fn_i64", SqliteHostBindingValue.Int64(5_000_000_123L)));
            connection.RegisterScalarFunction(Constant("fn_true", SqliteHostBindingValue.Bool(true)));
            connection.RegisterScalarFunction(Constant("fn_false", SqliteHostBindingValue.Bool(false)));
            connection.RegisterScalarFunction(Constant("fn_text", SqliteHostBindingValue.Text("değer 🎮")));
            connection.RegisterScalarFunction(Constant("fn_blob", SqliteHostBindingValue.Blob(new byte[] { 0xDE, 0xAD })));
            connection.RegisterScalarFunction(Constant("fn_f32", SqliteHostBindingValue.Float32(0.75f)));
            connection.RegisterScalarFunction(Constant("fn_f64", SqliteHostBindingValue.Float64(-98.5)));
            connection.RegisterScalarFunction(Constant("fn_nil", SqliteHostBindingValue.Null()));

            AssertSingleRow(connection, "SELECT fn_i32(), typeof(fn_i32())",
                row => row.GetInt32(0) + "|" + row.GetText(1), "-12|integer");
            AssertSingleRow(connection, "SELECT fn_i64(), typeof(fn_i64())",
                row => row.GetInt64(0) + "|" + row.GetText(1), "5000000123|integer");
            AssertSingleRow(connection, "SELECT fn_true(), fn_false(), typeof(fn_true())",
                row => row.GetInt64(0) + "|" + row.GetInt64(1) + "|" + row.GetText(2), "1|0|integer");
            AssertSingleRow(connection, "SELECT fn_text(), typeof(fn_text())",
                row => row.GetText(0) + "|" + row.GetText(1), "değer 🎮|text");
            AssertSingleRow(connection, "SELECT hex(fn_blob()), typeof(fn_blob())",
                row => row.GetText(0) + "|" + row.GetText(1), "DEAD|blob");
            var f32 = connection.Query("SELECT fn_f32(), typeof(fn_f32())", null,
                row => new { Value = row.GetFloat32(0), Type = row.GetText(1) });
            var f32Row = Assert.Single(f32);
            Assert.Equal(0.75f, f32Row.Value);
            Assert.Equal("real", f32Row.Type);
            var f64 = connection.Query("SELECT fn_f64(), typeof(fn_f64())", null,
                row => new { Value = row.GetFloat64(0), Type = row.GetText(1) });
            var f64Row = Assert.Single(f64);
            Assert.Equal(-98.5, f64Row.Value);
            Assert.Equal("real", f64Row.Type);
            AssertSingleRow(connection, "SELECT typeof(fn_nil())",
                row => row.GetText(0), "null");
        }

        [SkippableFact]
        public void ScalarFunction_ArgumentsArriveDynamicallyTyped()
        {
            using ISqliteHostScalarFunctionConnection connection = OpenFunctionCapable();
            var received = new List<SqliteHostBindingValue[]>();
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_probe", 5, 5,
                args => { received.Add(args); return SqliteHostBindingValue.Int64(1); }));

            connection.Query(
                "SELECT fn_probe(12, 2.5, 'metin', x'0102', NULL)", null, row => row.GetInt64(0));

            var args0 = Assert.Single(received);
            Assert.Equal(5, args0.Length);
            Assert.Equal(SqliteHostBindingType.Int64, args0[0].Type);
            Assert.Equal(12L, args0[0].Int64Value);
            Assert.Equal(SqliteHostBindingType.Float64, args0[1].Type);
            Assert.Equal(2.5, args0[1].Float64Value);
            Assert.Equal(SqliteHostBindingType.Text, args0[2].Type);
            Assert.Equal("metin", args0[2].TextValue);
            Assert.Equal(SqliteHostBindingType.Blob, args0[3].Type);
            Assert.Equal(new byte[] { 0x01, 0x02 }, args0[3].BlobValue);
            Assert.Equal(SqliteHostBindingType.Null, args0[4].Type);
        }

        [SkippableFact]
        public void ScalarFunction_ArityRange_RegistersEveryArity_OmittedTrailingReadsAsNull()
        {
            using ISqliteHostScalarFunctionConnection connection = OpenFunctionCapable();
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_arity", 1, 3,
                args =>
                {
                    // The invoked arity arrives as args.Length; the consumer
                    // treats omitted trailing args exactly like SQL NULLs.
                    var parts = new List<string>();
                    foreach (SqliteHostBindingValue arg in args)
                    {
                        parts.Add(arg.Type == SqliteHostBindingType.Text ? arg.TextValue : "-");
                    }
                    return SqliteHostBindingValue.Text(args.Length + ":" + string.Join("|", parts));
                }));

            AssertSingleRow(connection, "SELECT fn_arity('a')", row => row.GetText(0), "1:a");
            AssertSingleRow(connection, "SELECT fn_arity('a', 'b')", row => row.GetText(0), "2:a|b");
            AssertSingleRow(connection, "SELECT fn_arity('a', NULL)", row => row.GetText(0), "2:a|-");
            AssertSingleRow(connection, "SELECT fn_arity('a', 'b', 'c')", row => row.GetText(0), "3:a|b|c");

            // Arities outside MinArgs..MaxArgs are NOT registered.
            Assert.ThrowsAny<Exception>(
                () => connection.Query("SELECT fn_arity()", null, row => row.GetText(0)));
            Assert.ThrowsAny<Exception>(
                () => connection.Query("SELECT fn_arity('a', 'b', 'c', 'd')", null, row => row.GetText(0)));
        }

        [SkippableFact]
        public void ScalarFunction_UnicodeArguments_RoundTrip()
        {
            const string turkish = "İstanbul'da şöğüçı ĞÜŞİÖÇ";
            const string emoji = "🎮 sqlite host 🚀✨";
            using ISqliteHostScalarFunctionConnection connection = OpenFunctionCapable();
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_echo", 1, 1, args => args[0]));

            AssertSingleRow(connection,
                "SELECT fn_echo('" + turkish.Replace("'", "''") + "')", row => row.GetText(0), turkish);
            AssertSingleRow(connection,
                "SELECT fn_echo('" + emoji + "')", row => row.GetText(0), emoji);
        }

        [SkippableFact]
        public void ScalarFunction_InvokeThrow_FailsTheStatementWithMarker_NeverCrashes()
        {
            using ISqliteHostScalarFunctionConnection connection = OpenFunctionCapable();
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_boom", 1, 1,
                args => throw new InvalidOperationException("kaboom")));

            var ex = Assert.ThrowsAny<Exception>(
                () => connection.Query("SELECT fn_boom(1)", null, row => row.GetInt64(0)));
            Assert.Contains(SqliteHostScalarFunction.HandlerErrorMarker, ex.Message);
            Assert.Contains("kaboom", ex.Message);

            // The exception traveled through the SQL error channel, never
            // across the native frames: the connection stays fully usable.
            var rows = connection.Query("SELECT 41 + 1", null, row => row.GetInt64(0));
            Assert.Equal(new[] { 42L }, rows);
        }

        [SkippableFact]
        public void ScalarFunction_ExecuteEvaluatesEveryRow()
        {
            // The runtime routes every script statement through Execute
            // (SqliteHostRuntimeCore), so inline-function invocations in
            // later rows must not silently vanish: Execute must step a
            // row-producing statement to completion even though it discards
            // the rows.
            using ISqliteHostScalarFunctionConnection connection = OpenFunctionCapable();
            int invocations = 0;
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_probe", 1, 1,
                args => { invocations++; return SqliteHostBindingValue.Int64(1); }));
            connection.Execute("CREATE TABLE scratch (a INTEGER)", null);
            connection.Execute("INSERT INTO scratch (a) VALUES (1), (2), (3)", null);

            connection.Execute("SELECT fn_probe(a) FROM scratch", null);

            Assert.Equal(3, invocations);
        }

        [SkippableFact]
        public void ScalarFunction_NullForRequiredArg_FailsAsHandlerError_ThroughTheRuntime()
        {
            SkipIfSuiteDisabled();
            using (ISqliteHostConnection probe = OpenAdapterConnection())
            {
                Skip.If(!(probe is ISqliteHostScalarFunctionConnection),
                    "Adapter does not implement ISqliteHostScalarFunctionConnection (optional capability).");
            }
            var handlers = new RecordingProbeHandlers();

            SqliteHostRunResult result = RunScript(
                NewScript(
                    Step("only",
                        Statement(
                            "INSERT INTO call_get_value (call_id, input_key)"
                            + " SELECT 'c-1', 'k' WHERE fn_get_value(NULL) <> 0"))),
                handlers);

            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
            Assert.Equal("handler-error", result.ErrorCode);
            Assert.Equal("getValue", result.Method);
            Assert.Contains("required", result.ErrorMessage);
            Assert.Empty(handlers.Log);   // the handler itself never ran
            Assert.Equal(0, result.InlineCallCount);
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
        /// engine-level pending_host_calls and script_inputs. The method is
        /// also inline-exposed as fn_get_value; on adapters without the
        /// optional scalar-function capability the runtime simply skips the
        /// registration.
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
                        .Inline("fn_get_value")
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
