# Runtime error model

`SqliteHostRuntime<THandlers>.Run()` never throws for script-level
problems; it returns a structured `SqliteHostRunResult`. The
host application decides logging/telemetry policy.

## Statuses

| Status | Meaning |
|---|---|
| `Completed` | all steps executed, all calls drained |
| `SkippedUnsupported` | compatibility precheck failed — clean skip, workspace never opened |
| `FailedSql` | a statement failed to execute, or the queue/drain state it left behind was invalid |
| `FailedBinding` | binding validation failed for a statement |
| `FailedHandler` | a handler threw |
| `FailedSchema` | workspace schema creation failed |
| `FailedValidation` | the parsed script object is structurally invalid, or the control table carried an action the runtime does not recognize |
| `FailedScript` | the script aborted itself via `script_control` action `fail` |

## Error codes

`ErrorCode` values (string, stable across releases):

| Code | Status | Trigger |
|---|---|---|
| `unsupported-engine` | SkippedUnsupported | `engine != "sqlite-host-v1"` |
| `unsupported-api-level` | SkippedUnsupported | `requiredApiLevel` > host apiLevel |
| `missing-feature` | SkippedUnsupported | a `requiredFeatures` entry not supported |
| `missing-method` | SkippedUnsupported | a `requiredMethods` entry not registered |
| `invalid-script` | FailedValidation | null/empty steps, blank step id, null or blank statement sql, step with an empty/missing statements list, null input entry, blank input name, `requiredApiLevel` < 1. **Blank**, not merely empty: `"   "` is rejected wherever `""` is, on the pinned character set `docs/script-envelope.md` enumerates (the one the SQL scanners share) |
| `duplicate-input-name` | FailedValidation | two runtime `inputs` entries share a name |
| `duplicate-step-id` | FailedValidation | two steps share an id |
| `max-statements-exceeded` | FailedValidation | total statements > `MaxStatementsPerRun` |
| `sqlite-version-too-low` | FailedSchema | the actual `sqlite_version()` of the opened workspace is below the host definition's `MinSqliteVersionNumber` — checked on the first workspace open (and on demand via `ValidateEnvironment()`), so ancient system-provided SQLite builds (e.g. very old iOS clients) fail loudly instead of misbehaving; version strings are parsed tolerating the historical 4-component form (`3.8.11.1` → 3008011) |
| `schema-error` | FailedSchema | DDL execution failed |
| `input-insert-error` | FailedSchema | `script_inputs` insert failed |
| `sql-error` | FailedSql | statement execution failed (includes SQLite errors such as the UNIQUE violation from a duplicate `call_id`) |
| `missing-binding` | FailedBinding | SQL references a parameter with no binding (when `ValidateBindings`) |
| `unused-binding` | FailedBinding | binding not referenced by the SQL (when `ValidateBindings`) |
| `max-pending-calls-exceeded` | FailedSql | a step's drain reached more than `MaxPendingCallsPerStep` pending calls in total, counting calls enqueued *during* the drain — which is also what bounds the re-drain loop |
| `call-already-drained` | FailedSql | a queue row this run already drained is `pending` again. The queue row's `status` is ordinary data, so it is not by itself an at-most-once guarantee; the run keeps its own set of drained `queue_id`s, which a script cannot write. `Method` is set |
| `undrained-calls` | FailedSql | the run reached its end (completed or halted) with a row still `pending` in the queue. The per-step drain re-reads the pending set until it comes back empty, so this is the backstop for a drain that cannot see what the queue holds — `Completed` never means "all calls drained except one" |
| `unknown-queued-method` | FailedSql | queue row references a method with no registered spec (schema/spec mismatch) |
| `call-row-missing` | FailedSql | queue row exists but the parent call row is missing |
| `script-abort` | FailedScript | the script wrote action `fail` into the control table; `ErrorMessage` carries the script's message; the current step's pending calls are not drained. A row written by a *handler* is `handler-wrote-control` instead — the drain snapshots the table around every handler invocation, so the attribution is measured rather than assumed |
| `handler-wrote-control` | FailedHandler | the control table grew (or changed shape) while a handler was running, so the row is the host's and not the script's — a handler holding the workspace connection, or a nested runtime. Reported instead of `script-abort`, which would blame whichever statement the next control check happened to follow; `Method` names the handler |
| `invalid-control-action` | FailedValidation | the control table's first row carries an action other than `halt`/`fail` |
| `input-type-mismatch` | FailedSql | a call column's stored value contradicts the declared scalar type — SQLite affinity converts only when the conversion is lossless, so an INTEGER-declared column can hold TEXT and a typed getter would coerce it silently; the drain checks the storage class (`sqlite3_column_type`) before every typed read and names the column and both types in `ErrorMessage`. `Method` is set. Not a strict check: it stays in `SQLITEHOST_SLIM` builds, because the alternative is a handler invoked with a corrupted argument and a run reporting `Completed` |
| `handler-error` | FailedHandler | handler threw — via the queue drain OR inside an inline function (the runtime's own wrapper records which registered function threw during the statement, and the adapter's `SQLITEHOST_HANDLER_ERROR:` marker on the SQL error confirms it; the marker alone — SQLite echoes unresolved identifiers and collation names into error text — is a plain `sql-error`); `Method` and `ErrorMessage` carry details |
| `inline-registration-error` | FailedSchema | registering the host's inline scalar functions on a capable connection failed |
| `result-write-error` | FailedSql | writing result rows failed |
| `list-child-after-drain` | FailedSql | input list child rows appeared for a call that was already drained in an earlier step (the validator's `list-child-later-step` rule blocks this statically only when `call_id` is a literal or a bound text value; a computed `call_id` skips that rule — see `docs/validation.md` — so the runtime is the only backstop for those, re-counting child rows of drained calls after each step; under `SQLITEHOST_SLIM` this runtime check is stripped and the condition is not detected at all) |

Successful halts: `Status = Completed` with `Halted = true`,
`HaltMessage` carrying the script's optional message, and `StepId` set
to the halting step — a halt is not an error.

Failure context fields: `StepId` and `StatementIndex` are set for
statement-scoped failures (`StatementIndex` is `-1` otherwise);
`Method` is set for call-scoped failures; `BindingName` is set for
`missing-binding`/`unused-binding`; `SqliteErrorCode` carries the
extended result code (`sqlite3_extended_errcode`) when the adapter
surfaced one via `SqliteHostAdapterException`, reducing to the primary
code on adapters whose wrapper exposes only that (`0` = not available).
The same duplicate-key violation therefore reports `1555`
(`SQLITE_CONSTRAINT_PRIMARYKEY`) through the native adapter and `19`
(`SQLITE_CONSTRAINT`) through Microsoft.Data.Sqlite. Branch on the low
byte — `code & 0xFF` is the primary code and is the same everywhere;
the conformance suite pins exactly that
(`ConstraintViolation_SurfacesAConstraintResultCode`);
`ExecutedCallCount` always counts successfully completed handler
invocations through the queue drain — counted when the handler returns
and its result row is written, so a failure in the queue bookkeeping
that follows (there is no transaction around the pair) still reports
the invocation that did happen; `InlineCallCount` counts handler
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
skipping string literals ('…' with '' escapes), quoted identifiers
(double-quoted `"…"` with `""` escapes, bracket `[…]` ending at the
first `]` with no escape, and backtick `` `…` `` with doubled-backtick
escapes), line comments (`--`) and block comments (`/* */`) — and
compares the set against the statement's binding names. The same
scanner algorithm is used by the Java validator and the TypeScript
authoring lint (see `docs/validation.md`).

One SQLite-lexer subtlety is pinned explicitly: `$` is also an
identifier character in SQLite, so a `$` immediately preceded by an
identifier character continues that identifier instead of starting a
parameter — `a$b` and `foo$bar` contain no parameters, while `$v` at a
token boundary does. `:` and `@` are not identifier characters and
always start a parameter outside quoted regions.

A parameter's *name* runs over SQLite's full IdChar set (letters,
digits, `_`, `$`, and any character above 0x7f), so `:anahtarİsmi` is one
parameter named `anahtarİsmi` rather than `anahtar` followed by stray
text. The adapter conformance suite requires adapters to bind such names
unmangled, so the scanners accept what the engine accepts.

Two suffix forms belong to that name for the same reason. SQLite's
variable syntax also admits a doubled colon inside the name (`:a::b`) and
one trailing parenthesised group (`$a(1)`); both come from its TCL
variable support, both are compiled in by default, and both apply to
every prefix, not just `$`. Each is **one** parameter whose name carries
the suffix, verified against the sqlite3 CLI 3.51.0: `SELECT :a::b` binds
`:a::b` and reports a missing value for exactly that name. The group must
close on the same token — `$a( 1)` and `$a(1` are illegal tokens, as are
`::a` and the adjacent pair `:a:b`, and the scanners leave those to fail
where they already fail.
