using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Typed convenience over <see cref="ISqliteHostConnection.Query"/>.
    /// The interface method is deliberately non-generic: a generic method
    /// on an interface (generic virtual method) forces AOT compilers to
    /// carry the whole dynamic type-loader (~250 KB measured under
    /// NativeAOT — see docs/compatibility.md, App size). This extension
    /// restores the ergonomic typed shape for adapter consumers and tests;
    /// the runtime itself always calls the erased interface method.
    /// </summary>
    public static class SqliteHostConnectionExtensions
    {
        public static IReadOnlyList<T> Query<T>(
            this ISqliteHostConnection connection,
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, T> mapper)
        {
            IReadOnlyList<object> rows = connection.QueryRows(
                sql,
                bindings,
                delegate(ISqliteHostRow row) { return (object)mapper(row); });
            var typed = new List<T>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                typed.Add((T)rows[i]);
            }
            return typed;
        }
    }
}
