using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Erased field factories of the ultra profile: readers store column
    /// values into the call/row value bags by field name, writers read the
    /// declared values back out. Everything here is non-generic and shared
    /// by every ultra method.
    /// </summary>
    internal static class UltraFields
    {
        public static Func<object> CallFactory(
            IReadOnlyList<string> fieldNames,
            IReadOnlyList<string> listNames)
        {
            return delegate { return (object)new SqliteHostUltraCall(fieldNames, listNames); };
        }

        public static ErasedReadField ReadField(UltraFieldDecl decl)
        {
            string sqlName = decl.SqlName;
            HostScalarType scalarType = decl.ScalarType;
            bool optional = decl.Optional;
            return new ErasedReadField(sqlName, scalarType, optional,
                delegate(object dto, ISqliteHostRow row, int index)
                {
                    ((IUltraValueSink)dto).Store(sqlName, ReadValue(row, index, scalarType, optional));
                });
        }

        public static ErasedWriteField WriteField(UltraFieldDecl decl)
        {
            string sqlName = decl.SqlName;
            bool optional = decl.Optional;
            return new ErasedWriteField(sqlName, decl.ScalarType, optional,
                delegate(object source)
                {
                    return ((IUltraValueSource)source).ReadStored(sqlName, optional);
                });
        }

        public static ErasedInputListField InputList(string sqlName, List<UltraFieldDecl> itemDecls)
        {
            var itemFields = new List<ErasedReadField>(itemDecls.Count);
            foreach (UltraFieldDecl decl in itemDecls)
            {
                itemFields.Add(ReadField(decl));
            }
            string listName = sqlName;
            return new ErasedInputListField(
                sqlName,
                ToSchemaFields(itemDecls),
                delegate { return (object)new SqliteHostUltraRow(); },
                itemFields,
                delegate(object dto, IReadOnlyList<object> items)
                {
                    var rows = new List<SqliteHostUltraRow>(items.Count);
                    for (int i = 0; i < items.Count; i++)
                    {
                        rows.Add((SqliteHostUltraRow)items[i]);
                    }
                    ((SqliteHostUltraCall)dto).AssignList(listName, rows);
                });
        }

        public static ErasedResultListField ResultList(string sqlName, List<UltraFieldDecl> itemDecls)
        {
            var itemFields = new List<ErasedWriteField>(itemDecls.Count);
            foreach (UltraFieldDecl decl in itemDecls)
            {
                itemFields.Add(WriteField(decl));
            }
            string listName = sqlName;
            return new ErasedResultListField(
                sqlName,
                ToSchemaFields(itemDecls),
                delegate(object result) { return ((SqliteHostUltraResult)result).BoxedRows(listName); },
                itemFields);
        }

        public static IReadOnlyList<SchemaFieldModel> ToSchemaFields(List<UltraFieldDecl> decls)
        {
            var schemaFields = new List<SchemaFieldModel>(decls.Count);
            foreach (UltraFieldDecl decl in decls)
            {
                schemaFields.Add(new SchemaFieldModel(decl.SqlName, decl.ScalarType, decl.Optional));
            }
            return schemaFields;
        }

        private static SqliteHostBindingValue ReadValue(
            ISqliteHostRow row,
            int index,
            HostScalarType scalarType,
            bool optional)
        {
            if (optional && row.IsNull(index))
            {
                return SqliteHostBindingValue.Null();
            }
            switch (scalarType)
            {
                case HostScalarType.Int32:
                    return SqliteHostBindingValue.Int32(row.GetInt32(index));
                case HostScalarType.Int64:
                    return SqliteHostBindingValue.Int64(row.GetInt64(index));
                case HostScalarType.Boolean:
                    return SqliteHostBindingValue.Bool(row.GetBool(index));
                case HostScalarType.String:
                {
                    string text = row.GetText(index);
                    return text == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Text(text);
                }
                case HostScalarType.Bytes:
                {
                    byte[] blob = row.GetBlob(index);
                    return blob == null ? SqliteHostBindingValue.Null() : SqliteHostBindingValue.Blob(blob);
                }
                case HostScalarType.Float32:
                    return SqliteHostBindingValue.Float32(row.GetFloat32(index));
                default:
                    return SqliteHostBindingValue.Float64(row.GetFloat64(index));
            }
        }
    }

    /// <summary>
    /// The declared result shape of one ultra method, enforced fail-loud
    /// after every handler invocation: every declared field set (or
    /// legitimately NULL), every set field declared and correctly typed,
    /// same for result-list rows. Violations surface as handler errors.
    /// </summary>
    internal sealed class UltraResultShape
    {
        private readonly string _methodName;
        private readonly IReadOnlyList<UltraFieldDecl> _fields;
        private readonly Dictionary<string, UltraFieldDecl> _fieldsByName;
        private readonly Dictionary<string, IReadOnlyList<UltraFieldDecl>> _listsByName;

        public UltraResultShape(
            string methodName,
            IReadOnlyList<UltraFieldDecl> fields,
            IReadOnlyList<KeyValuePair<string, List<UltraFieldDecl>>> lists)
        {
            _methodName = methodName;
            _fields = fields;
            _fieldsByName = new Dictionary<string, UltraFieldDecl>(StringComparer.Ordinal);
            foreach (UltraFieldDecl field in fields)
            {
                _fieldsByName[field.SqlName] = field;
            }
            _listsByName = new Dictionary<string, IReadOnlyList<UltraFieldDecl>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<UltraFieldDecl>> list in lists)
            {
                _listsByName[list.Key] = list.Value;
            }
        }

        public void Validate(SqliteHostUltraResult result)
        {
            ValidateRow(result.ParentRow, _fields, _fieldsByName, "result field");

            foreach (KeyValuePair<string, List<SqliteHostUltraRow>> list in result.Lists)
            {
                IReadOnlyList<UltraFieldDecl> itemDecls;
                if (!_listsByName.TryGetValue(list.Key, out itemDecls))
                {
                    throw new InvalidOperationException(
                        "Method '" + _methodName + "': the handler added rows to undeclared result list '"
                        + list.Key + "'.");
                }
                var itemsByName = new Dictionary<string, UltraFieldDecl>(StringComparer.Ordinal);
                foreach (UltraFieldDecl decl in itemDecls)
                {
                    itemsByName[decl.SqlName] = decl;
                }
                foreach (SqliteHostUltraRow row in list.Value)
                {
                    ValidateRow(row, itemDecls, itemsByName, "field of result list '" + list.Key + "'");
                }
            }
        }

        private void ValidateRow(
            SqliteHostUltraRow row,
            IReadOnlyList<UltraFieldDecl> decls,
            Dictionary<string, UltraFieldDecl> declsByName,
            string what)
        {
            foreach (UltraFieldDecl decl in decls)
            {
                SqliteHostBindingValue value;
                if (!row.TryGetStored(decl.SqlName, out value))
                {
                    if (decl.Optional)
                    {
                        continue;
                    }
                    throw new InvalidOperationException(
                        "Method '" + _methodName + "': " + what + " '" + decl.SqlName
                        + "' was not set by the handler.");
                }
                CheckValueType(decl, value, what);
            }
            foreach (KeyValuePair<string, SqliteHostBindingValue> stored in row.StoredValues)
            {
                if (!declsByName.ContainsKey(stored.Key))
                {
                    throw new InvalidOperationException(
                        "Method '" + _methodName + "': the handler set undeclared " + what + " '"
                        + stored.Key + "'.");
                }
            }
        }

        private void CheckValueType(UltraFieldDecl decl, SqliteHostBindingValue value, string what)
        {
            if (value.Type == SqliteHostBindingType.Null)
            {
                if (decl.Optional
                    || decl.ScalarType == HostScalarType.String
                    || decl.ScalarType == HostScalarType.Bytes)
                {
                    // Optional fields may always be NULL; required text/blob
                    // may be NULL (classic parity: null string/byte[] values
                    // write NULL).
                    return;
                }
                throw new InvalidOperationException(
                    "Method '" + _methodName + "': required " + what + " '" + decl.SqlName
                    + "' cannot be NULL.");
            }
            SqliteHostBindingType expected = ExpectedBindingType(decl.ScalarType);
            if (value.Type != expected)
            {
                throw new InvalidOperationException(
                    "Method '" + _methodName + "': " + what + " '" + decl.SqlName
                    + "' was set to a " + value.Type + " value but is declared as " + expected + ".");
            }
        }

        private static SqliteHostBindingType ExpectedBindingType(HostScalarType scalarType)
        {
            switch (scalarType)
            {
                case HostScalarType.Int32:
                    return SqliteHostBindingType.Int32;
                case HostScalarType.Int64:
                    return SqliteHostBindingType.Int64;
                case HostScalarType.Boolean:
                    return SqliteHostBindingType.Bool;
                case HostScalarType.String:
                    return SqliteHostBindingType.Text;
                case HostScalarType.Bytes:
                    return SqliteHostBindingType.Blob;
                case HostScalarType.Float32:
                    return SqliteHostBindingType.Float32;
                default:
                    return SqliteHostBindingType.Float64;
            }
        }
    }
}
