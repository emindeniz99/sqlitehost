# sqlitehost — Roadmap / deferred follow-ups

Deferred from plan v2.1 (out of scope or impossible in this
environment). Delete entries when shipped.

- **Unity 2021 compile spike** (plan Phase 0/§29.6): compile
  Abstractions + Runtime + Generated.Sample inside a real Unity 2021
  LTS project; confirm the exact C# profile floor. No Unity available
  in this Linux container — code is written to the documented safe
  subset instead.
- **Real SQLite 3.19.3 binary test run**: execute the C#/Java suites
  against an actual 3.19.3 build (test adapters currently bundle newer
  SQLite; compatibility is by banned-feature policy).
- **Publishing (plan Phase 6)**: name/trademark + package availability
  check, license selection, NuGet/Maven/npm publishing, UPM package
  (`com.sqlitehost.runtime`), publish TypeSpec library, migration/
  versioning docs for consumers.
- **SqliteHost.Json** optional C# JSON parse/serialize helpers package.
- **SQLite-net / SQLite4Unity3d adapter** as a shippable package
  (current official adapter is Microsoft.Data.Sqlite in the test
  project).
- **Spring Boot starter** (`sqlite-host-spring-boot-starter`).
- **float32/float64 scalars** — only after SQLite REAL mapping and
  cross-language semantics are defined.
- **TS lineage lints**: `result-read-unknown-call` /
  `result-read-not-after-call` in the TypeScript authoring lint (Java
  validator covers them today; see expectations.json `validators`).
- **Sample Unity project + Java validator CLI/service + browser admin
  demo polish** (starter-kit deliverables of plan Phase 6).
