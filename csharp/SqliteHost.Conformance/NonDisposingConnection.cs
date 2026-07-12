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

        /// <summary>
        /// Wraps the connection without hiding its optional scalar-function
        /// capability: a function-capable inner connection gets a wrapper
        /// that still implements ISqliteHostScalarFunctionConnection.
        /// </summary>
        public static ISqliteHostConnection Wrap(ISqliteHostConnection inner)
        {
            var functionConnection = inner as ISqliteHostScalarFunctionConnection;
            return functionConnection != null
                ? new NonDisposingScalarFunctionConnection(functionConnection)
                : (ISqliteHostConnection)new NonDisposingConnection(inner);
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

    /// <summary>
    /// Caller-owned function-capable connection wrapper whose Dispose is a
    /// no-op (see <see cref="NonDisposingConnection.Wrap"/>).
    /// </summary>
    public sealed class NonDisposingScalarFunctionConnection : ISqliteHostScalarFunctionConnection
    {
        private readonly ISqliteHostScalarFunctionConnection _inner;

        public NonDisposingScalarFunctionConnection(ISqliteHostScalarFunctionConnection inner)
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

        public void RegisterScalarFunction(SqliteHostScalarFunction function)
            => _inner.RegisterScalarFunction(function);

        public void Dispose()
        {
            // Intentionally left open so tests can inspect the workspace.
        }
    }
}
