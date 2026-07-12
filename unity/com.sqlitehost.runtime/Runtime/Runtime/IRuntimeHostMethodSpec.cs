using System;

namespace SqliteHost
{
    /// <summary>
    /// Runtime-internal contract implemented by specs built through
    /// <see cref="HostMethod"/>. The public pinned surface is only
    /// <see cref="IHostMethodSpec{THandlers}"/>.
    /// </summary>
    internal interface IRuntimeHostMethodSpec<THandlers> : IHostMethodSpec<THandlers>
    {
        SchemaMethodModel SchemaModel { get; }

        /// <summary>Inline scalar-function exposure, or null when the method is not exposed.</summary>
        InlineFunctionModel InlineFunction { get; }

        /// <summary>
        /// Builds the registerable scalar function: maps the invocation's
        /// args to the input DTO (trailing omitted = null; SQL NULL for a
        /// required field fails), invokes the handler, and returns the
        /// single result field's value. Everything thrown inside is wrapped
        /// so its message starts with the function name.
        /// </summary>
        SqliteHostScalarFunction CreateInlineFunction(THandlers handlers, Action onHandlerInvocation);

        /// <summary>
        /// Reads the parent call row + input list child rows, maps them to
        /// the input DTO, invokes the handler, and writes the result parent
        /// row (status = the configured done literal) + result list child
        /// rows.
        /// </summary>
        void ExecuteCall(
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            SqliteHostColumns columns,
            THandlers handlers,
            string callId);
    }
}
