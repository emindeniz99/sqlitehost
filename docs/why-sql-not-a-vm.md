# Why SQL, not an embedded VM

The README says SqliteHost "is not a Lua-style embedded VM"; the plan
lists Lua under v1 non-goals. This document records *why* — which
alternatives were considered, the criteria they were judged against, and
what the SQL choice costs us. It is rationale, not a claim that embedded
VMs are bad: the last section says when you should pick one instead.

**Epistemic note.** Every SqliteHost number here is measured
(`docs/reports/il2cpp-size-report.md`). Third-party sizes are **order of
magnitude only, not measured in this repo** — they vary by version,
platform, architecture, and build flags. If a size decision hinges on
them, measure the candidate yourself with
`docs/guides/il2cpp-size-protocol.md`.

## What the script layer had to satisfy

These come from the actual product constraints, not a generic wishlist:

| # | Requirement | Why |
|---|---|---|
| R1 | **App download size budget** | Cellular App Store limit; the whole point of the size waves (profiles, `SQLITEHOST_SLIM`, erased core) |
| R2 | **Unity IL2CPP / AOT, no JIT on iOS** | iOS forbids third-party JIT; IL2CPP is AOT — no `Reflection.Emit`, no runtime code generation |
| R3 | **Exact 64-bit integers end to end** | IDs, currency, timestamps, scores. Silent precision loss is a data-corruption bug, not a rounding nit |
| R4 | **Typed host boundary, generated for 3 languages** | One TypeSpec definition → C#/Java/TS contracts, so backend and client cannot drift |
| R5 | **Scripts authored by our backend and validated before shipping** | Three validators + a shared fixture corpus reject a bad payload before a device ever sees it |
| R6 | **Terminating by construction** | Steps are a static sequence with no jumps; a script cannot hang the game loop |
| R7 | **Prefer a dependency we already ship** | Marginal cost beats new cost |

## The field

| Option | Kind | Runs under IL2CPP / iOS | Added payload (order of magnitude, **unmeasured**) |
|---|---|---|---|
| **SQL over SQLite** (chosen) | Declarative, embedded DB engine | Yes — native lib, no JIT | **~84–100 KB gz measured** for SqliteHost itself; SQLite often already shipped |
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
- **SQLite** is a plain native library with no code generation at all — its
  query planner is data, not emitted machine code.

## Why SQL won

- **R7 — it was already there.** Games that persist state already ship
  SQLite (a system library on iOS; commonly bundled on Android). The
  script engine's marginal cost is then only SqliteHost:
  **~84 KB gz** (ultra + SLIM, 50 methods) to **~100 KB gz** (ultra),
  measured under Unity IL2CPP on Android/ARM64.
- **R3 — int64 is native**, not an add-on type.
- **R4 — the boundary is data, not FFI.** Host calls are rows in `call_*`
  tables, so the same TypeSpec definition generates C#, Java, and
  TypeScript contracts and all three validators check the same payload.
  Binding a Lua/JS VM to typed host functions means a per-language marshaling
  layer instead.
- **R6 — terminating by construction.** Steps are a static sequence; there
  is no `goto`/`while` across steps, so a script cannot spin forever.
  Data-driven iteration uses recursive CTEs, which SQLite bounds.
- **R5 — validation is cheap because the surface is small.** A narrow
  declarative surface can be statically linted in three languages
  (`docs/validation.md`); a general-purpose language cannot be, so you fall
  back to runtime sandboxing.

## What this costs us (honest non-goals)

- **No imperative control flow across steps.** No loops, no `goto`, no
  early `continue` — deliberately (R6). Recursive CTEs cover data-driven
  iteration; `script_control` covers halt/abort.
- **SQL is an awkward language for arithmetic-heavy logic.** Anything
  beyond "select, compare, branch, call" belongs in a host method.
- **No hot-update story.** Shipping new *logic* (not just new scripts) means
  shipping the app. Remote delivery, signing, and TTL policy are explicit
  v1 non-goals. This is the dimension where Lua genuinely wins — it is why
  Lua dominates mobile games that patch logic without a store review.
- **Not a sandbox claim.** Full SQL sandboxing is a v1 non-goal. The threat
  model is "our backend authors scripts, validators gate them", not
  "arbitrary third parties upload code".

## When you should pick a VM instead

Pick an embedded VM (and accept the size and int64 work) when:

- You need **hot-updatable logic** without an app release — the single
  strongest reason, and the reason Lua/xLua exist in this space.
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
