# Adapter contract

An adapter implements `ISqliteHostConnection` (and optionally
`ISqliteHostPrepareConnection`) over a concrete SQLite wrapper. The
runtime is only as trustworthy as its adapter, so the contract below is
normative and enforced by a reusable conformance suite. **Silent
failure is a conformance violation.**

## Error surfacing (the core rule)

Adapters must never swallow SQL, prepare, step, schema, or binding
failures:

- `Execute` and `Query` must surface prepare/step/schema failures as
  exceptions (preferably `SqliteHostAdapterException`, carrying the
  native SQLite error code when available). The runtime maps them to
  `sql-error` / `FailedSql` and copies the code into
  `SqliteHostRunResult.SqliteErrorCode`.
- Malformed SQL, missing tables, and missing columns must never look
  like success with zero rows.
- Native bind errors must not be ignored.
- A statement error mid-step must abort the step: later statements do
  not execute and pending host calls are **not** drained for that step
  (runtime guarantee, but the adapter must not mask the trigger).

## Binding resolution policy

- Payload binding keys are **prefixless** (`"id"`).
- In SQL, a parameter may be written `:id`, `@id`, or `$id`; a binding
  matches a parameter when the names are equal after stripping the
  prefix character (see `docs/script-envelope.md`).
- One binding may legitimately feed the same name through multiple
  prefix forms in one statement (`:v` and `$v` both receive `"v"`).
  This is supported runtime behavior; validators emit the
  `mixed-prefix-binding` **warning** because it is usually an authoring
  accident. Authoring guidance: use `:name` consistently.
- The same named parameter may appear multiple times in one statement
  and must bind at every occurrence.
- Every supplied binding key must resolve to at least one parameter;
  the runtime rejects leftovers before execution (`unused-binding`),
  and parameters without bindings fail before execution
  (`missing-binding`). An adapter must never silently bind NULL for a
  parameter the payload did not provide.

## Value fidelity

Round-trip fidelity is part of the contract: int32, int64 (values
above 2^31), bool as 0/1, text (empty and non-ASCII), blob (empty and
large), explicit null, float32/float64 (REAL) — see the conformance
suite for the exact matrix.

## Conformance suite

`csharp/SqliteHost.Tests/Adapter/AdapterConformanceTestsBase.cs` is an
abstract xunit class encoding this contract; the repo runs it against
all three built-in adapters (Microsoft.Data.Sqlite, System.Data.SQLite,
sqlite-net-pcl). **If you write your own adapter — including private
forks of Unity SQLite wrappers — subclass it with your factory and run
it.** A wrapper that swallows errors will fail `malformed_sql_throws`
/ `missing_table_throws` immediately; that is the point.
