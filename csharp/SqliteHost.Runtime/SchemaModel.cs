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

    /// <summary>
    /// Logical description of one scalar field, plus the physical column
    /// name when the definition resolved it (generated hosts carry the
    /// manifest's <c>column</c>). Null means "not resolved here": the
    /// caller derives the column from the host naming instead — see
    /// <see cref="ResolvedNames"/>.
    /// </summary>
    internal sealed class SchemaFieldModel
    {
        public SchemaFieldModel(string sqlName, HostScalarType scalarType, bool optional, string column = null)
        {
            SqlName = sqlName;
            ScalarType = scalarType;
            Optional = optional;
            Column = column;
        }

        public string SqlName { get; }
        public HostScalarType ScalarType { get; }
        public bool Optional { get; }

        /// <summary>Resolved physical column name, or null to derive it.</summary>
        public string Column { get; }
    }

    /// <summary>
    /// Logical description of one list&lt;object&gt; field, plus the resolved
    /// physical child table name when the definition carried one (null to
    /// derive it).
    /// </summary>
    internal sealed class SchemaListFieldModel
    {
        public SchemaListFieldModel(
            string sqlName,
            IReadOnlyList<SchemaFieldModel> itemFields,
            string childTable = null)
        {
            SqlName = sqlName;
            ItemFields = itemFields;
            ChildTable = childTable;
        }

        public string SqlName { get; }
        public IReadOnlyList<SchemaFieldModel> ItemFields { get; }

        /// <summary>Resolved physical child table name, or null to derive it.</summary>
        public string ChildTable { get; }
    }

    /// <summary>Logical schema shape of one host method, consumed by <see cref="SchemaGenerator"/>.</summary>
    internal sealed class SchemaMethodModel
    {
        public SchemaMethodModel(
            string methodName,
            IReadOnlyList<SchemaFieldModel> inputFields,
            IReadOnlyList<SchemaListFieldModel> inputListFields,
            IReadOnlyList<SchemaFieldModel> resultFields,
            IReadOnlyList<SchemaListFieldModel> resultListFields,
            string callTable = null,
            string resultTable = null,
            string queueTrigger = null)
        {
            MethodName = methodName;
            InputFields = inputFields;
            InputListFields = inputListFields;
            ResultFields = resultFields;
            ResultListFields = resultListFields;
            CallTable = callTable;
            ResultTable = resultTable;
            QueueTrigger = queueTrigger;
        }

        public string MethodName { get; }
        public IReadOnlyList<SchemaFieldModel> InputFields { get; }
        public IReadOnlyList<SchemaListFieldModel> InputListFields { get; }
        public IReadOnlyList<SchemaFieldModel> ResultFields { get; }
        public IReadOnlyList<SchemaListFieldModel> ResultListFields { get; }

        /// <summary>Resolved physical call table name, or null to derive it.</summary>
        public string CallTable { get; }

        /// <summary>Resolved physical result table name, or null to derive it.</summary>
        public string ResultTable { get; }

        /// <summary>Resolved physical queue trigger name, or null to derive it.</summary>
        public string QueueTrigger { get; }
    }
}
