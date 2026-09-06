using System;

namespace SqliteHost
{
    /// <summary>Fluent descriptor for one host method (the generated-code target, docs/csharp-api.md).</summary>
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
        /// <summary>
        /// Same, with the arity the manifest already carries. Generated
        /// code uses this overload; the one-argument form derives the
        /// arity from the declared input fields instead, which is what a
        /// hand-written definition needs.
        /// </summary>
        IHostMethodSpecBuilder<THandlers, TInput, TResult> Inline(
            string functionName, int minArgs, int maxArgs);

        /// <summary>
        /// Physical call/result/queue-trigger table names of this method.
        /// Generated code emits the manifest's resolved names so the host
        /// reads and writes exactly the tables its own schema SQL creates;
        /// omit the call and the runtime derives all three from the host
        /// naming (docs/naming.md).
        /// </summary>
        IHostMethodSpecBuilder<THandlers, TInput, TResult> Tables(
            string callTable, string resultTable, string queueTrigger);

        IHostMethodSpec<THandlers> Build();
    }
}
