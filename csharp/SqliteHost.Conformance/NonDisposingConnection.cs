using System;
using System.Collections.Generic;

namespace SqliteHost.Conformance
{
    /// <summary>Caller-owned connection wrapper whose Dispose is a no-op.</summary>
    public sealed class NonDisposingConnection : ISqliteHostConnection
    {
        private readonly ISqliteHostConnection _inner;

        public NonDisposingConnection(ISqliteHostConnection inner)
        {
            _inner = inner;
        }

        public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
            => _inner.Execute(sql, bindings);

        public IReadOnlyList<T> Query<T>(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, T> mapper)
            => _inner.Query(sql, bindings, mapper);

        public void Dispose()
        {
            // Intentionally left open so tests can inspect the workspace.
        }
    }
}
