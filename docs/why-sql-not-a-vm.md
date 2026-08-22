# Why SQL, not an embedded VM

The README says SqliteHost "is not a Lua-style embedded VM", and lists
Lua under the v1 non-goals. This document records *why* — which
alternatives were considered, the criteria they were judged against, and
what the SQL choice costs us. It is rationale, not a claim that embedded
VMs are bad: the last section says when you should pick one instead.

**Epistemic note.** Every SqliteHost number here is measured
(`docs/reports/il2cpp-size-report.md`). Third-party sizes are **order of
magnitude only, not measured in this repo** — they vary by version,
platform, architecture, and build flags. If a size decision hinges on
them, measure the candidate yourself with
`docs/guides/il2cpp-size-protocol.md`.

## "Not a VM" means *no additional* VM

SQLite **is** a virtual machine, by its own documentation: it "works by
translating SQL statements into bytecode and then running that bytecode
in a virtual machine" — `sqlite3_prepare_v2()` is the compiler,
`sqlite3_step()` is the VM, and the VM has a name, the **VDBE**
(<https://sqlite.org/opcode.html>).

So the claim in this document is never "no VM". It is **"no *additional*
VM"**: no second bytecode runtime to ship, audit, AOT-compile, or keep
alive next to the one the app already links and already trusts with its
data. Read every "not a VM" phrasing in this repo — including this
file's title — that way. Stating it as "no VM" is wrong and a technical
reader will catch it.

## What the script layer had to satisfy

These come from the actual product constraints, not a generic wishlist:

| # | Requirement | Why |
|---|---|---|
| R1 | **App binary / download size budget** | Marginal-cost discipline (see note): an AOT/IL2CPP game binary is already large, and every embedded engine is bytes spent against the store's build maximums with no way to page them out — the whole point of the size waves (profiles, `SQLITEHOST_SLIM`, erased core) |
| R2 | **Unity IL2CPP / AOT, no JIT on iOS** | iOS forbids third-party JIT; IL2CPP is AOT — no `Reflection.Emit`, no runtime code generation |
| R3 | **Exact 64-bit integers end to end** | IDs, currency, timestamps, scores. Silent precision loss is a data-corruption bug, not a rounding nit |
| R4 | **Typed host boundary, generated for 3 languages** | One TypeSpec definition → C#/Java/TS contracts, so backend and client cannot drift |
| R5 | **Scripts authored by our backend and validated before shipping** | Three validators + a shared fixture corpus reject a bad payload before a device ever sees it |
| R6 | **Statically analyzable control flow** | Steps are a static sequence with no jumps — the *script* cannot loop. A single *statement* still can — see R6 under "Why SQL won" |
| R7 | **Prefer a dependency we already ship** | Marginal cost beats new cost |

**Note on R1 — the real store limits.** There is **no hard cellular
download cap**: since iOS 13, Settings > App Store > App Downloads is a
*user preference* ("Always Allow" / "Ask If Over 200 MB"); 200 MB is a
prompt threshold, not a ceiling, and a larger build installs over
cellular if the user taps through. The old hard cap (150 MB, raised to
200 MB in 2019) is gone. The limits that actually bind are Apple's
published build maximums, and they are **tiered by deployment target**:
the total of all `__TEXT` sections may be up to **500 MB on iOS 9.0 and
later** (60 MB on iOS 7.x–8.x, and only 80 MB below iOS 7.0 — a target
no shipping app has), plus a max uncompressed app of 4 GB
(<https://developer.apple.com/help/app-store-connect/reference/maximum-build-file-sizes/>).
Google Play's base-module compressed download limit is likewise 500 MB,
with asset packs above that. So no single hard ceiling is the forcing
function — the honest R1 argument is *marginal-cost discipline*: an AOT
game's `__TEXT` (IL2CPP/NativeAOT machine code) is already large and
non-pageable, so every embedded interpreter is bytes charged against
that budget for its whole install life. That is what pushes large
codebases to move logic out of the binary.

## The field

| Option | Kind | Runs under IL2CPP / iOS | Added payload (order of magnitude, **unmeasured**) |
|---|---|---|---|
| **SQL over SQLite** (chosen) | Declarative, embedded DB engine | Yes — native lib, no JIT | **~84–100 KB gz measured, *marginal*** — SqliteHost only, on top of an already-linked SQLite. Stock SQLite alone is ~590–750 KB (see below) |
| **Lua 5.3 / 5.4** | Embedded VM (native) | Yes (interpreter) | ~100s of KB native, per architecture |
| **LuaJIT** | Embedded VM (native, JIT) | Yes, but **JIT disabled on iOS** → interpreter mode | ~100s of KB native, per architecture |
| **xLua** (Tencent) | Unity binding over LuaJIT *or* Lua 5.3/5.4 | Yes; inherits the backend's iOS JIT limits | Backend lib + binding/glue layer |
| **MoonSharp** | Lua interpreter in pure C# | Yes (managed, no `Reflection.Emit`) | ~1 MB-class managed assembly before stripping |
| **Jint** | JS/ES interpreter in pure C# | Yes (managed) | ~1 MB-class managed assembly before stripping |
| **QuickJS** (e.g. via Puerts) | Small JS engine (native) | Yes (interpreter) | ~1 MB-class native, per architecture |
| **V8** (ClearScript / Puerts) | Full JS engine (native, JIT) | **Jitless mode required on iOS** | ~10s of MB, per architecture |
| **Wasm** (wasm3 / wasmtime) | Bytecode VM | wasm3 yes; JIT runtimes not on iOS | wasm3 ~100s of KB; JIT runtimes far larger |

Two things stand out immediately: everything except SQLite adds a *second*
execution engine to an app that already ships one, and the JS options
either blow R1 (V8) or still need a full language runtime for logic that,
in our case, is mostly "read some rows, call a host method, write some
rows".

## R3 in detail — 64-bit integers

This is the criterion that quietly eliminates most of the field, and it is
the one people check last.

| Option | Exact int64? | How |
|---|---|---|
| **SQLite** | ✅ **native** | `INTEGER` is a 64-bit signed integer |
| **Lua 5.3+ / 5.4** | ✅ native | 5.3 added an integer subtype (64-bit signed) |
| **Wasm** | ✅ native | `i64` is a core value type |
| **xLua** | ⚠️ supported, backend-dependent | Native integers on a Lua 5.3+ backend; boxed `cdata` int64 on the LuaJIT backend |
| **LuaJIT** | ⚠️ via FFI only | Numbers are doubles; int64 needs `ffi.cast("int64_t", …)` / `1LL` literals — a separate type that does not flow like a plain Lua number |
| **Jint / Puerts / ClearScript** | ⚠️ via `BigInt` | JS `number` is a double; `BigInt` exists but is a distinct type with its own arithmetic and JSON rules |
| **Lua 5.1 / 5.2** | ❌ | One number type: `double` |
| **MoonSharp** | ❌ | Lua 5.2 semantics — all numbers are doubles |

A `double` carries integers exactly only up to **2^53−1**
(9,007,199,254,740,991); int64 reaches **2^63−1**
(9,223,372,036,854,775,807). On the ❌ and ⚠️ rows, every large ID or
currency value must be hand-wrapped (string, `BigInt`, FFI `cdata`) at
every boundary — and the failure mode when someone forgets is *silent
corruption*, not an exception.

Choosing SQLite made int64 the native case. The one place the problem
still surfaced is the JSON wire (payloads are authored in TypeScript and
pass through browsers), and it is handled explicitly rather than left to
chance: `int64` is a JSON number when |v| ≤ 2^53−1 and a **decimal string**
otherwise, with both accepted by every parser
(`docs/script-envelope.md`). `float32`/`float64` deliberately reject the
string form — every IEEE-754 double round-trips as a JSON number, so only
int64 needs the escape hatch.

## R2 in detail — AOT and the iOS JIT ban

iOS does not allow third-party JIT compilation, and Unity IL2CPP is
ahead-of-time: no `Reflection.Emit`, no runtime code generation.

- **Native JIT VMs** (LuaJIT, V8) must fall back to interpretation on iOS,
  so you carry the JIT machinery's size without its speed.
- **Managed interpreters** (MoonSharp, Jint) are AOT-safe, but you pay a
  managed assembly and interpreted execution.
- **SQLite** generates no *machine* code at all: `sqlite3_prepare_v2()`
  compiles SQL to VDBE bytecode — data interpreted by `sqlite3_step()` —
  so nothing needs a JIT, and nothing needs an AOT pass of its own.

## Why SQL won

- **R7 — it was already there.** Games that persist state already ship
  SQLite (a system library on iOS; commonly bundled on Android). The
  script engine's marginal cost is then only SqliteHost:
  **~84 KB gz** (ultra + SLIM, 50 methods) to **~100 KB gz** (ultra),
  measured under Unity IL2CPP on Android/ARM64.
  **The condition is load-bearing — state it whenever you quote the
  number.** ~84–100 KB gz is *marginal*: it assumes SQLite is already
  linked. It is not the cost of the engine. SQLite itself, compiled
  `-Os` with no extra options, measures ~590 KB (ARM64), ~650 KB
  (Ubuntu x64) and ~750 KB (macOS arm64) of object code
  (<https://sqlite.org/footprint.html>). If your app does **not** already
  ship SQLite, that is the figure to compare against the VM rows above,
  and R7 does not apply to you at all — see "When you should pick a VM
  instead".
- **R3 — int64 is native**, not an add-on type.
- **R4 — the boundary is data, not FFI.** Host calls are rows in `call_*`
  tables, so the same TypeSpec definition generates C#, Java, and
  TypeScript contracts and all three validators check the same payload.
  Binding a Lua/JS VM to typed host functions means a per-language marshaling
  layer instead.
- **R6 — the script's shape is static; its runtime is not bounded.**
  What is true: steps are a static sequence, there is no `goto`/`while`
  across steps, and the runtime caps how *many* statements a run may
  execute (`MaxStatementsPerRun` 256, `MaxPendingCallsPerStep` 64 —
  `SqliteHostRuntimeOptions`). That is what makes the payload
  statically analyzable, and it is the whole of R6's benefit.
  **What is not true — and an earlier revision of this document claimed
  it — is that SQLite bounds recursive CTEs.** It does not.
  `WITH RECURSIVE q(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM q)
  SELECT max(x) FROM q` raises no error and returns nothing — measured
  on 3.45.1, still running when killed at 8 s; it ends only when memory
  is exhausted or something kills it. A recursive CTE
  terminates only if the author writes a terminating recursion, and a
  cartesian join needs no recursion at all to run effectively forever.
  Bounding the statement *count* does not bound the time any one
  statement takes. See the non-goal below.
- **R5 — validation is cheap because the surface is small.** A narrow
  declarative surface can be statically linted in three languages
  (`docs/validation.md`); a general-purpose language cannot be, so you fall
  back to runtime sandboxing.

## What this costs us (honest non-goals)

- **No imperative control flow across steps.** No loops, no `goto`, no
  early `continue` — deliberately (R6). Recursive CTEs cover data-driven
  iteration; `script_control` covers halt/abort.
- **No time or VM-step budget — a single statement can hang the app.**
  A runaway recursive CTE or an accidental cartesian join runs until it
  exhausts memory or the OS kills the process; on a game's main thread
  there is no recovery path. SQLite ships exactly the knobs to cap this
  — `sqlite3_progress_handler()` + `sqlite3_interrupt()`,
  `sqlite3_limit(SQLITE_LIMIT_VDBE_OP)` and the other
  `sqlite3_limit()` values, `sqlite3_set_authorizer()`
  (<https://sqlite.org/security.html>) — and **v1 sets none of them**;
  `docs/adapter-contract.md` does not require an adapter to set them
  either. Do not quote SQLite's resource-limit story as ours until it
  does. Mitigation today is authoring discipline plus the validators.
- **SQL is an awkward language for arithmetic-heavy logic.** Anything
  beyond "select, compare, branch, call" belongs in a host method.
- **Hot update is partial, and the line is the host boundary.** An
  earlier revision said "no hot-update story", which undersold the
  architecture. Precisely: **new scripts already ship without a build.**
  A script is a JSON envelope (`docs/script-envelope.md`) the app loads
  at runtime, so new rules, new branching and new tuning over the
  existing host methods need no store review. What *does* require
  shipping the app is a new **host method** — the typed C#/Java/TS
  surface is generated and compiled in. What v1 lacked was not the
  capability but the **delivery plumbing**: remote fetch, signature
  verification, TTL and rollback policy. Signature verification, TTL and
  rollback now ship as the optional `SqliteHost.Delivery` package
  (`docs/guides/script-delivery.md`); the transport stays yours, by
  design. Lua still wins outright when the thing that must
  change without a release is the *logic itself* rather than rules
  expressed over host methods.
- **Not a sandbox claim.** Full SQL sandboxing is a v1 non-goal. The threat
  model is "our backend authors scripts, validators gate them", not
  "arbitrary third parties upload code".
- **Script SQL is a wide surface, and not all of it is supported.**
  Script authors get real SQL, which means they can also reach features
  above the 3.19.3 floor, compile-gated modules SqliteHost never probes,
  and statements the runtime's execution model cannot survive. What is
  out of bounds — and why — is enumerated in `docs/sqlite-surface.md`.

## When you should pick a VM instead

Pick an embedded VM (and accept the size and int64 work) when:

- You need **new logic**, not new rules, without an app release —
  behaviour no host method exposes. (New *scripts* over existing host
  methods do not need a release; see the non-goal above.) This is still
  the single strongest reason to pick a VM, and the reason Lua/xLua
  exist in this space.
- Your scripts do **real computation** (AI, simulation, procedural
  generation) rather than orchestrating host calls over stored state.
- You need **general control flow** and are willing to own the
  non-termination and sandboxing problems that come with it.
- Your app **does not already ship SQLite**, so SQL has no incumbency
  advantage and the comparison is engine-vs-engine on its merits.

If none of those hold — the script orchestrates typed host calls over
data the app already stores in SQLite — then adding a second execution
engine buys ergonomics you can get from generated typed contracts, at a
size and int64 cost SQL does not charge.
