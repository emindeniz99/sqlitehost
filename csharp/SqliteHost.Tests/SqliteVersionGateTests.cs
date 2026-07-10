using System;
using System.Collections.Generic;
using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// The workspace version gate (docs/errors.md sqlite-version-too-low):
    /// the first workspace open of a runtime instance checks the actual
    /// sqlite_version() against the definition's MinSqliteVersionNumber, so
    /// ancient system-provided SQLite builds fail loudly before any DDL runs
    /// instead of misbehaving mid-script. ValidateEnvironment() runs the
    /// same check on demand without creating the schema.
    /// </summary>
    public class SqliteVersionGateTests
    {
        private static SqliteHostScript TrivialScript()
            => Scripts.New(Scripts.Step("noop", Scripts.Statement("SELECT 1")));

        private static SqliteHostDefinition<IGeneratedHostHandlers> Definition(int? minSqliteVersion = null)
        {
            ISqliteHostDefinitionBuilder<IGeneratedHostHandlers> builder =
                SqliteHostDefinition.ForHandlers<IGeneratedHostHandlers>().ApiLevel(1);
            if (minSqliteVersion.HasValue)
            {
                builder = builder.MinSqliteVersion(minSqliteVersion.Value);
            }
            return builder.Methods(GeneratedHostMethodSpecs.BuildAll());
        }

        private static SqliteHostRuntime<IGeneratedHostHandlers> CreateRuntime(
            ISqliteHostConnectionFactory factory,
            SqliteHostDefinition<IGeneratedHostHandlers> definition)
        {
            return new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory,
                hostDefinition: definition,
                handlers: new FakeGameHandlers(),
                options: null);
        }

        [Fact]
        public void MinSqliteVersionNumber_DefaultsTo3019003_WhenBuilderNeverSetsIt()
        {
            Assert.Equal(3019003, Definition().MinSqliteVersionNumber);
            Assert.Equal(3008007, Definition(3008007).MinSqliteVersionNumber);
            Assert.Equal(3019003, GeneratedHostDefinition.Build().MinSqliteVersionNumber);
        }

        [Fact]
        public void Run_FourComponentVersionBelowDefaultMinimum_FailsBeforeAnyDdl()
        {
            var factory = new FakeVersionWorkspaceFactory("3.8.7.4");
            var runtime = CreateRuntime(factory, Definition());

            SqliteHostRunResult result = runtime.Run(TrivialScript());

            Assert.Equal(SqliteHostRunStatus.FailedSchema, result.Status);
            Assert.Equal("sqlite-version-too-low", result.ErrorCode);
            Assert.Contains("3.8.7.4", result.ErrorMessage);
            Assert.Contains("3019003", result.ErrorMessage);
            Assert.Equal(0, result.ExecutedCallCount);
            // The gate fired before schema creation: nothing was executed.
            Assert.Empty(factory.LastConnection.ExecutedSql);
            Assert.True(factory.LastConnection.IsDisposed);
        }

        [Fact]
        public void Run_FourComponentVersion_PassesTheGate_WhenMinIsLoweredTo3008007()
        {
            var factory = new FakeVersionWorkspaceFactory("3.8.7.4");
            var runtime = CreateRuntime(factory, Definition(3008007));

            SqliteHostRunResult result = runtime.Run(TrivialScript());

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            // The gate passed and schema creation proceeded as usual.
            Assert.StartsWith("CREATE TABLE pending_host_calls (", factory.LastConnection.ExecutedSql[0]);
        }

        [Fact]
        public void Run_ChecksTheVersionOnlyOnTheFirstWorkspaceOpen()
        {
            var factory = new FakeVersionWorkspaceFactory("3.19.3");
            var runtime = CreateRuntime(factory, Definition());

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(TrivialScript()).Status);
            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(TrivialScript()).Status);

            Assert.Equal(2, factory.OpenCount);
            Assert.Equal(1, factory.VersionQueryCount);   // successful check is cached per instance
        }

        [Fact]
        public void ValidateEnvironment_SuccessIsCached_SoTheFirstRunSkipsTheVersionQuery()
        {
            var factory = new FakeVersionWorkspaceFactory("3.19.3");
            var runtime = CreateRuntime(factory, Definition());

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.ValidateEnvironment().Status);
            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(TrivialScript()).Status);

            Assert.Equal(1, factory.VersionQueryCount);
        }

        [Fact]
        public void ValidateEnvironment_OnRealAdapter_Completes_WithoutCreatingSchema()
        {
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(factory, GeneratedHostDefinition.Build());

            SqliteHostRunResult result = runtime.ValidateEnvironment();

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Null(result.ErrorCode);
            Assert.Equal(1, factory.OpenCount);
            // Version check only — no DDL ran in the workspace.
            var objectCounts = factory.LastWorkspace.Query(
                "SELECT COUNT(*) FROM sqlite_master", null, row => row.GetInt64(0));
            Assert.Equal(new[] { 0L }, objectCounts);
        }

        [Fact]
        public void ValidateEnvironment_DisposesTheWorkspace()
        {
            var factory = new TestWorkspaceFactory();
            var runtime = CreateRuntime(factory, GeneratedHostDefinition.Build());

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.ValidateEnvironment().Status);
            Assert.True(factory.LastWorkspace.IsDisposed);
        }

        [Fact]
        public void ValidateEnvironment_VersionBelowMinimum_FailsWithoutExecutingAnything()
        {
            var factory = new FakeVersionWorkspaceFactory("3.8.7.4");
            var runtime = CreateRuntime(factory, Definition());

            SqliteHostRunResult result = runtime.ValidateEnvironment();

            Assert.Equal(SqliteHostRunStatus.FailedSchema, result.Status);
            Assert.Equal("sqlite-version-too-low", result.ErrorCode);
            Assert.Contains("3.8.7.4", result.ErrorMessage);
            Assert.Contains("3019003", result.ErrorMessage);
            Assert.Empty(factory.LastConnection.ExecutedSql);
            Assert.True(factory.LastConnection.IsDisposed);
        }

        [Fact]
        public void Run_UnparseableVersionString_FailsWithVersionTooLow()
        {
            var factory = new FakeVersionWorkspaceFactory("definitely-not-a-version");
            var runtime = CreateRuntime(factory, Definition());

            SqliteHostRunResult result = runtime.Run(TrivialScript());

            Assert.Equal(SqliteHostRunStatus.FailedSchema, result.Status);
            Assert.Equal("sqlite-version-too-low", result.ErrorCode);
            Assert.Contains("definitely-not-a-version", result.ErrorMessage);
            Assert.Empty(factory.LastConnection.ExecutedSql);
        }

        [Theory]
        [InlineData("3.19.3", 3019003)]
        [InlineData("3.8.7.4", 3008007)]   // historical 4-component form, 4th ignored
        [InlineData("3.53.3", 3053003)]
        [InlineData("3.8.11.1", 3008011)]
        [InlineData("3.19", 3019000)]
        [InlineData("3", 3000000)]
        public void VersionParser_AcceptsOneToFourComponents(string versionString, int expected)
        {
            Assert.True(SqliteVersionParser.TryParse(versionString, out int versionNumber));
            Assert.Equal(expected, versionNumber);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("3.19.x")]
        [InlineData("3..3")]
        [InlineData("3.19.3.4.5")]   // five components
        [InlineData("3.-1.3")]
        [InlineData("3.19.3-beta")]
        public void VersionParser_RejectsNonVersionStrings(string versionString)
        {
            Assert.False(SqliteVersionParser.TryParse(versionString, out _));
        }

        /// <summary>
        /// Fake adapter whose only real answer is sqlite_version(); every
        /// Execute is recorded, every other query returns no rows.
        /// </summary>
        private sealed class FakeVersionConnection : ISqliteHostConnection
        {
            private readonly FakeVersionWorkspaceFactory _factory;
            private readonly string _versionString;

            public FakeVersionConnection(FakeVersionWorkspaceFactory factory, string versionString)
            {
                _factory = factory;
                _versionString = versionString;
            }

            public List<string> ExecutedSql { get; } = new List<string>();
            public bool IsDisposed { get; private set; }

            public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
                => ExecutedSql.Add(sql);

            public IReadOnlyList<T> Query<T>(
                string sql,
                IReadOnlyList<SqliteHostBinding> bindings,
                Func<ISqliteHostRow, T> mapper)
            {
                if (sql.Contains("sqlite_version()"))
                {
                    _factory.VersionQueryCount++;
                    return new List<T> { mapper(new FakeVersionRow(_versionString)) };
                }
                return new List<T>();   // e.g. the pending_host_calls drain query
            }

            public void Dispose() => IsDisposed = true;
        }

        private sealed class FakeVersionRow : ISqliteHostRow
        {
            private readonly string _text;

            public FakeVersionRow(string text) => _text = text;

            public string GetText(int index) => _text;
            public bool IsNull(int index) => false;
            public int GetInt32(int index) => throw new NotSupportedException();
            public long GetInt64(int index) => throw new NotSupportedException();
            public bool GetBool(int index) => throw new NotSupportedException();
            public byte[] GetBlob(int index) => throw new NotSupportedException();
            public float GetFloat32(int index) => throw new NotSupportedException();
            public double GetFloat64(int index) => throw new NotSupportedException();
        }

        private sealed class FakeVersionWorkspaceFactory : ISqliteHostConnectionFactory
        {
            private readonly string _versionString;

            public FakeVersionWorkspaceFactory(string versionString) => _versionString = versionString;

            public int OpenCount { get; private set; }

            /// <summary>sqlite_version() queries across all workspaces of this factory.</summary>
            public int VersionQueryCount { get; set; }

            public FakeVersionConnection LastConnection { get; private set; }

            public ISqliteHostConnection OpenWorkspace()
            {
                OpenCount++;
                LastConnection = new FakeVersionConnection(this, _versionString);
                return LastConnection;
            }
        }
    }
}
