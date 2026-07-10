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
    public sealed class SqliteNetConnection : ISqliteHostPrepareConnection
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
            int rc = raw.sqlite3_step(statement);
            if (rc != raw.SQLITE_DONE && rc != raw.SQLITE_ROW)
            {
                throw Error("sqlite3_step", rc);
            }
        }

        public IReadOnlyList<T> Query<T>(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, T> mapper)
        {
            using sqlite3_stmt statement = PrepareCore(sql, bindings);
            var row = new SqliteNetRow(statement);
            var results = new List<T>();
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
            return new SqliteNetPreparedStatement(PrepareCore(sql, null));
        }

        public void Dispose()
        {
            _connection.Dispose();
        }

        private sqlite3_stmt PrepareCore(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            int rc = raw.sqlite3_prepare_v2(Handle, sql, out sqlite3_stmt statement);
            if (rc != raw.SQLITE_OK)
            {
                throw Error("sqlite3_prepare_v2", rc);
            }
            if (bindings != null)
            {
                foreach (SqliteHostBinding binding in bindings)
                {
                    BindUnderAllPrefixes(statement, binding);
                }
            }
            return statement;
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

        private InvalidOperationException Error(string operation, int rc)
        {
            return new InvalidOperationException(
                operation + " failed (" + rc + "): " + raw.sqlite3_errmsg(Handle).utf8_to_string());
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

    /// <summary>In-memory workspace factory over the sqlite-net adapter.</summary>
    public sealed class SqliteNetWorkspaceFactory : ISqliteHostConnectionFactory
    {
        public ISqliteHostConnection OpenWorkspace() => SqliteNetConnection.OpenInMemory();
    }
}
