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
            return new HostMethodSpec<THandlers, TInput, TResult>(
                _methodName,
                _apiLevel,
                _inputs.Fields,
                _inputs.ListFields,
                _results.Fields,
                _results.ListFields,
                _handler,
                BuildInlineModel());
        }

        /// <summary>
        /// Re-checks the inline eligibility shape rules
        /// (docs/proposals/inline-host-functions.md) fail-loud: scalar-only
        /// input, exactly one scalar result, no lists, optional input
        /// fields trailing. Null when the method is not inline-exposed.
        /// </summary>
        private InlineFunctionModel BuildInlineModel()
        {
            if (_inlineFunctionName == null)
            {
                return null;
            }
            if (_inputs.ListFields.Count > 0)
            {
                throw new InvalidOperationException(
                    "Method '" + _methodName + "' cannot be exposed as inline function '"
                    + _inlineFunctionName + "': the input must have scalar fields only (no lists).");
            }
            if (_results.ListFields.Count > 0)
            {
                throw new InvalidOperationException(
                    "Method '" + _methodName + "' cannot be exposed as inline function '"
                    + _inlineFunctionName + "': the result must have scalar fields only (no lists).");
            }
            if (_results.Fields.Count != 1)
            {
                throw new InvalidOperationException(
                    "Method '" + _methodName + "' cannot be exposed as inline function '"
                    + _inlineFunctionName + "': the result must have exactly one scalar field (found "
                    + _results.Fields.Count + ").");
            }
            int requiredCount = 0;
            bool sawOptional = false;
            foreach (ScalarReadField<TInput> field in _inputs.Fields)
            {
                if (field.Optional)
                {
                    sawOptional = true;
                    continue;
                }
                if (sawOptional)
                {
                    throw new InvalidOperationException(
                        "Method '" + _methodName + "' cannot be exposed as inline function '"
                        + _inlineFunctionName + "': required input field '" + field.SqlName
                        + "' is declared after an optional field (optional fields must be trailing).");
                }
                requiredCount++;
            }
            return new InlineFunctionModel(_inlineFunctionName, requiredCount, _inputs.Fields.Count);
        }
    }
}
