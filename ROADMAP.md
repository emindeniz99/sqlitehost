# sqlitehost — Roadmap / deferred follow-ups

Items that still require action outside this environment. Delete
entries when shipped.

- **Unity 2021.3 in-editor spike (manual, partially superseded)**: a
  Unity-equipped agent has since compiled the sources and shipped
  IL2CPP builds on Unity 2022.3 (Android/ARM64 — see
  [docs/reports/il2cpp-size-report.md](./docs/reports/il2cpp-size-report.md)),
  so the compile + IL2CPP gates are de-facto proven one LTS up. What
  remains is the literal 2021.3 floor check: open `unity/SampleProject`
  with a 2021.3 editor per
  [docs/guides/unity-2021-spike.md](./docs/guides/unity-2021-spike.md)
  (.NET Standard 2.0 API level, zero-compile-error gate, Play-mode
  smoke, record results in docs/compatibility.md).
- **Execute the publishing checklist (manual/legal)**: accounts, 2FA,
  GPG key, `io.sqlitehost` namespace verification, `@sqlite-host` npm
  scope, license decision, and name/trademark signoff (note the SQLite
  trademark caveat) — everything else is prepared; follow
  [docs/guides/publishing.md](./docs/guides/publishing.md).
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
