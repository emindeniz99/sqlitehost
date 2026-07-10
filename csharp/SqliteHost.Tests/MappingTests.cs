using System.Collections.Generic;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    public class MappingTests
    {
        private const string InsertEveryType =
            "INSERT INTO call_every_type (call_id, input_i32, input_i64, input_flag, input_name, input_payload,"
            + " input_opt_i32, input_opt_i64, input_opt_flag, input_opt_name, input_opt_payload)"
            + " VALUES (:callId, :i32, @i64, $flag, :name, :payload, :optI32, :optI64, :optFlag, :optName, :optPayload)";

        private static readonly byte[] PayloadBytes = { 0x01, 0x02, 0x00, 0xFF };
        private static readonly byte[] OptPayloadBytes = { 0xAA, 0xBB };

        private static SqliteHostRuntime<IEveryTypeHandlers> CreateRuntime(
            EchoEveryTypeHandlers handlers,
            TestWorkspaceFactory factory)
        {
            return new SqliteHostRuntime<IEveryTypeHandlers>(
                connectionFactory: factory,
                hostDefinition: EveryTypeHost.Build(),
                handlers: handlers,
                options: null);
        }

        private static SqliteHostStatement EveryTypeStatement(bool withOptionals)
        {
            return Scripts.Statement(
                InsertEveryType,
                ("callId", SqliteHostBindingValue.Text("c-1")),
                ("i32", SqliteHostBindingValue.Int32(-123)),
                ("i64", SqliteHostBindingValue.Int64(9007199254740993L)),
                ("flag", SqliteHostBindingValue.Bool(true)),
                ("name", SqliteHostBindingValue.Text("héllo")),
                ("payload", SqliteHostBindingValue.Blob(PayloadBytes)),
                ("optI32", withOptionals ? SqliteHostBindingValue.Int32(7) : SqliteHostBindingValue.Null()),
                ("optI64", withOptionals ? SqliteHostBindingValue.Int64(-9L) : SqliteHostBindingValue.Null()),
                ("optFlag", withOptionals ? SqliteHostBindingValue.Bool(false) : SqliteHostBindingValue.Null()),
                ("optName", withOptionals ? SqliteHostBindingValue.Text("note") : SqliteHostBindingValue.Null()),
                ("optPayload", withOptionals ? SqliteHostBindingValue.Blob(OptPayloadBytes) : SqliteHostBindingValue.Null()));
        }

        [Fact]
        public void AllBindingTypesAndPrefixes_RoundTripIntoTheInputDto()
        {
            var handlers = new EchoEveryTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", EveryTypeStatement(withOptionals: true)));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            EveryTypeInput input = handlers.LastInput;
            Assert.Equal(-123, input.I32);
            Assert.Equal(9007199254740993L, input.I64);
            Assert.True(input.Flag);
            Assert.Equal("héllo", input.Name);
            Assert.Equal(PayloadBytes, input.Payload);
            Assert.Equal(7, input.OptI32);
            Assert.Equal(-9L, input.OptI64);
            Assert.False(input.OptFlag.Value);
            Assert.Equal("note", input.OptName);
            Assert.Equal(OptPayloadBytes, input.OptPayload);
            Assert.Empty(input.Pairs);
        }

        [Fact]
        public void OptionalNullInputs_MapToNullDtoValues()
        {
            var handlers = new EchoEveryTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", EveryTypeStatement(withOptionals: false)));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            EveryTypeInput input = handlers.LastInput;
            Assert.Null(input.OptI32);
            Assert.Null(input.OptI64);
            Assert.Null(input.OptFlag);
            Assert.Null(input.OptName);
            Assert.Null(input.OptPayload);
        }

        [Fact]
        public void ResultScalars_AreWrittenWithCorrectSqliteRepresentations()
        {
            var handlers = new EchoEveryTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", EveryTypeStatement(withOptionals: true)));

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            var rows = factory.LastWorkspace.Query(
                "SELECT status, result_i32, result_i64, result_flag, result_name, result_payload,"
                + " result_opt_i32, result_opt_flag FROM result_every_type WHERE call_id = 'c-1'",
                null,
                row => new
                {
                    Status = row.GetText(0),
                    I32 = row.GetInt32(1),
                    I64 = row.GetInt64(2),
                    Flag = row.GetInt64(3),
                    Name = row.GetText(4),
                    Payload = row.GetBlob(5),
                    OptI32 = row.IsNull(6) ? (int?)null : row.GetInt32(6),
                    OptFlag = row.IsNull(7) ? (long?)null : row.GetInt64(7)
                });

            var row0 = Assert.Single(rows);
            Assert.Equal("done", row0.Status);
            Assert.Equal(-123, row0.I32);
            Assert.Equal(9007199254740993L, row0.I64);
            Assert.Equal(1, row0.Flag);            // bool stored as INTEGER 1/0
            Assert.Equal("héllo", row0.Name);
            Assert.Equal(PayloadBytes, row0.Payload);
            Assert.Equal(7, row0.OptI32);
            Assert.Equal(0, row0.OptFlag);
        }

        [Fact]
        public void ResultOptionalNulls_AreWrittenAsNullColumns()
        {
            var handlers = new EchoEveryTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", EveryTypeStatement(withOptionals: false)));

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            var nullFlags = factory.LastWorkspace.Query(
                "SELECT result_opt_i32, result_opt_i64, result_opt_flag, result_opt_name, result_opt_payload"
                + " FROM result_every_type WHERE call_id = 'c-1'",
                null,
                row => new[] { row.IsNull(0), row.IsNull(1), row.IsNull(2), row.IsNull(3), row.IsNull(4) });

            Assert.All(Assert.Single(nullFlags), Assert.True);
        }

        [Fact]
        public void InputList_OrdersByItemIndex_NotInsertionOrder()
        {
            var handlers = new EchoEveryTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(
                Scripts.Step("only",
                    EveryTypeStatement(withOptionals: true),
                    Scripts.Statement(
                        "INSERT INTO call_every_type__input_pairs (call_id, item_index, input_k, input_opt_v)"
                        + " VALUES (:callId, 2, 'third', NULL), (:callId, 0, 'first', 10), (:callId, 1, 'second', NULL)",
                        ("callId", SqliteHostBindingValue.Text("c-1")))));

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            List<PairItem> pairs = handlers.LastInput.Pairs;
            Assert.Equal(3, pairs.Count);
            Assert.Equal("first", pairs[0].K);
            Assert.Equal(10L, pairs[0].OptV);
            Assert.Equal("second", pairs[1].K);
            Assert.Null(pairs[1].OptV);
            Assert.Equal("third", pairs[2].K);
        }

        [Fact]
        public void EmptyInputList_MapsToEmptyListAndWritesNoChildRows()
        {
            var handlers = new EchoEveryTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(Scripts.Step("only", EveryTypeStatement(withOptionals: true)));

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            Assert.NotNull(handlers.LastInput.Pairs);
            Assert.Empty(handlers.LastInput.Pairs);
            var childRows = factory.LastWorkspace.Query(
                "SELECT call_id FROM result_every_type__result_echo_pairs",
                null,
                row => row.GetText(0));
            Assert.Empty(childRows);
        }

        [Fact]
        public void ResultList_WritesChildRowsWithSequentialItemIndex()
        {
            var handlers = new EchoEveryTypeHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = CreateRuntime(handlers, factory);
            var script = Scripts.New(
                Scripts.Step("only",
                    EveryTypeStatement(withOptionals: true),
                    Scripts.Statement(
                        "INSERT INTO call_every_type__input_pairs (call_id, item_index, input_k, input_opt_v)"
                        + " VALUES (:callId, 0, 'a', 1), (:callId, 1, 'b', NULL)",
                        ("callId", SqliteHostBindingValue.Text("c-1")))));

            Assert.Equal(SqliteHostRunStatus.Completed, runtime.Run(script).Status);

            var childRows = factory.LastWorkspace.Query(
                "SELECT item_index, result_k, result_opt_v FROM result_every_type__result_echo_pairs"
                + " WHERE call_id = 'c-1' ORDER BY item_index",
                null,
                row => new
                {
                    Index = row.GetInt64(0),
                    K = row.GetText(1),
                    OptV = row.IsNull(2) ? (long?)null : row.GetInt64(2)
                });

            Assert.Equal(2, childRows.Count);
            Assert.Equal(0, childRows[0].Index);
            Assert.Equal("a", childRows[0].K);
            Assert.Equal(1L, childRows[0].OptV);
            Assert.Equal(1, childRows[1].Index);
            Assert.Equal("b", childRows[1].K);
            Assert.Null(childRows[1].OptV);
        }
    }
}
