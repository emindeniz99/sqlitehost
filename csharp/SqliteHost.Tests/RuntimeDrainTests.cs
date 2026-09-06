using System;
using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    public class RuntimeDrainTests
    {
        private static SqliteHostRuntime<IGeneratedHostHandlers> CreateRuntime(
            FakeGameHandlers handlers,
            TestWorkspaceFactory factory = null,
            SqliteHostRuntimeOptions options = null)
        {
            // Every test here runs the workspace on the real engine, so all
            // would fail via the version gate below the sample host's floor
            // (see FloorGateTests).
            SampleHostFloor.SkipBelowFloor();
            return new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory ?? new TestWorkspaceFactory(),
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: options);
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
        public void PendingCallsDrainInQueueIdOrder()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("many",
                    InsertGetValue("g-1", "k1"),
                    InsertSetValue("s-1", "k2", 5),
                    InsertGetValue("g-2", "k3")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(3, result.ExecutedCallCount);
            Assert.Equal(new[] { "getValue:k1", "setValue:k2:5", "getValue:k3" }, handlers.Log);
        }

        [SkippableFact]
        public void DrainHappensOnlyAfterTheWholeStepSucceeds()
        {
            // A step whose 2nd statement fails must not invoke handlers for
            // the call inserted by its 1st statement.
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("broken",
                    InsertGetValue("g-1", "k1"),
                    Scripts.Statement("INSERT INTO no_such_table (x) VALUES (1)")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("sql-error", result.ErrorCode);
            Assert.Equal("broken", result.StepId);
            Assert.Equal(1, result.StatementIndex);
            Assert.Empty(handlers.Log);
            Assert.Equal(0, result.ExecutedCallCount);
        }

        [SkippableFact]
        public void MaxPendingCallsPerStepExceeded_FailsBeforeAnyHandlerRuns()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers, options: new SqliteHostRuntimeOptions
            {
                MaxPendingCallsPerStep = 1
            });
            var script = Scripts.New(
                Scripts.Step("burst",
                    InsertGetValue("g-1", "k1"),
                    InsertGetValue("g-2", "k2")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("max-pending-calls-exceeded", result.ErrorCode);
            Assert.Equal("burst", result.StepId);
            Assert.Empty(handlers.Log);
        }

        [SkippableFact]
        public void HandlerException_FailsHandler_WithMethodAndCounts()
        {
            var handlers = new FakeGameHandlers();
            handlers.GetValueOverride = _ => throw new InvalidOperationException("boom from handler");
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("mixed",
                    InsertSetValue("s-1", "k", 1),
                    InsertGetValue("g-1", "k")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
            Assert.Equal("handler-error", result.ErrorCode);
            Assert.Equal("getValue", result.Method);
            Assert.Contains("boom from handler", result.ErrorMessage);
            // setValue drained first (queue order) and completed.
            Assert.Equal(1, result.ExecutedCallCount);
            Assert.Equal("mixed", result.StepId);
            Assert.Equal(-1, result.StatementIndex);
        }

        [SkippableFact]
        public void UnknownQueuedMethod_FailsSql()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("direct",
                    Scripts.Statement(
                        "INSERT INTO pending_host_calls (call_id, method) VALUES ('x-1', 'ghostMethod')")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("unknown-queued-method", result.ErrorCode);
            Assert.Equal("ghostMethod", result.Method);
        }

        [SkippableFact]
        public void QueueRowWithoutCallRow_FailsSql_CallRowMissing()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("direct",
                    Scripts.Statement(
                        "INSERT INTO pending_host_calls (call_id, method) VALUES ('orphan-1', 'getValue')")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("call-row-missing", result.ErrorCode);
            Assert.Equal("getValue", result.Method);
        }

        [SkippableFact]
        public void DuplicateCallIdAcrossMethods_IsSqlError()
        {
            // pending_host_calls.call_id is UNIQUE; the queue trigger insert
            // fails and surfaces as sql-error on the second statement.
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("dup",
                    InsertGetValue("same-id", "k1"),
                    InsertSetValue("same-id", "k2", 2)));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("sql-error", result.ErrorCode);
            Assert.Equal(1, result.StatementIndex);
            Assert.Empty(handlers.Log);
        }

        [SkippableFact]
        public void CompletedRun_MarksQueueRowsDone_AndWritesResultRows()
        {
            var handlers = new FakeGameHandlers();
            handlers.Storage["k"] = 41;
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", InsertGetValue("g-1", "k")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            var workspace = factory.LastWorkspace;

            var queueStatuses = workspace.Query(
                "SELECT status FROM pending_host_calls ORDER BY queue_id",
                null,
                row => row.GetText(0));
            Assert.Equal(new[] { "done" }, queueStatuses);

            var resultRows = workspace.Query(
                "SELECT call_id, status, result_value FROM result_get_value",
                null,
                row => row.GetText(0) + "|" + row.GetText(1) + "|" + row.GetInt64(2));
            Assert.Equal(new[] { "g-1|done|41" }, resultRows);
        }

        /// <summary>
        /// docs/errors.md: ExecutedCallCount "always counts successfully
        /// completed handler invocations". The bookkeeping UPDATE that
        /// marks the queue row done runs AFTER the handler, and there is no
        /// transaction around the pair (docs/sqlite-surface.md), so when it
        /// fails — disk full, SQLITE_BUSY, an IO error on the queue write —
        /// the handler's side effects and its result row are already
        /// committed. A count of zero then tells a host reconciling "which
        /// of my methods actually fired" the opposite of the truth.
        /// </summary>
        [SkippableFact]
        public void QueueUpdateFailsAfterASuccessfulHandler_StillCountsTheInvocation()
        {
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers, factory, new SqliteHostRuntimeOptions
            {
                EnableDiagnostics = true
            });
            // Drop the queue table from inside the handler: the result row
            // is written, then the UPDATE has nothing to update.
            handlers.GetValueOverride = _ =>
            {
                factory.LastWorkspace.Execute("DROP TABLE pending_host_calls", null);
                return new GetValueResult { Value = 5 };
            };
            var script = Scripts.New(Scripts.Step("only", InsertGetValue("g-1", "k")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("sql-error", result.ErrorCode);
            Assert.Equal(new[] { "getValue:k" }, handlers.Log);
            Assert.Equal(1, result.ExecutedCallCount);
            SqliteHostCallDiagnostic call = Assert.Single(result.Calls);
            Assert.Equal("g-1", call.CallId);
            Assert.Equal("getValue", call.Method);
            Assert.Equal("only", call.StepId);
            // The result row really is committed — that is why the count matters.
            var resultRows = factory.LastWorkspace.Query(
                "SELECT call_id || '=' || status FROM result_get_value", null, row => row.GetText(0));
            Assert.Equal(new[] { "g-1=done" }, resultRows);
        }

        [SkippableFact]
        public void HandlerFailure_LeavesNoResultRowForTheFailingCall()
        {
            var handlers = new FakeGameHandlers();
            handlers.GetValueOverride = _ => throw new InvalidOperationException("boom");
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", InsertGetValue("g-1", "k")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
            var resultCount = factory.LastWorkspace.Query(
                "SELECT call_id FROM result_get_value",
                null,
                row => row.GetText(0));
            Assert.Empty(resultCount);
        }
    }
}
