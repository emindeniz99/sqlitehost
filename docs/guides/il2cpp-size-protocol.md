# IL2CPP app-size measurement protocol

Every app-size number in `docs/compatibility.md` was measured with .NET 8
**NativeAOT as a proxy** for Unity IL2CPP (both are AOT + whole-program
trim). The mechanisms transfer; the exact bytes do not — and for two
findings the *magnitude* could genuinely differ, because IL2CPP ships
engine infrastructure unconditionally that NativeAOT only pays for on
demand. This document is a complete, self-contained protocol so that
**anyone with a Unity editor can re-run every hypothesis under real
IL2CPP and produce the definitive report** without asking anything.

Everything to build lives in the repo: `tests/app-size-bench/` generates
identical source sets for both toolchains from the repo's own emitters.

CI runs both halves, at cadences their cost justifies. The NativeAOT
sweep (§3) is a per-pull-request job — `ci.yml`, `app size (NativeAOT)`,
driven by `tests/app-size-bench/measure-nativeaot.mjs`. The Unity matrix
(§4) is `il2cpp-size-bench.yml`, monthly and on demand, driven by
`tests/app-size-bench/unity-project/` plus `prepare-row.sh`,
`measure-il2cpp.mjs` and `summarize-il2cpp.mjs`. It stays a measurement:
IL2CPP byte counts move with the editor patch, the NDK and the engine, so
it publishes a table and an artifact instead of blocking a merge. The
hand-run instructions below still work unchanged, and are what you want
when you are investigating rather than monitoring.

The iOS matrix is a third workflow, `ios-size-bench.yml`, monthly and on
demand, driven by the same rows and the same `SizeBench` entry point plus
`measure-ios.mjs`. It has never run and no iOS number exists yet. It is
written to measure a different unit on a different toolchain, and its
numbers will not be an extension of the Android table. §7 says what it is
built to measure and what may be compared with what.

---

## 1. Hypothesis ledger — what was tested, what must be re-tested

Legend: **NativeAOT result** = measured on linux-x64, .NET 8, from the
committed kit (§3 has the exact reference numbers). **IL2CPP status** =
what the Unity run must establish.

| # | Hypothesis | NativeAOT result | Why IL2CPP may differ | IL2CPP test |
|---|---|---|---|---|
| H-GVM | A generic virtual method (generic method on an interface) drags in the AOT dynamic type loader | **CONFIRMED, huge**: one GVM in a minimal probe costs +283 KB raw / +127 KB gz; in the real runtime, GVM + generic core types together cost ~250 KB (super-additive; removing both collapsed S_P_TypeLoader 2898 → 296 symbols) | IL2CPP ships its generic-sharing + metadata machinery (`global-metadata.dat`, runtime metadata init) in **every** build, so the *marginal* cost of one GVM may be far smaller — possibly near zero. The architectural change (`QueryRows`) can't hurt (strictly ≤), but its measured win may be NativeAOT-specific | **MUST re-test** — build `probes/gvm` vs `probes/nogvm` as two minimal Unity IL2CPP builds; the pair's size delta IS the answer |
| H-PROFILES | Per-method cost is unique-type count → compact/ultra profiles collapse it (from this kit: classic ≈3.5 KB raw / 1.2 KB gz per method → compact ≈1.8 / 0.6 → ultra ≈0.7 / 0.25) | **CONFIRMED** (that is why the profiles exist). Note the crossover: ultra's value-bag machinery is a *fixed* cost, so ultra beats compact only above roughly a dozen methods | Same mechanism exists (IL2CPP materializes per-instantiation metadata + generated C++), but the per-type unit cost differs | **MUST re-test** — the matrix (§4) has each profile at BOTH 5 and 50 methods; marginal per-method = (Δ₅₀ − Δ₅) / 45 |
| H-FIELDS | DTO auto-properties cost more than public fields | **REFUTED — exactly 0 bytes** (trivial accessors fully inlined; binaries byte-identical modulo build IDs) | IL2CPP generates C++ per method; its inliner (and the C++ compiler behind it) *usually* erases trivial accessors too, but that is an assumption, not a measurement | **MUST re-test** — `compact50-fields` vs `compact50` (kit generates both) |
| H-DATA | Data-driving the 50 registration bodies (delegate tables + one loop) shrinks the binary | **REFUTED — it GREW** (+4.2 KB raw / +5.6 KB gz): in NativeAOT a delegate-array initializer is *code* (~92 B/element), and the 50 near-identical fluent bodies were nearly free under gzip's 32 KB window | IL2CPP compiles the array initializers to C++ the same way; expected to reproduce, but cheap to spot-check | Optional — low priority, expected same sign |
| H-DISPATCH | Collapsing the handler interface to one `Invoke(int ordinal, object input)` slot | **CONFIRMED but unshipped**: −16.9 KB raw / −4.8 KB gz; rejected because it changes handler-authoring DX (switch instead of named methods) | Should transfer (fewer interface slots + thunks in any AOT) | Optional — measure only if the DX trade ever becomes tempting |
| H-SLIM | `SQLITEHOST_SLIM` strips optional strict checks | **CONFIRMED**: −28 KB raw / −12 KB gz on compact50 | Pure C# dead-code removal — transfers directly; Unity needs the define symbol set (§4) | Re-test cheaply as part of the matrix (one extra build) |
| H-STRINGS | Generated SQL/string literals are a major cost | **REFUTED**: all SQL-ish literals < 0.5 KB; unreferenced DDL constant strips | Same stripping semantics | No re-test needed |
| H-NANO | Whole-app trim flags (`StackTraceSupport=false` etc.) shrink SqliteHost's share | **Mostly app-side**: −57 KB gz for the game's own code, only −3 KB for SqliteHost's delta | Unity's equivalents are different knobs: Managed Stripping Level, IL2CPP Code Generation = "Faster (smaller) builds", Strip Engine Code | Re-test the *Unity knobs* instead: measure the matrix at Managed Stripping **High** and note the equivalents |
| H-ENGINE | Consuming system libsqlite3 via `SqliteHost.Adapters.Native` adds ~0 engine bytes | Structurally true (DllImport binds at load) | iOS ships `libsqlite3.dylib`; Android ships `libsqlite.so` but **its use by apps is restricted on modern API levels** — the vendored-amalgamation path must be sized too | Optional — measure APK/IPA delta of a vendored sqlite3 amalgamation `.so`/static lib for the Android fallback story |

**The two questions that matter most** (they decide whether the doc's
guidance stays as-is or gets IL2CPP-specific footnotes):

1. **H-GVM under IL2CPP** — if the probe pair's delta is small, the
   NativeAOT-derived "~250 KB structural win" narrative gets an IL2CPP
   footnote (the `QueryRows` contract stays: it is strictly better or
   equal everywhere, and NativeAOT/.NET-server consumers keep the win).
2. **Absolute per-profile deltas under IL2CPP** — the real numbers for
   the compatibility table's IL2CPP column.

## 2. What to build with

- **Unity versions**: 2021.3.55f1 and 2022.3.39f1 (the repo's pinned
  LTS targets — `docs/compatibility.md`). If only one is available,
  prefer 2022.3.39f1 and say so in the report.
- **Scripting backend**: IL2CPP. **Api Compatibility Level**:
  .NET Standard 2.0/2.1 (either; note which). **Managed Stripping
  Level**: run the matrix at **High** (record Low as a secondary column
  if time permits). **IL2CPP Code Generation**: "Faster (smaller)
  builds" where available; record the setting used.
- **Platform**: Android (release, IL2CPP, ARM64 only) is the primary
  target — its build sizes are easy to measure headlessly. iOS has a leg
  of its own, unrun so far, and it is a separate exercise with its own
  unit, its own pinned toolchain and its own comparability rules; see §7
  before putting an iOS number anywhere near an Android one.
- **Measure**, per build: (a) the stripped `libil2cpp.so` size (ARM64)
  from the APK/AAB, (b) `global-metadata.dat` size, (c) the compressed
  download proxy: `gzip -9` of those two files (or the APK size delta).
  Report all three; deltas between builds are the results, absolute
  sizes are context.

## 3. NativeAOT reference numbers (from this exact kit)

Regenerate sources with:

```bash
pnpm install && pnpm -r build   # emitters, from the repo root
cd tests/app-size-bench && node generate.mjs
```

Reference numbers measured from these generated sources on
.NET 8 NativeAOT linux-x64 (fill of record — the Unity report's table
mirrors this):

| Build | raw bytes | gzip -9 | Δraw vs baseline | Δgz vs baseline |
|---|---|---|---|---|
| gamebase (baseline) | 1,544,920 | 734,871 | — | — |
| classic50 | 1,848,904 | 857,523 | 303,984 (297 KB) | 122,652 (120 KB) |
| compact50 | 1,761,352 | 823,698 | 216,432 (211 KB) | 88,827 (87 KB) |
| compact50-fields | 1,761,360 | 823,738 | +8 B vs compact50 | +40 B vs compact50 |
| compact50 + SLIM | 1,736,040 | 811,987 | 191,120 (187 KB) | 77,116 (75 KB) |
| ultra50 | 1,729,200 | 817,711 | 184,280 (180 KB) | 82,840 (81 KB) |
| ultra50 + SLIM | 1,699,728 | 802,718 | 154,808 (151 KB) | 67,847 (66 KB) |
| classic5 | 1,691,864 | 802,741 | 146,944 (144 KB) | 67,870 (66 KB) |
| compact5 | 1,678,984 | 797,172 | 134,064 (131 KB) | 62,301 (61 KB) |
| ultra5 | 1,695,984 | 806,687 | 151,064 (148 KB) | 71,816 (70 KB) |
| probe-gvm | 1,714,512 | 798,256 | — | — |
| probe-nogvm | 1,424,576 | 667,021 | — | — |
| **probe delta (one GVM)** | | | **289,936 (283 KB)** | **131,235 (128 KB)** |

(compact50-fields differing from compact50 by 8 raw / 40 gz bytes is
build-ID noise — that IS the H-FIELDS zero-effect result under
NativeAOT.)

Marginal per-method cost, derived as (Δ₅₀ − Δ₅) / 45 from the rows
above: classic 3,490 B raw / 1,217 B gz; compact 1,830 / 589; ultra
738 / 245. The 5-method builds print DDL length **2771** instead of
22231 — that is their expected second output line.

The bench prints four lines (`104006`, DDL length, `Completed`,
`Completed`); any build whose output differs is invalid — fix before
measuring.

## 4. The build matrix (Unity side)

For each row: new empty 3D URP-less project, vendor
`csharp/SqliteHost.Abstractions/` + `csharp/SqliteHost.Runtime/`
sources (skip `bin`/`obj`; no `.csproj` — Unity compiles loose sources)
plus the row's `out/unity-src/<profile>/` folder, add one MonoBehaviour
that calls `BenchEntry.Run(7)` on `Start()` and logs the result, build
Android release, record sizes. The **baseline row** is the same project
with only `GameWork.cs` + a MonoBehaviour logging
`DummyGame.GameWork.RunAll(7)` (no SqliteHost sources at all).

| Row | Sources | Extra setting |
|---|---|---|
| 0 baseline | GameWork only | — |
| 1 classic50 | unity-src/classic | — |
| 2 compact50 | unity-src/compact | — |
| 3 compact50-slim | unity-src/compact | Scripting Define Symbols += `SQLITEHOST_SLIM` |
| 4 ultra50 | unity-src/ultra | — |
| 5 ultra50-slim | unity-src/ultra | Scripting Define Symbols += `SQLITEHOST_SLIM` |
| 6 compact50-fields | unity-src/compact with `HostMethodDtos.g.cs` fields variant (copy from `out/gen/compact-fields/`) | — |
| 7 probe-gvm | ONLY `probes/gvm/Program.cs` body (drop `Main`, call from a MonoBehaviour) | no SqliteHost sources |
| 8 probe-nogvm | ONLY `probes/nogvm/Program.cs` body (same wrapper) | no SqliteHost sources |
| 9 classic5 | unity-src/classic-5 | — |
| 10 compact5 | unity-src/compact-5 | — |
| 11 ultra5 | unity-src/ultra-5 | — |

Rows 9–11 exist to separate fixed cost from per-method cost:
**marginal per-method = (Δ₅₀ − Δ₅) / 45** per profile — without them
only the total delta is knowable. Report both the fixed intercept and
the per-method slope for each profile.

Validity checks: rows 1–6 must log the four expected lines (second
line `22231`); rows 9–11 likewise with second line `2771`; rows 7–8
must log the SAME value as each other (any consistent number). Keep
every Unity setting identical across rows except the one the row
varies; build twice if unsure a setting leaked.

Note for rows 7–8: IL2CPP may deprioritize dead branches differently —
confirm with an IL2CPP build report or `libil2cpp.so` symbol dump that
`Get<T>`/`Get` actually survived into the binary.

## 5. The report (deliverable)

Produce `docs/reports/il2cpp-size-report.md` (new folder is fine) with:

1. **Environment**: Unity version(s), platform, stripping level, IL2CPP
   codegen option, date.
2. **The matrix table**: per row — `libil2cpp.so` bytes,
   `global-metadata.dat` bytes, gzip of both, delta vs row 0.
3. **Per-hypothesis verdicts**: for each MUST row in §1 — CONFIRMED /
   REFUTED / DIFFERENT MAGNITUDE under IL2CPP, with numbers.
   Explicitly answer: (a) what does one GVM cost under IL2CPP?
   (b) fields vs auto-properties: zero or not? (c) per-method cost per
   profile?
4. **Doc patches**: a short list of concrete edits the results demand
   in `docs/compatibility.md` (App size section) — e.g. an IL2CPP
   column for the table, a footnote on H-GVM magnitude. Apply them in
   the same PR/branch if you have write access; otherwise include the
   diff in the report.
5. **Raw artifacts**: attach or link build logs and the size listings
   (`unzip -lv` of the APK) so numbers are auditable.

## 6. Hand-off brief (for whoever runs it on a Unity machine)

> Read `docs/guides/il2cpp-size-protocol.md` in this repository and
> execute it fully: regenerate the bench sources (`tests/app-size-bench/
> generate.mjs`), build the 12-row Unity IL2CPP matrix in §4 on
> Android/ARM64 with Unity 2022.3.39f1 (and 2021.3.55f1 if available),
> measure per §2, and write the report described in §5 to
> `docs/reports/il2cpp-size-report.md`, including
> the doc patches for `docs/compatibility.md`. The hypothesis ledger in
> §1 tells you exactly which NativeAOT findings you are re-testing and
> why they might not reproduce under IL2CPP — H-GVM and H-FIELDS are
> the two that can genuinely flip; treat their verdicts as the headline
> of your report. Every build must pass the §4 validity checks before
> its numbers count.

## 7. The iOS half

**No row of this matrix has been built.** The workflow, the `-sbTarget ios`
branch of `SizeBench.cs` that drives it and `measure-ios.mjs` are all new,
and none of them has run, so everything below describes what the code is
written to do rather than what was seen. The exceptions are the numbers
explicitly marked *measured*: those come from a trivial hand-written Xcode
app and from inspecting binaries already on a Mac, never from a Unity
build. "What is not settled yet" at the end lists the questions only a run
can answer.

### Why it is two stages and not one

Unity's iOS build emits an Xcode project; Xcode compiles and links it.
That is how Unity builds for iOS on every host, not a limitation of CI, so
the bench is a handoff by construction. `ios-size-bench.yml` runs the
editor in its `xcodeproj-linux` and `xcodeproj-macos` jobs and `xcodebuild`
in its `compile` job, which
is always a macOS runner.

The editor half runs on two hosts on purpose — `ubuntu-latest` with
GameCI's digest-pinned iOS editor container, and a standard macOS runner
where the same action installs the editor onto the machine. The `compile`
job's steps are identical for both, which is what would make a byte of
difference between the two attributable to the Unity host and nothing
else — an inference that holds only as far as the compile stage is
deterministic, and nothing has measured that yet. The summary prints that
delta row by row. One of the two hosts gets deleted once the comparison
has run; until then both are built, and a row JSON records which host
produced it in `unityHost`.

The validity gate is unchanged. `SizeBench.ValidateAndBuild` loads
`Assembly-CSharp` and invokes the row's entry point by reflection **inside
the editor**, before the build, and writes the result beside the output.
The iOS leg is written to hit that same gate — `Validate()` runs before
either platform's `Build()`, ahead of the `-sbTarget` branch — so a row that
prints the wrong value should fail before its bytes count. Written, not
observed: no editor has compiled the iOS branch, so the gate's behaviour
here is inherited from the Android leg rather than seen on this one.
Neither leg has ever proven that the code survives IL2CPP stripping in a
running player — that is a different claim and no platform here backs it.

### The unit

Per row — the first three weighed both raw and `gzip -9`, the rest recorded
as they are:

| Field | What it is |
|---|---|
| `unityFramework` | `UnityFramework.framework/UnityFramework` inside the built `.app` — the stripped, unsigned, single-slice arm64 Mach-O |
| `globalMetadata` | `global-metadata.dat` inside the same `.app` |
| `total` | the two above, summed, so the delta arithmetic is the same code as Android's |
| `il2cppOnly` | link-map bytes attributed to the archives the script treats as IL2CPP's — all three of `libGameAssembly.a`, `il2cpp.a` and `libil2cpp.a` (the last is the older layout's name for the runtime) — generated code plus the IL2CPP runtime, engine excluded |
| `machO` | what the weighed file's own load commands say: its architecture, whether an `LC_CODE_SIGNATURE` is attached, the `LC_SYMTAB`/`LC_DYSYMTAB` symbol counts, and per segment its page-rounded `vmSize`, its `fileOffset` and on-disk `fileSize`, and the byte-granular sizes of the sections inside it, plus `vmTotal` and `fileTotal` summed over the segments |
| `env` | Unity version, Unity host, Xcode version and build, iOS SDK, runner label |

Every path is found by searching the built product, and the measurement
fails if the search does not resolve to exactly one file. Unity's manual
describes the 2022.3 layout — three targets, `libGameAssembly.a` and
`il2cpp.a` linked into `UnityFramework` — but no build of it has been
opened in this repository, and a hardcoded path that stops matching is how
a bench silently measures nothing.

"Stripped, unsigned, single-slice arm64" is three `xcodebuild` arguments
away from being false of any given build, and none of the three fails
visibly when it stops applying, so `measure-ios.mjs` reads all three back
off the file it weighed — `lipo -archs` for the slice, `LC_CODE_SIGNATURE`
for the signature, `LC_DYSYMTAB nlocalsym` for the strip — and refuses to
write a row that is not the unit. That check is written, not exercised: no
Unity-built framework has been through it.

`il2cppOnly` is optional in a way the other fields are not: it exists only
if the link map parses and names archives the script recognises. When it
does not, the field is `null` and the row JSON carries the reason. It is
never estimated from anything else.

### The pinned xcodebuild settings, and why each one is pinned

`xcodebuild` defaults are not what a size measurement wants, so every
size-relevant setting is on the command line rather than inherited:

| Setting | Why |
|---|---|
| `-configuration Release -sdk iphoneos -destination 'generic/platform=iOS'` | A device build of the shipping configuration. A simulator slice is different code against a different SDK. |
| `ARCHS=arm64 ONLY_ACTIVE_ARCH=NO` | One slice, the one that ships. A fat binary would double the number and hide the change. |
| `DEPLOYMENT_POSTPROCESSING=YES STRIP_INSTALLED_PRODUCT=YES` (and deliberately NOT `STRIP_STYLE`) | **`xcodebuild build` does not strip by default.** `STRIP_INSTALLED_PRODUCT` is already `YES` and inert, because `DEPLOYMENT_POSTPROCESSING` is `NO` for the build action. Measured on Xcode 26.6, stripping is worth 22.8% of a trivial app, and should be worth more under IL2CPP, which emits one named C function per managed method — the exact quantity rows 9-11 exist to measure. Unstripped, the per-method slope would be inflated by symbol-name bytes that never ship. `STRIP_STYLE` is left to each target's own default: forcing `all` project-wide reaches UnityFramework, which is a dylib, and `strip` refuses it — measured, run 33207852029 failed on both hosts after a clean compile and link. |
| `ENABLE_CODE_COVERAGE=NO CLANG_COVERAGE_MAPPING=NO` | The build action silently enables coverage instrumentation and the archive action does not; on a trivial app that alone was ~28 KB of `libclang_rt.profile_ios` members plus an `__LLVM_COV` segment. |
| `CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY="" CODE_SIGN_ENTITLEMENTS=""` | CI has no Apple account, and unsigned is also the better measurement: signing appends a reserved region (measured: +19,720 B for 1,699 B of real signature) whose padding is lumpy across rows. |
| `DEAD_CODE_STRIPPING=YES ENABLE_BITCODE=NO` | Both are the current defaults; pinned so a toolchain default cannot move under a month-over-month comparison. Bitcode is dead in current Xcode and was measured to change nothing. |
| `LD_GENERATE_MAP_FILE=YES` | The link map is what makes `il2cppOnly` possible. `LD_MAP_FILE_PATH` is deliberately **not** set: on a multi-target project it makes two targets write one file and the build fails with `Multiple commands produce`. |

Xcode itself is an input to the measurement, exactly like the editor patch.
The workflow pins `DEVELOPER_DIR` to a specific Xcode and stamps the
version, the build number and the iOS SDK into every row JSON. When GitHub
retires that Xcode the pin fails loudly, which is the point — a silent roll
would change the compiler under a size measurement without failing
anything. Treat an Xcode bump as a re-baseline, not as something to compare
across.

### Comparability

**iOS absolute bytes are not comparable to Android absolute bytes.** Three
independent reasons, any one of which is sufficient:

1. The BCL profile differs — Android links `unityaot-linux`, iOS
   `unityaot-macos`.
2. The compiler differs — Android NDK clang against Apple clang.
3. The scope differs structurally. On iOS the generated code is linked
   **into `UnityFramework` together with the whole Unity engine**. On
   Android, `libil2cpp.so` excludes the engine, which lives in
   `libunity.so` — a file this bench has never measured. A row that drags
   in engine code shows up in the iOS number and is invisible in the
   Android one.

What is valid:

- **iOS row-minus-baseline deltas, compared against each other.** That is
  what the iOS leg is *intended* to produce. It becomes a result once the
  build-row-0-twice reproducibility check below has passed: until two
  identical builds are known to agree, a delta cannot be told from noise.
- **The shape claims.** H-PROFILES (per-method slope falling classic →
  compact → ultra), H-SLIM (a net win) and H-GVM (the cost of one generic
  virtual method) are stated as ratios and orderings rather than as byte
  counts, which is what makes them the kind of claim a second toolchain
  can re-test at all. Whether any of them survives that re-test is the
  hypothesis this leg exists to measure, not a property it may assume.
  H-FIELDS is deliberately not on that list: it predicts a zero, and the
  caveat below says a zero cannot be resolved from a file size at all. It
  is blocked on the sub-file-size numbers — the section sizes inside
  `machO`, and `il2cppOnly`, which is optional and may be `null`.
- **`il2cppOnly` as the same *kind* of quantity as Android's
  `libil2cpp.so`** — generated code plus IL2CPP runtime, engine excluded.
  Same kind, not the same number: different compiler, different ABI,
  different BCL, and a different way of counting. `il2cppOnly` is a sum of
  the extents of the symbols the linker kept, read out of the map `ld`
  writes at link time, before the strip step; Android's number is a file
  size. So it excludes the alignment padding, the Mach-O headers and the
  `__LINKEDIT` data that a file size includes, and no scaling reconciles
  the two.

One measurement caveat that is specific to Mach-O: segments are 16 KB
aligned, so an iOS file size is quantized in a way the Android `.so` is not
at the same granularity. A sub-page difference can vanish or jump a whole
page. That is why `machO` and `il2cppOnly` are recorded rather than being
nice-to-haves — but be exact about which of those numbers escapes the
quantization, because most of them do not. Of what the load commands
report, only `machO.segments[].sections[].size` is a byte count of real
content. `segments[].vmSize` is a virtual-memory size rounded up to a page
and `machO.vmTotal` sums those; `segments[].fileSize` is the segment's
on-disk extent, which is page-aligned as well for every segment but the
last, and `machO.fileTotal` sums to the file size itself. (Measured here on
a single-slice arm64 dylib from the Xcode 26.6 toolchain: `__TEXT`,
`__DATA_CONST` and `__DATA` at `filesize` 16,384 each against `__LINKEDIT`
at 24,608, the four summing to the file size exactly, and section sizes
down to 8 bytes.) So a zero-bytes hypothesis like H-FIELDS has to be
resolved from the section sizes or from `il2cppOnly`, never from a file
size, a segment size or a segment total.

### What is not settled yet

Nothing in this section has been produced by a run. Specifically unknown
until the first one: whether an Xcode project generated on Linux builds
unmodified on macOS (GameCI ships `dockerWorkspacePath` because absolute
paths can leak into `project.pbxproj`, and that input is the documented
lever if it bites — not a `sed` over the pbxproj); where 2022.3 puts the
generated C++ and whether the project carries any host-specific executable
an Xcode build phase invokes; whether `Unity-iPhone` is a shared scheme;
whether the IL2CPP archives (`libGameAssembly.a`, `il2cpp.a`, or the older
`libil2cpp.a`) separate cleanly in the link map; per-row wall clock and
peak disk on both stages; and whether iOS row deltas reproduce run to run
at all. Build row 0 twice on the first full run
and diff — if the two disagree, every delta below the quantum is noise and
this section needs a stated resolution floor.

---

Maintenance note: if the runtime or emitters change materially, re-run
`generate.mjs` and refresh the §3 reference table before handing this
off — the kit regenerates everything from the current sources, so the
two toolchains always measure the same code.
