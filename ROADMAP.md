# sqlitehost — Roadmap / deferred follow-ups

Items that still need hardware, accounts or credentials the repository
and its CI cannot hold. Delete entries when shipped.

- **Unity 2021.3 in-editor spike (manual, partially superseded)**: the
  sources have since been compiled and shipped as IL2CPP builds on
  Unity 2022.3 (Android/ARM64 — see
  [docs/reports/il2cpp-size-report.md](./docs/reports/il2cpp-size-report.md)),
  so the compile + IL2CPP gates are de-facto proven one LTS up. What
  remains is the literal 2021.3 floor check: open `unity/SampleProject`
  with a 2021.3 editor per
  [docs/guides/unity-2021-spike.md](./docs/guides/unity-2021-spike.md)
  (.NET Standard 2.0 API level, zero-compile-error gate, Play-mode
  smoke, record results in docs/compatibility.md).
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
