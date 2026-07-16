# sqlitehost — Roadmap / deferred follow-ups

Items that still require action outside this environment. Delete
entries when shipped.

- **Unity 2021 in-editor spike (manual)**: everything is scaffolded —
  open `unity/SampleProject` in Unity Hub with a 2021.3 editor and
  follow [docs/guides/unity-2021-spike.md](./docs/guides/unity-2021-spike.md)
  (set .NET Standard 2.0 API level, zero-compile-error gate, Play-mode
  smoke, record results in docs/compatibility.md). IL2CPP build is the
  stretch goal.
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
Have today: variables (`script_vars`), arithmetic/expressions (SQL),
conditionals (`WHERE`/`CASE`/`EXISTS` gating), functions (host
methods, plus inline scalar functions for eligible getters), early
halt/abort (`script_control`), bounded iteration (recursive CTEs, in
the 3.19.3 floor — steps themselves are a static sequence with no
jumps, which keeps every script terminating by construction).
Deliberately absent and proposed:

- **Determinism lint**: warn when payload SQL calls nondeterministic
  builtins (`random()`, `date('now')` etc.) since script replays would
  diverge.
- **Imperative loops/goto across steps**: intentionally NOT proposed —
  unbounded control flow breaks the terminating-by-construction
  guarantee that makes untrusted-ish scripts tractable; recursive CTEs
  cover data-driven iteration.

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
