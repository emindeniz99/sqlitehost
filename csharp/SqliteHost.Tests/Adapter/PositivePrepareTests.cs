using Example.Game.Generated;
using Microsoft.Data.Sqlite;
using Xunit;

namespace SqliteHost.Tests.Adapter
{
    /// <summary>
    /// POSITIVE prepare-only canaries for the compatibility matrix: SQL
    /// constructs SqliteHost scripts are allowed to use (all introduced by
    /// 3.8.3 or earlier, well below the 3.19.3 floor) must prepare against
    /// the generated workspace schema on the CURRENT engine — including
    /// every matrix version down to 3.9.0. Prepare-only keeps the canaries
    /// independent of data and honors the matrix's "no execution needed to
    /// detect a parser gap" property. The generated DDL + trigger themselves
    /// are exercised (executed, not just prepared) by every integration
    /// fixture run on the same engine.
    /// </summary>
    public class PositivePrepareTests
    {
        private static MicrosoftDataSqliteConnection OpenGeneratedWorkspace()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var workspace = new MicrosoftDataSqliteConnection(connection);
            foreach (string statement in GeneratedHostDefinition.Build().GenerateSchemaStatements())
            {
                workspace.Execute(statement, null);
            }
            return workspace;
        }

        [Theory]
        // recursive CTE (3.8.3)
        [InlineData("WITH RECURSIVE cnt(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM cnt WHERE x < 5) SELECT x FROM cnt")]
        // CASE expression
        [InlineData("SELECT CASE WHEN status = 'done' THEN 1 ELSE 0 END FROM pending_host_calls")]
        // scalar subquery
        [InlineData("SELECT (SELECT COUNT(*) FROM pending_host_calls) AS pending")]
        // EXISTS
        [InlineData("SELECT EXISTS (SELECT 1 FROM script_inputs WHERE name = :name)")]
        // multi-row VALUES (3.7.11)
        [InlineData("INSERT INTO call_get_value (call_id, input_key) VALUES (:a, 'k1'), (:b, 'k2')")]
        // printf() (3.8.3)
        [InlineData("SELECT printf('%d', 1)")]
        public void AllowedConstructs_PrepareAgainstTheGeneratedSchema(string sql)
        {
            using MicrosoftDataSqliteConnection workspace = OpenGeneratedWorkspace();
            using ISqliteHostPreparedStatement statement = workspace.Prepare(sql);
            Assert.NotNull(statement.ParameterNames);   // prepared, nothing executed
        }
    }
}
