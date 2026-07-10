# Architecture

SqliteHost is a generic, adapter-based, SQLite 3.19.3-compatible SQL
scripting and typed host-function binding toolkit (plan v2.1).

```text
Script author / backend / tooling = orchestrator
Host application / runtime user   = executor
SqliteHost                        = typed SQLite bridge
```

## Layers

```text
typespec/library        @sqlite-host/typespec — envelope + decorators (source of truth)
codegen/core            TypeSpec frontend → language-neutral IR; naming; canonical manifest; canonical DDL
codegen/*-emitter       IR → C# / Java / TypeScript artifacts + manifest/DDL snapshots
csharp/                 SqliteHost.Abstractions (adapter interfaces, envelope DTOs, result types)
                        SqliteHost.Runtime (execution loop, drain, mapping, fluent descriptors)
                        SqliteHost.Generated.Sample (generated-style sample host)
java/                   sqlite-host-model (envelope+manifest models), sqlite-host-validator
                        (semantic lint), sqlite-host-jdbc (prepare-only validation, DDL golden)
typescript/             @sqlite-host/runtime-types, @sqlite-host/authoring, sample-admin
fixtures/               canonical manifest, DDL snapshot, payload conformance fixtures
tests/                  cross-language golden runner, end-to-end harness
```

## Generated vs handwritten (the key boundary)

Handwritten once (generic): the C# runtime core (`Run()`, step
execution, queue drain, row↔DTO mapping engine, binding validation,
diagnostics), the Java validator engines, the TypeScript authoring
helpers, the emitter framework.

Generated per application from TypeSpec: DTOs, handler interface,
fluent descriptor registrations, host definition with naming, schema
metadata/DDL constants, manifest — for all three languages.

Analogy: the runtime is a generic protobuf runtime; TypeSpec output is
the generated message/service binding code.

## Envelope vendoring policy (resolves plan §31 open decision 3)

The script envelope is defined once in TypeSpec. Each language runtime
package ships a **vendored generated copy** (C#:
`SqliteHost.Abstractions`, Java: `sqlite-host-model`, TS:
`@sqlite-host/runtime-types`) so the runtimes don't depend on codegen at
build time. Golden tests re-run the emitters and assert the vendored
copies are byte-identical to fresh output. The `BindingValue` union is
emitted as a Unity-friendly manual representation (type discriminator +
typed accessors) rather than a language union.

## Runtime lifecycle (plan v2.1 §18)

```text
.Run(script)
  -> compatibility check (engine / apiLevel / features / methods)
  -> workspace open via ISqliteHostConnectionFactory.OpenWorkspace()
  -> generated schema create
  -> runtime inputs insert (script_inputs)
  -> per step: execute statements with typed bindings
  -> after the step succeeds: drain pending_host_calls in queue_id order
       call row (+ item_index-ordered child rows) -> input DTO
       -> IGeneratedHostHandlers method
       -> result row (+ child rows), queue row marked done
  -> structured diagnostics returned
  -> workspace dispose
```

Pinned rules: no drain between statements inside a step (which is why
parent call rows + list child rows must share a step); handlers are
invoked only through the generated interface; the runtime never invents
calls, infers effects, or adds refresh/sync/log behavior; read-after-
write is an explicit script step.

## Resolved open decisions (plan §31)

1. Working name kept: **SqliteHost** (availability check before publishing — ROADMAP).
2. C# floor: netstandard2.0 + C# 8 (Unity 2021-safe subset; in-Unity compile spike — ROADMAP).
3. BindingValue: manual discriminated class (see above).
4. Generated DTOs are the default.
5. Interface-first handlers; no static adapters in v1.
6. The runtime owns the workspace lifecycle via the factory; caller-owned
   connections are possible by handing the factory a wrapper whose
   Dispose is a no-op.
7. First official adapter: `Microsoft.Data.Sqlite` (test/tools adapter,
   lives in the C# test project); SQLite-net/Unity adapter — ROADMAP.
8. TypeSpec pinned per `typespec/library/package.json`; emitters are
   npm packages in this workspace.
9. Schema DDL is generated at build time (snapshot fixture + generated
   constants) **and** at runtime (`GenerateSchemaStatements()`), golden-
   tested to be identical.
10. Java CLI validator ships as a library API + test-driven CLI main in
    `sqlite-host-validator` (thin `Main` over the engine).
11. License: none yet — private scratch monorepo (ROADMAP before publishing).
