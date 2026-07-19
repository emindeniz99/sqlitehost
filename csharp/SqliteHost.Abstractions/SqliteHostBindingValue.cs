using System;

namespace SqliteHost
{
    /// <summary>
    /// Manual discriminated representation of a typed binding value
    /// (type discriminator + typed accessors; no language union so the
    /// class stays Unity-2021-safe).
    /// </summary>
    public sealed class SqliteHostBindingValue
    {
        private SqliteHostBindingValue(
            SqliteHostBindingType type,
            int int32Value,
            long int64Value,
            bool boolValue,
            string textValue,
            byte[] blobValue,
            float float32Value,
            double float64Value)
        {
            Type = type;
            Int32Value = int32Value;
            Int64Value = int64Value;
            BoolValue = boolValue;
            TextValue = textValue;
            BlobValue = blobValue;
            Float32Value = float32Value;
            Float64Value = float64Value;
        }

        public SqliteHostBindingType Type { get; }
        public int Int32Value { get; }
        public long Int64Value { get; }
        public bool BoolValue { get; }
        public string TextValue { get; }
        public byte[] BlobValue { get; }
        public float Float32Value { get; }
        public double Float64Value { get; }

        public static SqliteHostBindingValue Null()
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Null, 0, 0L, false, null, null, 0f, 0d);
        }

        public static SqliteHostBindingValue Int32(int value)
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Int32, value, 0L, false, null, null, 0f, 0d);
        }

        public static SqliteHostBindingValue Int64(long value)
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Int64, 0, value, false, null, null, 0f, 0d);
        }

        public static SqliteHostBindingValue Bool(bool value)
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Bool, 0, 0L, value, null, null, 0f, 0d);
        }

        public static SqliteHostBindingValue Text(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            return new SqliteHostBindingValue(SqliteHostBindingType.Text, 0, 0L, false, value, null, 0f, 0d);
        }

        public static SqliteHostBindingValue Blob(byte[] value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            return new SqliteHostBindingValue(SqliteHostBindingType.Blob, 0, 0L, false, null, value, 0f, 0d);
        }

        public static SqliteHostBindingValue Float32(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentException("float32 value must be finite: " + value, nameof(value));
            }
            return new SqliteHostBindingValue(SqliteHostBindingType.Float32, 0, 0L, false, null, null, value, 0d);
        }

        public static SqliteHostBindingValue Float64(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("float64 value must be finite: " + value, nameof(value));
            }
            return new SqliteHostBindingValue(SqliteHostBindingType.Float64, 0, 0L, false, null, null, 0f, value);
        }
    }
}
