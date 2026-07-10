# Pinned C# public API

This document pins the public surface of `SqliteHost.Abstractions` and
`SqliteHost.Runtime` that generated code and consumer code compile
against. The plan's code snippets (README examples, generated sample)
must compile **as written** against this surface. Internals are free;
this surface is not — the C# emitter emits code against it, so changes
here require regenerating everything.

Language/runtime floor: `netstandard2.0`, C# 8, Unity-2021-safe — no
records, no `required` members, no `init` setters, no default interface
members, no `System.Text.Json`, no source generators. Plain classes,
interfaces, delegates, `List<T>`/arrays, explicit null checks.

## SqliteHost.Abstractions (namespace `SqliteHost`)

### Binding values

```csharp
public enum SqliteHostBindingType { Null, Int32, Int64, Bool, Text, Blob }

public sealed class SqliteHostBindingValue
{
    public SqliteHostBindingType Type { get; }
    public int Int32Value { get; }
    public long Int64Value { get; }
    public bool BoolValue { get; }
    public string TextValue { get; }
    public byte[] BlobValue { get; }

    public static SqliteHostBindingValue Null();
    public static SqliteHostBindingValue Int32(int value);
    public static SqliteHostBindingValue Int64(long value);
    public static SqliteHostBindingValue Bool(bool value);
    public static SqliteHostBindingValue Text(string value);
    public static SqliteHostBindingValue Blob(byte[] value);
}

public sealed class SqliteHostBinding
{
    public SqliteHostBinding(string name, SqliteHostBindingValue value);
    public string Name { get; }          // bare name, no :/@/$ prefix
    public SqliteHostBindingValue Value { get; }
}
```

### SQLite adapter interfaces

```csharp
public interface ISqliteHostRow
{
    bool IsNull(int index);
    int GetInt32(int index);
    long GetInt64(int index);
    bool GetBool(int index);
    string GetText(int index);
    byte[] GetBlob(int index);
}

public interface ISqliteHostConnection : IDisposable
{
    void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings);
    IReadOnlyList<T> Query<T>(
        string sql,
        IReadOnlyList<SqliteHostBinding> bindings,
        Func<ISqliteHostRow, T> mapper);
}

public interface ISqliteHostConnectionFactory
{
    ISqliteHostConnection OpenWorkspace();
}

public interface ISqliteHostPrepareConnection : ISqliteHostConnection
{
    ISqliteHostPreparedStatement Prepare(string sql);
}

public interface ISqliteHostPreparedStatement : IDisposable
{
    IReadOnlyList<string> ParameterNames { get; }
}
```

### Script envelope DTOs (protocol v1, generated-then-vendored)

These are the C# projection of the TypeSpec script envelope. The C#
emitter regenerates them and a golden test asserts the vendored copy in
Abstractions is identical.

```csharp
public class SqliteHostScript
{
    public string Engine { get; set; }
    public string ScriptId { get; set; }
    public int RequiredApiLevel { get; set; }
    public List<string> RequiredFeatures { get; set; }
    public List<string> RequiredMethods { get; set; }
    public List<SqliteHostRuntimeInput> Inputs { get; set; }
    public List<SqliteHostStep> Steps { get; set; }
}

public class SqliteHostRuntimeInput
{
    public string Name { get; set; }
    public SqliteHostBindingValue Value { get; set; }
}

public class SqliteHostStep
{
    public string Id { get; set; }
    public List<SqliteHostStatement> Statements { get; set; }
}

public class SqliteHostStatement
{
    public string Sql { get; set; }
    public Dictionary<string, SqliteHostBindingValue> Bindings { get; set; }
}
```

### Run result

```csharp
public enum SqliteHostRunStatus
{
    Completed,
    SkippedUnsupported,
    FailedSql,
    FailedBinding,
    FailedHandler,
    FailedSchema,
    FailedValidation
}

public sealed class SqliteHostRunResult
{
    public SqliteHostRunStatus Status { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }
    public string StepId { get; set; }
    public int StatementIndex { get; set; }     // -1 when not applicable
    public string Method { get; set; }
    public int ExecutedCallCount { get; set; }
    public List<SqliteHostCallDiagnostic> Calls { get; set; }  // populated when EnableDiagnostics
}

public sealed class SqliteHostCallDiagnostic
{
    public string CallId { get; set; }
    public string Method { get; set; }
    public string StepId { get; set; }
}
```

Error codes are listed in `docs/errors.md`.

### Naming

```csharp
public sealed class SqliteHostNaming
{
    public static SqliteHostNaming Default { get; }
    public string CallTablePrefix { get; }
    public string ResultTablePrefix { get; }
    public string InputColumnPrefix { get; }
    public string ResultColumnPrefix { get; }
    public string InputListTableInfix { get; }
    public string ResultListTableInfix { get; }
}

public sealed class SqliteHostNamingBuilder   // each setter returns this
{
    public SqliteHostNamingBuilder CallTablePrefix(string value);
    public SqliteHostNamingBuilder ResultTablePrefix(string value);
    public SqliteHostNamingBuilder InputColumnPrefix(string value);
    public SqliteHostNamingBuilder ResultColumnPrefix(string value);
    public SqliteHostNamingBuilder InputListTableInfix(string value);
    public SqliteHostNamingBuilder ResultListTableInfix(string value);
}
```

### Method spec

`IHostMethodSpec<THandlers>` is the contract between generated
descriptors and the runtime. Members beyond these may exist but are
implementation details of the Runtime package:

```csharp
public interface IHostMethodSpec<THandlers>
{
    string MethodName { get; }
    int ApiLevel { get; }
}
```

## SqliteHost.Runtime (namespace `SqliteHost`)

### Host definition

```csharp
public static class SqliteHostDefinition
{
    public static ISqliteHostDefinitionBuilder<THandlers> ForHandlers<THandlers>();
}

public interface ISqliteHostDefinitionBuilder<THandlers>
{
    ISqliteHostDefinitionBuilder<THandlers> ApiLevel(int apiLevel);
    ISqliteHostDefinitionBuilder<THandlers> Naming(Action<SqliteHostNamingBuilder> configure);
    SqliteHostDefinition<THandlers> Methods(IReadOnlyList<IHostMethodSpec<THandlers>> methods);
}

public sealed class SqliteHostDefinition<THandlers>
{
    public int ApiLevel { get; }
    public SqliteHostNaming Naming { get; }
    public IReadOnlyList<IHostMethodSpec<THandlers>> Methods { get; }
    public IReadOnlyList<string> SupportedFeatures { get; }
    public IReadOnlyList<string> GenerateSchemaStatements();
    public string GenerateSchemaScript();   // byte-identical to the DDL snapshot fixture
}
```

`SupportedFeatures` for protocol v1 is
`["typedNamedBindings", "splitResultTables", "scriptInputs"]`.

### Fluent method descriptor API

```csharp
public static class HostMethod
{
    public static IHostMethodSpecBuilder<THandlers, TInput, TResult>
        For<THandlers, TInput, TResult>(string methodName)
        where TInput : new()
        where TResult : class;
}

public interface IHostMethodSpecBuilder<THandlers, TInput, TResult>
{
    IHostMethodSpecBuilder<THandlers, TInput, TResult> ApiLevel(int apiLevel);
    IHostMethodSpecBuilder<THandlers, TInput, TResult> Inputs(
        Action<IInputFieldsBuilder<TInput>> configure);
    IHostMethodSpecBuilder<THandlers, TInput, TResult> Results(
        Action<IResultFieldsBuilder<TResult>> configure);
    IHostMethodSpecBuilder<THandlers, TInput, TResult> Handler(
        Func<THandlers, TInput, TResult> handler);
    IHostMethodSpec<THandlers> Build();
}
```

Field builders — `sqlName` arguments are the logical snake_case names
(never physical column names; the runtime derives columns via naming):

```csharp
public interface IInputFieldsBuilder<TInput>
{
    IInputFieldsBuilder<TInput> Int(string sqlName, Action<TInput, int> setter);
    IInputFieldsBuilder<TInput> Long(string sqlName, Action<TInput, long> setter);
    IInputFieldsBuilder<TInput> Bool(string sqlName, Action<TInput, bool> setter);
    IInputFieldsBuilder<TInput> Text(string sqlName, Action<TInput, string> setter);
    IInputFieldsBuilder<TInput> Blob(string sqlName, Action<TInput, byte[]> setter);
    IInputFieldsBuilder<TInput> OptionalInt(string sqlName, Action<TInput, int?> setter);
    IInputFieldsBuilder<TInput> OptionalLong(string sqlName, Action<TInput, long?> setter);
    IInputFieldsBuilder<TInput> OptionalBool(string sqlName, Action<TInput, bool?> setter);
    IInputFieldsBuilder<TInput> OptionalText(string sqlName, Action<TInput, string> setter);
    IInputFieldsBuilder<TInput> OptionalBlob(string sqlName, Action<TInput, byte[]> setter);
    IInputFieldsBuilder<TInput> List<TItem>(
        string sqlName,
        Action<TInput, List<TItem>> setter,
        Action<IListItemFieldsBuilder<TItem>> configureItem) where TItem : new();
}

public interface IListItemFieldsBuilder<TItem>
{
    IListItemFieldsBuilder<TItem> Int(string sqlName, Action<TItem, int> setter);
    IListItemFieldsBuilder<TItem> Long(string sqlName, Action<TItem, long> setter);
    IListItemFieldsBuilder<TItem> Bool(string sqlName, Action<TItem, bool> setter);
    IListItemFieldsBuilder<TItem> Text(string sqlName, Action<TItem, string> setter);
    IListItemFieldsBuilder<TItem> Blob(string sqlName, Action<TItem, byte[]> setter);
    IListItemFieldsBuilder<TItem> OptionalInt(string sqlName, Action<TItem, int?> setter);
    IListItemFieldsBuilder<TItem> OptionalLong(string sqlName, Action<TItem, long?> setter);
    IListItemFieldsBuilder<TItem> OptionalBool(string sqlName, Action<TItem, bool?> setter);
    IListItemFieldsBuilder<TItem> OptionalText(string sqlName, Action<TItem, string> setter);
    IListItemFieldsBuilder<TItem> OptionalBlob(string sqlName, Action<TItem, byte[]> setter);
}

public interface IResultFieldsBuilder<TResult>
{
    IResultFieldsBuilder<TResult> Int(string sqlName, Func<TResult, int> getter);
    IResultFieldsBuilder<TResult> Long(string sqlName, Func<TResult, long> getter);
    IResultFieldsBuilder<TResult> Bool(string sqlName, Func<TResult, bool> getter);
    IResultFieldsBuilder<TResult> Text(string sqlName, Func<TResult, string> getter);
    IResultFieldsBuilder<TResult> Blob(string sqlName, Func<TResult, byte[]> getter);
    IResultFieldsBuilder<TResult> OptionalInt(string sqlName, Func<TResult, int?> getter);
    IResultFieldsBuilder<TResult> OptionalLong(string sqlName, Func<TResult, long?> getter);
    IResultFieldsBuilder<TResult> OptionalBool(string sqlName, Func<TResult, bool?> getter);
    IResultFieldsBuilder<TResult> OptionalText(string sqlName, Func<TResult, string> getter);
    IResultFieldsBuilder<TResult> OptionalBlob(string sqlName, Func<TResult, byte[]> getter);
    IResultFieldsBuilder<TResult> List<TItem>(
        string sqlName,
        Func<TResult, List<TItem>> getter,
        Action<IListItemResultFieldsBuilder<TItem>> configureItem);
}

public interface IListItemResultFieldsBuilder<TItem>
{
    IListItemResultFieldsBuilder<TItem> Int(string sqlName, Func<TItem, int> getter);
    IListItemResultFieldsBuilder<TItem> Long(string sqlName, Func<TItem, long> getter);
    IListItemResultFieldsBuilder<TItem> Bool(string sqlName, Func<TItem, bool> getter);
    IListItemResultFieldsBuilder<TItem> Text(string sqlName, Func<TItem, string> getter);
    IListItemResultFieldsBuilder<TItem> Blob(string sqlName, Func<TItem, byte[]> getter);
    IListItemResultFieldsBuilder<TItem> OptionalInt(string sqlName, Func<TItem, int?> getter);
    IListItemResultFieldsBuilder<TItem> OptionalLong(string sqlName, Func<TItem, long?> getter);
    IListItemResultFieldsBuilder<TItem> OptionalBool(string sqlName, Func<TItem, bool?> getter);
    IListItemResultFieldsBuilder<TItem> OptionalText(string sqlName, Func<TItem, string> getter);
    IListItemResultFieldsBuilder<TItem> OptionalBlob(string sqlName, Func<TItem, byte[]> getter);
}
```

### Runtime

```csharp
public sealed class SqliteHostRuntimeOptions
{
    public bool ValidateBindings { get; set; }        // default true
    public bool EnableDiagnostics { get; set; }       // default false
    public int MaxStatementsPerRun { get; set; }      // default 256
    public int MaxPendingCallsPerStep { get; set; }   // default 64
}

public sealed class SqliteHostRuntime<THandlers>
{
    public SqliteHostRuntime(
        ISqliteHostConnectionFactory connectionFactory,
        SqliteHostDefinition<THandlers> hostDefinition,
        THandlers handlers,
        SqliteHostRuntimeOptions options);

    public SqliteHostRunResult Run(SqliteHostScript script);
}
```

Constructor parameter **names** are pinned (callers use named
arguments): `connectionFactory`, `hostDefinition`, `handlers`,
`options`. `options` may be null → defaults.

### Runtime lifecycle (pinned semantics, plan §18)

`Run(script)` must:

1. Validate engine (`sqlite-host-v1`), `requiredApiLevel`,
   `requiredFeatures`, `requiredMethods` → mismatch returns
   `SkippedUnsupported` without opening a workspace.
2. Open a workspace via `connectionFactory.OpenWorkspace()`.
3. Create the generated schema (every statement from
   `GenerateSchemaStatements()`).
4. Insert runtime inputs into `script_inputs` if provided.
5. For each step in order:
   a. Execute each statement with typed bindings (validating bindings
      lexically when `ValidateBindings` — see `docs/errors.md`).
   b. Only after **all** statements in the step succeed, drain
      `pending_host_calls` in `queue_id` order: resolve method spec,
      read parent call row + input list child rows (ordered by
      `item_index`), map to the input DTO, invoke the handler, write the
      result parent row (status `'done'`) + result list child rows, mark
      the queue row `status = 'done'`.
   c. Never drain between statements inside a step.
6. Stop immediately on SQL, binding, schema, or handler failure.
7. Return `SqliteHostRunResult`; dispose the workspace connection.

## Generated code shape (target of the C# emitter)

Namespace: `Example.Game.Generated` for the sample. Files:

| File | Contents |
|---|---|
| `HostMethodDtos.g.cs` | input/result/item DTO classes — plain classes, public auto-properties, `List<T>` properties initialized to `new List<T>()` |
| `IGeneratedHostHandlers.g.cs` | handler interface, one method per op: `GetValueResult GetValue(GetValueInput input);` |
| `GeneratedHostMethodSpecs.g.cs` | `public static class GeneratedHostMethodSpecs` with `BuildAll()` + one private `Build<Op>Spec()` per method using the fluent API |
| `GeneratedHostDefinition.g.cs` | `public static class GeneratedHostDefinition { public static SqliteHostDefinition<IGeneratedHostHandlers> Build() }` per plan §11 |
| `GeneratedSchemaSql.g.cs` | `public static class GeneratedSchemaSql { public const string SchemaScript = "..."; }` — optional DDL constant, byte-identical to the snapshot |

Every generated file starts with the header line `// <auto-generated />`.
