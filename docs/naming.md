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
queueTable:   pending_host_calls   (default)
inputsTable:  script_inputs        (default)
varsTable:    script_vars          (default)
```

Override via `@hostLibrary({ queueTable: "...", inputsTable: "...",
varsTable: "..." })`. Names must be non-empty, mutually distinct, and
must not collide with any derived call/result/child table name.

## Protocol constants (deliberately NOT configurable)

Everything below is the protocol, not naming taste; making it
configurable would multiply the cross-language conformance surface for
no benefit:

- column names inside the runtime-managed tables (`queue_id`,
  `call_id`, `method`, `status`, `item_index`, `name`, `value_type`,
  `int_value`, `real_value`, `text_value`, `blob_value`);
- the result-row status literal `'done'`;
- the envelope engine string `sqlite-host-v1`;
- the queue-trigger derivation rule (`trg_<callTable>_queue` — scripts
  never reference triggers by name).

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
