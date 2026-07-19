using System;
using System.Collections.Generic;
using SQLitePCL;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Test-only sqlite-net (sqlite-net-pcl) adapter for the multi-adapter
    /// integration matrix, mirroring how a Unity adapter over the
    /// SQLite4Unity3d-style wrapper would work:
    ///
    ///  - sqlite-net's SQLite.SQLiteConnection owns open/lifecycle (the
    ///    thing a Unity game already has around);
    ///  - its raw database Handle is used with SQLitePCL.raw for statement
    ///    preparation and NAMED-parameter binding, because sqlite-net's own
    ///    Execute/Query APIs are positional-'?' only and cannot express
    ///    :name / @name / $name bindings.
    ///
    /// Binding rule (same as MicrosoftDataSqliteAdapter): each bare binding
    /// name is bound under every prefixed occurrence present in the SQL
    /// (:name / @name / $name via sqlite3_bind_parameter_index); names not
    /// present are ignored. Float mapping also mirrors the fixed semantics:
    /// Float32 binds as a double, Bool as 0/1 integer, Int32 widens to long.
    ///
    /// Note: sqlite-net-pcl rides on SQLitePCLRaw (bundle_green), so unlike
    /// System.Data.SQLite it WOULD see the SQLITEHOST_NATIVE_SQLITE
    /// dynamic-provider override; its integration tests are still skipped in
    /// override runs to keep the version matrix scoped to the
    /// Microsoft.Data.Sqlite adapter (see IntegrationFixtureTests).
    /// </summary>
    public sealed class SqliteNetConnection : ISqliteHostPrepareConnection, ISqliteHostScalarFunctionConnection
    {
        private readonly SQLite.SQLiteConnection _connection;

        public SqliteNetConnection(SQLite.SQLiteConnection connection)
        {
            _connection = connection;
        }

        public static SqliteNetConnection OpenInMemory()
        {
            return new SqliteNetConnection(new SQLite.SQLiteConnection(
                ":memory:",
                SQLite.SQLiteOpenFlags.ReadWrite | SQLite.SQLiteOpenFlags.Create));
        }

        private sqlite3 Handle => _connection.Handle;

        public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            using sqlite3_stmt statement = PrepareCore(sql, bindings);
            // SQLite evaluates a row-producing statement only as it is
            // stepped: drain to SQLITE_DONE (discarding rows) so later-row
            // evaluation — errors and inline function invocations — is
            // never silently skipped (docs/adapter-contract.md).
            int rc;
            while ((rc = raw.sqlite3_step(statement)) == raw.SQLITE_ROW)
            {
            }
            if (rc != raw.SQLITE_DONE)
            {
                throw Error("sqlite3_step", rc);
            }
        }

        public IReadOnlyList<object> QueryRows(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, object> mapper)
        {
            using sqlite3_stmt statement = PrepareCore(sql, bindings);
            var row = new SqliteNetRow(statement);
            var results = new List<object>();
            while (true)
            {
                int rc = raw.sqlite3_step(statement);
                if (rc == raw.SQLITE_ROW)
                {
                    results.Add(mapper(row));
                    continue;
                }
                if (rc == raw.SQLITE_DONE)
                {
                    return results;
                }
                throw Error("sqlite3_step", rc);
            }
        }

        public ISqliteHostPreparedStatement Prepare(string sql)
        {
            return new SqliteNetPreparedStatement(PrepareOnly(sql));
        }

        /// <summary>
        /// Optional capability (docs/adapter-contract.md) — THE reference
        /// native-style implementation: sqlite3_create_function on the raw
        /// handle, once per arity in MinArgs..MaxArgs (no
        /// SQLITE_DETERMINISTIC in v1). Args are read with sqlite3_value_*,
        /// the result is set with sqlite3_result_*, and EVERYTHING thrown
        /// by Invoke is reported via sqlite3_result_error with the
        /// SQLITEHOST_HANDLER_ERROR: marker — an exception must never cross
        /// the native frames (IL2CPP safety).
        /// </summary>
        public void RegisterScalarFunction(SqliteHostScalarFunction function)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }
            for (int arity = function.MinArgs; arity <= function.MaxArgs; arity++)
            {
                int rc = raw.sqlite3_create_function(
                    Handle, function.Name, arity, null,
                    delegate(sqlite3_context ctx, object userData, sqlite3_value[] args)
                    {
                        InvokeScalarFunction(function, ctx, args);
                    });
                if (rc != raw.SQLITE_OK)
                {
                    throw Error("sqlite3_create_function", rc);
                }
            }
        }

        private static void InvokeScalarFunction(
            SqliteHostScalarFunction function, sqlite3_context ctx, sqlite3_value[] args)
        {
            SqliteHostBindingValue result;
            try
            {
                int count = args == null ? 0 : args.Length;
                var values = new SqliteHostBindingValue[count];
                for (int i = 0; i < count; i++)
                {
                    values[i] = FromFunctionArgument(args[i]);
                }
                result = function.Invoke(values) ?? SqliteHostBindingValue.Null();
            }
            catch (Exception ex)
            {
                raw.sqlite3_result_error(
                    ctx, SqliteHostScalarFunction.HandlerErrorMarker + " " + ex.Message);
                return;
            }
            SetFunctionResult(ctx, result);
        }

        private static SqliteHostBindingValue FromFunctionArgument(sqlite3_value value)
        {
            switch (raw.sqlite3_value_type(value))
            {
                case raw.SQLITE_INTEGER:
                    return SqliteHostBindingValue.Int64(raw.sqlite3_value_int64(value));
                case raw.SQLITE_FLOAT:
                    return SqliteHostBindingValue.Float64(raw.sqlite3_value_double(value));
                case raw.SQLITE_TEXT:
                    return SqliteHostBindingValue.Text(raw.sqlite3_value_text(value).utf8_to_string());
                case raw.SQLITE_BLOB:
                    return SqliteHostBindingValue.Blob(raw.sqlite3_value_blob(value).ToArray());
                default:
                    return SqliteHostBindingValue.Null();
            }
        }

        private static void SetFunctionResult(sqlite3_context ctx, SqliteHostBindingValue value)
        {
            switch (value.Type)
            {
                case SqliteHostBindingType.Int32:
                    raw.sqlite3_result_int64(ctx, value.Int32Value);
                    break;
                case SqliteHostBindingType.Int64:
                    raw.sqlite3_result_int64(ctx, value.Int64Value);
                    break;
                case SqliteHostBindingType.Bool:
                    raw.sqlite3_result_int64(ctx, value.BoolValue ? 1L : 0L);
                    break;
                case SqliteHostBindingType.Text:
                    if (value.TextValue == null)
                    {
                        raw.sqlite3_result_null(ctx);
                    }
                    else
                    {
                        raw.sqlite3_result_text(ctx, value.TextValue);
                    }
                    break;
                case SqliteHostBindingType.Blob:
                    if (value.BlobValue == null)
                    {
                        raw.sqlite3_result_null(ctx);
                    }
                    else
                    {
                        raw.sqlite3_result_blob(ctx, value.BlobValue);
                    }
                    break;
                case SqliteHostBindingType.Float32:
                    raw.sqlite3_result_double(ctx, (double)value.Float32Value);
                    break;
                case SqliteHostBindingType.Float64:
                    raw.sqlite3_result_double(ctx, value.Float64Value);
                    break;
                default:
                    raw.sqlite3_result_null(ctx);
                    break;
            }
        }

        public void Dispose()
        {
            _connection.Dispose();
        }

        private sqlite3_stmt PrepareOnly(string sql)
        {
            int rc = raw.sqlite3_prepare_v2(Handle, sql, out sqlite3_stmt statement);
            if (rc != raw.SQLITE_OK)
            {
                throw Error("sqlite3_prepare_v2", rc);
            }
            return statement;
        }

        private sqlite3_stmt PrepareCore(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            sqlite3_stmt statement = PrepareOnly(sql);
            try
            {
                if (bindings != null)
                {
                    foreach (SqliteHostBinding binding in bindings)
                    {
                        BindUnderAllPrefixes(statement, binding);
                    }
                }
                RejectUnboundParameters(statement, bindings);
            }
            catch
            {
                statement.Dispose();
                throw;
            }
            return statement;
        }

        /// <summary>
        /// Adapter contract (docs/adapter-contract.md): a parameter the
        /// payload did not bind must never execute as a silent NULL. Raw
        /// sqlite3 leaves unbound parameters NULL, so verify every statement
        /// parameter received a binding before stepping.
        /// </summary>
        private static void RejectUnboundParameters(
            sqlite3_stmt statement, IReadOnlyList<SqliteHostBinding> bindings)
        {
            int count = raw.sqlite3_bind_parameter_count(statement);
            for (int i = 1; i <= count; i++)
            {
                string name = raw.sqlite3_bind_parameter_name(statement, i).utf8_to_string();
                // Nameless (?NNN) parameters can never be fed by bare-name
                // bindings; treat them as unbound too.
                string bareName = string.IsNullOrEmpty(name) ? null : name.Substring(1);
                bool bound = false;
                if (bareName != null && bindings != null)
                {
                    foreach (SqliteHostBinding binding in bindings)
                    {
                        if (binding.Name == bareName)
                        {
                            bound = true;
                            break;
                        }
                    }
                }
                if (!bound)
                {
                    throw new SqliteHostAdapterException(
                        "Parameter '" + (name ?? "?") + "' has no binding; refusing to execute with an implicit NULL.",
                        0, null);
                }
            }
        }

        private void BindUnderAllPrefixes(sqlite3_stmt statement, SqliteHostBinding binding)
        {
            // Bare names must bind every prefixed occurrence in the SQL;
            // prefixes absent from the statement resolve to index 0 and are
            // skipped, so collection parameters not present are ignored.
            foreach (string prefix in ParameterPrefixes)
            {
                int index = raw.sqlite3_bind_parameter_index(statement, prefix + binding.Name);
                if (index > 0)
                {
                    BindValue(statement, index, binding.Value);
                }
            }
        }

        private static readonly string[] ParameterPrefixes = { ":", "@", "$" };

        private void BindValue(sqlite3_stmt statement, int index, SqliteHostBindingValue value)
        {
            int rc;
            switch (value.Type)
            {
                case SqliteHostBindingType.Int32:
                    rc = raw.sqlite3_bind_int64(statement, index, value.Int32Value);
                    break;
                case SqliteHostBindingType.Int64:
                    rc = raw.sqlite3_bind_int64(statement, index, value.Int64Value);
                    break;
                case SqliteHostBindingType.Bool:
                    rc = raw.sqlite3_bind_int64(statement, index, value.BoolValue ? 1L : 0L);
                    break;
                case SqliteHostBindingType.Text:
                    rc = value.TextValue == null
                        ? raw.sqlite3_bind_null(statement, index)
                        : raw.sqlite3_bind_text(statement, index, value.TextValue);
                    break;
                case SqliteHostBindingType.Blob:
                    rc = value.BlobValue == null
                        ? raw.sqlite3_bind_null(statement, index)
                        : raw.sqlite3_bind_blob(statement, index, value.BlobValue);
                    break;
                case SqliteHostBindingType.Float32:
                    rc = raw.sqlite3_bind_double(statement, index, (double)value.Float32Value);
                    break;
                case SqliteHostBindingType.Float64:
                    rc = raw.sqlite3_bind_double(statement, index, value.Float64Value);
                    break;
                default:
                    rc = raw.sqlite3_bind_null(statement, index);
                    break;
            }
            if (rc != raw.SQLITE_OK)
            {
                throw Error("sqlite3_bind", rc);
            }
        }

        private SqliteHostAdapterException Error(string operation, int rc)
        {
            // Adapter contract: surface native failures with their SQLite result code.
            return new SqliteHostAdapterException(
                operation + " failed (" + rc + "): " + raw.sqlite3_errmsg(Handle).utf8_to_string(),
                rc, null);
        }
    }

    public sealed class SqliteNetRow : ISqliteHostRow
    {
        private readonly sqlite3_stmt _statement;

        public SqliteNetRow(sqlite3_stmt statement)
        {
            _statement = statement;
        }

        public bool IsNull(int index) => raw.sqlite3_column_type(_statement, index) == raw.SQLITE_NULL;
        public int GetInt32(int index) => raw.sqlite3_column_int(_statement, index);
        public long GetInt64(int index) => raw.sqlite3_column_int64(_statement, index);
        public bool GetBool(int index) => raw.sqlite3_column_int64(_statement, index) != 0;
        public string GetText(int index) => raw.sqlite3_column_text(_statement, index).utf8_to_string();
        public byte[] GetBlob(int index) => raw.sqlite3_column_blob(_statement, index).ToArray();
        public float GetFloat32(int index) => (float)raw.sqlite3_column_double(_statement, index);
        public double GetFloat64(int index) => raw.sqlite3_column_double(_statement, index);
    }

    public sealed class SqliteNetPreparedStatement : ISqliteHostPreparedStatement
    {
        private readonly sqlite3_stmt _statement;
        private readonly List<string> _parameterNames;

        public SqliteNetPreparedStatement(sqlite3_stmt statement)
        {
            _statement = statement;
            _parameterNames = new List<string>();
            int count = raw.sqlite3_bind_parameter_count(statement);
            for (int i = 1; i <= count; i++)
            {
                _parameterNames.Add(raw.sqlite3_bind_parameter_name(statement, i).utf8_to_string());
            }
        }

        public IReadOnlyList<string> ParameterNames => _parameterNames;

        public void Dispose()
        {
            _statement.Dispose();
        }
    }

    /// <summary>In-memory workspace factory over the sqlite-net adapter (scalar-function capable).</summary>
    public sealed class SqliteNetWorkspaceFactory : ISqliteHostScalarFunctionCapableFactory
    {
        public ISqliteHostConnection OpenWorkspace() => SqliteNetConnection.OpenInMemory();
    }
}
