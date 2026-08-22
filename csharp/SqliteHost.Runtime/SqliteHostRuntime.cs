namespace SqliteHost
{
    /// <summary>
    /// Executes parsed scripts against a temporary SQLite workspace and
    /// bridges call_* inserts to typed handler invocations (pinned
    /// lifecycle, docs/csharp-api.md). Never throws for
    /// script-level problems; returns a structured result instead.
    /// The generic parameter types the public API only; the whole engine
    /// lives in the non-generic <see cref="SqliteHostRuntimeCore"/>.
    /// </summary>
    public sealed class SqliteHostRuntime<THandlers>
    {
        private readonly SqliteHostRuntimeCore _core;

        public SqliteHostRuntime(
            ISqliteHostConnectionFactory connectionFactory,
            SqliteHostDefinition<THandlers> hostDefinition,
            THandlers handlers,
            SqliteHostRuntimeOptions options)
        {
            _core = new SqliteHostRuntimeCore(
                connectionFactory,
                hostDefinition == null ? null : hostDefinition.Core,
                handlers,
                options);
        }

        public SqliteHostRunResult Run(SqliteHostScript script)
        {
            return _core.Run(script);
        }

        /// <summary>
        /// Opens a workspace, checks the actual sqlite_version() against the
        /// definition's MinSqliteVersionNumber, disposes, and returns the
        /// outcome — lets hosts fail fast at init time instead of at the
        /// first Run (docs/csharp-api.md). No schema is created.
        /// </summary>
        public SqliteHostRunResult ValidateEnvironment()
        {
            return _core.ValidateEnvironment();
        }
    }
}
