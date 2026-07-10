namespace SqliteHost
{
    /// <summary>Opens the SQLite workspace used for one run. The runtime disposes what it opens.</summary>
    public interface ISqliteHostConnectionFactory
    {
        ISqliteHostConnection OpenWorkspace();
    }
}
