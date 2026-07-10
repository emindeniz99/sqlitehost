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
                _handler);
        }
    }
}
