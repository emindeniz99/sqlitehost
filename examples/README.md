# Examples

The runnable example payloads live in [`../fixtures/payloads/valid/`](../fixtures/payloads/valid/)
— they double as integration-test inputs so they can never rot. This
walkthrough follows `example-001-read-then-conditional-write.json`.

## The script

Two steps against the sample host (`fixtures/manifests/sample-host.manifest.json`):

1. **`read-current`** — inserts a row into `call_get_value` with
   `call_id = 'read-1'` and `input_key = 'example-key'`. The insert
   trigger queues the call in `pending_host_calls`. When the step
   finishes, the runtime drains the queue: maps the row to
   `GetValueInput { Key = "example-key" }`, calls
   `handlers.GetValue(input)`, and writes the returned value into
   `result_get_value` with `status = 'done'`.
2. **`write-value`** — a conditional `INSERT … SELECT` into
   `call_set_value` that only fires when the step-1 result exists and
   differs from the target: SQL reads `result_get_value` directly. If it
   fires, the drain calls `handlers.SetValue(...)` the same way.

The orchestration decision ("write only when the current value differs")
lives entirely in the script's SQL. The host application only supplied
two typed handlers; the runtime only executed SQL and bridged typed
calls. Nothing was inferred.

## Running it (C#)

```csharp
var hostDefinition = GeneratedHostDefinition.Build();          // generated
var handlers = new GameHostHandlers(storage);                  // yours: IGeneratedHostHandlers
var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
    connectionFactory: new YourSqliteConnectionFactory(),      // yours: adapter
    hostDefinition: hostDefinition,
    handlers: handlers,
    options: new SqliteHostRuntimeOptions { EnableDiagnostics = true });

SqliteHostRunResult result = runtime.Run(script);              // parsed envelope object
```

`csharp/SqliteHost.Tests` runs exactly this against every valid fixture
with an in-memory Microsoft.Data.Sqlite adapter and fake handlers.

## Validating it (Java)

```bash
cd ../java
mvn -q package
java -cp ... io.sqlitehost.validator.cli.ValidatorCli \
    ../fixtures/manifests/sample-host.manifest.json \
    ../fixtures/payloads/valid/example-001-read-then-conditional-write.json
```

## Authoring it (TypeScript)

`@sqlite-host/authoring`'s `ScriptBuilder` reproduces this exact file —
see `typescript/authoring-sdk` tests — and its lint flags the mistakes
demonstrated by the `invalid/` fixtures before a payload ships.

Other examples: `example-002` (list<object> roundtrip driving a second
step), `example-003` (runtime inputs via `script_inputs` +
read-after-write confirmation), `example-004` (blob bytes),
`example-005` (valid but with an `unused-required-method` warning).
