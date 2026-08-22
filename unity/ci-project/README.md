# `unity/ci-project` — the editor harness for Unity CI

A minimal Unity project whose only job is to put
`unity/com.sqlitehost.runtime` in front of a real editor.
`.github/workflows/unity-ci.yml` runs it through GameCI's
`unity-test-runner`; nothing else in the repo compiles the UPM package the
way Unity does.

This is **not** the sample project. `unity/SampleProject/` is the
hand-driven Play-mode spike from `docs/guides/unity-2021-spike.md`, opened
by a human. This one is opened by a container, has no scene, and runs
EditMode tests headlessly.

## What is authored here, and what Unity generates

| Path | Authored | Why |
|---|---|---|
| `Packages/manifest.json` | yes | Consumes the package from the worktree (`file:../../com.sqlitehost.runtime`) plus `com.unity.test-framework`. |
| `ProjectSettings/ProjectVersion.txt` | yes | Pins the committed project to the package floor, **2021.3.45f2**. Newer matrix legs open this same project with a newer editor and let it upgrade in place on the runner. |
| `Assets/Tests/EditMode/` | yes | The asmdef and the tests. |
| everything else | no | `Library/`, the rest of `ProjectSettings/`, every `.meta`, `packages-lock.json`. Unity writes them on first open and `.gitignore` keeps them out. |

## Why the committed pin is 2021.3.45f2

Two constraints meet at that patch:

- `com.sqlitehost.runtime/package.json` declares `"unity": "2021.3"`, and
  the floor is the version that actually breaks. An editor above the floor
  cannot prove the version consumers are promised, which is why the
  committed project sits at the floor and the guard job keeps it there.
- 2021.3.45f2 is the newest 2021.3 patch a **free personal licence** can
  activate. From .46f1 the line is Extended LTS, which needs a Unity
  Industry or Enterprise licence in CI and on a laptop alike. That also
  covers the 2021.3.55f1 in `unity/SampleProject`, so nobody should copy
  that pin here.

CI does not stop at the floor. `unity-ci.yml` runs this project through
eight editors in sequence: 2021.3.45f2, 2022.3.62f3 and the six Unity 6
lines. The workflow passes each editor version explicitly, so
`ProjectVersion.txt` is never consulted, and the in-place upgrade a newer
editor performs happens on the runner's throwaway checkout.
`docs/compatibility.md` carries the full table and the lines a personal
licence cannot reach.

## The sample is copied in, never committed

Unity ignores a package's `Samples~` folder, so `SmokeBehaviour.cs` is
invisible to the compiler until someone imports the sample. The workflow
copies `com.sqlitehost.runtime/Samples~/GeneratedSample` into
`Assets/Samples/` before the test run, which is exactly what
`Window > Package Manager > Samples > Import` does. `Assets/Samples/` is
gitignored: the package folder holds the only copy.

To reproduce a CI run locally, do the same copy, then open this folder in
Unity 2021.3.45f2 (or any editor from the workflow matrix) and run
`Window > General > Test Runner > EditMode`. The committed
`Packages/manifest.json` pins `com.unity.test-framework` at the floor's
1.1.33; each newer matrix leg overwrites that pin on the runner before the
editor starts.

## What the tests check

`Assets/Tests/EditMode/CleanSkipRunTests.cs` drives the runtime through a
fake in-memory connection factory. The package declares no native SQLite
dependency, so the behaviour it can prove without an adapter is the pinned
clean-skip contract (`docs/csharp-api.md`, `docs/errors.md`): a script the
host cannot serve is rejected by the precheck **before** a workspace is
opened. The fake connection throws on any SQL, so a refactor that moves a
precheck behind the factory fails here.

Compiling is the other half. A red compile in this project means the
package does not build on the editor version its `package.json` claims.
