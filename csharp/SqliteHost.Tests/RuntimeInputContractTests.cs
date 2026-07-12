using System.IO;
using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.Fixtures;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Runtime input contract: inputs are optional, duplicate input names
    /// fail structurally before the workspace opens (docs/errors.md
    /// duplicate-input-name), unknown value types are rejected by the
    /// test-side JSON loader, and mixed-prefix binding resolution works end
    /// to end (fixtures example-007).
    /// </summary>
    public class RuntimeInputContractTests
    {
        private static (SqliteHostRuntime<IGeneratedHostHandlers> Runtime, TestWorkspaceFactory Factory, FakeGameHandlers Handlers)
            CreateRuntime()
        {
            var factory = new TestWorkspaceFactory();
            var handlers = new FakeGameHandlers();
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory,
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: null);
            return (runtime, factory, handlers);
        }

        [SkippableFact]
        public void InputsAreOptional_ScriptWithoutInputsCompletes()
        {
            SampleHostFloor.SkipBelowFloor();
            var (runtime, _, _) = CreateRuntime();
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'k')",
                        ("callId", SqliteHostBindingValue.Text("c-1")))));
            Assert.Null(script.Inputs);

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
        }

        [Fact]
        public void DuplicateInputName_Fixture_FailsValidation_BeforeWorkspaceOpen()
        {
            var (runtime, factory, handlers) = CreateRuntime();
            SqliteHostScript script = ScriptEnvelopeJson.LoadPayload("invalid/duplicate-input-name.json");

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedValidation, result.Status);
            Assert.Equal("duplicate-input-name", result.ErrorCode);
            Assert.Contains("targetValue", result.ErrorMessage);
            Assert.Equal(0, factory.OpenCount);
            Assert.Empty(handlers.Log);
        }

        [Fact]
        public void UnknownInputValueType_IsRejectedByTheTestJsonLoader()
        {
            const string json = @"{
              ""engine"": ""sqlite-host-v1"",
              ""scriptId"": ""bad-type"",
              ""requiredApiLevel"": 1,
              ""inputs"": [
                { ""name"": ""when"", ""value"": { ""type"": ""date"", ""value"": ""2026-07-10"" } }
              ],
              ""steps"": [
                { ""id"": ""only"", ""statements"": [ { ""sql"": ""SELECT 1"" } ] }
              ]
            }";
            var ex = Assert.Throws<InvalidDataException>(() => ScriptEnvelopeJson.Parse(json));
            Assert.Contains("date", ex.Message);
        }

        [SkippableFact]
        public void Example007_MixedPrefixSameName_RunsCompleted()
        {
            // :callId and $callId in one statement are fed by the single
            // prefixless binding "callId" (docs/adapter-contract.md).
            SampleHostFloor.SkipBelowFloor();
            var (runtime, _, handlers) = CreateRuntime();
            handlers.Storage["example-key"] = 5;

            SqliteHostRunResult result = runtime.Run(
                ScriptEnvelopeJson.LoadPayload("valid/example-007-mixed-prefix.json"));

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.ExecutedCallCount);
            Assert.Equal(new[] { "getValue:example-key" }, handlers.Log);
        }
    }
}
