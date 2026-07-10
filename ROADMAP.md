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

## Scripting-language proposals (designed, awaiting owner decision)

What a "Lua-length" script needs vs. what SQL-as-the-language covers.
Have today: variables (`script_vars`), arithmetic/expressions (SQL),
conditionals (`WHERE`/`CASE`/`EXISTS` gating), functions (host
methods), bounded iteration (recursive CTEs, in the 3.19.3 floor —
steps themselves are a static sequence with no jumps, which keeps every
script terminating by construction). Deliberately absent and proposed:

- **Early halt/abort**: a script cannot say "stop here, success" or
  "abort with message". Sketch: a reserved `script_control` table the
  runtime checks after each step's drain (`action` = `halt` /
  `fail`, optional message) → clean `Completed`/`FailedValidation`
  outcome with the message in diagnostics. Cheap; needs a contract
  decision on status semantics.
- **Consumer-registered SQL functions**: expose custom deterministic
  scalar functions through the adapter (SQLite `create_function`) so
  hosts can offer e.g. domain math to scripts. Needs an adapter
  capability interface + validator awareness (unknown-function
  whitelist). **Version/capability caveat (owner note)**: the
  originating consumer environment — a SQLite-3.19-era wrapper — could
  not register custom functions at all; that limitation is part of why
  this toolkit exists (orchestrate with plain SQL + typed host calls
  instead of custom UDFs). If ever implemented, this must be an
  optional adapter capability gated behind a feature flag and an
  explicit min-version/capability check — never a floor requirement,
  and scripts relying on it must declare it in requiredFeatures so
  hosts without the capability clean-skip.
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
