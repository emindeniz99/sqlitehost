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
            byte[] blobValue)
        {
            Type = type;
            Int32Value = int32Value;
            Int64Value = int64Value;
            BoolValue = boolValue;
            TextValue = textValue;
            BlobValue = blobValue;
        }

        public SqliteHostBindingType Type { get; }
        public int Int32Value { get; }
        public long Int64Value { get; }
        public bool BoolValue { get; }
        public string TextValue { get; }
        public byte[] BlobValue { get; }

        public static SqliteHostBindingValue Null()
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Null, 0, 0L, false, null, null);
        }

        public static SqliteHostBindingValue Int32(int value)
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Int32, value, 0L, false, null, null);
        }

        public static SqliteHostBindingValue Int64(long value)
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Int64, 0, value, false, null, null);
        }

        public static SqliteHostBindingValue Bool(bool value)
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Bool, 0, 0L, value, null, null);
        }

        public static SqliteHostBindingValue Text(string value)
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Text, 0, 0L, false, value, null);
        }

        public static SqliteHostBindingValue Blob(byte[] value)
        {
            return new SqliteHostBindingValue(SqliteHostBindingType.Blob, 0, 0L, false, null, value);
        }
    }
}
