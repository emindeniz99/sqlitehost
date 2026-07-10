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

        /// <summary>
        /// Reads the parent call row + input list child rows, maps them to
        /// the input DTO, invokes the handler, and writes the result parent
        /// row (status 'done') + result list child rows.
        /// </summary>
        void ExecuteCall(
            ISqliteHostConnection connection,
            SqliteHostNaming naming,
            THandlers handlers,
            string callId);
    }
}
