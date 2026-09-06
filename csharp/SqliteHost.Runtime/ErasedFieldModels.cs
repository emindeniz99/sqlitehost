using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// One scalar column read into a boxed DTO. Type-erased on purpose:
    /// every host method shares these models (and their delegate shapes),
    /// so registering a method adds no generic instantiations — the
    /// AOT/IL2CPP footprint per method stays flat (docs/compatibility.md,
    /// app size).
    /// </summary>
    internal sealed class ErasedReadField
    {
        public ErasedReadField(
            string sqlName,
            HostScalarType scalarType,
            bool optional,
            Action<object, ISqliteHostRow, int> apply)
        {
            SqlName = sqlName;
            ScalarType = scalarType;
            Optional = optional;
            Apply = apply;
        }

        public string SqlName { get; }
        public HostScalarType ScalarType { get; }
        public bool Optional { get; }

        /// <summary>
        /// Resolved physical column name, or null when the definition
        /// declared logical names only (then the naming derivation
        /// supplies it — see <see cref="ResolvedNames"/>).
        /// </summary>
        public string Column { get; set; }

        /// <summary>Reads the column at the given index and assigns it on the boxed DTO.</summary>
        public Action<object, ISqliteHostRow, int> Apply { get; }

        /// <summary>Physical input column of this field under the given naming.</summary>
        public string ColumnName(SqliteHostNaming naming)
        {
            return Column ?? NamingDerivation.InputColumn(naming, SqlName);
        }

        public SchemaFieldModel ToSchemaField()
        {
            return new SchemaFieldModel(SqlName, ScalarType, Optional, Column);
        }
    }

    /// <summary>One scalar column written from a boxed DTO (type-erased, see <see cref="ErasedReadField"/>).</summary>
    internal sealed class ErasedWriteField
    {
        public ErasedWriteField(
            string sqlName,
            HostScalarType scalarType,
            bool optional,
            Func<object, SqliteHostBindingValue> read)
        {
            SqlName = sqlName;
            ScalarType = scalarType;
            Optional = optional;
            Read = read;
        }

        public string SqlName { get; }
        public HostScalarType ScalarType { get; }
        public bool Optional { get; }

        /// <summary>Resolved physical column name, or null to derive it (see <see cref="ErasedReadField.Column"/>).</summary>
        public string Column { get; set; }

        /// <summary>Reads the boxed DTO value as a typed binding value.</summary>
        public Func<object, SqliteHostBindingValue> Read { get; }

        /// <summary>Physical result column of this field under the given naming.</summary>
        public string ColumnName(SqliteHostNaming naming)
        {
            return Column ?? NamingDerivation.ResultColumn(naming, SqlName);
        }

        public SchemaFieldModel ToSchemaField()
        {
            return new SchemaFieldModel(SqlName, ScalarType, Optional, Column);
        }
    }

    /// <summary>
    /// One input list&lt;object&gt; field: the accessors needed to load ordered
    /// child rows into the boxed input DTO. The child-row SQL itself lives
    /// in <see cref="ErasedHostMethodSpec"/> so all methods share it.
    /// </summary>
    internal sealed class ErasedInputListField
    {
        public ErasedInputListField(
            string sqlName,
            IReadOnlyList<SchemaFieldModel> itemSchemaFields,
            Func<object> createItem,
            IReadOnlyList<ErasedReadField> itemFields,
            Action<object, IReadOnlyList<object>> assignItems)
        {
            SqlName = sqlName;
            ItemSchemaFields = itemSchemaFields;
            CreateItem = createItem;
            ItemFields = itemFields;
            AssignItems = assignItems;
        }

        public string SqlName { get; }
        public IReadOnlyList<SchemaFieldModel> ItemSchemaFields { get; }

        /// <summary>Resolved physical child table name, or null to derive it.</summary>
        public string ChildTable { get; set; }

        /// <summary>Creates one boxed list-item DTO.</summary>
        public Func<object> CreateItem { get; }

        public IReadOnlyList<ErasedReadField> ItemFields { get; }

        /// <summary>Assigns the loaded boxed items onto the boxed input DTO.</summary>
        public Action<object, IReadOnlyList<object>> AssignItems { get; }
    }

    /// <summary>
    /// One result list&lt;object&gt; field: the accessors needed to write child
    /// rows from the boxed result DTO (SQL lives in <see cref="ErasedHostMethodSpec"/>).
    /// </summary>
    internal sealed class ErasedResultListField
    {
        public ErasedResultListField(
            string sqlName,
            IReadOnlyList<SchemaFieldModel> itemSchemaFields,
            Func<object, IReadOnlyList<object>> getItems,
            IReadOnlyList<ErasedWriteField> itemFields)
        {
            SqlName = sqlName;
            ItemSchemaFields = itemSchemaFields;
            GetItems = getItems;
            ItemFields = itemFields;
        }

        public string SqlName { get; }
        public IReadOnlyList<SchemaFieldModel> ItemSchemaFields { get; }

        /// <summary>Resolved physical child table name, or null to derive it.</summary>
        public string ChildTable { get; set; }

        /// <summary>Reads the boxed items from the boxed result DTO (null and empty mean "no child rows").</summary>
        public Func<object, IReadOnlyList<object>> GetItems { get; }

        public IReadOnlyList<ErasedWriteField> ItemFields { get; }
    }
}
