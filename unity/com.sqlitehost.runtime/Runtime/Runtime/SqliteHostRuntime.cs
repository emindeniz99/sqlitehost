using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Executes parsed scripts against a temporary SQLite workspace and
    /// bridges call_* inserts to typed handler invocations (pinned
    /// lifecycle, plan §18 / docs/csharp-api.md). Never throws for
    /// script-level problems; returns a structured result instead.
    /// </summary>
    public sealed class SqliteHostRuntime<THandlers>
    {
        private const string EngineV1 = "sqlite-host-v1";

        private readonly ISqliteHostConnectionFactory _connectionFactory;
        private readonly SqliteHostDefinition<THandlers> _hostDefinition;
        private readonly THandlers _handlers;
        private readonly SqliteHostRuntimeOptions _options;

        public SqliteHostRuntime(
            ISqliteHostConnectionFactory connectionFactory,
            SqliteHostDefinition<THandlers> hostDefinition,
            THandlers handlers,
            SqliteHostRuntimeOptions options)
        {
            if (connectionFactory == null)
            {
                throw new ArgumentNullException(nameof(connectionFactory));
            }
            if (hostDefinition == null)
            {
                throw new ArgumentNullException(nameof(hostDefinition));
            }
            if (handlers == null)
            {
                throw new ArgumentNullException(nameof(handlers));
            }
            _connectionFactory = connectionFactory;
            _hostDefinition = hostDefinition;
            _handlers = handlers;
            _options = options ?? new SqliteHostRuntimeOptions();
        }

        public SqliteHostRunResult Run(SqliteHostScript script)
        {
            var state = new RunState(_options.EnableDiagnostics);

            SqliteHostRunResult precheckFailure = Precheck(script, state);
            if (precheckFailure != null)
            {
                return precheckFailure;
            }

            ISqliteHostConnection connection = _connectionFactory.OpenWorkspace();
            try
            {
                return Execute(connection, script, state);
            }
            finally
            {
                connection.Dispose();
            }
        }

        private SqliteHostRunResult Execute(
            ISqliteHostConnection connection,
            SqliteHostScript script,
            RunState state)
        {
            try
            {
                foreach (string statement in _hostDefinition.GenerateSchemaStatements())
                {
                    connection.Execute(statement, RuntimeSql.NoBindings);
                }
            }
            catch (Exception ex)
            {
                return WithSqliteErrorCode(
                    Failure(state, SqliteHostRunStatus.FailedSchema, "schema-error", ex.Message, null, null), ex);
            }

            if (script.Inputs != null)
            {
                try
                {
                    foreach (SqliteHostRuntimeInput input in script.Inputs)
                    {
                        InsertRuntimeInput(connection, input);
                    }
                }
                catch (Exception ex)
                {
                    return WithSqliteErrorCode(
                        Failure(state, SqliteHostRunStatus.FailedSchema, "input-insert-error", ex.Message, null, null), ex);
                }
            }

            foreach (SqliteHostStep step in script.Steps)
            {
                for (int statementIndex = 0; statementIndex < step.Statements.Count; statementIndex++)
                {
                    SqliteHostStatement statement = step.Statements[statementIndex];

                    if (_options.ValidateBindings)
                    {
                        SqliteHostRunResult bindingFailure =
                            ValidateStatementBindings(state, step.Id, statementIndex, statement);
                        if (bindingFailure != null)
                        {
                            return bindingFailure;
                        }
                    }

                    try
                    {
                        connection.Execute(statement.Sql, ToBindingList(statement.Bindings));
                    }
                    catch (Exception ex)
                    {
                        return WithSqliteErrorCode(StatementFailure(
                            state, SqliteHostRunStatus.FailedSql, "sql-error", ex.Message, step.Id, statementIndex), ex);
                    }
                }

                SqliteHostRunResult drainFailure = DrainPendingCalls(connection, step.Id, state);
                if (drainFailure != null)
                {
                    return drainFailure;
                }
            }

            var completed = new SqliteHostRunResult
            {
                Status = SqliteHostRunStatus.Completed,
                ErrorCode = null,
                ErrorMessage = null,
                StepId = null,
                StatementIndex = -1,
                Method = null,
                ExecutedCallCount = state.ExecutedCallCount,
                Calls = state.Calls
            };
            return completed;
        }

        private SqliteHostRunResult Precheck(SqliteHostScript script, RunState state)
        {
            if (script == null)
            {
                return Failure(state, SqliteHostRunStatus.FailedValidation, "invalid-script",
                    "Script is null.", null, null);
            }
            if (script.Engine != EngineV1)
            {
                return Failure(state, SqliteHostRunStatus.SkippedUnsupported, "unsupported-engine",
                    "Engine '" + (script.Engine ?? "<null>") + "' is not '" + EngineV1 + "'.", null, null);
            }
            if (script.RequiredApiLevel > _hostDefinition.ApiLevel)
            {
                return Failure(state, SqliteHostRunStatus.SkippedUnsupported, "unsupported-api-level",
                    "Script requires API level " + script.RequiredApiLevel
                    + " but the host supports " + _hostDefinition.ApiLevel + ".", null, null);
            }
            if (script.RequiredFeatures != null)
            {
                foreach (string feature in script.RequiredFeatures)
                {
                    if (!ContainsOrdinal(_hostDefinition.SupportedFeatures, feature))
                    {
                        return Failure(state, SqliteHostRunStatus.SkippedUnsupported, "missing-feature",
                            "Required feature '" + feature + "' is not supported.", null, null);
                    }
                }
            }
            if (script.RequiredMethods != null)
            {
                foreach (string method in script.RequiredMethods)
                {
                    if (_hostDefinition.ResolveSpec(method) == null)
                    {
                        return Failure(state, SqliteHostRunStatus.SkippedUnsupported, "missing-method",
                            "Required method '" + method + "' is not registered.", null, method);
                    }
                }
            }

            if (script.Inputs != null)
            {
                var inputNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (SqliteHostRuntimeInput input in script.Inputs)
                {
                    if (input != null && input.Name != null && !inputNames.Add(input.Name))
                    {
                        return Failure(state, SqliteHostRunStatus.FailedValidation, "duplicate-input-name",
                            "Runtime input name '" + input.Name + "' occurs more than once.", null, null);
                    }
                }
            }

            if (script.Steps == null || script.Steps.Count == 0)
            {
                return Failure(state, SqliteHostRunStatus.FailedValidation, "invalid-script",
                    "Script has no steps.", null, null);
            }
            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            int statementCount = 0;
            foreach (SqliteHostStep step in script.Steps)
            {
                if (step == null || string.IsNullOrEmpty(step.Id))
                {
                    return Failure(state, SqliteHostRunStatus.FailedValidation, "invalid-script",
                        "A step is null or has an empty id.", null, null);
                }
                if (step.Statements == null || step.Statements.Count == 0)
                {
                    return Failure(state, SqliteHostRunStatus.FailedValidation, "invalid-script",
                        "Step '" + step.Id + "' has an empty or missing statements list.", step.Id, null);
                }
                if (!stepIds.Add(step.Id))
                {
                    return Failure(state, SqliteHostRunStatus.FailedValidation, "duplicate-step-id",
                        "Step id '" + step.Id + "' occurs more than once.", step.Id, null);
                }
                foreach (SqliteHostStatement statement in step.Statements)
                {
                    if (statement == null || statement.Sql == null)
                    {
                        return Failure(state, SqliteHostRunStatus.FailedValidation, "invalid-script",
                            "A statement in step '" + step.Id + "' is null or has null sql.", step.Id, null);
                    }
                    statementCount++;
                }
            }
            if (statementCount > _options.MaxStatementsPerRun)
            {
                return Failure(state, SqliteHostRunStatus.FailedValidation, "max-statements-exceeded",
                    "Script has " + statementCount + " statements; MaxStatementsPerRun is "
                    + _options.MaxStatementsPerRun + ".", null, null);
            }
            return null;
        }

        private SqliteHostRunResult ValidateStatementBindings(
            RunState state,
            string stepId,
            int statementIndex,
            SqliteHostStatement statement)
        {
            List<string> parameters = SqlParameterScanner.ScanParameterNames(statement.Sql);
            Dictionary<string, SqliteHostBindingValue> bindings = statement.Bindings;
            foreach (string parameter in parameters)
            {
                if (bindings == null || !bindings.ContainsKey(parameter))
                {
                    SqliteHostRunResult missing = StatementFailure(
                        state, SqliteHostRunStatus.FailedBinding, "missing-binding",
                        "SQL references parameter '" + parameter + "' but no binding provides it.",
                        stepId, statementIndex);
                    missing.BindingName = parameter;
                    return missing;
                }
            }
            if (bindings != null)
            {
                foreach (KeyValuePair<string, SqliteHostBindingValue> binding in bindings)
                {
                    if (!parameters.Contains(binding.Key))
                    {
                        SqliteHostRunResult unused = StatementFailure(
                            state, SqliteHostRunStatus.FailedBinding, "unused-binding",
                            "Binding '" + binding.Key + "' is not referenced by the SQL.",
                            stepId, statementIndex);
                        unused.BindingName = binding.Key;
                        return unused;
                    }
                }
            }
            return null;
        }

        private SqliteHostRunResult DrainPendingCalls(
            ISqliteHostConnection connection,
            string stepId,
            RunState state)
        {
            SqliteHostRunResult staleListFailure = CheckDrainedListCalls(connection, stepId, state);
            if (staleListFailure != null)
            {
                return staleListFailure;
            }

            IReadOnlyList<PendingCall> pending;
            try
            {
                pending = connection.Query(
                    "SELECT queue_id, call_id, method FROM pending_host_calls WHERE status = 'pending' ORDER BY queue_id",
                    RuntimeSql.NoBindings,
                    delegate(ISqliteHostRow row)
                    {
                        return new PendingCall(row.GetInt64(0), row.GetText(1), row.GetText(2));
                    });
            }
            catch (Exception ex)
            {
                return WithSqliteErrorCode(
                    Failure(state, SqliteHostRunStatus.FailedSql, "sql-error", ex.Message, stepId, null), ex);
            }

            if (pending.Count > _options.MaxPendingCallsPerStep)
            {
                return Failure(state, SqliteHostRunStatus.FailedSql, "max-pending-calls-exceeded",
                    "Step '" + stepId + "' produced " + pending.Count
                    + " pending calls; MaxPendingCallsPerStep is " + _options.MaxPendingCallsPerStep + ".",
                    stepId, null);
            }

            foreach (PendingCall call in pending)
            {
                IRuntimeHostMethodSpec<THandlers> spec = _hostDefinition.ResolveSpec(call.Method);
                if (spec == null)
                {
                    return Failure(state, SqliteHostRunStatus.FailedSql, "unknown-queued-method",
                        "Queue row references method '" + call.Method + "' but no spec is registered.",
                        stepId, call.Method);
                }

                try
                {
                    spec.ExecuteCall(connection, _hostDefinition.Naming, _handlers, call.CallId);
                }
                catch (SqliteHostCallRowMissingException ex)
                {
                    return Failure(state, SqliteHostRunStatus.FailedSql, "call-row-missing",
                        ex.Message, stepId, call.Method);
                }
                catch (SqliteHostHandlerException ex)
                {
                    return Failure(state, SqliteHostRunStatus.FailedHandler, "handler-error",
                        ex.Message, stepId, call.Method);
                }
                catch (SqliteHostResultWriteException ex)
                {
                    return WithSqliteErrorCode(Failure(state, SqliteHostRunStatus.FailedSql, "result-write-error",
                        ex.Message, stepId, call.Method), ex);
                }
                catch (Exception ex)
                {
                    return WithSqliteErrorCode(Failure(state, SqliteHostRunStatus.FailedSql, "sql-error",
                        ex.Message, stepId, call.Method), ex);
                }

                try
                {
                    connection.Execute(
                        "UPDATE pending_host_calls SET status = 'done' WHERE queue_id = :queueId",
                        new List<SqliteHostBinding>
                        {
                            new SqliteHostBinding("queueId", SqliteHostBindingValue.Int64(call.QueueId))
                        });
                    RecordDrainedListCall(connection, spec, call, state);
                }
                catch (Exception ex)
                {
                    return WithSqliteErrorCode(Failure(state, SqliteHostRunStatus.FailedSql, "sql-error",
                        ex.Message, stepId, call.Method), ex);
                }

                state.ExecutedCallCount++;
                if (state.Calls != null)
                {
                    state.Calls.Add(new SqliteHostCallDiagnostic
                    {
                        CallId = call.CallId,
                        Method = call.Method,
                        StepId = stepId
                    });
                }
            }
            return null;
        }

        /// <summary>
        /// Records the per-child-table row counts of a just-drained call
        /// that has input list fields, so later steps that append child rows
        /// for it (list-child-after-drain) are detected defensively.
        /// </summary>
        private void RecordDrainedListCall(
            ISqliteHostConnection connection,
            IRuntimeHostMethodSpec<THandlers> spec,
            PendingCall call,
            RunState state)
        {
            IReadOnlyList<SchemaListFieldModel> listFields = spec.SchemaModel.InputListFields;
            if (listFields.Count == 0)
            {
                return;
            }
            var childTables = new List<string>(listFields.Count);
            var rowCounts = new List<long>(listFields.Count);
            foreach (SchemaListFieldModel listField in listFields)
            {
                string childTable = NamingDerivation.InputListTable(
                    _hostDefinition.Naming, call.Method, listField.SqlName);
                childTables.Add(childTable);
                rowCounts.Add(CountChildRows(connection, childTable, call.CallId));
            }
            state.DrainedListCalls.Add(new DrainedListCall(call.CallId, call.Method, childTables, rowCounts));
        }

        /// <summary>
        /// Re-counts input list child rows of previously drained calls; any
        /// change means the step that just ran appended rows to a list the
        /// handler already consumed (error code list-child-after-drain).
        /// </summary>
        private SqliteHostRunResult CheckDrainedListCalls(
            ISqliteHostConnection connection,
            string stepId,
            RunState state)
        {
            foreach (DrainedListCall drained in state.DrainedListCalls)
            {
                for (int i = 0; i < drained.ChildTables.Count; i++)
                {
                    long count;
                    try
                    {
                        count = CountChildRows(connection, drained.ChildTables[i], drained.CallId);
                    }
                    catch (Exception ex)
                    {
                        return WithSqliteErrorCode(Failure(state, SqliteHostRunStatus.FailedSql, "sql-error",
                            ex.Message, stepId, drained.Method), ex);
                    }
                    if (count != drained.RowCounts[i])
                    {
                        return Failure(state, SqliteHostRunStatus.FailedSql, "list-child-after-drain",
                            "Step '" + stepId + "' changed " + drained.ChildTables[i]
                            + " rows for call '" + drained.CallId + "' after it was drained.",
                            stepId, drained.Method);
                    }
                }
            }
            return null;
        }

        private static long CountChildRows(ISqliteHostConnection connection, string childTable, string callId)
        {
            IReadOnlyList<long> counts = connection.Query(
                "SELECT COUNT(*) FROM " + childTable + " WHERE call_id = :callId",
                RuntimeSql.CallIdBindings(callId),
                delegate(ISqliteHostRow row)
                {
                    return row.GetInt64(0);
                });
            return counts[0];
        }

        private static void InsertRuntimeInput(ISqliteHostConnection connection, SqliteHostRuntimeInput input)
        {
            SqliteHostBindingValue value = input.Value ?? SqliteHostBindingValue.Null();
            string valueType;
            SqliteHostBindingValue intValue = SqliteHostBindingValue.Null();
            SqliteHostBindingValue realValue = SqliteHostBindingValue.Null();
            SqliteHostBindingValue textValue = SqliteHostBindingValue.Null();
            SqliteHostBindingValue blobValue = SqliteHostBindingValue.Null();
            switch (value.Type)
            {
                case SqliteHostBindingType.Int32:
                    valueType = "int32";
                    intValue = value;
                    break;
                case SqliteHostBindingType.Int64:
                    valueType = "int64";
                    intValue = value;
                    break;
                case SqliteHostBindingType.Bool:
                    valueType = "bool";
                    intValue = value;
                    break;
                case SqliteHostBindingType.Text:
                    valueType = "text";
                    textValue = value;
                    break;
                case SqliteHostBindingType.Blob:
                    valueType = "blob";
                    blobValue = value;
                    break;
                case SqliteHostBindingType.Float32:
                    valueType = "float32";
                    realValue = value;
                    break;
                case SqliteHostBindingType.Float64:
                    valueType = "float64";
                    realValue = value;
                    break;
                default:
                    valueType = "null";
                    break;
            }
            connection.Execute(
                "INSERT INTO script_inputs (name, value_type, int_value, real_value, text_value, blob_value)"
                + " VALUES (:name, :valueType, :intValue, :realValue, :textValue, :blobValue)",
                new List<SqliteHostBinding>
                {
                    new SqliteHostBinding("name", SqliteHostBindingValue.Text(input.Name)),
                    new SqliteHostBinding("valueType", SqliteHostBindingValue.Text(valueType)),
                    new SqliteHostBinding("intValue", intValue),
                    new SqliteHostBinding("realValue", realValue),
                    new SqliteHostBinding("textValue", textValue),
                    new SqliteHostBinding("blobValue", blobValue)
                });
        }

        private static IReadOnlyList<SqliteHostBinding> ToBindingList(
            Dictionary<string, SqliteHostBindingValue> bindings)
        {
            if (bindings == null || bindings.Count == 0)
            {
                return RuntimeSql.NoBindings;
            }
            var list = new List<SqliteHostBinding>(bindings.Count);
            foreach (KeyValuePair<string, SqliteHostBindingValue> binding in bindings)
            {
                list.Add(new SqliteHostBinding(binding.Key, binding.Value));
            }
            return list;
        }

        private static bool ContainsOrdinal(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Copies the native SQLite error code onto the result when the
        /// caught exception (or one of its inner exceptions) is a
        /// <see cref="SqliteHostAdapterException"/>.
        /// </summary>
        private static SqliteHostRunResult WithSqliteErrorCode(SqliteHostRunResult result, Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                var adapterException = current as SqliteHostAdapterException;
                if (adapterException != null)
                {
                    result.SqliteErrorCode = adapterException.SqliteErrorCode;
                    break;
                }
            }
            return result;
        }

        private static SqliteHostRunResult StatementFailure(
            RunState state,
            SqliteHostRunStatus status,
            string errorCode,
            string errorMessage,
            string stepId,
            int statementIndex)
        {
            var result = Failure(state, status, errorCode, errorMessage, stepId, null);
            result.StatementIndex = statementIndex;
            return result;
        }

        private static SqliteHostRunResult Failure(
            RunState state,
            SqliteHostRunStatus status,
            string errorCode,
            string errorMessage,
            string stepId,
            string method)
        {
            return new SqliteHostRunResult
            {
                Status = status,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                StepId = stepId,
                StatementIndex = -1,
                Method = method,
                ExecutedCallCount = state.ExecutedCallCount,
                Calls = state.Calls
            };
        }

        private sealed class RunState
        {
            public RunState(bool enableDiagnostics)
            {
                ExecutedCallCount = 0;
                Calls = enableDiagnostics ? new List<SqliteHostCallDiagnostic>() : null;
                DrainedListCalls = new List<DrainedListCall>();
            }

            public int ExecutedCallCount { get; set; }
            public List<SqliteHostCallDiagnostic> Calls { get; }

            /// <summary>Drained calls with input list fields, for list-child-after-drain detection.</summary>
            public List<DrainedListCall> DrainedListCalls { get; }
        }

        /// <summary>Child-table row counts of one drained call, snapshotted at drain time.</summary>
        private sealed class DrainedListCall
        {
            public DrainedListCall(string callId, string method, List<string> childTables, List<long> rowCounts)
            {
                CallId = callId;
                Method = method;
                ChildTables = childTables;
                RowCounts = rowCounts;
            }

            public string CallId { get; }
            public string Method { get; }
            public List<string> ChildTables { get; }
            public List<long> RowCounts { get; }
        }

        private sealed class PendingCall
        {
            public PendingCall(long queueId, string callId, string method)
            {
                QueueId = queueId;
                CallId = callId;
                Method = method;
            }

            public long QueueId { get; }
            public string CallId { get; }
            public string Method { get; }
        }
    }
}
