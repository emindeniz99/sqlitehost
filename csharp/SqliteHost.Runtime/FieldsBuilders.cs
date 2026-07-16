using System;
using System.Collections.Generic;

namespace SqliteHost
{
    internal sealed class InputFieldsBuilder<TInput> : IInputFieldsBuilder<TInput>
    {
        public List<ErasedReadField> Fields { get; } = new List<ErasedReadField>();
        public List<ErasedInputListField> ListFields { get; } = new List<ErasedInputListField>();

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
            SpecGuards.RequireReferenceItemType(typeof(TItem), sqlName);
            var itemBuilder = new ListItemFieldsBuilder<TItem>();
            configureItem(itemBuilder);
            List<ErasedReadField> itemFields = itemBuilder.Fields;

            var itemSchemaFields = new List<SchemaFieldModel>();
            foreach (ErasedReadField field in itemFields)
            {
                itemSchemaFields.Add(field.ToSchemaField());
            }

            ListFields.Add(new ErasedInputListField(
                sqlName,
                itemSchemaFields,
                delegate { return (object)new TItem(); },
                itemFields,
                delegate(object dto, IReadOnlyList<object> items)
                {
                    var typedItems = new List<TItem>(items.Count);
                    for (int i = 0; i < items.Count; i++)
                    {
                        typedItems.Add((TItem)items[i]);
                    }
                    setter((TInput)dto, typedItems);
                }));
            return this;
        }
    }

    internal sealed class ListItemFieldsBuilder<TItem> : IListItemFieldsBuilder<TItem>
    {
        public List<ErasedReadField> Fields { get; } = new List<ErasedReadField>();

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
        public List<ErasedWriteField> Fields { get; } = new List<ErasedWriteField>();
        public List<ErasedResultListField> ListFields { get; } = new List<ErasedResultListField>();

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
            SpecGuards.RequireReferenceItemType(typeof(TItem), sqlName);
            var itemBuilder = new ListItemResultFieldsBuilder<TItem>();
            configureItem(itemBuilder);
            List<ErasedWriteField> itemFields = itemBuilder.Fields;

            var itemSchemaFields = new List<SchemaFieldModel>();
            foreach (ErasedWriteField field in itemFields)
            {
                itemSchemaFields.Add(field.ToSchemaField());
            }

            ListFields.Add(new ErasedResultListField(
                sqlName,
                itemSchemaFields,
                delegate(object result)
                {
                    List<TItem> items = getter((TResult)result);
                    if (items == null)
                    {
                        return null;
                    }
                    var boxedItems = new List<object>(items.Count);
                    for (int i = 0; i < items.Count; i++)
                    {
                        boxedItems.Add(items[i]);
                    }
                    return boxedItems;
                },
                itemFields));
            return this;
        }
    }

    internal sealed class ListItemResultFieldsBuilder<TItem> : IListItemResultFieldsBuilder<TItem>
    {
        public List<ErasedWriteField> Fields { get; } = new List<ErasedWriteField>();

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
