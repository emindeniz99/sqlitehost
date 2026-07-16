using System;

namespace SqliteHost
{
    /// <summary>
    /// The single home of the scalar column mapping rules, in type-erased
    /// form. Classic typed registration (<see cref="ScalarFields"/>) and
    /// the compact profile both lower to these factories, so read/write and
    /// null semantics cannot drift between profiles. Every factory is
    /// non-generic: its closures cost one shared display class for the
    /// whole assembly, never per method.
    /// </summary>
    internal static class ErasedScalarFields
    {
        public static ErasedReadField Int(string sqlName, Action<object, int> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Int32, false,
                delegate(object dto, ISqliteHostRow row, int index) { setter(dto, row.GetInt32(index)); });
        }

        public static ErasedReadField Long(string sqlName, Action<object, long> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Int64, false,
                delegate(object dto, ISqliteHostRow row, int index) { setter(dto, row.GetInt64(index)); });
        }

        public static ErasedReadField Bool(string sqlName, Action<object, bool> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Boolean, false,
                delegate(object dto, ISqliteHostRow row, int index) { setter(dto, row.GetBool(index)); });
        }

        public static ErasedReadField Text(string sqlName, Action<object, string> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.String, false,
                delegate(object dto, ISqliteHostRow row, int index) { setter(dto, row.GetText(index)); });
        }

        public static ErasedReadField Blob(string sqlName, Action<object, byte[]> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Bytes, false,
                delegate(object dto, ISqliteHostRow row, int index) { setter(dto, row.GetBlob(index)); });
        }

        public static ErasedReadField Float(string sqlName, Action<object, float> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Float32, false,
                delegate(object dto, ISqliteHostRow row, int index) { setter(dto, row.GetFloat32(index)); });
        }

        public static ErasedReadField Double(string sqlName, Action<object, double> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Float64, false,
                delegate(object dto, ISqliteHostRow row, int index) { setter(dto, row.GetFloat64(index)); });
        }

        public static ErasedReadField OptionalInt(string sqlName, Action<object, int?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Int32, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (int?)null : row.GetInt32(index));
                });
        }

        public static ErasedReadField OptionalLong(string sqlName, Action<object, long?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Int64, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (long?)null : row.GetInt64(index));
                });
        }

        public static ErasedReadField OptionalBool(string sqlName, Action<object, bool?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Boolean, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (bool?)null : row.GetBool(index));
                });
        }

        public static ErasedReadField OptionalText(string sqlName, Action<object, string> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.String, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? null : row.GetText(index));
                });
        }

        public static ErasedReadField OptionalBlob(string sqlName, Action<object, byte[]> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Bytes, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? null : row.GetBlob(index));
                });
        }

        public static ErasedReadField OptionalFloat(string sqlName, Action<object, float?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Float32, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (float?)null : row.GetFloat32(index));
                });
        }

        public static ErasedReadField OptionalDouble(string sqlName, Action<object, double?> setter)
        {
            return new ErasedReadField(sqlName, HostScalarType.Float64, true,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (double?)null : row.GetFloat64(index));
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
