using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>Small shared helpers for runtime-issued SQL.</summary>
    internal static class RuntimeSql
    {
        public static readonly IReadOnlyList<SqliteHostBinding> NoBindings = new List<SqliteHostBinding>();

        public static IReadOnlyList<SqliteHostBinding> CallIdBindings(string callId)
        {
            return new List<SqliteHostBinding>
            {
                new SqliteHostBinding("callId", SqliteHostBindingValue.Text(callId))
            };
        }
    }
}
