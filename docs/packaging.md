# Distribution / packaging

Nothing is published yet (scratch monorepo; naming/trademark and
license checks are ROADMAP items). Intended artifacts (plan §27):

```text
NuGet: SqliteHost.Runtime
NuGet: SqliteHost.Conformance          # adapter conformance suite for consumer test projects
NuGet: SqliteHost.Adapters.Native      # pure DllImport libsqlite3 adapter (scalar functions incl.)
NuGet: SqliteHost.Abstractions
NuGet: SqliteHost.Json                 # optional/later
Maven: io.sqlitehost:sqlite-host-model
Maven: io.sqlitehost:sqlite-host-validator
Maven: io.sqlitehost:sqlite-host-jdbc
npm:   @sqlite-host/typespec
npm:   @sqlite-host/authoring
npm:   @sqlite-host/runtime-types
UPM:   com.sqlitehost.runtime          # optional/later
```

## Copy-paste / source inclusion (supported today)

Unity/game projects can vendor the C# source directly: copy the
sources from `csharp/SqliteHost.Abstractions/`,
`csharp/SqliteHost.Runtime/`, and your generated folder into the
project (see `docs/guides/getting-started.md`, Path A). No reflection-only runtime
requirement, no external dependencies, single namespace root
(`SqliteHost`) that can be find/replace-renamed. Unity Package Manager
is not required to consume the sources.

## Versioning

The protocol (`sqlite-host-v1`, manifestVersion 1, apiLevel) versions
independently from package versions. Breaking generated-contract
changes follow `docs/api-levels.md` (new method name + higher API
level), never in-place signature changes.
