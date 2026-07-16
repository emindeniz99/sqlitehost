using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Entry point of the ultra method descriptor API — the minimum-size
    /// generated-code target (`--profile ultra`). No DTO types exist at
    /// all: handlers read a <see cref="SqliteHostUltraCall"/> and return a
    /// <see cref="SqliteHostUltraResult"/>, and the declaration lists field
    /// names and kinds only. Registering another method therefore adds
    /// nothing but data (plus one interface method on the handlers type).
    /// The trade against the classic/compact profiles is compile-time
    /// typing of the per-method payloads; the wire contract, schema, and
    /// runtime behavior are identical — all profiles lower to the same
    /// erased execution core, and the declared shape is enforced fail-loud
    /// after each handler invocation.
    /// </summary>
    public static class UltraHostMethod
    {
        public static IUltraHostMethodBuilder<THandlers> For<THandlers>(string methodName)
        {
            return new UltraHostMethodBuilder<THandlers>(methodName);
        }
    }

    /// <summary>
    /// Flat ultra descriptor for one host method: declared field names and
    /// kinds, the handler, and optionally the inline exposure. `Input*`/
    /// `Result*` mirror the classic field kinds one-to-one.
    /// </summary>
    public interface IUltraHostMethodBuilder<THandlers>
    {
        IUltraHostMethodBuilder<THandlers> ApiLevel(int apiLevel);

        IUltraHostMethodBuilder<THandlers> InputInt(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputLong(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputBool(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputText(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputBlob(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputFloat(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputDouble(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputOptionalInt(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputOptionalLong(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputOptionalBool(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputOptionalText(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputOptionalBlob(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputOptionalFloat(string sqlName);
        IUltraHostMethodBuilder<THandlers> InputOptionalDouble(string sqlName);

        /// <summary>One input list&lt;object&gt; field; configureItem declares the item columns.</summary>
        IUltraHostMethodBuilder<THandlers> InputList(
            string sqlName,
            Action<IUltraListItemFieldsBuilder> configureItem);

        IUltraHostMethodBuilder<THandlers> ResultInt(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultLong(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultBool(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultText(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultBlob(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultFloat(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultDouble(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultOptionalInt(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultOptionalLong(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultOptionalBool(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultOptionalText(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultOptionalBlob(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultOptionalFloat(string sqlName);
        IUltraHostMethodBuilder<THandlers> ResultOptionalDouble(string sqlName);

        /// <summary>One result list&lt;object&gt; field; configureItem declares the item columns.</summary>
        IUltraHostMethodBuilder<THandlers> ResultList(
            string sqlName,
            Action<IUltraListItemFieldsBuilder> configureItem);

        /// <summary>Handler invocation: (handlers, call) → result (required).</summary>
        IUltraHostMethodBuilder<THandlers> Handler(
            Func<object, SqliteHostUltraCall, SqliteHostUltraResult> handler);

        /// <summary>
        /// Exposes the method as an inline scalar function (feature
        /// inlineFunctions). Build() re-checks the shape rules fail-loud
        /// with the same rules and messages as the classic profile.
        /// </summary>
        IUltraHostMethodBuilder<THandlers> Inline(string functionName);

        IHostMethodSpec<THandlers> Build();
    }

    /// <summary>Item columns of an ultra list field (declarations only, no accessors).</summary>
    public interface IUltraListItemFieldsBuilder
    {
        IUltraListItemFieldsBuilder Int(string sqlName);
        IUltraListItemFieldsBuilder Long(string sqlName);
        IUltraListItemFieldsBuilder Bool(string sqlName);
        IUltraListItemFieldsBuilder Text(string sqlName);
        IUltraListItemFieldsBuilder Blob(string sqlName);
        IUltraListItemFieldsBuilder Float(string sqlName);
        IUltraListItemFieldsBuilder Double(string sqlName);
        IUltraListItemFieldsBuilder OptionalInt(string sqlName);
        IUltraListItemFieldsBuilder OptionalLong(string sqlName);
        IUltraListItemFieldsBuilder OptionalBool(string sqlName);
        IUltraListItemFieldsBuilder OptionalText(string sqlName);
        IUltraListItemFieldsBuilder OptionalBlob(string sqlName);
        IUltraListItemFieldsBuilder OptionalFloat(string sqlName);
        IUltraListItemFieldsBuilder OptionalDouble(string sqlName);
    }

    /// <summary>One declared ultra field: name, kind, optionality.</summary>
    internal sealed class UltraFieldDecl
    {
        public UltraFieldDecl(string sqlName, HostScalarType scalarType, bool optional)
        {
            SqlName = sqlName;
            ScalarType = scalarType;
            Optional = optional;
        }

        public string SqlName { get; }
        public HostScalarType ScalarType { get; }
        public bool Optional { get; }
    }

    internal sealed class UltraListItemFieldsBuilder : IUltraListItemFieldsBuilder
    {
        public List<UltraFieldDecl> Fields { get; } = new List<UltraFieldDecl>();

        public IUltraListItemFieldsBuilder Int(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Int32, false));
            return this;
        }

        public IUltraListItemFieldsBuilder Long(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Int64, false));
            return this;
        }

        public IUltraListItemFieldsBuilder Bool(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Boolean, false));
            return this;
        }

        public IUltraListItemFieldsBuilder Text(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.String, false));
            return this;
        }

        public IUltraListItemFieldsBuilder Blob(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Bytes, false));
            return this;
        }

        public IUltraListItemFieldsBuilder Float(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Float32, false));
            return this;
        }

        public IUltraListItemFieldsBuilder Double(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Float64, false));
            return this;
        }

        public IUltraListItemFieldsBuilder OptionalInt(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Int32, true));
            return this;
        }

        public IUltraListItemFieldsBuilder OptionalLong(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Int64, true));
            return this;
        }

        public IUltraListItemFieldsBuilder OptionalBool(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Boolean, true));
            return this;
        }

        public IUltraListItemFieldsBuilder OptionalText(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.String, true));
            return this;
        }

        public IUltraListItemFieldsBuilder OptionalBlob(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Bytes, true));
            return this;
        }

        public IUltraListItemFieldsBuilder OptionalFloat(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Float32, true));
            return this;
        }

        public IUltraListItemFieldsBuilder OptionalDouble(string sqlName)
        {
            Fields.Add(new UltraFieldDecl(sqlName, HostScalarType.Float64, true));
            return this;
        }
    }

    internal sealed class UltraHostMethodBuilder<THandlers> : IUltraHostMethodBuilder<THandlers>
    {
        private readonly string _methodName;
        private readonly List<UltraFieldDecl> _inputFields = new List<UltraFieldDecl>();
        private readonly List<KeyValuePair<string, List<UltraFieldDecl>>> _inputLists =
            new List<KeyValuePair<string, List<UltraFieldDecl>>>();
        private readonly List<UltraFieldDecl> _resultFields = new List<UltraFieldDecl>();
        private readonly List<KeyValuePair<string, List<UltraFieldDecl>>> _resultLists =
            new List<KeyValuePair<string, List<UltraFieldDecl>>>();
        private int _apiLevel = 1;
        private Func<object, SqliteHostUltraCall, SqliteHostUltraResult> _handler;
        private string _inlineFunctionName;

        public UltraHostMethodBuilder(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                throw new ArgumentException("methodName must be non-empty.", nameof(methodName));
            }
            _methodName = methodName;
        }

        public IUltraHostMethodBuilder<THandlers> ApiLevel(int apiLevel)
        {
            _apiLevel = apiLevel;
            return this;
        }

        public IUltraHostMethodBuilder<THandlers> InputInt(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Int32, false);
        }

        public IUltraHostMethodBuilder<THandlers> InputLong(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Int64, false);
        }

        public IUltraHostMethodBuilder<THandlers> InputBool(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Boolean, false);
        }

        public IUltraHostMethodBuilder<THandlers> InputText(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.String, false);
        }

        public IUltraHostMethodBuilder<THandlers> InputBlob(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Bytes, false);
        }

        public IUltraHostMethodBuilder<THandlers> InputFloat(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Float32, false);
        }

        public IUltraHostMethodBuilder<THandlers> InputDouble(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Float64, false);
        }

        public IUltraHostMethodBuilder<THandlers> InputOptionalInt(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Int32, true);
        }

        public IUltraHostMethodBuilder<THandlers> InputOptionalLong(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Int64, true);
        }

        public IUltraHostMethodBuilder<THandlers> InputOptionalBool(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Boolean, true);
        }

        public IUltraHostMethodBuilder<THandlers> InputOptionalText(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.String, true);
        }

        public IUltraHostMethodBuilder<THandlers> InputOptionalBlob(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Bytes, true);
        }

        public IUltraHostMethodBuilder<THandlers> InputOptionalFloat(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Float32, true);
        }

        public IUltraHostMethodBuilder<THandlers> InputOptionalDouble(string sqlName)
        {
            return AddInput(sqlName, HostScalarType.Float64, true);
        }

        public IUltraHostMethodBuilder<THandlers> InputList(
            string sqlName,
            Action<IUltraListItemFieldsBuilder> configureItem)
        {
            var itemBuilder = new UltraListItemFieldsBuilder();
            configureItem(itemBuilder);
            _inputLists.Add(new KeyValuePair<string, List<UltraFieldDecl>>(sqlName, itemBuilder.Fields));
            return this;
        }

        public IUltraHostMethodBuilder<THandlers> ResultInt(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Int32, false);
        }

        public IUltraHostMethodBuilder<THandlers> ResultLong(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Int64, false);
        }

        public IUltraHostMethodBuilder<THandlers> ResultBool(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Boolean, false);
        }

        public IUltraHostMethodBuilder<THandlers> ResultText(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.String, false);
        }

        public IUltraHostMethodBuilder<THandlers> ResultBlob(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Bytes, false);
        }

        public IUltraHostMethodBuilder<THandlers> ResultFloat(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Float32, false);
        }

        public IUltraHostMethodBuilder<THandlers> ResultDouble(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Float64, false);
        }

        public IUltraHostMethodBuilder<THandlers> ResultOptionalInt(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Int32, true);
        }

        public IUltraHostMethodBuilder<THandlers> ResultOptionalLong(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Int64, true);
        }

        public IUltraHostMethodBuilder<THandlers> ResultOptionalBool(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Boolean, true);
        }

        public IUltraHostMethodBuilder<THandlers> ResultOptionalText(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.String, true);
        }

        public IUltraHostMethodBuilder<THandlers> ResultOptionalBlob(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Bytes, true);
        }

        public IUltraHostMethodBuilder<THandlers> ResultOptionalFloat(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Float32, true);
        }

        public IUltraHostMethodBuilder<THandlers> ResultOptionalDouble(string sqlName)
        {
            return AddResult(sqlName, HostScalarType.Float64, true);
        }

        public IUltraHostMethodBuilder<THandlers> ResultList(
            string sqlName,
            Action<IUltraListItemFieldsBuilder> configureItem)
        {
            var itemBuilder = new UltraListItemFieldsBuilder();
            configureItem(itemBuilder);
            _resultLists.Add(new KeyValuePair<string, List<UltraFieldDecl>>(sqlName, itemBuilder.Fields));
            return this;
        }

        public IUltraHostMethodBuilder<THandlers> Handler(
            Func<object, SqliteHostUltraCall, SqliteHostUltraResult> handler)
        {
            _handler = handler;
            return this;
        }

        public IUltraHostMethodBuilder<THandlers> Inline(string functionName)
        {
            if (string.IsNullOrEmpty(functionName))
            {
                throw new ArgumentException(
                    "Method '" + _methodName + "': inline functionName must be non-empty.",
                    nameof(functionName));
            }
            _inlineFunctionName = functionName;
            return this;
        }

        public IHostMethodSpec<THandlers> Build()
        {
            if (_handler == null)
            {
                throw new InvalidOperationException(
                    "Method '" + _methodName + "' has no handler; call Handler(...) before Build().");
            }
            var inputFields = new List<ErasedReadField>(_inputFields.Count);
            var fieldNames = new List<string>(_inputFields.Count);
            foreach (UltraFieldDecl decl in _inputFields)
            {
                inputFields.Add(UltraFields.ReadField(decl));
                fieldNames.Add(decl.SqlName);
            }

            var inputListFields = new List<ErasedInputListField>(_inputLists.Count);
            var listNames = new List<string>(_inputLists.Count);
            foreach (KeyValuePair<string, List<UltraFieldDecl>> list in _inputLists)
            {
                inputListFields.Add(UltraFields.InputList(list.Key, list.Value));
                listNames.Add(list.Key);
            }

            var resultFields = new List<ErasedWriteField>(_resultFields.Count);
            foreach (UltraFieldDecl decl in _resultFields)
            {
                resultFields.Add(UltraFields.WriteField(decl));
            }

            var resultListFields = new List<ErasedResultListField>(_resultLists.Count);
            foreach (KeyValuePair<string, List<UltraFieldDecl>> list in _resultLists)
            {
                resultListFields.Add(UltraFields.ResultList(list.Key, list.Value));
            }

            var shape = new UltraResultShape(_methodName, _resultFields, _resultLists);
            Func<object, SqliteHostUltraCall, SqliteHostUltraResult> handler = _handler;
            string methodName = _methodName;

            return new ErasedSpecAdapter<THandlers>(new ErasedHostMethodSpec(
                _methodName,
                _apiLevel,
                UltraFields.CallFactory(fieldNames, listNames),
                inputFields,
                inputListFields,
                resultFields,
                resultListFields,
                delegate(object handlers, object input)
                {
                    SqliteHostUltraResult result = handler(handlers, (SqliteHostUltraCall)input);
                    if (result == null)
                    {
                        throw new InvalidOperationException(
                            "Method '" + methodName
                            + "': the handler returned null instead of a SqliteHostUltraResult.");
                    }
                    shape.Validate(result);
                    return result;
                },
                InlineShapeRules.BuildModel(
                    _methodName,
                    _inlineFunctionName,
                    inputFields,
                    inputListFields.Count,
                    resultFields.Count,
                    resultListFields.Count)));
        }

        private IUltraHostMethodBuilder<THandlers> AddInput(string sqlName, HostScalarType type, bool optional)
        {
            _inputFields.Add(new UltraFieldDecl(sqlName, type, optional));
            return this;
        }

        private IUltraHostMethodBuilder<THandlers> AddResult(string sqlName, HostScalarType type, bool optional)
        {
            _resultFields.Add(new UltraFieldDecl(sqlName, type, optional));
            return this;
        }
    }
}
