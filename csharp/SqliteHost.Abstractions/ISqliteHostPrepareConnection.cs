namespace SqliteHost
{
    /// <summary>
    /// Optional prepare capability for validators and preflight checks.
    /// Not required by the core runtime.
    /// </summary>
    public interface ISqliteHostPrepareConnection : ISqliteHostConnection
    {
        ISqliteHostPreparedStatement Prepare(string sql);
    }
}
