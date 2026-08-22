# Getting started — consumer guide

You have an application (game client, backend, tool) and you want
scripts — SQL payloads — to drive *your* typed host methods through
SqliteHost. Consuming SqliteHost always means the same four things,
whatever the packaging:

1. **A host contract**: DTOs, a handler interface, method specs, a
   host definition (generated from TypeSpec, or hand-written in the
   generated style).
2. **Handlers**: your application logic behind the generated
   `IGeneratedHostHandlers`-style interface.
3. **An adapter**: `ISqliteHostConnectionFactory` /
   `ISqliteHostConnection` over your SQLite wrapper
   (`docs/adapter-contract.md`).
4. **A runtime call**: `new SqliteHostRuntime<…>(…).Run(script)`.

This guide gives four self-contained recipes for getting there, then
walks one mini-host end to end. Background reading:
[architecture](../architecture.md),
[C# API surface](../csharp-api.md),
[script envelope](../script-envelope.md).

## Which path am I?

| You are… | Path |
|---|---|
| C#/Unity app, **no npm/NuGet/Node wanted, ever** | **A** (full vendor; hand-write the generated-style files) |
| Same, but someone can run Node **once** on any machine | **B** once for codegen, then **A** to vendor the outputs |
| Team with Node ≥ 20 available; host contract will evolve | **B** (TypeSpec is the source of truth; regen on change) |
| Multi-language consumer (C# runtime + Java/TS backend) | **B** — one `.tsp`, three generated contracts |
| Normal .NET/Java/npm project that prefers package managers | **C** (published feeds later; local feed works today) |
| Unity project that prefers Package Manager over copying | **C** (UPM git URL) |
| Java backend handling envelope/manifest JSON or DDL | **C** Maven (`sqlite-host-model`, `-jdbc`) |
| TS tool where script payloads get authored | **C** npm (`@sqlite-host/authoring`) + **D** lint |
| Backend that must vet script payloads before shipping them | **D** (Java validator CLI + TS authoring lint) |
| "Just show me one host working" | [Your first host end-to-end](#your-first-host-end-to-end) |

Paths combine: a typical setup is B (codegen) + C (runtime packages)
+ D (backend validation).

## Path A — full vendor, zero package managers

For the consumer who wants no Node, no NuGet, no npm — only source
files checked into their own repo. This is a supported first-class
path (`docs/packaging.md`), not a workaround: the C# sources are
netstandard2.0 / C# 8, dependency-free, single namespace root.

### A.1 What to copy

Copy these folders (each is flat: `*.cs` files plus a `.csproj` you
can keep or discard) into your repo, in this order:

| # | Copy | Why / depends on |
|---|---|---|
| 1 | `csharp/SqliteHost.Abstractions/` | adapter interfaces, envelope DTOs, result types — zero dependencies |
| 2 | `csharp/SqliteHost.Runtime/` | execution core, fluent descriptors, schema generation — depends only on Abstractions |
| 3 | `csharp/SqliteHost.Conformance/` | adapter conformance suite — **test project only**, depends on both + xunit.core/xunit.assert/Xunit.SkippableFact |
| 4 | your generated folder | see A.2 |

Skip `bin/`/`obj/`. If you drop the `.cs` files straight into an
existing project instead of keeping the two csproj files, that works
too — Abstractions grants internals to Runtime via
`InternalsVisibleTo`, which is a no-op when both compile into one
assembly. The namespace root is `SqliteHost` everywhere; keep it, or
find/replace-rename it across the copied files if your project bans
third-party root namespaces.

**Unity projects**: skip steps 1–2 and copy
`unity/com.sqlitehost.runtime/` instead — it is the same two source
trees already packaged with an asmdef (`Runtime/SqliteHost.asmdef`,
`autoReferenced: true`) plus the generated sample under
`Samples~/GeneratedSample/`. Either place the folder under your
project's `Packages/` (embedded package) or reference it by path the
way `unity/SampleProject/Packages/manifest.json` does
(`"com.sqlitehost.runtime": "file:../../com.sqlitehost.runtime"`).
See `docs/guides/unity-2021-spike.md` for the editor checklist.

### A.2 Generated code without Node — the two honest options

Without Node you cannot run the TypeSpec codegen. You are not stuck;
you have exactly two options.

**Option 1 — hand-write the generated-style files.** Plan §31
(resolved decisions, `docs/architecture.md`) makes generated DTOs the
*default*, not a requirement: the fluent descriptor API is public and
pinned (`docs/csharp-api.md`), so hand-written registrations are
first-class. Use `csharp/SqliteHost.Generated.Sample/` as the
template. It is five files:

| File | Contains |
|---|---|
| `HostMethodDtos.g.cs` | one input DTO + one result DTO per method (plus item DTOs for list fields) — plain classes, public auto-properties, `List<T>` properties initialized inline |
| `IGeneratedHostHandlers.g.cs` | the handler interface: one member per method, `GetValueResult GetValue(GetValueInput input);` |
| `GeneratedHostMethodSpecs.g.cs` | `public static class GeneratedHostMethodSpecs` with `BuildAll()` returning one spec per method, each built by a private `Build<Op>Spec()` using the fluent `HostMethod.For<…>(…)` API |
| `GeneratedHostDefinition.g.cs` | `GeneratedHostDefinition.Build()` — `SqliteHostDefinition.ForHandlers<…>().ApiLevel(…).MinSqliteVersion(…).Naming(…).Methods(GeneratedHostMethodSpecs.BuildAll())` |
| `GeneratedSchemaSql.g.cs` | optional `const string SchemaScript` DDL snapshot — you can omit it; the runtime generates the schema itself via `GenerateSchemaStatements()` |

The invariant to preserve: **one method = one DTO pair + one
interface member + one `Build<Op>Spec()` registered in `BuildAll()`**,
and every field's `sqlName` in the spec is the logical snake_case
name (`"default_value"`, never the physical `input_default_value` —
the runtime derives columns via naming, `docs/naming.md`).

**Option 2 — run codegen once, vendor the outputs.** Any machine with
Node ≥ 20 (a colleague's laptop, a one-off CI job) can produce the
generated sources following [Path B](#path-b--typespec-codegen-flow);
commit the outputs to your repo and never need Node again — until the
contract changes, at which point you repeat the one-off run. Golden
tests in this repo guarantee generated output is deterministic, so
vendored files are stable and diffable.

### A.3 Handlers, adapter, bootstrap, first script

**Handlers.** Implement the handler interface with your application
logic. The runtime invokes it only through the generated interface —
never by reflection, never by inference.

**Adapter.** Easiest path: use the shipped
`csharp/SqliteHost.Adapters.Native/` package — a pure
DllImport("sqlite3") adapter with no dependencies beyond Abstractions
(scalar-function capability included; under Unity IL2CPP add
`[MonoPInvokeCallback]` to its two static callbacks when vendoring —
the Execute/Query path needs nothing). Or implement
`ISqliteHostConnectionFactory` / `ISqliteHostConnection` /
`ISqliteHostRow` over whatever SQLite wrapper you already use — read
`docs/adapter-contract.md` first; the core rule is *no silent
failure*. Reference adapters to mirror, in
`csharp/SqliteHost.Tests/Adapter/`:

- `MicrosoftDataSqliteAdapter.cs` (Microsoft.Data.Sqlite — the shape
  to copy for desktop/server),
- `SystemDataSqliteAdapter.cs` (System.Data.SQLite),
- `SqliteNetAdapter.cs` (sqlite-net-pcl — the shape to copy for
  wrapper-handle integrations).

Then prove your adapter with the conformance suite: your vendored
copy of `csharp/SqliteHost.Conformance/` ships
`AdapterConformanceTestsBase` (23 xunit tests). In your test project:

```csharp
using SqliteHost;
using SqliteHost.Conformance;

public class MyAdapterConformanceTests : AdapterConformanceTestsBase
{
    protected override ISqliteHostConnection OpenAdapterConnection()
        => MySqliteHostConnection.OpenInMemory();
}
```

**Bootstrap** (the pinned constructor shape, `docs/csharp-api.md` and
`examples/README.md`):

```csharp
var hostDefinition = GeneratedHostDefinition.Build();          // generated (or hand-written)
var handlers = new GameHostHandlers(storage);                  // yours: IGeneratedHostHandlers
var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
    connectionFactory: new YourSqliteConnectionFactory(),      // yours: adapter
    hostDefinition: hostDefinition,
    handlers: handlers,
    options: new SqliteHostRuntimeOptions { EnableDiagnostics = true });

SqliteHostRunResult result = runtime.Run(script);
```

**First script.** The hello world is
`fixtures/payloads/valid/example-001-read-then-conditional-write.json`
(walked through in `examples/README.md`): step 1 inserts into
`call_get_value`, step 2 conditionally inserts into `call_set_value`
by reading `result_get_value`. Note that JSON parsing is deliberately
*not* in the C# runtime — `Run()` takes a parsed `SqliteHostScript`
object. Either build the script in code (see the end-to-end section
below) or crib the ~100-line test-only loader
`csharp/SqliteHost.Tests/Fixtures/ScriptEnvelopeJson.cs`. Expected
outcome with a handler that returns something other than 42:
`Status == Completed`, `ExecutedCallCount == 2`.

## Path B — TypeSpec codegen flow

The intended long-term flow: the `.tsp` file is the single source of
truth, and C#, Java, and TypeScript contracts are emitted from it.

**Prereqs (one-time):** Node ≥ 20 and pnpm (npm works for running the
CLIs, but this workspace builds with pnpm). Until the npm packages
publish (`docs/guides/publishing.md`), codegen runs from a clone of
this repo:

```sh
cd projects/sqlitehost
pnpm install
pnpm -r run build        # builds the TypeSpec library + all emitters into dist/
```

### B.1 Write your host library

Create `your-host.tsp`. Minimal two-method example (one read, one
write), modeled on `typespec/examples/sample-host-methods.tsp`:

```typespec
import "@sqlite-host/typespec";

using SqliteHost;

namespace Acme.Notes;

@hostLibrary({ apiLevel: 1 })
interface NotesHostMethods {
  @hostMethod({ name: "getNote", handler: "GetNote" })
  op GetNote(input: GetNoteInput): GetNoteResult;

  @hostMethod({ name: "saveNote", handler: "SaveNote" })
  op SaveNote(input: SaveNoteInput): SaveNoteResult;
}

model GetNoteInput {
  key: string;
}

model GetNoteResult {
  body: string;
  found: boolean;
}

model SaveNoteInput {
  key: string;
  body: string;
}

model SaveNoteResult {
  saved: boolean;
}
```

Only `apiLevel` is required in `@hostLibrary`; the six naming keys
default to protocol v1 naming (`call_` / `result_` / `input_` …) and
`minSqliteVersion` defaults to `"3.19.3"`. `@sqlName` is optional and
defaults to snake_case of the property name. The full option surface
is documented in `typespec/library/lib/decorators.tsp`.

The `import "@sqlite-host/typespec"` must resolve through Node module
resolution from the `.tsp` file's location. Inside this repo's
workspace it just works; for a `.tsp` living elsewhere, give its
folder a `node_modules/@sqlite-host/typespec` (a dependency once
published; a symlink to `typespec/library` today).

### B.2 Run the emitters

Two stages: `.tsp` → canonical manifest + DDL, then manifest → each
language. Exact commands, verified against this repo (all four CLIs
print every file they write and exit non-zero on failure):

```sh
cd projects/sqlitehost

# 1. TypeSpec -> canonical manifest + DDL snapshot
node codegen/manifest-emitter/dist/cli.js path/to/your-host.tsp generated --base-name your-host
#    writes generated/your-host.manifest.json + generated/your-host.ddl.sql

# 2. manifest -> generated sources per language
node codegen/csharp-emitter/dist/cli.js     generated/your-host.manifest.json generated/csharp
node codegen/java-emitter/dist/cli.js       generated/your-host.manifest.json generated/java
node codegen/typescript-emitter/dist/cli.js generated/your-host.manifest.json generated/ts --base-name your-host
```

Argument shapes (from each CLI's usage string):

| CLI (bin name) | Usage |
|---|---|
| `sqlite-host-emit-manifest` | `<entrypoint.tsp> <out-dir> [--base-name <name>]` |
| *(no bin — run `codegen/csharp-emitter/dist/cli.js`)* | `<manifest.json> <out-dir> [--profile classic\|compact\|ultra] [--namespace <ns>] [--dto-fields]` |
| `sqlite-host-emit-java` | `<manifest.json> <out-dir>` |
| `sqlite-host-emit-typescript` | `<manifest.json> <out-dir> [--base-name <name>]` |

C#-only: `--profile` picks the generated-code **size profile** —
`classic` (default; typed fluent style), `compact` (same typed
DTOs/handlers, static pre-erased accessors: ~8× smaller per-method
AOT/IL2CPP footprint), or `ultra` (no DTOs, name-keyed call/result
surface: smallest). Behavior is identical across profiles; pick by
app-size budget (measured numbers: `docs/compatibility.md`, App
size). `--namespace` overrides the generated namespace (default
`<tsp namespace>.Generated`). `--dto-fields` emits DTO members as
public fields instead of auto-properties — recommended when targeting
**Unity IL2CPP** (measured ~32 KB raw / ~12 KB gz smaller on a
50-method host there; zero difference under NativeAOT; usage code
`x.Key = v` unchanged — `docs/reports/il2cpp-size-report.md`).

`--base-name` defaults to `sample-host` — pass your own. The bin
names come from each emitter's `package.json` and matter once the
emitter packages are installable dependencies; today the `node
…/dist/cli.js` form is the reliable invocation (note the C# emitter
currently declares no `bin` entry at all).

### B.3 Where the outputs go

**C#** — six files. Put the five root files in your generated
project/folder (compiled together with Abstractions + Runtime, e.g. a
`YourApp.Generated` classlib or a `Generated/` folder):

```text
generated/csharp/HostMethodDtos.g.cs
generated/csharp/IGeneratedHostHandlers.g.cs
generated/csharp/GeneratedHostMethodSpecs.g.cs
generated/csharp/GeneratedHostDefinition.g.cs
generated/csharp/GeneratedSchemaSql.g.cs
generated/csharp/envelope/ScriptEnvelope.g.cs   <- do NOT compile this one
```

`envelope/ScriptEnvelope.g.cs` is the vendored envelope copy that
`SqliteHost.Abstractions` already ships (namespace `SqliteHost`) —
compiling it next to Abstractions gives duplicate-type errors. It
exists so *this* repo can golden-test the vendored copy; consumers
skip it.

**Java** — package trees, ready for `src/main/java`:

```text
generated/java/<your/name/space>/generated/*.java   <- your DTOs + MethodDescriptors: keep
generated/java/io/sqlitehost/model/envelope/*.java  <- skip when you depend on sqlite-host-model
```

The envelope classes are byte-identical to the ones shipped in
`io.github.emindeniz99:sqlite-host-model`; only take them if you refuse the
Maven dependency. `MethodDescriptors.java` is a compile-time mirror
of the manifest metadata (resolved table/trigger/column names per
method) for backend code that doesn't want to parse manifest JSON.

**TypeScript** — mirrors the vendored `typescript/` layout:

```text
generated/ts/authoring-sdk/src/generated/<base-name>.ts  <- your typed authoring module: keep
generated/ts/runtime-types/src/generated/envelope.ts     <- skip when you depend on @sqlite-host/runtime-types
```

Place `<base-name>.ts` at `src/generated/` in your authoring project.
Caveat: it is emitted for the in-repo layout, so it imports
`../metadata.js`; when placing it outside a vendored authoring-sdk
tree, change that line to
`import type { HostMetadata } from "@sqlite-host/authoring";`.

Also commit `your-host.manifest.json` — it is the input to every
validator (Path D), and the DDL snapshot is a useful review artifact.

### B.4 Regen policy

- **Commit the generated files.** They are inputs to your build, not
  build outputs; emission is deterministic.
- **Re-run the emitters on every `.tsp` change**, in the same commit.
- **Golden-diff in CI**: re-run the emitters and byte-compare against
  the committed files, so drift fails the build. The pattern to copy
  is `tests/cross-language-golden/run.mjs` — it recompiles the sample
  `.tsp`, re-runs all four emitters, and `assert.equal`s every output
  against the committed sources.
- Contract evolution is additive: breaking changes get a **new method
  name + higher api level**, never an in-place signature change
  (`docs/api-levels.md`).

## Path C — package consumption

Nothing is published to public registries yet
(`docs/guides/publishing.md` is the checklist; ROADMAP gates it on
naming/license signoff). The package IDs below are final per
`docs/packaging.md`; "today" recipes use a local feed and work now.

### C.1 NuGet — the C# trio

| Package | Reference from |
|---|---|
| `SqliteHost.Abstractions` | anywhere the envelope DTOs / adapter interfaces appear |
| `SqliteHost.Runtime` | the host application (pulls Abstractions) |
| `SqliteHost.Conformance` | your **test** project — adapter conformance suite |

Today, build the local feed from a clone (verified):

```sh
cd projects/sqlitehost/csharp
dotnet pack SqliteHost.Abstractions/SqliteHost.Abstractions.csproj -c Release -o /path/to/local-feed
dotnet pack SqliteHost.Runtime/SqliteHost.Runtime.csproj           -c Release -o /path/to/local-feed
dotnet pack SqliteHost.Conformance/SqliteHost.Conformance.csproj   -c Release -o /path/to/local-feed
```

In the consuming repo, a `nuget.config` next to the solution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="sqlitehost-local" value="/path/to/local-feed" />
  </packageSources>
</configuration>
```

and normal `PackageReference`s (`Version="0.1.0-preview"`, the
current csproj version). Gotcha: NuGet caches by version — if you
repack the same version after pulling newer sources, restore keeps
serving the stale cached copy. Bump a local suffix on repack
(`dotnet pack … -p:PackageVersion=0.1.0-local.2`) or clear it
(`dotnet nuget locals global-packages --clear`).

For adapter tests, add `SqliteHost.Conformance` plus a runner to your
test project and subclass the suite — full snippet and csproj
`ItemGroup` in `docs/adapter-contract.md`. xunit discovers the 23
inherited tests automatically.

### C.2 npm — the authoring/validation trio

| Package | For |
|---|---|
| `@sqlite-host/typespec` | authoring the `.tsp` (decorators + envelope models) |
| `@sqlite-host/runtime-types` | envelope types, `parseScript`/`serializeScript`/`validateScript`, binding helpers |
| `@sqlite-host/authoring` | `ScriptBuilder`, manifest metadata/autocomplete, static `lintScript` |

Once published: `pnpm add @sqlite-host/authoring` (pulls
runtime-types) and `pnpm add -D @sqlite-host/typespec
@typespec/compiler`. Today they are `"private": true` workspace
packages — consume them by working inside this workspace, or `pnpm
pack` each package (`typescript/runtime-types`,
`typescript/authoring-sdk`, `typespec/library`) and install the
tarballs. `@sqlite-host/sample-admin` is a demo CLI
(`sqlite-host-demo`), never published.

### C.3 Maven — the backend trio + validator CLI

| Artifact | For |
|---|---|
| `io.github.emindeniz99:sqlite-host-model` | envelope + manifest models, strict JSON reader/writer, DDL generator |
| `io.github.emindeniz99:sqlite-host-validator` | semantic lint engine (library) + thin CLI main |
| `io.github.emindeniz99:sqlite-host-jdbc` | prepare-only SQLite validation over the generated schema |

Today: `cd projects/sqlitehost/java && mvn -q install` puts
`0.1.0-SNAPSHOT` into your local `~/.m2`, then depend on the
coordinates normally. The shaded validator CLI is a *local tool*, not
a published contract — `mvn -q package` builds
`sqlite-host-validator/target/sqlite-host-validator-<version>-cli.jar`
plus the `java/bin/sqlite-host-validate` launcher (usage and exit
codes: `java/README.md` and Path D below).

### C.4 UPM — Unity Package Manager

`com.sqlitehost.runtime` installs today with zero publishing via a
git URL (publishing guide §f):

```text
Window > Package Manager > + > Add package from git URL…
https://github.com/OWNER/REPO.git?path=/projects/sqlitehost/unity/com.sqlitehost.runtime
pin a release: …?path=/projects/sqlitehost/unity/com.sqlitehost.runtime#sqlitehost-v0.1.0
```

(`?path=` selects the package subfolder in the monorepo; `#<rev>`
pins a tag/branch/SHA.) An OpenUPM listing is the planned longer-term
channel. The package contains the runtime only — no native SQLite,
no generated code; import the "Generated Sample" from the package's
Samples tab to see the expected shape of yours.

## Path D — backend validation pipeline

Scripts are data. Anything that ships a script payload to clients
should gate on validation first — the runtime will also fail fast at
execution time, but a backend gate catches authoring mistakes before
they reach a device. Two tools, one contract.

### D.1 The contract: what gets caught

`fixtures/payloads/expectations.json` is the executable definition of
what validation catches. Shape: a `manifest` reference plus `cases`,
one per fixture payload, each with `valid`, expected `errors[]` and
`warnings[]`; every finding carries a `code` (pinned in
`docs/validation.md`) and a `validators` list naming which
implementations MUST report it — `"java"` (the full engine, includes
prepare-only SQLite checks) and/or `"typescript"` (the static
authoring subset, no SQLite). Both implementations run conformance
tests against this file, so it is the ground truth for "if my
pipeline passes, what has been proven": structural envelope errors,
binding errors (`missing-binding`, `unused-binding`,
`binding-type-mismatch`, `mixed-prefix-binding`), host-call usage
(`implicit-column-list`, `undeclared-method-use`,
`duplicate-call-id`, list child colocation) and — Java only —
result-read lineage (`result-read-unknown-call`,
`result-read-not-after-call`) and `sql-prepare-error`.

The publishability rule (`docs/validation.md`): **zero errors =
publishable; warnings don't block.**

### D.2 Java shaded CLI — the pre-publish gate

```sh
cd projects/sqlitehost/java
mvn -q package
bin/sqlite-host-validate path/to/your-host.manifest.json path/to/payload.json
# or: java -jar sqlite-host-validator/target/sqlite-host-validator-0.1.0-SNAPSHOT-cli.jar <manifest> <payload>
```

One finding per line (`ERROR <code> [step/statement] message` /
`WARNING …`). Exit codes (verified):

| Code | Meaning |
|---|---|
| 0 | publishable — no errors (warnings may have printed) |
| 1 | validation errors |
| 2 | usage error, or manifest/script unreadable |

Wire it as a CI/publish gate: `sqlite-host-validate manifest.json
payload.json || reject`. Note the CLI runs the semantic lint only;
the prepare-only SQLite layer lives in `sqlite-host-jdbc` as a
library (add it to your backend's tests for full coverage).

### D.3 TypeScript lint — at authoring time

`@sqlite-host/authoring` runs the static subset in-process, e.g. in
the tool where scripts are written (verified):

```ts
import { readFileSync } from "node:fs";
import { isPublishable, lintScript, parseHostManifest } from "@sqlite-host/authoring";

const manifest = parseHostManifest(readFileSync("your-host.manifest.json", "utf8"));
const findings = lintScript(JSON.parse(readFileSync("payload.json", "utf8")), manifest);
if (!isPublishable(findings)) throw new Error(JSON.stringify(findings, null, 2));
```

Better: don't hand-write payloads at all — build them with the fluent
`script({...}).step("id").statement(sql, bindings).build()` builder
(structural validation built into `build()`), serialize with
`serializeScript` for canonical bytes, and still run `lintScript`
before publishing. Recommended pipeline: **TS lint at authoring time
→ Java CLI at publish time → runtime enforcement at execution time.**

## Your first host end-to-end

One concrete mini-host — `notes-host`, one read (`getNote`) + one
write (`saveNote`) — from `.tsp` to a passing C# integration test.
Every step below was run as written.

**1. Define** — the `notes-host.tsp` from
[B.1](#b1-write-your-host-library) above, verbatim.

**2. Generate** (Path B):

```sh
cd projects/sqlitehost && pnpm install && pnpm -r run build
node codegen/manifest-emitter/dist/cli.js notes-host.tsp generated --base-name notes-host
node codegen/csharp-emitter/dist/cli.js generated/notes-host.manifest.json generated/csharp
```

The DDL snapshot now shows your workspace: `call_get_note(call_id,
input_key)`, `result_get_note(call_id, status, result_body,
result_found)`, `call_save_note(call_id, input_key, input_body)`,
`result_save_note(call_id, status, result_saved)`, plus the shared
`pending_host_calls` / `script_inputs` / `script_vars` tables
(`docs/workspace-schema.md`).

**3. Consumer project** — a test project `NotesHost.Tests` with:

- the five generated `*.g.cs` files under `Generated/` (skipping
  `envelope/`),
- `SqliteHost.Abstractions` + `SqliteHost.Runtime` +
  `SqliteHost.Conformance` referenced from a local feed
  ([C.1](#c1-nuget--the-c-trio)) — vendored sources (Path A) work
  identically,
- `NotesAdapter.cs`: a Microsoft.Data.Sqlite adapter copied from the
  reference shape
  `csharp/SqliteHost.Tests/Adapter/MicrosoftDataSqliteAdapter.cs`
  (in-memory factory; wraps `SqliteException` in
  `SqliteHostAdapterException` with the native error code),
- `NotesHandlers.cs`: the application logic —

```csharp
public sealed class NotesHandlers : IGeneratedHostHandlers
{
    public readonly Dictionary<string, string> Notes = new Dictionary<string, string>();

    public GetNoteResult GetNote(GetNoteInput input)
    {
        string body;
        bool found = Notes.TryGetValue(input.Key, out body);
        return new GetNoteResult { Body = found ? body : "", Found = found };
    }

    public SaveNoteResult SaveNote(SaveNoteInput input)
    {
        Notes[input.Key] = input.Body;
        return new SaveNoteResult { Saved = true };
    }
}
```

**4. The integration test** — a two-step script (write, then a
read-after-write step gated on the write's result row, per the pinned
lifecycle in `docs/architecture.md`):

```csharp
[Fact]
public void SaveThenRead_RoundTripsThroughTheWorkspace()
{
    var handlers = new NotesHandlers();
    var runtime = new SqliteHostRuntime<IGeneratedHostHandlers>(
        connectionFactory: new NotesWorkspaceFactory(),
        hostDefinition: GeneratedHostDefinition.Build(),
        handlers: handlers,
        options: new SqliteHostRuntimeOptions { EnableDiagnostics = true });

    var script = new SqliteHostScript
    {
        Engine = "sqlite-host-v1",
        ScriptId = "notes-hello-world",
        RequiredApiLevel = 1,
        RequiredFeatures = new List<string> { "typedNamedBindings", "splitResultTables" },
        RequiredMethods = new List<string> { "getNote", "saveNote" },
        Steps = new List<SqliteHostStep>
        {
            new SqliteHostStep
            {
                Id = "save",
                Statements = new List<SqliteHostStatement>
                {
                    new SqliteHostStatement
                    {
                        Sql = "INSERT INTO call_save_note (call_id, input_key, input_body)"
                            + " VALUES (:callId, :key, :body)",
                        Bindings = new Dictionary<string, SqliteHostBindingValue>
                        {
                            { "callId", SqliteHostBindingValue.Text("save-1") },
                            { "key", SqliteHostBindingValue.Text("greeting") },
                            { "body", SqliteHostBindingValue.Text("hello sqlitehost") }
                        }
                    }
                }
            },
            new SqliteHostStep
            {
                Id = "read-back",
                Statements = new List<SqliteHostStatement>
                {
                    new SqliteHostStatement
                    {
                        Sql = "INSERT INTO call_get_note (call_id, input_key)"
                            + " SELECT :callId, 'greeting' WHERE EXISTS (SELECT 1"
                            + " FROM result_save_note WHERE call_id = 'save-1'"
                            + " AND status = 'done' AND result_saved = 1)",
                        Bindings = new Dictionary<string, SqliteHostBindingValue>
                        {
                            { "callId", SqliteHostBindingValue.Text("read-1") }
                        }
                    }
                }
            }
        }
    };

    SqliteHostRunResult result = runtime.Run(script);

    Assert.Equal(SqliteHostRunStatus.Completed, result.Status);
    Assert.Equal(2, result.ExecutedCallCount);
    Assert.Equal("hello sqlitehost", handlers.Notes["greeting"]);
}
```

Note the read lives in a **separate step**: results are written when
a step's drain runs, so read-after-write is always an explicit next
step (this is exactly what the `result-read-not-after-call` lint
enforces). The same pattern at larger scale is
`csharp/SqliteHost.Tests/IntegrationFixtureTests.cs` running every
`fixtures/payloads/valid/` payload across three adapters.

**5. Prove the adapter too** — one subclass, 23 inherited tests:

```csharp
public class NotesAdapterConformanceTests : AdapterConformanceTestsBase
{
    protected override ISqliteHostConnection OpenAdapterConnection()
        => NotesConnection.OpenInMemory();
}
```

**6. Run** — `dotnet test` → **24 passed** (1 integration + 23
conformance). And close the loop with Path D: the same script as a
JSON payload passes both gates —

```sh
java/bin/sqlite-host-validate generated/notes-host.manifest.json hello.json   # exit 0
# TS: lintScript(payload, parseHostManifest(...)) -> [] , isPublishable -> true
```

You now have every moving part of a real integration: a TypeSpec
contract, generated bindings, handlers, a conformance-proven adapter,
a validated script, and a green end-to-end test.
