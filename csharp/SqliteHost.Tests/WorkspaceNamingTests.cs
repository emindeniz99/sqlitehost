using System;
using System.Collections.Generic;
using System.Linq;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Configurable workspace table names (docs/naming.md): queueTable,
    /// inputsTable, and varsTable flow from the definition's naming into the
    /// generated schema, the trigger bodies, and every runtime-issued query.
    /// </summary>
    public class WorkspaceNamingTests
    {
        private sealed class KeyInput
        {
            public string Key { get; set; }
        }

        private sealed class ValueResult
        {
            public long Value { get; set; }
        }

        private interface ICustomHandlers
        {
            ValueResult GetValue(KeyInput input);
        }

        private sealed class RecordingHandlers : ICustomHandlers
        {
            /// <summary>"getValue:&lt;key&gt;" entries in call order.</summary>
            public List<string> Log { get; } = new List<string>();

            public ValueResult GetValue(KeyInput input)
            {
                Log.Add("getValue:" + input.Key);
                return new ValueResult { Value = 7 };
            }
        }

        private static IHostMethodSpec<ICustomHandlers> GetValueSpec()
        {
            return HostMethod
                .For<ICustomHandlers, KeyInput, ValueResult>("getValue")
                .ApiLevel(1)
                .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                .Results(r => r.Long("value", x => x.Value))
                .Handler((handlers, input) => handlers.GetValue(input))
                .Build();
        }

        /// <summary>Single-method host with all four workspace tables renamed.</summary>
        private static SqliteHostDefinition<ICustomHandlers> BuildCustomNamedHost()
        {
            return SqliteHostDefinition
                .ForHandlers<ICustomHandlers>()
                .ApiLevel(1)
                .Naming(n => n
                    .QueueTable("host_queue")
                    .InputsTable("script_params")
                    .VarsTable("script_scratch")
                    .ControlTable("script_flow"))
                .Methods(new[] { GetValueSpec() });
        }

        [Fact]
        public void DefaultNaming_HasPinnedWorkspaceTableNames()
        {
            SqliteHostNaming naming = SqliteHostNaming.Default;
            Assert.Equal("pending_host_calls", naming.QueueTable);
            Assert.Equal("script_inputs", naming.InputsTable);
            Assert.Equal("script_vars", naming.VarsTable);
            Assert.Equal("script_control", naming.ControlTable);
        }

        [Fact]
        public void CustomWorkspaceNames_AppearInSchema_DefaultsDoNot()
        {
            var definition = BuildCustomNamedHost();

            Assert.Equal("host_queue", definition.Naming.QueueTable);
            Assert.Equal("script_params", definition.Naming.InputsTable);
            Assert.Equal("script_scratch", definition.Naming.VarsTable);
            Assert.Equal("script_flow", definition.Naming.ControlTable);

            string script = definition.GenerateSchemaScript();
            Assert.Contains("CREATE TABLE host_queue (", script);
            Assert.Contains("CREATE TABLE script_params (", script);
            Assert.Contains("CREATE TABLE script_scratch (", script);
            Assert.Contains("CREATE TABLE script_flow (", script);
            Assert.DoesNotContain("pending_host_calls", script);
            Assert.DoesNotContain("script_inputs", script);
            Assert.DoesNotContain("script_vars", script);
            Assert.DoesNotContain("script_control", script);
        }

        [Fact]
        public void CustomQueueName_AppearsInTriggerBody()
        {
            var definition = BuildCustomNamedHost();

            List<string> statements = definition.GenerateSchemaStatements().ToList();
            string trigger = Assert.Single(statements, s => s.StartsWith("CREATE TRIGGER "));
            Assert.StartsWith("CREATE TRIGGER trg_call_get_value_queue\n", trigger);
            Assert.Contains("    INSERT INTO host_queue (call_id, method)\n", trigger);
        }

        [Fact]
        public void CustomNamedHost_EndToEnd_QueueInputsAndVarsUseTheCustomTables()
        {
            var handlers = new RecordingHandlers();
            using var factory = new TestWorkspaceFactory(retainWorkspace: true);
            var runtime = new SqliteHostRuntime<ICustomHandlers>(
                connectionFactory: factory,
                hostDefinition: BuildCustomNamedHost(),
                handlers: handlers,
                options: null);

            // The call insert reads its key from the renamed inputs table, so
            // the run proves both the 'script_params' insert and the
            // 'host_queue' drain; the second statement uses 'script_scratch'.
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key)"
                        + " VALUES ('c-1', (SELECT text_value FROM script_params WHERE name = 'keyName'))"),
                    Scripts.Statement(
                        "INSERT INTO script_scratch (name, value_type, int_value) VALUES ('counter', 'int64', 42)")));
            script.Inputs = new List<SqliteHostRuntimeInput>
            {
                new SqliteHostRuntimeInput { Name = "keyName", Value = SqliteHostBindingValue.Text("gold") }
            };

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.ExecutedCallCount);
            Assert.Equal(new[] { "getValue:gold" }, handlers.Log);

            var workspace = factory.LastWorkspace;

            // The call drained through 'host_queue' and was marked done.
            var queueRows = workspace.Query(
                "SELECT call_id, method, status FROM host_queue ORDER BY queue_id",
                null,
                row => row.GetText(0) + "|" + row.GetText(1) + "|" + row.GetText(2));
            Assert.Equal(new[] { "c-1|getValue|done" }, queueRows);

            // The runtime input landed in 'script_params'.
            var inputRows = workspace.Query(
                "SELECT name, value_type, text_value FROM script_params ORDER BY name",
                null,
                row => row.GetText(0) + "|" + row.GetText(1) + "|" + row.GetText(2));
            Assert.Equal(new[] { "keyName|text|gold" }, inputRows);

            // The script's scratch var landed in 'script_scratch'.
            var varRows = workspace.Query(
                "SELECT name, int_value FROM script_scratch ORDER BY name",
                null,
                row => row.GetText(0) + "|" + row.GetInt64(1));
            Assert.Equal(new[] { "counter|42" }, varRows);

            // And the handler's result row was written as usual.
            var resultRows = workspace.Query(
                "SELECT call_id, status, result_value FROM result_get_value",
                null,
                row => row.GetText(0) + "|" + row.GetText(1) + "|" + row.GetInt64(2));
            Assert.Equal(new[] { "c-1|done|7" }, resultRows);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void EmptyWorkspaceTableName_ThrowsAtBuildTime(string bad)
        {
            var builder = SqliteHostDefinition
                .ForHandlers<ICustomHandlers>()
                .Naming(n => n.QueueTable(bad));

            var ex = Assert.Throws<ArgumentException>(
                () => builder.Methods(new[] { GetValueSpec() }));
            Assert.Contains("non-empty", ex.Message);
        }

        [Theory]
        [InlineData("pending_host_calls", "shared", "shared", "script_control")]
        [InlineData("shared", "shared", "script_vars", "script_control")]
        [InlineData("shared", "script_inputs", "shared", "script_control")]
        [InlineData("pending_host_calls", "script_inputs", "shared", "shared")]
        public void DuplicateWorkspaceTableNames_ThrowAtBuildTime(
            string queue, string inputs, string vars, string control)
        {
            var builder = SqliteHostDefinition
                .ForHandlers<ICustomHandlers>()
                .Naming(n => n.QueueTable(queue).InputsTable(inputs).VarsTable(vars).ControlTable(control));

            var ex = Assert.Throws<ArgumentException>(
                () => builder.Methods(new[] { GetValueSpec() }));
            Assert.Contains("mutually distinct", ex.Message);
        }

        [Fact]
        public void WorkspaceNameCollidingWithCallTable_ThrowsAtBuildTime()
        {
            var builder = SqliteHostDefinition
                .ForHandlers<ICustomHandlers>()
                .Naming(n => n.QueueTable("call_get_value"));

            var ex = Assert.Throws<ArgumentException>(
                () => builder.Methods(new[] { GetValueSpec() }));
            Assert.Contains("'call_get_value'", ex.Message);
            Assert.Contains("getValue", ex.Message);
        }

        [Fact]
        public void WorkspaceNameCollidingWithResultTable_ThrowsAtBuildTime()
        {
            var builder = SqliteHostDefinition
                .ForHandlers<ICustomHandlers>()
                .Naming(n => n.InputsTable("result_get_value"));

            var ex = Assert.Throws<ArgumentException>(
                () => builder.Methods(new[] { GetValueSpec() }));
            Assert.Contains("'result_get_value'", ex.Message);
        }

        [Fact]
        public void WorkspaceNameCollidingWithListChildTable_ThrowsAtBuildTime()
        {
            var listSpec = HostMethod
                .For<ICustomHandlers, EveryTypeInput, EveryTypeResult>("getValues")
                .Inputs(i => i
                    .List<PairItem>("keys", (x, v) => x.Pairs = v, item => item
                        .Text("key", (p, v) => p.K = v)))
                .Results(r => r
                    .List<PairItem>("entries", x => x.EchoPairs, item => item
                        .Text("key", p => p.K)))
                .Handler((handlers, input) => new EveryTypeResult())
                .Build();

            var callChildBuilder = SqliteHostDefinition
                .ForHandlers<ICustomHandlers>()
                .Naming(n => n.VarsTable("call_get_values__input_keys"));
            var ex = Assert.Throws<ArgumentException>(
                () => callChildBuilder.Methods(new[] { listSpec }));
            Assert.Contains("'call_get_values__input_keys'", ex.Message);

            var resultChildBuilder = SqliteHostDefinition
                .ForHandlers<ICustomHandlers>()
                .Naming(n => n.VarsTable("result_get_values__result_entries"));
            ex = Assert.Throws<ArgumentException>(
                () => resultChildBuilder.Methods(new[] { listSpec }));
            Assert.Contains("'result_get_values__result_entries'", ex.Message);
        }
    }
}
