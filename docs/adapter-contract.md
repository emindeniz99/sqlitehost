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

`SqliteHost.Conformance` (source: `csharp/SqliteHost.Conformance/`) is
a shippable netstandard2.0 library containing
`AdapterConformanceTestsBase` — 23 xunit tests encoding this contract,
fully self-contained (it builds its own minimal probe host through the
public fluent API; no dependency on the sample or any concrete
adapter). The repo runs it against all three built-in adapters
(Microsoft.Data.Sqlite, System.Data.SQLite, sqlite-net-pcl) and
against real SQLite engines 3.9.0 → newest in the version matrix.

**If you write your own adapter — including private forks of Unity
SQLite wrappers — add the package to your test project and subclass
it.** A wrapper that swallows errors fails `MalformedSql_Throws` /
`MissingTable_Throws` immediately; a wrapper that silently binds NULL
for unbound parameters fails `NoSilentNullSemantics` — that is the
point (this exact bug was found and fixed in the bundled sqlite-net
adapter by this suite).

```xml
<ItemGroup>
  <PackageReference Include="SqliteHost.Conformance" Version="0.1.0-preview" />
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

xunit discovers the 23 inherited tests automatically; an optional
`protected override string SkipEntireSuiteReason` skips the suite with
a reason where the adapter cannot run. Until the package is published,
vendoring works too: copy `csharp/SqliteHost.Conformance/` sources into
your test project.
