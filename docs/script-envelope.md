# Script envelope contract (protocol v1)

The script envelope is the cross-language payload contract. It is
defined in TypeSpec (`typespec/library`) and projected into C#
(`SqliteHost.Abstractions`), Java (`sqlite-host-model`), and TypeScript
(`@sqlite-host/runtime-types`). The JSON shape below is normative;
golden tests keep the three projections in sync.

## Shape

```json
{
  "engine": "sqlite-host-v1",
  "scriptId": "example-001",
  "requiredApiLevel": 1,
  "requiredFeatures": ["typedNamedBindings", "splitResultTables"],
  "requiredMethods": ["getValue", "setValue"],
  "inputs": [
    { "name": "targetValue", "value": { "type": "int64", "value": 42 } }
  ],
  "steps": [
    {
      "id": "read-current",
      "statements": [
        {
          "sql": "INSERT INTO call_get_value (call_id, input_key) VALUES (:callId, 'example-key')",
          "bindings": {
            "callId": { "type": "text", "value": "read-1" }
          }
        }
      ]
    }
  ]
}
```

| Field | Required | Notes |
|---|---|---|
| `engine` | yes | must be `"sqlite-host-v1"` |
| `scriptId` | no | opaque identifier for diagnostics |
| `requiredApiLevel` | yes | integer ≥ 1 |
| `requiredFeatures` | no | subset of the host's supported features, else clean skip |
| `requiredMethods` | no | methods the script uses; missing method → clean skip |
| `inputs` | no | runtime inputs inserted into `script_inputs` before step 1 |
| `steps` | yes | ordered; step `id`s must be unique and non-empty |
| `steps[].statements` | yes | ordered; each has `sql` and optional `bindings` |

## Binding values

Discriminated by `type`:

| `type` | JSON `value` | SQLite storage |
|---|---|---|
| `null` | absent | NULL |
| `int32` | number (or decimal string) in int32 range | INTEGER |
| `int64` | number when \|v\| ≤ 2^53−1, else decimal string; parsers accept both | INTEGER |
| `bool` | `true` / `false` | INTEGER 1 / 0 |
| `text` | string | TEXT |
| `blob` | base64 string (standard alphabet, padding, no line breaks) | BLOB |

Binding **names** are bare (no prefix). In SQL, named parameters may be
written `:name`, `@name`, or `$name`; a binding matches a parameter when
the names are equal after stripping the prefix character. Positional
(`?`) parameters are not supported in v1.

## Semantics

- Statements run in order within a step; steps run in order.
- The runtime drains `pending_host_calls` only after **all** statements
  in a step succeeded — never between statements of the same step.
  Result rows for calls emitted in step N are therefore visible to SQL
  starting at step N+1.
- Parent call rows and their list child rows must be emitted in the
  same step (see `docs/validation.md`, `list-child-later-step`).
- JSON parsing is not part of the core C# runtime: the runtime consumes
  a parsed `SqliteHostScript` object. Java (`sqlite-host-model`) and
  TypeScript (`@sqlite-host/runtime-types`) provide JSON parsing for
  tooling/validation. An optional `SqliteHost.Json` package may add C#
  JSON helpers later (see ROADMAP).
