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
    public sealed class SystemDataSqliteConnection : ISqliteHostConnection
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

        public IReadOnlyList<T> Query<T>(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, T> mapper)
        {
            using var command = CreateCommand(sql, bindings);
            try
            {
                using var reader = command.ExecuteReader();
                var row = new SystemDataSqliteRow(reader);
                var results = new List<T>();
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

    /// <summary>In-memory workspace factory over the System.Data.SQLite adapter.</summary>
    public sealed class SystemDataSqliteWorkspaceFactory : ISqliteHostConnectionFactory
    {
        public ISqliteHostConnection OpenWorkspace() => SystemDataSqliteConnection.OpenInMemory();
    }
}
