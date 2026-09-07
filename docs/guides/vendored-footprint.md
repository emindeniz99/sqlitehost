# Vendored footprint — what runs, what to audit, what to trim

For teams vendoring the C# sources into a game/Unity project (the
copy-paste path in `docs/packaging.md`). It answers three questions a
reviewer asks about a vendored dependency:

1. **What actually runs on the device** when a backend script executes?
2. **What is the security-review surface** — how many lines, which files?
3. **How do I trim the package** to just the profile I use?

All line counts below are for the vendored runtime sources
(`unity/com.sqlitehost.runtime/Runtime/`, which mirrors
`csharp/SqliteHost.Abstractions/` + `csharp/SqliteHost.Runtime/`). They are
**fixed** — they do not grow with the number of host methods. Per-method
DTOs are *generated separately* into your own generated folder and are not
part of these counts.

## The map (8,527 vendored lines)

| Bucket | Lines | Runs when |
|---|---:|---|
| **Execution engine** | **3,181** | **every script, on device** |
| Authoring builders — 3 profiles, you use **one** | 2,801 | compile-time API (you define handlers) |
| Registration (assemble the host definition) | 248 | once, at startup |
| Optional validation (`SQLITEHOST_SLIM` strips it) | 918 | build/registration only |
| Types / interfaces / config / inline-functions | 1,379 | declarations |

Every file counts once, and the rows sum to the header. The validation row
is the exception: it is the lines `--slim` removes — `SqlParameterScanner.cs`
plus every `#if !SQLITEHOST_SLIM` block — wherever they live, so the other
four rows count their files net of those blocks. The authoring row is the
eight profile files the trim table below names.

The **engine** is the only bucket that executes a backend-supplied script
at runtime. Everything else is either the typed API *you* use to declare
your handlers, plain type declarations, or defense-in-depth validation.

## The audit surface: ~1 file

If your question is *"what interprets an untrusted-ish backend script on the
player's device,"* you review the engine — and it is concentrated:

| File | Lines | Role |
|---|---:|---|
| `SqliteHostRuntimeCore.cs` | 1,297 | the run loop: execute SQL via the adapter, read the control row, drain the queue, dispatch `call_*` rows to your handler, write results back |
| `ErasedHostMethodSpec.cs` | 385 | per-call marshaling: call row → input object → handler → result rows |
| `ErasedScalarFields.cs` / `ErasedFieldModels.cs` | 638 | scalar column read/write |
| `SchemaGenerator.cs` / `NamingDerivation.cs` / `ResolvedNames.cs` | 358 | workspace DDL + physical names (read from the definition, derived when it declares none) |

So the real "what runs untrusted input" review is **~2.7k lines, half of it
one file** (the 1,297-line run loop) — not the whole package. It is
ordinary C# with no reflection, no codegen at runtime, and no external
dependencies.

## Binary size vs. visible source

These are different problems, and the binary one is already solved:

- **Binary / download size**: under Unity IL2CPP, managed code stripping
  removes unreferenced code, so the profiles you do not call and (with
  `SQLITEHOST_SLIM`) the validation cost **nothing** in the shipped app.
  The measured floor is in `docs/reports/il2cpp-size-report.md`; SLIM's own
  delta is ~1.4–2.6 KB gzipped on a 50-method host, because validation is
  fixed code, not the per-method DTO cost that dominates.
- **Visible source lines**: trimming (below) is about *what a reviewer
  sees in the vendored folder*, not download size.

## Trimming to one profile (optional, source-only)

The three authoring profiles (`classic` / `compact` / `ultra`,
`docs/csharp-api.md`) are mutually independent and each sits on the shared
engine — nothing in the engine references a profile entry point. So a
project that uses one profile can delete the other two profiles' files. The
generated code you emit references only your chosen profile.

Delete the files for the profiles you do **not** use:

| Keep this profile | Delete these files |
|---|---|
| **ultra** | `CompactHostMethod.cs`, `HostMethod.cs`, `HostMethodSpecBuilder.cs`, `ScalarFields.cs`, `FieldsBuilders.cs` |
| **compact** | `UltraHostMethod.cs`, `UltraFields.cs`, `SqliteHostUltraValues.cs`, `HostMethod.cs`, `HostMethodSpecBuilder.cs`, `ScalarFields.cs`, `FieldsBuilders.cs` |
| **classic** | `UltraHostMethod.cs`, `UltraFields.cs`, `SqliteHostUltraValues.cs`, `CompactHostMethod.cs` |

Or let the tool do it — `node unity/vendor.mjs --profile ultra --out <dir>`
copies the package with the other profiles dropped. A single-profile tree is
6,474 lines (compact), 6,500 (classic) or 6,977 (ultra) instead of 8,527.

Each of these three trims is compiled as a single assembly (mirroring the
UPM package's `SqliteHost.asmdef`) by `tests/vendor-trim` in the full gate
(`tests/end-to-end/run-all.sh`), so the delete list stays verified — **0
warnings, 0 errors** — as the runtime evolves, not asserted once.

To also drop the optional validation, either define `SQLITEHOST_SLIM`
(Unity: Scripting Define Symbols) or pass `--slim` to the tool:
`node unity/vendor.mjs --profile ultra --slim --out <dir>`. `--slim` removes
every `#if !SQLITEHOST_SLIM` block and the validation-only
`SqlParameterScanner.cs` from the copied source, so the result compiles with
no define set — an ultra `--slim` tree is 6,059 lines (classic 5,596,
compact 5,570). See
`docs/compatibility.md` ("App size") for exactly what SLIM removes.

**Trade-off:** deleting a profile or defining SLIM removes defense-in-depth
that catches malformed method definitions and backend/client contract skew
early. If your backend is the only source of scripts and is already
validated by the Java/TS validators (`docs/validation.md`), that safety net
is redundant on device — which is the whole premise of the SLIM build.

Nothing here changes the `csharp/` source of truth or the authoring API:
trimming happens only in your vendored copy, and is fully reversible by
re-copying the sources.
