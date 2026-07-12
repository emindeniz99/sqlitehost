using Example.Game.Generated;
using Microsoft.Data.Sqlite;
using SqliteHost.Adapters.Native;
using SqliteHost.Conformance;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// The workspace version gate (docs/errors.md sqlite-version-too-low)
    /// measured against the REAL engine of the current run — every matrix
    /// cell exercises this class (SqliteVersionGateTests covers the same
    /// gate against fake version strings). Two directions:
    ///
    /// - Sample floor (3019003): on a below-floor engine (the matrix's
    ///   3.9.x rows) the gate must refuse the run before any DDL — the one
    ///   intentional assertion that stands in for every runtime-driven
    ///   suite skipped there (see SampleHostFloor). At/above the floor the
    ///   same script must instead run end to end.
    /// - Lowered floor (3009000): a definition built from the same sample
    ///   method specs with MinSqliteVersion(3009000) must run a real
    ///   call -> drain -> result read script end to end on the actual
    ///   engine in EVERY cell — the measured proof that the runtime works
    ///   down to 3.9.0 when a host explicitly opts its floor down.
    ///
    /// Runs on both adapters that honor SQLITEHOST_NATIVE_SQLITE:
    /// Microsoft.Data.Sqlite (SQLitePCLRaw dynamic provider) and
    /// SqliteHost.Adapters.Native (test-side DllImportResolver), via the
    /// concrete subclasses at the bottom of this file.
    /// </summary>
    public abstract class FloorGateTestsBase
    {
        /// <summary>Opens one in-memory workspace on the adapter under test.</summary>
        protected abstract ISqliteHostConnection OpenAdapterConnection();

        /// <summary>sqlite_version() of the adapter under test, numeric encoding.</summary>
        private int EngineVersionNumber()
        {
            using ISqliteHostConnection probe = OpenAdapterConnection();
            string version = probe.Query("SELECT sqlite_version()", null, row => row.GetText(0))[0];
            Assert.True(SqliteVersionParser.TryParse(version, out int number),
                "sqlite_version() returned unparseable '" + version + "'");
            return number;
        }

        /// <summary>Sample method specs with the floor explicitly lowered to 3.9.0.</summary>
        private static SqliteHostDefinition<IGeneratedHostHandlers> LoweredFloorDefinition()
        {
            return SqliteHostDefinition
                .ForHandlers<IGeneratedHostHandlers>()
                .ApiLevel(1)
                .MinSqliteVersion(3009000)
                .Methods(GeneratedHostMethodSpecs.BuildAll());
        }

        /// <summary>
        /// call -> drain -> result read: step "read" queues a getValue, the
        /// step-boundary drain writes its result row, and step "write" reads
        /// that row back to drive a setValue with result_value + 1.
        /// </summary>
        private static SqliteHostScript EndToEndScript()
        {
            return Scripts.New(
                Scripts.Step("read",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key) VALUES ('read-1', 'example-key')")),
                Scripts.Step("write",
                    Scripts.Statement(
                        "INSERT INTO call_set_value (call_id, input_key, input_value)"
                        + " SELECT 'write-1', 'example-key', result_value + 1"
                        + " FROM result_get_value WHERE call_id = 'read-1'")));
        }

        private SqliteHostRunResult Run(
            SqliteHostDefinition<IGeneratedHostHandlers> definition,
            FakeGameHandlers handlers,
            AdapterWorkspaceFactory factory)
        {
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory,
                hostDefinition: definition,
                handlers: handlers,
                options: null);
            return runtime.Run(EndToEndScript());
        }

        private void AssertRanEndToEnd(
            SqliteHostRunResult result, FakeGameHandlers handlers, AdapterWorkspaceFactory factory)
        {
            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Null(result.ErrorCode);
            Assert.Equal(2, result.ExecutedCallCount);
            Assert.Equal(new[] { "getValue:example-key", "setValue:example-key:8" }, handlers.Log);
            Assert.Equal(8, handlers.Storage["example-key"]);

            // The result rows both calls wrote are readable in the workspace.
            var getValueResults = factory.LastWorkspace.Query(
                "SELECT call_id, result_value FROM result_get_value", null,
                row => row.GetText(0) + "|" + row.GetInt64(1));
            Assert.Equal(new[] { "read-1|7" }, getValueResults);
            var setValueResults = factory.LastWorkspace.Query(
                "SELECT call_id, result_success FROM result_set_value", null,
                row => row.GetText(0) + "|" + row.GetInt64(1));
            Assert.Equal(new[] { "write-1|1" }, setValueResults);
        }

        [SkippableFact]
        public void SampleFloor_BelowFloorEngine_GateRefusesTheRun_BeforeAnyDdl()
        {
            Skip.If(EngineVersionNumber() >= SampleHostFloor.FloorVersionNumber,
                "engine is at/above the sample host's floor; this direction is covered by"
                + " SampleFloor_AtOrAboveFloorEngine_RunsEndToEnd.");
            var handlers = new FakeGameHandlers();
            handlers.Storage["example-key"] = 7;
            using var factory = new AdapterWorkspaceFactory(OpenAdapterConnection, retainWorkspace: true);

            SqliteHostRunResult result = Run(GeneratedHostDefinition.Build(), handlers, factory);

            Assert.Equal(SqliteHostRunStatus.FailedSchema, result.Status);
            Assert.Equal("sqlite-version-too-low", result.ErrorCode);
            Assert.Contains(SampleHostFloor.FloorVersionNumber.ToString(), result.ErrorMessage);
            Assert.Equal(0, result.ExecutedCallCount);
            Assert.Empty(handlers.Log);
            // The gate fired before schema creation: the workspace is empty.
            var objectCounts = factory.LastWorkspace.Query(
                "SELECT COUNT(*) FROM sqlite_master", null, row => row.GetInt64(0));
            Assert.Equal(new[] { 0L }, objectCounts);
        }

        [SkippableFact]
        public void SampleFloor_AtOrAboveFloorEngine_RunsEndToEnd()
        {
            Skip.If(EngineVersionNumber() < SampleHostFloor.FloorVersionNumber,
                "engine is below the sample host's floor; this direction is covered by"
                + " SampleFloor_BelowFloorEngine_GateRefusesTheRun_BeforeAnyDdl.");
            var handlers = new FakeGameHandlers();
            handlers.Storage["example-key"] = 7;
            using var factory = new AdapterWorkspaceFactory(OpenAdapterConnection, retainWorkspace: true);

            SqliteHostRunResult result = Run(GeneratedHostDefinition.Build(), handlers, factory);

            AssertRanEndToEnd(result, handlers, factory);
        }

        [Fact]
        public void LoweredFloor_3009000_RunsEndToEnd_OnTheActualEngine()
        {
            var handlers = new FakeGameHandlers();
            handlers.Storage["example-key"] = 7;
            using var factory = new AdapterWorkspaceFactory(OpenAdapterConnection, retainWorkspace: true);

            SqliteHostRunResult result = Run(LoweredFloorDefinition(), handlers, factory);

            AssertRanEndToEnd(result, handlers, factory);
        }
    }

    /// <summary>Floor gate on the Microsoft.Data.Sqlite adapter (SQLitePCLRaw; honors SQLITEHOST_NATIVE_SQLITE).</summary>
    public class MicrosoftDataSqliteFloorGateTests : FloorGateTestsBase
    {
        protected override ISqliteHostConnection OpenAdapterConnection()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return new MicrosoftDataSqliteConnection(connection);
        }
    }

    /// <summary>Floor gate on the shippable SqliteHost.Adapters.Native P/Invoke adapter (honors SQLITEHOST_NATIVE_SQLITE).</summary>
    public class NativeAdapterFloorGateTests : FloorGateTestsBase
    {
        protected override ISqliteHostConnection OpenAdapterConnection()
            => NativeSqliteHostConnection.OpenInMemory();
    }
}
