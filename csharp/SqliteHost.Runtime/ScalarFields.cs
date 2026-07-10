using System;

namespace SqliteHost
{
    /// <summary>Shared factories for scalar read/write fields (one place for type mapping rules).</summary>
    internal static class ScalarFields
    {
        public static ScalarReadField<T> Int<T>(string sqlName, Action<T, int> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Int32, false,
                delegate(T dto, ISqliteHostRow row, int index) { setter(dto, row.GetInt32(index)); });
        }

        public static ScalarReadField<T> Long<T>(string sqlName, Action<T, long> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Int64, false,
                delegate(T dto, ISqliteHostRow row, int index) { setter(dto, row.GetInt64(index)); });
        }

        public static ScalarReadField<T> Bool<T>(string sqlName, Action<T, bool> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Boolean, false,
                delegate(T dto, ISqliteHostRow row, int index) { setter(dto, row.GetBool(index)); });
        }

        public static ScalarReadField<T> Text<T>(string sqlName, Action<T, string> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.String, false,
                delegate(T dto, ISqliteHostRow row, int index) { setter(dto, row.GetText(index)); });
        }

        public static ScalarReadField<T> Blob<T>(string sqlName, Action<T, byte[]> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Bytes, false,
                delegate(T dto, ISqliteHostRow row, int index) { setter(dto, row.GetBlob(index)); });
        }

        public static ScalarReadField<T> Float<T>(string sqlName, Action<T, float> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Float32, false,
                delegate(T dto, ISqliteHostRow row, int index) { setter(dto, row.GetFloat32(index)); });
        }

        public static ScalarReadField<T> Double<T>(string sqlName, Action<T, double> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Float64, false,
                delegate(T dto, ISqliteHostRow row, int index) { setter(dto, row.GetFloat64(index)); });
        }

        public static ScalarReadField<T> OptionalInt<T>(string sqlName, Action<T, int?> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Int32, true,
                delegate(T dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (int?)null : row.GetInt32(index));
                });
        }

        public static ScalarReadField<T> OptionalLong<T>(string sqlName, Action<T, long?> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Int64, true,
                delegate(T dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (long?)null : row.GetInt64(index));
                });
        }

        public static ScalarReadField<T> OptionalBool<T>(string sqlName, Action<T, bool?> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Boolean, true,
                delegate(T dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (bool?)null : row.GetBool(index));
                });
        }

        public static ScalarReadField<T> OptionalText<T>(string sqlName, Action<T, string> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.String, true,
                delegate(T dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? null : row.GetText(index));
                });
        }

        public static ScalarReadField<T> OptionalBlob<T>(string sqlName, Action<T, byte[]> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Bytes, true,
                delegate(T dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? null : row.GetBlob(index));
                });
        }

        public static ScalarReadField<T> OptionalFloat<T>(string sqlName, Action<T, float?> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Float32, true,
                delegate(T dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (float?)null : row.GetFloat32(index));
                });
        }

        public static ScalarReadField<T> OptionalDouble<T>(string sqlName, Action<T, double?> setter)
        {
            return new ScalarReadField<T>(sqlName, HostScalarType.Float64, true,
                delegate(T dto, ISqliteHostRow row, int index)
                {
                    setter(dto, row.IsNull(index) ? (double?)null : row.GetFloat64(index));
                });
        }

        public static ScalarWriteField<T> WriteInt<T>(string sqlName, Func<T, int> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Int32, false,
                delegate(T value) { return SqliteHostBindingValue.Int32(getter(value)); });
        }

        public static ScalarWriteField<T> WriteLong<T>(string sqlName, Func<T, long> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Int64, false,
                delegate(T value) { return SqliteHostBindingValue.Int64(getter(value)); });
        }

        public static ScalarWriteField<T> WriteBool<T>(string sqlName, Func<T, bool> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Boolean, false,
                delegate(T value) { return SqliteHostBindingValue.Bool(getter(value)); });
        }

        public static ScalarWriteField<T> WriteText<T>(string sqlName, Func<T, string> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.String, false,
                delegate(T value)
                {
                    string text = getter(value);
                    return text == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Text(text);
                });
        }

        public static ScalarWriteField<T> WriteBlob<T>(string sqlName, Func<T, byte[]> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Bytes, false,
                delegate(T value)
                {
                    byte[] blob = getter(value);
                    return blob == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Blob(blob);
                });
        }

        public static ScalarWriteField<T> WriteFloat<T>(string sqlName, Func<T, float> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Float32, false,
                delegate(T value) { return SqliteHostBindingValue.Float32(getter(value)); });
        }

        public static ScalarWriteField<T> WriteDouble<T>(string sqlName, Func<T, double> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Float64, false,
                delegate(T value) { return SqliteHostBindingValue.Float64(getter(value)); });
        }

        public static ScalarWriteField<T> WriteOptionalInt<T>(string sqlName, Func<T, int?> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Int32, true,
                delegate(T value)
                {
                    int? number = getter(value);
                    return number.HasValue
                        ? SqliteHostBindingValue.Int32(number.Value)
                        : SqliteHostBindingValue.Null();
                });
        }

        public static ScalarWriteField<T> WriteOptionalLong<T>(string sqlName, Func<T, long?> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Int64, true,
                delegate(T value)
                {
                    long? number = getter(value);
                    return number.HasValue
                        ? SqliteHostBindingValue.Int64(number.Value)
                        : SqliteHostBindingValue.Null();
                });
        }

        public static ScalarWriteField<T> WriteOptionalBool<T>(string sqlName, Func<T, bool?> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Boolean, true,
                delegate(T value)
                {
                    bool? flag = getter(value);
                    return flag.HasValue
                        ? SqliteHostBindingValue.Bool(flag.Value)
                        : SqliteHostBindingValue.Null();
                });
        }

        public static ScalarWriteField<T> WriteOptionalText<T>(string sqlName, Func<T, string> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.String, true,
                delegate(T value)
                {
                    string text = getter(value);
                    return text == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Text(text);
                });
        }

        public static ScalarWriteField<T> WriteOptionalBlob<T>(string sqlName, Func<T, byte[]> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Bytes, true,
                delegate(T value)
                {
                    byte[] blob = getter(value);
                    return blob == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Blob(blob);
                });
        }

        public static ScalarWriteField<T> WriteOptionalFloat<T>(string sqlName, Func<T, float?> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Float32, true,
                delegate(T value)
                {
                    float? number = getter(value);
                    return number.HasValue
                        ? SqliteHostBindingValue.Float32(number.Value)
                        : SqliteHostBindingValue.Null();
                });
        }

        public static ScalarWriteField<T> WriteOptionalDouble<T>(string sqlName, Func<T, double?> getter)
        {
            return new ScalarWriteField<T>(sqlName, HostScalarType.Float64, true,
                delegate(T value)
                {
                    double? number = getter(value);
                    return number.HasValue
                        ? SqliteHostBindingValue.Float64(number.Value)
                        : SqliteHostBindingValue.Null();
                });
        }
    }
}
