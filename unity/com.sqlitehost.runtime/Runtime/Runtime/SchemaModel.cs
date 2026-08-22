using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>Logical scalar types of the host-method type system (docs/manifest.md).</summary>
    internal enum HostScalarType
    {
        Int32,
        Int64,
        Boolean,
        String,
        Bytes,
        Float32,
        Float64
    }

    /// <summary>Logical description of one scalar field (no physical names).</summary>
    internal sealed class SchemaFieldModel
    {
        public SchemaFieldModel(string sqlName, HostScalarType scalarType, bool optional)
        {
            SqlName = sqlName;
            ScalarType = scalarType;
            Optional = optional;
        }

        public string SqlName { get; }
        public HostScalarType ScalarType { get; }
        public bool Optional { get; }
    }

    /// <summary>Logical description of one list&lt;object&gt; field.</summary>
    internal sealed class SchemaListFieldModel
    {
        public SchemaListFieldModel(string sqlName, IReadOnlyList<SchemaFieldModel> itemFields)
        {
            SqlName = sqlName;
            ItemFields = itemFields;
        }

        public string SqlName { get; }
        public IReadOnlyList<SchemaFieldModel> ItemFields { get; }
    }

    /// <summary>Logical schema shape of one host method, consumed by <see cref="SchemaGenerator"/>.</summary>
    internal sealed class SchemaMethodModel
    {
        public SchemaMethodModel(
            string methodName,
            IReadOnlyList<SchemaFieldModel> inputFields,
            IReadOnlyList<SchemaListFieldModel> inputListFields,
            IReadOnlyList<SchemaFieldModel> resultFields,
            IReadOnlyList<SchemaListFieldModel> resultListFields)
        {
            MethodName = methodName;
            InputFields = inputFields;
            InputListFields = inputListFields;
            ResultFields = resultFields;
            ResultListFields = resultListFields;
        }

        public string MethodName { get; }
        public IReadOnlyList<SchemaFieldModel> InputFields { get; }
        public IReadOnlyList<SchemaListFieldModel> InputListFields { get; }
        public IReadOnlyList<SchemaFieldModel> ResultFields { get; }
        public IReadOnlyList<SchemaListFieldModel> ResultListFields { get; }
    }
}
