using System.Collections.Generic;

namespace SqliteHost.Tests.TestSupport
{
    /// <summary>Helpers for building parsed scripts in tests.</summary>
    public static class Scripts
    {
        public const string Engine = "sqlite-host-v1";

        public static SqliteHostScript New(params SqliteHostStep[] steps)
        {
            return new SqliteHostScript
            {
                Engine = Engine,
                ScriptId = "test-script",
                RequiredApiLevel = 1,
                Steps = new List<SqliteHostStep>(steps)
            };
        }

        public static SqliteHostStep Step(string id, params SqliteHostStatement[] statements)
        {
            return new SqliteHostStep
            {
                Id = id,
                Statements = new List<SqliteHostStatement>(statements)
            };
        }

        public static SqliteHostStatement Statement(string sql)
        {
            return new SqliteHostStatement { Sql = sql };
        }

        public static SqliteHostStatement Statement(
            string sql,
            params (string Name, SqliteHostBindingValue Value)[] bindings)
        {
            var statement = new SqliteHostStatement
            {
                Sql = sql,
                Bindings = new Dictionary<string, SqliteHostBindingValue>()
            };
            foreach ((string name, SqliteHostBindingValue value) in bindings)
            {
                statement.Bindings.Add(name, value);
            }
            return statement;
        }
    }
}
