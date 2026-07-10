using System;

namespace SqliteHost
{
    /// <summary>Registers scalar fields of one input list item.</summary>
    public interface IListItemFieldsBuilder<TItem>
    {
        IListItemFieldsBuilder<TItem> Int(string sqlName, Action<TItem, int> setter);
        IListItemFieldsBuilder<TItem> Long(string sqlName, Action<TItem, long> setter);
        IListItemFieldsBuilder<TItem> Bool(string sqlName, Action<TItem, bool> setter);
        IListItemFieldsBuilder<TItem> Text(string sqlName, Action<TItem, string> setter);
        IListItemFieldsBuilder<TItem> Blob(string sqlName, Action<TItem, byte[]> setter);
        IListItemFieldsBuilder<TItem> OptionalInt(string sqlName, Action<TItem, int?> setter);
        IListItemFieldsBuilder<TItem> OptionalLong(string sqlName, Action<TItem, long?> setter);
        IListItemFieldsBuilder<TItem> OptionalBool(string sqlName, Action<TItem, bool?> setter);
        IListItemFieldsBuilder<TItem> OptionalText(string sqlName, Action<TItem, string> setter);
        IListItemFieldsBuilder<TItem> OptionalBlob(string sqlName, Action<TItem, byte[]> setter);
    }
}
