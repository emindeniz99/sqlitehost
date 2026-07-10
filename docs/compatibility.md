# Compatibility targets

## SQLite — minimum 3.19.3

Generated SQL and runtime features must work on SQLite 3.19.3. Do not
require JSON1, window functions, UPSERT, `RETURNING`, `STRICT` tables,
modern-only SQL functions, or a custom SQLite build. Constructs used
and their minimum versions: `AUTOINCREMENT`, `AFTER INSERT` triggers,
composite primary keys, multi-row `VALUES` (3.7.11), named parameters
(`:name`/`@name`/`$name`) — all well below 3.19.3.

The test environments (Microsoft.Data.Sqlite, xerial sqlite-jdbc)
bundle newer SQLite; 3.19.3 compatibility is enforced by construction
(banned-feature policy above) plus the compatibility fixture tests in
`tests/compatibility-sqlite-3.19.3` scope of the C#/Java suites. An
actual 3.19.3 binary run is a ROADMAP item.

## C# / Unity — Unity 2021 LTS and newer

`SqliteHost.Abstractions` and `SqliteHost.Runtime` target
`netstandard2.0`, C# 8 subset: no records, no `required`, no `init`, no
default interface members, no `System.Text.Json`, no source generators,
no modern hosting abstractions. Ordinary classes, interfaces,
delegates, lists, explicit null checks. An in-Unity compile spike is a
ROADMAP item (no Unity available in this environment); the source is
kept vendorable (copy the two folders + generated sample).

## Java — 17+

Generated/handwritten Java targets release 17 (records allowed,
standard collections, JDBC validation adapters). No Spring dependency
in core modules; a Spring Boot starter would be a separate module
(ROADMAP).

## TypeScript — 5+

Tooling/authoring only; the core runtime never requires Node.js.
