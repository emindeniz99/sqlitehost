using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// Test-only Microsoft.Data.Sqlite adapter (plan §31 resolved decision 7:
    /// the runtime packages stay dependency-free; the first official adapter
    /// lives in the test project).
    /// </summary>
    public sealed class MicrosoftDataSqliteConnection : ISqliteHostPrepareConnection
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
            command.ExecuteNonQuery();
        }

        public IReadOnlyList<T> Query<T>(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, T> mapper)
        {
            using var command = CreateCommand(sql, bindings);
            using var reader = command.ExecuteReader();
            var row = new MicrosoftDataSqliteRow(reader);
            var results = new List<T>();
            while (reader.Read())
            {
                results.Add(mapper(row));
            }
            return results;
        }

        public ISqliteHostPreparedStatement Prepare(string sql)
        {
            sqlite3 db = _connection.Handle
                ?? throw new InvalidOperationException("Connection must be open to prepare statements.");
            int rc = raw.sqlite3_prepare_v2(db, sql, out sqlite3_stmt statement);
            if (rc != raw.SQLITE_OK)
            {
                throw new InvalidOperationException(
                    "sqlite3_prepare_v2 failed (" + rc + "): " + raw.sqlite3_errmsg(db).utf8_to_string());
            }
            return new MicrosoftDataSqlitePreparedStatement(statement);
        }

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
    /// </summary>
    public sealed class TestWorkspaceFactory : ISqliteHostConnectionFactory, IDisposable
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
            return _retainWorkspace ? new NonDisposingConnection(adapter) : (ISqliteHostConnection)adapter;
        }

        public void Dispose()
        {
            if (_retainWorkspace && LastWorkspace != null && !LastWorkspace.IsDisposed)
            {
                LastWorkspace.Dispose();
            }
        }
    }

    /// <summary>Caller-owned connection wrapper whose Dispose is a no-op.</summary>
    public sealed class NonDisposingConnection : ISqliteHostConnection
    {
        private readonly ISqliteHostConnection _inner;

        public NonDisposingConnection(ISqliteHostConnection inner)
        {
            _inner = inner;
        }

        public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
            => _inner.Execute(sql, bindings);

        public IReadOnlyList<T> Query<T>(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, T> mapper)
            => _inner.Query(sql, bindings, mapper);

        public void Dispose()
        {
            // Intentionally left open so tests can inspect the workspace.
        }
    }
}
