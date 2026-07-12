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

        /// <summary>
        /// Exposes the method as an inline scalar function (feature
        /// inlineFunctions; generated code emits this between .Results and
        /// .Handler). Build() re-checks the shape rules fail-loud:
        /// scalar-only input, exactly one scalar result, no lists, optional
        /// input fields trailing. mutates:false semantics are the
        /// generator's duty.
        /// </summary>
        IHostMethodSpecBuilder<THandlers, TInput, TResult> Inline(string functionName);

        IHostMethodSpec<THandlers> Build();
    }
}
