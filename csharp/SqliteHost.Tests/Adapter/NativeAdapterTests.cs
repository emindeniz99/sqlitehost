using System;
using System.Collections.Generic;
using SqliteHost.Adapters.Native;
using Xunit;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Native-specific unit tests for the shippable
    /// SqliteHost.Adapters.Native P/Invoke adapter — behaviors that live
    /// below what the shared conformance suite proves: extended error
    /// codes, UTF-8 marshalling across the raw boundary, empty-vs-NULL blob
    /// binding, raw prepared-statement parameter names, dispose hygiene,
    /// and GCHandle lifecycle of scalar-function registrations.
    /// </summary>
    public class NativeAdapterTests
    {
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

        // ---- error surfacing: extended result codes -----------------------

        [Fact]
        public void ConstraintViolation_SurfacesTheExtendedErrorCode()
        {
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            connection.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY)", null);
            connection.Execute("INSERT INTO t (id) VALUES (1)", null);

            var ex = Assert.Throws<SqliteHostAdapterException>(
                () => connection.Execute("INSERT INTO t (id) VALUES (1)", null));

            // sqlite3_extended_errcode, not just the primary code: the low
            // byte is SQLITE_CONSTRAINT (19), the full value is
            // SQLITE_CONSTRAINT_PRIMARYKEY (1555) on every matrix engine
            // (extended constraint codes predate 3.9.0).
            Assert.Equal(19, ex.SqliteErrorCode & 0xFF);
            Assert.Equal(1555, ex.SqliteErrorCode);
            Assert.Contains("t.id", ex.Message);
        }

        [Fact]
        public void ScalarFunctionThrow_SurfacesAsAdapterException_WithMarkerAndSqliteError()
        {
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_boom", 0, 0, args => throw new InvalidOperationException("patladı 💥")));

            var ex = Assert.Throws<SqliteHostAdapterException>(
                () => connection.Query("SELECT fn_boom()", null, row => row.GetInt64(0)));

            // The exception travelled the sqlite3_result_error channel:
            // SQLITE_ERROR (1) with the marker-prefixed UTF-8 message intact.
            Assert.Equal(1, ex.SqliteErrorCode);
            Assert.Contains(SqliteHostScalarFunction.HandlerErrorMarker, ex.Message);
            Assert.Contains("patladı 💥", ex.Message);
        }

        // ---- UTF-8 fidelity across the raw boundary ------------------------

        [Fact]
        public void Utf8SqlLiterals_RoundTripThroughTheNativeBoundary()
        {
            const string turkish = "İstanbul'da şöğüçı ĞÜŞİÖÇ";
            const string emoji = "🎮 sqlite host 🚀✨";
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            connection.Execute("CREATE TABLE s (id INTEGER, v TEXT)", null);
            // Values embedded as SQL literals: the SQL text itself crosses
            // the boundary as manually marshalled UTF-8 bytes.
            connection.Execute(
                "INSERT INTO s (id, v) VALUES (1, '" + turkish.Replace("'", "''") + "'), (2, '" + emoji + "')",
                null);

            var rows = connection.Query(
                "SELECT v, length(CAST(v AS BLOB)) FROM s ORDER BY id", null,
                row => row.GetText(0) + "|" + row.GetInt64(1));

            Assert.Equal(
                new[]
                {
                    turkish + "|" + System.Text.Encoding.UTF8.GetByteCount(turkish),
                    emoji + "|" + System.Text.Encoding.UTF8.GetByteCount(emoji)
                },
                rows);
        }

        [Fact]
        public void Utf8FunctionNameArgumentsAndResult_RoundTripThroughTheNativeBoundary()
        {
            const string turkish = "İstanbul'da şöğüçı ĞÜŞİÖÇ";
            const string emoji = "🎮 sqlite host 🚀✨";
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            // Non-ASCII function NAME exercises the UTF-8 path of
            // sqlite3_create_function_v2 itself.
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_birleştir", 2, 2,
                args => SqliteHostBindingValue.Text(args[0].TextValue + "|" + args[1].TextValue)));

            var rows = connection.Query(
                "SELECT fn_birleştir(:a, :b)",
                Bind(
                    ("a", SqliteHostBindingValue.Text(turkish)),
                    ("b", SqliteHostBindingValue.Text(emoji))),
                row => row.GetText(0));

            Assert.Equal(new[] { turkish + "|" + emoji }, rows);
        }

        // ---- empty vs NULL blob ------------------------------------------------

        [Fact]
        public void EmptyBlobBinding_StaysABlob_DistinctFromNull()
        {
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            connection.Execute("CREATE TABLE b (id INTEGER, v BLOB)", null);
            connection.Execute(
                "INSERT INTO b (id, v) VALUES (1, :empty), (2, :nil)",
                Bind(
                    ("empty", SqliteHostBindingValue.Blob(new byte[0])),
                    ("nil", SqliteHostBindingValue.Null())));

            var rows = connection.Query(
                "SELECT typeof(v), v FROM b ORDER BY id", null,
                row => row.GetText(0) + "|" + row.IsNull(1) + "|" + row.GetBlob(1).Length);

            // Zero-length blob binds as an actual blob (sqlite3_bind_zeroblob
            // path), never as an implicit NULL; NULL stays NULL. GetBlob
            // returns an empty array in both cases, never null.
            Assert.Equal(new[] { "blob|False|0", "null|True|0" }, rows);
        }

        [Fact]
        public void EmptyBlobFunctionResult_StaysABlob_DistinctFromNull()
        {
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_empty_blob", 0, 0, args => SqliteHostBindingValue.Blob(new byte[0])));

            var rows = connection.Query(
                "SELECT typeof(fn_empty_blob()), length(fn_empty_blob())", null,
                row => row.GetText(0) + "|" + row.GetInt64(1));

            Assert.Equal(new[] { "blob|0" }, rows);
        }

        // ---- prepared statements ---------------------------------------------------

        [Fact]
        public void ParameterNames_AreRawWithPrefix_InStatementOrder()
        {
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            using ISqliteHostPreparedStatement statement =
                connection.Prepare("SELECT :a, @b, $c, :a");

            // Raw as written in the SQL, prefix included, one entry per
            // distinct parameter index (":a" appears once) — the same choice
            // the other adapters made.
            Assert.Equal(new[] { ":a", "@b", "$c" }, statement.ParameterNames);
        }

        // ---- binding resolution through the native path ------------------------------

        [Fact]
        public void SameBareName_UnderAllThreePrefixesInOneStatement_BindsEveryOccurrence()
        {
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            connection.Execute("CREATE TABLE s (a INTEGER, b INTEGER, c INTEGER)", null);
            connection.Execute(
                "INSERT INTO s (a, b, c) VALUES (:v, @v, $v)",
                Bind(("v", SqliteHostBindingValue.Int32(21))));

            var rows = connection.Query(
                "SELECT a, b, c FROM s", null,
                row => row.GetInt64(0) + "," + row.GetInt64(1) + "," + row.GetInt64(2));

            Assert.Equal(new[] { "21,21,21" }, rows);
        }

        // ---- dispose hygiene -------------------------------------------------------------

        [Fact]
        public void DoubleDispose_IsSafe_AndLaterUseFailsLoud_InsteadOfCrashing()
        {
            var connection = NativeSqliteHostConnection.OpenInMemory();
            connection.Execute("CREATE TABLE t (a INTEGER)", null);
            connection.Dispose();
            connection.Dispose();   // idempotent

            // A P/Invoke on the closed handle would be a native crash; the
            // adapter must fail loud in managed code instead.
            Assert.Throws<ObjectDisposedException>(
                () => connection.Execute("SELECT 1", null));
            Assert.Throws<ObjectDisposedException>(
                () => connection.Query("SELECT 1", null, row => row.GetInt64(0)));
            Assert.Throws<ObjectDisposedException>(
                () => connection.Prepare("SELECT :x"));
            Assert.Throws<ObjectDisposedException>(
                () => connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                    "fn_late", 0, 0, args => SqliteHostBindingValue.Null())));
        }

        [Fact]
        public void PreparedStatement_DisposedAfterTheConnection_DoesNotCrash()
        {
            var connection = NativeSqliteHostConnection.OpenInMemory();
            ISqliteHostPreparedStatement statement = connection.Prepare("SELECT :x, @y");
            Assert.Equal(new[] { ":x", "@y" }, statement.ParameterNames);

            // Connection dispose finalizes live statements; the statement's
            // own later Dispose must be a harmless no-op, not a double
            // finalize of a dangling pointer.
            connection.Dispose();
            statement.Dispose();
            statement.Dispose();
        }

        // ---- GCHandle lifecycle of scalar-function registrations ---------------------------

        [Fact]
        public void ScalarFunctionRegistrations_SurviveGc_AcrossRepeatedConnectionCycles()
        {
            // Each cycle: register (multi-arity = several GCHandles), force
            // a full GC while the registrations are live, invoke through the
            // native callback, dispose (sqlite3_close_v2 runs xDestroy and
            // frees every handle), then GC again. Any prematurely collected
            // delegate/context or double-freed handle crashes or corrupts
            // one of the later cycles.
            for (int cycle = 0; cycle < 5; cycle++)
            {
                using (var connection = NativeSqliteHostConnection.OpenInMemory())
                {
                    connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                        "fn_cycle", 0, 2,
                        args => SqliteHostBindingValue.Int64(args.Length + cycle * 10)));

                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    var rows = connection.Query(
                        "SELECT fn_cycle(), fn_cycle(1), fn_cycle(1, 2)", null,
                        row => row.GetInt64(0) + "," + row.GetInt64(1) + "," + row.GetInt64(2));
                    long expected = cycle * 10;
                    Assert.Equal(
                        new[] { expected + "," + (expected + 1) + "," + (expected + 2) },
                        rows);
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        [Fact]
        public void ReRegisteringTheSameNameAndArity_ReplacesTheFunction_AndFreesTheOldHandle()
        {
            using var connection = NativeSqliteHostConnection.OpenInMemory();
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_ver", 0, 0, args => SqliteHostBindingValue.Text("v1")));
            Assert.Equal(new[] { "v1" },
                connection.Query("SELECT fn_ver()", null, row => row.GetText(0)));

            // SQLite invokes xDestroy on the replaced registration's user
            // data; the old GCHandle is freed while the connection stays up.
            connection.RegisterScalarFunction(new SqliteHostScalarFunction(
                "fn_ver", 0, 0, args => SqliteHostBindingValue.Text("v2")));
            GC.Collect();
            GC.WaitForPendingFinalizers();

            Assert.Equal(new[] { "v2" },
                connection.Query("SELECT fn_ver()", null, row => row.GetText(0)));
        }
    }
}
