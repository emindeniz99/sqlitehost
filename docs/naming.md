# Naming conventions

Physical table/column naming belongs to the **host definition**, not to
individual method specs. Method specs and generated code use logical
method and field names; naming conventions derive the physical names.

## Defaults (protocol v1)

```text
callTablePrefix:       call_
resultTablePrefix:     result_
inputColumnPrefix:     input_
resultColumnPrefix:    result_
inputListTableInfix:   __input_
resultListTableInfix:  __result_
```

## Shared workspace table names (configurable per host)

The three runtime-managed tables are host-level naming too — their
names appear inside script SQL, so they flow from `@hostLibrary`
through the manifest into every language, exactly like the prefixes:

```text
queueTable:    pending_host_calls   (default)
inputsTable:   script_inputs        (default)
varsTable:     script_vars          (default)
controlTable:  script_control      (default)
functionPrefix: fn_                  (default; inline scalar functions)
```

Derived inline function name: `functionPrefix + snake(methodName)` —
`getValue` → `fn_get_value` (only for inline-eligible methods; see
`docs/proposals/inline-host-functions.md`). Diagnostics reject
collisions with derived names and SQLite built-in function names.

Override via `@hostLibrary({ queueTable: "...", ... })`. Names must be
non-empty, mutually distinct, and must not collide with any derived
call/result/child table name.

## Shared column names and the done literal (configurable per host)

Every remaining SQL-visible identifier is host-configurable through
`@hostLibrary` too — each with a `...Column` option (plus
`doneStatusValue`), resolved into the manifest's `columns` block that
every language reads:

```text
callIdColumn:     call_id       itemIndexColumn: item_index
statusColumn:     status        doneStatusValue: done
queueIdColumn:    queue_id      methodColumn:    method
nameColumn:       name          valueTypeColumn: value_type
intValueColumn:   int_value     realValueColumn: real_value
textValueColumn:  text_value    blobValueColumn: blob_value
actionColumn:     action        messageColumn:   message
```

Column names must be non-empty and mutually distinct within each
table, and the row-identity columns (`callId`/`itemIndex`/`status`)
must not collide with any derived input/result field column.

## Protocol constants (deliberately NOT configurable)

- the envelope engine string `sqlite-host-v1` — protocol identity,
  never appears in script SQL;
- the control-table action verbs `halt` / `fail` — commands **to** the
  runtime (unlike the `done` label, which is data a script filters on
  and is therefore configurable);
- the queue-trigger derivation rule (`trg_<callTable>_queue` — scripts
  never reference triggers by name);
- the manifest/envelope JSON keys (wire format, not SQL).

## Derivation rules

Canonical implementation: `codegen/core/src/naming.ts`. All other
implementations (C# runtime naming, Java DDL generator) must match it.

| Thing | Rule | Example |
|---|---|---|
| call table | `callTablePrefix + snake(methodName)` | `getValue` → `call_get_value` |
| result table | `resultTablePrefix + snake(methodName)` | `getValue` → `result_get_value` |
| input column | `inputColumnPrefix + sqlName` | `key` → `input_key` |
| result column | `resultColumnPrefix + sqlName` | `value` → `result_value` |
| input list child table | `callTable + inputListTableInfix + sqlName` | `keys` → `call_get_values__input_keys` |
| result list child table | `resultTable + resultListTableInfix + sqlName` | `entries` → `result_get_values__result_entries` |
| queue trigger | `"trg_" + callTable + "_queue"` | `trg_call_get_value_queue` |

`sqlName` defaults to `snake(propertyName)` and can be overridden with
the `@sqlName` decorator. Property names and SQL names are intentionally
separate:

```text
TypeSpec / C# / Java property: targetValue / TargetValue
SQL logical name:              target_value
Generated input column:        input_target_value
Generated result column:       result_target_value
```

## snake_case rule

Insert `_` before an uppercase letter that follows a lowercase letter or
digit, or that is followed by a lowercase letter; lowercase everything.

```text
getValue     -> get_value
defaultValue -> default_value
HTTPServer   -> http_server
putBlob2X    -> put_blob2_x
```
