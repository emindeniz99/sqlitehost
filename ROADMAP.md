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
- **Shippable Unity SQLite adapter package**: the sqlite-net adapter
  pattern is implemented and tested in `csharp/SqliteHost.Tests/Adapter/
  SqliteNetAdapter.cs`; packaging it into the UPM package (with a
  native SQLite plugin story per platform) remains.

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
