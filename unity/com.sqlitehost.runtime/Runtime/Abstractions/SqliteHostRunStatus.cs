namespace SqliteHost
{
    /// <summary>Outcome of one <c>Run(script)</c>. See docs/errors.md for the code table.</summary>
    public enum SqliteHostRunStatus
    {
        Completed,
        SkippedUnsupported,
        FailedSql,
        FailedBinding,
        FailedHandler,
        FailedSchema,
        FailedValidation
    }
}
