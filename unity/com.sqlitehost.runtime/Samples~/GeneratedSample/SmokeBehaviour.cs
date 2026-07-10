// Handwritten sample (NOT synced from csharp/ — unity/sync.mjs only owns
// the *.g.cs files in this folder). Unity-2021-safe C# 8 / netstandard2.0.
//
// Proves API usability inside Unity without a native SQLite plugin:
// builds the generated host definition, constructs SqliteHostRuntime with
// trivial fake handlers and a fake in-memory connection factory, then runs
// a script whose engine string mismatches on purpose. The pinned runtime
// lifecycle (docs/csharp-api.md, plan section 18) returns
// SkippedUnsupported from the precheck WITHOUT opening a workspace, so the
// clean-skip run needs no SQL at all.

using System;
using System.Collections.Generic;
using Example.Game.Generated;
using UnityEngine;

namespace SqliteHost.Sample
{
    /// <summary>
    /// Attach to any GameObject (Assets/Smoke/SmokeRunner.cs in the sample
    /// project does this automatically at runtime) and check the Console
    /// for a "[SqliteHost] SMOKE OK" line in Play mode.
    /// </summary>
    public sealed class SmokeBehaviour : MonoBehaviour
    {
        private void Start()
        {
            var factory = new RecordingFakeConnectionFactory();
            SqliteHostDefinition<IGeneratedHostHandlers> definition = GeneratedHostDefinition.Build();
            var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
                connectionFactory: factory,
                hostDefinition: definition,
                handlers: new FakeHandlers(),
                options: null);

            // Engine string mismatches "sqlite-host-v1" on purpose: the
            // precheck must return SkippedUnsupported before any SQL runs.
            var script = new SqliteHostScript
            {
                Engine = "sqlite-host-v999-smoke",
                ScriptId = "unity-2021-smoke",
                RequiredApiLevel = 1,
                RequiredFeatures = new List<string>(),
                RequiredMethods = new List<string> { "getValue" },
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

            SqliteHostRunResult result = runtime.Run(script);
            int schemaStatementCount = definition.GenerateSchemaStatements().Count;

            bool ok = result.Status == SqliteHostRunStatus.SkippedUnsupported
                && result.ErrorCode == "unsupported-engine"
                && !factory.WorkspaceOpened
                && schemaStatementCount > 0;

            string details = "status=" + result.Status
                + " errorCode=" + result.ErrorCode
                + " workspaceOpened=" + factory.WorkspaceOpened
                + " apiLevel=" + definition.ApiLevel
                + " methods=" + definition.Methods.Count
                + " schemaStatements=" + schemaStatementCount;

            if (ok)
            {
                Debug.Log("[SqliteHost] SMOKE OK — clean-skip run behaved as pinned: " + details);
            }
            else
            {
                Debug.LogError("[SqliteHost] SMOKE FAILED — " + details
                    + " errorMessage=" + result.ErrorMessage);
            }
        }
    }

    /// <summary>Trivial handler fake — never invoked by the clean-skip run.</summary>
    internal sealed class FakeHandlers : IGeneratedHostHandlers
    {
        public GetValueResult GetValue(GetValueInput input)
        {
            return new GetValueResult { Value = 0L };
        }

        public SetValueResult SetValue(SetValueInput input)
        {
            return new SetValueResult { Success = true };
        }

        public GetValuesResult GetValues(GetValuesInput input)
        {
            return new GetValuesResult();
        }

        public PutBlobResult PutBlob(PutBlobInput input)
        {
            return new PutBlobResult { Stored = true };
        }
    }

    /// <summary>
    /// In-memory fake factory. Records whether a workspace was ever opened
    /// so the smoke can assert the clean-skip run touched no SQL.
    /// </summary>
    internal sealed class RecordingFakeConnectionFactory : ISqliteHostConnectionFactory
    {
        public bool WorkspaceOpened { get; private set; }

        public ISqliteHostConnection OpenWorkspace()
        {
            WorkspaceOpened = true;
            return new FakeConnection();
        }
    }

    /// <summary>
    /// Minimal fake connection — sufficient for a clean-skip run, which by
    /// contract executes no SQL. Any SQL reaching it is a smoke failure.
    /// </summary>
    internal sealed class FakeConnection : ISqliteHostConnection
    {
        public void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings)
        {
            throw new NotSupportedException(
                "Smoke fake cannot execute SQL; a clean-skip run must never get here. Attempted: " + sql);
        }

        public IReadOnlyList<T> Query<T>(
            string sql,
            IReadOnlyList<SqliteHostBinding> bindings,
            Func<ISqliteHostRow, T> mapper)
        {
            throw new NotSupportedException(
                "Smoke fake cannot query SQL; a clean-skip run must never get here. Attempted: " + sql);
        }

        public void Dispose()
        {
        }
    }
}
