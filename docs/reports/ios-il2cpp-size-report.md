# IL2CPP app-size report (Unity, iOS/ARM64)

The iOS half of `docs/guides/il2cpp-size-protocol.md` §7. Same twelve rows,
same sources and same editor as the Android report — a different platform
and a different measured unit. Read that file for the protocol; this one
carries only what the iOS run produced.

## 1. Environment

Recorded by `tests/app-size-bench/measure-ios.mjs` into every row JSON and
identical across all 48 legs:

| | |
|---|---|
| Unity | 2022.3.62f3 |
| Xcode | 26.6 (17F113) |
| iOS SDK | 26.5 |
| Runner | `macos-26`, image 20260728.0273.1, ARM64 |
| Unity editor host | `ubuntu-latest` in `unityci/editor` (digest-pinned) **and** `macos-26` |
| Workflow | `.github/workflows/ios-size-bench.yml`, run 33255105207 |
| Result | 48 of 48 legs green |

Every row's binary was verified before it was weighed: `arch: arm64`, no
code signature, and zero named local symbols (a correctly `-x`-stripped
dylib reports `nlocalsym 1` — Apple's `radr://5614542` placeholder — so
the check counts named locals via `nm`, not `nlocalsym`).

## 2. The unit, and why it is not the Android unit

On Android the IL2CPP output is a separate `libil2cpp.so` and the engine is
not in it. On iOS there is no such file: IL2CPP's C++ links into
`UnityFramework` together with the engine. So Δ here is
**`UnityFramework` + `global-metadata.dat`** over the baseline row, and the
absolute figures are dominated by engine bytes that no row changes.

`il2cppOnly` is the second unit in every table below. It is the sum of the
link map's extents for the IL2CPP archives only (`libGameAssembly.a` +
`libil2cpp.a`), so it excludes the engine and is byte-granular —
`UnityFramework` moves in 16,384 B steps because Mach-O quantizes to the
16 KB page. When the two disagree about a small delta, `il2cppOnly` is the
one carrying signal.

**Do not compare any number here to the Android report.** Different BCL
profile (`unityaot-macos` vs `unityaot-linux`), different compiler, and the
engine is inside the iOS unit and outside the Android one.

## 3. Matrix results

Twelve rows, Unity editor on Linux (the macOS-host figures differ only as
described in §5):

| row | label | sources | `UnityFramework` | `global-metadata.dat` | total raw | Δ raw | total gz | Δ gz | `il2cppOnly` | Δ `il2cppOnly` |
|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | `baseline` | GameWork only | 10,802,416 | 1,012,068 | 11,814,484 | — | 4,935,647 | — | 3,428,123 | — |
| 1 | `classic50` | unity-src/classic | 11,198,296 | 1,143,460 | 12,341,756 | +527,272 | 5,077,965 | +142,318 | 3,811,045 | +382,922 |
| 2 | `compact50` | unity-src/compact | 11,115,360 | 1,126,784 | 12,242,144 | +427,660 | 5,051,467 | +115,820 | 3,717,898 | +289,775 |
| 3 | `compact50-slim` | unity-src/compact | 11,066,136 | 1,121,004 | 12,187,140 | +372,656 | 5,033,718 | +98,071 | 3,675,603 | +247,480 |
| 4 | `ultra50` | unity-src/ultra | 11,082,136 | 1,085,208 | 12,167,344 | +352,860 | 5,039,292 | +103,645 | 3,686,469 | +258,346 |
| 5 | `ultra50-slim` | unity-src/ultra | 11,016,504 | 1,078,548 | 12,095,052 | +280,568 | 5,016,578 | +80,931 | 3,636,483 | +208,360 |
| 6 | `compact50-fields` | unity-src/compact + compact-fields DTOs | 11,098,776 | 1,109,104 | 12,207,880 | +393,396 | 5,046,243 | +110,596 | 3,713,898 | +285,775 |
| 7 | `probe-gvm` | probes/gvm | 10,769,496 | 1,005,140 | 11,774,636 | -39,848 | 4,917,939 | -17,708 | 3,397,202 | -30,921 |
| 8 | `probe-nogvm` | probes/nogvm | 10,769,472 | 1,005,060 | 11,774,532 | -39,952 | 4,917,533 | -18,114 | 3,396,017 | -32,106 |
| 9 | `classic5` | unity-src/classic-5 | 11,000,064 | 1,077,920 | 12,077,984 | +263,500 | 5,024,497 | +88,850 | 3,615,944 | +187,821 |
| 10 | `compact5` | unity-src/compact-5 | 10,967,080 | 1,073,060 | 12,040,140 | +225,656 | 5,016,630 | +80,983 | 3,596,115 | +167,992 |
| 11 | `ultra5` | unity-src/ultra-5 | 10,999,936 | 1,074,516 | 12,074,452 | +259,968 | 5,029,294 | +93,647 | 3,621,530 | +193,407 |

## 4. Fixed vs marginal

Rows 1/2/4 are 50-method hosts and rows 9/10/11 are the 5-method hosts of
the same three profiles, so **marginal = (Δ₅₀ − Δ₅) / 45** and
**fixed = Δ₅ − 5·marginal**:

| profile | raw slope B/method | raw fixed | gz slope B/method | gz fixed |
|---|---:|---:|---:|---:|
| classic | 5,862 | 229 KB | 1,188 | 81 KB |
| compact | 4,489 | 198 KB | 774 | 75 KB |
| ultra | 2,064 | 244 KB | 222 | 90 KB |

Two readings, both of which matter for the recommendation in
`docs/compatibility.md`:

- **The profile ordering at 50 methods carries over from Android
  unchanged**: ultra < compact < classic, and `SQLITEHOST_SLIM` is a net
  win on both profiles. The architecture's conclusions hold on iOS.
- **The crossover does not carry over.** Ultra has the largest fixed cost
  and the smallest slope on both platforms, but on iOS the compact↔ultra
  crossover sits at **~19 methods raw (~28 gzipped)** against Android's
  ~14 (~21). Ultra needs a bigger host to pay for itself here. Below ~19
  methods, compact is the smaller choice on iOS — and row 11 shows it
  directly: `ultra5` (+259,968 B) is larger than `compact5` (+225,656 B).

The GVM probes (rows 7/8) land 39 KB *below* the baseline on iOS, as on
Android, and differ from each other by 104 B raw / 1,185 B `il2cppOnly` —
the marginal cost of the first generic virtual method is again
noise-level, and again that is not the finding: the finding is what GVMs
cost once the hot path depends on them, which the profile rows measure.

## 5. The two Unity hosts

Each row's Xcode project was generated twice — once in GameCI's iOS editor
container on `ubuntu-latest`, once on a macOS runner where the same action
installs the editor onto the machine — and both were compiled by one
identical `xcodebuild` step on a macOS runner. The toolchain premise for
reading a difference as host-attributable holds exactly: both hosts
recorded the same Unity, Xcode, SDK and runner image, and every row's
`validity` fields match.

| row | label | Δ total raw | Δ total gz | Δ `il2cppOnly` | Δ `appBytes` |
|---:|---|---:|---:|---:|---:|
| 0 | `baseline` | +0 | +0 | +0 | -393,472 |
| 1 | `classic50` | +0 | +67 | +0 | -393,472 |
| 2 | `compact50` | +0 | +81 | -4 | -393,472 |
| 3 | `compact50-slim` | +0 | +0 | +0 | -393,472 |
| 4 | `ultra50` | +0 | +50 | +4 | -393,472 |
| 5 | `ultra50-slim` | +0 | +0 | +0 | -393,472 |
| 6 | `compact50-fields` | +0 | +228 | -4 | -393,472 |
| 7 | `probe-gvm` | +0 | +0 | +0 | -393,472 |
| 8 | `probe-nogvm` | +0 | +0 | +0 | -393,472 |
| 9 | `classic5` | +8 | +21 | +0 | -393,464 |
| 10 | `compact5` | +0 | +254 | +0 | -393,472 |
| 11 | `ultra5` | +0 | +105 | +0 | -393,472 |


Three separable effects:

1. **A constant 393,472 B (384.25 KiB) of bundle payload** present in the
   Linux-host `.app` and absent from the macOS-host `.app`, on every row
   including the baseline and both probes. `appBytes - total.raw` is fixed
   per host (3,283,430 vs 2,889,958), so it is a fixed file set unrelated
   to the row's sources, and it sits entirely outside the two measured
   files — it cancels in every published delta. Which files they are is
   not yet known; `measure-ios.mjs` now records a per-file `appInventory`,
   so the next scheduled run names them.
2. **±4 B inside `libGameAssembly.a`** on rows 2, 4 and 6 — exactly and
   only where `il2cppOnly` differs, with the sign alternating. An
   alignment-class difference in one archive.
3. **+8 B of `UnityFramework` raw** on row 9, the only row where a
   published raw quantity differs at all.

On eleven of twelve rows every published quantity is byte-identical across
the two hosts; on the twelfth it differs by 8 bytes in 12 MB. The figures
quoted in `docs/compatibility.md` are host-independent at their stated
precision. Reproducibility is not a confound: row 2 built twice on the same
host matched on every recorded field, gz included.

## 6. Raw artifacts

Per-row JSON, one file per row per host, is attached to the workflow run as
`ios-size-matrix` (and `ios-row-<n>-<host>` individually). Each carries the
weighed sizes, the link-map archive breakdown, the Mach-O segment table and
symbol counts, the `validity` fields the editor emitted, and the full
toolchain environment.
