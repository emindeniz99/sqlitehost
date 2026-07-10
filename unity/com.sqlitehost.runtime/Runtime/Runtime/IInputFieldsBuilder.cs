using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Registers input fields. <c>sqlName</c> arguments are logical
    /// snake_case names (never physical column names; the runtime derives
    /// columns via the host naming conventions).
    /// </summary>
    public interface IInputFieldsBuilder<TInput>
    {
        IInputFieldsBuilder<TInput> Int(string sqlName, Action<TInput, int> setter);
        IInputFieldsBuilder<TInput> Long(string sqlName, Action<TInput, long> setter);
        IInputFieldsBuilder<TInput> Bool(string sqlName, Action<TInput, bool> setter);
        IInputFieldsBuilder<TInput> Text(string sqlName, Action<TInput, string> setter);
        IInputFieldsBuilder<TInput> Blob(string sqlName, Action<TInput, byte[]> setter);
        IInputFieldsBuilder<TInput> Float(string sqlName, Action<TInput, float> setter);
        IInputFieldsBuilder<TInput> Double(string sqlName, Action<TInput, double> setter);
        IInputFieldsBuilder<TInput> OptionalInt(string sqlName, Action<TInput, int?> setter);
        IInputFieldsBuilder<TInput> OptionalLong(string sqlName, Action<TInput, long?> setter);
        IInputFieldsBuilder<TInput> OptionalBool(string sqlName, Action<TInput, bool?> setter);
        IInputFieldsBuilder<TInput> OptionalText(string sqlName, Action<TInput, string> setter);
        IInputFieldsBuilder<TInput> OptionalBlob(string sqlName, Action<TInput, byte[]> setter);
        IInputFieldsBuilder<TInput> OptionalFloat(string sqlName, Action<TInput, float?> setter);
        IInputFieldsBuilder<TInput> OptionalDouble(string sqlName, Action<TInput, double?> setter);

        IInputFieldsBuilder<TInput> List<TItem>(
            string sqlName,
            Action<TInput, List<TItem>> setter,
            Action<IListItemFieldsBuilder<TItem>> configureItem) where TItem : new();
    }
}
