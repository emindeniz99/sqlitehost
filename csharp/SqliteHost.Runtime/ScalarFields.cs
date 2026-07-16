using System;

namespace SqliteHost
{
    /// <summary>
    /// Typed wrappers for the classic fluent API: each factory erases the
    /// DTO type and lowers to <see cref="ErasedScalarFields"/>, which owns
    /// the actual read/write and null-handling rules. The boxed-DTO cast is
    /// safe because the spec builders reject value-type DTOs fail-loud.
    /// </summary>
    internal static class ScalarFields
    {
        public static ErasedReadField Int<T>(string sqlName, Action<T, int> setter)
        {
            return ErasedScalarFields.Int(sqlName,
                delegate(object dto, int value) { setter((T)dto, value); });
        }

        public static ErasedReadField Long<T>(string sqlName, Action<T, long> setter)
        {
            return ErasedScalarFields.Long(sqlName,
                delegate(object dto, long value) { setter((T)dto, value); });
        }

        public static ErasedReadField Bool<T>(string sqlName, Action<T, bool> setter)
        {
            return ErasedScalarFields.Bool(sqlName,
                delegate(object dto, bool value) { setter((T)dto, value); });
        }

        public static ErasedReadField Text<T>(string sqlName, Action<T, string> setter)
        {
            return ErasedScalarFields.Text(sqlName,
                delegate(object dto, string value) { setter((T)dto, value); });
        }

        public static ErasedReadField Blob<T>(string sqlName, Action<T, byte[]> setter)
        {
            return ErasedScalarFields.Blob(sqlName,
                delegate(object dto, byte[] value) { setter((T)dto, value); });
        }

        public static ErasedReadField Float<T>(string sqlName, Action<T, float> setter)
        {
            return ErasedScalarFields.Float(sqlName,
                delegate(object dto, float value) { setter((T)dto, value); });
        }

        public static ErasedReadField Double<T>(string sqlName, Action<T, double> setter)
        {
            return ErasedScalarFields.Double(sqlName,
                delegate(object dto, double value) { setter((T)dto, value); });
        }

        public static ErasedReadField OptionalInt<T>(string sqlName, Action<T, int?> setter)
        {
            return ErasedScalarFields.OptionalInt(sqlName,
                delegate(object dto, int? value) { setter((T)dto, value); });
        }

        public static ErasedReadField OptionalLong<T>(string sqlName, Action<T, long?> setter)
        {
            return ErasedScalarFields.OptionalLong(sqlName,
                delegate(object dto, long? value) { setter((T)dto, value); });
        }

        public static ErasedReadField OptionalBool<T>(string sqlName, Action<T, bool?> setter)
        {
            return ErasedScalarFields.OptionalBool(sqlName,
                delegate(object dto, bool? value) { setter((T)dto, value); });
        }

        public static ErasedReadField OptionalText<T>(string sqlName, Action<T, string> setter)
        {
            return ErasedScalarFields.OptionalText(sqlName,
                delegate(object dto, string value) { setter((T)dto, value); });
        }

        public static ErasedReadField OptionalBlob<T>(string sqlName, Action<T, byte[]> setter)
        {
            return ErasedScalarFields.OptionalBlob(sqlName,
                delegate(object dto, byte[] value) { setter((T)dto, value); });
        }

        public static ErasedReadField OptionalFloat<T>(string sqlName, Action<T, float?> setter)
        {
            return ErasedScalarFields.OptionalFloat(sqlName,
                delegate(object dto, float? value) { setter((T)dto, value); });
        }

        public static ErasedReadField OptionalDouble<T>(string sqlName, Action<T, double?> setter)
        {
            return ErasedScalarFields.OptionalDouble(sqlName,
                delegate(object dto, double? value) { setter((T)dto, value); });
        }

        public static ErasedWriteField WriteInt<T>(string sqlName, Func<T, int> getter)
        {
            return ErasedScalarFields.WriteInt(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteLong<T>(string sqlName, Func<T, long> getter)
        {
            return ErasedScalarFields.WriteLong(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteBool<T>(string sqlName, Func<T, bool> getter)
        {
            return ErasedScalarFields.WriteBool(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteText<T>(string sqlName, Func<T, string> getter)
        {
            return ErasedScalarFields.WriteText(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteBlob<T>(string sqlName, Func<T, byte[]> getter)
        {
            return ErasedScalarFields.WriteBlob(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteFloat<T>(string sqlName, Func<T, float> getter)
        {
            return ErasedScalarFields.WriteFloat(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteDouble<T>(string sqlName, Func<T, double> getter)
        {
            return ErasedScalarFields.WriteDouble(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteOptionalInt<T>(string sqlName, Func<T, int?> getter)
        {
            return ErasedScalarFields.WriteOptionalInt(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteOptionalLong<T>(string sqlName, Func<T, long?> getter)
        {
            return ErasedScalarFields.WriteOptionalLong(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteOptionalBool<T>(string sqlName, Func<T, bool?> getter)
        {
            return ErasedScalarFields.WriteOptionalBool(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteOptionalText<T>(string sqlName, Func<T, string> getter)
        {
            return ErasedScalarFields.WriteOptionalText(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteOptionalBlob<T>(string sqlName, Func<T, byte[]> getter)
        {
            return ErasedScalarFields.WriteOptionalBlob(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteOptionalFloat<T>(string sqlName, Func<T, float?> getter)
        {
            return ErasedScalarFields.WriteOptionalFloat(sqlName,
                delegate(object value) { return getter((T)value); });
        }

        public static ErasedWriteField WriteOptionalDouble<T>(string sqlName, Func<T, double?> getter)
        {
            return ErasedScalarFields.WriteOptionalDouble(sqlName,
                delegate(object value) { return getter((T)value); });
        }
    }
}
