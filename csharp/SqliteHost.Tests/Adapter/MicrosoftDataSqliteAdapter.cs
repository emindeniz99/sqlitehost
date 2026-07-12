using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SqliteHost.Conformance;
using SQLitePCL;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Test-only Microsoft.Data.Sqlite adapter (plan §31 resolved decision 7:
    /// the runtime packages stay dependency-free; the first official adapter
    /// lives in the test project).
    /// </summary>
    public sealed class MicrosoftDataSqliteConnection : ISqliteHostPrepareConnection, ISqliteHostScalarFunctionConnection
    {
        private readonly SqliteConnection _connection;

        public MicrosoftDataSqliteConnection(SqliteConnection connection)
        {
            _connection = connection;
        }

        public bool IsDisposed { get; private set; }

        public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            using var command = CreateCommand(sql, bindings);
            try
            {
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                throw Wrap(ex);
            }
        }

        public IReadOnlyList<T> Query<T>(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, T> mapper)
        {
            using var command = CreateCommand(sql, bindings);
            try
            {
                using var reader = command.ExecuteReader();
                var row = new MicrosoftDataSqliteRow(reader);
                var results = new List<T>();
                while (reader.Read())
                {
                    results.Add(mapper(row));
                }
                return results;
            }
            catch (SqliteException ex)
            {
                throw Wrap(ex);
            }
        }

        public ISqliteHostPreparedStatement Prepare(string sql)
        {
            sqlite3 db = _connection.Handle
                ?? throw new InvalidOperationException("Connection must be open to prepare statements.");
            int rc = raw.sqlite3_prepare_v2(db, sql, out sqlite3_stmt statement);
            if (rc != raw.SQLITE_OK)
            {
                throw new SqliteHostAdapterException(
                    "sqlite3_prepare_v2 failed (" + rc + "): " + raw.sqlite3_errmsg(db).utf8_to_string(),
                    rc, null);
            }
            return new MicrosoftDataSqlitePreparedStatement(statement);
        }

        /// <summary>
        /// Optional capability (docs/adapter-contract.md): registers the
        /// function once per arity in MinArgs..MaxArgs via the generic
        /// CreateFunction overloads (isDeterministic stays false — no
        /// SQLITE_DETERMINISTIC in v1). Microsoft.Data.Sqlite catches
        /// exceptions thrown inside the delegate and reports them through
        /// sqlite3_result_error with the exception message (verified
        /// behavior), so throwing with the marker-prefixed message makes
        /// SQLITEHOST_HANDLER_ERROR: reach the statement's SqliteException
        /// text.
        /// </summary>
        public void RegisterScalarFunction(SqliteHostScalarFunction function)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }
            for (int arity = function.MinArgs; arity <= function.MaxArgs; arity++)
            {
                RegisterScalarFunctionArity(function, arity);
            }
        }

        private void RegisterScalarFunctionArity(SqliteHostScalarFunction function, int arity)
        {
            switch (arity)
            {
                case 0:
                    _connection.CreateFunction(function.Name,
                        () => CallScalarFunction(function));
                    break;
                case 1:
                    _connection.CreateFunction<object, object>(function.Name,
                        a1 => CallScalarFunction(function, a1));
                    break;
                case 2:
                    _connection.CreateFunction<object, object, object>(function.Name,
                        (a1, a2) => CallScalarFunction(function, a1, a2));
                    break;
                case 3:
                    _connection.CreateFunction<object, object, object, object>(function.Name,
                        (a1, a2, a3) => CallScalarFunction(function, a1, a2, a3));
                    break;
                case 4:
                    _connection.CreateFunction<object, object, object, object, object>(function.Name,
                        (a1, a2, a3, a4) => CallScalarFunction(function, a1, a2, a3, a4));
                    break;
                case 5:
                    _connection.CreateFunction<object, object, object, object, object, object>(function.Name,
                        (a1, a2, a3, a4, a5) => CallScalarFunction(function, a1, a2, a3, a4, a5));
                    break;
                case 6:
                    _connection.CreateFunction<object, object, object, object, object, object, object>(function.Name,
                        (a1, a2, a3, a4, a5, a6) => CallScalarFunction(function, a1, a2, a3, a4, a5, a6));
                    break;
                case 7:
                    _connection.CreateFunction<object, object, object, object, object, object, object, object>(function.Name,
                        (a1, a2, a3, a4, a5, a6, a7) => CallScalarFunction(function, a1, a2, a3, a4, a5, a6, a7));
                    break;
                case 8:
                    _connection.CreateFunction<object, object, object, object, object, object, object, object, object>(function.Name,
                        (a1, a2, a3, a4, a5, a6, a7, a8) => CallScalarFunction(function, a1, a2, a3, a4, a5, a6, a7, a8));
                    break;
                default:
                    throw new SqliteHostAdapterException(
                        "This adapter registers scalar functions up to arity 8; '"
                        + function.Name + "' needs arity " + arity + ".",
                        0, null);
            }
        }

        private static object CallScalarFunction(SqliteHostScalarFunction function, params object[] args)
        {
            SqliteHostBindingValue result;
            try
            {
                var values = new SqliteHostBindingValue[args.Length];
                for (int i = 0; i < args.Length; i++)
                {
                    values[i] = FromFunctionArgument(args[i]);
                }
                result = function.Invoke(values) ?? SqliteHostBindingValue.Null();
            }
            catch (Exception ex)
            {
                // Never let the exception cross uncontrolled: rethrow with the
                // marker so Microsoft.Data.Sqlite turns it into the SQL error.
                throw new InvalidOperationException(
                    SqliteHostScalarFunction.HandlerErrorMarker + " " + ex.Message);
            }
            return ToFunctionResult(result);
        }

        private static SqliteHostBindingValue FromFunctionArgument(object value)
        {
            switch (value)
            {
                case null:
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
                    return null;
            }
        }

        /// <summary>Adapter contract: surface native failures with their SQLite result code.</summary>
        private static SqliteHostAdapterException Wrap(SqliteException ex)
            => new SqliteHostAdapterException(ex.Message, ex.SqliteErrorCode, ex);

        public void Dispose()
        {
            _connection.Dispose();
            IsDisposed = true;
        }

        private SqliteCommand CreateCommand(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            if (bindings != null)
            {
                foreach (SqliteHostBinding binding in bindings)
                {
                    // Bare names must bind every prefixed occurrence in the
                    // SQL (:name / @name / $name). Adding a bare name is
                    // ambiguous to Microsoft.Data.Sqlite when one statement
                    // mixes prefixes for the same name, so add the binding
                    // under all three prefixed names; collection parameters
                    // not present in the SQL are ignored.
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

    public sealed class MicrosoftDataSqliteRow : ISqliteHostRow
    {
        private readonly SqliteDataReader _reader;

        public MicrosoftDataSqliteRow(SqliteDataReader reader)
        {
            _reader = reader;
        }

        public bool IsNull(int index) => _reader.IsDBNull(index);
        public int GetInt32(int index) => _reader.GetInt32(index);
        public long GetInt64(int index) => _reader.GetInt64(index);
        public bool GetBool(int index) => _reader.GetInt64(index) != 0;
        public string GetText(int index) => _reader.GetString(index);
        public byte[] GetBlob(int index) => _reader.GetFieldValue<byte[]>(index);
        public float GetFloat32(int index) => _reader.GetFloat(index);
        public double GetFloat64(int index) => _reader.GetDouble(index);
    }

    public sealed class MicrosoftDataSqlitePreparedStatement : ISqliteHostPreparedStatement
    {
        private readonly sqlite3_stmt _statement;
        private readonly List<string> _parameterNames;

        public MicrosoftDataSqlitePreparedStatement(sqlite3_stmt statement)
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

    /// <summary>
    /// In-memory workspace factory. Counts OpenWorkspace calls (clean-skip
    /// assertions) and can retain the underlying workspace past the run via
    /// a no-op-dispose wrapper so tests can inspect final table contents.
    /// Carries the scalar-function capability marker: the adapter
    /// implements ISqliteHostScalarFunctionConnection.
    /// </summary>
    public sealed class TestWorkspaceFactory : ISqliteHostScalarFunctionCapableFactory, IDisposable
    {
        private readonly bool _retainWorkspace;

        public TestWorkspaceFactory(bool retainWorkspace = false)
        {
            _retainWorkspace = retainWorkspace;
        }

        public int OpenCount { get; private set; }

        /// <summary>The most recently opened adapter (disposed after the run unless retained).</summary>
        public MicrosoftDataSqliteConnection LastWorkspace { get; private set; }

        public ISqliteHostConnection OpenWorkspace()
        {
            OpenCount++;
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var adapter = new MicrosoftDataSqliteConnection(connection);
            LastWorkspace = adapter;
            return _retainWorkspace ? NonDisposingConnection.Wrap(adapter) : adapter;
        }

        public void Dispose()
        {
            if (_retainWorkspace && LastWorkspace != null && !LastWorkspace.IsDisposed)
            {
                LastWorkspace.Dispose();
            }
        }
    }
}
