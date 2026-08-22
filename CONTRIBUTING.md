# Contributing

One host contract, three languages. A TypeSpec definition compiles to a
canonical manifest, and every emitter, runtime and validator is measured
against that manifest byte for byte. A change to the contract lands in
`typespec/`, `codegen/`, the vendored generated copies in `csharp/`,
`java/`, `typescript/`, and `fixtures/` together, or the cross-language
goldens fail.

## Prerequisites

| Tool | Floor | Why |
|---|---|---|
| .NET SDK | 8.0 | libraries target `netstandard2.0`, tests target `net8.0` |
| JDK | 17 | `maven.compiler.release=17` in `java/pom.xml` |
| Maven | 3.x | builds the three Java modules; the POM declares no floor |
| Node | 20 | `engines.node` on every npm package; CI runs 20, 22, 24 and 26 |
| pnpm | 9 | the workspace root is this repo root; CI pins 10.34.5, the version the committed lockfile was written with |

Optional, for the suites that need them: `gcc`/`curl`/`unzip` for the
real-SQLite matrix, and a Chromium for the playground browser tests.

## Running the tests

The whole matrix in one command:

```bash
./tests/end-to-end/run-all.sh
```

Individual tracks:

```bash
cd csharp && dotnet test                    # runtime, adapters, integration fixtures
cd java   && mvn -q test                    # model, validator, prepare-only JDBC
pnpm install && pnpm -r run test            # typespec library, emitters, TS SDKs
node tests/cross-language-golden/run.mjs    # emitters vs committed sources
node tests/delivery-golden/run.mjs          # TS signer bytes verify under .NET
node unity/sync.mjs --check                 # UPM copies match csharp/
node tests/vendor-trim/run.mjs              # each vendoring profile compiles alone
bash tests/compatibility-sqlite/run-matrix.sh   # real SQLite 3.9.0 to newest (Linux)
```

The playground's browser tests need its web bundle built first:

```bash
pnpm --dir typescript/playground run build:web
pnpm --dir typescript/playground run test:e2e
```

### Two things that bite on a Mac

**System.Data.SQLite has no arm64 macOS native.** `System.Data.SQLite.Core`
ships `SQLite.Interop.dll` for `win-x86`, `win-x64`, `linux-x64` and
`osx-x64` only, so on Apple Silicon the 53 `SystemDataSqlite*` fixtures
die with `DllNotFoundException` before the first assertion. That adapter
is covered by the Linux and Windows CI jobs. Locally, skip it:

```bash
dotnet test --filter "FullyQualifiedName!~SystemDataSqlite"
```

That leaves 473 tests: 467 pass, 6 skip.

**No .NET 8 runtime installed?** The test assembly targets `net8.0` and
will not start on a machine that only has newer runtimes. Roll it
forward instead of installing an old runtime:

```bash
DOTNET_ROLL_FORWARD=Major dotnet test
```

The six skipped tests are engine-gated on purpose: they assert the
below-floor behaviour that only appears when `SQLITEHOST_NATIVE_SQLITE`
points at a real pre-3.19.3 build (see `tests/compatibility-sqlite/`).

## Generated files and goldens

Generated code is committed. It is an input to the C#, Java and TypeScript
builds, not a build output, and emission is deterministic.

- **Never hand-edit** a `.g.cs`, a generated Java or TypeScript file,
  `fixtures/manifests/sample-host.manifest.json`, or
  `fixtures/schemas/sample-host.ddl.sql`. Change the emitter in
  `codegen/`, or the definition in `typespec/`, and re-emit.
- **Re-emit and commit in the same change.** Each CLI takes a manifest and
  an output directory, so emit into a scratch directory and copy each file
  onto the committed one:

  ```bash
  pnpm -r run build
  node codegen/manifest-emitter/dist/cli.js \
      typespec/examples/sample-host-methods.tsp generated --base-name sample-host
  node codegen/csharp-emitter/dist/cli.js     generated/sample-host.manifest.json generated/csharp
  node codegen/java-emitter/dist/cli.js       generated/sample-host.manifest.json generated/java
  node codegen/typescript-emitter/dist/cli.js generated/sample-host.manifest.json generated/ts \
      --base-name sample-host
  ```

  `tests/cross-language-golden/run.mjs` names the destination for every
  emitted file, including the two extra C# size profiles, emitted with
  `--profile compact|ultra` plus the namespace override the committed
  samples use. Copy, then rerun the golden runner to prove the bytes.
- **Two goldens regenerate themselves.** The delivery envelopes:
  `UPDATE_DELIVERY_GOLDENS=1 node tests/delivery-golden/run.mjs`, then rerun
  without the flag to verify. The UPM copies: `node unity/sync.mjs`, with
  `csharp/` as the single source of truth.
- **The cross-language runner has no update mode on purpose.** If its
  bytes differ, either the emitter changed and you re-emit, or something
  drifted and you fix it.

## Commits

Conventional Commits with a **mandatory scope**:

```
<type>(<scope>): <imperative subject, lowercase, no trailing period>
```

Keep the whole header line at 72 characters or less. Types: `feat`, `fix`,
`docs`, `refactor`, `perf`, `test`, `chore`, `build`, `ci`, `revert`.

Scopes are areas of the tree:

| Scope | Covers |
|---|---|
| `abstractions` | `csharp/SqliteHost.Abstractions` |
| `runtime` | `csharp/SqliteHost.Runtime` |
| `adapters` | `csharp/SqliteHost.Adapters.Native` and the test adapters |
| `conformance` | `csharp/SqliteHost.Conformance` |
| `delivery` | `csharp/SqliteHost.Delivery`, the TypeScript signer, `fixtures/delivery` |
| `csharp` | solution-wide C# work, the generated samples, `csharp/SqliteHost.Tests` |
| `java` | `java/` |
| `typespec` | `typespec/` |
| `codegen` | `codegen/` |
| `ts` | `typescript/runtime-types`, `typescript/authoring-sdk`, `typescript/sample-admin` |
| `playground` | `typescript/playground` |
| `unity` | `unity/` |
| `fixtures` | `fixtures/` |
| `tests` | `tests/` |
| `docs` | `docs/`, `README.md`, this file |
| `ci` | `.github/`, the CI guard scripts in `scripts/` |
| `release` | version bumps, publish plumbing, `release-please-config.json` |
| `deps` | dependency bumps |
| `repo` | root config: `.gitignore`, `LICENSE`, `pnpm-workspace.yaml` |

A protocol change spans several of these by construction, since the
goldens force the emitter and the vendored copies to move together. Scope
it at the layer that drove the change, usually `typespec` or `codegen`,
and say so in the body. Unrelated topics still get separate commits.

The body explains **why**: the constraint, the measurement, the trade-off
you rejected. Wrap it at 72 characters. Skip it when the diff already says
everything.

AI-assisted commits carry a `Co-Authored-By:` trailer naming the assistant
and its model. Same trailer on pull request bodies and substantive review
comments.

## Merging

Pull requests merge with a **real merge commit**. Never squash, never
rebase-merge. The per-commit sequence is the record of how the contract
was built, and on `main` a squash destroys it for good. If you think a
particular branch deserves a squash, ask in the pull request first.

Do not delete the source branch as part of a merge unless the author asks.

## Releases

Nothing publishes from a laptop, and nothing publishes by accident:

- **One version, one source.** `version.txt` holds it, and
  release-please's `extra-files` in `release-please-config.json` mirror it
  into every npm `package.json`, the five packable `.csproj` files, all
  four `pom.xml` files, and the UPM `package.json`. Never hand-edit a
  version number; add any new manifest to that list instead.
- Conventional Commits on `main` drive the bump, so `feat` and `fix` are
  for user-visible changes only.
- Every workspace package carries `"private": true`. For the publishable
  trio (`@sqlite-host/typespec`, `@sqlite-host/runtime-types`,
  `@sqlite-host/authoring`) that flag is the publish gate, and removing it
  is the publish decision rather than a cleanup. The emitters,
  `@sqlite-host/codegen-core`, the playground and `sample-admin` stay
  private.
- npm trusted publishing (OIDC) matches the **filename** of the publish
  workflow. Renaming `.github/workflows/release.yml` breaks publishing
  silently.
- `docs/guides/publishing.md` has the per-registry checklists, including
  the one-time account and trusted-publisher setup only the owner can do.

## Where the rules live

`docs/` is normative, not background reading. Before changing behaviour:
`docs/architecture.md` for the generated-versus-handwritten boundary,
`docs/csharp-api.md` for the pinned public surface, `docs/api-levels.md`
for how a contract change is allowed to break, `docs/compatibility.md` for
the SQLite, Unity, Java and TypeScript floors, and `docs/validation.md`
for what the validators do and deliberately do not enforce.

`CLAUDE.md` is the compressed version of this file for AI assistants.
Where the two overlap, they agree.
