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
- **Inline host functions (scalar UDFs) — designed, awaiting
  implementation go**: non-mutating single-scalar-result host methods
  automatically exposed as SQL functions (`fn_get_value('k')`) on
  adapters capable of `sqlite3_create_function` (DllImport-style
  wrappers included), dual with the always-present call-table form,
  gated by the `inlineFunctions` feature + factory capability so
  incapable hosts clean-skip. Full design (owner decisions: automatic
  exposure with opt-out; mutations closed in v1 with the door
  documented; no idempotent flag yet; single-scalar rule and the
  obj/list answer via a future `tableFunctions` tier):
  [docs/proposals/inline-host-functions.md](./docs/proposals/inline-host-functions.md).
  **Version/capability caveat (owner note)**: the originating consumer
  environment — a SQLite-3.19-era wrapper — could not register custom
  functions at all; that limitation is part of why this toolkit
  exists. Never a floor requirement.
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
