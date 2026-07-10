# Runtime error model

`SqliteHostRuntime<THandlers>.Run()` never throws for script-level
problems; it returns a structured `SqliteHostRunResult` (plan §25). The
host application decides logging/telemetry policy.

## Statuses

| Status | Meaning |
|---|---|
| `Completed` | all steps executed, all calls drained |
| `SkippedUnsupported` | compatibility precheck failed — clean skip, workspace never opened |
| `FailedSql` | a statement failed to execute |
| `FailedBinding` | binding validation failed for a statement |
| `FailedHandler` | a handler threw |
| `FailedSchema` | workspace schema creation failed |
| `FailedValidation` | the parsed script object is structurally invalid |

## Error codes

`ErrorCode` values (string, stable across releases):

| Code | Status | Trigger |
|---|---|---|
| `unsupported-engine` | SkippedUnsupported | `engine != "sqlite-host-v1"` |
| `unsupported-api-level` | SkippedUnsupported | `requiredApiLevel` > host apiLevel |
| `missing-feature` | SkippedUnsupported | a `requiredFeatures` entry not supported |
| `missing-method` | SkippedUnsupported | a `requiredMethods` entry not registered |
| `invalid-script` | FailedValidation | null/empty steps, empty step id, null statement sql |
| `duplicate-step-id` | FailedValidation | two steps share an id |
| `max-statements-exceeded` | FailedValidation | total statements > `MaxStatementsPerRun` |
| `schema-error` | FailedSchema | DDL execution failed |
| `input-insert-error` | FailedSchema | `script_inputs` insert failed |
| `sql-error` | FailedSql | statement execution failed (includes SQLite errors such as the UNIQUE violation from a duplicate `call_id`) |
| `missing-binding` | FailedBinding | SQL references a parameter with no binding (when `ValidateBindings`) |
| `unused-binding` | FailedBinding | binding not referenced by the SQL (when `ValidateBindings`) |
| `max-pending-calls-exceeded` | FailedSql | queue drain found more than `MaxPendingCallsPerStep` pending calls after a step |
| `unknown-queued-method` | FailedSql | queue row references a method with no registered spec (schema/spec mismatch) |
| `call-row-missing` | FailedSql | queue row exists but the parent call row is missing |
| `handler-error` | FailedHandler | handler threw; `Method` and `ErrorMessage` carry details |
| `result-write-error` | FailedSql | writing result rows failed |

Failure context fields: `StepId` and `StatementIndex` are set for
statement-scoped failures (`StatementIndex` is `-1` otherwise);
`Method` is set for call-scoped failures; `ExecutedCallCount` always
counts successfully completed handler invocations.

## Binding validation

When `ValidateBindings` is on, the runtime lexically scans each
statement's SQL for named parameters (`:name`, `@name`, `$name`) —
skipping string literals ('…' with '' escapes), double-quoted
identifiers, line comments (`--`) and block comments (`/* */`) — and
compares the set against the statement's binding names. The same
scanner algorithm is used by the Java validator and the TypeScript
authoring lint (see `docs/validation.md`).
