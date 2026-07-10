using SqliteHost.Tests.Adapter;
using Xunit;

namespace SqliteHost.Tests
{
    public class AdapterTests
    {
        [Fact]
        public void Prepare_ExposesParameterNames_WithoutExecuting()
        {
            var factory = new TestWorkspaceFactory();
            using var connection = (MicrosoftDataSqliteConnection)factory.OpenWorkspace();
            connection.Execute("CREATE TABLE t (a INTEGER, b TEXT)", null);

            using var statement = connection.Prepare(
                "INSERT INTO t (a, b) VALUES (:alpha, @beta)");

            Assert.Equal(new[] { ":alpha", "@beta" }, statement.ParameterNames);
            var rows = connection.Query("SELECT a FROM t", null, row => row.GetInt64(0));
            Assert.Empty(rows);   // prepare-only: nothing was executed
        }

        [Fact]
        public void Prepare_ThrowsOnInvalidSql()
        {
            var factory = new TestWorkspaceFactory();
            using var connection = (MicrosoftDataSqliteConnection)factory.OpenWorkspace();
            Assert.ThrowsAny<System.Exception>(() => connection.Prepare("SELECT FROM nothing ("));
        }

        [Fact]
        public void ParameterPrefixes_AllBindByBareName()
        {
            var factory = new TestWorkspaceFactory();
            using var connection = (MicrosoftDataSqliteConnection)factory.OpenWorkspace();
            connection.Execute("CREATE TABLE t (a INTEGER, b INTEGER, c INTEGER)", null);
            connection.Execute(
                "INSERT INTO t (a, b, c) VALUES (:one, @two, $three)",
                new[]
                {
                    new SqliteHostBinding("one", SqliteHostBindingValue.Int64(1)),
                    new SqliteHostBinding("two", SqliteHostBindingValue.Int64(2)),
                    new SqliteHostBinding("three", SqliteHostBindingValue.Int64(3))
                });

            var rows = connection.Query(
                "SELECT a, b, c FROM t",
                null,
                row => row.GetInt64(0) + "," + row.GetInt64(1) + "," + row.GetInt64(2));
            Assert.Equal(new[] { "1,2,3" }, rows);
        }
    }
}
