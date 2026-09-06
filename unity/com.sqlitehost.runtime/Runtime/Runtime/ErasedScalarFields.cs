using System;

namespace SqliteHost
{
    /// <summary>
    /// The single home of the scalar column mapping rules, in type-erased
    /// form. Classic typed registration (<see cref="ScalarFields"/>) and
    /// the compact profile both lower to these factories, and the ultra
    /// profile's own column read (<see cref="UltraFields"/>) delegates to
    /// <see cref="ReadColumn"/> here, so read/write and null semantics
    /// cannot drift between profiles. Every factory is non-generic: its
    /// closures cost one shared display class for the whole assembly,
    /// never per method.
    /// </summary>
    internal static class ErasedScalarFields
    {
        /// <summary>
        /// Reads one declared column as a binding value — the ultra
        /// profile's entry into the same mapping the typed factories below
        /// use, so a REAL column means the same thing in all three
        /// profiles. Non-finite doubles reach the caller raw: the
        /// finiteness rule belongs to the JSON envelope, not to a value the
        /// engine just handed back (SqliteHostBindingValue.Float64FromColumn).
        /// </summary>
        public static SqliteHostBindingValue ReadColumn(
            ISqliteHostRow row,
            int index,
            HostScalarType scalarType,
            bool optional,
            string sqlName)
        {
            if (optional && row.IsNull(index))
            {
                return SqliteHostBindingValue.Null();
            }
            RequireStorageClass(row, index, scalarType, sqlName);
            switch (scalarType)
            {
                case HostScalarType.Int32:
                    return SqliteHostBindingValue.Int32(ReadInt32(row, index, sqlName));
                case HostScalarType.Int64:
                    return SqliteHostBindingValue.Int64(row.GetInt64(index));
                case HostScalarType.Boolean:
                    return SqliteHostBindingValue.Bool(row.GetBool(index));
                case HostScalarType.String:
                {
                    string text = row.GetText(index);
                    return text == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Text(text);
                }
                case HostScalarType.Bytes:
                {
                    byte[] blob = row.GetBlob(index);
                    return blob == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Blob(blob);
                }
                case HostScalarType.Float32:
                    return SqliteHostBindingValue.Float32FromColumn(row.GetFloat32(index));
                default:
                    return SqliteHostBindingValue.Float64FromColumn(row.GetFloat64(index));
            }
        }

        /// <summary>
        /// Refuses a column whose stored value contradicts the declared
        /// scalar type, before any typed getter can coerce it.
        ///
        /// SQLite affinity is a hint: it converts a value only when the
        /// conversion is lossless, so the text <c>'1,000'</c> stays TEXT in
        /// an INTEGER-declared call column and <c>GetInt64</c> would report
        /// <c>1</c> — a corrupted argument the run reports as Completed.
        /// The declared type is already on the read path, so the check costs
        /// one <c>sqlite3_column_type</c> per column read and stays in every
        /// build: this is data loss, not a strict authoring check, the same
        /// reasoning that keeps the undeclared-result-list gate outside
        /// SQLITEHOST_SLIM.
        ///
        /// NULL is deliberately not this guard's business — the typed
        /// getters' own NULL contract (docs/adapter-contract.md, "Value
        /// fidelity") already refuses it, and optional fields ask
        /// <c>IsNull</c> first.
        /// </summary>
        public static void RequireStorageClass(
            ISqliteHostRow row, int index, HostScalarType scalarType, string sqlName)
        {
            SqliteHostStorageClass actual = row.GetStorageClass(index);
            if (actual == SqliteHostStorageClass.Null || Accepts(scalarType, actual))
            {
                return;
            }
            throw new SqliteHostInputTypeMismatchException(
                "Column '" + sqlName + "' is declared " + Describe(scalarType)
                + " but the stored value is " + Describe(actual)
                + "; SQLite affinity does not convert it, so reading it as "
                + Describe(scalarType) + " would silently change the value.");
        }

        /// <summary>
        /// An int32 field read out of an INTEGER column, which holds any
        /// int64. The value is read as int64 and range-checked here rather
        /// than through GetInt32, so the failure is the runtime's
        /// input-type-mismatch (naming the column) on every adapter instead
        /// of whatever the wrapper happens to do — the shipped adapters now
        /// refuse too, but an out-of-range value is a data problem in the
        /// workspace, not an adapter fault.
        /// </summary>
        public static int ReadInt32(ISqliteHostRow row, int index, string sqlName)
        {
            long value = row.GetInt64(index);
            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new SqliteHostInputTypeMismatchException(
                    "Column '" + sqlName + "' is declared int32 but holds " + value
                    + ", which is outside the int32 range; reading it as int32 would"
                    + " keep only its low 32 bits.");
            }
            return (int)value;
        }

        private static bool Accepts(HostScalarType scalarType, SqliteHostStorageClass actual)
        {
            switch (scalarType)
            {
                case HostScalarType.Int32:
                case HostScalarType.Int64:
                case HostScalarType.Boolean:
                    return actual == SqliteHostStorageClass.Integer;
                case HostScalarType.String:
                    return actual == SqliteHostStorageClass.Text;
                case HostScalarType.Bytes:
                    return actual == SqliteHostStorageClass.Blob;
                default:
                    // REAL columns hold whole numbers as INTEGER, so a float
                    // field accepts both without losing anything.
                    return actual == SqliteHostStorageClass.Real
                        || actual == SqliteHostStorageClass.Integer;
            }
        }

        /// <summary>The declared type as docs/workspace-schema.md spells it.</summary>
        private static string Describe(HostScalarType scalarType)
        {
            switch (scalarType)
            {
                case HostScalarType.Int32:
                    return "int32";
                case HostScalarType.Int64:
                    return "int64";
                case HostScalarType.Boolean:
                    return "bool";
                case HostScalarType.String:
                    return "text";
                case HostScalarType.Bytes:
                    return "blob";
                case HostScalarType.Float32:
                    return "float32";
                default:
                    return "float64";
            }
        }

        /// <summary>The storage class as SQLite's own typeof() spells it.</summary>
        private static string Describe(SqliteHostStorageClass storageClass)
        {
            switch (storageClass)
            {
                case SqliteHostStorageClass.Integer:
                    return "integer";
                case SqliteHostStorageClass.Real:
                    return "real";
                case SqliteHostStorageClass.Text:
                    return "text";
                case SqliteHostStorageClass.Blob:
                    return "blob";
                default:
                    return "null";
            }
        }

        public static ErasedReadField Int(string sqlName, Action<object, int> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Int32, false,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    RequireStorageClass(row, index, HostScalarType.Int32, sqlName);
                    setter(dto, ReadInt32(row, index, sqlName));
                });
        }

        public static ErasedReadField Long(string sqlName, Action<object, long> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Int64, false,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    RequireStorageClass(row, index, HostScalarType.Int64, sqlName);
                    setter(dto, row.GetInt64(index));
                });
        }

        public static ErasedReadField Bool(string sqlName, Action<object, bool> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Boolean, false,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    RequireStorageClass(row, index, HostScalarType.Boolean, sqlName);
                    setter(dto, row.GetBool(index));
                });
        }

        public static ErasedReadField Text(string sqlName, Action<object, string> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.String, false,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    RequireStorageClass(row, index, HostScalarType.String, sqlName);
                    setter(dto, row.GetText(index));
                });
        }

        public static ErasedReadField Blob(string sqlName, Action<object, byte[]> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Bytes, false,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    RequireStorageClass(row, index, HostScalarType.Bytes, sqlName);
                    setter(dto, row.GetBlob(index));
                });
        }

        public static ErasedReadField Float(string sqlName, Action<object, float> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Float32, false,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    RequireStorageClass(row, index, HostScalarType.Float32, sqlName);
                    setter(dto, row.GetFloat32(index));
                });
        }

        public static ErasedReadField Double(string sqlName, Action<object, double> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Float64, false,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    RequireStorageClass(row, index, HostScalarType.Float64, sqlName);
                    setter(dto, row.GetFloat64(index));
                });
        }

        public static ErasedReadField OptionalInt(string sqlName, Action<object, int?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Int32, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    if (row.IsNull(index))
                    {
                        setter(dto, (int?)null);
                        return;
                    }
                    RequireStorageClass(row, index, HostScalarType.Int32, sqlName);
                    setter(dto, ReadInt32(row, index, sqlName));
                });
        }

        public static ErasedReadField OptionalLong(string sqlName, Action<object, long?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Int64, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    if (row.IsNull(index))
                    {
                        setter(dto, (long?)null);
                        return;
                    }
                    RequireStorageClass(row, index, HostScalarType.Int64, sqlName);
                    setter(dto, row.GetInt64(index));
                });
        }

        public static ErasedReadField OptionalBool(string sqlName, Action<object, bool?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Boolean, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    if (row.IsNull(index))
                    {
                        setter(dto, (bool?)null);
                        return;
                    }
                    RequireStorageClass(row, index, HostScalarType.Boolean, sqlName);
                    setter(dto, row.GetBool(index));
                });
        }

        public static ErasedReadField OptionalText(string sqlName, Action<object, string> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.String, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    if (row.IsNull(index))
                    {
                        setter(dto, null);
                        return;
                    }
                    RequireStorageClass(row, index, HostScalarType.String, sqlName);
                    setter(dto, row.GetText(index));
                });
        }

        public static ErasedReadField OptionalBlob(string sqlName, Action<object, byte[]> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Bytes, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    if (row.IsNull(index))
                    {
                        setter(dto, null);
                        return;
                    }
                    RequireStorageClass(row, index, HostScalarType.Bytes, sqlName);
                    setter(dto, row.GetBlob(index));
                });
        }

        public static ErasedReadField OptionalFloat(string sqlName, Action<object, float?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Float32, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    if (row.IsNull(index))
                    {
                        setter(dto, (float?)null);
                        return;
                    }
                    RequireStorageClass(row, index, HostScalarType.Float32, sqlName);
                    setter(dto, row.GetFloat32(index));
                });
        }

        public static ErasedReadField OptionalDouble(string sqlName, Action<object, double?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Float64, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    if (row.IsNull(index))
                    {
                        setter(dto, (double?)null);
                        return;
                    }
                    RequireStorageClass(row, index, HostScalarType.Float64, sqlName);
                    setter(dto, row.GetFloat64(index));
                });
        }

        public static ErasedWriteField WriteInt(string sqlName, Func<object, int> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Int32, false,
                delegate(object value) { return SqliteHostBindingValue.Int32(getter(value)); });
        }

        public static ErasedWriteField WriteLong(string sqlName, Func<object, long> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Int64, false,
                delegate(object value) { return SqliteHostBindingValue.Int64(getter(value)); });
        }

        public static ErasedWriteField WriteBool(string sqlName, Func<object, bool> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Boolean, false,
                delegate(object value) { return SqliteHostBindingValue.Bool(getter(value)); });
        }

        public static ErasedWriteField WriteText(string sqlName, Func<object, string> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.String, false,
                delegate(object value)
                {
                    string text = getter(value);
                    return text == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Text(text);
                });
        }

        public static ErasedWriteField WriteBlob(string sqlName, Func<object, byte[]> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Bytes, false,
                delegate(object value)
                {
                    byte[] blob = getter(value);
                    return blob == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Blob(blob);
                });
        }

        public static ErasedWriteField WriteFloat(string sqlName, Func<object, float> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Float32, false,
                delegate(object value) { return SqliteHostBindingValue.Float32(getter(value)); });
        }

        public static ErasedWriteField WriteDouble(string sqlName, Func<object, double> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Float64, false,
                delegate(object value) { return SqliteHostBindingValue.Float64(getter(value)); });
        }

        public static ErasedWriteField WriteOptionalInt(string sqlName, Func<object, int?> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Int32, true,
                delegate(object value)
                {
                    int? number = getter(value);
                    return number.HasValue
                        ? SqliteHostBindingValue.Int32(number.Value)
                        : SqliteHostBindingValue.Null();
                });
        }

        public static ErasedWriteField WriteOptionalLong(string sqlName, Func<object, long?> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Int64, true,
                delegate(object value)
                {
                    long? number = getter(value);
                    return number.HasValue
                        ? SqliteHostBindingValue.Int64(number.Value)
                        : SqliteHostBindingValue.Null();
                });
        }

        public static ErasedWriteField WriteOptionalBool(string sqlName, Func<object, bool?> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Boolean, true,
                delegate(object value)
                {
                    bool? flag = getter(value);
                    return flag.HasValue
                        ? SqliteHostBindingValue.Bool(flag.Value)
                        : SqliteHostBindingValue.Null();
                });
        }

        public static ErasedWriteField WriteOptionalText(string sqlName, Func<object, string> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.String, true,
                delegate(object value)
                {
                    string text = getter(value);
                    return text == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Text(text);
                });
        }

        public static ErasedWriteField WriteOptionalBlob(string sqlName, Func<object, byte[]> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Bytes, true,
                delegate(object value)
                {
                    byte[] blob = getter(value);
                    return blob == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Blob(blob);
                });
        }

        public static ErasedWriteField WriteOptionalFloat(string sqlName, Func<object, float?> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Float32, true,
                delegate(object value)
                {
                    float? number = getter(value);
                    return number.HasValue
                        ? SqliteHostBindingValue.Float32(number.Value)
                        : SqliteHostBindingValue.Null();
                });
        }

        public static ErasedWriteField WriteOptionalDouble(string sqlName, Func<object, double?> getter)
        {
            return new ErasedWriteField(sqlName, HostScalarType.Float64, true,
                delegate(object value)
                {
                    double? number = getter(value);
                    return number.HasValue
                        ? SqliteHostBindingValue.Float64(number.Value)
                        : SqliteHostBindingValue.Null();
                });
        }
    }
}
