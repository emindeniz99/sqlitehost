using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Registers result fields. <c>sqlName</c> arguments are logical
    /// snake_case names; the runtime derives result columns via the host
    /// naming conventions.
    /// </summary>
    public interface IResultFieldsBuilder<TResult>
    {
        IResultFieldsBuilder<TResult> Int(string sqlName, Func<TResult, int> getter);
        IResultFieldsBuilder<TResult> Long(string sqlName, Func<TResult, long> getter);
        IResultFieldsBuilder<TResult> Bool(string sqlName, Func<TResult, bool> getter);
        IResultFieldsBuilder<TResult> Text(string sqlName, Func<TResult, string> getter);
        IResultFieldsBuilder<TResult> Blob(string sqlName, Func<TResult, byte[]> getter);
        IResultFieldsBuilder<TResult> OptionalInt(string sqlName, Func<TResult, int?> getter);
        IResultFieldsBuilder<TResult> OptionalLong(string sqlName, Func<TResult, long?> getter);
        IResultFieldsBuilder<TResult> OptionalBool(string sqlName, Func<TResult, bool?> getter);
        IResultFieldsBuilder<TResult> OptionalText(string sqlName, Func<TResult, string> getter);
        IResultFieldsBuilder<TResult> OptionalBlob(string sqlName, Func<TResult, byte[]> getter);

        IResultFieldsBuilder<TResult> List<TItem>(
            string sqlName,
            Func<TResult, List<TItem>> getter,
            Action<IListItemResultFieldsBuilder<TItem>> configureItem);
    }
}
