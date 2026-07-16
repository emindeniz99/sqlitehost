using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Test-only System.Data.SQLite (ADO.NET) adapter for the multi-adapter
    /// integration matrix. System.Data.SQLite supports named parameters
    /// natively; the binding rule mirrors MicrosoftDataSqliteAdapter: each
    /// bare binding name is added under all three prefixed spellings
    /// (:name / @name / $name) so every occurrence in the SQL is covered,
    /// and command parameters not present in the SQL are ignored.
    ///
    /// Note: System.Data.SQLite ships its own interop layer and bundled
    /// native SQLite — it is NOT governed by the SQLITEHOST_NATIVE_SQLITE
    /// dynamic-provider override (which only affects SQLitePCLRaw users).
    /// </summary>
    public sealed class SystemDataSqliteConnection : ISqliteHostScalarFunctionConnection
    {
        private readonly SQLiteConnection _connection;

        public SystemDataSqliteConnection(SQLiteConnection connection)
        {
            _connection = connection;
        }

        public static SystemDataSqliteConnection OpenInMemory()
        {
            var connection = new SQLiteConnection("Data Source=:memory:");
            connection.Open();
            return new SystemDataSqliteConnection(connection);
        }

        public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            using var command = CreateCommand(sql, bindings);
            try
            {
                command.ExecuteNonQuery();
            }
            catch (SQLiteException ex)
            {
                throw Wrap(ex);
            }
        }

        public IReadOnlyList<object> QueryRows(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, object> mapper)
        {
            using var command = CreateCommand(sql, bindings);
            try
            {
                using var reader = command.ExecuteReader();
                var row = new SystemDataSqliteRow(reader);
                var results = new List<object>();
                while (reader.Read())
                {
                    results.Add(mapper(row));
                }
                return results;
            }
            catch (SQLiteException ex)
            {
                throw Wrap(ex);
            }
        }

        /// <summary>
        /// Optional capability (docs/adapter-contract.md): binds one
        /// SQLiteFunction per arity in MinArgs..MaxArgs via BindFunction
        /// with a per-arity SQLiteFunctionAttribute (no SQLITE_DETERMINISTIC
        /// flags in v1).
        /// </summary>
        public void RegisterScalarFunction(SqliteHostScalarFunction function)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }
            for (int arity = function.MinArgs; arity <= function.MaxArgs; arity++)
            {
                _connection.BindFunction(
                    new SQLiteFunctionAttribute(function.Name, arity, FunctionType.Scalar),
                    new ScalarFunctionBinding(function));
            }
        }

        /// <summary>
        /// System.Data.SQLite SWALLOWS exceptions thrown out of
        /// SQLiteFunction.Invoke (the statement then sees a NULL result) —
        /// verified behavior. Its supported error channel is RETURNING an
        /// Exception object, which SetReturnValue turns into
        /// sqlite3_result_error with the exception's message; that carries
        /// the SQLITEHOST_HANDLER_ERROR: marker into the statement's
        /// SQLiteException text.
        /// </summary>
        private sealed class ScalarFunctionBinding : SQLiteFunction
        {
            private readonly SqliteHostScalarFunction _function;

            public ScalarFunctionBinding(SqliteHostScalarFunction function)
            {
                _function = function;
            }

            public override object Invoke(object[] args)
            {
                try
                {
                    var values = new SqliteHostBindingValue[args.Length];
                    for (int i = 0; i < args.Length; i++)
                    {
                        values[i] = FromFunctionArgument(args[i]);
                    }
                    SqliteHostBindingValue result = _function.Invoke(values) ?? SqliteHostBindingValue.Null();
                    return ToFunctionResult(result);
                }
                catch (Exception ex)
                {
                    return new InvalidOperationException(
                        SqliteHostScalarFunction.HandlerErrorMarker + " " + ex.Message);
                }
            }
        }

        private static SqliteHostBindingValue FromFunctionArgument(object value)
        {
            switch (value)
            {
                case null:
                case DBNull _:
                    return SqliteHostBindingValue.Null();
                case long int64:
                    return SqliteHostBindingValue.Int64(int64);
                case double float64:
                    return SqliteHostBindingValue.Float64(float64);
                case string text:
                    return SqliteHostBindingValue.Text(text);
                case byte[] blob:
                    return SqliteHostBindingValue.Blob(blob);
                default:
                    throw new InvalidOperationException(
                        "Unexpected scalar function argument type " + value.GetType().Name + ".");
            }
        }

        private static object ToFunctionResult(SqliteHostBindingValue value)
        {
            switch (value.Type)
            {
                case SqliteHostBindingType.Int32:
                    return (long)value.Int32Value;
                case SqliteHostBindingType.Int64:
                    return value.Int64Value;
                case SqliteHostBindingType.Bool:
                    return value.BoolValue ? 1L : 0L;
                case SqliteHostBindingType.Text:
                    return value.TextValue;
                case SqliteHostBindingType.Blob:
                    return value.BlobValue;
                case SqliteHostBindingType.Float32:
                    return (double)value.Float32Value;
                case SqliteHostBindingType.Float64:
                    return value.Float64Value;
                default:
                    return DBNull.Value;
            }
        }

        /// <summary>Adapter contract: surface native failures with their SQLite result code.</summary>
        private static SqliteHostAdapterException Wrap(SQLiteException ex)
            => new SqliteHostAdapterException(ex.Message, (int)ex.ResultCode, ex);

        public void Dispose()
        {
            _connection.Dispose();
        }

        private SQLiteCommand CreateCommand(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            if (bindings != null)
            {
                foreach (SqliteHostBinding binding in bindings)
                {
                    object value = ToParameterValue(binding.Value);
                    command.Parameters.AddWithValue(":" + binding.Name, value);
                    command.Parameters.AddWithValue("@" + binding.Name, value);
                    command.Parameters.AddWithValue("$" + binding.Name, value);
                }
            }
            return command;
        }

        private static object ToParameterValue(SqliteHostBindingValue value)
        {
            switch (value.Type)
            {
                case SqliteHostBindingType.Int32:
                    return (long)value.Int32Value;
                case SqliteHostBindingType.Int64:
                    return value.Int64Value;
                case SqliteHostBindingType.Bool:
                    return value.BoolValue ? 1L : 0L;
                case SqliteHostBindingType.Text:
                    return (object)value.TextValue ?? DBNull.Value;
                case SqliteHostBindingType.Blob:
                    return (object)value.BlobValue ?? DBNull.Value;
                case SqliteHostBindingType.Float32:
                    return (double)value.Float32Value;
                case SqliteHostBindingType.Float64:
                    return value.Float64Value;
                default:
                    return DBNull.Value;
            }
        }
    }

    public sealed class SystemDataSqliteRow : ISqliteHostRow
    {
        private readonly SQLiteDataReader _reader;

        public SystemDataSqliteRow(SQLiteDataReader reader)
        {
            _reader = reader;
        }

        public bool IsNull(int index) => _reader.IsDBNull(index);
        public int GetInt32(int index) => _reader.GetInt32(index);
        public long GetInt64(int index) => _reader.GetInt64(index);
        public bool GetBool(int index) => _reader.GetInt64(index) != 0;
        public string GetText(int index) => _reader.GetString(index);
        public byte[] GetBlob(int index) => (byte[])_reader.GetValue(index);
        public float GetFloat32(int index) => _reader.GetFloat(index);
        public double GetFloat64(int index) => _reader.GetDouble(index);
    }

    /// <summary>In-memory workspace factory over the System.Data.SQLite adapter (scalar-function capable).</summary>
    public sealed class SystemDataSqliteWorkspaceFactory : ISqliteHostScalarFunctionCapableFactory
    {
        public ISqliteHostConnection OpenWorkspace() => SystemDataSqliteConnection.OpenInMemory();
    }
}
