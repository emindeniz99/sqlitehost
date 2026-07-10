using System;

namespace SqliteHost
{
    /// <summary>
    /// Thrown by adapters to surface native SQLite failures (prepare, step,
    /// bind, schema) without masking them (docs/adapter-contract.md). The
    /// runtime copies <see cref="SqliteErrorCode"/> into
    /// <see cref="SqliteHostRunResult.SqliteErrorCode"/> when a failure maps
    /// to a structured result.
    /// </summary>
    public class SqliteHostAdapterException : Exception
    {
        public SqliteHostAdapterException(string message, int sqliteErrorCode, Exception innerException)
            : base(message, innerException)
        {
            SqliteErrorCode = sqliteErrorCode;
        }

        /// <summary>Native SQLite result code when available; 0 = not available.</summary>
        public int SqliteErrorCode { get; }
    }
}
