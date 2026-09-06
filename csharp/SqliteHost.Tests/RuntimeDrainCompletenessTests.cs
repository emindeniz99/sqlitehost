using System;
using System.Collections.Generic;
using Example.Game.Generated;
using Microsoft.Data.Sqlite;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// "Completed" means all calls drained (docs/errors.md), and one call
    /// row means at most one handler invocation
    /// (docs/workspace-schema.md). Both were statements about the drain's
    /// snapshot rather than about the queue: the drain read the pending set
    /// once per step and trusted the queue row's status column, which is
    /// ordinary data a script can write.
    /// </summary>
    public class RuntimeDrainCompletenessTests
    {
        private static SqliteHostRuntime<IGeneratedHostHandlers> CreateRuntime(
            FakeGameHandlers handlers,
            ISqliteHostConnectionFactory factory,
            SqliteHostRuntimeOptions options = null)
        {
            SampleHostFloor.SkipBelowFloor();
            return new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory,
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: options);
        }

        /// <summary>
        /// A trigger fires while the runtime writes g-1's result row — i.e.
        /// after the drain already snapshotted the pending set — and the
        /// call it enqueues is in the LAST step, so no later drain existed
        /// to pick it up. The run reported Completed with setValue queued
        /// and never invoked: the silent success docs/sqlite-surface.md §3
        /// names as the failure class to avoid.
        ///
        /// CREATE TRIGGER is denied by the validator's forbidden-statement
        /// rule, so this is outside the stated threat model — but the
        /// Completed contract is unconditional, and re-reading the pending
        /// set costs one query.
        /// </summary>
        [SkippableFact]
        public void CallEnqueuedDuringTheDrain_IsStillDrained()
        {
            var handlers = new FakeGameHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "CREATE TRIGGER t_after_result AFTER INSERT ON result_get_value"
                        + " BEGIN INSERT INTO call_set_value (call_id, input_key, input_value)"
                        + " VALUES ('late-1', 'k', 99); END"),
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key) VALUES ('g-1', 'k')")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(new[] { "getValue:k", "setValue:k:99" }, handlers.Log);
            Assert.Equal(2, result.ExecutedCallCount);
            var queue = factory.LastWorkspace.Query(
                "SELECT call_id || '=' || status FROM pending_host_calls ORDER BY queue_id",
                null, row => row.GetText(0));
            Assert.Equal(new[] { "g-1=done", "late-1=done" }, queue);
        }

        /// <summary>
        /// The re-drain is bounded by MaxPendingCallsPerStep, so a trigger
        /// that enqueues a fresh call every time one drains cannot spin
        /// forever — it hits the existing cap instead.
        /// </summary>
        [SkippableFact]
        public void EndlesslyReenqueuingTrigger_HitsTheMaxPendingCap()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers, new TestWorkspaceFactory(),
                new SqliteHostRuntimeOptions { MaxPendingCallsPerStep = 4 });
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "CREATE TRIGGER t_chain AFTER INSERT ON result_set_value"
                        + " BEGIN INSERT INTO call_set_value (call_id, input_key, input_value)"
                        + " VALUES (NEW.call_id || '+', 'k', 1); END"),
                    Scripts.Statement(
                        "INSERT INTO call_set_value (call_id, input_key, input_value)"
                        + " VALUES ('s', 'k', 1)")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("max-pending-calls-exceeded", result.ErrorCode);
            Assert.Equal("only", result.StepId);
        }

        /// <summary>
        /// The backstop behind the re-drain: whatever the drain believes,
        /// a run does not get to report Completed while the queue still
        /// holds a pending row. Driven here by a connection that hides
        /// pending rows from the drain's own query and answers everything
        /// else honestly — the one thing that can defeat a drain built on
        /// that query.
        /// </summary>
        [SkippableFact]
        public void PendingCallLeftInTheQueue_IsNotCompleted()
        {
            var handlers = new FakeGameHandlers();
            using var factory = new HidingWorkspaceFactory();
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key) VALUES ('g-1', 'k')")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("undrained-calls", result.ErrorCode);
            Assert.Empty(handlers.Log);
        }

        /// <summary>Workspace whose drain query never sees the pending rows it asks for.</summary>
        private sealed class HidingWorkspaceFactory : ISqliteHostConnectionFactory, IDisposable
        {
            private HidingConnection _last;

            public ISqliteHostConnection OpenWorkspace()
            {
                var connection = new SqliteConnection("Data Source=:memory:");
                connection.Open();
                _last = new HidingConnection(new MicrosoftDataSqliteConnection(connection));
                return _last;
            }

            public void Dispose() => _last?.DisposeInner();
        }

        private sealed class HidingConnection : ISqliteHostConnection
        {
            private readonly ISqliteHostConnection _inner;

            public HidingConnection(ISqliteHostConnection inner) => _inner = inner;

            public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
                => _inner.Execute(sql, bindings);

            public IReadOnlyList<object> QueryRows(
                string sql,
                IReadOnlyList<SqliteHostBinding> bindings,
                Func<ISqliteHostRow, object> mapper)
            {
                // Only the drain's own "which calls are pending" query; the
                // final COUNT(*) check and everything else pass through.
                return sql.Contains("FROM pending_host_calls") && sql.Contains("ORDER BY queue_id")
                    ? new List<object>()
                    : _inner.QueryRows(sql, bindings, mapper);
            }

            public void Dispose()
            {
                // The runtime disposes this after the run; the factory owns it.
            }

            public void DisposeInner() => _inner.Dispose();
        }
    }
}
