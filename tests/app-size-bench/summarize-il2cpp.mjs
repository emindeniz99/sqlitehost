#!/usr/bin/env node
/**
 * Combines the per-row JSON that measure-il2cpp.mjs wrote into the matrix
 * table docs/guides/il2cpp-size-protocol.md §5 asks for, and reports the
 * two architectural shape claims the protocol makes.
 *
 * This is a MEASUREMENT, not a gate, and exits 0 even when a claim looks
 * violated. IL2CPP byte counts move with the editor patch, the NDK and the
 * engine itself, so a numeric red here would usually be about Unity rather
 * than about this repository. The numeric regression gate lives on the
 * NativeAOT half (measure-nativeaot.mjs), which is deterministic and two
 * orders of magnitude cheaper.
 *
 * Usage: node tests/app-size-bench/summarize-il2cpp.mjs <dir-of-row-json>
 */

import { appendFileSync, readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";

const dir = resolve(process.argv[2] ?? "tests/app-size-bench/out/il2cpp");
const rows = readdirSync(dir)
  .filter((f) => /^row\d+\.json$/.test(f))
  .map((f) => JSON.parse(readFileSync(join(dir, f), "utf8")))
  .sort((a, b) => a.row - b.row);

if (rows.length === 0) {
  console.error(`no row JSON under ${dir}`);
  process.exit(1);
}

const by = (label) => rows.find((r) => r.label === label);
const baseline = by("baseline");
const kb = (n) => `${(n / 1024).toFixed(1)} KB`;
const signed = (n) => `${n > 0 ? "+" : ""}${kb(n)}`;

const lines = [
  "### Unity IL2CPP app size (Android / ARM64)",
  "",
  baseline
    ? "Deltas are over the `baseline` row (GameWork only, no SqliteHost sources)."
    : "**No baseline row in this run** — absolute sizes only.",
  "",
  "| row | libil2cpp.so | global-metadata.dat | total raw | total gz | Δraw | Δgz |",
  "|---|---:|---:|---:|---:|---:|---:|",
];
for (const r of rows) {
  const d = baseline && r.label !== "baseline"
    ? { raw: r.total.raw - baseline.total.raw, gz: r.total.gz - baseline.total.gz }
    : null;
  lines.push(
    `| ${r.label} | ${r.libil2cpp.raw.toLocaleString()} | ${r.globalMetadata.raw.toLocaleString()} | ` +
      `${r.total.raw.toLocaleString()} | ${r.total.gz.toLocaleString()} | ` +
      `${d ? signed(d.raw) : "—"} | ${d ? signed(d.gz) : "—"} |`,
  );
}

// --- the two shape claims (reported, never enforced) ----------------------
const notes = [];
const delta = (label) => {
  const r = by(label);
  return r && baseline ? { raw: r.total.raw - baseline.total.raw, gz: r.total.gz - baseline.total.gz } : null;
};
const perMethod = (profile) => {
  const big = delta(`${profile}50`);
  const small = delta(`${profile}5`);
  return big && small ? { raw: (big.raw - small.raw) / 45, gz: (big.gz - small.gz) / 45 } : null;
};

const compactPer = perMethod("compact");
const ultraPer = perMethod("ultra");
const classicPer = perMethod("classic");
if (classicPer && compactPer && ultraPer) {
  notes.push(
    `Marginal per-method: classic ${classicPer.raw.toFixed(0)} B raw / ${classicPer.gz.toFixed(0)} B gz, ` +
      `compact ${compactPer.raw.toFixed(0)} / ${compactPer.gz.toFixed(0)}, ` +
      `ultra ${ultraPer.raw.toFixed(0)} / ${ultraPer.gz.toFixed(0)}.`,
    ultraPer.raw < compactPer.raw
      ? "CLAIM HOLDS: ultra's marginal per-method cost stays below compact's."
      : "CLAIM IN DOUBT: ultra's marginal per-method cost is no longer below compact's — worth a look before the docs are trusted.",
  );
}
for (const [full, slim] of [["compact50", "compact50-slim"], ["ultra50", "ultra50-slim"]]) {
  const a = delta(full);
  const b = delta(slim);
  if (!a || !b) continue;
  notes.push(
    b.raw < a.raw
      ? `CLAIM HOLDS: SQLITEHOST_SLIM is a net win on ${full} (${kb(a.raw - b.raw)} raw / ${kb(a.gz - b.gz)} gz).`
      : `CLAIM IN DOUBT: SQLITEHOST_SLIM did not shrink ${full} (${signed(b.raw - a.raw)} raw).`,
  );
}
const gvm = by("probe-gvm");
const nogvm = by("probe-nogvm");
if (gvm && nogvm) {
  notes.push(
    `One generic virtual method (probe pair): ${signed(gvm.total.raw - nogvm.total.raw)} raw / ` +
      `${signed(gvm.total.gz - nogvm.total.gz)} gz.`,
  );
  if (gvm.validity.join("|") !== nogvm.validity.join("|")) {
    notes.push(
      `NOTE: the probe pair printed different values (${gvm.validity} vs ${nogvm.validity}); ` +
        "the protocol requires them to agree, so treat the delta with suspicion.",
    );
  }
}
if (rows.length < 12) {
  notes.push(`Partial run: ${rows.length} of 12 rows. Per-method slopes need the 5-method rows (9-11).`);
}

if (notes.length > 0) lines.push("", ...notes.map((n) => `- ${n}`));

const summary = lines.join("\n");
console.log(summary);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary}\n`);
