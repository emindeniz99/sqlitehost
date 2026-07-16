using System;

namespace SqliteHost
{
    internal sealed class HostMethodSpecBuilder<THandlers, TInput, TResult>
        : IHostMethodSpecBuilder<THandlers, TInput, TResult>
        where TInput : new()
        where TResult : class
    {
        private readonly string _methodName;
        private readonly InputFieldsBuilder<TInput> _inputs = new InputFieldsBuilder<TInput>();
        private readonly ResultFieldsBuilder<TResult> _results = new ResultFieldsBuilder<TResult>();
        private int _apiLevel = 1;
        private Func<THandlers, TInput, TResult> _handler;
        private string _inlineFunctionName;

        public HostMethodSpecBuilder(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                throw new ArgumentException("methodName must be non-empty.", nameof(methodName));
            }
            SpecGuards.RequireReferenceDtoTypes(typeof(TInput), typeof(TResult), methodName);
            _methodName = methodName;
        }

        public IHostMethodSpecBuilder<THandlers, TInput, TResult> ApiLevel(int apiLevel)
        {
            _apiLevel = apiLevel;
            return this;
        }

        public IHostMethodSpecBuilder<THandlers, TInput, TResult> Inputs(
            Action<IInputFieldsBuilder<TInput>> configure)
        {
            configure(_inputs);
            return this;
        }

        public IHostMethodSpecBuilder<THandlers, TInput, TResult> Results(
            Action<IResultFieldsBuilder<TResult>> configure)
        {
            configure(_results);
            return this;
        }

        public IHostMethodSpecBuilder<THandlers, TInput, TResult> Handler(
            Func<THandlers, TInput, TResult> handler)
        {
            _handler = handler;
            return this;
        }

        public IHostMethodSpecBuilder<THandlers, TInput, TResult> Inline(string functionName)
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
            Func<THandlers, TInput, TResult> handler = _handler;
            return new ErasedSpecAdapter<THandlers>(new ErasedHostMethodSpec(
                _methodName,
                _apiLevel,
                delegate { return (object)new TInput(); },
                _inputs.Fields,
                _inputs.ListFields,
                _results.Fields,
                _results.ListFields,
                delegate(object handlers, object input) { return handler((THandlers)handlers, (TInput)input); },
                InlineShapeRules.BuildModel(
                    _methodName,
                    _inlineFunctionName,
                    _inputs.Fields,
                    _inputs.ListFields.Count,
                    _results.Fields.Count,
                    _results.ListFields.Count)));
        }
    }
}
