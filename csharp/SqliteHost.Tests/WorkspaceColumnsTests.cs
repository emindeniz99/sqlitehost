using System;
using System.Collections.Generic;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Configurable shared column names and the done literal
    /// (docs/naming.md): the definition's Columns flow into the generated
    /// schema and every runtime-issued SQL statement — queue drain, trigger
    /// bodies, inputs insert, result writes, list child reads, and the
    /// script_control check.
    /// </summary>
    public class WorkspaceColumnsTests
    {
        private sealed class KeyInput
        {
            public string Key { get; set; }
        }

        private sealed class ValueResult
        {
            public long Value { get; set; }
        }

        private interface IColumnsHandlers
        {
            ValueResult GetValue(KeyInput input);
            EveryTypeResult EchoPairs(EveryTypeInput input);
        }

        private sealed class RecordingHandlers : IColumnsHandlers
        {
            /// <summary>"getValue:&lt;key&gt;" / "echoPairs:&lt;count&gt;" entries in call order.</summary>
            public List<string> Log { get; } = new List<string>();

            /// <summary>The pair keys of the last EchoPairs input, in mapped order.</summary>
            public List<string> LastPairKeys { get; } = new List<string>();

            public ValueResult GetValue(KeyInput input)
            {
                Log.Add("getValue:" + input.Key);
                return new ValueResult { Value = 7 };
            }

            public EveryTypeResult EchoPairs(EveryTypeInput input)
            {
                Log.Add("echoPairs:" + input.Pairs.Count);
                LastPairKeys.Clear();
                foreach (PairItem pair in input.Pairs)
                {
                    LastPairKeys.Add(pair.K);
                }
                return new EveryTypeResult { EchoPairs = input.Pairs };
            }
        }

        private static IHostMethodSpec<IColumnsHandlers>[] Specs()
        {
            return new[]
            {
                HostMethod
                    .For<IColumnsHandlers, KeyInput, ValueResult>("getValue")
                    .ApiLevel(1)
                    .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                    .Results(r => r.Long("value", x => x.Value))
                    .Handler((handlers, input) => handlers.GetValue(input))
                    .Build(),
                HostMethod
                    .For<IColumnsHandlers, EveryTypeInput, EveryTypeResult>("echoPairs")
                    .ApiLevel(1)
                    .Inputs(i => i
                        .List<PairItem>("pairs", (x, v) => x.Pairs = v, item => item
                            .Text("k", (p, v) => p.K = v)))
                    .Results(r => r
                        .List<PairItem>("echo", x => x.EchoPairs, item => item
                            .Text("k", p => p.K)))
                    .Handler((handlers, input) => handlers.EchoPairs(input))
                    .Build()
            };
        }

        /// <summary>Two-method host with all fourteen column values overridden.</summary>
        private static SqliteHostDefinition<IColumnsHandlers> BuildCustomColumnsHost()
        {
            return SqliteHostDefinition
                .ForHandlers<IColumnsHandlers>()
                .ApiLevel(1)
                .Columns(c => c
                    .CallId("cid")
                    .ItemIndex("idx")
                    .Status("state")
                    .DoneValue("ok")
                    .QueueId("qid")
                    .Method("op")
                    .Name("var_name")
                    .ValueType("vtype")
                    .IntValue("ival")
                    .RealValue("rval")
                    .TextValue("tval")
                    .BlobValue("bval")
                    .Action("cmd")
                    .Message("msg"))
                .Methods(Specs());
        }

        [Fact]
        public void DefaultColumns_HavePinnedProtocolNames()
        {
            SqliteHostColumns columns = SqliteHostColumns.Default;
            Assert.Equal("call_id", columns.CallId);
            Assert.Equal("item_index", columns.ItemIndex);
            Assert.Equal("status", columns.Status);
            Assert.Equal("done", columns.DoneValue);
            Assert.Equal("queue_id", columns.QueueId);
            Assert.Equal("method", columns.Method);
            Assert.Equal("name", columns.Name);
            Assert.Equal("value_type", columns.ValueType);
            Assert.Equal("int_value", columns.IntValue);
            Assert.Equal("real_value", columns.RealValue);
            Assert.Equal("text_value", columns.TextValue);
            Assert.Equal("blob_value", columns.BlobValue);
            Assert.Equal("action", columns.Action);
            Assert.Equal("message", columns.Message);
        }

        [Fact]
        public void CustomColumns_AppearInEveryDdlStatement_DefaultsDoNot()
        {
            var definition = BuildCustomColumnsHost();
            Assert.Equal("cid", definition.Columns.CallId);
            Assert.Equal("ok", definition.Columns.DoneValue);

            string ddl = definition.GenerateSchemaScript();

            // Queue table.
            Assert.Contains("    qid INTEGER PRIMARY KEY AUTOINCREMENT,\n", ddl);
            Assert.Contains("    cid TEXT NOT NULL UNIQUE,\n", ddl);
            Assert.Contains("    op TEXT NOT NULL,\n", ddl);
            Assert.Contains("    state TEXT NOT NULL DEFAULT 'pending'\n", ddl);
            // Inputs/vars tables.
            Assert.Contains("    var_name TEXT NOT NULL PRIMARY KEY,\n", ddl);
            Assert.Contains("    vtype TEXT NOT NULL,\n", ddl);
            Assert.Contains("    ival INTEGER,\n", ddl);
            Assert.Contains("    rval REAL,\n", ddl);
            Assert.Contains("    tval TEXT,\n", ddl);
            Assert.Contains("    bval BLOB\n", ddl);
            // Control table.
            Assert.Contains("    cmd TEXT NOT NULL,\n", ddl);
            Assert.Contains("    msg TEXT\n", ddl);
            // Parent/result tables and the configurable done literal.
            Assert.Contains("    cid TEXT NOT NULL PRIMARY KEY,\n", ddl);
            Assert.Contains("    state TEXT NOT NULL DEFAULT 'ok',\n", ddl);
            // List child tables.
            Assert.Contains("    idx INTEGER NOT NULL,\n", ddl);
            Assert.Contains("    PRIMARY KEY (cid, idx)\n", ddl);
            // Trigger bodies.
            Assert.Contains("    INSERT INTO pending_host_calls (cid, op)\n", ddl);
            Assert.Contains("    VALUES (NEW.cid, 'getValue');\n", ddl);

            // None of the default names leak into the DDL.
            Assert.DoesNotContain("call_id", ddl);
            Assert.DoesNotContain("item_index", ddl);
            Assert.DoesNotContain("status", ddl);
            Assert.DoesNotContain("queue_id", ddl);
            Assert.DoesNotContain("value_type", ddl);
            Assert.DoesNotContain("int_value", ddl);
            Assert.DoesNotContain("real_value", ddl);
            Assert.DoesNotContain("text_value", ddl);
            Assert.DoesNotContain("blob_value", ddl);
            Assert.DoesNotContain("'done'", ddl);
        }

        [SkippableFact]
        public void CustomColumnsHost_EndToEnd_DrainResultsListsAndHaltUseTheCustomNames()
        {
            SampleHostFloor.SkipBelowFloor();
            var handlers = new RecordingHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = new SqliteHostRuntime<IColumnsHandlers>(
                connectionFactory: factory,
                hostDefinition: BuildCustomColumnsHost(),
                handlers: handlers,
                options: null);

            var script = Scripts.New(
                // The call insert reads its key from script_inputs through
                // the renamed name/text-value columns.
                Scripts.Step("read",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (cid, input_key)"
                        + " VALUES ('c-1', (SELECT tval FROM script_inputs WHERE var_name = 'keyName'))")),
                // Script-side filtering on the renamed status column and the
                // custom done literal.
                Scripts.Step("filter",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (cid, input_key)"
                        + " SELECT 'c-2', 'silver' FROM result_get_value"
                        + " WHERE cid = 'c-1' AND state = 'ok' AND result_value = 7")),
                // List child rows keyed by the renamed cid/idx columns; idx
                // values are written out of order and have a gap.
                Scripts.Step("list",
                    Scripts.Statement("INSERT INTO call_echo_pairs (cid) VALUES ('e-1')"),
                    Scripts.Statement(
                        "INSERT INTO call_echo_pairs__input_pairs (cid, idx, input_k) VALUES ('e-1', 5, 'zz')"),
                    Scripts.Statement(
                        "INSERT INTO call_echo_pairs__input_pairs (cid, idx, input_k) VALUES ('e-1', 2, 'aa')")),
                // Halt through the renamed action/message columns.
                Scripts.Step("stop",
                    Scripts.Statement("INSERT INTO script_control (cmd, msg) VALUES ('halt', 'done early')")),
                Scripts.Step("never",
                    Scripts.Statement("INSERT INTO call_get_value (cid, input_key) VALUES ('c-3', 'never')")));
            script.Inputs = new List<SqliteHostRuntimeInput>
            {
                new SqliteHostRuntimeInput { Name = "keyName", Value = SqliteHostBindingValue.Text("gold") }
            };

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.True(result.Halted);
            Assert.Equal("done early", result.HaltMessage);
            Assert.Equal("stop", result.StepId);
            Assert.Equal(3, result.ExecutedCallCount);
            Assert.Equal(new[] { "getValue:gold", "getValue:silver", "echoPairs:2" }, handlers.Log);

            // Input list child rows reached the handler ordered by idx.
            Assert.Equal(new[] { "aa", "zz" }, handlers.LastPairKeys);

            var workspace = factory.LastWorkspace;

            // The runtime input landed through the renamed inputs columns.
            var inputRows = workspace.Query(
                "SELECT var_name, vtype, tval FROM script_inputs",
                null,
                row => row.GetText(0) + "|" + row.GetText(1) + "|" + row.GetText(2));
            Assert.Equal(new[] { "keyName|text|gold" }, inputRows);

            // Every queue row drained and was marked with the custom done
            // literal in the renamed status column.
            var queueRows = workspace.Query(
                "SELECT cid, op, state FROM pending_host_calls ORDER BY qid",
                null,
                row => row.GetText(0) + "|" + row.GetText(1) + "|" + row.GetText(2));
            Assert.Equal(new[] { "c-1|getValue|ok", "c-2|getValue|ok", "e-1|echoPairs|ok" }, queueRows);

            // Result rows carry state = 'ok'.
            var resultRows = workspace.Query(
                "SELECT cid, state, result_value FROM result_get_value ORDER BY cid",
                null,
                row => row.GetText(0) + "|" + row.GetText(1) + "|" + row.GetInt64(2));
            Assert.Equal(new[] { "c-1|ok|7", "c-2|ok|7" }, resultRows);

            // Result list child rows were written with the renamed cid/idx.
            var echoRows = workspace.Query(
                "SELECT idx, result_k FROM result_echo_pairs__result_echo WHERE cid = 'e-1' ORDER BY idx",
                null,
                row => row.GetInt64(0) + "|" + row.GetText(1));
            Assert.Equal(new[] { "0|aa", "1|zz" }, echoRows);

            // The step after the halt never ran.
            var callIds = workspace.Query(
                "SELECT cid FROM call_get_value ORDER BY cid",
                null,
                row => row.GetText(0));
            Assert.Equal(new[] { "c-1", "c-2" }, callIds);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void EmptyColumnName_ThrowsAtBuildTime(string bad)
        {
            var builder = SqliteHostDefinition
                .ForHandlers<IColumnsHandlers>()
                .Columns(c => c.CallId(bad));

            var ex = Assert.Throws<ArgumentException>(() => builder.Methods(Specs()));
            Assert.Contains("non-empty", ex.Message);
        }

        [Fact]
        public void EmptyDoneValue_ThrowsAtBuildTime()
        {
            var builder = SqliteHostDefinition
                .ForHandlers<IColumnsHandlers>()
                .Columns(c => c.DoneValue(""));

            var ex = Assert.Throws<ArgumentException>(() => builder.Methods(Specs()));
            Assert.Contains("non-empty", ex.Message);
        }

        [Theory]
        [InlineData("qid", "qid", "method", "status")]        // queue-id vs call-id
        [InlineData("queue_id", "call_id", "shared", "shared")] // method vs status
        public void DuplicateColumnWithinQueueTable_ThrowsAtBuildTime(
            string queueId, string callId, string method, string status)
        {
            var builder = SqliteHostDefinition
                .ForHandlers<IColumnsHandlers>()
                .Columns(c => c.QueueId(queueId).CallId(callId).Method(method).Status(status));

            var ex = Assert.Throws<ArgumentException>(() => builder.Methods(Specs()));
            Assert.Contains("occurs more than once", ex.Message);
            Assert.Contains("queue", ex.Message);
        }

        [Fact]
        public void DuplicateColumnWithinInputsTable_ThrowsAtBuildTime()
        {
            var builder = SqliteHostDefinition
                .ForHandlers<IColumnsHandlers>()
                .Columns(c => c.IntValue("shared").RealValue("shared"));

            var ex = Assert.Throws<ArgumentException>(() => builder.Methods(Specs()));
            Assert.Contains("occurs more than once", ex.Message);
            Assert.Contains("inputs/vars", ex.Message);
        }

        [Fact]
        public void DuplicateColumnWithinControlTable_ThrowsAtBuildTime()
        {
            var builder = SqliteHostDefinition
                .ForHandlers<IColumnsHandlers>()
                .Columns(c => c.Action("cmd").Message("cmd"));

            var ex = Assert.Throws<ArgumentException>(() => builder.Methods(Specs()));
            Assert.Contains("occurs more than once", ex.Message);
            Assert.Contains("control", ex.Message);
        }

        [Fact]
        public void CallIdCollidingWithDerivedInputColumn_ThrowsAtBuildTime()
        {
            // getValue's "key" input derives input_key.
            var builder = SqliteHostDefinition
                .ForHandlers<IColumnsHandlers>()
                .Columns(c => c.CallId("input_key"));

            var ex = Assert.Throws<ArgumentException>(() => builder.Methods(Specs()));
            Assert.Contains("'input_key'", ex.Message);
            Assert.Contains("getValue", ex.Message);
        }

        [Fact]
        public void StatusCollidingWithDerivedResultColumn_ThrowsAtBuildTime()
        {
            // getValue's "value" result derives result_value.
            var builder = SqliteHostDefinition
                .ForHandlers<IColumnsHandlers>()
                .Columns(c => c.Status("result_value"));

            var ex = Assert.Throws<ArgumentException>(() => builder.Methods(Specs()));
            Assert.Contains("'result_value'", ex.Message);
        }

        [Fact]
        public void ItemIndexCollidingWithDerivedListItemColumn_ThrowsAtBuildTime()
        {
            // echoPairs' list item "k" derives input_k in the child table.
            var builder = SqliteHostDefinition
                .ForHandlers<IColumnsHandlers>()
                .Columns(c => c.ItemIndex("input_k"));

            var ex = Assert.Throws<ArgumentException>(() => builder.Methods(Specs()));
            Assert.Contains("'input_k'", ex.Message);
            Assert.Contains("echoPairs", ex.Message);
        }
    }
}
