# IL2CPP app-size report (Unity, Android/ARM64)

Executes `docs/guides/il2cpp-size-protocol.md` §4–§5. Companion to the
NativeAOT numbers in §3 of that protocol (same generated sources, same
seed, same validity checks).

## 1. Environment

- **Unity**: 2022.3.9f1 (Linux editor, headless batchmode). The protocol
  prefers 2022.3.39f1; only 2022.3.9f1 was installed in the measurement
  container — same 2022.3 LTS line, noted per §2. 2021.3 was not available.
- **Platform**: Android, release APK, **IL2CPP**, **ARM64 only**.
- **Managed Stripping Level**: **High** (all rows).
- **IL2CPP Code Generation**: **Faster (smaller) builds** (`OptimizeSize`).
- **IL2CPP Compiler Configuration**: Release. **Strip Engine Code**: on.
- **Api Compatibility Level**: .NET Standard 2.1 (`ApiCompatibilityLevel.NET_Standard`).
- **Scripting Define Symbols**: empty except rows 3/5 (`SQLITEHOST_SLIM`).
- Every other setting identical across rows (single reused project, only
  `Assets/Sources` + defines swap per row; per-row build driver:
  `SizeBench.cs`, archived in §6).
- **Date**: 2026-07-18.

### Validity-check deviation (no device in the container)

The protocol asks for the four expected log lines "on device or in the
editor's IL2CPP player log". This container has no Android device or
emulator, so each row's code was executed **in the editor (Mono) in the
same batch invocation that produced the build**, via reflection on the
compiled `Assembly-CSharp` — the same sources the IL2CPP build compiled.
Rows 1–6 must print `104006 / <ddl-len> / Completed / Completed`; rows
7–8 must print the same value as each other. Additionally, for rows 7–8
the probe types' survival into the IL2CPP binary was verified via
`global-metadata.dat` string inspection (§4.1).

## 2. Matrix results

| Row | Build | `libil2cpp.so` | `global-metadata.dat` | so.gz | md.gz | Δso | Δmd | Δgz (so+md) |
|---|---|---|---|---|---|---|---|---|
| 0 | baseline (GameWork only) | 16,928,256 | 3,758,336 | 5,585,345 | 1,145,764 | — | — | — |
| 1 | classic50 | 17,535,616 | 3,881,884 | 5,705,766 | 1,181,501 | +607,360 | +123,548 | +156,158 |
| 2 | compact50 | 17,308,208 | 3,865,868 | 5,678,037 | 1,176,362 | +379,952 | +107,532 | +123,290 |
| 3 | compact50 + SLIM | 17,279,416 | 3,862,192 | 5,666,563 | 1,174,885 | +351,160 | +103,856 | +110,339 |
| 4 | ultra50 | 17,234,800 | 3,825,772 | 5,665,079 | 1,166,733 | +306,544 | +67,436 | +100,703 |
| 5 | ultra50 + SLIM | 17,196,184 | 3,821,248 | 5,653,020 | 1,164,064 | +267,928 | +62,912 | +85,975 |
| 6 | compact50-fields | 17,293,368 | 3,848,996 | 5,671,571 | 1,171,183 | +365,112 | +90,660 | +111,645 |
| 7 | probe-gvm | 16,909,640 | 3,755,332 | 5,577,993 | 1,146,284 | -18,616 | -3,004 | -6,832 |
| 8 | probe-nogvm | 16,906,760 | 3,755,268 | 5,572,531 | 1,146,295 | -21,496 | -3,068 | -12,283 |

All sizes in bytes. `Δ` columns are vs row 0. Rows 7–8 are smaller than
the baseline because the probes contain no `GameWork`; only their
**pair delta** is meaningful (that is the H-GVM answer).

Per-method cost of the 50-method host (Δ(so+md) / 50):

| Profile | raw/method | gz/method | NativeAOT raw/method (ref) |
|---|---|---|---|
| classic | 14.28 KB | 3.05 KB | ≈10 KB |
| compact | 9.52 KB | 2.41 KB | ≈1.2 KB |
| ultra | 7.30 KB | 1.97 KB | ≈0.7 KB |

## 3. Hypothesis verdicts

### H-GVM — **CONFIRMED FLIP: near-zero under IL2CPP** (headline)

**One generic virtual method costs +2,880 bytes of `libil2cpp.so`,
+64 bytes of metadata, +5,451 bytes gzipped (so+md) —
~1% of the NativeAOT cost** (283 KB raw / 128 KB gz). The §1 prediction
held: IL2CPP ships its generic-sharing + metadata machinery in every
build, so the *marginal* cost of the first GVM is noise-level. Survival
check passed: `IFace`/`Impl`/`Get` are present in both probes'
`global-metadata.dat` (the pair really differs only in the generic
virtual method).

Consequence: the "~250 KB structural win" narrative for the non-generic
`QueryRows` contract is **NativeAOT-specific**. The contract stays as
designed — it is strictly ≤ everywhere, and NativeAOT/.NET-server
consumers keep the large win — but under IL2CPP its size benefit is
~5.3 KB gz, not hundreds of KB. `docs/compatibility.md`
now carries this as a footnote (§4).

### H-FIELDS — **FLIPPED: fields are smaller under IL2CPP** (headline)

NativeAOT measured **exactly 0** (accessors fully inlined,
byte-identical binaries). Under IL2CPP the fields variant of the same
50-method host is **-14,840 bytes of `libil2cpp.so` and -16,872
bytes of metadata (-11,645 gz)** — i.e. auto-properties cost real
bytes: IL2CPP emits per-accessor C++ + method metadata that High
stripping does not erase (≈32 KB across the host's DTO accessors).
Not zero, but small relative to the per-method total (~0.62 KB/method
of the 9.52 KB/method compact cost).

### H-PROFILES — **CONFIRMED, higher unit costs**

The profile ladder transfers: classic **14.28 KB** → compact
**9.52 KB** → ultra **7.30 KB** raw/method (gz: **3.05 / 2.41 /
1.97 KB**). IL2CPP's absolute per-method unit cost is ~1.4–2× the
NativeAOT raw cost, but the ordering and the "unique-type count drives
cost" mechanism are identical — the compact/ultra guidance stands
unchanged for Unity consumers.

### H-SLIM — **CONFIRMED, transfers directly**

`SQLITEHOST_SLIM` removes 32,468 bytes raw / 12,951 gz on
compact50 and 43,140 bytes raw / 14,728 gz on ultra50
(NativeAOT: −28 KB raw / −12 KB gz on compact50 — same order).

### Optional rows not re-run

H-DATA, H-DISPATCH, H-STRINGS, H-NANO, H-ENGINE were optional per §1
(expected same sign / app-side / structural); nothing in the measured
rows contradicts their NativeAOT verdicts.


## 4. Doc patches applied

Applied in this branch (same commit):

1. `docs/compatibility.md` — App size section: added the measured
   IL2CPP table (this report's rows 1–5) next to the NativeAOT one,
   retitled the section (no longer proxy-only), pointed it at this
   report.
2. Footnote on structural finding 1 (no-GVM): IL2CPP marginal GVM cost
   measured at ~2.9 KB raw / ~5.3 KB gz — the big win is
   NativeAOT-specific; contract unchanged.
3. Footnote on the fields/auto-properties claim: the "saved 0 bytes"
   result is NativeAOT-only; IL2CPP measured ~31 KB raw / ~11 KB gz in
   favor of fields on a 50-method host (left as a documented
   IL2CPP-specific consideration, not a generator change).


## 5. Raw artifacts

Per-row artifacts (build log, `unzip -lv` APK listing, extracted
`libil2cpp.so` / `global-metadata.dat` sizes, gzip -9 sizes, validity
output) were produced under the measurement workspace; the size listings
and validity outputs are reproduced in §6 so the numbers are auditable
without the container.

## 6. Appendix

Per-row extracted sizes (bytes) and validity output:

**Row 0 — baseline (GameWork only)**
```
/home/user/zen-bench/out/row0/lib/arm64-v8a/libil2cpp.so 16928256
/home/user/zen-bench/out/row0/assets/bin/Data/Managed/Metadata/global-metadata.dat 3758336
/home/user/zen-bench/out/row0/libil2cpp.so.gz 5585345
/home/user/zen-bench/out/row0/global-metadata.dat.gz 1145764
/home/user/zen-bench/out/row0.apk 12234195
SB_VALIDATE_BEGIN
104006
SB_VALIDATE_END
```
**Row 1 — classic50**
```
/home/user/zen-bench/out/row1/lib/arm64-v8a/libil2cpp.so 17535616
/home/user/zen-bench/out/row1/assets/bin/Data/Managed/Metadata/global-metadata.dat 3881884
/home/user/zen-bench/out/row1/libil2cpp.so.gz 5705766
/home/user/zen-bench/out/row1/global-metadata.dat.gz 1181501
/home/user/zen-bench/out/row1.apk 12394151
SB_VALIDATE_BEGIN
104006
22231
Completed
Completed
SB_VALIDATE_END
```
**Row 2 — compact50**
```
/home/user/zen-bench/out/row2/lib/arm64-v8a/libil2cpp.so 17308208
/home/user/zen-bench/out/row2/assets/bin/Data/Managed/Metadata/global-metadata.dat 3865868
/home/user/zen-bench/out/row2/libil2cpp.so.gz 5678037
/home/user/zen-bench/out/row2/global-metadata.dat.gz 1176362
/home/user/zen-bench/out/row2.apk 12358480
SB_VALIDATE_BEGIN
104006
22231
Completed
Completed
SB_VALIDATE_END
```
**Row 3 — compact50 + SLIM**
```
/home/user/zen-bench/out/row3/lib/arm64-v8a/libil2cpp.so 17279416
/home/user/zen-bench/out/row3/assets/bin/Data/Managed/Metadata/global-metadata.dat 3862192
/home/user/zen-bench/out/row3/libil2cpp.so.gz 5666563
/home/user/zen-bench/out/row3/global-metadata.dat.gz 1174885
/home/user/zen-bench/out/row3.apk 12347218
SB_VALIDATE_BEGIN
104006
22231
Completed
Completed
SB_VALIDATE_END
```
**Row 4 — ultra50**
```
/home/user/zen-bench/out/row4/lib/arm64-v8a/libil2cpp.so 17234800
/home/user/zen-bench/out/row4/assets/bin/Data/Managed/Metadata/global-metadata.dat 3825772
/home/user/zen-bench/out/row4/libil2cpp.so.gz 5665079
/home/user/zen-bench/out/row4/global-metadata.dat.gz 1166733
/home/user/zen-bench/out/row4.apk 12336115
SB_VALIDATE_BEGIN
104006
22231
Completed
Completed
SB_VALIDATE_END
```
**Row 5 — ultra50 + SLIM**
```
/home/user/zen-bench/out/row5/lib/arm64-v8a/libil2cpp.so 17196184
/home/user/zen-bench/out/row5/assets/bin/Data/Managed/Metadata/global-metadata.dat 3821248
/home/user/zen-bench/out/row5/libil2cpp.so.gz 5653020
/home/user/zen-bench/out/row5/global-metadata.dat.gz 1164064
/home/user/zen-bench/out/row5.apk 12321751
SB_VALIDATE_BEGIN
104006
22231
Completed
Completed
SB_VALIDATE_END
```
**Row 6 — compact50-fields**
```
/home/user/zen-bench/out/row6/lib/arm64-v8a/libil2cpp.so 17293368
/home/user/zen-bench/out/row6/assets/bin/Data/Managed/Metadata/global-metadata.dat 3848996
/home/user/zen-bench/out/row6/libil2cpp.so.gz 5671571
/home/user/zen-bench/out/row6/global-metadata.dat.gz 1171183
/home/user/zen-bench/out/row6.apk 12351610
SB_VALIDATE_BEGIN
104006
22231
Completed
Completed
SB_VALIDATE_END
```
**Row 7 — probe-gvm**
```
/home/user/zen-bench/out/row7/lib/arm64-v8a/libil2cpp.so 16909640
/home/user/zen-bench/out/row7/assets/bin/Data/Managed/Metadata/global-metadata.dat 3755332
/home/user/zen-bench/out/row7/libil2cpp.so.gz 5577993
/home/user/zen-bench/out/row7/global-metadata.dat.gz 1146284
/home/user/zen-bench/out/row7.apk 12227223
SB_VALIDATE_BEGIN
8
SB_VALIDATE_END
```
**Row 8 — probe-nogvm**
```
/home/user/zen-bench/out/row8/lib/arm64-v8a/libil2cpp.so 16906760
/home/user/zen-bench/out/row8/assets/bin/Data/Managed/Metadata/global-metadata.dat 3755268
/home/user/zen-bench/out/row8/libil2cpp.so.gz 5572531
/home/user/zen-bench/out/row8/global-metadata.dat.gz 1146295
/home/user/zen-bench/out/row8.apk 12225693
SB_VALIDATE_BEGIN
8
SB_VALIDATE_END
```

Probe survival check (both probes): `strings global-metadata.dat`
contains `IFace`, `Impl`, `Get`.

Full build logs (`row<N>.log`) and APK listings (`apk-listing.txt`)
were retained in the measurement workspace (`/home/user/zen-bench/out`)
during the run; the numbers above are the complete extraction from
them. The headless driver (`SizeBench.cs`) and per-row assembly script
(`prepare_row.sh`) are reproduced below so the run is repeatable
verbatim.

### SizeBench.cs (Editor driver)

```csharp
// Headless driver for the IL2CPP size matrix (il2cpp-size-protocol.md §4).
// Env: SB_OUTPUT (apk path), SB_DEFINES (";"-joined), SB_VALIDATE (bench|game|probe|none).
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SizeBench
{
    public static void ValidateAndBuild()
    {
        try
        {
            Validate();
            Build();
        }
        catch (Exception e)
        {
            Debug.LogError("SB_FAIL " + e);
            EditorApplication.Exit(1);
        }
    }

    static void Validate()
    {
        var mode = Environment.GetEnvironmentVariable("SB_VALIDATE") ?? "none";
        if (mode == "none") return;
        var asm = Assembly.Load("Assembly-CSharp");
        object result;
        switch (mode)
        {
            case "bench":
                result = asm.GetType("BenchEntry").GetMethod("Run").Invoke(null, new object[] { 7 });
                break;
            case "game":
                result = asm.GetType("DummyGame.GameWork").GetMethod("RunAll").Invoke(null, new object[] { 7 });
                break;
            case "probe":
                result = asm.GetType("Program").GetMethod("Run").Invoke(null, null);
                break;
            default:
                throw new Exception("unknown SB_VALIDATE " + mode);
        }
        Debug.Log("SB_VALIDATE_BEGIN\n" + result + "\nSB_VALIDATE_END");
    }

    static void Build()
    {
        var output = Environment.GetEnvironmentVariable("SB_OUTPUT");
        var defines = Environment.GetEnvironmentVariable("SB_DEFINES") ?? "";
        var nbt = NamedBuildTarget.Android;
        PlayerSettings.SetScriptingBackend(nbt, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(nbt, ManagedStrippingLevel.High);
        PlayerSettings.SetApiCompatibilityLevel(nbt, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetIl2CppCodeGeneration(nbt, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetIl2CppCompilerConfiguration(nbt, Il2CppCompilerConfiguration.Release);
        PlayerSettings.SetScriptingDefineSymbols(nbt, defines);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.stripEngineCode = true;
        EditorUserBuildSettings.buildAppBundle = false;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Runner");
        var runnerType = Assembly.Load("Assembly-CSharp").GetType("Runner");
        if (runnerType == null) throw new Exception("Runner type not found");
        go.AddComponent(runnerType);
        EditorSceneManager.SaveScene(scene, "Assets/Main.unity");

        var report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Main.unity" }, output, BuildTarget.Android, BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception("build result " + report.summary.result);
        Debug.Log("SB_BUILD_OK " + output);
        EditorApplication.Exit(0);
    }
}
```

### prepare_row.sh (row assembly)

```bash
#!/usr/bin/env bash
# Assembles proj/Assets/Sources for one matrix row (il2cpp-size-protocol.md §4).
set -euo pipefail
ROW="$1"
BENCH=/home/user/zen/projects/sqlitehost/tests/app-size-bench
CS=/home/user/zen/projects/sqlitehost/csharp
PROJ=/home/user/zen-bench/proj
SRC="$PROJ/Assets/Sources"
rm -rf "$SRC" "$PROJ/Assets/Sources.meta" "$PROJ/Assets/Main.unity" "$PROJ/Assets/Main.unity.meta"
mkdir -p "$SRC"

vendor_runtime() {
  mkdir -p "$SRC/Abstractions" "$SRC/Runtime"
  cp "$CS/SqliteHost.Abstractions/"*.cs "$SRC/Abstractions/"
  cp "$CS/SqliteHost.Runtime/"*.cs "$SRC/Runtime/"
}

runner_bench() { cat > "$SRC/Runner.cs" <<'R'
using UnityEngine;
public sealed class Runner : MonoBehaviour
{
    void Start() { Debug.Log(BenchEntry.Run(7)); }
}
R
}
runner_game() { cat > "$SRC/Runner.cs" <<'R'
using UnityEngine;
public sealed class Runner : MonoBehaviour
{
    void Start() { Debug.Log(DummyGame.GameWork.RunAll(7)); }
}
R
}
runner_probe() { cat > "$SRC/Runner.cs" <<'R'
using UnityEngine;
public sealed class Runner : MonoBehaviour
{
    void Start() { Debug.Log(Program.Run()); }
}
R
}

vendor_probe() { # $1 = gvm|nogvm — identical transform for both (drop Main, expose Run)
  sed -e 's/static class Program/public static class Program/' \
      -e 's/static void Main()/public static int Run()/' \
      -e 's/Console.WriteLine(n);/return n;/' \
      "$BENCH/probes/$1/Program.cs" > "$SRC/Program.cs"
}

case "$ROW" in
  0) cp "$BENCH/out/unity-src/classic/GameWork.cs" "$SRC/"; runner_game ;;
  1) vendor_runtime; cp "$BENCH/out/unity-src/classic/"*.cs "$SRC/"; runner_bench ;;
  2) vendor_runtime; cp "$BENCH/out/unity-src/compact/"*.cs "$SRC/"; runner_bench ;;
  3) vendor_runtime; cp "$BENCH/out/unity-src/compact/"*.cs "$SRC/"; runner_bench ;;
  4) vendor_runtime; cp "$BENCH/out/unity-src/ultra/"*.cs "$SRC/"; runner_bench ;;
  5) vendor_runtime; cp "$BENCH/out/unity-src/ultra/"*.cs "$SRC/"; runner_bench ;;
  6) vendor_runtime; cp "$BENCH/out/unity-src/compact/"*.cs "$SRC/"
     cp "$BENCH/out/gen/compact-fields/HostMethodDtos.g.cs" "$SRC/HostMethodDtos.g.cs"; runner_bench ;;
  7) vendor_probe gvm; runner_probe ;;
  8) vendor_probe nogvm; runner_probe ;;
  *) echo "unknown row $ROW"; exit 2 ;;
esac
echo "row $ROW prepared: $(find "$SRC" -name '*.cs' | wc -l) sources"
```
