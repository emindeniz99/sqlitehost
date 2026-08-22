#!/usr/bin/env node
/**
 * App-size bench generator (docs/guides/il2cpp-size-protocol.md).
 *
 * Produces, under out/ (gitignored), the exact source sets both toolchains
 * measure so NativeAOT and Unity IL2CPP numbers come from identical inputs.
 * Hosts are generated at TWO sizes — 50 methods and 5 methods — because a
 * single size can only measure the total delta; the pair separates the
 * fixed runtime cost from the marginal per-method cost:
 *
 *     per-method = (delta_50 - delta_5) / 45
 *
 *   out/bench-host.tsp / bench-host5.tsp          synthetic hosts (opN:
 *     + *.manifest.json                           key text + amount int64
 *                                                 -> ok bool), generated
 *                                                 through the repo toolchain
 *   out/gen/{classic,compact,ultra}/              emitter output, 50 methods
 *   out/gen/{classic,compact,ultra}-5/            emitter output, 5 methods
 *   out/gen/compact-fields/                       50-method compact with DTO
 *                                                 fields instead of
 *                                                 auto-properties (H-FIELDS)
 *   out/nativeaot/{gamebase,classic50,compact50,compact50-fields,ultra50,
 *                  classic5,compact5,ultra5}/     ready-to-publish .NET 8
 *                                                 NativeAOT console benches
 *   out/unity-src/{profile}[-5]/                  the same sources arranged
 *                                                 for vendoring into a Unity
 *                                                 project (see protocol doc)
 *
 * Usage: node generate.mjs        (from this directory; emitters must be
 *                                  built — pnpm -r build at the repo root)
 */

import { execFileSync } from "node:child_process";
import { cpSync, mkdirSync, readFileSync, rmSync, writeFileSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, "../..");            // repo root
const OUT = join(HERE, "out");
const node = process.execPath;
const gameWork = readFileSync(join(HERE, "GameWork.cs"), "utf8");

rmSync(OUT, { recursive: true, force: true });
mkdirSync(join(OUT, "gen"), { recursive: true });

// ---------- shared source templates ----------

function handlersClass(profile, methods) {
  const lines = ["sealed class H : Bench.Host.Generated.IGeneratedHostHandlers", "{"];
  for (let i = 0; i < methods; i++) {
    if (profile === "ultra") {
      lines.push(`    public SqliteHost.SqliteHostUltraResult Op${i}(SqliteHost.SqliteHostUltraCall call) { return new SqliteHost.SqliteHostUltraResult().SetBool("ok", true); }`);
    } else {
      lines.push(`    public Bench.Host.Generated.Op${i}Result Op${i}(Bench.Host.Generated.Op${i}Input input) { return new Bench.Host.Generated.Op${i}Result { Ok = true }; }`);
    }
  }
  lines.push("}");
  return lines.join("\n");
}

// BenchEntry is the single measured entry point: the console bench prints its
// lines, and a Unity runner logs the same string — identical code either way.
function benchEntry(withHost) {
  const body = withHost
    ? `
            var sb = new System.Text.StringBuilder();
            sb.Append(DummyGame.GameWork.RunAll(seed)).Append('\\n');
            var definition = Bench.Host.Generated.GeneratedHostDefinition.Build();
            sb.Append(definition.GenerateSchemaScript().Length).Append('\\n');
            var runtime = new SqliteHost.SqliteHostRuntime<Bench.Host.Generated.IGeneratedHostHandlers>(
                new Fac(), definition, new H(), new SqliteHost.SqliteHostRuntimeOptions());
            var script = new SqliteHost.SqliteHostScript
            {
                Engine = "sqlite-host-v1",
                RequiredApiLevel = 1,
                Steps = new System.Collections.Generic.List<SqliteHost.SqliteHostStep>
                {
                    new SqliteHost.SqliteHostStep
                    {
                        Id = "s",
                        Statements = new System.Collections.Generic.List<SqliteHost.SqliteHostStatement>
                        {
                            new SqliteHost.SqliteHostStatement
                            {
                                Sql = "INSERT INTO call_op0 (call_id, input_key, input_amount) VALUES (:c, x, 1)",
                                Bindings = new System.Collections.Generic.Dictionary<string, SqliteHost.SqliteHostBindingValue>
                                {
                                    ["c"] = SqliteHost.SqliteHostBindingValue.Text("1")
                                }
                            }
                        }
                    }
                }
            };
            sb.Append(runtime.Run(script).Status).Append('\\n');
            sb.Append(runtime.ValidateEnvironment().Status);
            return sb.ToString();`
    : `
            return DummyGame.GameWork.RunAll(seed).ToString();`;
  return `public static class BenchEntry
{
    // Expected output: 104006 / <ddl length> / Completed / Completed
    public static string Run(int seed)
    {${body}
    }
}`;
}

const fakeAdapter = `sealed class Row : SqliteHost.ISqliteHostRow
{
    public bool IsNull(int i) { return true; }
    public int GetInt32(int i) { return 0; }
    public long GetInt64(int i) { return 0; }
    public bool GetBool(int i) { return false; }
    public string GetText(int i) { return "3.19.3"; }
    public byte[] GetBlob(int i) { return System.Array.Empty<byte>(); }
    public float GetFloat32(int i) { return 0; }
    public double GetFloat64(int i) { return 0; }
}
sealed class Conn : SqliteHost.ISqliteHostConnection
{
    public void Execute(string sql, System.Collections.Generic.IReadOnlyList<SqliteHost.SqliteHostBinding> bindings) { }
    public System.Collections.Generic.IReadOnlyList<object> QueryRows(
        string sql,
        System.Collections.Generic.IReadOnlyList<SqliteHost.SqliteHostBinding> bindings,
        System.Func<SqliteHost.ISqliteHostRow, object> mapper)
    {
        var rows = new System.Collections.Generic.List<object>();
        if (sql.IndexOf("sqlite_version", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            rows.Add(mapper(new Row()));
        }
        return rows;
    }
    public void Dispose() { }
}
sealed class Fac : SqliteHost.ISqliteHostConnectionFactory
{
    public SqliteHost.ISqliteHostConnection OpenWorkspace() { return new Conn(); }
}`;

const csprojWithHost = `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <StripSymbols>true</StripSymbols>
    <InvariantGlobalization>true</InvariantGlobalization>
    <NoWarn>IL2104;IL3053</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../../../csharp/SqliteHost.Abstractions/SqliteHost.Abstractions.csproj" />
    <ProjectReference Include="../../../../../csharp/SqliteHost.Runtime/SqliteHost.Runtime.csproj" />
  </ItemGroup>
</Project>
`;
const csprojBare = csprojWithHost.replace(/  <ItemGroup>[\s\S]*<\/ItemGroup>\n/, "");

const mainCs = `static class Program
{
    static void Main(string[] args)
    {
        System.Console.WriteLine(BenchEntry.Run(args.Length + 7));
    }
}`;

// ---------- per-size generation (tsp -> manifest -> C# per profile) ----------

function generateHost(methods, tag /* "" for 50, "5" for 5 */) {
  const baseName = tag ? `bench-host${tag}` : "bench-host";
  const tsp = [];
  tsp.push('import "@sqlite-host/typespec";', "", "using SqliteHost;", "", "namespace Bench.Host;", "");
  tsp.push("@hostLibrary({", "  apiLevel: 1", "})");
  tsp.push("interface BenchHostMethods {");
  for (let i = 0; i < methods; i++) {
    tsp.push(`  @hostMethod({ name: "op${i}", handler: "Op${i}" })`);
    tsp.push(`  op Op${i}(input: Op${i}Input): Op${i}Result;`);
    tsp.push("");
  }
  tsp.push("}");
  tsp.push("");
  for (let i = 0; i < methods; i++) {
    tsp.push(`model Op${i}Input {`, "  key: string;", "  amount: int64;", "}", "");
    tsp.push(`model Op${i}Result {`, "  ok: boolean;", "}", "");
  }
  writeFileSync(join(OUT, `${baseName}.tsp`), tsp.join("\n"));

  execFileSync(node, [
    join(ROOT, "codegen/manifest-emitter/dist/cli.js"),
    join(OUT, `${baseName}.tsp`), OUT, "--base-name", baseName,
  ], { stdio: "inherit" });

  const dirSuffix = tag ? `-${tag}` : "";
  for (const profile of ["classic", "compact", "ultra"]) {
    execFileSync(node, [
      join(ROOT, "codegen/csharp-emitter/dist/cli.js"),
      join(OUT, `${baseName}.manifest.json`), join(OUT, "gen", profile + dirSuffix),
      "--profile", profile,
    ], { stdio: "inherit" });
  }
}

generateHost(50, "");
generateHost(5, "5");

// H-FIELDS variant: 50-method compact with the emitter's --dto-fields flag
// (public fields instead of auto-properties).
execFileSync(node, [
  join(ROOT, "codegen/csharp-emitter/dist/cli.js"),
  join(OUT, "bench-host.manifest.json"), join(OUT, "gen/compact-fields"),
  "--profile", "compact", "--dto-fields",
], { stdio: "inherit" });

// ---------- assemble NativeAOT bench projects ----------

function writeBench(name, genDir /* null for gamebase */, profile, methods) {
  const dir = join(OUT, "nativeaot", name);
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, "GameWork.cs"), gameWork);
  writeFileSync(join(dir, `${name}.csproj`), genDir ? csprojWithHost : csprojBare);
  if (genDir) {
    for (const f of readdirSync(genDir)) {
      if (f.endsWith(".g.cs")) cpSync(join(genDir, f), join(dir, f));
    }
    writeFileSync(join(dir, "Bench.cs"),
      [fakeAdapter, handlersClass(profile, methods), benchEntry(true), mainCs].join("\n\n"));
  } else {
    writeFileSync(join(dir, "Bench.cs"), [benchEntry(false), mainCs].join("\n\n"));
  }
}

writeBench("gamebase", null);
writeBench("classic50", join(OUT, "gen/classic"), "classic", 50);
writeBench("compact50", join(OUT, "gen/compact"), "classic", 50);
writeBench("compact50-fields", join(OUT, "gen/compact-fields"), "classic", 50);
writeBench("ultra50", join(OUT, "gen/ultra"), "ultra", 50);
writeBench("classic5", join(OUT, "gen/classic-5"), "classic", 5);
writeBench("compact5", join(OUT, "gen/compact-5"), "classic", 5);
writeBench("ultra5", join(OUT, "gen/ultra-5"), "ultra", 5);

// ---------- Unity vendoring source sets ----------

function writeUnitySrc(name, genDir, profile, methods) {
  const dir = join(OUT, "unity-src", name);
  mkdirSync(dir, { recursive: true });
  for (const f of readdirSync(genDir)) {
    if (f.endsWith(".g.cs")) cpSync(join(genDir, f), join(dir, f));
  }
  writeFileSync(join(dir, "GameWork.cs"), gameWork);
  writeFileSync(join(dir, "Bench.cs"),
    [fakeAdapter, handlersClass(profile, methods), benchEntry(true)].join("\n\n"));
  writeFileSync(join(dir, "README.txt"),
    "Vendor this folder + csharp/SqliteHost.Abstractions + csharp/SqliteHost.Runtime\n" +
    "into Assets/, add a MonoBehaviour that calls BenchEntry.Run(7) once and logs it,\n" +
    "then follow docs/guides/il2cpp-size-protocol.md.\n");
}

writeUnitySrc("classic", join(OUT, "gen/classic"), "classic", 50);
writeUnitySrc("compact", join(OUT, "gen/compact"), "classic", 50);
writeUnitySrc("ultra", join(OUT, "gen/ultra"), "ultra", 50);
writeUnitySrc("classic-5", join(OUT, "gen/classic-5"), "classic", 5);
writeUnitySrc("compact-5", join(OUT, "gen/compact-5"), "classic", 5);
writeUnitySrc("ultra-5", join(OUT, "gen/ultra-5"), "ultra", 5);

console.log("bench sources generated under", OUT);
