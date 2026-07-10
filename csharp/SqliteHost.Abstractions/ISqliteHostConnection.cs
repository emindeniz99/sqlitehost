using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Minimal SQLite connection adapter the runtime executes against.
    /// The core packages never depend on a specific SQLite library.
    /// </summary>
    public interface ISqliteHostConnection : IDisposable
    {
        void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings);

        IReadOnlyList<T> Query<T>(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, T> mapper);
    }
}
