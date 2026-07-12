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

    /// <summary>
    /// An inline scalar-function argument violated the signature mapping
    /// (SQL NULL for a required field, or a value that cannot be read as
    /// the field's scalar type). Mirrors the NOT NULL call-table contract.
    /// </summary>
    internal sealed class SqliteHostInlineArgumentException : Exception
    {
        public SqliteHostInlineArgumentException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Wraps everything thrown inside an inline scalar function so the
    /// message the adapter reports through the SQL error channel starts
    /// with the function name — the runtime resolves the failed Method
    /// from it when mapping the SQLITEHOST_HANDLER_ERROR: marker back to
    /// FailedHandler/handler-error.
    /// </summary>
    internal sealed class SqliteHostInlineFunctionException : Exception
    {
        public SqliteHostInlineFunctionException(string functionName, Exception inner)
            : base(functionName + ": " + inner.Message, inner)
        {
        }
    }
}
