#!/usr/bin/env node
/**
 * Measures one row of the Unity IL2CPP app-size matrix
 * (docs/guides/il2cpp-size-protocol.md §4).
 *
 * Reads the APK the editor produced plus the batchmode log, checks the
 * row's validity output, extracts the two files the protocol measures
 * (`lib/arm64-v8a/libil2cpp.so` and `global-metadata.dat`), gzip -9's both,
 * and writes one JSON file per row for summarize-il2cpp.mjs to combine.
 *
 * A row whose validity output is wrong measured something other than a
 * working runtime, so its bytes are worthless — that fails here rather
 * than quietly entering the table.
 *
 * Usage:
 *   node tests/app-size-bench/measure-il2cpp.mjs <row> --apk <path> \
 *        --log <path> --out <dir>
 */

import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";

const HERE = dirname(fileURLToPath(import.meta.url));
const ROWS = JSON.parse(readFileSync(join(HERE, "il2cpp-rows.json"), "utf8"));

const arg = (name, fallback) => {
  const i = process.argv.indexOf(`--${name}`);
  return i > -1 ? process.argv[i + 1] : fallback;
};

const rowNumber = Number(process.argv[2]);
const spec = ROWS.find((r) => r.row === rowNumber);
if (!spec) {
  console.error(`unknown row ${process.argv[2]} — see tests/app-size-bench/il2cpp-rows.json`);
  process.exit(2);
}
const apk = resolve(arg("apk"));
const logPath = arg("log");
const outDir = resolve(arg("out", join(HERE, "out", "il2cpp")));

// --- validity check -------------------------------------------------------
// The editor logs the bench output between SB_VALIDATE_BEGIN/END markers.
const log = readFileSync(logPath, "utf8");
const match = /SB_VALIDATE_BEGIN\r?\n([\s\S]*?)\r?\nSB_VALIDATE_END/.exec(log);
if (!match) {
  console.error(`row ${rowNumber}: no SB_VALIDATE block in ${logPath} — the editor never ran the bench`);
  process.exit(1);
}
const printed = match[1].trim().split(/\r?\n/);

if (spec.validate === "bench") {
  const want = ["104006", String(spec.ddl), "Completed", "Completed"];
  if (printed.join("|") !== want.join("|")) {
    console.error(
      `row ${rowNumber} (${spec.label}): bench printed ${JSON.stringify(printed)}, expected ` +
        `${JSON.stringify(want)} — invalid row, its bytes do not count`,
    );
    process.exit(1);
  }
} else if (spec.validate === "game" && printed[0] !== "104006") {
  console.error(`row ${rowNumber} (${spec.label}): baseline printed ${JSON.stringify(printed)}, expected 104006`);
  process.exit(1);
}
// The probe rows have no fixed expected value; the protocol only requires
// that the pair agree, which summarize-il2cpp.mjs checks across rows.

// --- extract and size -----------------------------------------------------
const MEMBERS = {
  libil2cpp: "lib/arm64-v8a/libil2cpp.so",
  globalMetadata: "assets/bin/Data/Managed/Metadata/global-metadata.dat",
};

const staging = mkdtempSync(join(tmpdir(), "il2cpp-row-"));
const sizes = {};
try {
  for (const [key, member] of Object.entries(MEMBERS)) {
    execFileSync("unzip", ["-q", "-o", apk, member, "-d", staging], { stdio: "inherit" });
    const extracted = join(staging, member);
    const bytes = readFileSync(extracted);
    sizes[key] = { raw: statSync(extracted).size, gz: gzipSync(bytes, { level: 9 }).length };
  }
} finally {
  rmSync(staging, { recursive: true, force: true });
}

const result = {
  ...spec,
  apkBytes: statSync(apk).size,
  ...sizes,
  // What the protocol reports as "the download cost": the two IL2CPP
  // artifacts together, raw and compressed.
  total: {
    raw: sizes.libil2cpp.raw + sizes.globalMetadata.raw,
    gz: sizes.libil2cpp.gz + sizes.globalMetadata.gz,
  },
  validity: printed,
};

mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, `row${rowNumber}.json`), `${JSON.stringify(result, null, 2)}\n`);
console.log(
  `row ${rowNumber} (${spec.label}): libil2cpp ${result.libil2cpp.raw.toLocaleString()} B ` +
    `(gz ${result.libil2cpp.gz.toLocaleString()}), metadata ${result.globalMetadata.raw.toLocaleString()} B ` +
    `(gz ${result.globalMetadata.gz.toLocaleString()})`,
);
