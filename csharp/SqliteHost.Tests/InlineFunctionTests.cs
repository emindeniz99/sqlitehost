using System;
using System.Collections.Generic;
using Example.Game.Generated;
using Microsoft.Data.Sqlite;
using SqliteHost.Adapters.Native;
using SqliteHost.Conformance;
using SqliteHost.Tests.Adapter;
using SqliteHost.Tests.TestSupport;
using Xunit;

namespace SqliteHost.Tests
{
    /// <summary>
    /// Inline scalar functions (feature inlineFunctions,
    /// docs/proposals/inline-host-functions.md) driven end-to-end through
    /// the runtime, parameterized across every real adapter via the
    /// concrete subclasses at the bottom of this file. Same native-override
    /// policy as IntegrationFixtureTests.
    /// </summary>
    public abstract class InlineFunctionTestsBase
    {
        /// <summary>Opens one in-memory workspace on the adapter under test.</summary>
        protected abstract ISqliteHostConnection OpenAdapterConnection();

        /// <summary>See IntegrationFixtureTestsBase: true for adapters excluded from native-override matrix runs.</summary>
        protected virtual bool SkipUnderNativeOverride => false;

        private void SkipIfExcluded()
        {
            Skip.If(
                SkipUnderNativeOverride && NativeSqliteOverride.IsActive,
                "SQLITEHOST_NATIVE_SQLITE is set: the override is scoped to the Microsoft.Data.Sqlite "
                + "and SqliteHost.Adapters.Native adapters; this adapter bundles/loads its own native SQLite.");
            // Below the sample host's floor every runtime-driven scenario
            // would fail via the designed sqlite-version-too-low gate (see
            // FloorGateTests). The two CleanSkip_* tests never open a
            // workspace and run everywhere.
            SampleHostFloor.SkipBelowFloor();
        }

        private SqliteHostRunResult RunGeneratedHost(
            SqliteHostScript script,
            FakeGameHandlers handlers,
            AdapterWorkspaceFactory factory)
        {
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory,
                hostDefinition: GeneratedHostDefinition.Build(),
                handlers: handlers,
                options: null);
            return runtime.Run(script);
        }

        private static SqliteHostScript RequiringInlineFunctions(SqliteHostScript script)
        {
            script.RequiredFeatures = new List<string> { "inlineFunctions" };
            return script;
        }

        [SkippableFact]
        public void DualMode_SameMethodServesTheCallTableAndTheInlineFunction_InOneScript()
        {
            SkipIfExcluded();
            var handlers = new FakeGameHandlers();
            handlers.Storage["example-key"] = 10;
            using var factory = new ScalarFunctionCapableAdapterWorkspaceFactory(OpenAdapterConnection);

            SqliteHostRunResult result = RunGeneratedHost(
                RequiringInlineFunctions(Scripts.New(
                    Scripts.Step("read-queued",
                        Scripts.Statement(
                            "INSERT INTO call_get_value (call_id, input_key) VALUES ('read-1', 'example-key')")),
                    Scripts.Step("write-combined",
                        Scripts.Statement(
                            "INSERT INTO call_set_value (call_id, input_key, input_value)"
                            + " SELECT 'write-1', 'example-key', result_value + fn_get_value('example-key')"
                            + " FROM result_get_value WHERE call_id = 'read-1'")))),
                handlers, factory);

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            // The queued getValue and the setValue drained; the inline read
            // is counted separately and never enqueues.
            Assert.Equal(2, result.ExecutedCallCount);
            Assert.Equal(1, result.InlineCallCount);
            Assert.Equal(20, handlers.Storage["example-key"]);
            Assert.Equal(
                new[] { "getValue:example-key", "getValue:example-key", "setValue:example-key:20" },
                handlers.Log);
        }

        [Fact]
        public void CleanSkip_WhenTheFactoryIsNotCapable_WorkspaceNeverOpened()
        {
            var handlers = new FakeGameHandlers();
            // Plain factory: no ISqliteHostScalarFunctionCapableFactory marker.
            using var factory = new AdapterWorkspaceFactory(OpenAdapterConnection);

            SqliteHostRunResult result = RunGeneratedHost(
                RequiringInlineFunctions(Scripts.New(
                    Scripts.Step("only", Scripts.Statement("SELECT 1")))),
                handlers, factory);

            Assert.Equal(SqliteHostRunStatus.SkippedUnsupported, result.Status);
            Assert.Equal("missing-feature", result.ErrorCode);
            Assert.Equal(0, factory.OpenCount);
            Assert.Empty(handlers.Log);
        }

        [Fact]
        public void CleanSkip_WhenTheDefinitionHasNoInlineMethods_EvenOnACapableFactory()
        {
            var definition = SqliteHostDefinition
                .ForHandlers<object>()
                .ApiLevel(1)
                .Methods(new[]
                {
                    HostMethod
                        .For<object, PlainInput, PlainResult>("getValue")
                        .ApiLevel(1)
                        .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                        .Results(r => r.Long("value", x => x.Value))
                        .Handler((h, input) => new PlainResult { Value = 7 })
                        .Build()
                });
            using var factory = new ScalarFunctionCapableAdapterWorkspaceFactory(OpenAdapterConnection);
            var runtime = new SqliteHostRuntime<object>(
                connectionFactory: factory,
                hostDefinition: definition,
                handlers: new object(),
                options: null);

            SqliteHostRunResult result = runtime.Run(RequiringInlineFunctions(Scripts.New(
                Scripts.Step("only", Scripts.Statement("SELECT 1")))));

            Assert.Equal(SqliteHostRunStatus.SkippedUnsupported, result.Status);
            Assert.Equal("missing-feature", result.ErrorCode);
            Assert.Equal(0, factory.OpenCount);
        }

        [SkippableFact]
        public void HandlerThrow_InsideAnInlineFunction_MapsToFailedHandler_WithMethodAndMarker()
        {
            SkipIfExcluded();
            var handlers = new FakeGameHandlers();
            handlers.GetValueOverride = input => throw new InvalidOperationException("inline boom");
            using var factory = new ScalarFunctionCapableAdapterWorkspaceFactory(OpenAdapterConnection);

            SqliteHostRunResult result = RunGeneratedHost(
                RequiringInlineFunctions(Scripts.New(
                    Scripts.Step("write",
                        Scripts.Statement(
                            "INSERT INTO call_set_value (call_id, input_key, input_value)"
                            + " VALUES ('w-1', 'example-key', fn_get_value('example-key'))")))),
                handlers, factory);

            Assert.Equal(SqliteHostRunStatus.FailedHandler, result.Status);
            Assert.Equal("handler-error", result.ErrorCode);
            Assert.Equal("getValue", result.Method);
            Assert.Equal("write", result.StepId);
            Assert.Equal(0, result.StatementIndex);
            Assert.Contains("SQLITEHOST_HANDLER_ERROR:", result.ErrorMessage);
            Assert.Contains("inline boom", result.ErrorMessage);
            Assert.Equal(0, result.ExecutedCallCount);
            Assert.Equal(1, result.InlineCallCount);   // the invocation was made; it threw
        }

        [SkippableFact]
        public void UnknownFunctionName_AtRuntime_IsAPlainSqlError()
        {
            SkipIfExcluded();
            var handlers = new FakeGameHandlers();
            using var factory = new ScalarFunctionCapableAdapterWorkspaceFactory(OpenAdapterConnection);

            SqliteHostRunResult result = RunGeneratedHost(
                RequiringInlineFunctions(Scripts.New(
                    Scripts.Step("write",
                        Scripts.Statement(
                            "INSERT INTO call_set_value (call_id, input_key, input_value)"
                            + " VALUES ('w-1', 'example-key', fn_get_price('example-key'))")))),
                handlers, factory);

            Assert.Equal(SqliteHostRunStatus.FailedSql, result.Status);
            Assert.Equal("sql-error", result.ErrorCode);
            Assert.Null(result.Method);
            Assert.Equal(0, result.InlineCallCount);
            Assert.Empty(handlers.Log);
        }

        [SkippableFact]
        public void CustomFunctionPrefix_EndToEnd_UdfWorld()
        {
            SkipIfExcluded();
            var definition = SqliteHostDefinition
                .ForHandlers<object>()
                .ApiLevel(1)
                .Naming(n => n.FunctionPrefix("udf_"))
                .Methods(new[]
                {
                    HostMethod
                        .For<object, PlainInput, PlainResult>("getValue")
                        .ApiLevel(1)
                        .Inputs(i => i.Text("key", (x, v) => x.Key = v))
                        .Results(r => r.Long("value", x => x.Value))
                        .Inline("udf_get_value")
                        .Handler((h, input) => new PlainResult { Value = 7 })
                        .Build()
                });
            Assert.Equal("udf_", definition.Naming.FunctionPrefix);
            using var factory = new ScalarFunctionCapableAdapterWorkspaceFactory(
                OpenAdapterConnection, retainWorkspace: true);
            var runtime = new SqliteHostRuntime<object>(
                connectionFactory: factory,
                hostDefinition: definition,
                handlers: new object(),
                options: null);

            SqliteHostRunResult result = runtime.Run(RequiringInlineFunctions(Scripts.New(
                Scripts.Step("scratch", Scripts.Statement("CREATE TABLE scratch (v INTEGER)")),
                Scripts.Step("write",
                    Scripts.Statement("INSERT INTO scratch (v) VALUES (udf_get_value('k') * 3)")))));

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(1, result.InlineCallCount);
            Assert.Equal(0, result.ExecutedCallCount);
            var values = factory.LastWorkspace.Query(
                "SELECT v FROM scratch", null, row => row.GetInt64(0));
            Assert.Equal(new[] { 21L }, values);
        }

        [SkippableFact]
        public void RegistrationFailure_MapsToFailedSchema_InlineRegistrationError_BeforeAnyDdl()
        {
            SkipIfExcluded();
            var handlers = new FakeGameHandlers();
            using var factory = new ScalarFunctionCapableAdapterWorkspaceFactory(
                () => new RegistrationFailingConnection(OpenAdapterConnection()), retainWorkspace: true);

            SqliteHostRunResult result = RunGeneratedHost(
                RequiringInlineFunctions(Scripts.New(
                    Scripts.Step("only", Scripts.Statement("SELECT 1")))),
                handlers, factory);

            Assert.Equal(SqliteHostRunStatus.FailedSchema, result.Status);
            Assert.Equal("inline-registration-error", result.ErrorCode);
            Assert.Contains("registration denied", result.ErrorMessage);
            // Registration runs before the schema DDL: no table was created.
            Assert.ThrowsAny<Exception>(() => factory.LastWorkspace.Query(
                "SELECT call_id FROM call_get_value", null, row => row.GetText(0)));
        }

        /// <summary>Function-capable wrapper whose registration always fails; everything else delegates.</summary>
        private sealed class RegistrationFailingConnection : ISqliteHostScalarFunctionConnection
        {
            private readonly ISqliteHostConnection _inner;

            public RegistrationFailingConnection(ISqliteHostConnection inner)
            {
                _inner = inner;
            }

            public void RegisterScalarFunction(SqliteHostScalarFunction function)
                => throw new InvalidOperationException("registration denied");

            public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
                => _inner.Execute(sql, bindings);

            public IReadOnlyList<object> QueryRows(
                string sql,
                IReadOnlyList<SqliteHostBinding> bindings,
                Func<ISqliteHostRow, object> mapper)
                => _inner.QueryRows(sql, bindings, mapper);

            public void Dispose() => _inner.Dispose();
        }

        [SkippableFact]
        public void OptionalTrailingArg_RegistersBothArities_OmittedArgArrivesAsNull()
        {
            SkipIfExcluded();
            var definition = SqliteHostDefinition
                .ForHandlers<object>()
                .ApiLevel(1)
                .Methods(new[]
                {
                    HostMethod
                        .For<object, PickInput, PlainResult>("pick")
                        .ApiLevel(1)
                        .Inputs(i => i
                            .Text("key", (x, v) => x.Key = v)
                            .OptionalLong("fallback", (x, v) => x.Fallback = v))
                        .Results(r => r.Long("value", x => x.Value))
                        .Inline("fn_pick")
                        .Handler((h, input) => new PlainResult { Value = input.Fallback ?? 99 })
                        .Build()
                });
            using var factory = new ScalarFunctionCapableAdapterWorkspaceFactory(
                OpenAdapterConnection, retainWorkspace: true);
            var runtime = new SqliteHostRuntime<object>(
                connectionFactory: factory,
                hostDefinition: definition,
                handlers: new object(),
                options: null);

            SqliteHostRunResult result = runtime.Run(RequiringInlineFunctions(Scripts.New(
                Scripts.Step("scratch", Scripts.Statement("CREATE TABLE scratch (id INTEGER, v INTEGER)")),
                Scripts.Step("write",
                    Scripts.Statement(
                        "INSERT INTO scratch (id, v) VALUES"
                        + " (1, fn_pick('k')), (2, fn_pick('k', 5)), (3, fn_pick('k', NULL))")))));

            Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
            Assert.Equal(3, result.InlineCallCount);
            var values = factory.LastWorkspace.Query(
                "SELECT v FROM scratch ORDER BY id", null, row => row.GetInt64(0));
            Assert.Equal(new[] { 99L, 5L, 99L }, values);
        }

        private sealed class PlainInput
        {
            public string Key { get; set; }
        }

        private sealed class PickInput
        {
            public string Key { get; set; }
            public long? Fallback { get; set; }
        }

        private sealed class PlainResult
        {
            public long Value { get; set; }
        }
    }

    /// <summary>Inline function matrix on the Microsoft.Data.Sqlite adapter (SQLitePCLRaw; honors SQLITEHOST_NATIVE_SQLITE).</summary>
    public class MicrosoftDataSqliteInlineFunctionTests : InlineFunctionTestsBase
    {
        protected override ISqliteHostConnection OpenAdapterConnection()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return new MicrosoftDataSqliteConnection(connection);
        }
    }

    /// <summary>Inline function matrix on the System.Data.SQLite ADO.NET adapter (own bundled native).</summary>
    public class SystemDataSqliteInlineFunctionTests : InlineFunctionTestsBase
    {
        protected override bool SkipUnderNativeOverride => true;

        protected override ISqliteHostConnection OpenAdapterConnection()
            => SystemDataSqliteConnection.OpenInMemory();
    }

    /// <summary>Inline function matrix on the sqlite-net (Unity-style wrapper) adapter.</summary>
    public class SqliteNetInlineFunctionTests : InlineFunctionTestsBase
    {
        protected override bool SkipUnderNativeOverride => true;

        protected override ISqliteHostConnection OpenAdapterConnection()
            => SqliteNetConnection.OpenInMemory();
    }

    /// <summary>Inline function matrix on the shippable SqliteHost.Adapters.Native P/Invoke adapter (honors SQLITEHOST_NATIVE_SQLITE).</summary>
    public class NativeAdapterInlineFunctionTests : InlineFunctionTestsBase
    {
        protected override ISqliteHostConnection OpenAdapterConnection()
            => NativeSqliteHostConnection.OpenInMemory();
    }
}
