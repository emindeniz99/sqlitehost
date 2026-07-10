using System;

namespace SqliteHost
{
    /// <summary>Fluent descriptor for one host method (the generated-code target, plan §12.2).</summary>
    public interface IHostMethodSpecBuilder<THandlers, TInput, TResult>
    {
        IHostMethodSpecBuilder<THandlers, TInput, TResult> ApiLevel(int apiLevel);

        IHostMethodSpecBuilder<THandlers, TInput, TResult> Inputs(
            Action<IInputFieldsBuilder<TInput>> configure);

        IHostMethodSpecBuilder<THandlers, TInput, TResult> Results(
            Action<IResultFieldsBuilder<TResult>> configure);

        IHostMethodSpecBuilder<THandlers, TInput, TResult> Handler(
            Func<THandlers, TInput, TResult> handler);

        IHostMethodSpec<THandlers> Build();
    }
}
