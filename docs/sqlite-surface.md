# The script SQL surface — what you may not rely on

`docs/compatibility.md` says which SQLite feature set script authors
**may** assume: the required floor, **3.19.3**. This document is the
other half — the features a script may *write*, because SQL is a wide
language, but must **not rely on**.

Read the scope precisely, because it is the single most-conflated point
in this repo:

- The bans in `docs/compatibility.md` are self-imposed on **generated**
  SQL and DDL. The runtime's own emitted SQL is held to 3.19.3 and to a
  stock SQLite build, and that is enforced by the cross-language golden
  fixtures and the compatibility matrix.
- **Script SQL is not the same thing.** A payload statement is arbitrary
  SQL handed to the device engine. Nothing in the *language* stops a
  script using a 3.45 feature; the floor is a contract with the author,
  not a wall around the author.

Three categories follow: **above the floor** (works on your machine,
fails on the device), **compile-gated** (works on one device, fails on
another), and **forbidden by the validators** (breaks the runtime's own
model regardless of engine).

## 1. Above the floor — version-gated features

These parse and run on a modern SQLite and fail on a 3.19.3 engine with
`FailedSql` / `sql-error` at runtime. The version column is the release
that **introduced** the feature; anything at or below 3.19.3 is inside
the floor and fine.

| Introduced | Feature | Note |
|---|---|---|
| 3.23.0 | `TRUE` / `FALSE` keyword literals | **The trap nobody expects.** `WHERE flag = TRUE` is idiomatic modern SQL and simply is not a 3.19.3 construct. Write `= 1` / `= 0` |
| 3.24.0 | **UPSERT** — `ON CONFLICT … DO UPDATE` / `DO NOTHING` | The most-reached-for missing construct. Use `INSERT OR REPLACE` / `INSERT OR IGNORE`, which are inside the floor |
| 3.25.0 | **Window functions** (`OVER`, `PARTITION BY`, named `WINDOW`); `ALTER TABLE RENAME COLUMN` | |
| 3.27.0 | `VACUUM INTO` | |
| 3.28.0 | Extended window frames (`RANGE BETWEEN <value>`, `GROUPS`, `EXCLUDE`) | |
| 3.30.0 | `FILTER` on aggregates; `NULLS FIRST` / `NULLS LAST` | |
| 3.31.0 | **Generated columns** (`VIRTUAL` and `STORED`) | |
| 3.32.0 | `iif()` | Use `CASE WHEN` |
| 3.33.0 | `UPDATE … FROM`; the `sqlite_schema` alias | Portable introspection must still say `sqlite_master` |
| 3.35.0 | **`RETURNING`**; **math functions** (`ceil`, `floor`, `pow`, `log`, …); `ALTER TABLE DROP COLUMN`; CTE `MATERIALIZED` / `NOT MATERIALIZED` | Math functions are *doubly* gated — see §2 |
| 3.37.0 | **`STRICT` tables**; `PRAGMA table_list` | |
| 3.38.0 | JSON functions built in by default; `->` and `->>`; **`unixepoch()`**; `format()` | Before 3.38, JSON needs a compile option — see §2 |
| 3.39.0 | **`RIGHT JOIN`** and **`FULL OUTER JOIN`**; `IS [NOT] DISTINCT FROM` | `LEFT JOIN` is inside the floor; the other two are not |
| 3.43.0 | `octet_length()`, `timediff()` | |
| 3.44.0 | `concat()`, `concat_ws()`, `string_agg()`; `ORDER BY` inside aggregates | Use `||` and `group_concat()` |
| 3.45.0 | **JSONB** and the `jsonb_*` family | |

**Nothing catches these at authoring time today.** Prepare-only
validation (`docs/validation.md`, layer 3) compiles script SQL against
the JDBC driver's bundled SQLite, which is far newer than 3.19.3, so
every row above validates clean and then fails on a player's device.
Treat this table as the contract until tooling enforces it.

**The supported unlock path** is to raise the host's floor rather than
to hope: declare `@hostLibrary({ minSqliteVersion: "3.35.0" })` in
TypeSpec (or `.MinSqliteVersion(3035000)` on the C# builder). The
runtime then checks `sqlite_version()` on first workspace open and
fails loudly with `FailedSchema` / `sqlite-version-too-low`
(`docs/errors.md`) instead of failing mid-run on the device. Raising
the floor is a real product decision — it drops every device whose
system SQLite is older — but it is an *honest*, loud one.

## 2. Compile-gated modules — neither guaranteed nor validated

| Module | Gate |
|---|---|
| **FTS5** (and FTS3/4) | `SQLITE_ENABLE_FTS5` |
| **R-Tree / Geopoly** | `SQLITE_ENABLE_RTREE`, `SQLITE_ENABLE_GEOPOLY` |
| **Math functions** (`ceil`, `pow`, `log`, …) | `SQLITE_ENABLE_MATH_FUNCTIONS` **and** 3.35+ |
| **ICU** collations / `LIKE` | `SQLITE_ENABLE_ICU` |
| **JSON** before 3.38 | `SQLITE_ENABLE_JSON1` |

Raising `minSqliteVersion` **cannot** fix these: they depend on how the
device's engine was *compiled*, not on how new it is. That makes them
the nastiest failure shape in the system — a script passes validation,
runs fine on the iOS system SQLite, and fails on some Android OEM or
vendored build, or the reverse.

SqliteHost's position is deliberately narrow: **it neither requires nor
detects any of them.** No validator probes them, the compatibility
matrix does not measure them (`run-matrix.sh` builds stock
amalgamations, which have none of these), and no runtime capability
negotiation exists. A script that uses them is outside the supported
surface. If you need one, prove it on every target device yourself.

Related, and a hard rule for adapters rather than authors: **extension
loading must stay disabled.** `load_extension()` and
`sqlite3_enable_load_extension()` turn a script statement into
arbitrary native code execution. Mainstream wrappers disable it by
default; an adapter must not re-enable it.

## 3. Forbidden by the validators

These are not version or build problems — they break SqliteHost's own
execution model on **any** engine. They are being rejected by the
validators as part of a separate, parallel change; the
**validator-hardening stream owns the enforcement and the lint codes**
(pinned in `docs/validation.md`). This section states the rule and the
reason; it is not the specification of the checks.

**Transaction control — `BEGIN`, `COMMIT`, `ROLLBACK`, `SAVEPOINT`,
`RELEASE`.** The runtime issues no transaction control of its own and
provides **no atomicity beyond the single autocommitted statement**:
every script statement and every drain write commits on its own, so a
mid-step SQL error leaves the step's earlier statements committed. A
script that opens a transaction wraps the runtime's own
result-table writes and queue-status updates inside it, so a later
`ROLLBACK` erases them **after the host handlers have already run and
had real-world side effects** — the run then reports `Completed` with
calls executed and no results. Silent data loss is the exact failure
class `docs/adapter-contract.md` exists to prevent.

**`ATTACH` / `DETACH`.** `ATTACH` gives a script read *and write* access
to any reachable file on disk, including the app's own save data — it
leaves the workspace entirely. This is not confined to file-backed
workspaces: an in-memory connection can `ATTACH` an on-disk database and
create and write tables in it just the same, so the default (in-memory)
configuration is not exempt.

**`PRAGMA`.** Unsupported as a statement, in both directions:
semantics-changing pragmas (`foreign_keys`, `recursive_triggers`,
`case_sensitive_like`, `legacy_alter_table`, `trusted_schema`) silently
change how the
generated schema behaves, and `PRAGMA writable_schema=ON` lets a script
rewrite `sqlite_master` and redefine or drop the runtime's own triggers
and constraints. *Exception:* the `pragma_*` table-valued functions
inside a `SELECT` (e.g. `pragma_table_info('t')`, 3.16+) are ordinary
reads and stay legal.

**Writes and DDL against the protocol tables.** `pending_host_calls`,
every `result_<method>` table and its list children, and the
`trg_call_<method>_queue` triggers are **runtime-owned**: the drain and
the result-write policy both assume they are the only writers. Scripts
read them; scripts never write them. A script that deletes from
`pending_host_calls` makes calls silently never drain, one that inserts
into a `result_*` table forges a result the host never produced, and one
that drops a queue trigger disables the protocol while the run still
reports success. `call_<method>` inserts — how a script *makes* a call —
remain the supported path (`docs/workspace-schema.md`).

## 4. Smaller things worth knowing

- **A script's `SELECT` returns nothing to the host.** The runtime
  executes statements for effect and discards their rows. A `SELECT` is
  useful as the source of an `INSERT`, or to drive an inline scalar
  function — never to hand data back. The only channel to the host is
  the `call_*` / `result_*` tables.
- **`NOCASE` is ASCII-only.** It does not case-fold non-ASCII text, so
  player and item names with accents or non-Latin scripts do not
  compare the way an author usually expects. Non-ASCII case folding
  needs ICU, which is §2.
- **Rowids are not stable.** `VACUUM` may renumber them. Never persist a
  rowid as a key or pass one across a run.
- **Inline host functions are registered without `SQLITE_DETERMINISTIC`
  (`docs/adapter-contract.md`).** The planner may call one zero or many
  times per row, in any order. Do not write a script whose correctness
  depends on the call count.
