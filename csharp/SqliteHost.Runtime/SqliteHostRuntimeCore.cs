using System;
using System.Collections.Generic;

namespace SqliteHost
{
    /// <summary>
    /// Non-generic execution core behind
    /// <see cref="SqliteHostRuntime{THandlers}"/>. Carries the whole run
    /// lifecycle on erased (object) handlers and erased method specs, so
    /// the generic runtime type stays a thin API-typing wrapper and the
    /// engine code is never re-instantiated per handlers type. Never throws
    /// for script-level problems; returns a structured result instead.
    /// </summary>
    internal sealed class SqliteHostRuntimeCore
    {
        private const string EngineV1 = "sqlite-host-v1";

        private readonly ISqliteHostConnectionFactory _connectionFactory;
        private readonly SqliteHostDefinitionCore _hostDefinition;
        private readonly object _handlers;
        private readonly SqliteHostRuntimeOptions _options;

        /// <summary>
        /// The definition's features plus "inlineFunctions" when the
        /// definition exposes at least one inline method AND the factory is
        /// statically function-capable — so the compat precheck stays
        /// workspace-free (docs/proposals/inline-host-functions.md).
        /// </summary>
        private readonly IReadOnlyList<string> _supportedFeatures;

        /// <summary>
        /// True once a workspace of this runtime instance passed the
        /// sqlite_version() gate; the native library cannot change under a
        /// live instance, so the check is not repeated on later runs.
        /// </summary>
        private bool _sqliteVersionChecked;

        public SqliteHostRuntimeCore(
            ISqliteHostConnectionFactory connectionFactory,
            SqliteHostDefinitionCore hostDefinition,
            object handlers,
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
            _supportedFeatures = ComputeSupportedFeatures(connectionFactory, hostDefinition);
        }

        private static IReadOnlyList<string> ComputeSupportedFeatures(
            ISqliteHostConnectionFactory connectionFactory,
            SqliteHostDefinitionCore hostDefinition)
        {
            if (!hostDefinition.HasInlineFunctions
                || !(connectionFactory is ISqliteHostScalarFunctionCapableFactory))
            {
                return hostDefinition.SupportedFeatures;
            }
            var features = new List<string>(hostDefinition.SupportedFeatures);
            features.Add(ProtocolConstants.FeatureInlineFunctions);
            return features;
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

        /// <summary>
        /// Opens a workspace, checks the actual sqlite_version() against the
        /// definition's MinSqliteVersionNumber, disposes, and returns the
        /// outcome — lets hosts fail fast at init time instead of at the
        /// first Run (docs/csharp-api.md). No schema is created.
        /// </summary>
        public SqliteHostRunResult ValidateEnvironment()
        {
            var state = new RunState(_options.EnableDiagnostics);
            ISqliteHostConnection connection = _connectionFactory.OpenWorkspace();
            try
            {
                SqliteHostRunResult versionFailure = CheckSqliteVersion(connection, state);
                if (versionFailure != null)
                {
                    return versionFailure;
                }
                _sqliteVersionChecked = true;
                return new SqliteHostRunResult
                {
                    Status = SqliteHostRunStatus.Completed,
                    ErrorCode = null,
                    ErrorMessage = null,
                    StepId = null,
                    StatementIndex = -1,
                    Method = null,
                    ExecutedCallCount = state.ExecutedCallCount,
                    InlineCallCount = state.InlineCallCount,
                    Calls = state.Calls
                };
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
            if (!_sqliteVersionChecked)
            {
                SqliteHostRunResult versionFailure = CheckSqliteVersion(connection, state);
                if (versionFailure != null)
                {
                    return versionFailure;
                }
                _sqliteVersionChecked = true;
            }

            SqliteHostRunResult registrationFailure = RegisterInlineFunctions(connection, state);
            if (registrationFailure != null)
            {
                return registrationFailure;
            }

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
                        InsertRuntimeInput(
                            connection, _hostDefinition.Naming.InputsTable, _hostDefinition.Columns, input);
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
                bool halted = false;
                string haltMessage = null;
                for (int statementIndex = 0; statementIndex < step.Statements.Count; statementIndex++)
                {
                    SqliteHostStatement statement = step.Statements[statementIndex];

#if !SQLITEHOST_SLIM
                    if (_options.ValidateBindings)
                    {
                        SqliteHostRunResult bindingFailure =
                            ValidateStatementBindings(state, step.Id, statementIndex, statement);
                        if (bindingFailure != null)
                        {
                            return bindingFailure;
                        }
                    }
#endif

                    try
                    {
                        connection.Execute(statement.Sql, ToBindingList(statement.Bindings));
                    }
                    catch (Exception ex)
                    {
                        return MapStatementException(state, step.Id, statementIndex, ex);
                    }

                    // Statement-granular control check (docs/workspace-schema.md):
                    // a fail written by the last statement of a step must be
                    // seen BEFORE that step's drain, so the check runs after
                    // every successful statement.
                    ControlRow control;
                    try
                    {
                        control = ReadControlRow(connection);
                    }
                    catch (Exception ex)
                    {
                        return WithSqliteErrorCode(StatementFailure(
                            state, SqliteHostRunStatus.FailedSql, "sql-error", ex.Message, step.Id, statementIndex), ex);
                    }
                    if (control == null)
                    {
                        continue;
                    }
                    if (control.Action == ProtocolConstants.ControlActionHalt)
                    {
                        // Graceful stop: skip the remaining statements, drain
                        // the calls this step already emitted, skip the rest.
                        halted = true;
                        haltMessage = control.Message;
                        break;
                    }
                    if (control.Action == ProtocolConstants.ControlActionFail)
                    {
                        // Script-initiated abort: no drain for this step.
                        return StatementFailure(
                            state, SqliteHostRunStatus.FailedScript, "script-abort",
                            control.Message, step.Id, statementIndex);
                    }
                    return StatementFailure(
                        state, SqliteHostRunStatus.FailedValidation, "invalid-control-action",
                        "Control table action '" + control.Action + "' is not '"
                            + ProtocolConstants.ControlActionHalt + "' or '"
                            + ProtocolConstants.ControlActionFail + "'.",
                        step.Id, statementIndex);
                }

                SqliteHostRunResult drainFailure = DrainPendingCalls(connection, step.Id, state);
                if (drainFailure != null)
                {
                    return drainFailure;
                }

                if (halted)
                {
                    return new SqliteHostRunResult
                    {
                        Status = SqliteHostRunStatus.Completed,
                        ErrorCode = null,
                        ErrorMessage = null,
                        StepId = step.Id,
                        StatementIndex = -1,
                        Method = null,
                        Halted = true,
                        HaltMessage = haltMessage,
                        ExecutedCallCount = state.ExecutedCallCount,
                        InlineCallCount = state.InlineCallCount,
                        Calls = state.Calls
                    };
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
                InlineCallCount = state.InlineCallCount,
                Calls = state.Calls
            };
            return completed;
        }

        /// <summary>
        /// Reads the control table's winning row (first by rowid); null when
        /// the table is empty. The runtime only ever reads this table.
        /// </summary>
        private ControlRow ReadControlRow(ISqliteHostConnection connection)
        {
            SqliteHostColumns columns = _hostDefinition.Columns;
            IReadOnlyList<object> rows = connection.QueryRows(
                "SELECT " + columns.Action + ", " + columns.Message
                + " FROM " + _hostDefinition.Naming.ControlTable
                + " ORDER BY rowid LIMIT 1",
                RuntimeSql.NoBindings,
                delegate(ISqliteHostRow row)
                {
                    return new ControlRow(
                        row.GetText(0),
                        row.IsNull(1) ? null : row.GetText(1));
                });
            return rows.Count > 0 ? (ControlRow)rows[0] : null;
        }

        /// <summary>
        /// Registers every inline scalar function on the workspace, before
        /// any schema DDL runs, when the definition exposes inline methods
        /// and the connection can register functions. Returns null on
        /// success (or nothing to do); a FailedSchema/
        /// inline-registration-error result otherwise.
        /// </summary>
        private SqliteHostRunResult RegisterInlineFunctions(ISqliteHostConnection connection, RunState state)
        {
            if (!_hostDefinition.HasInlineFunctions)
            {
                return null;
            }
            var functionConnection = connection as ISqliteHostScalarFunctionConnection;
            if (functionConnection == null)
            {
                return null;
            }
            try
            {
                foreach (ErasedHostMethodSpec spec in _hostDefinition.Specs)
                {
                    if (spec.InlineFunction == null)
                    {
                        continue;
                    }
                    functionConnection.RegisterScalarFunction(
                        spec.CreateInlineFunction(_handlers, state.CountInlineHandlerInvocation));
                }
            }
            catch (Exception ex)
            {
                return WithSqliteErrorCode(Failure(
                    state, SqliteHostRunStatus.FailedSchema, "inline-registration-error",
                    ex.Message, null, null), ex);
            }
            return null;
        }

        /// <summary>
        /// Maps a failed script statement to its result: when the adapter's
        /// error text carries the SQLITEHOST_HANDLER_ERROR: marker the
        /// failure happened inside an inline scalar function and becomes
        /// FailedHandler/handler-error (Method resolved from the function
        /// name when derivable); otherwise plain FailedSql/sql-error.
        /// </summary>
        private SqliteHostRunResult MapStatementException(
            RunState state,
            string stepId,
            int statementIndex,
            Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                string message = current.Message;
                int markerIndex = message == null
                    ? -1
                    : message.IndexOf(SqliteHostScalarFunction.HandlerErrorMarker, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    continue;
                }
                SqliteHostRunResult failure = StatementFailure(
                    state, SqliteHostRunStatus.FailedHandler, "handler-error",
                    exception.Message, stepId, statementIndex);
                failure.Method = ResolveInlineMethod(
                    message, markerIndex + SqliteHostScalarFunction.HandlerErrorMarker.Length);
                return WithSqliteErrorCode(failure, exception);
            }
            return WithSqliteErrorCode(StatementFailure(
                state, SqliteHostRunStatus.FailedSql, "sql-error",
                exception.Message, stepId, statementIndex), exception);
        }

        /// <summary>
        /// Resolves the method whose inline function reported the error:
        /// the runtime's invocation wrapper prefixes every message with
        /// "&lt;functionName&gt;: ", so the text right after the marker names
        /// the function. Null when not derivable.
        /// </summary>
        private string ResolveInlineMethod(string message, int afterMarkerIndex)
        {
            string remainder = message.Substring(afterMarkerIndex).TrimStart();
            foreach (ErasedHostMethodSpec spec in _hostDefinition.Specs)
            {
                if (spec.InlineFunction != null
                    && remainder.StartsWith(spec.InlineFunction.FunctionName + ":", StringComparison.Ordinal))
                {
                    return spec.MethodName;
                }
            }
            return null;
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
            if (script.RequiredApiLevel < 1)
            {
                return Failure(state, SqliteHostRunStatus.FailedValidation, "invalid-script",
                    "Script requiredApiLevel " + script.RequiredApiLevel
                    + " is invalid; the envelope requires an integer >= 1.", null, null);
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
                    if (!ContainsOrdinal(_supportedFeatures, feature))
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
                    if (input == null || string.IsNullOrEmpty(input.Name))
                    {
                        return Failure(state, SqliteHostRunStatus.FailedValidation, "invalid-script",
                            "A runtime input is null or has an empty name.", null, null);
                    }
                    if (!inputNames.Add(input.Name))
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
                    if (statement == null || string.IsNullOrEmpty(statement.Sql))
                    {
                        return Failure(state, SqliteHostRunStatus.FailedValidation, "invalid-script",
                            "A statement in step '" + step.Id + "' is null or has null or empty sql.", step.Id, null);
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

        /// <summary>
        /// Version gate (docs/errors.md sqlite-version-too-low): queries the
        /// workspace's sqlite_version() and compares it against the host
        /// definition's MinSqliteVersionNumber. Returns null when the
        /// workspace passes; a FailedSchema result otherwise.
        /// </summary>
        private SqliteHostRunResult CheckSqliteVersion(ISqliteHostConnection connection, RunState state)
        {
            string versionString;
            try
            {
                IReadOnlyList<object> rows = connection.QueryRows(
                    "SELECT sqlite_version()",
                    RuntimeSql.NoBindings,
                    delegate(ISqliteHostRow row)
                    {
                        return row.GetText(0);
                    });
                versionString = rows.Count > 0 ? (string)rows[0] : null;
            }
            catch (Exception ex)
            {
                return WithSqliteErrorCode(Failure(state, SqliteHostRunStatus.FailedSchema, "sqlite-version-too-low",
                    "Could not determine sqlite_version(): " + ex.Message
                    + " The host requires at least " + _hostDefinition.MinSqliteVersionNumber + ".",
                    null, null), ex);
            }

            int versionNumber;
            if (!SqliteVersionParser.TryParse(versionString, out versionNumber))
            {
                return Failure(state, SqliteHostRunStatus.FailedSchema, "sqlite-version-too-low",
                    "Could not parse sqlite_version() result '" + (versionString ?? "<null>")
                    + "'. The host requires at least " + _hostDefinition.MinSqliteVersionNumber + ".",
                    null, null);
            }
            if (versionNumber < _hostDefinition.MinSqliteVersionNumber)
            {
                return Failure(state, SqliteHostRunStatus.FailedSchema, "sqlite-version-too-low",
                    "SQLite version " + versionString + " (" + versionNumber
                    + ") is below the host definition's minimum " + _hostDefinition.MinSqliteVersionNumber + ".",
                    null, null);
            }
            return null;
        }

#if !SQLITEHOST_SLIM
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
#endif

        private SqliteHostRunResult DrainPendingCalls(
            ISqliteHostConnection connection,
            string stepId,
            RunState state)
        {
#if !SQLITEHOST_SLIM
            SqliteHostRunResult staleListFailure = CheckDrainedListCalls(connection, stepId, state);
            if (staleListFailure != null)
            {
                return staleListFailure;
            }
#endif

            SqliteHostColumns columns = _hostDefinition.Columns;
            IReadOnlyList<object> pending;
            try
            {
                pending = connection.QueryRows(
                    "SELECT " + columns.QueueId + ", " + columns.CallId + ", " + columns.Method
                    + " FROM " + _hostDefinition.Naming.QueueTable
                    + " WHERE " + columns.Status + " = '" + ProtocolConstants.PendingStatus + "' ORDER BY " + columns.QueueId,
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
                ErasedHostMethodSpec spec = _hostDefinition.ResolveSpec(call.Method);
                if (spec == null)
                {
                    return Failure(state, SqliteHostRunStatus.FailedSql, "unknown-queued-method",
                        "Queue row references method '" + call.Method + "' but no spec is registered.",
                        stepId, call.Method);
                }

                try
                {
                    spec.ExecuteCall(connection, _hostDefinition.Naming, columns, _handlers, call.CallId);
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
                        "UPDATE " + _hostDefinition.Naming.QueueTable
                        + " SET " + columns.Status + " = :done WHERE " + columns.QueueId + " = :queueId",
                        new List<SqliteHostBinding>
                        {
                            new SqliteHostBinding("done", SqliteHostBindingValue.Text(columns.DoneValue)),
                            new SqliteHostBinding("queueId", SqliteHostBindingValue.Int64(call.QueueId))
                        });
#if !SQLITEHOST_SLIM
                    RecordDrainedListCall(connection, spec, call, state);
#endif
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
#if !SQLITEHOST_SLIM
        private void RecordDrainedListCall(
            ISqliteHostConnection connection,
            ErasedHostMethodSpec spec,
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
                rowCounts.Add(CountChildRows(connection, childTable, _hostDefinition.Columns, call.CallId));
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
                        count = CountChildRows(
                            connection, drained.ChildTables[i], _hostDefinition.Columns, drained.CallId);
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

        private static long CountChildRows(
            ISqliteHostConnection connection,
            string childTable,
            SqliteHostColumns columns,
            string callId)
        {
            IReadOnlyList<object> counts = connection.QueryRows(
                "SELECT COUNT(*) FROM " + childTable + " WHERE " + columns.CallId + " = :callId",
                RuntimeSql.CallIdBindings(callId),
                delegate(ISqliteHostRow row)
                {
                    return (object)row.GetInt64(0);
                });
            return (long)counts[0];
        }
#endif

        private static void InsertRuntimeInput(
            ISqliteHostConnection connection,
            string inputsTable,
            SqliteHostColumns columns,
            SqliteHostRuntimeInput input)
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
                "INSERT INTO " + inputsTable
                + " (" + columns.Name + ", " + columns.ValueType + ", " + columns.IntValue
                + ", " + columns.RealValue + ", " + columns.TextValue + ", " + columns.BlobValue + ")"
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
                InlineCallCount = state.InlineCallCount,
                Calls = state.Calls
            };
        }

        private sealed class RunState
        {
            public RunState(bool enableDiagnostics)
            {
                ExecutedCallCount = 0;
                InlineCallCount = 0;
                Calls = enableDiagnostics ? new List<SqliteHostCallDiagnostic>() : null;
#if !SQLITEHOST_SLIM
                DrainedListCalls = new List<DrainedListCall>();
#endif
            }

            public int ExecutedCallCount { get; set; }

            /// <summary>Handler invocations made through inline scalar functions.</summary>
            public int InlineCallCount { get; set; }

            /// <summary>Passed into every inline function wrapper (single-threaded runs by contract).</summary>
            public void CountInlineHandlerInvocation()
            {
                InlineCallCount++;
            }

            public List<SqliteHostCallDiagnostic> Calls { get; }

#if !SQLITEHOST_SLIM
            /// <summary>Drained calls with input list fields, for list-child-after-drain detection.</summary>
            public List<DrainedListCall> DrainedListCalls { get; }
#endif
        }

#if !SQLITEHOST_SLIM
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
#endif

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

        /// <summary>The control table's winning row (first by rowid).</summary>
        private sealed class ControlRow
        {
            public ControlRow(string action, string message)
            {
                Action = action;
                Message = message;
            }

            public string Action { get; }
            public string Message { get; }
        }
    }
}
