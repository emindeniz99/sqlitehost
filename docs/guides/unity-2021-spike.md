# Unity 2021 compile spike — manual procedure

The Unity 2021 compile spike (plan Phase 0 / §29.6, ROADMAP item) cannot
run in this Linux container — it needs a real Unity editor. Everything
else is scaffolded under `unity/`; the only remaining manual step is
opening the project in Unity Hub and following this checklist.

## What is already scaffolded

| Path | What |
|---|---|
| `unity/com.sqlitehost.runtime/` | UPM package: `package.json`, `Runtime/SqliteHost.asmdef`, synced copies of `csharp/SqliteHost.Abstractions` (→ `Runtime/Abstractions/`) and `csharp/SqliteHost.Runtime` (→ `Runtime/Runtime/`), and a `Samples~/GeneratedSample/` sample (synced `*.g.cs` + handwritten `SmokeBehaviour.cs` + `SqliteHost.Sample.asmdef`) |
| `unity/sync.mjs` | Sync mechanism. `node unity/sync.mjs` regenerates the copies from `csharp/`; `node unity/sync.mjs --check` exits 1 with a diff listing on drift. `csharp/` is the source of truth — never hand-edit the synced copies. |
| `unity/SampleProject/` | Minimal Unity 2021 project: `Packages/manifest.json` referencing the package via `file:../../com.sqlitehost.runtime`, `ProjectSettings/ProjectVersion.txt` (2021.3.55f1), and `Assets/Smoke/SmokeRunner.cs` |

Deliberately **not** authored (Unity regenerates them on first open):

- `ProjectSettings/ProjectSettings.asset` and the other settings assets —
  hand-authoring them risks breaking import across editor patch versions.
  Unity creates defaults on first open; the Api Compatibility Level must
  then be set manually (step 3) — that setting **is** the spike's
  verification point.
- A scene. `Assets/Scenes/` is empty (`.gitkeep`); create one via
  `File > New Scene` (step 6). No scene wiring is needed — the smoke
  bootstraps itself via `[RuntimeInitializeOnLoadMethod]`.
- Expect Unity to rewrite `Packages/manifest.json` (appending built-in
  modules/packages) and to generate `.meta` files, including inside the
  `file:`-referenced package folder. Both are normal; commit or ignore
  the `.meta` files as you prefer — `sync.mjs` never touches non-`.cs`
  files, so they survive re-syncs.

## Procedure

1. **Install Unity Hub + Unity 2021.3 LTS.** Download Unity Hub from
   <https://unity.com/download>, then in Hub: `Installs > Install Editor >
   Archive` and pick a **2021.3.x LTS** release (the project is pinned to
   `2021.3.55f1`; any 2021.3.x works — Hub will offer to open with your
   installed version, accept and note the exact version for step 7).
   Run the spike on **both pinned LTS targets** — `2021.3.55f1` and
   `2022.3.39f1`: for the 2022.3 pass, duplicate `SampleProject` (or
   let Hub upgrade a copy) and record both results in
   docs/compatibility.md.
   No extra modules are needed for the editor gate; add a platform module
   (e.g. Windows/Linux IL2CPP) only for the stretch build in step 8.

2. **Open the project.** In Unity Hub: `Projects > Add > Add project from
   disk` → select `projects/sqlitehost/unity/SampleProject` → open it with
   the 2021.3 editor. First import takes a while (Library/ is built).

3. **Set the API level (the spike's verification point).**
   `Edit > Project Settings > Player > Other Settings > Configuration >
   Api Compatibility Level` → **.NET Standard 2.0** (2021 labels it
   ".NET Standard 2.1" vs ".NET Framework"; pick **.NET Standard 2.1**
   only if no plain ".NET Standard 2.0" entry exists — record which one
   you set in step 7; the package targets netstandard2.0 so it must
   compile under either).

4. **Gate: zero compile errors.** Open the Console (`Window > General >
   Console`), clear it, and confirm there are **no compile errors**. At
   this point Unity has compiled the package's `Runtime/` sources into
   the `SqliteHost` asmdef assembly. This is the Phase 0 spike gate. A
   `[SqliteHost] SmokeBehaviour not found …` **warning** in Play mode is
   expected until step 5.

5. **Import the sample.** `Window > Package Manager` → select
   **SqliteHost Runtime** (under "Packages: In Project") → `Samples` tab →
   **Generated Sample** → `Import`. Unity copies it to
   `Assets/Samples/SqliteHost Runtime/0.1.0/Generated Sample/` and
   compiles the `SqliteHost.Sample` asmdef. Again confirm zero compile
   errors.

6. **Play-mode smoke.** `File > New Scene` (any template), then press
   Play. `Assets/Smoke/SmokeRunner.cs` runs automatically
   (`[RuntimeInitializeOnLoadMethod]`), finds the sample's
   `SmokeBehaviour` by reflection, attaches it to a new GameObject, and
   the Console must show:

   ```
   [SqliteHost] SmokeBehaviour attached; watch the Console for the SMOKE result.
   [SqliteHost] SMOKE OK — clean-skip run behaved as pinned: status=SkippedUnsupported errorCode=unsupported-engine workspaceOpened=False …
   ```

   The smoke builds `GeneratedHostDefinition`, constructs
   `SqliteHostRuntime` with fake handlers and a fake in-memory connection
   factory, and runs a script with a mismatching engine string. Per the
   pinned lifecycle the precheck returns `SkippedUnsupported` **without
   opening a workspace** — so the run needs no SQL and no native SQLite
   plugin. `[SqliteHost] SMOKE FAILED …` (error) means the runtime
   misbehaved under Unity — record the details and stop.

7. **Record the results in `docs/compatibility.md`** (C# / Unity
   section): exact editor version (e.g. `2021.3.55f1`), the Api
   Compatibility Level you set, confirmation of zero compile errors for
   package + sample, the smoke output line, and the effective C# level
   (Unity 2021.3 ships a C# 9 compiler; the sources only need the
   documented C# 8 subset — note any construct Unity complained about,
   there should be none). Then delete the "Unity 2021 compile spike"
   entry from `ROADMAP.md`.

8. **Stretch: IL2CPP build.** `Edit > Project Settings > Player > Other
   Settings > Scripting Backend` → **IL2CPP** (requires the platform's
   IL2CPP module in Hub), then `File > Build Settings > Build` and run
   the player; check the player log for the `SMOKE OK` line. Caveat:
   `SmokeRunner` locates `SmokeBehaviour` via `Type.GetType`, so managed
   code stripping can strip it — set `Managed Stripping Level` to
   **Minimal** (or add a `link.xml` preserving the `SqliteHost.Sample`
   assembly) for the build. Record IL2CPP results (version, platform,
   stripping level, outcome) in `docs/compatibility.md` too.

## Troubleshooting

- **`SqliteHost` types not visible from a script** — asmdef visibility:
  code inside another asmdef must list `SqliteHost` in its `references`
  (the sample's `SqliteHost.Sample.asmdef` does). Loose scripts under
  `Assets/` compile into `Assembly-CSharp`, which references the package
  automatically because `SqliteHost.asmdef` sets `autoReferenced: true`.
- **Why are `InternalsVisibleTo` attributes stripped by the sync?** In
  `csharp/`, Abstractions and Runtime are two assemblies and Abstractions
  grants internals to Runtime (and both to the test project) via the
  csproj. In the UPM package both folders compile into **one** assembly
  (`SqliteHost.asmdef`), so `InternalsVisibleTo("SqliteHost.Runtime")`
  would be a redundant self-reference, and `SqliteHost.Tests` is never
  shipped to Unity. `unity/sync.mjs` therefore drops any
  `[assembly: InternalsVisibleTo(...)]` line while keeping file structure
  intact (full rationale in the script header).
- **`sync --check` fails** — the `csharp/` sources moved ahead (e.g. the
  float32/float64 work). Run `node unity/sync.mjs` from
  `projects/sqlitehost/` and re-open Unity; the script only rewrites the
  synced `.cs` copies, never the handwritten package files.
- **Where a real SQLite adapter plugs in** — the package has no SQLite
  dependency by design. To run real scripts in Unity, implement
  `ISqliteHostConnectionFactory` / `ISqliteHostConnection` /
  `ISqliteHostRow` over a Unity-friendly SQLite binding
  (SQLite4Unity3d or a sqlite-net/sqlite-net-pcl wrapper):
  `OpenWorkspace()` opens a temporary or in-memory database, `Execute`
  binds the typed `SqliteHostBinding` values to named parameters, and
  `Query` maps rows through the `ISqliteHostRow` accessors. The shape to
  mirror is the test adapter
  `csharp/SqliteHost.Tests/Adapter/MicrosoftDataSqliteAdapter.cs`; a
  shippable adapter package is a ROADMAP item.
- **Console shows errors about missing built-in modules** on first open —
  let Unity finish rewriting `Packages/manifest.json`, then
  `Assets > Reimport All` if it doesn't settle on its own.
