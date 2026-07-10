using System;

namespace SqliteHost
{
    /// <summary>Queue row exists but the parent call row is missing (error code call-row-missing).</summary>
    internal sealed class SqliteHostCallRowMissingException : Exception
    {
        public SqliteHostCallRowMissingException(string message)
            : base(message)
        {
        }
    }

    /// <summary>A consumer handler threw (error code handler-error).</summary>
    internal sealed class SqliteHostHandlerException : Exception
    {
        public SqliteHostHandlerException(Exception inner)
            : base(inner.Message, inner)
        {
        }
    }

    /// <summary>Writing result rows failed (error code result-write-error).</summary>
    internal sealed class SqliteHostResultWriteException : Exception
    {
        public SqliteHostResultWriteException(Exception inner)
            : base(inner.Message, inner)
        {
        }
    }
}
