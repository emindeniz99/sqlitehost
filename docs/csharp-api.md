# Pinned C# public API

This document pins the public surface of `SqliteHost.Abstractions` and
`SqliteHost.Runtime` that generated code and consumer code compile
against. Every code snippet in the docs (README examples, generated
sample) must compile **as written** against this surface. Internals are free;
this surface is not — the C# emitter emits code against it, so changes
here require regenerating everything.

Language/runtime floor: `netstandard2.0`, C# 8, Unity-2021-safe — no
records, no `required` members, no `init` setters, no default interface
members, no `System.Text.Json`, no source generators. Plain classes,
interfaces, delegates, `List<T>`/arrays, explicit null checks.

## SqliteHost.Abstractions (namespace `SqliteHost`)

### Binding values

```csharp
public enum SqliteHostBindingType { Null, Int32, Int64, Bool, Text, Blob, Float32, Float64 }

public sealed class SqliteHostBindingValue
{
    public SqliteHostBindingType Type { get; }
    public int Int32Value { get; }
    public long Int64Value { get; }
    public bool BoolValue { get; }
    public string TextValue { get; }
    public byte[] BlobValue { get; }
    public float Float32Value { get; }
    public double Float64Value { get; }

    public static SqliteHostBindingValue Null();
    public static SqliteHostBindingValue Int32(int value);
    public static SqliteHostBindingValue Int64(long value);
    public static SqliteHostBindingValue Bool(bool value);
    public static SqliteHostBindingValue Text(string value);
    public static SqliteHostBindingValue Blob(byte[] value);
    public static SqliteHostBindingValue Float32(float value);
    public static SqliteHostBindingValue Float64(double value);
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
    float GetFloat32(int index);
    double GetFloat64(int index);
}

public interface ISqliteHostConnection : IDisposable
{
    void Execute(string sql, IReadOnlyList<SqliteHostBinding> bindings);
    IReadOnlyList<object> QueryRows(
        string sql,
        IReadOnlyList<SqliteHostBinding> bindings,
        Func<ISqliteHostRow, object> mapper);
}

// Typed convenience over QueryRows (adapter consumers and tests keep
// the ergonomic shape; adapters implement only QueryRows).
public static class SqliteHostConnectionExtensions
{
    public static IReadOnlyList<T> Query<T>(
        this ISqliteHostConnection connection,
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

`QueryRows` is deliberately **non-generic**. A generic method on an
interface is a *generic virtual method*; calling one forces AOT
compilers to keep the whole dynamic type loader alive (measured under
NativeAOT: ~250 KB raw across code + metadata + EH — see
`docs/compatibility.md`, App size). The runtime only ever calls
`QueryRows`; the `Query<T>` extension exists for adapter consumers and
is trimmed when unused. History: pre-release versions declared
`Query<T>` on the interface itself — adapters written against that
shape change the method name and erase `T` to `object` (see
`docs/adapter-contract.md`, migration note).

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
    FailedValidation,
    FailedScript
}

public sealed class SqliteHostRunResult
{
    public SqliteHostRunStatus Status { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }
    public string StepId { get; set; }
    public int StatementIndex { get; set; }     // -1 when not applicable
    public string Method { get; set; }
    public string BindingName { get; set; }     // set for missing-/unused-binding
    public int SqliteErrorCode { get; set; }    // native code via SqliteHostAdapterException; 0 = not available
    public bool Halted { get; set; }            // true when the script halted itself (Status stays Completed)
    public string HaltMessage { get; set; }     // the script's optional halt message
    public int ExecutedCallCount { get; set; }
    public int InlineCallCount { get; set; }    // handler invocations via inline functions (informational)
    public List<SqliteHostCallDiagnostic> Calls { get; set; }  // populated when EnableDiagnostics
}

public class SqliteHostAdapterException : Exception   // adapters wrap native failures in this
{
    public SqliteHostAdapterException(string message, int sqliteErrorCode, Exception innerException);
    public int SqliteErrorCode { get; }
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
    public string QueueTable { get; }     // default "pending_host_calls"
    public string InputsTable { get; }    // default "script_inputs"
    public string VarsTable { get; }      // default "script_vars"
    public string FunctionPrefix { get; } // default "fn_" (inline scalar functions)
}

public sealed class SqliteHostNamingBuilder   // each setter returns this
{
    public SqliteHostNamingBuilder CallTablePrefix(string value);
    public SqliteHostNamingBuilder ResultTablePrefix(string value);
    public SqliteHostNamingBuilder InputColumnPrefix(string value);
    public SqliteHostNamingBuilder ResultColumnPrefix(string value);
    public SqliteHostNamingBuilder InputListTableInfix(string value);
    public SqliteHostNamingBuilder ResultListTableInfix(string value);
    public SqliteHostNamingBuilder QueueTable(string value);
    public SqliteHostNamingBuilder InputsTable(string value);
    public SqliteHostNamingBuilder VarsTable(string value);
    public SqliteHostNamingBuilder ControlTable(string value);
    public SqliteHostNamingBuilder FunctionPrefix(string value);
}

// ---- Inline scalar functions (feature inlineFunctions) ----

public sealed class SqliteHostScalarFunction
{
    public string Name { get; }
    public int MinArgs { get; }
    public int MaxArgs { get; }
    // Invoke receives the ACTUAL invoked arity: args.Length is within
    // [MinArgs..MaxArgs]; the runtime's wrapper treats absent trailing
    // args as null when mapping to the input DTO.
    public Func<SqliteHostBindingValue[], SqliteHostBindingValue> Invoke { get; }
}

// A connection that can register scalar functions. Adapter contract:
// catch EVERYTHING thrown by Invoke and report it through the SQL error
// channel prefixed with "SQLITEHOST_HANDLER_ERROR:" — an exception must
// never cross the native frames (IL2CPP safety).
public interface ISqliteHostScalarFunctionConnection : ISqliteHostConnection
{
    void RegisterScalarFunction(SqliteHostScalarFunction function);
}

// Marker on the factory = static capability: the runtime knows whether
// inlineFunctions is supported WITHOUT opening a workspace, so the
// clean-skip precheck stays workspace-free. Connections returned by a
// capable factory must implement ISqliteHostScalarFunctionConnection.
public interface ISqliteHostScalarFunctionCapableFactory : ISqliteHostConnectionFactory
{
}

public sealed class SqliteHostColumns      // all default to the protocol names
{
    public static SqliteHostColumns Default { get; }
    public string CallId { get; }          public string ItemIndex { get; }
    public string Status { get; }          public string DoneValue { get; }
    public string QueueId { get; }         public string Method { get; }
    public string Name { get; }            public string ValueType { get; }
    public string IntValue { get; }        public string RealValue { get; }
    public string TextValue { get; }       public string BlobValue { get; }
    public string Action { get; }          public string Message { get; }
}

public sealed class SqliteHostColumnsBuilder   // one setter per property, returns this
{
    public SqliteHostColumnsBuilder CallId(string value);
    // ... one fluent setter per SqliteHostColumns property, same pattern
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
    ISqliteHostDefinitionBuilder<THandlers> MinSqliteVersion(int versionNumber);  // SQLITE_VERSION_NUMBER form, e.g. 3019003
    ISqliteHostDefinitionBuilder<THandlers> Naming(Action<SqliteHostNamingBuilder> configure);
    ISqliteHostDefinitionBuilder<THandlers> Columns(Action<SqliteHostColumnsBuilder> configure);
    SqliteHostDefinition<THandlers> Methods(IReadOnlyList<IHostMethodSpec<THandlers>> methods);
}

public sealed class SqliteHostDefinition<THandlers>
{
    public int ApiLevel { get; }
    public int MinSqliteVersionNumber { get; }   // defaults to 3019003 when not set
    public SqliteHostNaming Naming { get; }
    public SqliteHostColumns Columns { get; }
    public IReadOnlyList<IHostMethodSpec<THandlers>> Methods { get; }
    public IReadOnlyList<string> SupportedFeatures { get; }
    public IReadOnlyList<string> GenerateSchemaStatements();
    public string GenerateSchemaScript();   // byte-identical to the DDL snapshot fixture
}
```

`SupportedFeatures` on the definition lists the base features
(`typedNamedBindings`, `splitResultTables`, `scriptInputs`,
`scriptVars`, `scriptControl`); the runtime's *effective* feature set
additionally includes `inlineFunctions` when the definition exposes at
least one inline function AND the connection factory implements
`ISqliteHostScalarFunctionCapableFactory`.

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
    // Exposes the method as an inline scalar function (generated code
    // emits this between .Results and .Handler). Build() validates
    // eligibility: mutates:false semantics are the generator's duty;
    // shape rules (scalar-only input, exactly one scalar result, no
    // lists) are re-checked here fail-loud.
    IHostMethodSpecBuilder<THandlers, TInput, TResult> Inline(string functionName);
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
    IInputFieldsBuilder<TInput> Float(string sqlName, Action<TInput, float> setter);
    IInputFieldsBuilder<TInput> Double(string sqlName, Action<TInput, double> setter);
    IInputFieldsBuilder<TInput> OptionalInt(string sqlName, Action<TInput, int?> setter);
    IInputFieldsBuilder<TInput> OptionalLong(string sqlName, Action<TInput, long?> setter);
    IInputFieldsBuilder<TInput> OptionalBool(string sqlName, Action<TInput, bool?> setter);
    IInputFieldsBuilder<TInput> OptionalText(string sqlName, Action<TInput, string> setter);
    IInputFieldsBuilder<TInput> OptionalBlob(string sqlName, Action<TInput, byte[]> setter);
    IInputFieldsBuilder<TInput> OptionalFloat(string sqlName, Action<TInput, float?> setter);
    IInputFieldsBuilder<TInput> OptionalDouble(string sqlName, Action<TInput, double?> setter);
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
    IListItemFieldsBuilder<TItem> Float(string sqlName, Action<TItem, float> setter);
    IListItemFieldsBuilder<TItem> Double(string sqlName, Action<TItem, double> setter);
    IListItemFieldsBuilder<TItem> OptionalInt(string sqlName, Action<TItem, int?> setter);
    IListItemFieldsBuilder<TItem> OptionalLong(string sqlName, Action<TItem, long?> setter);
    IListItemFieldsBuilder<TItem> OptionalBool(string sqlName, Action<TItem, bool?> setter);
    IListItemFieldsBuilder<TItem> OptionalText(string sqlName, Action<TItem, string> setter);
    IListItemFieldsBuilder<TItem> OptionalBlob(string sqlName, Action<TItem, byte[]> setter);
    IListItemFieldsBuilder<TItem> OptionalFloat(string sqlName, Action<TItem, float?> setter);
    IListItemFieldsBuilder<TItem> OptionalDouble(string sqlName, Action<TItem, double?> setter);
}

public interface IResultFieldsBuilder<TResult>
{
    IResultFieldsBuilder<TResult> Int(string sqlName, Func<TResult, int> getter);
    IResultFieldsBuilder<TResult> Long(string sqlName, Func<TResult, long> getter);
    IResultFieldsBuilder<TResult> Bool(string sqlName, Func<TResult, bool> getter);
    IResultFieldsBuilder<TResult> Text(string sqlName, Func<TResult, string> getter);
    IResultFieldsBuilder<TResult> Blob(string sqlName, Func<TResult, byte[]> getter);
    IResultFieldsBuilder<TResult> Float(string sqlName, Func<TResult, float> getter);
    IResultFieldsBuilder<TResult> Double(string sqlName, Func<TResult, double> getter);
    IResultFieldsBuilder<TResult> OptionalInt(string sqlName, Func<TResult, int?> getter);
    IResultFieldsBuilder<TResult> OptionalLong(string sqlName, Func<TResult, long?> getter);
    IResultFieldsBuilder<TResult> OptionalBool(string sqlName, Func<TResult, bool?> getter);
    IResultFieldsBuilder<TResult> OptionalText(string sqlName, Func<TResult, string> getter);
    IResultFieldsBuilder<TResult> OptionalBlob(string sqlName, Func<TResult, byte[]> getter);
    IResultFieldsBuilder<TResult> OptionalFloat(string sqlName, Func<TResult, float?> getter);
    IResultFieldsBuilder<TResult> OptionalDouble(string sqlName, Func<TResult, double?> getter);
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
    IListItemResultFieldsBuilder<TItem> Float(string sqlName, Func<TItem, float> getter);
    IListItemResultFieldsBuilder<TItem> Double(string sqlName, Func<TItem, double> getter);
    IListItemResultFieldsBuilder<TItem> OptionalInt(string sqlName, Func<TItem, int?> getter);
    IListItemResultFieldsBuilder<TItem> OptionalLong(string sqlName, Func<TItem, long?> getter);
    IListItemResultFieldsBuilder<TItem> OptionalBool(string sqlName, Func<TItem, bool?> getter);
    IListItemResultFieldsBuilder<TItem> OptionalText(string sqlName, Func<TItem, string> getter);
    IListItemResultFieldsBuilder<TItem> OptionalBlob(string sqlName, Func<TItem, byte[]> getter);
    IListItemResultFieldsBuilder<TItem> OptionalFloat(string sqlName, Func<TItem, float?> getter);
    IListItemResultFieldsBuilder<TItem> OptionalDouble(string sqlName, Func<TItem, double?> getter);
}
```

DTO types must be **classes**: the erased execution core passes DTOs
around boxed, so `HostMethod.For<...>` and the `List<TItem>` field
builders reject value-type DTO/item types fail-loud
(`ArgumentException`, "must be classes") at registration time.

### Compact descriptor API (size profile `compact`)

Same typed DTOs and handler interface as classic; only the registration
plumbing differs — every accessor is a pre-erased delegate (generated
code passes static method groups), so a method registration adds **no
lambdas, no display classes, and no generic instantiations**. Runtime
behavior is identical to classic by construction (both lower to the
same erased core; pinned by the profile-equivalence tests). Measured
footprint: `docs/compatibility.md`, App size.

```csharp
public static class CompactHostMethod
{
    public static ICompactHostMethodBuilder<THandlers> For<THandlers>(string methodName);
}

public interface ICompactHostMethodBuilder<THandlers>
{
    ICompactHostMethodBuilder<THandlers> ApiLevel(int apiLevel);
    ICompactHostMethodBuilder<THandlers> CreateInput(Func<object> factory);   // required
    // 14 scalar input kinds mirroring IInputFieldsBuilder, erased setters:
    //   InputInt(string, Action<object, int>), InputLong, InputBool,
    //   InputText, InputBlob, InputFloat, InputDouble,
    //   InputOptionalInt(string, Action<object, int?>), ... InputOptionalDouble
    ICompactHostMethodBuilder<THandlers> InputList(
        string sqlName,
        Func<object> createItem,
        Action<object, IReadOnlyList<object>> assignItems,
        Action<ICompactListItemFieldsBuilder> configureItem);
    // 14 scalar result kinds mirroring IResultFieldsBuilder, erased getters:
    //   ResultInt(string, Func<object, int>), ... ResultOptionalDouble
    ICompactHostMethodBuilder<THandlers> ResultList(
        string sqlName,
        Func<object, IReadOnlyList<object>> getItems,
        Action<ICompactListItemResultFieldsBuilder> configureItem);
    ICompactHostMethodBuilder<THandlers> Handler(Func<object, object, object> handler); // required
    ICompactHostMethodBuilder<THandlers> Inline(string functionName);
    IHostMethodSpec<THandlers> Build();
}

// Item builders: the same 14 scalar kinds with erased accessors.
public interface ICompactListItemFieldsBuilder { /* Int(string, Action<object,int>) ... OptionalDouble */ }
public interface ICompactListItemResultFieldsBuilder { /* Int(string, Func<object,int>) ... OptionalDouble */ }
```

`Build()` enforces the classic preconditions with the same messages
(missing `Handler`, inline shape rules) plus "has no input factory"
when `CreateInput` was not called.

### Ultra descriptor API (size profile `ultra`)

No DTO types at all: the declaration carries field names and kinds
only, handlers work against a name-keyed value surface. The trade is
compile-time typing of per-method payloads — in exchange the declared
shape is enforced **fail-loud after every handler invocation**
(unset/mistyped/undeclared result fields surface as `FailedHandler`).
Wire contract, DDL, and runtime behavior stay identical to the other
profiles.

```csharp
public static class UltraHostMethod
{
    public static IUltraHostMethodBuilder<THandlers> For<THandlers>(string methodName);
}

public interface IUltraHostMethodBuilder<THandlers>
{
    IUltraHostMethodBuilder<THandlers> ApiLevel(int apiLevel);
    // 14 scalar input kinds, declaration-only: InputInt(string), InputLong,
    //   ..., InputOptionalDouble(string)
    IUltraHostMethodBuilder<THandlers> InputList(
        string sqlName, Action<IUltraListItemFieldsBuilder> configureItem);
    // 14 scalar result kinds, declaration-only: ResultInt(string), ...,
    //   ResultOptionalDouble(string)
    IUltraHostMethodBuilder<THandlers> ResultList(
        string sqlName, Action<IUltraListItemFieldsBuilder> configureItem);
    IUltraHostMethodBuilder<THandlers> Handler(
        Func<object, SqliteHostUltraCall, SqliteHostUltraResult> handler);   // required
    IUltraHostMethodBuilder<THandlers> Inline(string functionName);
    IHostMethodSpec<THandlers> Build();
}

public interface IUltraListItemFieldsBuilder { /* Int(string) ... OptionalDouble(string), shared by input and result lists */ }

public sealed class SqliteHostUltraCall
{
    public bool IsNull(string fieldName);
    public int GetInt32(string fieldName);
    public long GetInt64(string fieldName);
    public bool GetBool(string fieldName);
    public string GetText(string fieldName);
    public byte[] GetBlob(string fieldName);
    public float GetFloat32(string fieldName);
    public double GetFloat64(string fieldName);
    public IReadOnlyList<SqliteHostUltraRow> GetList(string listName);
}

public sealed class SqliteHostUltraResult
{
    public SqliteHostUltraResult SetInt32(string fieldName, int value);      // + SetInt64,
    // SetBool, SetText, SetBlob, SetFloat32, SetFloat64 — all chainable
    public SqliteHostUltraResult SetNull(string fieldName);
    public SqliteHostUltraRow AddRow(string listName);
}

public sealed class SqliteHostUltraRow
{
    // read side (input list rows): IsNull + the same 7 Get* as SqliteHostUltraCall
    // write side (result list rows): the same 7 Set* + SetNull as
    // SqliteHostUltraResult, chainable, returning SqliteHostUltraRow
}
```

Pinned ultra read/write semantics:

- Declared-but-NULL input fields answer `IsNull` true; `Get*` on a NULL
  or missing field fails loud (undeclared names are never silently
  null).
- `GetList` on a declared list with no queued rows returns an empty
  list; undeclared list names fail loud.
- After the handler returns, the result is validated against the
  declaration: unset required fields, undeclared fields/lists, and
  type-mismatched values are handler errors (`FailedHandler` /
  `handler-error`); unset optional fields write NULL. A null string or
  blob on a required text/blob field passes the shape layer and is
  rejected by the NOT NULL result column (`FailedSql`) — classic
  parity.

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

    // Opens a workspace, checks the actual sqlite_version() against the
    // definition's MinSqliteVersionNumber, disposes, and returns the
    // outcome — lets hosts fail fast at init time (e.g. ancient
    // system-provided SQLite on old mobile clients) instead of at the
    // first Run. Run() itself also enforces the check on its first
    // workspace open (code: sqlite-version-too-low).
    public SqliteHostRunResult ValidateEnvironment();
}
```

Constructor parameter **names** are pinned (callers use named
arguments): `connectionFactory`, `hostDefinition`, `handlers`,
`options`. `options` may be null → defaults.

### SQLITEHOST_SLIM (size-critical vendoring builds)

Compiling `SqliteHost.Runtime` with the `SQLITEHOST_SLIM` define
(`-p:SqliteHostSlim=true` on dotnet builds; a Scripting Define Symbol
for Unity-vendored sources) strips the optional strict checks:
registration-time naming/shape validation, the value-type DTO guards,
lexical binding validation (`ValidateBindings` remains settable but is
ignored; `missing-binding`/`unused-binding` never fire), ultra
result-shape enforcement, and list-child-after-drain probing.
Functional semantics — schema creation, execution, drain order, the
version gate, halt/control, error mapping — are identical; measured
savings in `docs/compatibility.md`. Use it only for final size-critical
builds and keep CI/dev builds full: the stripped checks are the
fail-loud layer that catches authoring bugs.

### Runtime lifecycle (pinned semantics)

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

Namespace: `Example.Game.Generated` for the sample. The emitter takes
`--profile classic|compact|ultra` (default `classic` — existing output
unchanged), an optional `--namespace <ns>` override (used by the
repo's committed profile samples; also lets consumers run two profiles
side by side), and `--dto-fields` (DTO members as public fields
instead of auto-properties — recommended for Unity IL2CPP targets,
where the accessors cost real bytes; measured in
`docs/reports/il2cpp-size-report.md`, H-FIELDS). Classic files:

| File | Contents |
|---|---|
| `HostMethodDtos.g.cs` | input/result/item DTO classes — plain classes, public auto-properties, `List<T>` properties initialized to `new List<T>()` |
| `IGeneratedHostHandlers.g.cs` | handler interface, one method per op: `GetValueResult GetValue(GetValueInput input);` |
| `GeneratedHostMethodSpecs.g.cs` | `public static class GeneratedHostMethodSpecs` with `BuildAll()` + one private `Build<Op>Spec()` per method using the fluent API |
| `GeneratedHostDefinition.g.cs` | `public static class GeneratedHostDefinition { public static SqliteHostDefinition<IGeneratedHostHandlers> Build() }` — the `.Naming(...)` block always emits all nine naming values explicitly (six prefixes + queue/inputs/vars table names) |
| `GeneratedSchemaSql.g.cs` | `public static class GeneratedSchemaSql { public const string SchemaScript = "..."; }` — optional DDL constant, byte-identical to the snapshot |

Profile deltas (committed goldens:
`csharp/SqliteHost.Generated.Sample.Compact/`,
`csharp/SqliteHost.Generated.Sample.Ultra/`):

- **compact** — same DTOs, handler interface, definition, and schema
  files; `GeneratedHostMethodSpecs.g.cs` registers through
  `CompactHostMethod` with one private static accessor method per
  create/set/read/invoke (no lambdas anywhere in the file).
- **ultra** — no `HostMethodDtos.g.cs`; the handler interface methods
  are `SqliteHostUltraResult GetValue(SqliteHostUltraCall call);`;
  `GeneratedHostMethodSpecs.g.cs` registers through `UltraHostMethod`
  with declaration-only field calls plus one static `Invoke<Op>` per
  method.

Every generated file starts with the header line `// <auto-generated />`.

### DX deltas (author + reviewer)

The profile changes the *generated* registration and, for ultra, the
handler contract — never runtime behavior (all three lower to the same
erased core). What it costs to author and to read:

| | classic | compact | ultra |
|---|---|---|---|
| Handler signature | `GetValueResult GetValue(GetValueInput input)` | identical to classic | `SqliteHostUltraResult GetValue(SqliteHostUltraCall call)` |
| Input/result types | typed DTO classes | identical to classic | none — `call.GetText("key")` / `result.SetInt64("value", …)` |
| Field access in handlers | typed member (`input.Key`): autocomplete, F12, rename-safe | same as classic | string key: no autocomplete, typo → runtime error |
| Generated registration | fluent + inline lambdas | flat + one named `private static` accessor per field (no lambdas/closures) | declaration-only field calls + one `Invoke<Op>` per method |
| Per-app generated lines (5-method sample) | 230 | 405 | 183 |
| Fixed vendored runtime (method-independent) | 5,329 | **5,218 (smallest)** | 5,755 (largest) |

**classic and compact are identical to author against** — same DTOs, same
handler interface; compact only swaps inline lambdas for named static
accessors (smaller IL, no closures), so it is a pure size win with no
authoring-DX cost (the reviewer just sees more, simpler, helper methods).
**ultra is the only profile that changes the handler contract**: no DTOs,
string-keyed `SqliteHostUltraCall`/`Result`, so a mistyped field surfaces
at runtime instead of compile time — the DX price of its smallest
per-method output. Binary-size deltas: `docs/reports/il2cpp-size-report.md`.
