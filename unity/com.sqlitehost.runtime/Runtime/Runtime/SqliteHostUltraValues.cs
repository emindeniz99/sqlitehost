using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>Receives column values while the runtime materializes ultra inputs.</summary>
    internal interface IUltraValueSink
    {
        void Store(string fieldName, SqliteHostBindingValue value);
    }

    /// <summary>Yields declared field values while the runtime writes ultra results.</summary>
    internal interface IUltraValueSource
    {
        SqliteHostBindingValue ReadStored(string fieldName, bool optional);
    }

    /// <summary>
    /// One row of ultra-profile values, keyed by field name. Used on both
    /// sides: the runtime fills it for input list rows (read it with
    /// <c>IsNull</c>/<c>Get*</c>), and handlers fill it for result list
    /// rows via <see cref="SqliteHostUltraResult.AddRow"/> (write it with
    /// <c>Set*</c>). Values are typed; reads are strict — a field declared
    /// int64 must be read with <see cref="GetInt64"/>.
    /// </summary>
    public sealed class SqliteHostUltraRow : IUltraValueSink, IUltraValueSource
    {
        private readonly Dictionary<string, SqliteHostBindingValue> _values =
            new Dictionary<string, SqliteHostBindingValue>(StringComparer.Ordinal);

        public bool IsNull(string fieldName)
        {
            return Fetch(fieldName).Type == SqliteHostBindingType.Null;
        }

        public int GetInt32(string fieldName)
        {
            return Typed(fieldName, SqliteHostBindingType.Int32).Int32Value;
        }

        public long GetInt64(string fieldName)
        {
            return Typed(fieldName, SqliteHostBindingType.Int64).Int64Value;
        }

        public bool GetBool(string fieldName)
        {
            return Typed(fieldName, SqliteHostBindingType.Bool).BoolValue;
        }

        public string GetText(string fieldName)
        {
            return Typed(fieldName, SqliteHostBindingType.Text).TextValue;
        }

        public byte[] GetBlob(string fieldName)
        {
            return Typed(fieldName, SqliteHostBindingType.Blob).BlobValue;
        }

        public float GetFloat32(string fieldName)
        {
            return Typed(fieldName, SqliteHostBindingType.Float32).Float32Value;
        }

        public double GetFloat64(string fieldName)
        {
            return Typed(fieldName, SqliteHostBindingType.Float64).Float64Value;
        }

        public SqliteHostUltraRow SetInt32(string fieldName, int value)
        {
            _values[fieldName] = SqliteHostBindingValue.Int32(value);
            return this;
        }

        public SqliteHostUltraRow SetInt64(string fieldName, long value)
        {
            _values[fieldName] = SqliteHostBindingValue.Int64(value);
            return this;
        }

        public SqliteHostUltraRow SetBool(string fieldName, bool value)
        {
            _values[fieldName] = SqliteHostBindingValue.Bool(value);
            return this;
        }

        public SqliteHostUltraRow SetText(string fieldName, string value)
        {
            _values[fieldName] = value == null
                ? SqliteHostBindingValue.Null()
                : SqliteHostBindingValue.Text(value);
            return this;
        }

        public SqliteHostUltraRow SetBlob(string fieldName, byte[] value)
        {
            _values[fieldName] = value == null
                ? SqliteHostBindingValue.Null()
                : SqliteHostBindingValue.Blob(value);
            return this;
        }

        public SqliteHostUltraRow SetFloat32(string fieldName, float value)
        {
            _values[fieldName] = SqliteHostBindingValue.Float32(value);
            return this;
        }

        public SqliteHostUltraRow SetFloat64(string fieldName, double value)
        {
            _values[fieldName] = SqliteHostBindingValue.Float64(value);
            return this;
        }

        public SqliteHostUltraRow SetNull(string fieldName)
        {
            _values[fieldName] = SqliteHostBindingValue.Null();
            return this;
        }

        void IUltraValueSink.Store(string fieldName, SqliteHostBindingValue value)
        {
            _values[fieldName] = value;
        }

        SqliteHostBindingValue IUltraValueSource.ReadStored(string fieldName, bool optional)
        {
            SqliteHostBindingValue value;
            if (_values.TryGetValue(fieldName, out value))
            {
                return value;
            }
            if (optional)
            {
                return SqliteHostBindingValue.Null();
            }
            throw new InvalidOperationException(
                "Result field '" + fieldName + "' was not set.");
        }

        internal IEnumerable<KeyValuePair<string, SqliteHostBindingValue>> StoredValues
        {
            get { return _values; }
        }

        internal bool TryGetStored(string fieldName, out SqliteHostBindingValue value)
        {
            return _values.TryGetValue(fieldName, out value);
        }

        private SqliteHostBindingValue Fetch(string fieldName)
        {
            SqliteHostBindingValue value;
            if (!_values.TryGetValue(fieldName, out value))
            {
                throw new InvalidOperationException(
                    "Field '" + fieldName + "' is not present on this row"
                    + " (undeclared input field or unset result field).");
            }
            return value;
        }

        private SqliteHostBindingValue Typed(string fieldName, SqliteHostBindingType expected)
        {
            SqliteHostBindingValue value = Fetch(fieldName);
            if (value.Type == SqliteHostBindingType.Null)
            {
                throw new InvalidOperationException(
                    "Field '" + fieldName + "' is NULL; check IsNull(...) before reading it.");
            }
            if (value.Type != expected)
            {
                throw new InvalidOperationException(
                    "Field '" + fieldName + "' holds a " + value.Type
                    + " value and cannot be read as " + expected + ".");
            }
            return value;
        }
    }

    /// <summary>
    /// The input of one ultra-profile handler invocation: the parent call
    /// row's fields plus the declared input lists. Declared-but-NULL
    /// fields answer <see cref="IsNull"/> true; undeclared names fail
    /// loud.
    /// </summary>
    public sealed class SqliteHostUltraCall : IUltraValueSink
    {
        private readonly SqliteHostUltraRow _row = new SqliteHostUltraRow();
        private readonly Dictionary<string, IReadOnlyList<SqliteHostUltraRow>> _lists =
            new Dictionary<string, IReadOnlyList<SqliteHostUltraRow>>(StringComparer.Ordinal);

        internal SqliteHostUltraCall(
            IReadOnlyList<string> fieldNames,
            IReadOnlyList<string> listNames)
        {
            IUltraValueSink sink = _row;
            foreach (string fieldName in fieldNames)
            {
                sink.Store(fieldName, SqliteHostBindingValue.Null());
            }
            foreach (string listName in listNames)
            {
                _lists[listName] = new List<SqliteHostUltraRow>();
            }
        }

        public bool IsNull(string fieldName)
        {
            return _row.IsNull(fieldName);
        }

        public int GetInt32(string fieldName)
        {
            return _row.GetInt32(fieldName);
        }

        public long GetInt64(string fieldName)
        {
            return _row.GetInt64(fieldName);
        }

        public bool GetBool(string fieldName)
        {
            return _row.GetBool(fieldName);
        }

        public string GetText(string fieldName)
        {
            return _row.GetText(fieldName);
        }

        public byte[] GetBlob(string fieldName)
        {
            return _row.GetBlob(fieldName);
        }

        public float GetFloat32(string fieldName)
        {
            return _row.GetFloat32(fieldName);
        }

        public double GetFloat64(string fieldName)
        {
            return _row.GetFloat64(fieldName);
        }

        /// <summary>The ordered rows of a declared input list (empty when the script queued none).</summary>
        public IReadOnlyList<SqliteHostUltraRow> GetList(string listName)
        {
            IReadOnlyList<SqliteHostUltraRow> rows;
            if (!_lists.TryGetValue(listName, out rows))
            {
                throw new InvalidOperationException(
                    "'" + listName + "' is not a declared input list of this method.");
            }
            return rows;
        }

        void IUltraValueSink.Store(string fieldName, SqliteHostBindingValue value)
        {
            ((IUltraValueSink)_row).Store(fieldName, value);
        }

        internal void AssignList(string listName, IReadOnlyList<SqliteHostUltraRow> rows)
        {
            _lists[listName] = rows;
        }
    }

    /// <summary>
    /// The result of one ultra-profile handler invocation. Set every
    /// declared result field (explicitly, with <c>Set*</c> or
    /// <see cref="SetNull"/> where the contract allows NULL) and add one
    /// row per result-list item; the runtime validates the shape against
    /// the declaration after the handler returns and fails loud on unset,
    /// mistyped, or undeclared fields.
    /// </summary>
    public sealed class SqliteHostUltraResult : IUltraValueSource
    {
        private readonly SqliteHostUltraRow _row = new SqliteHostUltraRow();
        private readonly Dictionary<string, List<SqliteHostUltraRow>> _lists =
            new Dictionary<string, List<SqliteHostUltraRow>>(StringComparer.Ordinal);

        public SqliteHostUltraResult SetInt32(string fieldName, int value)
        {
            _row.SetInt32(fieldName, value);
            return this;
        }

        public SqliteHostUltraResult SetInt64(string fieldName, long value)
        {
            _row.SetInt64(fieldName, value);
            return this;
        }

        public SqliteHostUltraResult SetBool(string fieldName, bool value)
        {
            _row.SetBool(fieldName, value);
            return this;
        }

        public SqliteHostUltraResult SetText(string fieldName, string value)
        {
            _row.SetText(fieldName, value);
            return this;
        }

        public SqliteHostUltraResult SetBlob(string fieldName, byte[] value)
        {
            _row.SetBlob(fieldName, value);
            return this;
        }

        public SqliteHostUltraResult SetFloat32(string fieldName, float value)
        {
            _row.SetFloat32(fieldName, value);
            return this;
        }

        public SqliteHostUltraResult SetFloat64(string fieldName, double value)
        {
            _row.SetFloat64(fieldName, value);
            return this;
        }

        public SqliteHostUltraResult SetNull(string fieldName)
        {
            _row.SetNull(fieldName);
            return this;
        }

        /// <summary>Appends one item row to a declared result list and returns it for its Set* calls.</summary>
        public SqliteHostUltraRow AddRow(string listName)
        {
            List<SqliteHostUltraRow> rows;
            if (!_lists.TryGetValue(listName, out rows))
            {
                rows = new List<SqliteHostUltraRow>();
                _lists[listName] = rows;
            }
            var row = new SqliteHostUltraRow();
            rows.Add(row);
            return row;
        }

        SqliteHostBindingValue IUltraValueSource.ReadStored(string fieldName, bool optional)
        {
            return ((IUltraValueSource)_row).ReadStored(fieldName, optional);
        }

        internal SqliteHostUltraRow ParentRow
        {
            get { return _row; }
        }

        internal IEnumerable<KeyValuePair<string, List<SqliteHostUltraRow>>> Lists
        {
            get { return _lists; }
        }

        internal IReadOnlyList<object> BoxedRows(string listName)
        {
            List<SqliteHostUltraRow> rows;
            if (!_lists.TryGetValue(listName, out rows))
            {
                return null;
            }
            var boxed = new List<object>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                boxed.Add(rows[i]);
            }
            return boxed;
        }
    }
}
