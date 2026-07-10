# Workspace schema model

Every run executes against a temporary SQLite workspace whose schema is
generated from the host definition. All DDL stays inside the SQLite
3.19.3 feature set (no JSON1, window functions, UPSERT, RETURNING,
STRICT tables, or modern-only functions).

## Shared tables

```sql
CREATE TABLE pending_host_calls (
    queue_id INTEGER PRIMARY KEY AUTOINCREMENT,
    call_id TEXT NOT NULL UNIQUE,
    method TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending'
);

CREATE TABLE script_inputs (
    name TEXT NOT NULL PRIMARY KEY,
    value_type TEXT NOT NULL,
    int_value INTEGER,
    real_value REAL,
    text_value TEXT,
    blob_value BLOB
);
```

```sql
CREATE TABLE script_vars (
    name TEXT NOT NULL PRIMARY KEY,
    value_type TEXT NOT NULL,
    int_value INTEGER,
    real_value REAL,
    text_value TEXT,
    blob_value BLOB
);
```

`script_vars` is the script's scratch variable space (feature
`scriptVars`): the runtime creates it empty and never touches it —
scripts INSERT/UPDATE/DELETE freely to hold named intermediate values
across steps (declare with `INSERT`, reassign with
`INSERT OR REPLACE`, read with a scalar subquery). Same column shape
as `script_inputs`; `value_type` is self-declared by the script for
its own bookkeeping.

Runtime inputs land in `script_inputs` before the first step:
`value_type` is the binding type (`null`/`int32`/`int64`/`bool`/`text`/
`blob`/`float32`/`float64`); `bool`, `int32`, `int64` store into
`int_value` (bool as 0/1), `float32`/`float64` into `real_value`,
`text` into `text_value`, `blob` into `blob_value`, `null` stores all
value columns as NULL.

## Per-method tables

For each method (naming derives from host-level conventions, see
`docs/naming.md`):

- **Call table** `call_<method>`: `call_id TEXT NOT NULL PRIMARY KEY`
  plus one column per scalar input field. Required fields are
  `NOT NULL`; optional fields are nullable.
- **Result table** `result_<method>`: `call_id TEXT NOT NULL PRIMARY
  KEY`, `status TEXT NOT NULL DEFAULT 'done'`, plus one column per
  scalar result field.
- **List child tables** `call_<method>__input_<field>` /
  `result_<method>__result_<field>`: `call_id TEXT NOT NULL`,
  `item_index INTEGER NOT NULL`, one column per item field,
  `PRIMARY KEY (call_id, item_index)`. List order is defined by
  `item_index`, not insertion order. Pinned index semantics:
  `item_index` values may have gaps — the mapped DTO list is dense,
  ordered by ascending `item_index` (gaps do not produce nulls or
  placeholders); duplicate `(call_id, item_index)` pairs fail at
  insert time through the primary-key constraint (`sql-error`);
  an empty list maps to an empty DTO list, never null.
- **Queue trigger** `trg_call_<method>_queue`:

```sql
CREATE TRIGGER trg_call_get_value_queue
AFTER INSERT ON call_get_value
BEGIN
    INSERT INTO pending_host_calls (call_id, method)
    VALUES (NEW.call_id, 'getValue');
END;
```

## Column type mapping

| scalar type | SQLite column |
|---|---|
| `int32`, `int64` | INTEGER |
| `boolean` | INTEGER (1/0) |
| `string` | TEXT |
| `bytes` | BLOB |
| `float32`, `float64` | REAL |

## Canonical DDL bytes

Statement order: `pending_host_calls`, `script_inputs`, then per method
in declaration order: call table, input list child tables (field
order), result table, result list child tables, trigger. Statements are
joined with a blank line; the script ends with a trailing newline;
4-space column indentation. The canonical implementation is
`codegen/core/src/ddl.ts`; `fixtures/schemas/sample-host.ddl.sql` is the
committed snapshot, and the C# `GenerateSchemaScript()` and the Java DDL
generator must reproduce it byte-for-byte.

## Result write policy

The runtime writes a result row with `status = 'done'` after a
successful handler invocation, then marks the queue row `status =
'done'`. A handler exception aborts the run (`failed-handler`) — no
partial result row is written for the failing call. Scripts should
filter on `status = 'done'` when reading result tables (forward
compatibility with future statuses).
