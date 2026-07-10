using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.Fixtures;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// End-to-end runs of the committed fixture payloads through the real
    /// Microsoft.Data.Sqlite adapter with dictionary-backed fake handlers.
    /// </summary>
    public class IntegrationFixtureTests
    {
        private static SqliteHostRunResult RunFixture(
            string payload,
            FakeGameHandlers handlers,
            SqliteHostRuntimeOptions options = null,
            TestWorkspaceFactory factory = null)
        {
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory ?? new TestWorkspaceFactory(),
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: options);
            return runtime.Run(ScriptEnvelopeJson.LoadPayload(payload));
        }

        [Fact]
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

        [Fact]
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

        [Fact]
        public void Example002_ListRoundTrip_ResultChildRowsDriveTheSecondStep()
        {
            var handlers = new FakeGameHandlers();
            handlers.Storage["alpha"] = 10;
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);

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

        [Fact]
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

        [Fact]
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

        [Fact]
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

        [Fact]
        public void Example006_FloatScores_AverageFeedsTheFollowUpStep()
        {
            var handlers = new FakeGameHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);

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

        [Fact]
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

        [Fact]
        public void Diagnostics_AreNullWhenDisabled()
        {
            var handlers = new FakeGameHandlers();
            SqliteHostRunResult result = RunFixture("valid/example-004-blob.json", handlers);
            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Null(result.Calls);
        }
    }
}
