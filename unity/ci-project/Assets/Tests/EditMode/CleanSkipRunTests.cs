// EditMode tests for the com.sqlitehost.runtime UPM package, run by
// GameCI's unity-test-runner (.github/workflows/unity-ci.yml) inside a real
// Unity editor at the package's declared floor (package.json "unity":
// "2021.3"). Unity-2021-safe C# — same constraint as the package sources.
//
// Why these assertions and not others: the package ships no native SQLite,
// so the only behaviour it can prove without an adapter is the pinned
// "clean skip" contract (docs/csharp-api.md, docs/errors.md) — a script the
// host cannot serve is rejected by the precheck BEFORE a workspace is ever
// opened. The fake factory records whether OpenWorkspace() was called and
// the fake connection throws on any SQL, so a refactor that moves a
// precheck behind the connection factory fails here instead of shipping.
//
// Compiling at all is the second half of the job: a red compile in this
// project means the package does not build on the version its package.json
// claims to support.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using SqliteHost;

namespace SqliteHostCiProject.Tests
{
    public sealed class CleanSkipRunTests
    {
        private const string EngineV1 = "sqlite-host-v1";

        [Test]
        public void UnsupportedEngine_IsSkippedWithoutOpeningAWorkspace()
        {
            var factory = new RecordingFakeConnectionFactory();
            SqliteHostRuntime<object> runtime = NewRuntime(factory);

            SqliteHostRunResult result = runtime.Run(
                NewScript("sqlite-host-v999-ci", new List<string>()));

            Assert.AreEqual(SqliteHostRunStatus.SkippedUnsupported, result.Status, result.ErrorMessage);
            Assert.AreEqual("unsupported-engine", result.ErrorCode);
            Assert.IsFalse(
                factory.WorkspaceOpened,
                "a script the host cannot serve must be skipped before a workspace is opened");
        }

        [Test]
        public void MissingRequiredMethod_IsSkippedWithoutOpeningAWorkspace()
        {
            var factory = new RecordingFakeConnectionFactory();
            SqliteHostRuntime<object> runtime = NewRuntime(factory);

            SqliteHostRunResult result = runtime.Run(
                NewScript(EngineV1, new List<string> { "getValue" }));

            Assert.AreEqual(SqliteHostRunStatus.SkippedUnsupported, result.Status, result.ErrorMessage);
            Assert.AreEqual("missing-method", result.ErrorCode);
            Assert.AreEqual("getValue", result.Method);
            Assert.IsFalse(
                factory.WorkspaceOpened,
                "an unregistered required method must be caught by the precheck, not by SQL");
        }

        [Test]
        public void SchemaGeneration_EmitsTheWorkspaceTables()
        {
            SqliteHostDefinition<object> definition = NewDefinition();

            IReadOnlyList<string> statements = definition.GenerateSchemaStatements();

            Assert.Greater(statements.Count, 0, "a host with no methods still owns the workspace tables");
            string script = definition.GenerateSchemaScript();
            StringAssert.Contains(definition.Naming.QueueTable, script);
            StringAssert.Contains(definition.Naming.InputsTable, script);
            StringAssert.Contains(definition.Naming.VarsTable, script);
            StringAssert.Contains(definition.Naming.ControlTable, script);
        }

        /// <summary>
        /// A host with zero registered methods: enough to exercise the
        /// precheck and the workspace schema, and it keeps the test free of
        /// the generated sample (which lives in Samples~ and is not part of
        /// the compiled package).
        /// </summary>
        private static SqliteHostDefinition<object> NewDefinition()
        {
            return SqliteHostDefinition
                .ForHandlers<object>()
                .ApiLevel(1)
                .Methods(new List<IHostMethodSpec<object>>());
        }

        private static SqliteHostRuntime<object> NewRuntime(ISqliteHostConnectionFactory factory)
        {
            return new SqliteHostRuntime<object>(
                connectionFactory: factory,
                hostDefinition: NewDefinition(),
                handlers: new object(),
                options: null);
        }

        private static SqliteHostScript NewScript(string engine, List<string> requiredMethods)
        {
            return new SqliteHostScript
            {
                Engine = engine,
                ScriptId = "unity-ci-clean-skip",
                RequiredApiLevel = 1,
                RequiredFeatures = new List<string>(),
                RequiredMethods = requiredMethods,
                Inputs = new List<SqliteHostRuntimeInput>(),
                Steps = new List<SqliteHostStep>
                {
                    new SqliteHostStep
                    {
                        Id = "never-runs",
                        Statements = new List<SqliteHostStatement>
                        {
                            new SqliteHostStatement
                            {
                                Sql = "INSERT INTO call_get_value (call_id, input_key) VALUES ('c1', 'hello')",
                                Bindings = new Dictionary<string, SqliteHostBindingValue>()
                            }
                        }
                    }
                }
            };
        }
    }

    /// <summary>Records whether the runtime ever asked for a workspace.</summary>
    internal sealed class RecordingFakeConnectionFactory : ISqliteHostConnectionFactory
    {
        public bool WorkspaceOpened { get; private set; }

        public ISqliteHostConnection OpenWorkspace()
        {
            WorkspaceOpened = true;
            return new FailingFakeConnection();
        }
    }

    /// <summary>Any SQL reaching this connection is a failed test, by design.</summary>
    internal sealed class FailingFakeConnection : ISqliteHostConnection
    {
        public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            throw new NotSupportedException(
                "a clean-skip run must execute no SQL; attempted: " + sql);
        }

        public IReadOnlyList<object> QueryRows(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, object> mapper)
        {
            throw new NotSupportedException(
                "a clean-skip run must query no SQL; attempted: " + sql);
        }

        public void Dispose()
        {
        }
    }
}
