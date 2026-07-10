using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Pinned list child-table semantics (docs/workspace-schema.md): the
    /// (call_id, item_index) primary key rejects duplicates at insert time,
    /// item_index gaps map to a dense ordered DTO list, and child rows added
    /// for an already-drained call are detected defensively
    /// (list-child-after-drain, docs/errors.md). Runs on the
    /// Microsoft.Data.Sqlite adapter.
    /// </summary>
    public class ListSemanticsTests
    {
        private const int SqliteConstraint = 19;   // SQLITE_CONSTRAINT

        private static SqliteHostRuntime<IGeneratedHostHandlers> CreateRuntime(
            FakeGameHandlers handlers,
            TestWorkspaceFactory factory = null)
        {
            return new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory ?? new TestWorkspaceFactory(),
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: null);
        }

        private static SqliteHostStatement InsertGetValuesParent(string callId)
        {
            return Scripts.Statement(
                "INSERT INTO call_get_values (call_id) VALUES (:callId)",
                ("callId", SqliteHostBindingValue.Text(callId)));
        }

        private static SqliteHostStatement InsertKeyChild(string callId, long itemIndex, string key)
        {
            return Scripts.Statement(
                "INSERT INTO call_get_values__input_keys (call_id, item_index, input_key)"
                + " VALUES (:callId, :itemIndex, :key)",
                ("callId", SqliteHostBindingValue.Text(callId)),
                ("itemIndex", SqliteHostBindingValue.Int64(itemIndex)),
                ("key", SqliteHostBindingValue.Text(key)));
        }

        [Fact]
        public void DuplicateItemIndex_FailsAtInsert_ThroughThePrimaryKeyConstraint()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("dup-index",
                    InsertGetValuesParent("list-1"),
                    InsertKeyChild("list-1", 0, "a"),
                    InsertKeyChild("list-1", 0, "b")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("sql-error", result.ErrorCode);
            Assert.Equal("dup-index", result.StepId);
            Assert.Equal(2, result.StatementIndex);
            Assert.Equal(SqliteConstraint, result.SqliteErrorCode);
            Assert.Empty(handlers.Log);   // step aborted before drain
        }

        [Fact]
        public void ItemIndexGaps_MapToADenseOrderedList()
        {
            var handlers = new FakeGameHandlers();
            handlers.Storage["a"] = 1;
            handlers.Storage["c"] = 3;
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("gaps",
                    InsertGetValuesParent("list-1"),
                    InsertKeyChild("list-1", 0, "a"),
                    InsertKeyChild("list-1", 5, "b"),
                    InsertKeyChild("list-1", 9, "c")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.ExecutedCallCount);
            Assert.NotNull(handlers.LastGetValuesInput);
            // Dense 3-item list ordered by ascending item_index — gaps
            // produce no nulls or placeholders.
            Assert.Equal(3, handlers.LastGetValuesInput.Keys.Count);
            Assert.Equal("a", handlers.LastGetValuesInput.Keys[0].Key);
            Assert.Equal("b", handlers.LastGetValuesInput.Keys[1].Key);
            Assert.Equal("c", handlers.LastGetValuesInput.Keys[2].Key);
        }

        [Fact]
        public void ListChildRowsAddedAfterDrain_FailSql_ListChildAfterDrain()
        {
            var handlers = new FakeGameHandlers();
            var runtime = CreateRuntime(handlers);
            var script = Scripts.New(
                Scripts.Step("drain",
                    InsertGetValuesParent("list-1"),
                    InsertKeyChild("list-1", 0, "a")),
                Scripts.Step("too-late",
                    InsertKeyChild("list-1", 1, "b")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("list-child-after-drain", result.ErrorCode);
            Assert.Equal("too-late", result.StepId);   // the step that added the rows
            Assert.Equal("getValues", result.Method);
            // The handler ran exactly once, with the pre-drain single-item list.
            Assert.Equal(1, result.ExecutedCallCount);
            Assert.Equal(new[] { "getValues:1" }, handlers.Log);
        }
    }
}
