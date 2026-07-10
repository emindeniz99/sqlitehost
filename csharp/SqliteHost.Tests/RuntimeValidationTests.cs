using System.Collections.Generic;
using Example.Game.Generated;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    public class RuntimeValidationTests
    {
        private static (SqliteHostRuntime<IGeneratedHostHandlers> Runtime, TestWorkspaceFactory Factory, FakeGameHandlers Handlers)
            CreateRuntime(SqliteHostRuntimeOptions options = null)
        {
            var factory = new TestWorkspaceFactory();
            var handlers = new FakeGameHandlers();
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory,
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: options);
            return (runtime, factory, handlers);
        }

        private static SqliteHostScript ValidSingleCallScript()
        {
            return Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'k')",
                        ("callId", SqliteHostBindingValue.Text("c-1")))));
        }

        [Fact]
        public void UnsupportedEngine_IsCleanSkip_WorkspaceNeverOpened()
        {
            var (runtime, factory, handlers) = CreateRuntime();
            SqliteHostScript script = ValidSingleCallScript();
            script.Engine = "some-other-engine";

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.SkippedUnsupported, result.Status);
            Assert.Equal("unsupported-engine", result.ErrorCode);
            Assert.Equal(0, factory.OpenCount);
            Assert.Empty(handlers.Log);
            Assert.Equal(-1, result.StatementIndex);
        }

        [Fact]
        public void RequiredApiLevelAboveHost_IsCleanSkip_WorkspaceNeverOpened()
        {
            var (runtime, factory, _) = CreateRuntime();
            SqliteHostScript script = ValidSingleCallScript();
            script.RequiredApiLevel = 999;

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.SkippedUnsupported, result.Status);
            Assert.Equal("unsupported-api-level", result.ErrorCode);
            Assert.Equal(0, factory.OpenCount);
        }

        [Fact]
        public void UnknownRequiredFeature_IsCleanSkip_WorkspaceNeverOpened()
        {
            var (runtime, factory, _) = CreateRuntime();
            SqliteHostScript script = ValidSingleCallScript();
            script.RequiredFeatures = new List<string> { "typedNamedBindings", "futureFeature" };

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.SkippedUnsupported, result.Status);
            Assert.Equal("missing-feature", result.ErrorCode);
            Assert.Equal(0, factory.OpenCount);
        }

        [Fact]
        public void UnknownRequiredMethod_IsCleanSkip_WorkspaceNeverOpened()
        {
            var (runtime, factory, _) = CreateRuntime();
            SqliteHostScript script = ValidSingleCallScript();
            script.RequiredMethods = new List<string> { "getValue", "notARealMethod" };

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.SkippedUnsupported, result.Status);
            Assert.Equal("missing-method", result.ErrorCode);
            Assert.Equal("notARealMethod", result.Method);
            Assert.Equal(0, factory.OpenCount);
        }

        [Fact]
        public void NullScript_FailsValidation()
        {
            var (runtime, factory, _) = CreateRuntime();
            SqliteHostRunResult result = runtime.Run(null);
            Assert.Equal(SqliteHostRunStatus.FailedValidation, result.Status);
            Assert.Equal("invalid-script", result.ErrorCode);
            Assert.Equal(0, factory.OpenCount);
        }

        [Fact]
        public void NullOrEmptySteps_FailValidation()
        {
            var (runtime, _, _) = CreateRuntime();

            SqliteHostScript nullSteps = ValidSingleCallScript();
            nullSteps.Steps = null;
            Assert.Equal("invalid-script", runtime.Run(nullSteps).ErrorCode);

            SqliteHostScript emptySteps = ValidSingleCallScript();
            emptySteps.Steps = new List<SqliteHostStep>();
            SqliteHostRunResult result = runtime.Run(emptySteps);
            Assert.Equal(SqliteHostRunStatus.FailedValidation, result.Status);
            Assert.Equal("invalid-script", result.ErrorCode);
        }

        [Fact]
        public void EmptyStepId_FailsValidation()
        {
            var (runtime, _, _) = CreateRuntime();
            SqliteHostScript script = ValidSingleCallScript();
            script.Steps[0].Id = "";
            SqliteHostRunResult result = runtime.Run(script);
            Assert.Equal(SqliteHostRunStatus.FailedValidation, result.Status);
            Assert.Equal("invalid-script", result.ErrorCode);
        }

        [Fact]
        public void NullStatementSql_FailsValidation()
        {
            var (runtime, _, _) = CreateRuntime();
            SqliteHostScript script = ValidSingleCallScript();
            script.Steps[0].Statements[0].Sql = null;
            SqliteHostRunResult result = runtime.Run(script);
            Assert.Equal(SqliteHostRunStatus.FailedValidation, result.Status);
            Assert.Equal("invalid-script", result.ErrorCode);
        }

        [Fact]
        public void DuplicateStepId_FailsValidation()
        {
            var (runtime, _, _) = CreateRuntime();
            var script = Scripts.New(
                Scripts.Step("dup", Scripts.Statement("SELECT 1")),
                Scripts.Step("dup", Scripts.Statement("SELECT 2")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedValidation, result.Status);
            Assert.Equal("duplicate-step-id", result.ErrorCode);
            Assert.Equal("dup", result.StepId);
        }

        [Fact]
        public void MaxStatementsPerRunExceeded_FailsValidation_BeforeWorkspaceOpen()
        {
            var (runtime, factory, _) = CreateRuntime(new SqliteHostRuntimeOptions
            {
                MaxStatementsPerRun = 1
            });
            var script = Scripts.New(
                Scripts.Step("s1", Scripts.Statement("SELECT 1"), Scripts.Statement("SELECT 2")));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedValidation, result.Status);
            Assert.Equal("max-statements-exceeded", result.ErrorCode);
            Assert.Equal(0, factory.OpenCount);
        }

        [Fact]
        public void MissingBinding_FailsBinding_WithStatementContext()
        {
            var (runtime, _, handlers) = CreateRuntime();
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, :missingKey)",
                        ("callId", SqliteHostBindingValue.Text("c-1")))));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedBinding, result.Status);
            Assert.Equal("missing-binding", result.ErrorCode);
            Assert.Equal("only", result.StepId);
            Assert.Equal(0, result.StatementIndex);
            Assert.Contains("missingKey", result.ErrorMessage);
            Assert.Empty(handlers.Log);
        }

        [Fact]
        public void UnusedBinding_FailsBinding()
        {
            var (runtime, _, _) = CreateRuntime();
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'k')",
                        ("callId", SqliteHostBindingValue.Text("c-1")),
                        ("extra", SqliteHostBindingValue.Int64(1)))));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.FailedBinding, result.Status);
            Assert.Equal("unused-binding", result.ErrorCode);
            Assert.Contains("extra", result.ErrorMessage);
            Assert.Equal(0, result.StatementIndex);
        }

        [Fact]
        public void ParametersInsideCommentsAndLiterals_AreNotMissingBindings()
        {
            var (runtime, _, _) = CreateRuntime();
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key) -- :ghost\n"
                        + "VALUES (:callId, ':ghost2' /* @ghost3 */)",
                        ("callId", SqliteHostBindingValue.Text("c-1")))));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
        }

        [Fact]
        public void ValidateBindingsOff_SkipsBindingValidation()
        {
            var (runtime, _, _) = CreateRuntime(new SqliteHostRuntimeOptions
            {
                ValidateBindings = false
            });
            var script = Scripts.New(
                Scripts.Step("only",
                    Scripts.Statement(
                        "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'k')",
                        ("callId", SqliteHostBindingValue.Text("c-1")),
                        ("extra", SqliteHostBindingValue.Int64(1)))));

            SqliteHostRunResult result = runtime.Run(script);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
        }

        [Fact]
        public void WorkspaceIsDisposedAfterRun()
        {
            var (runtime, factory, _) = CreateRuntime();
            SqliteHostRunResult result = runtime.Run(ValidSingleCallScript());
            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, factory.OpenCount);
            Assert.True(factory.LastWorkspace.IsDisposed);
        }
    }
}
