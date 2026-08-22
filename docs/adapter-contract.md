# Adapter contract

An adapter implements `ISqliteHostConnection` (and optionally
`ISqliteHostPrepareConnection`) over a concrete SQLite wrapper. The
runtime is only as trustworthy as its adapter, so the contract below is
normative and enforced by a reusable conformance suite. **Silent
failure is a conformance violation.**

## The Query contract (and a pre-release breaking change)

An adapter implements exactly two data operations:

```csharp
void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings);
IReadOnlyList<object> QueryRows(
    string sql,
    IReadOnlyList<SqliteHostBinding> bindings,
    Func<ISqliteHostRow, object> mapper);
```

`QueryRows` is non-generic **by contract**: a generic interface method
(generic virtual method) forces AOT compilers (NativeAOT, IL2CPP) to
carry their dynamic type loader — measured at ~250 KB. Call the mapper
once per row, in row order, and return the mapped values. Typed reads
stay ergonomic for everyone else through the `Query<T>` extension
method on `ISqliteHostConnection` (SqliteHost.Abstractions).

**Migration (pre-release breaking change):** earlier revisions declared
`IReadOnlyList<T> Query<T>(...)` on the interface. To update an
adapter: rename the method to `QueryRows`, replace `T` with `object`,
and change `new List<T>()` to `new List<object>()` — the body is
otherwise unchanged. Consumers *calling* `Query<T>(...)` need no
changes (the extension method has the old name and shape). The
conformance suite verifies the new contract.

## Error surfacing (the core rule)

Adapters must never swallow SQL, prepare, step, schema, or binding
failures:

- `Execute` and `QueryRows` must surface prepare/step/schema failures
  as exceptions (preferably `SqliteHostAdapterException`, carrying the
  native SQLite error code when available). The runtime maps them to
  `sql-error` / `FailedSql` and copies the code into
  `SqliteHostRunResult.SqliteErrorCode`.
- Malformed SQL, missing tables, and missing columns must never look
  like success with zero rows.
- `Execute` must step a row-producing statement to completion (until
  `SQLITE_DONE`), discarding rows. SQLite evaluates a SELECT only as
  it is stepped, so stopping at the first row would silently skip
  later-row evaluation — errors and inline function invocations alike.
- Native bind errors must not be ignored.
- A statement error mid-step must abort the step: later statements do
  not execute and pending host calls are **not** drained for that step
  (runtime guarantee, but the adapter must not mask the trigger).
- One statement per call. Scripts are single-statement by validation
  (the `multiple-statements` error, `docs/validation.md`), so an adapter
  that receives a trailing statement is looking at a payload no
  validator passed. Compiling the first statement and dropping the rest
  without a word is the one response the contract forbids. The shipped
  native adapter throws before stepping anything, since
  `sqlite3_prepare_v2` hands the tail back rather than running it; an
  ADO.NET-style wrapper that executes the whole batch is also
  conformant.

## Workspace lifecycle

`ISqliteHostConnectionFactory.OpenWorkspace()` is called once per
`Run(script)`, and the runtime creates its whole generated schema on the
connection it gets back (plain `CREATE TABLE`, no `IF NOT EXISTS`). A
factory therefore has to hand back an **empty** database. The workspace
holds one run's scratch state and nothing a player would miss.

The shipped `NativeSqliteHostConnectionFactory` honours that two ways:
the default constructor opens `:memory:`, and the constructor taking a
path deletes any file already at that path, plus its `-wal` and `-shm`
siblings, before opening it. Passing the path of a database you care
about (a save file, an asset database) destroys it on the next `Run`,
and deleting the `-wal`/`-shm` of a database another process holds open
corrupts that database. Point it at a temporary path, or use the
in-memory default.

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

## Optional capability: inline scalar functions

An adapter whose wrapper can reach `sqlite3_create_function` may
implement `ISqliteHostScalarFunctionConnection` and mark its factory
with `ISqliteHostScalarFunctionCapableFactory`. Contract:

- register each `SqliteHostScalarFunction` for **every arity** in
  `MinArgs..MaxArgs` before any script SQL runs;
- catch **everything** thrown by `Invoke` and report it via the SQL
  error channel prefixed `SQLITEHOST_HANDLER_ERROR:` — an exception
  must never cross the native frames (IL2CPP safety); the runtime maps
  the marker back to `FailedHandler`/`handler-error`;
- do not register with SQLITE_DETERMINISTIC (v1 rule — see
  `docs/proposals/inline-host-functions.md`);
- incapable adapters implement nothing: hosts running on them
  clean-skip scripts that require `inlineFunctions`.

The conformance suite gains an optional capability section that runs
only against capable adapters.

## Value fidelity

Round-trip fidelity is part of the contract: int32, int64 (values
above 2^31), bool as 0/1, text (empty and non-ASCII), blob (empty and
large), explicit null, float32/float64 (REAL) — see the conformance
suite for the exact matrix.

## Conformance suite

`SqliteHost.Conformance` (source: `csharp/SqliteHost.Conformance/`) is
a shippable netstandard2.0 library containing
`AdapterConformanceTestsBase` — the xunit contract suite (24 core
tests + an optional scalar-function capability section on capable
adapters),
fully self-contained (it builds its own minimal probe host through the
public fluent API; no dependency on the sample or any concrete
adapter). The repo runs it against all four built-in adapters
(Microsoft.Data.Sqlite, System.Data.SQLite, sqlite-net-pcl, and
**SqliteHost.Adapters.Native** — a shippable pure-DllImport/P-Invoke
adapter that consumes libsqlite3 directly, implements the
scalar-function capability natively, and serves as the reference for
DllImport-style wrapper authors) and against real SQLite engines
3.9.0 → newest in the version matrix.

Native-adapter IL2CPP caveat: its scalar-function support uses reverse
P/Invoke callbacks, which under Unity IL2CPP require
`[MonoPInvokeCallback]` on two static methods — a netstandard2.0
package cannot reference the Unity attribute, so Unity consumers
vendoring the source add it themselves (documented on the class
headers). The Execute/Query path is callback-free and IL2CPP-clean
as-is.

**If you write your own adapter — including private forks of Unity
SQLite wrappers — add the package to your test project and subclass
it.** A wrapper that swallows errors fails `MalformedSql_Throws` /
`MissingTable_Throws` immediately; a wrapper that silently binds NULL
for unbound parameters fails `NoSilentNullSemantics` — that is the
point (this exact bug was found and fixed in the bundled sqlite-net
adapter by this suite).

```xml
<ItemGroup>
  <PackageReference Include="SqliteHost.Conformance" Version="0.1.0" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
  <PackageReference Include="xunit" Version="2.9.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" PrivateAssets="all" />
  <!-- plus whatever SQLite wrapper your adapter is built on -->
</ItemGroup>
```

```csharp
using SqliteHost;
using SqliteHost.Conformance;

public class MyAdapterConformanceTests : AdapterConformanceTestsBase
{
    protected override ISqliteHostConnection OpenAdapterConnection()
        => MySqliteHostConnection.OpenInMemory();   // your adapter factory
}
```

xunit discovers the inherited tests automatically; an optional
`protected override string SkipEntireSuiteReason` skips the suite with
a reason where the adapter cannot run. Until the package is published,
vendoring works too: copy `csharp/SqliteHost.Conformance/` sources into
your test project.
