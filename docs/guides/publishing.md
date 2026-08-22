# Publishing SqliteHost — master guide

Publishing (plan Phase 6, ROADMAP item) cannot be completed in this
container — it needs accounts, keys, and legal signoff. Everything
mechanical is prepared in-repo (npm metadata, the Maven `central` profile,
this guide); what remains is a checklist. Work top to bottom: the
MANUAL-ONLY list first, then the per-registry sections.

Intended artifacts (see `docs/packaging.md`):

```text
NuGet: SqliteHost.Runtime, SqliteHost.Abstractions
Maven: io.github.emindeniz99:sqlite-host-model / -validator / -jdbc
npm:   @sqlite-host/typespec, @sqlite-host/authoring, @sqlite-host/runtime-types
UPM:   com.sqlitehost.runtime
```

## MANUAL-ONLY steps (everything else is prepared)

These are the only steps that require a human with accounts/authority.
Each links to the section with full detail.

| # | Step | Section |
|---|------|---------|
| 1 | Name/legal signoff: availability sweep + `Sqlite` trademark review (read [sqlite.org/copyright.html](https://sqlite.org/copyright.html)); decide SqliteHost vs fallback (SqliteScriptBridge / SqliteHostBridge) | §a |
| 2 | ~~License decision~~ — done: MIT, applied everywhere | §b |
| 3 | npmjs.com account + `@sqlite-host` org/scope creation + 2FA enabled | §c |
| 4 | nuget.org account + API key | §d |
| 5 | Maven Central Portal account — decided: the already-verified `io.github.emindeniz99` namespace (§a outcome) | §e |
| 6 | GPG key pair generation + publish public key to a keyserver | §e |
| 7 | Fill placeholder metadata: repo URL / author / developers / scm in the npm `package.json`s and `java/pom.xml` (search for `TODO`) | §c, §e |
| 8 | Flip the publish gates: remove `"private": true` from the three npm packages (versions are release-please's job, never hand-edited) | §c |
| 9 | OpenUPM package submission PR (or decide git-URL-only) | §f |
| 10 | Bootstrap publish: one manual publish per npm package at the current version, then merge the first release-please PR | §h |
| 11 | Unity CI licence: create a free personal licence and add the `UNITY_LICENSE` / `UNITY_EMAIL` / `UNITY_PASSWORD` repository secrets | §i |

Everything not in this table (pack commands, publish commands, POM
profiles, package metadata) is already scripted or written down below.

## a. Naming / trademark availability

The working name is **SqliteHost** (architecture.md resolved decision 1
— kept pending exactly this check). Fallbacks from the plan:
**SqliteScriptBridge**, **SqliteHostBridge**. Run the same sweep for a
fallback if the primary fails anywhere.

> **Outcome (2026-08-22) — name decided: `sqlitehost` / SqliteHost, kept.**
> Availability sweep run against live registry APIs: npm name + `@sqlite-host`
> scope, NuGet `SqliteHost.*` ids, Maven Central artifact tokens, GitHub
> `emindeniz99/sqlitehost`, and `sqlitehost.io`/`.dev` domains — all free.
> Trademark screen done: SQLITE is a live USPTO registration (no. 3451983,
> Hwaci); the one documented enforcement (2014, "SQLite Database Browser"
> renamed at Richard Hipp's request) targets exactly this mark-leading
> pattern. The owner reviewed that evidence, weighed it against a decade of
> unchallenged mark-leading libraries (SQLiteCpp, npm `sqlite3`), and
> accepted the residual risk. Licence: MIT. Maven groupId: the
> `io.github.emindeniz99` GitHub-verified namespace (already verified on
> Central) instead of `io.sqlitehost` — no domain purchase; artifactIds and
> all other ids stay as written in the manifests.

### Registry availability sweep

| Registry | Check | How |
|---|---|---|
| nuget.org | `SqliteHost.Runtime`, `SqliteHost.Abstractions` free | Search <https://www.nuget.org/packages?q=SqliteHost> and try the direct URLs <https://www.nuget.org/packages/SqliteHost.Runtime> / `…/SqliteHost.Abstractions` (404 = free). Also check the bare prefix `SqliteHost` and consider [NuGet package ID prefix reservation](https://learn.microsoft.com/nuget/nuget-org/id-prefix-reservation) for `SqliteHost.*` once the first package is up. |
| Maven Central | `io.sqlitehost` groupId free | Search <https://search.maven.org/search?q=g:io.sqlitehost> and <https://central.sonatype.com/search?q=io.sqlitehost> (no results = free). The groupId is only truly yours after **namespace verification** in the Central Portal: register at <https://central.sonatype.com>, add namespace `io.sqlitehost`, and verify by publishing a DNS TXT record on `sqlitehost.io` with the token they give you. This requires owning the domain — if you don't, the zero-cost alternative is a GitHub-verified namespace `io.github.<owner>` (verified via a temporary public repo named after the token), at the cost of the nicer groupId. |
| npmjs.com | `@sqlite-host` scope free | Visit <https://www.npmjs.com/org/sqlite-host> (404 = free) and search <https://www.npmjs.com/search?q=%40sqlite-host>. A scope is claimed by creating the org: npm website → avatar → *Add an Organization* → name `sqlite-host` (free plan is fine for public packages). Also check the unscoped names aren't confusingly squatted: <https://www.npmjs.com/search?q=sqlite-host>. |
| UPM | `com.sqlitehost.runtime` | Unity package names are reverse-DNS ([Unity naming rules](https://docs.unity3d.com/Manual/cus-naming.html)): lowercase, `com.<company>.<package>`, ≤50 chars for visibility in the editor. `com.sqlitehost.runtime` complies. There is no central registry to reserve against; check OpenUPM for collisions: <https://openupm.com/packages/?q=sqlitehost>. Reverse-DNS convention implies you should control `sqlitehost.com` or at least not conflict with someone who does — fold into the domain/trademark check. |

### Trademark screen

- **SQLite is a registered trademark** of Hipp, Wyrick & Company, Inc.
  Read <https://sqlite.org/copyright.html> (and the linked trademark
  page) **before** shipping any name containing "Sqlite". Nominative
  use ("works with SQLite") is generally tolerated; a *product name*
  leading with the mark ("SqliteHost") is the risky pattern — this is
  exactly why the fallbacks exist. **Recommendation: get a legal
  review of the name before the first public artifact.** If counsel
  says no, `SqliteScriptBridge`/`SqliteHostBridge` have the same
  problem to a lesser degree (the mark is embedded, not leading the
  brand… it still leads). Have counsel look at all three at once.
- **USPTO quick screen**: <https://tmsearch.uspto.gov> → search
  "sqlitehost", "sqlite host" and the fallbacks in the trademark
  search (TESS successor). No hits ≠ clear, but hits = stop.
- **EUIPO quick screen**: <https://euipo.europa.eu/eSearch/> → same
  queries.
- General collision sweep: web search, GitHub
  (<https://github.com/search?q=sqlitehost>), crates.io/PyPI (future
  ecosystems), domain WHOIS for `sqlitehost.io` / `sqlitehost.com`.

Record the outcome (date, screenshots/links, decision) in the repo
before proceeding — every later section bakes the name into immutable
registries.

## b. License — MIT (decided)

MIT, with the copyright line in `LICENSE` at the repository root. This
resolves `docs/architecture.md` decision 11; it is a settled decision, not
an open question, and every registry below already carries the SPDX id.

Apache-2.0 was the earlier recommendation, for its explicit patent grant
and its contribution and trademark clauses. MIT won on the grounds it
usually wins on: a protocol toolkit that people vendor into Unity games
benefits more from a licence nobody has to read than from clauses nobody
here is positioned to enforce.

Where the id lives, so a new artifact does not miss one:

| Place | Form |
|---|---|
| repository root | `LICENSE`, full text |
| every `package.json` | `"license": "MIT"` (all 11 npm manifests plus the UPM one) |
| `java/pom.xml` | the parent's `<licenses>` block; the three modules inherit it |
| the five packable csproj | `<PackageLicenseExpression>MIT</PackageLicenseExpression>` |
| the UPM package folder | `unity/com.sqlitehost.runtime/LICENSE.md`, per UPM convention |

No per-file source headers, deliberately: a root `LICENSE` plus registry
metadata is what MIT needs, and headers would mean teaching the codegen
emitters to stamp generated files for no gain.

## c. npm — @sqlite-host/typespec, @sqlite-host/runtime-types, @sqlite-host/authoring

The publishable trio. `@sqlite-host/codegen-core` and the four
emitters stay `private` initially (consumers run codegen from this
repo); publish them later with the same recipe if demand appears.
`@sqlite-host/sample-admin` is a demo and never publishes.

### One-time setup (manual)

1. npm account → create org `sqlite-host` (claims the scope, §a).
2. Enable 2FA (auth-and-writes) on every account with publish rights.
3. Add maintainers to the org; grant the CI a granular automation
   token (or better, use trusted publishing/OIDC from GitHub Actions).

### Prepared metadata and the publish gate

The three packages already carry (added by this track):
`"repository"` (git+https **placeholder** URL + `"directory"`),
`"keywords"`, `"author"` (placeholder), and
`"publishConfig": {"access": "public"}`.

They also still carry **`"private": true` — that is the safety gate.**
`npm`/`pnpm` refuse to publish a private package, so nothing can ship
by accident. **Flipping the gate = the publish action:** remove
`"private": true`, set the real `"version"`, add `"license"` (§b), and
replace the `TODO` placeholders in `repository`/`author`.

### Per-package pre-publish checklist

For each of `typespec/library`, `typescript/runtime-types`,
`typescript/authoring-sdk`:

- [ ] `"files"` covers everything consumers need — verified today:
  `runtime-types` and `authoring` ship `["dist"]`; `typespec` ships
  `["dist", "lib"]` (the `.tsp` sources must ship — see §g).
- [ ] `"exports"` / `"main"` / `"types"` point at built output —
  verified today for all three.
- [ ] `README.md` in the package directory — **currently missing in
  all three**; npm renders it as the package page. Write one per
  package before publishing (what it is, install line, minimal usage,
  link back to the repo docs).
- [ ] `"license"` present (§b), placeholders replaced, `private`
  removed.
- [ ] `pnpm run build && pnpm run test` green in the package dir.
- [ ] `pnpm pack` and inspect the tarball
  (`tar -tzf *.tgz`) — no `src/`, no test output missing/extra files.
- [ ] Workspace deps: `@sqlite-host/authoring` depends on
  `@sqlite-host/runtime-types` via `workspace:*` — `pnpm publish`
  rewrites this to the real version automatically (one reason to
  publish with pnpm, not raw npm).

### Publishing

From each package directory, in dependency order
(`runtime-types` → `authoring`; `typespec` independent):

```sh
pnpm publish --access public          # local, will prompt for 2FA OTP
pnpm publish --access public --provenance   # from CI (GitHub Actions OIDC) — preferred
```

`--provenance` attaches a signed build attestation; it only works in a
supported CI environment. Long term, publish from CI only.

### Version strategy and dist-tags

**Recommendation: fixed 0.x lockstep** across the trio (and the
emitters when they publish): one version number, bumped together, even
when a package has no changes. The packages are three views of one
protocol; independent versioning invents a compatibility matrix nobody
wants to maintain. Revisit at 1.0.

Dist-tags: `latest` for releases. Use `next` for pre-releases
(`pnpm publish --tag next`) so `pnpm add @sqlite-host/typespec` never
resolves to a pre-release by accident.

## d. NuGet — SqliteHost.Abstractions, SqliteHost.Runtime, SqliteHost.Conformance, SqliteHost.Adapters.Native

The csproj packing metadata (`PackageId`, `Version`, `Description`,
`PackageLicenseExpression`, `PackageReadmeFile`, repo/source-link
properties) **is being added by the C# packaging track** — this
section only covers the workflow around it; don't duplicate the
metadata here.

1. **Account + API key (manual):** nuget.org account (Microsoft
   account), enable 2FA, create an API key scoped to *Push new
   packages and package versions* with a glob pattern `SqliteHost.*`.
2. **Pack:**

   ```sh
   cd csharp
   dotnet pack -c Release
   ```

   Pack `SqliteHost.Abstractions`, `SqliteHost.Runtime`,
   `SqliteHost.Conformance` (the adapter conformance suite consumers
   reference from their test projects — see docs/adapter-contract.md),
   and `SqliteHost.Adapters.Native` (the DllImport adapter);
   the sample and tests never publish.
3. **Symbols:** enable snupkg in the csproj metadata
   (`IncludeSymbols=true`, `SymbolPackageFormat=snupkg`) — `dotnet
   nuget push` uploads the `.snupkg` alongside automatically.
4. **Push:**

   ```sh
   dotnet nuget push bin/Release/SqliteHost.Abstractions.<version>.nupkg \
     --api-key <KEY> --source https://api.nuget.org/v3/index.json
   dotnet nuget push bin/Release/SqliteHost.Runtime.<version>.nupkg \
     --api-key <KEY> --source https://api.nuget.org/v3/index.json
   dotnet nuget push bin/Release/SqliteHost.Conformance.<version>.nupkg \
     --api-key <KEY> --source https://api.nuget.org/v3/index.json
   ```

   Push Abstractions first (Runtime depends on it). nuget.org
   publishes are **immutable** — you can unlist, never replace.
5. **Package README:** nuget.org renders `PackageReadmeFile`; give
   each package a short README (same content guidance as §c).
   Icon (`PackageIcon`) is optional — skip for 0.x.
6. After the first push, reserve the `SqliteHost.*` ID prefix (§a).

## e. Maven Central — io.github.emindeniz99:sqlite-host-model / -validator / -jdbc

### One-time setup (manual)

1. **Central Portal account**: <https://central.sonatype.com> (the
   legacy OSSRH/Jira flow is dead; new namespaces go through the
   Portal).
2. **Namespace verification** — already done: `io.github.emindeniz99` is a
   verified namespace on the Central Portal (used by apple-purchase-receipt-verifier).
3. **GPG key**:

   ```sh
   gpg --gen-key                       # RSA 4096 or ed25519, real name + email
   gpg --keyserver keyserver.ubuntu.com --send-keys <KEYID>
   ```

   Central verifies signatures against public keyservers
   (keyserver.ubuntu.com, keys.openpgp.org). Keep the private key and
   passphrase in your secrets manager; CI needs them for release
   builds only.
4. **Portal token**: Portal → *View Account* → *Generate User Token*;
   put it in `~/.m2/settings.xml`:

   ```xml
   <servers>
     <server>
       <id>central</id>
       <username><!-- token username --></username>
       <password><!-- token password --></password>
     </server>
   </servers>
   ```

### What is already in the POMs (this track)

`java/pom.xml` (parent, inherited by all three modules) now carries:

- `<name>`/`<description>` (pre-existing on parent and every module),
  `<url>` placeholder, `<developers>` placeholder, `<scm>` placeholder
  — **search for `TODO` and fill before releasing**;
- a commented `<licenses>` skeleton — uncomment when §b resolves;
- a **`central` profile** containing `maven-source-plugin`
  (sources jar), `maven-javadoc-plugin` (javadoc jar),
  `maven-gpg-plugin` (signing, bound inside the profile so normal
  `mvn test`/`mvn package` never asks for GPG), and
  `central-publishing-maven-plugin` with `autoPublish=true`.

Central requires all of: name, description, url, licenses, developers,
scm, javadoc + sources jars, and GPG signatures on every file — the
profile produces exactly that set.

### Releasing

```sh
cd java
mvn -B -P central deploy
```

The version is already correct in the working tree: release-please wrote
it into all four POMs when it cut the release PR (see §h). Never run
`mvn versions:set` by hand — `scripts/check-versions.mjs` fails the tag
build the moment a POM disagrees with `version.txt`.

That is what `.github/workflows/release.yml` runs on a version tag, so
the hand-run above is a rehearsal, not the normal path. With
`autoPublish=true` a validated bundle goes live without a Publish click;
flip the flag to `false` in `java/pom.xml` for the first release if you
want to inspect it at <https://central.sonatype.com/publishing> first.
Note the parent POM (`sqlite-host-parent`,
packaging `pom`) publishes too — the modules reference it.

The validator's shaded `-cli.jar` is a local tool (classifier `cli`,
`createDependencyReducedPom=false` — see `java/README.md`); it rides
along as an attached artifact, which is fine, but the *library* jar is
the published contract.

## f. UPM — com.sqlitehost.runtime

Three options, in recommendation order:

1. **OpenUPM (recommended).** Zero infrastructure: tag releases in the
   public repo (OpenUPM tracks git tags that look like versions — set
   the tag filter to `v*`, the scheme release-please pushes, §h), then
   submit one metadata PR at
   <https://openupm.com/packages/add/> pointing at the repo and the
   package folder. Consumers add the OpenUPM scoped registry and
   install `com.sqlitehost.runtime`. Version = the
   `unity/com.sqlitehost.runtime/package.json` `"version"` field at
   the tag — bump it in lockstep with releases (§h).
2. **Self-hosted scoped registry** (Verdaccio or a cloud npm registry):
   full control, but you now run infrastructure and consumers must add
   a custom `scopedRegistries` entry. Only worth it if OpenUPM's
   build-from-tags model doesn't fit.
3. **Plain git-URL install — works today, zero publishing.** Document
   this immediately regardless of 1/2:

   ```text
   Window > Package Manager > + > Add package from git URL…
   https://github.com/emindeniz99/sqlitehost.git?path=/unity/com.sqlitehost.runtime
   pin a release: …?path=/unity/com.sqlitehost.runtime#v0.1.0
   ```

   (`?path=` selects the package subfolder; `#<rev>` pins a
   tag/branch/SHA.)

Whatever the channel, `node unity/sync.mjs --check` must be green at
the release commit — the package contains synced copies of `csharp/`
sources (see `docs/guides/unity-2021-spike.md`).

## g. TypeSpec library specifics — @sqlite-host/typespec

This package ships **both** compiled JS (`dist/`, the `$lib`/decorator
implementations) and the TypeSpec sources (`lib/*.tsp`) — consumers'
`import "@sqlite-host/typespec";` resolves the `.tsp` side.

Current `package.json` shape — verified correct for publishing:

- `"files": ["dist", "lib"]` — both halves ship. ✔
- `"exports"."."` has the `"typespec"` condition → `./lib/main.tsp`
  plus `"types"`/`"default"` → `dist/` — this is the modern resolution
  path for the TypeSpec compiler. ✔
- `"tspMain": "lib/main.tsp"` — legacy/fallback field, harmless to
  keep. ✔
- `"peerDependencies": {"@typespec/compiler": "1.13.0"}` — **required
  change before publish:** the exact pin forces every consumer onto
  precisely 1.13.0 and will cause peer-resolution failures the moment
  they upgrade. Relax to a range (`"^1.13.0"`) for the published
  package; keep the exact version in `devDependencies` to pin what CI
  tests against. Verify the emitters (if/when published) get the same
  treatment.

Post-publish smoke test (do this once against the real registry): in a
scratch project, `pnpm add @typespec/compiler @sqlite-host/typespec`,
create a `main.tsp` with `import "@sqlite-host/typespec";` + one
decorated model, and `npx tsp compile .` — proves .tsp resolution,
decorator loading, and the peer range all work outside the workspace.

## h. Release process

### Version + lockstep policy

Two independent version axes — do not conflate them:

- **Protocol**: `sqlite-host-v1`, `manifestVersion 1`, `apiLevel N`
  (`docs/api-levels.md`). Bumps only on contract changes, never on a
  routine release.
- **Packages**: one release version shared by **all** ecosystems
  (npm trio, both NuGet packages, the Maven trio, the UPM package) —
  fixed 0.x lockstep. A release of anything is a release of
  everything; consumers reason about "SqliteHost 0.2.0", not a matrix.

### Cutting a release

Releases are automated end to end. Nothing publishes from a laptop, and
no version number is edited by hand.

1. **Land Conventional Commits on `main`.** `feat` and `fix` drive the
   bump, so keep them for user-visible changes.
2. **release-please opens (or updates) a release PR.**
   `.github/workflows/release-please.yml` computes the next version and
   writes it into `version.txt` plus every manifest listed under
   `extra-files` in `release-please-config.json`: the 11 npm
   `package.json` files, the UPM `package.json`, all four `pom.xml`
   files, and the five packable `.csproj` files. It also cuts
   `CHANGELOG.md`. Expect cosmetic XML churn in the POM and csproj
   diffs — release-please re-serializes what it edits.
3. **Review the PR, then merge it.** Merging creates the `vX.Y.Z` tag and
   the GitHub Release, and the workflow dispatches `release.yml` at that
   tag. (A tag pushed by the default `GITHUB_TOKEN` does not trigger
   other workflows on its own, which is why the dispatch is explicit.)
4. **`release.yml` publishes**, one job per registry, after a
   `verify-versions` gate that runs `node scripts/check-versions.mjs`
   and refuses to publish anything if one manifest is out of lockstep.
   Every job re-checks its registry first and skips loudly when the
   version is already there, so a hand-bootstrapped version does not
   fail the run:
   - `publish-npm` — `pnpm publish --provenance` for the trio, over npm
     trusted publishing (OIDC). No token.
   - `publish-maven` — `mvn -B -P central deploy` (§e).
   - `publish-nuget` — `dotnet pack` + `dotnet nuget push` for the five
     `SqliteHost.*` ids.
   - UPM has no job: OpenUPM builds from the tag (§f).
5. **Smoke-test the published artifacts** (§g's TypeSpec consumer test;
   `dotnet add package SqliteHost.Runtime` in a scratch project; Maven
   `dependency:get` on `io.github.emindeniz99:sqlite-host-model:X.Y.Z`),
   and confirm the provenance badge on all three npm packages.

Nothing goes back to `main` afterwards: there is no `-SNAPSHOT` to
restore and no `Unreleased` section to reopen.

### Running the pre-flight by hand

`ci.yml` gates `main` and the tag is cut from `main`, so the suites have
already run. To reproduce them locally:

```sh
./tests/end-to-end/run-all.sh        # all languages + cross-language goldens
node unity/sync.mjs --check          # UPM synced copies not drifted
node scripts/check-versions.mjs      # every manifest agrees with version.txt
node scripts/check-npm-publishable.mjs
```

### The one-time bootstrap

OIDC cannot create a package that does not exist, so the very first
publish of each npm package is manual, at the version already in
`version.txt`. `.release-please-manifest.json` records that version as
released, so the first automated release is the next one — which proves
the whole chain with no collision. The account, secret and
trusted-publisher steps are §c (npm), §d (NuGet), §e (Maven) and §i
(Unity CI).

## i. Unity CI licence secrets

`.github/workflows/unity-ci.yml` compiles `com.sqlitehost.runtime` inside
a real Unity editor and runs its EditMode tests. Unity refuses to start in
batch mode without an activated licence, and a licence cannot be
committed, so the job's `edit-mode-tests` half fails — with an explicit
`::error::` naming the missing secrets — until the owner does this once.

1. Install Unity Hub and sign in with the Unity ID that should own the CI
   activations.
2. `Unity Hub > Preferences (Settings) > Licenses > Add > Get a free
   personal license`.
3. Read the licence file Unity just wrote:

   ```text
   macOS   /Library/Application Support/Unity/Unity_lic.ulf
   Linux   ~/.local/share/unity3d/Unity/Unity_lic.ulf
   Windows C:\ProgramData\Unity\Unity_lic.ulf
   ```

4. `Settings > Secrets and variables > Actions > New repository secret`,
   three times:

   | Secret | Value |
   |---|---|
   | `UNITY_LICENSE` | the entire contents of `Unity_lic.ulf`, XML declaration included |
   | `UNITY_EMAIL` | the Unity ID email |
   | `UNITY_PASSWORD` | the Unity ID password |

   All three are needed: the editor re-validates the account when it
   applies the licence file, so the `.ulf` alone is not enough.

Two constraints are baked into the workflow and should not be "simplified"
away:

- **A personal licence allows only a couple of concurrent activations.**
  The version matrix therefore runs `max-parallel: 1`. Adding editor
  versions makes the job longer, not wider.
- **Unity 2021.3 patches newer than 2021.3.45f2 are Extended LTS** and
  refuse to activate under a personal licence, which is why CI pins
  2021.3.45f2 — the newest patch a personal licence can run, and still on
  the `2021.3` line the package declares as its floor. `unity/SampleProject`
  pins a later patch on purpose; it is opened by a human with their own
  editor, not by CI.

A Unity Pro/Plus seat activates from a serial (`UNITY_SERIAL`) instead and
would need the workflow's licence env adjusted; nothing else changes.
