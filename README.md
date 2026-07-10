# sqlitehost

## What it does

**SqliteHost** is a generic SQLite-first scripting and typed
host-function binding toolkit (working name; see plan v2.1). An
application defines its host methods once in TypeSpec; SqliteHost
generates matching C#, Java, and TypeScript contracts, executes parsed
SQL scripts against a temporary SQLite workspace, converts inserts into
`call_*` tables into typed host-method calls through a generated
handler interface, and writes typed results back into `result_*` tables
where later SQL can read them.

The script is the orchestrator, the host application is the executor,
SqliteHost is the typed SQLite bridge. The runtime is deliberately
thin: it never invents calls, infers business semantics, or adds
refresh/sync/log effects. It is not a Lua-style embedded VM, an ORM, or
a workflow engine — SQL (SQLite 3.19.3-compatible) *is* the scripting
surface.

```text
TypeSpec (typespec/) ──frontend──> IR (codegen/core)
   ├── manifest-emitter ──> canonical manifest + DDL snapshot (fixtures/)
   ├── csharp-emitter ────> csharp/SqliteHost.Generated.Sample
   ├── java-emitter ──────> java generated model/descriptors
   └── typescript-emitter > typescript generated types/metadata

C# runtime (Unity 2021-safe, netstandard2.0, adapter-based SQLite)
Java validator (prepare-only SQLite checks + semantic lint)
TypeScript authoring SDK (typed payload building + static lint)
```

## How to run

Prerequisites: .NET 8 SDK, JDK 17+, Maven, Node 20+, pnpm.

```bash
# everything (builds + tests all languages + cross-language goldens)
cd projects/sqlitehost
./tests/end-to-end/run-all.sh

# individual tracks
cd csharp && dotnet test                 # runtime + integration tests
cd java && mvn -q test                   # model + validator + jdbc tests
pnpm install && pnpm -r run test         # typespec, emitters, TS SDKs
node tests/cross-language-golden/run.mjs # emitters vs committed sources
```

## Docs

| Doc | What |
|---|---|
| [docs/architecture.md](./docs/architecture.md) | layers, generated-vs-handwritten boundary, lifecycle, resolved decisions |
| [docs/script-envelope.md](./docs/script-envelope.md) | the cross-language script payload contract |
| [docs/workspace-schema.md](./docs/workspace-schema.md) | call/result/queue tables, triggers, DDL canon |
| [docs/naming.md](./docs/naming.md) | host-level naming conventions + snake_case rules |
| [docs/manifest.md](./docs/manifest.md) | canonical manifest (serialized IR) |
| [docs/csharp-api.md](./docs/csharp-api.md) | pinned C# public surface |
| [docs/errors.md](./docs/errors.md) | runtime statuses + error codes |
| [docs/validation.md](./docs/validation.md) | validation layers + lint codes |
| [docs/api-levels.md](./docs/api-levels.md) | compatibility / clean-skip rules |
| [docs/compatibility.md](./docs/compatibility.md) | SQLite 3.19.3 / Unity 2021 / Java 17 / TS 5 floors |
| [docs/testing.md](./docs/testing.md) | test matrix |
| [docs/packaging.md](./docs/packaging.md) | intended distribution |

## Notes / learnings

- Built from `SqliteHostplanv2.1.md` (uploaded plan). Phases 0–5 are
  implemented in-repo; phase 6 (publishing) is deliberately deferred —
  see [ROADMAP.md](./ROADMAP.md).
- The canonical manifest + DDL snapshot under `fixtures/` are the
  keystone: every language golden-tests against the same bytes.
- v1 non-goals (per plan §30): remote delivery, signing, TTL policy,
  full SQL sandboxing, durable workflows, ORM/HTTP generation, Lua.
