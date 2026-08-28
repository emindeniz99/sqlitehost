# sqlitehost — Roadmap / deferred follow-ups

Items that still need hardware, accounts or credentials the repository
and its CI cannot hold. Delete entries when shipped.

- **Unity in-editor spike (manual, mostly superseded)**: CI now compiles
  the package and runs its EditMode tests inside eight real editors, from
  the 2021.3.45f2 floor up to 6000.5.9f1
  ([docs/compatibility.md](./docs/compatibility.md)), and the sources have
  shipped as IL2CPP builds on Unity 2022.3 (Android/ARM64, see
  [docs/reports/il2cpp-size-report.md](./docs/reports/il2cpp-size-report.md)).
  What a headless run cannot cover is what remains: open
  `unity/SampleProject` in an editor per
  [docs/guides/unity-2021-spike.md](./docs/guides/unity-2021-spike.md) and
  record the .NET Standard 2.0 API-level setting and a Play-mode smoke
  pass.
- **Registry bootstrap (owner-only)**: the release pipeline is written
  and wired (`.github/workflows/release-please.yml` cuts the version,
  `release.yml` publishes npm, Maven Central and NuGet from the tag).
  What is left needs accounts and 2FA the automation cannot have: the
  `@sqlite-host` npm org plus one manual first publish per package
  before OIDC can take over, the nuget.org API key, the Central Portal
  token and GPG key, the OpenUPM submission, and name/trademark signoff
  (note the SQLite trademark caveat). The licence is decided (MIT,
  `LICENSE`) and the Maven namespace `io.github.emindeniz99` is already
  verified. Step-by-step:
  [docs/guides/publishing.md](./docs/guides/publishing.md).
- **`@sqlite-host/authoring` depends on an unpublished package**: its
  `dependencies` name `@sqlite-host/codegen-core` as `workspace:*`, and
  codegen-core is private. pnpm rewrites that to a real range in the
  tarball, so every consumer install would 404.
  `scripts/check-npm-publishable.mjs` blocks the publish until this is
  resolved — publish codegen-core too, bundle it, or drop the
  dependency.
- **The adapter conformance suite has no multi-statement or NUL coverage**,
  and two test adapters violate the contract because of it. Measured while
  fixing the native adapter's NUL truncation: `sqlite-net` accepts
  `DELETE FROM t\0 WHERE k = 'x'` and deletes every row, and
  `Microsoft.Data.Sqlite` never returns on the same input (a hang, not a
  truncation). Both are test-only adapters, so nothing shipped is affected,
  but `docs/adapter-contract.md` forbids the behaviour for every adapter and
  only `SqliteHost.Adapters.Native` is actually tested for it. The fix is a
  conformance-level multi-statement + embedded-NUL case, which will fail for
  those two adapters until they are patched.
- **Statement denylist has no DDL coverage**: `FORBIDDEN_LEADING_KEYWORDS`
  (codegen/core/src/ir.ts) stops `DELETE`/`UPDATE`-class statements but not
  `DROP TABLE`/`DROP TRIGGER`/`ALTER TABLE` against runtime-owned objects.
  Closing it changes a cross-language golden (the keyword list is projected
  into the generated Java `Protocol` and the shared fixtures), so it needs
  the full golden-regeneration dance across all three languages in one
  deliberate change — not a quick patch. Lints are not a sandbox either
  way (docs/validation.md), but this one is cheap signal worth adding.
- **Pick the iOS bench's Unity host, now that the comparison has run.**
  `.github/workflows/ios-size-bench.yml` generates the Xcode project twice
  per row — on `ubuntu-latest` in GameCI's digest-pinned iOS editor
  container, and on a macOS runner where the same action installs the
  editor onto the machine — then compiles both with one identical
  `xcodebuild` step, so a byte difference between them belongs to the
  Unity host. Measured on rows 0/2/4 (runs 33208162606 and 33217023452):

  | | Δ raw | Δ gz | Δ UnityFramework | Δ il2cppOnly |
  |---|---|---|---|---|
  | baseline | 0 | 0 | 0 | 0 |
  | compact50 | 0 | +81 B | 0 | −4 B |
  | ultra50 | 0 | +50 B | 0 | +4 B |

  Every published quantity — `total raw`, `UnityFramework`,
  `global-metadata.dat` — is identical on both hosts. The residue is
  ~0.0007% of a 12 MB artifact and it is REAL, not noise: the same row
  built twice on the same host came back byte-identical on every field,
  gz included, so the build is reproducible run to run and the remaining
  difference is attributable to the host. Same size, different content.

  Nothing forces a decision, which is why this is a roadmap item and not a
  bug: keeping both costs one extra macOS editor leg per row, and dropping
  `macos` from the `hosts` list in the `plan` job costs the cross-check.
  What must NOT happen is publishing a number without saying which host
  produced it. The unexplained part — why two hosts emit same-sized,
  different-content binaries — is unexamined; 4 bytes did not justify the
  dig, but the cause is unknown rather than known-benign.
- **Unity-packaged SQLite adapter**: `SqliteHost.Adapters.Native`
  now ships the DllImport adapter (scalar functions included; Unity
  consumers vendoring it add `[MonoPInvokeCallback]` on two callbacks
  for IL2CPP — see docs/adapter-contract.md). What remains is only the
  UPM packaging + per-platform native libsqlite3 plugin story; the
  sqlite-net wrapper pattern also stays available as
  `csharp/SqliteHost.Tests/Adapter/SqliteNetAdapter.cs`.

## Scripting-language proposals (designed, awaiting owner decision)

What a "Lua-length" script needs vs. what SQL-as-the-language covers.
(Why an embedded VM was not adopted at all — the Lua/xLua, MoonSharp,
Jint, QuickJS/V8 and Wasm comparison — is in
[docs/why-sql-not-a-vm.md](./docs/why-sql-not-a-vm.md).)
Have today: variables (`script_vars`), arithmetic/expressions (SQL),
conditionals (`WHERE`/`CASE`/`EXISTS` gating), functions (host
methods, plus inline scalar functions for eligible getters), early
halt/abort (`script_control`), data-driven iteration (recursive CTEs,
in the 3.19.3 floor — steps themselves are a static sequence with no
jumps, which keeps every script statically analyzable; it does **not**
bound run time, since SQLite does not bound a recursive CTE — see
`docs/why-sql-not-a-vm.md`).
Deliberately absent and proposed:

- **Imperative loops/goto across steps**: intentionally NOT proposed —
  unbounded control flow breaks the static-sequence property that makes
  untrusted-ish scripts tractable to lint; recursive CTEs cover
  data-driven iteration.

## Dropped (decided against, not deferred)

- **SqliteHost.Json** — optional C# JSON parse helpers. The core
  contract is that the runtime consumes a parsed `SqliteHostScript`
  object; Unity consumers have their own JSON stacks and the Java/TS
  packages already ship JSON tooling for the backend/authoring sides.
  A C# JSON helper would just bless one serializer without adding
  capability.
- **sqlite-host-spring-boot-starter** — nothing in the validator needs
  Spring; the plain library + shaded CLI cover backend integration.
  Revisit only if a real Spring consumer materializes.
