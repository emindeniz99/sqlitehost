# Claude Rules — sqlitehost

Rules for AI assistants working in this repo. `CONTRIBUTING.md` is the
human-facing version; where they overlap, they agree.

## Commits and merges

- Conventional Commits, scope **mandatory**. Area scopes: `abstractions`,
  `runtime`, `adapters`, `conformance`, `delivery`, `csharp`, `java`,
  `typespec`, `codegen`, `ts`, `playground`, `unity`, `fixtures`, `tests`,
  `docs`, `ci`, `release`, `deps`, `repo`.
- Imperative subject, lowercase, no trailing period, 72 characters or less
  for the whole header. The body explains *why*, wrapped at 72.
- A protocol change legitimately spans emitters plus vendored copies plus
  fixtures, because the goldens force them to move together. Scope it at
  the driving layer and say so in the body. Unrelated topics stay separate.
- Add a `Co-Authored-By:` trailer naming the assistant and model.
- Merge pull requests with a **real merge commit** (`merge_method: "merge"`,
  locally `git merge --no-ff`). Never squash, never rebase-merge.

## The invariants that are easy to break

- **Never rename `.github/workflows/release.yml`.** npm trusted publishing
  (OIDC) matches the workflow *filename*. Renaming it kills publishing with
  no error anyone will read.
- **Publish jobs never cache.** A poisoned cache restore becomes the
  shipped artifact. Test jobs cache; publish jobs do not. Keep that split.
- **One version, one source.** `version.txt` holds it; release-please's
  `extra-files` mirror it into every npm `package.json`, the five packable
  `.csproj` files, all four `pom.xml` files, and the UPM `package.json`.
  Never hand-edit a version number, and never add a manifest without
  adding it to `release-please-config.json`.
- **Every workspace package carries `"private": true`, and for three of them
  that is the publish gate.** `@sqlite-host/typespec`,
  `@sqlite-host/runtime-types` and `@sqlite-host/authoring` are the
  publishable trio; removing the flag is the publish decision, made once in
  the bootstrap-publish commit. `@sqlite-host/codegen-core`, the four
  emitters, the playground and `sample-admin` stay private.
- **Floors are tested claims, not decoration.** `maven.compiler.release=17`;
  `SqliteHost.Abstractions` and `SqliteHost.Runtime` target `netstandard2.0`
  with `LangVersion 8.0`; the test project targets `net8.0`; every npm
  package declares `engines.node >= 20`. CI runs the declared floor and
  every maintained line above it: Node 20/22/24/26, JDK 17/21/25, and the
  .NET solution on ubuntu and windows. Raising a floor is a decision with a reason, never a
  side effect of a dependency bump.
- **The UPM package stays inside the Unity-2021.3-safe subset.**
  `unity/com.sqlitehost.runtime/package.json` declares `"unity": "2021.3"`,
  and `docs/compatibility.md` states the constraint: `netstandard2.0`,
  C# 8 subset, no records, no `required`, no `init`, no default interface
  members, no `System.Text.Json`, no source generators, no modern hosting
  abstractions. Ordinary classes, interfaces, delegates, lists, explicit
  null checks. C# 9 was evaluated and declined; do not reintroduce it.
- **`csharp/` is the only source of truth for the Unity package.** The files
  under `unity/com.sqlitehost.runtime/Runtime/` are synced copies. Edit the
  originals, run `node unity/sync.mjs`, and never hand-edit a synced copy.
  `node unity/sync.mjs --check` fails on drift.
- **Generated files are committed and never hand-edited**: every `.g.cs`,
  the generated Java and TypeScript sources, `fixtures/manifests/*.json`,
  `fixtures/schemas/*.ddl.sql`. Change `typespec/` or `codegen/`, re-emit,
  copy into place, and rerun `node tests/cross-language-golden/run.mjs`.
  That runner has no update mode by design. The only self-updating goldens
  are `UPDATE_DELIVERY_GOLDENS=1 node tests/delivery-golden/run.mjs` and
  `node unity/sync.mjs`.
- **`docs/csharp-api.md` pins the public C# surface** the emitter targets.
  Changing that surface means regenerating everything, so the doc and the
  code change in the same commit.
- **Contract changes are additive.** A breaking method change gets a new
  method name and a higher API level, keeping the old one alive
  (`docs/api-levels.md`). There is no signature-version subsystem, and v1
  is not the place to invent one.
- **The runtime carries no reflection.** No `Reflection.Emit`, no
  reflection-based row-to-DTO mapping, no dynamic code generation. A
  source-level guard test enforces it, and it is why IL2CPP consumers need
  neither `[Preserve]` nor `link.xml`. Adding reflection breaks that
  promise for every Unity consumer.
- **Generated SQL stays inside the SQLite 3.19.3 floor.** No JSON1, window
  functions, UPSERT, `RETURNING`, `STRICT` tables, or a custom build. The
  real-engine matrix in `tests/compatibility-sqlite/` measures this down to
  3.9.0.
- **`fixtures/delivery/` contains private keys on purpose.** They are
  throwaway development keys with `insecure-private` in the filename, and
  the delivery goldens cannot be verified without them. Do not delete them
  in a secret-scan cleanup, and never add `*.pem` to `.gitignore`, which
  would silently drop committed fixtures.

## Threat model

The stated model is "our backend authors scripts, validators gate them
before publication", not "arbitrary third parties upload code". The Java
validator and the TypeScript lint enforce the statement denylist and the
engine-portability rules; the C# runtime and the Unity package deliberately
contain none of it, because client bytes are the budget this project
exists to protect. Do not add runtime checks to "harden" it and do not
describe SqliteHost as a sandbox. If the model ever changes, the honest
fix is `sqlite3_set_authorizer`, which is a different project with a real
binary cost (`docs/validation.md`, `docs/why-sql-not-a-vm.md`).

## CI hygiene

- Every action SHA-pinned with the version tag in a trailing comment.
- Top-level `permissions: contents: read`; individual jobs elevate only
  what they need. `persist-credentials: false` on every checkout.
- Plain `pull_request` triggers, never `pull_request_target`.
- Never interpolate event fields (PR titles, branch names, comment bodies)
  into a `run:` block. Route them through `env:`.
- One `concurrency` group per workflow.

## Running the suites

`./tests/end-to-end/run-all.sh` is the whole matrix. Per-track commands and
the two macOS traps (`System.Data.SQLite` has no arm64 native;
`DOTNET_ROLL_FORWARD=Major` when no .NET 8 runtime is installed) are in
`CONTRIBUTING.md`. Report a skipped suite; do not call a partial run green.
