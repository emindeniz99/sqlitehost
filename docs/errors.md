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
| `FailedScript` | the script aborted itself via `script_control` action `fail` |

## Error codes

`ErrorCode` values (string, stable across releases):

| Code | Status | Trigger |
|---|---|---|
| `unsupported-engine` | SkippedUnsupported | `engine != "sqlite-host-v1"` |
| `unsupported-api-level` | SkippedUnsupported | `requiredApiLevel` > host apiLevel |
| `missing-feature` | SkippedUnsupported | a `requiredFeatures` entry not supported |
| `missing-method` | SkippedUnsupported | a `requiredMethods` entry not registered |
| `invalid-script` | FailedValidation | null/empty steps, empty step id, null statement sql, step with an empty/missing statements list |
| `duplicate-input-name` | FailedValidation | two runtime `inputs` entries share a name |
| `duplicate-step-id` | FailedValidation | two steps share an id |
| `max-statements-exceeded` | FailedValidation | total statements > `MaxStatementsPerRun` |
| `sqlite-version-too-low` | FailedSchema | the actual `sqlite_version()` of the opened workspace is below the host definition's `MinSqliteVersionNumber` — checked on the first workspace open (and on demand via `ValidateEnvironment()`), so ancient system-provided SQLite builds (e.g. very old iOS clients) fail loudly instead of misbehaving; version strings are parsed tolerating the historical 4-component form (`3.8.11.1` → 3008011) |
| `schema-error` | FailedSchema | DDL execution failed |
| `input-insert-error` | FailedSchema | `script_inputs` insert failed |
| `sql-error` | FailedSql | statement execution failed (includes SQLite errors such as the UNIQUE violation from a duplicate `call_id`) |
| `missing-binding` | FailedBinding | SQL references a parameter with no binding (when `ValidateBindings`) |
| `unused-binding` | FailedBinding | binding not referenced by the SQL (when `ValidateBindings`) |
| `max-pending-calls-exceeded` | FailedSql | queue drain found more than `MaxPendingCallsPerStep` pending calls after a step |
| `unknown-queued-method` | FailedSql | queue row references a method with no registered spec (schema/spec mismatch) |
| `call-row-missing` | FailedSql | queue row exists but the parent call row is missing |
| `script-abort` | FailedScript | the script wrote action `fail` into the control table; `ErrorMessage` carries the script's message; the current step's pending calls are not drained |
| `invalid-control-action` | FailedValidation | the control table's first row carries an action other than `halt`/`fail` |
| `handler-error` | FailedHandler | handler threw — via the queue drain OR inside an inline function (the adapter reports the `SQLITEHOST_HANDLER_ERROR:` marker through the SQL error and the runtime maps it back); `Method` and `ErrorMessage` carry details |
| `inline-registration-error` | FailedSchema | registering the host's inline scalar functions on a capable connection failed |
| `result-write-error` | FailedSql | writing result rows failed |
| `list-child-after-drain` | FailedSql | input list child rows appeared for a call that was already drained in an earlier step (the validator blocks this statically; the runtime detects it defensively by re-counting child rows of drained calls after each step) |

Successful halts: `Status = Completed` with `Halted = true`,
`HaltMessage` carrying the script's optional message, and `StepId` set
to the halting step — a halt is not an error.

Failure context fields: `StepId` and `StatementIndex` are set for
statement-scoped failures (`StatementIndex` is `-1` otherwise);
`Method` is set for call-scoped failures; `BindingName` is set for
`missing-binding`/`unused-binding`; `SqliteErrorCode` carries the
native SQLite error code when the adapter surfaced one via
`SqliteHostAdapterException` (`0` = not available);
`ExecutedCallCount` always counts successfully completed handler
invocations through the queue drain; `InlineCallCount` counts handler
invocations made through inline scalar functions (informational — the
SQLite planner may evaluate a function 0..N times per row).

## Logging policy

The runtime emits **no logs** — no `Console.WriteLine`, no
`Debug.Log`, no telemetry hooks. All failure information travels in
the structured `SqliteHostRunResult`; the consumer maps it to its own
logging/telemetry. A source-level guard test enforces this.

## Binding validation

When `ValidateBindings` is on, the runtime lexically scans each
statement's SQL for named parameters (`:name`, `@name`, `$name`) —
skipping string literals ('…' with '' escapes), double-quoted
identifiers, line comments (`--`) and block comments (`/* */`) — and
compares the set against the statement's binding names. The same
scanner algorithm is used by the Java validator and the TypeScript
authoring lint (see `docs/validation.md`).

One SQLite-lexer subtlety is pinned explicitly: `$` is also an
identifier character in SQLite, so a `$` immediately preceded by an
identifier character continues that identifier instead of starting a
parameter — `a$b` and `foo$bar` contain no parameters, while `$v` at a
token boundary does. `:` and `@` are not identifier characters and
always start a parameter outside quoted regions.
