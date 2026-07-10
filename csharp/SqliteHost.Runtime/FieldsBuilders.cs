using System;
using System.Collections.Generic;

namespace SqliteHost
{
    internal sealed class InputFieldsBuilder<TInput> : IInputFieldsBuilder<TInput>
    {
        public List<ScalarReadField<TInput>> Fields { get; } = new List<ScalarReadField<TInput>>();
        public List<InputListField<TInput>> ListFields { get; } = new List<InputListField<TInput>>();

        public IInputFieldsBuilder<TInput> Int(string sqlName, Action<TInput, int> setter)
        {
            Fields.Add(ScalarFields.Int(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> Long(string sqlName, Action<TInput, long> setter)
        {
            Fields.Add(ScalarFields.Long(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> Bool(string sqlName, Action<TInput, bool> setter)
        {
            Fields.Add(ScalarFields.Bool(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> Text(string sqlName, Action<TInput, string> setter)
        {
            Fields.Add(ScalarFields.Text(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> Blob(string sqlName, Action<TInput, byte[]> setter)
        {
            Fields.Add(ScalarFields.Blob(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> Float(string sqlName, Action<TInput, float> setter)
        {
            Fields.Add(ScalarFields.Float(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> Double(string sqlName, Action<TInput, double> setter)
        {
            Fields.Add(ScalarFields.Double(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> OptionalInt(string sqlName, Action<TInput, int?> setter)
        {
            Fields.Add(ScalarFields.OptionalInt(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> OptionalLong(string sqlName, Action<TInput, long?> setter)
        {
            Fields.Add(ScalarFields.OptionalLong(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> OptionalBool(string sqlName, Action<TInput, bool?> setter)
        {
            Fields.Add(ScalarFields.OptionalBool(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> OptionalText(string sqlName, Action<TInput, string> setter)
        {
            Fields.Add(ScalarFields.OptionalText(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> OptionalBlob(string sqlName, Action<TInput, byte[]> setter)
        {
            Fields.Add(ScalarFields.OptionalBlob(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> OptionalFloat(string sqlName, Action<TInput, float?> setter)
        {
            Fields.Add(ScalarFields.OptionalFloat(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> OptionalDouble(string sqlName, Action<TInput, double?> setter)
        {
            Fields.Add(ScalarFields.OptionalDouble(sqlName, setter));
            return this;
        }

        public IInputFieldsBuilder<TInput> List<TItem>(
            string sqlName,
            Action<TInput, List<TItem>> setter,
            Action<IListItemFieldsBuilder<TItem>> configureItem) where TItem : new()
        {
            var itemBuilder = new ListItemFieldsBuilder<TItem>();
            configureItem(itemBuilder);
            List<ScalarReadField<TItem>> itemFields = itemBuilder.Fields;

            var itemSchemaFields = new List<SchemaFieldModel>();
            foreach (ScalarReadField<TItem> field in itemFields)
            {
                itemSchemaFields.Add(field.ToSchemaField());
            }

            ListFields.Add(new InputListField<TInput>(
                sqlName,
                itemSchemaFields,
                delegate(TInput dto, ISqliteHostConnection connection, SqliteHostNaming naming, string methodName, string callId)
                {
                    string childTable = NamingDerivation.InputListTable(naming, methodName, sqlName);
                    var columns = new List<string>();
                    foreach (ScalarReadField<TItem> field in itemFields)
                    {
                        columns.Add(NamingDerivation.InputColumn(naming, field.SqlName));
                    }
                    string sql = "SELECT " + string.Join(", ", columns)
                        + " FROM " + childTable
                        + " WHERE call_id = :callId ORDER BY item_index";
                    IReadOnlyList<TItem> items = connection.Query(
                        sql,
                        RuntimeSql.CallIdBindings(callId),
                        delegate(ISqliteHostRow row)
                        {
                            var item = new TItem();
                            for (int i = 0; i < itemFields.Count; i++)
                            {
                                itemFields[i].Apply(item, row, i);
                            }
                            return item;
                        });
                    setter(dto, new List<TItem>(items));
                }));
            return this;
        }
    }

    internal sealed class ListItemFieldsBuilder<TItem> : IListItemFieldsBuilder<TItem>
    {
        public List<ScalarReadField<TItem>> Fields { get; } = new List<ScalarReadField<TItem>>();

        public IListItemFieldsBuilder<TItem> Int(string sqlName, Action<TItem, int> setter)
        {
            Fields.Add(ScalarFields.Int(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> Long(string sqlName, Action<TItem, long> setter)
        {
            Fields.Add(ScalarFields.Long(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> Bool(string sqlName, Action<TItem, bool> setter)
        {
            Fields.Add(ScalarFields.Bool(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> Text(string sqlName, Action<TItem, string> setter)
        {
            Fields.Add(ScalarFields.Text(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> Blob(string sqlName, Action<TItem, byte[]> setter)
        {
            Fields.Add(ScalarFields.Blob(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> Float(string sqlName, Action<TItem, float> setter)
        {
            Fields.Add(ScalarFields.Float(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> Double(string sqlName, Action<TItem, double> setter)
        {
            Fields.Add(ScalarFields.Double(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> OptionalInt(string sqlName, Action<TItem, int?> setter)
        {
            Fields.Add(ScalarFields.OptionalInt(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> OptionalLong(string sqlName, Action<TItem, long?> setter)
        {
            Fields.Add(ScalarFields.OptionalLong(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> OptionalBool(string sqlName, Action<TItem, bool?> setter)
        {
            Fields.Add(ScalarFields.OptionalBool(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> OptionalText(string sqlName, Action<TItem, string> setter)
        {
            Fields.Add(ScalarFields.OptionalText(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> OptionalBlob(string sqlName, Action<TItem, byte[]> setter)
        {
            Fields.Add(ScalarFields.OptionalBlob(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> OptionalFloat(string sqlName, Action<TItem, float?> setter)
        {
            Fields.Add(ScalarFields.OptionalFloat(sqlName, setter));
            return this;
        }

        public IListItemFieldsBuilder<TItem> OptionalDouble(string sqlName, Action<TItem, double?> setter)
        {
            Fields.Add(ScalarFields.OptionalDouble(sqlName, setter));
            return this;
        }
    }

    internal sealed class ResultFieldsBuilder<TResult> : IResultFieldsBuilder<TResult>
    {
        public List<ScalarWriteField<TResult>> Fields { get; } = new List<ScalarWriteField<TResult>>();
        public List<ResultListField<TResult>> ListFields { get; } = new List<ResultListField<TResult>>();

        public IResultFieldsBuilder<TResult> Int(string sqlName, Func<TResult, int> getter)
        {
            Fields.Add(ScalarFields.WriteInt(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> Long(string sqlName, Func<TResult, long> getter)
        {
            Fields.Add(ScalarFields.WriteLong(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> Bool(string sqlName, Func<TResult, bool> getter)
        {
            Fields.Add(ScalarFields.WriteBool(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> Text(string sqlName, Func<TResult, string> getter)
        {
            Fields.Add(ScalarFields.WriteText(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> Blob(string sqlName, Func<TResult, byte[]> getter)
        {
            Fields.Add(ScalarFields.WriteBlob(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> Float(string sqlName, Func<TResult, float> getter)
        {
            Fields.Add(ScalarFields.WriteFloat(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> Double(string sqlName, Func<TResult, double> getter)
        {
            Fields.Add(ScalarFields.WriteDouble(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> OptionalInt(string sqlName, Func<TResult, int?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalInt(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> OptionalLong(string sqlName, Func<TResult, long?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalLong(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> OptionalBool(string sqlName, Func<TResult, bool?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalBool(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> OptionalText(string sqlName, Func<TResult, string> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalText(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> OptionalBlob(string sqlName, Func<TResult, byte[]> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalBlob(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> OptionalFloat(string sqlName, Func<TResult, float?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalFloat(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> OptionalDouble(string sqlName, Func<TResult, double?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalDouble(sqlName, getter));
            return this;
        }

        public IResultFieldsBuilder<TResult> List<TItem>(
            string sqlName,
            Func<TResult, List<TItem>> getter,
            Action<IListItemResultFieldsBuilder<TItem>> configureItem)
        {
            var itemBuilder = new ListItemResultFieldsBuilder<TItem>();
            configureItem(itemBuilder);
            List<ScalarWriteField<TItem>> itemFields = itemBuilder.Fields;

            var itemSchemaFields = new List<SchemaFieldModel>();
            foreach (ScalarWriteField<TItem> field in itemFields)
            {
                itemSchemaFields.Add(field.ToSchemaField());
            }

            ListFields.Add(new ResultListField<TResult>(
                sqlName,
                itemSchemaFields,
                delegate(TResult result, ISqliteHostConnection connection, SqliteHostNaming naming, string methodName, string callId)
                {
                    List<TItem> items = getter(result);
                    if (items == null || items.Count == 0)
                    {
                        return;
                    }
                    string childTable = NamingDerivation.ResultListTable(naming, methodName, sqlName);
                    var columns = new List<string> { "call_id", "item_index" };
                    var placeholders = new List<string> { ":callId", ":itemIndex" };
                    for (int i = 0; i < itemFields.Count; i++)
                    {
                        columns.Add(NamingDerivation.ResultColumn(naming, itemFields[i].SqlName));
                        placeholders.Add(":v" + i);
                    }
                    string sql = "INSERT INTO " + childTable
                        + " (" + string.Join(", ", columns) + ")"
                        + " VALUES (" + string.Join(", ", placeholders) + ")";
                    for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
                    {
                        var bindings = new List<SqliteHostBinding>
                        {
                            new SqliteHostBinding("callId", SqliteHostBindingValue.Text(callId)),
                            new SqliteHostBinding("itemIndex", SqliteHostBindingValue.Int32(itemIndex))
                        };
                        for (int i = 0; i < itemFields.Count; i++)
                        {
                            bindings.Add(new SqliteHostBinding("v" + i, itemFields[i].Read(items[itemIndex])));
                        }
                        connection.Execute(sql, bindings);
                    }
                }));
            return this;
        }
    }

    internal sealed class ListItemResultFieldsBuilder<TItem> : IListItemResultFieldsBuilder<TItem>
    {
        public List<ScalarWriteField<TItem>> Fields { get; } = new List<ScalarWriteField<TItem>>();

        public IListItemResultFieldsBuilder<TItem> Int(string sqlName, Func<TItem, int> getter)
        {
            Fields.Add(ScalarFields.WriteInt(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> Long(string sqlName, Func<TItem, long> getter)
        {
            Fields.Add(ScalarFields.WriteLong(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> Bool(string sqlName, Func<TItem, bool> getter)
        {
            Fields.Add(ScalarFields.WriteBool(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> Text(string sqlName, Func<TItem, string> getter)
        {
            Fields.Add(ScalarFields.WriteText(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> Blob(string sqlName, Func<TItem, byte[]> getter)
        {
            Fields.Add(ScalarFields.WriteBlob(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> Float(string sqlName, Func<TItem, float> getter)
        {
            Fields.Add(ScalarFields.WriteFloat(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> Double(string sqlName, Func<TItem, double> getter)
        {
            Fields.Add(ScalarFields.WriteDouble(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> OptionalInt(string sqlName, Func<TItem, int?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalInt(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> OptionalLong(string sqlName, Func<TItem, long?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalLong(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> OptionalBool(string sqlName, Func<TItem, bool?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalBool(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> OptionalText(string sqlName, Func<TItem, string> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalText(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> OptionalBlob(string sqlName, Func<TItem, byte[]> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalBlob(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> OptionalFloat(string sqlName, Func<TItem, float?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalFloat(sqlName, getter));
            return this;
        }

        public IListItemResultFieldsBuilder<TItem> OptionalDouble(string sqlName, Func<TItem, double?> getter)
        {
            Fields.Add(ScalarFields.WriteOptionalDouble(sqlName, getter));
            return this;
        }
    }
}
