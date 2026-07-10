# Testing strategy

Maps plan §28 onto concrete suites. Everything runs on Linux in this
repo; Unity compile tests are a ROADMAP item.

## C# (`csharp/SqliteHost.Tests`, `dotnet test`)

Unit: scalar required/optional mapping, all five binding types, named
parameter prefixes (`:` `@` `$`), naming convention derivation, schema
generation (golden vs `fixtures/schemas/sample-host.ddl.sql`), queue
order, handler success/failure, generated interface invocation, list
input/output mapping incl. empty lists and `item_index` ordering,
unsupported engine/API/feature/method clean skips, binding validation
(missing/unused), statement/pending-call limits.

Integration (real `Microsoft.Data.Sqlite` adapter, in-memory
workspace): execute the parsed fixture scripts end-to-end with fake
handlers — read → conditional write → read-after-write, list roundtrip,
runtime inputs, blob roundtrip — verifying result rows feed later SQL
and diagnostics are populated.

## TypeSpec/codegen (`pnpm -r test`, node:test)

Decorator parsing, tsp→IR normalization (golden IR), manifest emission
byte-equal to the committed manifest, DDL emission byte-equal to the
committed snapshot, sqlName overrides, naming-prefix changes
propagating, unsupported-model diagnostics, deterministic output
(emit twice → identical), emitter goldens byte-equal to the committed
generated sources in `csharp/`, `java/`, `typescript/`.

## Java (`mvn test`)

Envelope/manifest JSON model round-trips; DDL generator golden vs the
snapshot; prepare-only validation catches bad table/column/syntax;
binding lint (missing/unused/type-mismatch); lineage lints; list
colocation lints; required-methods lints; explicit-column-list lint —
all driven by `fixtures/payloads/expectations.json` (the `java`
validator entries).

## TypeScript (`pnpm -r test`)

Envelope type guards + JSON round-trip; authoring builder produces
fixture-identical payloads; static lint subset driven by
`expectations.json` (`typescript` entries); metadata/autocomplete
tables match the manifest.

## Cross-language golden (`tests/cross-language-golden`)

One runner that rebuilds all emitter outputs and diffs against the
committed fixtures + vendored sources; fails on any byte difference.
Identical manifest, identical DDL, identical envelope contract,
identical table/column names, identical optional/required semantics,
identical API-level metadata.

## End-to-end (`tests/end-to-end`)

Orchestrates the full matrix: `dotnet test`, `mvn -q test`,
`pnpm -r test`, then the cross-language golden runner. This is the
single entry point CI would call.
