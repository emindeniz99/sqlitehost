namespace SqliteHost
{
    /// <summary>
    /// Inline scalar-function exposure of one method (feature
    /// inlineFunctions): the resolved function name and the arity range
    /// derived from the input's scalar fields (MinArgs = required count,
    /// MaxArgs = all fields; optional fields are trailing by construction).
    /// </summary>
    internal sealed class InlineFunctionModel
    {
        public InlineFunctionModel(string functionName, int minArgs, int maxArgs)
        {
            FunctionName = functionName;
            MinArgs = minArgs;
            MaxArgs = maxArgs;
        }

        public string FunctionName { get; }
        public int MinArgs { get; }
        public int MaxArgs { get; }
    }

    /// <summary>
    /// Presents one inline invocation's argument values as an
    /// <see cref="ISqliteHostRow"/>, so the spec's scalar input fields map
    /// them into the input DTO with the same setters the call-table read
    /// uses. Indexes at or beyond args.Length read as null (omitted
    /// trailing args). Adapters pass SQL values dynamically typed
    /// (integer/real/text/blob/null); the getters coerce numerics and fail
    /// loud on impossible reads (text where a number is required, ...).
    /// </summary>
    internal sealed class InlineArgumentRow : ISqliteHostRow
    {
        private readonly SqliteHostBindingValue[] _args;

        public InlineArgumentRow(SqliteHostBindingValue[] args)
        {
            _args = args;
        }

        public bool IsNull(int index)
        {
            return index >= _args.Length || _args[index].Type == SqliteHostBindingType.Null;
        }

        public int GetInt32(int index)
        {
            return (int)GetInt64(index);
        }

        public long GetInt64(int index)
        {
            SqliteHostBindingValue value = _args[index];
            switch (value.Type)
            {
                case SqliteHostBindingType.Int32:
                    return value.Int32Value;
                case SqliteHostBindingType.Int64:
                    return value.Int64Value;
                case SqliteHostBindingType.Bool:
                    return value.BoolValue ? 1 : 0;
                case SqliteHostBindingType.Float32:
                    return (long)value.Float32Value;
                case SqliteHostBindingType.Float64:
                    return (long)value.Float64Value;
                default:
                    throw Mismatch(index, "an integer", value);
            }
        }

        public bool GetBool(int index)
        {
            return GetInt64(index) != 0;
        }

        public string GetText(int index)
        {
            SqliteHostBindingValue value = _args[index];
            if (value.Type != SqliteHostBindingType.Text)
            {
                throw Mismatch(index, "text", value);
            }
            return value.TextValue;
        }

        public byte[] GetBlob(int index)
        {
            SqliteHostBindingValue value = _args[index];
            if (value.Type != SqliteHostBindingType.Blob)
            {
                throw Mismatch(index, "a blob", value);
            }
            return value.BlobValue;
        }

        public float GetFloat32(int index)
        {
            return (float)GetFloat64(index);
        }

        public double GetFloat64(int index)
        {
            SqliteHostBindingValue value = _args[index];
            switch (value.Type)
            {
                case SqliteHostBindingType.Float32:
                    return value.Float32Value;
                case SqliteHostBindingType.Float64:
                    return value.Float64Value;
                case SqliteHostBindingType.Int32:
                    return value.Int32Value;
                case SqliteHostBindingType.Int64:
                    return value.Int64Value;
                case SqliteHostBindingType.Bool:
                    return value.BoolValue ? 1 : 0;
                default:
                    throw Mismatch(index, "a number", value);
            }
        }

        private static SqliteHostInlineArgumentException Mismatch(
            int index, string expected, SqliteHostBindingValue value)
        {
            return new SqliteHostInlineArgumentException(
                "Argument " + (index + 1) + " cannot be read as " + expected
                + " (received a " + value.Type + " value).");
        }
    }
}
