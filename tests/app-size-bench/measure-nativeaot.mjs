#!/usr/bin/env node
/**
 * NativeAOT half of the app-size bench: publish every row, measure it, and
 * gate on what the documentation claims.
 *
 * generate.mjs writes the sources; this publishes them and turns the
 * result into a verdict. Before it existed the numbers in
 * docs/guides/il2cpp-size-protocol.md §3, docs/compatibility.md and
 * docs/reports/il2cpp-size-report.md were prose nothing re-measured.
 *
 * Two layers of checking, deliberately separate:
 *
 *  1. SHAPE INVARIANTS (always enforced). Everything asserted here is a
 *     RATIO or an ORDERING computed inside this one run, so it is immune
 *     to .NET SDK patch drift and to which architecture the runner has:
 *     profiles must stay ordered, marginal per-method cost must fall from
 *     classic to compact to ultra, DTO fields must stay a no-op, SLIM must
 *     stay a net win, and the reflection-free build must still run.
 *
 *  2. BYTE REGRESSION (enforced only once baseline.json says `"gate":
 *     true` for this runtime identifier). Compares each row's delta over
 *     the gamebase baseline BUILT IN THE SAME RUN — never absolute bytes,
 *     because a same-run baseline cancels the SDK and arch differences an
 *     absolute threshold cannot. Re-record with UPDATE_SIZE_BASELINE=1.
 *
 * Usage:
 *   node tests/app-size-bench/measure-nativeaot.mjs [--rid linux-x64]
 *   UPDATE_SIZE_BASELINE=1 node tests/app-size-bench/measure-nativeaot.mjs
 */

import { execFileSync } from "node:child_process";
import { appendFileSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { gzipSync } from "node:zlib";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, "out");
const BASELINE_FILE = join(HERE, "baseline.json");

const ridArg = process.argv.indexOf("--rid");
const RID = ridArg > -1 ? process.argv[ridArg + 1] : defaultRid();

function defaultRid() {
  const arch = process.arch === "arm64" ? "arm64" : "x64";
  if (process.platform === "linux") return `linux-${arch}`;
  if (process.platform === "darwin") return `osx-${arch}`;
  return `win-${arch}`;
}

// name -> { project, props, expect }
//   expect: the four lines the bench prints (protocol §3). A row whose
//   output differs measured something other than a working runtime, so its
//   bytes mean nothing — that is a failure, not a footnote.
const ROWS = [
  { name: "gamebase", project: "out/nativeaot/gamebase/gamebase.csproj", expect: ["104006"] },
  { name: "classic50", project: "out/nativeaot/classic50/classic50.csproj", expect: bench(22231) },
  { name: "compact50", project: "out/nativeaot/compact50/compact50.csproj", expect: bench(22231) },
  { name: "compact50-fields", project: "out/nativeaot/compact50-fields/compact50-fields.csproj", expect: bench(22231) },
  { name: "ultra50", project: "out/nativeaot/ultra50/ultra50.csproj", expect: bench(22231) },
  { name: "classic5", project: "out/nativeaot/classic5/classic5.csproj", expect: bench(2771) },
  { name: "compact5", project: "out/nativeaot/compact5/compact5.csproj", expect: bench(2771) },
  { name: "ultra5", project: "out/nativeaot/ultra5/ultra5.csproj", expect: bench(2771) },
  // H-SLIM: same sources, the vendoring define flipped on the referenced
  // runtime project (csharp/SqliteHost.Runtime reads -p:SqliteHostSlim).
  {
    name: "compact50-slim",
    project: "out/nativeaot/compact50/compact50.csproj",
    props: ["-p:SqliteHostSlim=true"],
    expect: bench(22231),
  },
  // The "no reflection anywhere" guarantee docs/compatibility.md states as
  // a measured fact: the ILC option that makes reflection unavailable at
  // all. A build that needs reflection fails to compile or fails to run.
  //
  // Two accepted outputs, and the difference is the point: with reflection
  // disabled `Enum.ToString()` cannot look a name up, so the two status
  // lines print the ordinal instead of the name. 0 is
  // SqliteHostRunStatus.Completed — the run completed either way, which is
  // exactly the claim being kept honest. Anything else (an exception, a
  // non-zero status) is a real failure.
  {
    name: "compact50-noreflection",
    project: "out/nativeaot/compact50/compact50.csproj",
    props: ["-p:IlcDisableReflection=true"],
    expect: [bench(22231), ["104006", "22231", "0", "0"]],
    excludeFromBaseline: true,
  },
  { name: "probe-gvm", project: "probes/gvm/gvm.csproj" },
  { name: "probe-nogvm", project: "probes/nogvm/nogvm.csproj" },
];

function bench(ddlLength) {
  return ["104006", String(ddlLength), "Completed", "Completed"];
}

// ---------- publish + measure ----------

function publish(row) {
  const outDir = join(OUT, "measure", `${row.name}-${RID}`);
  execFileSync(
    "dotnet",
    [
      "publish", join(HERE, row.project),
      "-c", "Release",
      "-r", RID,
      "-o", outDir,
      "--nologo", "-v", "q",
      ...(row.props ?? []),
    ],
    { stdio: "inherit" },
  );
  const base = row.project.split("/").pop().replace(/\.csproj$/, "");
  const exe = join(outDir, process.platform === "win32" ? `${base}.exe` : base);
  const raw = statSync(exe).size;
  const gz = gzipSync(readFileSync(exe), { level: 9 }).length;
  return { exe, raw, gz };
}

const measured = new Map();
const failures = [];

for (const row of ROWS) {
  process.stdout.write(`\n=== ${row.name} (${RID}) ===\n`);
  const { exe, raw, gz } = publish(row);
  if (row.expect) {
    // `expect` is one accepted output, or a list of them.
    const accepted = Array.isArray(row.expect[0]) ? row.expect : [row.expect];
    const printed = execFileSync(exe, { encoding: "utf8" }).trim().split(/\r?\n/);
    if (!accepted.some((want) => printed.join("|") === want.join("|"))) {
      failures.push(
        `${row.name}: bench printed ${JSON.stringify(printed)}, expected one of ` +
          `${JSON.stringify(accepted)} — the row did not measure a working runtime`,
      );
    }
  }
  measured.set(row.name, { raw, gz });
  console.log(`  raw ${raw.toLocaleString()}  gz ${gz.toLocaleString()}`);
}

const base = measured.get("gamebase");
const delta = (name) => ({
  raw: measured.get(name).raw - base.raw,
  gz: measured.get(name).gz - base.gz,
});
const deltas = Object.fromEntries(
  ROWS.filter((r) => r.name !== "gamebase" && !r.name.startsWith("probe-") && !r.excludeFromBaseline)
    .map((r) => [r.name, delta(r.name)]),
);
// The probe pair is its own comparison: one generic virtual method,
// measured against its twin rather than against gamebase.
const probeDelta = {
  raw: measured.get("probe-gvm").raw - measured.get("probe-nogvm").raw,
  gz: measured.get("probe-gvm").gz - measured.get("probe-nogvm").gz,
};

// ---------- layer 1: shape invariants ----------

const perMethod = (profile) => ({
  raw: (deltas[`${profile}50`].raw - deltas[`${profile}5`].raw) / 45,
  gz: (deltas[`${profile}50`].gz - deltas[`${profile}5`].gz) / 45,
});
const perMethodCost = {
  classic: perMethod("classic"),
  compact: perMethod("compact"),
  ultra: perMethod("ultra"),
};

function invariant(ok, message) {
  if (!ok) failures.push(message);
}

invariant(
  deltas.classic50.raw > deltas.compact50.raw && deltas.compact50.raw > deltas.ultra50.raw,
  `profile ordering broke: classic50 Δ${deltas.classic50.raw} / compact50 Δ${deltas.compact50.raw} / ` +
    `ultra50 Δ${deltas.ultra50.raw} raw — the smaller profile must stay smaller at 50 methods`,
);
invariant(
  perMethodCost.classic.raw > perMethodCost.compact.raw &&
    perMethodCost.compact.raw > perMethodCost.ultra.raw,
  `marginal per-method cost stopped falling across profiles: classic ` +
    `${perMethodCost.classic.raw.toFixed(0)} B > compact ${perMethodCost.compact.raw.toFixed(0)} B > ` +
    `ultra ${perMethodCost.ultra.raw.toFixed(0)} B is the claim in docs/compatibility.md`,
);
// H-FIELDS measured exactly 0 under NativeAOT (8 bytes of build-ID noise).
// A kilobyte of headroom keeps that a no-op claim rather than a byte match.
invariant(
  Math.abs(deltas["compact50-fields"].raw - deltas.compact50.raw) < 4096,
  `DTO fields vs auto-properties moved ${deltas["compact50-fields"].raw - deltas.compact50.raw} raw bytes; ` +
    `the documented H-FIELDS result is zero effect under NativeAOT`,
);
invariant(
  deltas.compact50.raw - deltas["compact50-slim"].raw >= 8192,
  `SQLITEHOST_SLIM saved only ${deltas.compact50.raw - deltas["compact50-slim"].raw} raw bytes on compact50; ` +
    `docs/compatibility.md claims it is a clear net win (~28 KB when measured)`,
);
invariant(probeDelta.raw > 0, "the GVM probe pair is no longer ordered — check probes/gvm vs probes/nogvm");

// ---------- layer 2: byte regression vs the committed baseline ----------

const baseline = JSON.parse(readFileSync(BASELINE_FILE, "utf8"));
const recorded = baseline.runtimes?.[RID];
const drift = [];

if (process.env.UPDATE_SIZE_BASELINE === "1") {
  baseline.runtimes = baseline.runtimes ?? {};
  baseline.runtimes[RID] = { gate: true, deltas, probeDelta };
  writeFileSync(BASELINE_FILE, `${JSON.stringify(baseline, null, 2)}\n`);
  console.log(`\nbaseline.json updated for ${RID} (gate enabled).`);
} else if (recorded) {
  // Tolerance: 3% of the recorded delta, floored so the small rows do not
  // trip on a few kilobytes of compiler noise.
  const tolerance = (value, floor) => Math.max(Math.abs(value) * 0.03, floor);
  for (const [name, got] of Object.entries(deltas)) {
    const want = recorded.deltas?.[name];
    if (!want) continue;
    for (const metric of ["raw", "gz"]) {
      const allowed = tolerance(want[metric], metric === "raw" ? 10240 : 5120);
      const moved = got[metric] - want[metric];
      if (Math.abs(moved) > allowed) {
        const line =
          `${name} Δ${metric} moved ${moved > 0 ? "+" : ""}${moved.toLocaleString()} B ` +
          `(${want[metric].toLocaleString()} → ${got[metric].toLocaleString()}, tolerance ` +
          `±${Math.round(allowed).toLocaleString()})`;
        if (recorded.gate) failures.push(`app size regression: ${line}`);
        else drift.push(line);
      }
    }
  }
  if (!recorded.gate) {
    console.log(
      `\nbaseline.json has ${RID} recorded but "gate": false — differences are advisory. ` +
        `Promote it by running once with UPDATE_SIZE_BASELINE=1 on this runner and committing the result.`,
    );
  }
} else {
  drift.push(`no baseline recorded for ${RID} — run with UPDATE_SIZE_BASELINE=1 to record one`);
}

// ---------- report ----------

const report = {
  rid: RID,
  measuredAt: new Date().toISOString(),
  absolute: Object.fromEntries(measured),
  deltas,
  probeDelta,
  perMethodCost,
};
writeFileSync(join(OUT, "nativeaot-sizes.json"), `${JSON.stringify(report, null, 2)}\n`);

const kb = (n) => `${(n / 1024).toFixed(1)} KB`;
const lines = [
  `### NativeAOT app size (${RID})`,
  "",
  "Deltas are over the `gamebase` row built in the same run.",
  "",
  "| row | raw | gz | Δraw | Δgz |",
  "|---|---:|---:|---:|---:|",
];
for (const [name, size] of measured) {
  const d = name === "gamebase" ? null : { raw: size.raw - base.raw, gz: size.gz - base.gz };
  lines.push(
    `| ${name} | ${size.raw.toLocaleString()} | ${size.gz.toLocaleString()} | ` +
      `${d ? kb(d.raw) : "—"} | ${d ? kb(d.gz) : "—"} |`,
  );
}
lines.push(
  "",
  `Marginal per-method: classic ${perMethodCost.classic.raw.toFixed(0)} B raw / ` +
    `${perMethodCost.classic.gz.toFixed(0)} B gz, compact ${perMethodCost.compact.raw.toFixed(0)} / ` +
    `${perMethodCost.compact.gz.toFixed(0)}, ultra ${perMethodCost.ultra.raw.toFixed(0)} / ` +
    `${perMethodCost.ultra.gz.toFixed(0)}.`,
  `One generic virtual method (probe pair): ${kb(probeDelta.raw)} raw / ${kb(probeDelta.gz)} gz.`,
);
if (drift.length > 0) lines.push("", "**Advisory baseline differences**", "", ...drift.map((d) => `- ${d}`));

const summary = lines.join("\n");
console.log(`\n${summary}`);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary}\n`);
for (const d of drift) console.log(`::warning::${d}`);

if (failures.length > 0) {
  console.error("\napp-size bench FAILED:");
  for (const f of failures) console.error(`  - ${f}`);
  process.exit(1);
}
console.log("\nApp-size bench passed: shape invariants hold and every row ran its bench correctly.");
