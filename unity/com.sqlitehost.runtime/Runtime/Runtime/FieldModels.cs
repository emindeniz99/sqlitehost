using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>One scalar column read into a DTO of type <typeparamref name="T"/>.</summary>
    internal sealed class ScalarReadField<T>
    {
        public ScalarReadField(
            string sqlName,
            HostScalarType scalarType,
            bool optional,
            Action<T, ISqliteHostRow, int> apply)
        {
            SqlName = sqlName;
            ScalarType = scalarType;
            Optional = optional;
            Apply = apply;
        }

        public string SqlName { get; }
        public HostScalarType ScalarType { get; }
        public bool Optional { get; }

        /// <summary>Reads the column at the given index and assigns it on the DTO.</summary>
        public Action<T, ISqliteHostRow, int> Apply { get; }

        public SchemaFieldModel ToSchemaField()
        {
            return new SchemaFieldModel(SqlName, ScalarType, Optional);
        }
    }

    /// <summary>One scalar column written from a DTO of type <typeparamref name="T"/>.</summary>
    internal sealed class ScalarWriteField<T>
    {
        public ScalarWriteField(
            string sqlName,
            HostScalarType scalarType,
            bool optional,
            Func<T, SqliteHostBindingValue> read)
        {
            SqlName = sqlName;
            ScalarType = scalarType;
            Optional = optional;
            Read = read;
        }

        public string SqlName { get; }
        public HostScalarType ScalarType { get; }
        public bool Optional { get; }

        /// <summary>Reads the DTO value as a typed binding value.</summary>
        public Func<T, SqliteHostBindingValue> Read { get; }

        public SchemaFieldModel ToSchemaField()
        {
            return new SchemaFieldModel(SqlName, ScalarType, Optional);
        }
    }

    /// <summary>One input list&lt;object&gt; field: loads ordered child rows into the input DTO.</summary>
    internal sealed class InputListField<TInput>
    {
        public InputListField(
            string sqlName,
            IReadOnlyList<SchemaFieldModel> itemSchemaFields,
            Action<TInput, ISqliteHostConnection, SqliteHostNaming, SqliteHostColumns, string, string> load)
        {
            SqlName = sqlName;
            ItemSchemaFields = itemSchemaFields;
            Load = load;
        }

        public string SqlName { get; }
        public IReadOnlyList<SchemaFieldModel> ItemSchemaFields { get; }

        /// <summary>(dto, connection, naming, columns, methodName, callId) — reads child rows ordered by the configured item-index column.</summary>
        public Action<TInput, ISqliteHostConnection, SqliteHostNaming, SqliteHostColumns, string, string> Load { get; }
    }

    /// <summary>One result list&lt;object&gt; field: writes child rows from the result DTO.</summary>
    internal sealed class ResultListField<TResult>
    {
        public ResultListField(
            string sqlName,
            IReadOnlyList<SchemaFieldModel> itemSchemaFields,
            Action<TResult, ISqliteHostConnection, SqliteHostNaming, SqliteHostColumns, string, string> write)
        {
            SqlName = sqlName;
            ItemSchemaFields = itemSchemaFields;
            Write = write;
        }

        public string SqlName { get; }
        public IReadOnlyList<SchemaFieldModel> ItemSchemaFields { get; }

        /// <summary>(result, connection, naming, columns, methodName, callId) — inserts one child row per list item.</summary>
        public Action<TResult, ISqliteHostConnection, SqliteHostNaming, SqliteHostColumns, string, string> Write { get; }
    }
}
