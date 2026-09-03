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
- **The iOS bench's host difference is explained; both hosts stay.**
  `.github/workflows/ios-size-bench.yml` generates the Xcode project twice
  per row — on `ubuntu-latest` in GameCI's digest-pinned iOS editor
  container, and on a macOS runner where the same action installs the
  editor onto the machine — then compiles both with one identical
  `xcodebuild` step, so a byte difference between them belongs to the
  Unity host. The twelve-row matrix ran on both (run 33255105207, 48/48
  legs green) with the toolchain premise holding exactly: same Unity
  2022.3.62f3, Xcode 26.6 / 17F113, iOS SDK 26.5, same runner image,
  matching `validity` on every row.

  The large effect was a constant 393,472 B of bundle payload carried by
  the Linux-host `.app` and not the macOS-host one, on every row
  including the baseline and both probes. The per-file `appInventory`
  added to `measure-ios.mjs` named it in a single follow-up run
  (33724537438, row 0, both hosts): the Linux-host bundle has 32 files
  and the macOS-host one 31, and the whole difference is

  | file | Δ (macos − linux) |
  |---|---:|
  | `Data/level0.resS` | −393,216 B (absent on macOS) |
  | `Data/level0` | −256 B |
  | | **−393,472 B**, reconciling exactly |

  A `.resS` is Unity's streaming-resource sidecar: the scene's raw
  texture/audio/mesh bytes split out of the serialized scene file. So the
  Linux-host editor split the scene's resource data into a sidecar and
  the macOS-host editor did not, and `level0` differs by the 256 B of
  bookkeeping that is consistent with naming one. That last clause is an
  inference from sizes — the bench records file sizes, not contents.

  This never touched a published number: the measured unit is
  `UnityFramework` + `global-metadata.dat`, both outside these two files,
  and row 0's `total.raw` is byte-identical across hosts. What remains is
  two single-word differences with no further explanation and no reason
  to chase one: ±4 B inside `libGameAssembly.a` on rows 2, 4 and 6 (sign
  alternating, an alignment-class difference) and +8 B of
  `UnityFramework` on row 9. Eleven of twelve rows are byte-identical on
  every published quantity.

  Both hosts stay. The cross-check costs one extra macOS editor leg per
  row on a free runner, and it is the only thing that would notice if the
  two paths ever diverged by more than this. Dropping `macos` would never
  have removed iOS builds or the Mac — `xcodebuild` runs on a macOS
  runner for both hosts; the `hosts` dimension only selects which machine
  runs the Unity EDITOR.

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
