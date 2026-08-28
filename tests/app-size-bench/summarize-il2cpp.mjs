#!/usr/bin/env node
/**
 * Combines the per-row JSON that measure-il2cpp.mjs (Android) or
 * measure-ios.mjs (iOS) wrote into the matrix table
 * docs/guides/il2cpp-size-protocol.md §5 asks for, and reports the
 * architectural shape claims the protocol makes.
 *
 * This is a MEASUREMENT, not a gate, and exits 0 even when a claim looks
 * violated. IL2CPP byte counts move with the editor patch, the NDK and the
 * engine itself, so a numeric red here would usually be about Unity rather
 * than about this repository. The numeric regression gate lives on the
 * NativeAOT half (measure-nativeaot.mjs), which is deterministic and two
 * orders of magnitude cheaper.
 *
 * Rows carry a `platform` field ("ios") and a `unityHost` field
 * ("linux"/"macos"); Android rows carry neither, and are read as
 * platform "android" on a single unnamed host. When rows carry more than
 * one host, every row appears once per host and the last section of the
 * report is the host-to-host delta — the whole reason both hosts are
 * built (§7 of the protocol).
 *
 * A directory that mixes platforms, or that mixes hosted and unhosted rows
 * of one platform, is refused with a non-zero exit instead of summarized:
 * those rows measure different quantities, so no table combining them means
 * anything. That refusal is about incoherent input, not about a claim — a
 * claim that looks violated is still only reported.
 *
 * Usage: node tests/app-size-bench/summarize-il2cpp.mjs <dir-of-row-json>
 */

import { appendFileSync, readFileSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";

const dir = resolve(process.argv[2] ?? "tests/app-size-bench/out/il2cpp");
// Which file each row was read from, so a refusal below can name the files
// to move instead of leaving the reader to work out which rows are the odd
// ones out.
const fileOf = new Map();
const rows = readdirSync(dir)
  // `row<n>.json` is what the Android script writes; the iOS script adds
  // the Unity host, because both hosts measure the same row number and
  // the artifacts are merged into one directory.
  .filter((f) => /^row\d+(-[a-z0-9]+)?\.json$/.test(f))
  .map((f) => {
    const row = JSON.parse(readFileSync(join(dir, f), "utf8"));
    fileOf.set(row, f);
    return row;
  })
  .sort((a, b) => a.row - b.row || String(a.unityHost ?? "").localeCompare(String(b.unityHost ?? "")));

if (rows.length === 0) {
  console.error(`no row JSON under ${dir}`);
  process.exit(1);
}

const kb = (n) => `${(n / 1024).toFixed(1)} KB`;
const signed = (n) => `${n > 0 ? "+" : ""}${kb(n)}`;
// Host-comparison deltas only. Rounding a 300 B disagreement to "+0.0 KB"
// makes the table read as all-zero while the verdict underneath says the
// hosts differ, and il2cppOnly is byte-granular precisely so small
// differences are visible.
const signedFine = (n) =>
  Math.abs(n) < 1024 ? `${n > 0 ? "+" : ""}${n} B` : signed(n);
const hostOf = (r) => r.unityHost ?? null;
const platformOf = (r) => r.platform ?? "android";
// The first measured member: Android weighs libil2cpp.so, iOS the
// UnityFramework Mach-O. They are not the same quantity — UnityFramework
// carries the engine too — which is why the iOS table also prints
// il2cppOnly and why the two tables must never be read side by side as a
// platform comparison.
const member = (r) => (platformOf(r) === "ios" ? r.unityFramework : r.libil2cpp);

// --- refuse incoherent inputs ---------------------------------------------
// Android's `total` weighs libil2cpp.so; iOS's weighs UnityFramework, which
// links the whole engine into the same Mach-O. Subtracting one from the other
// produces a number that means nothing, and the filename regex above matches
// both scripts' output, so merging two artifact directories reaches this.
const platforms = [...new Set(rows.map(platformOf))].sort();
if (platforms.length > 1) {
  const listing = platforms
    .map((p) => `${p} (${rows.filter((r) => platformOf(r) === p).map((r) => fileOf.get(r)).join(", ")})`)
    .join("; ");
  console.error(`${dir} mixes ${platforms.length} platforms: ${listing}`);
  console.error(
    "Android and iOS bytes are different quantities and no table may combine them. " +
      "Summarize one platform per directory.",
  );
  process.exit(1);
}

// A row without `unityHost` is not a host — it is a row that never recorded
// one. Treating null as a host name makes it the reference of a "host
// comparison" whose reference column is literally `null`.
const hosts = [...new Set(rows.map(hostOf))];
if (hosts.length > 1 && hosts.includes(null)) {
  const named = hosts.filter((h) => h !== null).join(", ");
  const unhosted = rows.filter((r) => hostOf(r) === null).map((r) => fileOf.get(r)).join(", ");
  console.error(
    `${dir} mixes rows that name a Unity host (${named}) with rows that record none (${unhosted}).`,
  );
  console.error(
    "Those rows cannot be attributed to a host, and dropping them silently would hide them. " +
      "Summarize them separately.",
  );
  process.exit(1);
}

// The env fields measure-ios.mjs stamps into every row precisely so that the
// host comparison can be checked instead of asserted. Android rows carry no
// `env` at all, so every field reads null on every row, agrees with itself,
// and produces no note.
const TOOLCHAIN = [
  ["unityVersion", "Unity editor version"],
  ["xcode", "Xcode version"],
  ["xcodeBuild", "Xcode build number"],
  ["iosSdk", "iOS SDK"],
];
const envField = (r, field) => r.env?.[field] ?? null;
const showEnv = (v) => (v === null ? "unset" : `\`${v}\``);

/** The toolchain fields `group` does NOT agree on, with the values seen. */
const toolchainDisagreements = (group) =>
  TOOLCHAIN.map(([field, human]) => ({
    field,
    human,
    values: [...new Set(group.map((r) => envField(r, field)))],
  })).filter((d) => d.values.length > 1);

/** One platform table plus the shape-claim notes for one group of rows. */
function section(group) {
  const platform = platformOf(group[0]);
  const ios = platform === "ios";
  const host = hostOf(group[0]);
  const by = (label) => group.find((r) => r.label === label);
  const baseline = by("baseline");

  const title = ios ? "### Unity IL2CPP app size (iOS / arm64)" : "### Unity IL2CPP app size (Android / ARM64)";
  const lines = [host === null ? title : `${title} — Unity host: ${host}`, ""];
  if (ios) {
    lines.push(
      "**These bytes are not comparable to the Android table.** `UnityFramework` links the whole Unity " +
        "engine into the same Mach-O, while Android's `libil2cpp.so` excludes it; the BCL profile and the " +
        "C++ compiler also differ. Row-minus-baseline deltas within this table are the result; `il2cppOnly` " +
        "is the only column that is the same *kind* of quantity as the Android one.",
      "",
      "`UnityFramework` is a Mach-O whose segments are page-aligned to 16 KB, so its file size moves in " +
        "16,384 B steps: it carries up to a page of padding, and a change smaller than a page need not move it " +
        "at all. `total raw` sums that padded size with the byte-exact `global-metadata.dat`, so every Δraw " +
        "here inherits the same 16 KB of slack in its `UnityFramework` half. `il2cppOnly` comes from the link " +
        "map at byte granularity, so the claim verdicts under the table are computed from it, and say so when " +
        "it is missing.",
      "",
    );
  }
  lines.push(
    baseline
      ? "Deltas are over the `baseline` row (GameWork only, no SqliteHost sources)."
      : "**No baseline row in this run** — absolute sizes only.",
    "",
  );

  if (ios) {
    lines.push(
      "| row | UnityFramework | global-metadata.dat | il2cppOnly | total raw | total gz | Δraw | Δgz | Δil2cppOnly |",
      "|---|---:|---:|---:|---:|---:|---:|---:|---:|",
    );
  } else {
    lines.push(
      "| row | libil2cpp.so | global-metadata.dat | total raw | total gz | Δraw | Δgz |",
      "|---|---:|---:|---:|---:|---:|---:|",
    );
  }
  for (const r of group) {
    const d =
      baseline && r.label !== "baseline"
        ? { raw: r.total.raw - baseline.total.raw, gz: r.total.gz - baseline.total.gz }
        : null;
    const head =
      `| ${r.label} | ${member(r).raw.toLocaleString()} | ${r.globalMetadata.raw.toLocaleString()} | ` +
      (ios ? `${r.il2cppOnly === null || r.il2cppOnly === undefined ? "—" : r.il2cppOnly.toLocaleString()} | ` : "");
    const tail = ios
      ? ` ${
          d && baseline.il2cppOnly != null && r.il2cppOnly != null ? signed(r.il2cppOnly - baseline.il2cppOnly) : "—"
        } |`
      : "";
    lines.push(
      `${head}${r.total.raw.toLocaleString()} | ${r.total.gz.toLocaleString()} | ` +
        `${d ? signed(d.raw) : "—"} | ${d ? signed(d.gz) : "—"} |${tail}`,
    );
  }

  // --- the shape claims (reported, never enforced) -------------------------
  const notes = [];

  // A partial re-run can mix toolchains inside a single host — rows 0-4 from
  // the original run, rows 5-11 from a re-run on a newer Xcode. Every delta
  // in this table subtracts one of those rows from another, so the mixing is
  // said out loud rather than left for the numbers to imply.
  for (const d of toolchainDisagreements(group)) {
    const byValue = new Map();
    for (const r of group) {
      const key = showEnv(envField(r, d.field));
      byValue.set(key, [...(byValue.get(key) ?? []), r.label]);
    }
    const listing = [...byValue].map(([value, labels]) => `${labels.join(", ")} = ${value}`).join("; ");
    notes.push(
      `MIXED TOOLCHAIN within this host: ${d.human} (\`env.${d.field}\`) is not the same for every row — ` +
        `${listing}. Rows built on different toolchains are tabulated, and differenced, together above, so ` +
        "the numbers are not a like-for-like comparison of the code under test.",
    );
  }

  const delta = (label) => {
    const r = by(label);
    return r && baseline ? { raw: r.total.raw - baseline.total.raw, gz: r.total.gz - baseline.total.gz } : null;
  };
  const perMethod = (profile) => {
    const big = delta(`${profile}50`);
    const small = delta(`${profile}5`);
    return big && small ? { raw: (big.raw - small.raw) / 45, gz: (big.gz - small.gz) / 45 } : null;
  };

  // Which quantity a verdict may be computed from differs by platform.
  // Android weighs two zip members, both byte-exact, so `total` is a faithful
  // basis. On iOS the UnityFramework half of `total` is a Mach-O file size
  // quantized to a 16 KB page, which is larger than several of the effects
  // being judged, so the iOS verdicts read `il2cppOnly` from the link map
  // instead — and where it is missing, print no verdict at all.
  const IOS_QUANTUM = 16384;
  const only = (label) => {
    const r = by(label);
    return r && r.il2cppOnly != null ? r.il2cppOnly : null;
  };
  const unresolvable = (what, labels) =>
    `${what}: UNRESOLVABLE — il2cppOnly is unavailable for ${labels.filter((l) => only(l) === null).join(", ")}, ` +
    `and the file size cannot stand in for it: UnityFramework's Mach-O size is quantized to ` +
    `${IOS_QUANTUM.toLocaleString()} B (16 KB), so a delta below that quantum may be padding rather than code.`;

  if (ios) {
    const iosPer = (profile) => {
      const big = only(`${profile}50`);
      const small = only(`${profile}5`);
      return big === null || small === null ? null : (big - small) / 45;
    };
    const perLabels = ["classic5", "classic50", "compact5", "compact50", "ultra5", "ultra50"];
    if (perLabels.every((l) => by(l))) {
      const classicOnly = iosPer("classic");
      const compactOnly = iosPer("compact");
      const ultraOnly = iosPer("ultra");
      if (classicOnly === null || compactOnly === null || ultraOnly === null) {
        notes.push(unresolvable("Marginal per-method, and the ultra-vs-compact claim resting on it", perLabels));
      } else {
        notes.push(
          `Marginal per-method, from il2cppOnly (byte-granular, not the 16 KB-quantized file size): ` +
            `classic ${classicOnly.toFixed(0)} B, compact ${compactOnly.toFixed(0)} B, ` +
            `ultra ${ultraOnly.toFixed(0)} B.`,
          ultraOnly < compactOnly
            ? "CLAIM HOLDS: ultra's marginal per-method cost stays below compact's."
            : "CLAIM IN DOUBT: ultra's marginal per-method cost is no longer below compact's — worth a look before the docs are trusted.",
        );
      }
    }
  } else {
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
  }
  for (const [full, slim] of [
    ["compact50", "compact50-slim"],
    ["ultra50", "ultra50-slim"],
  ]) {
    if (ios) {
      if (!by(full) || !by(slim)) continue;
      const fullOnly = only(full);
      const slimOnly = only(slim);
      if (fullOnly === null || slimOnly === null) {
        notes.push(unresolvable(`Whether SQLITEHOST_SLIM is a net win on ${full}`, [full, slim]));
        continue;
      }
      notes.push(
        slimOnly < fullOnly
          ? `CLAIM HOLDS: SQLITEHOST_SLIM is a net win on ${full} ` +
            `(${kb(fullOnly - slimOnly)} of il2cppOnly, byte-granular).`
          : `CLAIM IN DOUBT: SQLITEHOST_SLIM did not shrink ${full} ` +
            `(${signed(slimOnly - fullOnly)} of il2cppOnly, byte-granular).`,
      );
      continue;
    }
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
    if (ios) {
      const gvmOnly = only("probe-gvm");
      const nogvmOnly = only("probe-nogvm");
      notes.push(
        gvmOnly === null || nogvmOnly === null
          ? unresolvable("One generic virtual method (probe pair)", ["probe-gvm", "probe-nogvm"])
          : `One generic virtual method (probe pair): ${signed(gvmOnly - nogvmOnly)} of il2cppOnly ` +
            `(byte-granular, so a figure below the 16 KB file-size quantum still counts).`,
      );
    } else {
      notes.push(
        `One generic virtual method (probe pair): ${signed(gvm.total.raw - nogvm.total.raw)} raw / ` +
          `${signed(gvm.total.gz - nogvm.total.gz)} gz.`,
      );
    }
    if (gvm.validity.join("|") !== nogvm.validity.join("|")) {
      notes.push(
        `NOTE: the probe pair printed different values (${gvm.validity} vs ${nogvm.validity}); ` +
          "the protocol requires them to agree, so treat the delta with suspicion.",
      );
    }
  }
  if (ios && group.some((r) => r.il2cppOnly === null || r.il2cppOnly === undefined)) {
    const missing = group.filter((r) => r.il2cppOnly === null || r.il2cppOnly === undefined).map((r) => r.label);
    notes.push(
      `il2cppOnly is unavailable for ${missing.join(", ")} — it is reported missing rather than ` +
        "estimated, and each of those rows' JSON says which of the three reasons applied in its " +
        "`il2cppOnlyNote` field: no link map was found, the search for one matched more than once, " +
        "or a map that did resolve attributed nothing to the archive names this bench recognises.",
    );
  }
  if (group.length < 12) {
    notes.push(`Partial run: ${group.length} of 12 rows. Per-method slopes need the 5-method rows (9-11).`);
  }

  if (notes.length > 0) lines.push("", ...notes.map((n) => `- ${n}`));
  return lines;
}

/**
 * The point of building two Unity hosts: if the toolchain either side of the
 * Unity editor is the same, every byte of difference here is a property of
 * the host that generated the Xcode project.
 *
 * That premise is a fact about the run, not an axiom, so it is checked here
 * against the `env` fields measure-ios.mjs stamps into every row. When they
 * disagree the causal reading is withdrawn and the differing field named:
 * the delta is then at least partly a toolchain difference. When they are
 * unset the premise is reported as unverified rather than assumed.
 */
function hostComparison(all, hostList) {
  const [reference, ...others] = hostList;
  const lines = ["### Unity host comparison", ""];
  const disagreements = toolchainDisagreements(all);
  const unrecorded = TOOLCHAIN.filter(([field]) => all.every((r) => envField(r, field) === null));
  if (disagreements.length > 0) {
    // Name where each value came from: this fires both for two hosts on
    // different toolchains and for one host whose rows were re-run on a new
    // one, and the reader needs to know which.
    const listing = disagreements
      .map(({ field, human }) => {
        const perHost = hostList
          .map((h) => {
            const seen = [...new Set(all.filter((r) => hostOf(r) === h).map((r) => envField(r, field)))];
            return `${h} = ${seen.map(showEnv).join(" + ")}`;
          })
          .join(", ");
        return `${human} (\`env.${field}\`): ${perHost}`;
      })
      .join("; ");
    lines.push(
      `NOT a host comparison: the rows below did not all run the same toolchain — ${listing}. A non-zero ` +
        `delta is therefore at least partly a toolchain difference and cannot be read as a property of the ` +
        `Unity host. Fix the toolchain drift and re-run before drawing a conclusion from the table. ` +
        `Reference host: \`${reference}\`.`,
      "",
    );
  } else if (unrecorded.length > 0) {
    const missing = unrecorded.map(([field, human]) => `${human} (\`env.${field}\`)`).join(", ");
    lines.push(
      `Premise UNVERIFIED: the rows agree on every toolchain field they recorded, but ${missing} ` +
        `${unrecorded.length === 1 ? "is" : "are"} unset in every row, so this run cannot show that both ` +
        `hosts used the same toolchain. Read a non-zero delta below as "the Unity host or something ` +
        `unrecorded", not as a property of the host. Reference host: \`${reference}\`.`,
      "",
    );
  } else {
    const shown = TOOLCHAIN.map(([field, human]) => `${human} ${showEnv(envField(all[0], field))}`).join(", ");
    lines.push(
      `Checked against every row before comparing: the hosts agree on ${shown}; only the machine that ` +
        `generated the Xcode project differs. A non-zero delta below is therefore a property of the Unity ` +
        `host. Reference host: \`${reference}\`.`,
      "",
    );
  }
  const memberName = platformOf(all[0]) === "ios" ? "UnityFramework" : "libil2cpp.so";
  for (const other of others) {
    lines.push(
      `| row | ${reference} total raw | ${other} total raw | Δ raw | Δ gz | Δ ${memberName} | Δ il2cppOnly |`,
      "|---|---:|---:|---:|---:|---:|---:|",
    );
    const rowNumbers = [...new Set(all.map((r) => r.row))].sort((a, b) => a - b);
    const identical = [];
    const differing = [];
    const quantizedOnly = [];
    // Row numbers only one of the two hosts measured. They are printed in the
    // table with an em dash and cannot be differenced, so they are evidence of
    // nothing about the hosts — and when they are ALL of the rows there is no
    // comparison to draw a verdict from.
    const unshared = [];
    for (const number of rowNumbers) {
      const a = all.find((r) => r.row === number && hostOf(r) === reference);
      const b = all.find((r) => r.row === number && hostOf(r) === other);
      if (!a || !b) {
        unshared.push(number);
        lines.push(`| ${(a ?? b).label} | ${a ? a.total.raw.toLocaleString() : "—"} | ${b ? b.total.raw.toLocaleString() : "—"} | — | — | — | — |`);
        continue;
      }
      const dRaw = b.total.raw - a.total.raw;
      const dGz = b.total.gz - a.total.gz;
      const dMember = member(b).raw - member(a).raw;
      const dIl2cpp = a.il2cppOnly != null && b.il2cppOnly != null ? b.il2cppOnly - a.il2cppOnly : null;
      // `total` alone would call a row identical when all that agreed was the
      // 16 KB-quantized Mach-O size. il2cppOnly is byte-granular, so a row is
      // only identical when it agrees there too; where the link map did not
      // resolve, the row is classified on the quantized size and said to be.
      if (dIl2cpp === null && platformOf(a) === "ios") quantizedOnly.push(a.label);
      (dRaw === 0 && dGz === 0 && (dIl2cpp === null || dIl2cpp === 0) ? identical : differing).push(a.label);
      lines.push(
        `| ${a.label} | ${a.total.raw.toLocaleString()} | ${b.total.raw.toLocaleString()} | ` +
          `${dRaw === 0 ? "0" : signedFine(dRaw)} | ${dGz === 0 ? "0" : signedFine(dGz)} | ` +
          `${dMember === 0 ? "0" : signedFine(dMember)} | ${dIl2cpp === null ? "—" : dIl2cpp === 0 ? "0" : signedFine(dIl2cpp)} |`,
      );
    }
    lines.push(
      "",
      identical.length === 0 && differing.length === 0
        ? `- \`${reference}\` and \`${other}\` have no row number in common, so no row of one host can be ` +
          `differenced against the same row of the other and this comparison is impossible. ` +
          `${unshared.length} row number(s) were measured on only one of the two hosts. Nothing here says ` +
          "whether the hosts agree, so neither may be deleted from the matrix on this run."
        : differing.length === 0
          ? `- All ${identical.length} shared row(s) agree between \`${reference}\` and \`${other}\` on every ` +
            "quantity compared here: total raw, total gz, and il2cppOnly wherever the link map resolved it. " +
            "On this evidence the two hosts are interchangeable and one of them can be deleted from the matrix."
          : `- \`${reference}\` and \`${other}\` disagree on: ${differing.join(", ")} ` +
            `(${identical.length} row(s) identical). The hosts are NOT interchangeable — decide which one the ` +
            "published numbers come from before deleting either.",
      ...(quantizedOnly.length === 0
        ? []
        : [
            `- Compared on file size alone for: ${quantizedOnly.join(", ")} — il2cppOnly is unavailable there, ` +
              "and UnityFramework's Mach-O size is quantized to 16,384 B (16 KB), so a difference confined to " +
              "UnityFramework and smaller than that quantum need not show up in those rows at all.",
          ]),
      "",
    );
  }
  return lines;
}

const lines = [];
if (hosts.length <= 1) {
  lines.push(...section(rows));
} else {
  for (const host of hosts) {
    lines.push(...section(rows.filter((r) => hostOf(r) === host)), "");
  }
  lines.push(...hostComparison(rows, hosts));
}

const summary = lines.join("\n");
console.log(summary);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${summary}\n`);
