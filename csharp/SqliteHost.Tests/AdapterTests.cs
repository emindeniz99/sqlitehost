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

        [Fact]
        public void FloatBindings_StoreAsRealAndReadBackThroughFloatAccessors()
        {
            var factory = new TestWorkspaceFactory();
            using var connection = (MicrosoftDataSqliteConnection)factory.OpenWorkspace();
            connection.Execute("CREATE TABLE t (a REAL, b REAL)", null);
            connection.Execute(
                "INSERT INTO t (a, b) VALUES (:f32, :f64)",
                new[]
                {
                    new SqliteHostBinding("f32", SqliteHostBindingValue.Float32(0.75f)),
                    new SqliteHostBinding("f64", SqliteHostBindingValue.Float64(98.5))
                });

            var rows = connection.Query(
                "SELECT a, b, typeof(a), typeof(b) FROM t",
                null,
                row => new
                {
                    F32 = row.GetFloat32(0),
                    F64 = row.GetFloat64(1),
                    AType = row.GetText(2),
                    BType = row.GetText(3)
                });

            var row0 = Assert.Single(rows);
            Assert.Equal(0.75f, row0.F32);     // dyadic-exact single round trip
            Assert.Equal(98.5, row0.F64);
            Assert.Equal("real", row0.AType);  // Float32 binds as a double parameter
            Assert.Equal("real", row0.BType);
        }

        [Fact]
        public void GetFloat32AndGetFloat64_ReadTheSameRealColumn()
        {
            var factory = new TestWorkspaceFactory();
            using var connection = (MicrosoftDataSqliteConnection)factory.OpenWorkspace();
            connection.Execute("CREATE TABLE t (a REAL)", null);
            connection.Execute(
                "INSERT INTO t (a) VALUES (:v)",
                new[]
                {
                    new SqliteHostBinding("v", SqliteHostBindingValue.Float64(-12.25))
                });

            var rows = connection.Query(
                "SELECT a FROM t",
                null,
                row => new { F32 = row.GetFloat32(0), F64 = row.GetFloat64(0) });

            var row0 = Assert.Single(rows);
            Assert.Equal(-12.25f, row0.F32);
            Assert.Equal(-12.25, row0.F64);
        }

        [Fact]
        public void SameBareName_BindsAllPrefixedOccurrences()
        {
            var factory = new TestWorkspaceFactory();
            using var connection = (MicrosoftDataSqliteConnection)factory.OpenWorkspace();
            connection.Execute("CREATE TABLE scratch (a INTEGER, b INTEGER)", null);
            connection.Execute(
                "INSERT INTO scratch (a, b) VALUES (:v, $v)",
                new[]
                {
                    new SqliteHostBinding("v", SqliteHostBindingValue.Int32(21))
                });

            var rows = connection.Query(
                "SELECT a, b FROM scratch",
                null,
                row => row.GetInt64(0) + "," + row.GetInt64(1));
            Assert.Equal(new[] { "21,21" }, rows);
        }
    }
}
