using System;

namespace SqliteHost
{
    /// <summary>Registers scalar fields of one result list item.</summary>
    public interface IListItemResultFieldsBuilder<TItem>
    {
        IListItemResultFieldsBuilder<TItem> Int(string sqlName, Func<TItem, int> getter);
        IListItemResultFieldsBuilder<TItem> Long(string sqlName, Func<TItem, long> getter);
        IListItemResultFieldsBuilder<TItem> Bool(string sqlName, Func<TItem, bool> getter);
        IListItemResultFieldsBuilder<TItem> Text(string sqlName, Func<TItem, string> getter);
        IListItemResultFieldsBuilder<TItem> Blob(string sqlName, Func<TItem, byte[]> getter);
        IListItemResultFieldsBuilder<TItem> Float(string sqlName, Func<TItem, float> getter);
        IListItemResultFieldsBuilder<TItem> Double(string sqlName, Func<TItem, double> getter);
        IListItemResultFieldsBuilder<TItem> OptionalInt(string sqlName, Func<TItem, int?> getter);
        IListItemResultFieldsBuilder<TItem> OptionalLong(string sqlName, Func<TItem, long?> getter);
        IListItemResultFieldsBuilder<TItem> OptionalBool(string sqlName, Func<TItem, bool?> getter);
        IListItemResultFieldsBuilder<TItem> OptionalText(string sqlName, Func<TItem, string> getter);
        IListItemResultFieldsBuilder<TItem> OptionalBlob(string sqlName, Func<TItem, byte[]> getter);
        IListItemResultFieldsBuilder<TItem> OptionalFloat(string sqlName, Func<TItem, float?> getter);
        IListItemResultFieldsBuilder<TItem> OptionalDouble(string sqlName, Func<TItem, double?> getter);

        /// <summary>
        /// Physical column name of the scalar field declared immediately
        /// before this call. Generated code emits the manifest's resolved
        /// <c>column</c>; omit it and the runtime derives the column from
        /// the host naming (docs/naming.md).
        /// </summary>
        IListItemResultFieldsBuilder<TItem> Column(string column);
    }
}
