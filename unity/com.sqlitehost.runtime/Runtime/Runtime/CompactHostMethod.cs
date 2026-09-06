using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Entry point of the compact method descriptor API — the size-first
    /// generated-code target (`--profile compact`). Same typed DTOs and
    /// handler interface as the classic profile, but every accessor is a
    /// pre-erased delegate (typically a static method group), so
    /// registering a method adds no lambdas, no display classes, and no
    /// generic instantiations. Runtime behavior is identical to
    /// <see cref="HostMethod"/> by construction: both lower to the same
    /// erased execution core.
    /// </summary>
    public static class CompactHostMethod
    {
        public static ICompactHostMethodBuilder<THandlers> For<THandlers>(string methodName)
        {
            return new CompactHostMethodBuilder<THandlers>(methodName);
        }
    }

    /// <summary>
    /// Flat compact descriptor for one host method. Field order follows
    /// call order; `Input*`/`Result*` mirror the classic
    /// `Inputs(...)`/`Results(...)` field kinds one-to-one. Setters receive
    /// the boxed input DTO, getters the boxed result DTO.
    /// </summary>
    public interface ICompactHostMethodBuilder<THandlers>
    {
        ICompactHostMethodBuilder<THandlers> ApiLevel(int apiLevel);

        /// <summary>Factory for the boxed input DTO (required; generated code passes a static method group).</summary>
        ICompactHostMethodBuilder<THandlers> CreateInput(Func<object> factory);

        ICompactHostMethodBuilder<THandlers> InputInt(string sqlName, Action<object, int> setter);
        ICompactHostMethodBuilder<THandlers> InputLong(string sqlName, Action<object, long> setter);
        ICompactHostMethodBuilder<THandlers> InputBool(string sqlName, Action<object, bool> setter);
        ICompactHostMethodBuilder<THandlers> InputText(string sqlName, Action<object, string> setter);
        ICompactHostMethodBuilder<THandlers> InputBlob(string sqlName, Action<object, byte[]> setter);
        ICompactHostMethodBuilder<THandlers> InputFloat(string sqlName, Action<object, float> setter);
        ICompactHostMethodBuilder<THandlers> InputDouble(string sqlName, Action<object, double> setter);
        ICompactHostMethodBuilder<THandlers> InputOptionalInt(string sqlName, Action<object, int?> setter);
        ICompactHostMethodBuilder<THandlers> InputOptionalLong(string sqlName, Action<object, long?> setter);
        ICompactHostMethodBuilder<THandlers> InputOptionalBool(string sqlName, Action<object, bool?> setter);
        ICompactHostMethodBuilder<THandlers> InputOptionalText(string sqlName, Action<object, string> setter);
        ICompactHostMethodBuilder<THandlers> InputOptionalBlob(string sqlName, Action<object, byte[]> setter);
        ICompactHostMethodBuilder<THandlers> InputOptionalFloat(string sqlName, Action<object, float?> setter);
        ICompactHostMethodBuilder<THandlers> InputOptionalDouble(string sqlName, Action<object, double?> setter);

        /// <summary>
        /// One input list&lt;object&gt; field. createItem builds a boxed item
        /// DTO; assignItems receives the ordered boxed items and assigns
        /// the typed list onto the boxed input DTO; configureItem declares
        /// the item columns (generated code passes static method groups
        /// for all three).
        /// </summary>
        ICompactHostMethodBuilder<THandlers> InputList(
            string sqlName,
            Func<object> createItem,
            Action<object, IReadOnlyList<object>> assignItems,
            Action<ICompactListItemFieldsBuilder> configureItem);

        ICompactHostMethodBuilder<THandlers> ResultInt(string sqlName, Func<object, int> getter);
        ICompactHostMethodBuilder<THandlers> ResultLong(string sqlName, Func<object, long> getter);
        ICompactHostMethodBuilder<THandlers> ResultBool(string sqlName, Func<object, bool> getter);
        ICompactHostMethodBuilder<THandlers> ResultText(string sqlName, Func<object, string> getter);
        ICompactHostMethodBuilder<THandlers> ResultBlob(string sqlName, Func<object, byte[]> getter);
        ICompactHostMethodBuilder<THandlers> ResultFloat(string sqlName, Func<object, float> getter);
        ICompactHostMethodBuilder<THandlers> ResultDouble(string sqlName, Func<object, double> getter);
        ICompactHostMethodBuilder<THandlers> ResultOptionalInt(string sqlName, Func<object, int?> getter);
        ICompactHostMethodBuilder<THandlers> ResultOptionalLong(string sqlName, Func<object, long?> getter);
        ICompactHostMethodBuilder<THandlers> ResultOptionalBool(string sqlName, Func<object, bool?> getter);
        ICompactHostMethodBuilder<THandlers> ResultOptionalText(string sqlName, Func<object, string> getter);
        ICompactHostMethodBuilder<THandlers> ResultOptionalBlob(string sqlName, Func<object, byte[]> getter);
        ICompactHostMethodBuilder<THandlers> ResultOptionalFloat(string sqlName, Func<object, float?> getter);
        ICompactHostMethodBuilder<THandlers> ResultOptionalDouble(string sqlName, Func<object, double?> getter);

        /// <summary>
        /// One result list&lt;object&gt; field. getItems reads the boxed items
        /// off the boxed result DTO (null or empty mean "no child rows");
        /// configureItem declares the item columns.
        /// </summary>
        ICompactHostMethodBuilder<THandlers> ResultList(
            string sqlName,
            Func<object, IReadOnlyList<object>> getItems,
            Action<ICompactListItemResultFieldsBuilder> configureItem);

        /// <summary>Boxed handler invocation: (handlers, input DTO) → result DTO (required).</summary>
        ICompactHostMethodBuilder<THandlers> Handler(Func<object, object, object> handler);

        /// <summary>
        /// Exposes the method as an inline scalar function (feature
        /// inlineFunctions). Build() re-checks the shape rules fail-loud
        /// with the same rules and messages as the classic profile.
        /// </summary>
        ICompactHostMethodBuilder<THandlers> Inline(string functionName);
        /// <summary>
        /// Same, with the arity the manifest already carries. Generated
        /// code uses this overload; the one-argument form derives the
        /// arity from the declared input fields instead, which is what a
        /// hand-written definition needs.
        /// </summary>
        ICompactHostMethodBuilder<THandlers> Inline(
            string functionName, int minArgs, int maxArgs);

        /// <summary>
        /// Physical column name of the scalar field declared immediately
        /// before this call. Generated code emits the manifest's resolved
        /// <c>column</c>; omit it and the runtime derives the column from
        /// the host naming (docs/naming.md).
        /// </summary>
        ICompactHostMethodBuilder<THandlers> Column(string column);

        /// <summary>
        /// Physical child table name of the list field declared immediately
        /// before this call (resolved <c>childTable</c>; omit to derive).
        /// </summary>
        ICompactHostMethodBuilder<THandlers> ChildTable(string childTable);

        /// <summary>
        /// Physical call/result/queue-trigger table names of this method
        /// (resolved names from the manifest; omit to derive all three).
        /// </summary>
        ICompactHostMethodBuilder<THandlers> Tables(
            string callTable, string resultTable, string queueTrigger);

        IHostMethodSpec<THandlers> Build();
    }

    /// <summary>Item columns of a compact input list field (boxed item DTO setters).</summary>
    public interface ICompactListItemFieldsBuilder
    {
        ICompactListItemFieldsBuilder Int(string sqlName, Action<object, int> setter);
        ICompactListItemFieldsBuilder Long(string sqlName, Action<object, long> setter);
        ICompactListItemFieldsBuilder Bool(string sqlName, Action<object, bool> setter);
        ICompactListItemFieldsBuilder Text(string sqlName, Action<object, string> setter);
        ICompactListItemFieldsBuilder Blob(string sqlName, Action<object, byte[]> setter);
        ICompactListItemFieldsBuilder Float(string sqlName, Action<object, float> setter);
        ICompactListItemFieldsBuilder Double(string sqlName, Action<object, double> setter);
        ICompactListItemFieldsBuilder OptionalInt(string sqlName, Action<object, int?> setter);
        ICompactListItemFieldsBuilder OptionalLong(string sqlName, Action<object, long?> setter);
        ICompactListItemFieldsBuilder OptionalBool(string sqlName, Action<object, bool?> setter);
        ICompactListItemFieldsBuilder OptionalText(string sqlName, Action<object, string> setter);
        ICompactListItemFieldsBuilder OptionalBlob(string sqlName, Action<object, byte[]> setter);
        ICompactListItemFieldsBuilder OptionalFloat(string sqlName, Action<object, float?> setter);
        ICompactListItemFieldsBuilder OptionalDouble(string sqlName, Action<object, double?> setter);

        /// <summary>
        /// Physical column name of the scalar field declared immediately
        /// before this call. Generated code emits the manifest's resolved
        /// <c>column</c>; omit it and the runtime derives the column from
        /// the host naming (docs/naming.md).
        /// </summary>
        ICompactListItemFieldsBuilder Column(string column);
    }

    /// <summary>Item columns of a compact result list field (boxed item DTO getters).</summary>
    public interface ICompactListItemResultFieldsBuilder
    {
        ICompactListItemResultFieldsBuilder Int(string sqlName, Func<object, int> getter);
        ICompactListItemResultFieldsBuilder Long(string sqlName, Func<object, long> getter);
        ICompactListItemResultFieldsBuilder Bool(string sqlName, Func<object, bool> getter);
        ICompactListItemResultFieldsBuilder Text(string sqlName, Func<object, string> getter);
        ICompactListItemResultFieldsBuilder Blob(string sqlName, Func<object, byte[]> getter);
        ICompactListItemResultFieldsBuilder Float(string sqlName, Func<object, float> getter);
        ICompactListItemResultFieldsBuilder Double(string sqlName, Func<object, double> getter);
        ICompactListItemResultFieldsBuilder OptionalInt(string sqlName, Func<object, int?> getter);
        ICompactListItemResultFieldsBuilder OptionalLong(string sqlName, Func<object, long?> getter);
        ICompactListItemResultFieldsBuilder OptionalBool(string sqlName, Func<object, bool?> getter);
        ICompactListItemResultFieldsBuilder OptionalText(string sqlName, Func<object, string> getter);
        ICompactListItemResultFieldsBuilder OptionalBlob(string sqlName, Func<object, byte[]> getter);
        ICompactListItemResultFieldsBuilder OptionalFloat(string sqlName, Func<object, float?> getter);
        ICompactListItemResultFieldsBuilder OptionalDouble(string sqlName, Func<object, double?> getter);

        /// <summary>
        /// Physical column name of the scalar field declared immediately
        /// before this call. Generated code emits the manifest's resolved
        /// <c>column</c>; omit it and the runtime derives the column from
        /// the host naming (docs/naming.md).
        /// </summary>
        ICompactListItemResultFieldsBuilder Column(string column);
    }

    internal sealed class CompactHostMethodBuilder<THandlers> : ICompactHostMethodBuilder<THandlers>
    {
        private readonly string _methodName;
        private readonly List<ErasedReadField> _inputFields = new List<ErasedReadField>();
        private readonly List<ErasedInputListField> _inputListFields = new List<ErasedInputListField>();
        private readonly List<ErasedWriteField> _resultFields = new List<ErasedWriteField>();
        private readonly List<ErasedResultListField> _resultListFields = new List<ErasedResultListField>();
        private int _apiLevel = 1;
        private Func<object> _createInput;
        private Func<object, object, object> _handler;
        // Most recent scalar / list declaration, so Column(...) and
        // ChildTable(...) attach to it without a per-call closure (the
        // compact profile's whole point is that a registration allocates
        // no lambdas).
        private object _lastField;
        private object _lastListField;
        private string _callTable;
        private string _resultTable;
        private string _queueTrigger;
        private string _inlineFunctionName;
        private int _inlineMinArgs = InlineShapeRules.NotDeclared;
        private int _inlineMaxArgs = InlineShapeRules.NotDeclared;

        public CompactHostMethodBuilder(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                throw new ArgumentException("methodName must be non-empty.", nameof(methodName));
            }
            _methodName = methodName;
        }

        public ICompactHostMethodBuilder<THandlers> ApiLevel(int apiLevel)
        {
            _apiLevel = apiLevel;
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> CreateInput(Func<object> factory)
        {
            _createInput = factory;
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputInt(string sqlName, Action<object, int> setter)
        {
            _lastField = ErasedScalarFields.Int(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputLong(string sqlName, Action<object, long> setter)
        {
            _lastField = ErasedScalarFields.Long(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputBool(string sqlName, Action<object, bool> setter)
        {
            _lastField = ErasedScalarFields.Bool(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputText(string sqlName, Action<object, string> setter)
        {
            _lastField = ErasedScalarFields.Text(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputBlob(string sqlName, Action<object, byte[]> setter)
        {
            _lastField = ErasedScalarFields.Blob(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputFloat(string sqlName, Action<object, float> setter)
        {
            _lastField = ErasedScalarFields.Float(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputDouble(string sqlName, Action<object, double> setter)
        {
            _lastField = ErasedScalarFields.Double(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputOptionalInt(string sqlName, Action<object, int?> setter)
        {
            _lastField = ErasedScalarFields.OptionalInt(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputOptionalLong(string sqlName, Action<object, long?> setter)
        {
            _lastField = ErasedScalarFields.OptionalLong(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputOptionalBool(string sqlName, Action<object, bool?> setter)
        {
            _lastField = ErasedScalarFields.OptionalBool(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputOptionalText(string sqlName, Action<object, string> setter)
        {
            _lastField = ErasedScalarFields.OptionalText(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputOptionalBlob(string sqlName, Action<object, byte[]> setter)
        {
            _lastField = ErasedScalarFields.OptionalBlob(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputOptionalFloat(string sqlName, Action<object, float?> setter)
        {
            _lastField = ErasedScalarFields.OptionalFloat(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputOptionalDouble(string sqlName, Action<object, double?> setter)
        {
            _lastField = ErasedScalarFields.OptionalDouble(sqlName, setter);
            _inputFields.Add((ErasedReadField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> InputList(
            string sqlName,
            Func<object> createItem,
            Action<object, IReadOnlyList<object>> assignItems,
            Action<ICompactListItemFieldsBuilder> configureItem)
        {
            var itemBuilder = new CompactListItemFieldsBuilder();
            configureItem(itemBuilder);
            List<ErasedReadField> itemFields = itemBuilder.Fields;

            var itemSchemaFields = new List<SchemaFieldModel>();
            foreach (ErasedReadField field in itemFields)
            {
                itemSchemaFields.Add(field.ToSchemaField());
            }

            var listField = new ErasedInputListField(
                sqlName, itemSchemaFields, createItem, itemFields, assignItems);
            _lastListField = listField;
            _inputListFields.Add(listField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultInt(string sqlName, Func<object, int> getter)
        {
            _lastField = ErasedScalarFields.WriteInt(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultLong(string sqlName, Func<object, long> getter)
        {
            _lastField = ErasedScalarFields.WriteLong(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultBool(string sqlName, Func<object, bool> getter)
        {
            _lastField = ErasedScalarFields.WriteBool(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultText(string sqlName, Func<object, string> getter)
        {
            _lastField = ErasedScalarFields.WriteText(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultBlob(string sqlName, Func<object, byte[]> getter)
        {
            _lastField = ErasedScalarFields.WriteBlob(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultFloat(string sqlName, Func<object, float> getter)
        {
            _lastField = ErasedScalarFields.WriteFloat(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultDouble(string sqlName, Func<object, double> getter)
        {
            _lastField = ErasedScalarFields.WriteDouble(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultOptionalInt(string sqlName, Func<object, int?> getter)
        {
            _lastField = ErasedScalarFields.WriteOptionalInt(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultOptionalLong(string sqlName, Func<object, long?> getter)
        {
            _lastField = ErasedScalarFields.WriteOptionalLong(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultOptionalBool(string sqlName, Func<object, bool?> getter)
        {
            _lastField = ErasedScalarFields.WriteOptionalBool(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultOptionalText(string sqlName, Func<object, string> getter)
        {
            _lastField = ErasedScalarFields.WriteOptionalText(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultOptionalBlob(string sqlName, Func<object, byte[]> getter)
        {
            _lastField = ErasedScalarFields.WriteOptionalBlob(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultOptionalFloat(string sqlName, Func<object, float?> getter)
        {
            _lastField = ErasedScalarFields.WriteOptionalFloat(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultOptionalDouble(string sqlName, Func<object, double?> getter)
        {
            _lastField = ErasedScalarFields.WriteOptionalDouble(sqlName, getter);
            _resultFields.Add((ErasedWriteField)_lastField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ResultList(
            string sqlName,
            Func<object, IReadOnlyList<object>> getItems,
            Action<ICompactListItemResultFieldsBuilder> configureItem)
        {
            var itemBuilder = new CompactListItemResultFieldsBuilder();
            configureItem(itemBuilder);
            List<ErasedWriteField> itemFields = itemBuilder.Fields;

            var itemSchemaFields = new List<SchemaFieldModel>();
            foreach (ErasedWriteField field in itemFields)
            {
                itemSchemaFields.Add(field.ToSchemaField());
            }

            var listField = new ErasedResultListField(
                sqlName, itemSchemaFields, getItems, itemFields);
            _lastListField = listField;
            _resultListFields.Add(listField);
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> Handler(Func<object, object, object> handler)
        {
            _handler = handler;
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> Inline(string functionName)
        {
            return Inline(functionName, InlineShapeRules.NotDeclared, InlineShapeRules.NotDeclared);
        }

        public ICompactHostMethodBuilder<THandlers> Inline(string functionName, int minArgs, int maxArgs)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                throw new ArgumentException(
                    "Method '" + _methodName + "': inline functionName must be non-empty.",
                    nameof(functionName));
            }
            _inlineFunctionName = functionName;
            _inlineMinArgs = minArgs;
            _inlineMaxArgs = maxArgs;
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> Column(string column)
        {
            SpecGuards.RequireDeclarationBefore(_lastField != null, "Column");
            var readField = _lastField as ErasedReadField;
            if (readField != null)
            {
                readField.Column = column;
            }
            else
            {
                ((ErasedWriteField)_lastField).Column = column;
            }
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> ChildTable(string childTable)
        {
            SpecGuards.RequireDeclarationBefore(_lastListField != null, "ChildTable");
            var inputList = _lastListField as ErasedInputListField;
            if (inputList != null)
            {
                inputList.ChildTable = childTable;
            }
            else
            {
                ((ErasedResultListField)_lastListField).ChildTable = childTable;
            }
            return this;
        }

        public ICompactHostMethodBuilder<THandlers> Tables(
            string callTable, string resultTable, string queueTrigger)
        {
            _callTable = callTable;
            _resultTable = resultTable;
            _queueTrigger = queueTrigger;
            return this;
        }

        public IHostMethodSpec<THandlers> Build()
        {
            if (_handler == null)
            {
                throw new InvalidOperationException(
                    "Method '" + _methodName + "' has no handler; call Handler(...) before Build().");
            }
            if (_createInput == null)
            {
                throw new InvalidOperationException(
                    "Method '" + _methodName + "' has no input factory; call CreateInput(...) before Build().");
            }
            return new ErasedSpecAdapter<THandlers>(new ErasedHostMethodSpec(
                _methodName,
                _apiLevel,
                _createInput,
                _inputFields,
                _inputListFields,
                _resultFields,
                _resultListFields,
                _handler,
                InlineShapeRules.BuildModel(
                    _methodName,
                    _inlineFunctionName,
                    _inputFields,
                    _inputListFields.Count,
                    _resultFields.Count,
                    _resultListFields.Count,
                    _inlineMinArgs,
                    _inlineMaxArgs),
                _callTable,
                _resultTable,
                _queueTrigger));
        }
    }

    internal sealed class CompactListItemFieldsBuilder : ICompactListItemFieldsBuilder
    {
        public List<ErasedReadField> Fields { get; } = new List<ErasedReadField>();

        public ICompactListItemFieldsBuilder Int(string sqlName, Action<object, int> setter)
        {
            Fields.Add(ErasedScalarFields.Int(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder Long(string sqlName, Action<object, long> setter)
        {
            Fields.Add(ErasedScalarFields.Long(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder Bool(string sqlName, Action<object, bool> setter)
        {
            Fields.Add(ErasedScalarFields.Bool(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder Text(string sqlName, Action<object, string> setter)
        {
            Fields.Add(ErasedScalarFields.Text(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder Blob(string sqlName, Action<object, byte[]> setter)
        {
            Fields.Add(ErasedScalarFields.Blob(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder Float(string sqlName, Action<object, float> setter)
        {
            Fields.Add(ErasedScalarFields.Float(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder Double(string sqlName, Action<object, double> setter)
        {
            Fields.Add(ErasedScalarFields.Double(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder OptionalInt(string sqlName, Action<object, int?> setter)
        {
            Fields.Add(ErasedScalarFields.OptionalInt(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder OptionalLong(string sqlName, Action<object, long?> setter)
        {
            Fields.Add(ErasedScalarFields.OptionalLong(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder OptionalBool(string sqlName, Action<object, bool?> setter)
        {
            Fields.Add(ErasedScalarFields.OptionalBool(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder OptionalText(string sqlName, Action<object, string> setter)
        {
            Fields.Add(ErasedScalarFields.OptionalText(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder OptionalBlob(string sqlName, Action<object, byte[]> setter)
        {
            Fields.Add(ErasedScalarFields.OptionalBlob(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder OptionalFloat(string sqlName, Action<object, float?> setter)
        {
            Fields.Add(ErasedScalarFields.OptionalFloat(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder OptionalDouble(string sqlName, Action<object, double?> setter)
        {
            Fields.Add(ErasedScalarFields.OptionalDouble(sqlName, setter));
            return this;
        }

        public ICompactListItemFieldsBuilder Column(string column)
        {
            SpecGuards.RequireDeclarationBefore(Fields.Count > 0, "Column");
            Fields[Fields.Count - 1].Column = column;
            return this;
        }
    }

    internal sealed class CompactListItemResultFieldsBuilder : ICompactListItemResultFieldsBuilder
    {
        public List<ErasedWriteField> Fields { get; } = new List<ErasedWriteField>();

        public ICompactListItemResultFieldsBuilder Int(string sqlName, Func<object, int> getter)
        {
            Fields.Add(ErasedScalarFields.WriteInt(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder Long(string sqlName, Func<object, long> getter)
        {
            Fields.Add(ErasedScalarFields.WriteLong(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder Bool(string sqlName, Func<object, bool> getter)
        {
            Fields.Add(ErasedScalarFields.WriteBool(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder Text(string sqlName, Func<object, string> getter)
        {
            Fields.Add(ErasedScalarFields.WriteText(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder Blob(string sqlName, Func<object, byte[]> getter)
        {
            Fields.Add(ErasedScalarFields.WriteBlob(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder Float(string sqlName, Func<object, float> getter)
        {
            Fields.Add(ErasedScalarFields.WriteFloat(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder Double(string sqlName, Func<object, double> getter)
        {
            Fields.Add(ErasedScalarFields.WriteDouble(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder OptionalInt(string sqlName, Func<object, int?> getter)
        {
            Fields.Add(ErasedScalarFields.WriteOptionalInt(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder OptionalLong(string sqlName, Func<object, long?> getter)
        {
            Fields.Add(ErasedScalarFields.WriteOptionalLong(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder OptionalBool(string sqlName, Func<object, bool?> getter)
        {
            Fields.Add(ErasedScalarFields.WriteOptionalBool(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder OptionalText(string sqlName, Func<object, string> getter)
        {
            Fields.Add(ErasedScalarFields.WriteOptionalText(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder OptionalBlob(string sqlName, Func<object, byte[]> getter)
        {
            Fields.Add(ErasedScalarFields.WriteOptionalBlob(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder OptionalFloat(string sqlName, Func<object, float?> getter)
        {
            Fields.Add(ErasedScalarFields.WriteOptionalFloat(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder OptionalDouble(string sqlName, Func<object, double?> getter)
        {
            Fields.Add(ErasedScalarFields.WriteOptionalDouble(sqlName, getter));
            return this;
        }

        public ICompactListItemResultFieldsBuilder Column(string column)
        {
            SpecGuards.RequireDeclarationBefore(Fields.Count > 0, "Column");
            Fields[Fields.Count - 1].Column = column;
            return this;
        }
    }
}
