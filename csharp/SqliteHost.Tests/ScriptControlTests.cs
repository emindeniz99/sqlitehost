using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// script_control semantics (docs/workspace-schema.md): the runtime
    /// checks the control table after every statement; halt drains the
    /// current step's emitted calls and completes with Halted, fail aborts
    /// without draining, anything else is invalid-control-action.
    /// </summary>
    public class ScriptControlTests
    {
        private static SqliteHostRuntime<IGeneratedHostHandlers> CreateRuntime(
            FakeGameHandlers handlers,
            TestWorkspaceFactory factory = null)
        {
            // Every test here runs the workspace on the real engine, so all
            // would fail via the version gate below the sample host's floor
            // (see FloorGateTests).
            SampleHostFloor.SkipBelowFloor();
            return new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory ?? new TestWorkspaceFactory(),
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: null);
        }

        private static SqliteHostStatement InsertGetValue(string callId, string key)
        {
            return Scripts.Statement(
                "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, :key)",
                ("callId", SqliteHostBindingValue.Text(callId)),
                ("key", SqliteHostBindingValue.Text(key)));
        }

        private static SqliteHostStatement InsertSetValue(string callId, string key, long value)
        {
            return Scripts.Statement(
                "INSERT INTO call_set_value (call_id, input_key, input_value) VALUES (:callId, :key, :value)",
                ("callId", SqliteHostBindingValue.Text(callId)),
                ("key", SqliteHostBindingValue.Text(key)),
                ("value", SqliteHostBindingValue.Int64(value)));
        }

        [SkippableFact]
        public void FailAction_SkipsTheFailingStepsDrain_EarlierStepsEffectsPersist()
        {
            var handlers = new FakeGameHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);

            // The fail is written by the LAST statement of the step, so the
            // check that runs before the end-of-step drain must see it and
            // suppress the drain of g-1.
            var script = Scripts.New(
                Scripts.Step("first",
                    InsertSetValue("s-1", "k", 5)),
                Scripts.Step("second",
                    InsertGetValue("g-1", "k"),
                    Scripts.Statement(
                        "INSERT INTO script_control (action, message) VALUES ('fail', 'stop: bad state')")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedScript, result.Status);
            Assert.Equal("script-abort", result.ErrorCode);
            Assert.Equal("stop: bad state", result.ErrorMessage);
            Assert.Equal("second", result.StepId);
            Assert.Equal(1, result.StatementIndex);
            Assert.False(result.Halted);

            // Step 1 drained and its effect persists; step 2's call never
            // reached a handler and its queue row is still pending.
            Assert.Equal(new[] { "setValue:k:5" }, handlers.Log);
            Assert.Equal(1, result.ExecutedCallCount);
            Assert.Equal(5, handlers.Storage["k"]);
            var queueRows = factory.LastWorkspace.Query(
                "SELECT call_id, status FROM pending_host_calls ORDER BY queue_id",
                null,
                row => row.GetText(0) + "|" + row.GetText(1));
            Assert.Equal(new[] { "s-1|done", "g-1|pending" }, queueRows);
        }

        [SkippableFact]
        public void InvalidControlAction_FailsValidation_WithStatementContext()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO script_control (action, message) VALUES ('pause', 'not a verb')"),
                    InsertGetValue("g-1", "k")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedValidation, result.Status);
            Assert.Equal("invalid-control-action", result.ErrorCode);
            Assert.Contains("'pause'", result.ErrorMessage);
            Assert.Equal("only", result.StepId);
            Assert.Equal(0, result.StatementIndex);
            Assert.Empty(handlers.Log);
        }

        [SkippableFact]
        public void HaltMidStep_SkipsLaterStatements_ButDrainsCallsEmittedSoFar()
        {
            var handlers = new FakeGameHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);

            // Statement 2 of 3 writes the halt: statement 3 must never
            // execute, but statement 1's call DOES drain, and the following
            // step is skipped entirely.
            var script = Scripts.New(
                Scripts.Step("partial",
                    InsertGetValue("g-1", "k1"),
                    Scripts.Statement(
                        "INSERT INTO script_control (action, message) VALUES ('halt', 'enough')"),
                    InsertGetValue("g-2", "k2")),
                Scripts.Step("never",
                    InsertGetValue("g-3", "k3")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.True(result.Halted);
            Assert.Equal("enough", result.HaltMessage);
            Assert.Equal("partial", result.StepId);
            Assert.Null(result.ErrorCode);
            Assert.Equal(-1, result.StatementIndex);
            Assert.Equal(new[] { "getValue:k1" }, handlers.Log);
            Assert.Equal(1, result.ExecutedCallCount);

            var callIds = factory.LastWorkspace.Query(
                "SELECT call_id FROM call_get_value ORDER BY call_id",
                null,
                row => row.GetText(0));
            Assert.Equal(new[] { "g-1" }, callIds);
        }

        [SkippableFact]
        public void Halt_WithNullMessage_CompletesWithNullHaltMessage()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement("INSERT INTO script_control (action) VALUES ('halt')")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.True(result.Halted);
            Assert.Null(result.HaltMessage);
        }

        [SkippableFact]
        public void FirstControlRowByRowidWins()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO script_control (action, message)"
                        + " VALUES ('halt', 'first'), ('fail', 'second')")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.True(result.Halted);
            Assert.Equal("first", result.HaltMessage);
        }
    }
}
