# Publishing SqliteHost — master guide

Publishing (plan Phase 6, ROADMAP item) cannot be completed in this
container — it needs accounts, keys, and legal signoff. Everything
mechanical is prepared in-repo (npm metadata, Maven release profile,
this guide); what remains is a checklist. Work top to bottom: the
MANUAL-ONLY list first, then the per-registry sections.

Intended artifacts (see `docs/packaging.md`):

```text
NuGet: SqliteHost.Runtime, SqliteHost.Abstractions
Maven: io.sqlitehost:sqlite-host-model / -validator / -jdbc
npm:   @sqlite-host/typespec, @sqlite-host/authoring, @sqlite-host/runtime-types
UPM:   com.sqlitehost.runtime
```

## MANUAL-ONLY steps (everything else is prepared)

These are the only steps that require a human with accounts/authority.
Each links to the section with full detail.

| # | Step | Section |
|---|------|---------|
| 1 | Name/legal signoff: availability sweep + `Sqlite` trademark review (read [sqlite.org/copyright.html](https://sqlite.org/copyright.html)); decide SqliteHost vs fallback (SqliteScriptBridge / SqliteHostBridge) | §a |
| 2 | License decision (recommended: Apache-2.0), then execute the license TODO list | §b |
| 3 | npmjs.com account + `@sqlite-host` org/scope creation + 2FA enabled | §c |
| 4 | nuget.org account + API key | §d |
| 5 | Maven Central Portal account + `io.sqlitehost` namespace verification (DNS TXT on sqlitehost.io, or use the `io.github.<user>` shortcut) | §e |
| 6 | GPG key pair generation + publish public key to a keyserver | §e |
| 7 | Fill placeholder metadata: repo URL / author / developers / scm in the npm `package.json`s and `java/pom.xml` (search for `TODO`) | §c, §e |
| 8 | Flip the publish gates: remove `"private": true` from the three npm packages; add real versions everywhere | §c |
| 9 | OpenUPM package submission PR (or decide git-URL-only) | §f |
| 10 | Final pre-flight run (tests + sync check) and tag push | §h |

Everything not in this table (pack commands, publish commands, POM
profiles, package metadata) is already scripted or written down below.

## a. Naming / trademark availability

The working name is **SqliteHost** (architecture.md resolved decision 1
— kept pending exactly this check). Fallbacks from the plan:
**SqliteScriptBridge**, **SqliteHostBridge**. Run the same sweep for a
fallback if the primary fails anywhere.

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

## b. License selection

`docs/architecture.md` resolved decision 11: **no license yet**. Pick
one before any publish — every registry below requires or strongly
expects an SPDX expression.

| | MIT | Apache-2.0 | BSD-3-Clause |
|---|---|---|---|
| Length/complexity | tiny | long | tiny |
| Explicit patent grant | no | **yes** | no |
| Trademark clause | no | yes (explicitly withholds) | no (name-endorsement clause only) |
| Contribution terms (§5) | no | yes | no |
| Corporate-adoption friction | none | none (most-vetted at large orgs) | none |

**Recommendation: Apache-2.0.** SqliteHost is a protocol-ish toolkit
(wire envelope, manifest format, generated contracts across three
languages); the explicit patent grant and the contribution/trademark
clauses are worth the longer text. Choose MIT only if maximal
simplicity is the overriding goal — it is not wrong, just weaker on
patents. BSD-3 offers nothing over MIT here.

### TODO list once chosen (mechanical, do all of it in one commit)

1. `LICENSE` file at the project root
   (`projects/sqlitehost/LICENSE`) — full license text, correct
   copyright line.
2. `"license": "<SPDX>"` in **every** `package.json` (the three
   publishable ones and, for hygiene, the private workspace/emitter
   ones): `typespec/library`, `typescript/runtime-types`,
   `typescript/authoring-sdk`, plus root, `codegen/*`,
   `typescript/sample-admin`. (JSON has no comments, so this field is
   deliberately absent today rather than a placeholder — this list is
   the reminder.)
3. `<PackageLicenseExpression><SPDX></PackageLicenseExpression>` in
   the C# csproj packing metadata (NuGet track, see §d — csproj
   metadata is owned by the C# packaging track; hand them the SPDX id).
4. `java/pom.xml`: uncomment/fill the `<licenses>` block (a commented
   TODO skeleton is already in the parent POM; children inherit).
5. `unity/com.sqlitehost.runtime/package.json`: add
   `"license": "<SPDX>"` (UPM supports the field; also ship a
   `LICENSE.md` inside the package folder — UPM convention).
6. Source headers policy: **recommend none** (no per-file headers) —
   a root `LICENSE` plus registry metadata is sufficient for MIT and
   acceptable for Apache-2.0. If you want Apache's belt-and-braces,
   use the minimal one-line SPDX form
   (`// SPDX-License-Identifier: Apache-2.0`) rather than the 12-line
   boilerplate; but note the codegen emitters would then also need to
   stamp generated files — extra churn for little gain. Decide once,
   write it in CONVENTIONS, don't mix.

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

## d. NuGet — SqliteHost.Abstractions, SqliteHost.Runtime

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
   cd projects/sqlitehost/csharp
   dotnet pack -c Release
   ```

   Pack `SqliteHost.Abstractions` and `SqliteHost.Runtime` only (the
   sample and tests never publish).
3. **Symbols:** enable snupkg in the csproj metadata
   (`IncludeSymbols=true`, `SymbolPackageFormat=snupkg`) — `dotnet
   nuget push` uploads the `.snupkg` alongside automatically.
4. **Push:**

   ```sh
   dotnet nuget push bin/Release/SqliteHost.Abstractions.<version>.nupkg \
     --api-key <KEY> --source https://api.nuget.org/v3/index.json
   dotnet nuget push bin/Release/SqliteHost.Runtime.<version>.nupkg \
     --api-key <KEY> --source https://api.nuget.org/v3/index.json
   ```

   Push Abstractions first (Runtime depends on it). nuget.org
   publishes are **immutable** — you can unlist, never replace.
5. **Package README:** nuget.org renders `PackageReadmeFile`; give
   each package a short README (same content guidance as §c).
   Icon (`PackageIcon`) is optional — skip for 0.x.
6. After the first push, reserve the `SqliteHost.*` ID prefix (§a).

## e. Maven Central — io.sqlitehost:sqlite-host-model / -validator / -jdbc

### One-time setup (manual)

1. **Central Portal account**: <https://central.sonatype.com> (the
   legacy OSSRH/Jira flow is dead; new namespaces go through the
   Portal).
2. **Namespace verification** for `io.sqlitehost` — DNS TXT record on
   `sqlitehost.io` (or the `io.github.<owner>` fallback), see §a.
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
- a **`release` profile** containing `maven-source-plugin`
  (sources jar), `maven-javadoc-plugin` (javadoc jar),
  `maven-gpg-plugin` (signing, bound inside the profile so normal
  `mvn test`/`mvn package` never asks for GPG), and
  `central-publishing-maven-plugin` with `autoPublish=false`.

Central requires all of: name, description, url, licenses, developers,
scm, javadoc + sources jars, and GPG signatures on every file — the
profile produces exactly that set.

### Releasing

```sh
cd projects/sqlitehost/java
# set the release version (drop -SNAPSHOT) across parent + modules:
mvn versions:set -DnewVersion=0.1.0 && mvn versions:commit
mvn -Prelease clean deploy
```

With `autoPublish=false` the bundle lands in the Portal in *validated,
waiting* state — review it at <https://central.sonatype.com/publishing>
and press **Publish** (or drop it). Flip to `autoPublish=true` once the
process is trusted. Note the parent POM (`sqlite-host-parent`,
packaging `pom`) publishes too — the modules reference it.

The validator's shaded `-cli.jar` is a local tool (classifier `cli`,
`createDependencyReducedPom=false` — see `java/README.md`); it rides
along as an attached artifact, which is fine, but the *library* jar is
the published contract.

## f. UPM — com.sqlitehost.runtime

Three options, in recommendation order:

1. **OpenUPM (recommended).** Zero infrastructure: tag releases in the
   public repo (OpenUPM tracks git tags that look like versions —
   configure the tag filter to match the `sqlitehost-v*` scheme in §h,
   or add plain `v0.1.0` tags too), then submit one metadata PR at
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
   https://github.com/OWNER/REPO.git?path=/projects/sqlitehost/unity/com.sqlitehost.runtime
   pin a release: …?path=/projects/sqlitehost/unity/com.sqlitehost.runtime#sqlitehost-v0.1.0
   ```

   (`?path=` selects the package subfolder in the monorepo; `#<rev>`
   pins a tag/branch/SHA.)

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

### Release checklist (order matters)

1. Choose the version `X.Y.Z`. Update, in one commit:
   npm `"version"` fields (trio + workspace root for tidiness), the
   Maven version (`mvn versions:set -DnewVersion=X.Y.Z` — replaces
   `-SNAPSHOT`), the csproj `Version`, and
   `unity/com.sqlitehost.runtime/package.json`.
2. Changelog: keep a single `CHANGELOG.md` at the project root
   (Keep-a-Changelog format), one section per release covering all
   ecosystems, with a **Protocol** subsection whenever
   apiLevel/features changed. Cut the `Unreleased` section into
   `X.Y.Z` in the same commit as step 1.
3. **Pre-flight (must be green):**

   ```sh
   cd projects/sqlitehost
   ./tests/end-to-end/run-all.sh        # all languages + cross-language goldens
   node unity/sync.mjs --check          # UPM synced copies not drifted
   ```

4. Tag: `sqlitehost-vX.Y.Z` scheme, i.e.

   ```sh
   git tag sqlitehost-v0.1.0 && git push origin sqlitehost-v0.1.0
   ```

   (monorepo — the project prefix keeps tags unambiguous; OpenUPM tag
   filter must match, §f).
5. Publish in this order (downstream metadata may reference upstream):
   Maven (`mvn -Prelease clean deploy` + Portal publish button) →
   NuGet (Abstractions then Runtime) → npm (runtime-types →
   authoring → typespec) → UPM (tag already pushed; OpenUPM picks it
   up / consumers pin the tag).
6. Post-publish smoke: §g's TypeSpec consumer test; `dotnet add
   package SqliteHost.Runtime` in a scratch project; Maven
   `dependency:get` on `io.sqlitehost:sqlite-host-model:X.Y.Z`.
7. Back on main: bump Maven to `X.Y.(Z+1)-SNAPSHOT`, open a fresh
   `Unreleased` changelog section.
