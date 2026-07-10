using System;
using Example.Game.Generated;
using Microsoft.Data.Sqlite;
using SqliteHost.Conformance;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.Fixtures;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// End-to-end runs of the committed fixture payloads with
    /// dictionary-backed fake handlers, parameterized across every real
    /// adapter (Microsoft.Data.Sqlite, System.Data.SQLite, sqlite-net) via
    /// the concrete subclasses at the bottom of this file. Every scenario
    /// runs once per adapter.
    ///
    /// Native-override runs (SQLITEHOST_NATIVE_SQLITE set by
    /// tests/compatibility-sqlite/run-matrix.sh): only the
    /// Microsoft.Data.Sqlite subclass runs. System.Data.SQLite ships its own
    /// interop + native and never sees the SQLitePCLRaw provider override;
    /// sqlite-net technically would, but is skipped too so each matrix run
    /// exercises exactly one adapter against exactly one known native build.
    /// </summary>
    public abstract class IntegrationFixtureTestsBase
    {
        /// <summary>Opens one in-memory workspace on the adapter under test.</summary>
        protected abstract ISqliteHostConnection OpenAdapterConnection();

        /// <summary>
        /// True for adapters that must not run when SQLITEHOST_NATIVE_SQLITE
        /// pins a specific native build (see class remarks).
        /// </summary>
        protected virtual bool SkipUnderNativeOverride => false;

        private AdapterWorkspaceFactory CreateFactory(bool retainWorkspace = false)
            => new AdapterWorkspaceFactory(OpenAdapterConnection, retainWorkspace);

        private SqliteHostRunResult RunFixture(
            string payload,
            FakeGameHandlers handlers,
            SqliteHostRuntimeOptions options = null,
            AdapterWorkspaceFactory factory = null)
        {
            Skip.If(
                SkipUnderNativeOverride && NativeSqliteOverride.IsActive,
                "SQLITEHOST_NATIVE_SQLITE is set: the dynamic-provider override is scoped to the "
                + "Microsoft.Data.Sqlite adapter; this adapter bundles/loads its own native SQLite.");
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory ?? CreateFactory(),
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: options);
            return runtime.Run(ScriptEnvelopeJson.LoadPayload(payload));
        }

        [SkippableFact]
        public void Example001_WritesOnlyWhenStoredValueIsNot42()
        {
            var handlers = new FakeGameHandlers();
            handlers.Storage["example-key"] = 7;

            SqliteHostRunResult result = RunFixture(
                "valid/example-001-read-then-conditional-write.json", handlers);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(2, result.ExecutedCallCount);
            Assert.Equal(new[] { "getValue:example-key", "setValue:example-key:42" }, handlers.Log);
            Assert.Equal(42, handlers.Storage["example-key"]);
        }

        [SkippableFact]
        public void Example001_SkipsTheWriteWhenStoredValueIsAlready42()
        {
            var handlers = new FakeGameHandlers();
            handlers.Storage["example-key"] = 42;

            SqliteHostRunResult result = RunFixture(
                "valid/example-001-read-then-conditional-write.json", handlers);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.ExecutedCallCount);
            Assert.Equal(new[] { "getValue:example-key" }, handlers.Log);
        }

        [SkippableFact]
        public void Example002_ListRoundTrip_ResultChildRowsDriveTheSecondStep()
        {
            var handlers = new FakeGameHandlers();
            handlers.Storage["alpha"] = 10;
            using var factory = CreateFactory(retainWorkspace: true);

            SqliteHostRunResult result = RunFixture(
                "valid/example-002-list-roundtrip.json", handlers, factory: factory);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(2, result.ExecutedCallCount);

            // getValues received the optional defaultValue binding + ordered keys.
            Assert.NotNull(handlers.LastGetValuesInput);
            Assert.Equal(7L, handlers.LastGetValuesInput.DefaultValue);
            Assert.Equal(2, handlers.LastGetValuesInput.Keys.Count);
            Assert.Equal("alpha", handlers.LastGetValuesInput.Keys[0].Key);
            Assert.Equal("beta", handlers.LastGetValuesInput.Keys[1].Key);

            // The result child rows written after step 1 drove step 2's insert:
            // only the missing key 'beta' produced a setValue call.
            Assert.Equal(new[] { "getValues:2", "setValue:beta:7" }, handlers.Log);
            Assert.Equal(7, handlers.Storage["beta"]);

            var entries = factory.LastWorkspace.Query(
                "SELECT item_index, result_key, result_value, result_found"
                + " FROM result_get_values__result_entries WHERE call_id = 'list-1' ORDER BY item_index",
                null,
                row => row.GetInt64(0) + "|" + row.GetText(1) + "|" + row.GetInt64(2) + "|" + row.GetInt64(3));
            Assert.Equal(new[] { "0|alpha|10|1", "1|beta|7|0" }, entries);

            var setValueCallIds = factory.LastWorkspace.Query(
                "SELECT call_id FROM call_set_value",
                null,
                row => row.GetText(0));
            Assert.Equal(new[] { "w-beta" }, setValueCallIds);
        }

        [SkippableFact]
        public void Example003_RuntimeInputsFeedTheScript_AndReadAfterWriteConfirms()
        {
            var handlers = new FakeGameHandlers();

            SqliteHostRunResult result = RunFixture(
                "valid/example-003-runtime-inputs.json", handlers);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(2, result.ExecutedCallCount);
            // The write got its value from script_inputs; the confirm step is
            // an explicit read-after-write that only runs when the write
            // reported success.
            Assert.Equal(new[] { "setValue:example-key:42", "getValue:example-key" }, handlers.Log);
            Assert.Equal(42, handlers.Storage["example-key"]);
        }

        [SkippableFact]
        public void Example004_BlobBytesReachTheHandlerIntact()
        {
            var handlers = new FakeGameHandlers();

            SqliteHostRunResult result = RunFixture("valid/example-004-blob.json", handlers);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.ExecutedCallCount);
            Assert.NotNull(handlers.LastPutBlobInput);
            Assert.Equal("blob-key", handlers.LastPutBlobInput.Key);
            Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, handlers.LastPutBlobInput.Data);
            Assert.Null(handlers.LastPutBlobInput.Note);   // bound as typed null
            Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, handlers.Blobs["blob-key"]);
        }

        [SkippableFact]
        public void Example005_UnusedRequiredMethod_StillCompletes()
        {
            var handlers = new FakeGameHandlers();
            handlers.Storage["example-key"] = 3;

            SqliteHostRunResult result = RunFixture(
                "valid/example-005-unused-required-method.json", handlers);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.ExecutedCallCount);
            Assert.Equal(new[] { "getValue:example-key" }, handlers.Log);
        }

        [SkippableFact]
        public void Example006_FloatScores_AverageFeedsTheFollowUpStep()
        {
            var handlers = new FakeGameHandlers();
            using var factory = CreateFactory(retainWorkspace: true);

            SqliteHostRunResult result = RunFixture(
                "valid/example-006-floats.json", handlers, factory: factory);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(2, result.ExecutedCallCount);
            Assert.Equal(new[] { "recordScore:score-key", "recordScore:score-key-2" }, handlers.Log);

            // The first call carries the payload's dyadic-exact floats.
            Assert.Equal(2, handlers.RecordScoreInputs.Count);
            RecordScoreInput first = handlers.RecordScoreInputs[0];
            Assert.Equal("score-key", first.Key);
            Assert.Equal(98.5, first.Score);
            Assert.Equal(0.75f, first.Weight);

            // The 98.5 average written after step 1 drove step 2's insert:
            // input_score = result_average * 0.5 with a NULL weight.
            RecordScoreInput second = handlers.RecordScoreInputs[1];
            Assert.Equal("score-key-2", second.Key);
            Assert.Equal(49.25, second.Score);
            Assert.Null(second.Weight);

            var averages = factory.LastWorkspace.Query(
                "SELECT call_id, result_average FROM result_record_score ORDER BY call_id",
                null,
                row => row.GetText(0) + "|" + row.GetFloat64(1).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(new[] { "score-1|98.5", "score-2|49.25" }, averages);
        }

        [SkippableFact]
        public void Example008_ScriptVars_ComputedVariableFeedsTheHostCall()
        {
            var handlers = new FakeGameHandlers();
            using var factory = CreateFactory(retainWorkspace: true);

            SqliteHostRunResult result = RunFixture(
                "valid/example-008-variables.json", handlers, factory: factory);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.ExecutedCallCount);
            // The script declared aa=55 and kh=4 in script_vars, reassigned
            // total = aa * kh with INSERT OR REPLACE, and fed it into the
            // write step: setValue must receive input_value 220.
            Assert.Equal(new[] { "setValue:computed-key:220" }, handlers.Log);
            Assert.Equal(220, handlers.Storage["computed-key"]);

            // script_vars is script-owned scratch space: exactly the three
            // variables the script wrote, untouched by the runtime.
            var vars = factory.LastWorkspace.Query(
                "SELECT name, int_value FROM script_vars ORDER BY name",
                null,
                row => row.GetText(0) + "=" + row.GetInt64(1));
            Assert.Equal(new[] { "aa=55", "kh=4", "total=220" }, vars);
        }

        [SkippableFact]
        public void Diagnostics_PopulateCallsWhenEnabled()
        {
            var handlers = new FakeGameHandlers();
            handlers.Storage["example-key"] = 7;

            SqliteHostRunResult result = RunFixture(
                "valid/example-001-read-then-conditional-write.json",
                handlers,
                new SqliteHostRuntimeOptions { EnableDiagnostics = true });

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.NotNull(result.Calls);
            Assert.Equal(2, result.Calls.Count);
            Assert.Equal("read-1", result.Calls[0].CallId);
            Assert.Equal("getValue", result.Calls[0].Method);
            Assert.Equal("read-current", result.Calls[0].StepId);
            Assert.Equal("write-1", result.Calls[1].CallId);
            Assert.Equal("setValue", result.Calls[1].Method);
            Assert.Equal("write-value", result.Calls[1].StepId);
        }

        [SkippableFact]
        public void Diagnostics_AreNullWhenDisabled()
        {
            var handlers = new FakeGameHandlers();
            SqliteHostRunResult result = RunFixture("valid/example-004-blob.json", handlers);
            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Null(result.Calls);
        }
    }

    /// <summary>Fixture matrix on the Microsoft.Data.Sqlite adapter (SQLitePCLRaw; honors SQLITEHOST_NATIVE_SQLITE).</summary>
    public class MicrosoftDataSqliteIntegrationFixtureTests : IntegrationFixtureTestsBase
    {
        protected override ISqliteHostConnection OpenAdapterConnection()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return new MicrosoftDataSqliteConnection(connection);
        }
    }

    /// <summary>Fixture matrix on the System.Data.SQLite ADO.NET adapter (own bundled native).</summary>
    public class SystemDataSqliteIntegrationFixtureTests : IntegrationFixtureTestsBase
    {
        protected override bool SkipUnderNativeOverride => true;

        protected override ISqliteHostConnection OpenAdapterConnection()
            => SystemDataSqliteConnection.OpenInMemory();
    }

    /// <summary>Fixture matrix on the sqlite-net (Unity-style wrapper) adapter.</summary>
    public class SqliteNetIntegrationFixtureTests : IntegrationFixtureTestsBase
    {
        protected override bool SkipUnderNativeOverride => true;

        protected override ISqliteHostConnection OpenAdapterConnection()
            => SqliteNetConnection.OpenInMemory();
    }
}
