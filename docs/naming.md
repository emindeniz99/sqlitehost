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

Each prefix and infix (and `functionPrefix` below) must be an ASCII
identifier fragment (`[A-Za-z_][A-Za-z0-9_]*`). Derived names inherit
it, and every protocol-table check downstream — the authoring SDK's
write denylist, the Java validator's table maps — matches ASCII
identifiers, so a name outside that shape would generate DDL SQLite
accepts while silently disabling those checks.

## Shared workspace table names (configurable per host)

The four runtime-managed tables are host-level naming too — their
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
`docs/proposals/inline-host-functions.md`). An explicit
`@hostMethod({ functionName })` must be snake_case, the same shape
`@sqlName` takes — it is registered verbatim as a SQL function name.
Diagnostics reject collisions with derived names and with every name
SQLite already owns: the built-in functions, the compile-option-gated
ones, the version-gated ones and their `json_*`/`jsonb_*` families, the
nondeterministic and wall-clock names, the built-ins scripts may not
call, and the `sqlite_*` system tables. Naming an inline function after
a built-in is not just confusing — the script lint exempts declared
inline functions from the portability and version rules, so the name
would switch those rules off for itself.

A SQLite **keyword** (`SQL_KEYWORDS` in `codegen/core/src/ir.ts`, the
147 names on <https://www.sqlite.org/lang_keywords.html>) is rejected
too. It registers cleanly and is then unreachable: `SELECT select(1)` is
a syntax error, because the parser matches the keyword before it looks
for a function.

Configurable table and column names take a narrower version of that
rule: the 59 keywords SQLite refuses in identifier position
(`SQL_KEYWORDS_UNUSABLE_AS_IDENTIFIERS`, measured — the file says how).
`CREATE TABLE select (...)` and `CREATE TABLE t (order TEXT)` are syntax
errors, and the DDL interpolates these names unquoted, so the workspace
schema would never create. The other 88 keywords stay legal names:
SQLite accepts `action`, which is this protocol's default
`actionColumn`. Derived tables and columns need no rule of their own —
each is a non-empty prefix joined to a method or field name, so no
single option decides the result; a prefix that joins *into* a keyword
(`in` + `dex`) is caught by the manifest check before any emitter runs.

Override via `@hostLibrary({ queueTable: "...", ... })`. Names must be
ASCII identifiers (`[A-Za-z_][A-Za-z0-9_]*`, same reason as the prefixes
above), mutually distinct, usable as a bare identifier, and must not
collide with any derived call/result/child table name.

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

Column names must be snake_case identifiers (`[a-z][a-z0-9_]*`, the same
shape `@sqlName` enforces — they are interpolated unquoted into the
generated DDL, so a keyword SQLite refuses in identifier position is
rejected here too) and mutually distinct within each table (compared
case-insensitively, since SQLite resolves column names
case-insensitively); the row-identity columns
(`callId`/`itemIndex`/`status`) must not collide (case-insensitively)
with any derived input/result field column.

## Protocol constants (deliberately NOT configurable)

- the envelope engine string `sqlite-host-v1` — protocol identity,
  never appears in script SQL;
- the control-table action verbs `halt` / `fail` — commands **to** the
  runtime (unlike the `done` label, which is data a script filters on
  and is therefore configurable);
- the reserved initial queue status `pending` — the queue defaults new
  rows to it and the drain selects `status = 'pending'`, so (unlike the
  configurable `done` label) it is **not** a valid `doneStatusValue`;
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
