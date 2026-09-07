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

## Planned test infrastructure (after the 0.1.0 bootstrap)

Two workstreams the gap-hunt rounds argued for. Neither blocks the first
release; both belong before 1.0. Order: differential fuzzing first, because
it finds the class of bug the hunts kept finding by hand; the runtime
benchmark second, because its result feeds a design decision.

- **Property-based and differential fuzzing.** Rounds 1 to 3 found
  parity bugs between the Java and TypeScript validators and the C#
  runtime by reading code: single-quoted names, the BOM, canonical
  base64, null versus absent, float text. A seeded generator finds that
  class on purpose. The repository already has the shape the harness
  needs: `fixtures/payloads` with `expectations.json`, three consumers
  (Java, TypeScript, C#) and `scripts/check-fixture-corpus.mjs`. First
  slice: a fast-check generator in TypeScript writes a seeded temporary
  corpus of scripts and payloads, runs the TypeScript validator for the
  expected codes, and hands the corpus to the existing Java and C#
  runners; any disagreement is saved as a minimal fixture and becomes a
  committed regression case after the fix. Second slice: random SQL
  through both tokenizers, comparing token streams and analyzer verdicts
  (the three CRITICAL bypasses of round 3 were tokenizer disagreements).
  Third slice: generated values, storage classes and SQL shapes across
  the four adapters, comparing logical values and contract categories,
  never provider error text. Fourth slice: small generated scripts
  against the runtime's queue and lifecycle invariants (each call runs
  once, drain order, a failed statement blocks the step's drain, no
  state across `Run()` calls). Libraries: fast-check (TypeScript), jqwik
  (Java), FsCheck or CsCheck (C#), all test-only. PR CI runs a bounded
  fixed-seed pass; a nightly workflow runs a large campaign and keeps
  counterexamples as artifacts. Every failing seed is printed with the
  shrunk case. libFuzzer on the native adapter is out: the adapter is a
  thin P/Invoke layer over sqlite3, and fuzzing SQLite is not this
  project's job. Keep every existing deterministic test; generated tests
  add to them.
- **Runtime latency and allocation benchmark.** Every `Run()` opens a
  fresh `:memory:` workspace and creates the generated schema, so a
  50-method host executes a few hundred DDL statements before the first
  script statement. Measuring that fixed cost decides whether a cloned
  template workspace or a warm-workspace option is worth an API change,
  which is why this belongs before 1.0. Level 1: a BenchmarkDotNet
  project over the generated sample host and the four adapters,
  covering the smallest valid run, 1/10/100 statements, 1/10/50 queued
  calls, the same calls as inline scalar functions, representative
  bindings (int64, text, blob, optional), and one realistic mixed
  script; report p50, p95, mean and allocated bytes per operation;
  separate workspace open, `ValidateEnvironment()`, schema creation via
  `GenerateSchemaScript()` plus the adapter's `Execute`, and disposal
  using public operations only. Results go into a committed report in
  `docs/reports/` like the size reports, with no latency gate in PR CI.
  Level 2: the IL2CPP size bench already builds and executes the bench
  APK on the Android emulator, so a timing loop rides the same rows and
  prints JSON, labelled emulator-representative (x86_64 host, not a
  phone). On iOS the macOS runner has the iOS Simulator: an
  `iphonesimulator` IL2CPP build runs under `xcrun simctl`, executing
  the arm64 IL2CPP code on the runner's Apple Silicon CPU, so timing is
  automated there too and labelled simulator, not device. The size
  bench keeps its device build, because a simulator slice has different
  bytes. Desktop .NET numbers are never labelled as IL2CPP numbers.
  Measure first; optimizations are a separate follow-up ranked by the
  measured cost centers.

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
