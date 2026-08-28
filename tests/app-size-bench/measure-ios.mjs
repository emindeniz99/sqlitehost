#!/usr/bin/env node
/**
 * Measures one row of the Unity IL2CPP app-size matrix on iOS
 * (docs/guides/il2cpp-size-protocol.md §7).
 *
 * The Mac-side sibling of measure-il2cpp.mjs. Same contract: check the
 * row's validity output first, weigh the two artifacts the protocol
 * measures raw and `gzip -9`, sum them as `total`, write one JSON per row
 * for summarize-il2cpp.mjs. A row whose validity output is wrong measured
 * something other than a working runtime, so its bytes are worthless —
 * that fails here rather than quietly entering the table.
 *
 * Four things differ from the Android script, all forced by the platform:
 *
 *   1. It walks a BUILT PRODUCT DIRECTORY instead of unzipping an APK.
 *      Unity's iOS build emits an Xcode project; xcodebuild turns it into
 *      an .app, and that .app is what this reads.
 *   2. It DISCOVERS every path by searching and refuses to continue unless
 *      the search matched exactly once. The 2022.3 iOS layout is not
 *      verified anywhere in this repository, and a hardcoded path that
 *      stops matching is how a bench silently measures nothing. A miss
 *      prints the whole listing so the next run can be written against
 *      the real tree.
 *   3. It records `il2cppOnly` — the bytes the link map attributes to the
 *      IL2CPP archives. `unityFramework` contains the Unity engine as
 *      well; `il2cppOnly` is the only field that is the same KIND of
 *      quantity as Android's libil2cpp.so. When no link map is available
 *      the field is null and says why. It is never estimated.
 *   4. It VERIFIES the file it weighed. The protocol's unit is "the
 *      stripped, unsigned, single-slice arm64 Mach-O", which is three
 *      xcodebuild arguments away from being true of any given build, and
 *      none of the three fails visibly if it stops applying: a fat file
 *      weighs every slice it carries, an unstripped one carries symbol
 *      names that never ship, a signed one carries padding. All three are
 *      read back from the file's own load commands, and a row that is not
 *      the unit refuses to be written rather than entering the table.
 *
 * Usage:
 *   node tests/app-size-bench/measure-ios.mjs <row> \
 *     --product <built-products-dir> --derived <derived-data-dir> \
 *     --log <path> --out <dir> --host linux|macos \
 *     [--unity <version>] [--xcode <version>] [--xcode-build <build>] \
 *     [--sdk <version>] [--runner <label>]
 */

import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
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
const productDir = resolve(arg("product"));
const derivedDir = arg("derived") ? resolve(arg("derived")) : null;
const logPath = arg("log");
const outDir = resolve(arg("out", join(HERE, "out", "ios")));
const unityHost = arg("host");
if (unityHost !== "linux" && unityHost !== "macos") {
  console.error(`--host must be linux or macos (which machine ran the Unity editor), got ${unityHost}`);
  process.exit(2);
}

// --- validity check -------------------------------------------------------
// Deliberately the same gate as measure-il2cpp.mjs, copied rather than
// shared. Folding both scripts onto one module would edit the Android
// measurement path, which is the only Unity measurement in this repository
// that has ever run green, to serve a workflow that has never run at all.
// Fold them together once the iOS leg is proven; until then the two copies
// must be changed together.
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

// --- discovery ------------------------------------------------------------
// Never follows a symlink. A framework that points out of the tree is not
// a file this bench may weigh, and following one is also how a walk finds
// its own root again.
function* walk(root) {
  const stack = [root];
  while (stack.length > 0) {
    const dir = stack.pop();
    let entries;
    try {
      entries = readdirSync(dir, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const entry of entries) {
      const path = join(dir, entry.name);
      if (entry.isSymbolicLink()) continue;
      if (entry.isDirectory()) {
        stack.push(path);
        yield { path, directory: true };
      } else if (entry.isFile()) {
        yield { path, directory: false };
      }
    }
  }
}

const LAYOUT_EVIDENCE =
  "Nothing is guessed here on purpose: the Unity 2022.3 iOS output layout is not verified in this " +
  "repository, so a search that does not resolve to one file means the layout moved and the row " +
  "must be re-read before its bytes mean anything. The listing above is the evidence to write the " +
  "next version of this script against.";

// `why` is the paragraph printed under the candidate listing. It defaults to
// the path-discovery explanation because that is what most callers are doing;
// the Mach-O checks below pass their own, because "the layout moved" is not
// true of a file that resolved perfectly and simply is not the unit.
const exactlyOne = (what, matches, where, why = LAYOUT_EVIDENCE) => {
  if (matches.length === 1) return matches[0];
  console.error(`row ${rowNumber}: expected exactly one ${what} under ${where}, found ${matches.length}.`);
  for (const path of matches.slice(0, 40)) console.error(`  candidate: ${path}`);
  console.error(why);
  process.exit(1);
};

const productEntries = [...walk(productDir)];
const appBundles = productEntries
  .filter((e) => e.directory && e.path.endsWith(".app"))
  .map((e) => e.path)
  // Xcode also leaves a standalone copy of each framework beside the .app;
  // only the bundle itself is the shipped shape, and a bundle nested in
  // another bundle is not a second candidate.
  .filter((path, _i, all) => !all.some((other) => other !== path && path.startsWith(`${other}/`)));
const app = exactlyOne(".app bundle", appBundles, productDir);

const appEntries = [...walk(app)];
const unityFrameworkPath = exactlyOne(
  "UnityFramework Mach-O",
  appEntries
    .filter((e) => !e.directory && basename(e.path) === "UnityFramework")
    .filter((e) => basename(dirname(e.path)) === "UnityFramework.framework")
    .map((e) => e.path),
  app,
);
const globalMetadataPath = exactlyOne(
  "global-metadata.dat",
  appEntries.filter((e) => !e.directory && basename(e.path) === "global-metadata.dat").map((e) => e.path),
  app,
);

// Context, not a result: the whole bundle also carries icons, storyboards
// and the engine's data files, which is exactly why the protocol measures
// two files instead of an app.
const appBytes = appEntries.reduce((sum, e) => (e.directory ? sum : sum + statSync(e.path).size), 0);

const weigh = (path) => {
  const bytes = readFileSync(path);
  return { raw: statSync(path).size, gz: gzipSync(bytes, { level: 9 }).length };
};
const unityFramework = weigh(unityFrameworkPath);
const globalMetadata = weigh(globalMetadataPath);

// --- the measured Mach-O --------------------------------------------------
// Mach-O segments are 16 KB-aligned, so a file size is quantized and a
// sub-page difference between two rows can vanish or jump a whole page. The
// escape from that is a per-section breakdown, which moves at byte
// granularity and is what a zero-bytes hypothesis like H-FIELDS needs — but
// only the SECTION numbers are byte-granular. `size -m` — the obvious tool,
// and the one the protocol reached for — reports VIRTUAL MEMORY sizes for
// segments and sums those into its grand total, so two thirds of what it
// prints is quantized exactly like the file size, and its total is the size
// of nothing on disk. Measured on
// Xcode 26.6, arm64 iOS dylib: segment vmsizes 32,768 + 16,384, `size -m`
// total 49,152; segment filesizes 32,768 + 2,088, and 34,856 is what the
// filesystem reports for the same file. Both quantities are recorded here,
// under names that say which is which, and both come from the load commands
// rather than from two tools that could disagree.
//
// The load commands also answer the three questions the protocol's unit —
// "the stripped, unsigned, single-slice arm64 Mach-O" — asserts and no build
// step verifies. `ARCHS`, `DEPLOYMENT_POSTPROCESSING` and
// `CODE_SIGNING_ALLOWED` are arguments on one xcodebuild command line; if any
// of them stops applying, nothing fails, the row is simply no longer a
// measurement of the unit. Each is checked below against the file that was
// weighed.
const UNVERIFIABLE =
  "The measured Mach-O could not be inspected, so none of the three properties the protocol requires " +
  "of it — single-slice arm64, unsigned, stripped — can be established, and bytes from a file that " +
  "cannot be shown to be the unit are not this row's bytes.";

const inspect = (tool, args, why) => {
  try {
    return execFileSync(tool, args, { encoding: "utf8" });
  } catch (error) {
    console.error(`row ${rowNumber}: \`${tool} ${args.join(" ")}\` failed: ${error.message}`);
    console.error(why);
    process.exit(1);
  }
};

/**
 * The load commands of a THIN Mach-O: every segment with both its
 * page-rounded vmsize and its on-disk filesize, the byte-granular sections
 * inside it, whether a signature is attached, and the symbol table counts.
 *
 * Only ever called on a file `lipo -archs` has already reported as a single
 * architecture. On a fat file `otool -l` prints one block per slice, and
 * every field below would then be read once per slice and silently merged —
 * which is the whole reason the architecture is checked first.
 */
function loadCommands(binary) {
  const output = inspect("otool", ["-l", binary], UNVERIFIABLE);
  const segments = [];
  // LC_SYMTAB's nsyms, and LC_DYSYMTAB's nlocalsym / nextdefsym / nundefsym.
  const symbols = { total: null, local: null, definedExternal: null, undefinedExternal: null };
  let codeSignature = false;
  let command = null;
  let segment = null;
  let section = null;
  for (const raw of output.split(/\r?\n/)) {
    const line = raw.trim();
    const header = /^cmd (LC_[A-Z0-9_]+)$/.exec(line);
    if (header) {
      command = header[1];
      // A section's fields include `segname`, so the sections of the previous
      // segment must stop being the parse target before the next command's
      // fields are read.
      section = null;
      if (command === "LC_SEGMENT_64") {
        segment = { name: null, vmSize: null, fileOffset: null, fileSize: null, sections: [] };
        segments.push(segment);
      } else if (command === "LC_CODE_SIGNATURE") {
        codeSignature = true;
      }
      continue;
    }
    if (line === "Section") {
      section = { name: null, size: null };
      if (segment !== null) segment.sections.push(section);
      continue;
    }
    // Every field otool prints as one name and one value. Anything else (the
    // `Load command N` banners, a dylib `name … (offset 24)`) is not one.
    const field = /^(\w+)\s+(\S+)$/.exec(line);
    if (field === null) continue;
    const [, key, value] = field;
    if (section !== null) {
      if (key === "sectname") section.name = value;
      else if (key === "size") section.size = Number(value);
    } else if (command === "LC_SEGMENT_64" && segment !== null) {
      if (key === "segname") segment.name = value;
      else if (key === "vmsize") segment.vmSize = Number(value);
      else if (key === "fileoff") segment.fileOffset = Number(value);
      else if (key === "filesize") segment.fileSize = Number(value);
    } else if (command === "LC_SYMTAB") {
      if (key === "nsyms") symbols.total = Number(value);
    } else if (command === "LC_DYSYMTAB") {
      if (key === "nlocalsym") symbols.local = Number(value);
      else if (key === "nextdefsym") symbols.definedExternal = Number(value);
      else if (key === "nundefsym") symbols.undefinedExternal = Number(value);
    }
  }
  return { segments, symbols, codeSignature };
}

const arch = exactlyOne(
  "architecture",
  inspect("lipo", ["-archs", unityFrameworkPath], UNVERIFIABLE).trim().split(/\s+/).filter(Boolean),
  unityFrameworkPath,
  "The protocol's unit is the single-slice arm64 Mach-O that ships, which is what `ARCHS=arm64 " +
    "ONLY_ACTIVE_ARCH=NO` pins. A file carrying more than one slice weighs every slice it carries plus the " +
    "padding lipo aligns them with, and " +
    "`otool`/`size` print one block per slice, so a fat binary does not make this measurement fail — it " +
    "inflates it and still looks like a row. The file is deliberately not thinned here: a row is only " +
    "worth reading if the build that produced it was the pinned one, so fix the settings and rebuild.",
);
if (arch !== "arm64") {
  console.error(`row ${rowNumber}: the measured Mach-O is ${arch}, not arm64: ${unityFrameworkPath}`);
  console.error(
    "The protocol's unit is the arm64 slice that ships on a device. An x86_64 slice is a simulator " +
      "build and arm64e is a different ABI; either is a different compilation whose bytes are not this " +
      "row's. Rebuild with the pinned `-sdk iphoneos -destination generic/platform=iOS ARCHS=arm64`.",
  );
  process.exit(1);
}

const loaded = loadCommands(unityFrameworkPath);
if (loaded.segments.length === 0 || loaded.symbols.local === null) {
  console.error(
    `row ${rowNumber}: \`otool -l ${unityFrameworkPath}\` yielded ${loaded.segments.length} LC_SEGMENT_64 ` +
      `commands and ${loaded.symbols.local === null ? "no LC_DYSYMTAB" : "an LC_DYSYMTAB"}.`,
  );
  console.error(UNVERIFIABLE);
  process.exit(1);
}
if (loaded.codeSignature) {
  console.error(`row ${rowNumber}: the measured Mach-O carries an LC_CODE_SIGNATURE: ${unityFrameworkPath}`);
  console.error(
    'The protocol\'s unit is unsigned, and `CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO CODE_SIGN_IDENTITY=""` ' +
      "is what keeps it that way. A signature appends a padded reserved region — the protocol measured it, " +
      "which is why those settings are pinned — so a signed row's bytes are not comparable with an " +
      "unsigned row's and the two must not share a table.",
  );
  process.exit(1);
}
if (loaded.symbols.local > 0) {
  console.error(
    `row ${rowNumber}: the measured Mach-O still carries ${loaded.symbols.local.toLocaleString()} local ` +
      `symbols, so it was never stripped: ${unityFrameworkPath}`,
  );
  console.error(
    "`DEPLOYMENT_POSTPROCESSING=YES STRIP_INSTALLED_PRODUCT=YES` is what strips it, and " +
      "`xcodebuild build` does not strip without the first of those. A stripped Mach-O reports LC_DYSYMTAB " +
      "nlocalsym 0 — measured on Xcode 26.6 on an arm64 iOS dylib, both plain `strip` and `strip -x -S` " +
      "leave 0 where the unstripped file had 209. Symbol names are bytes that never ship, and IL2CPP emits " +
      "one named C function per managed method, so an unstripped row inflates exactly the per-method slope " +
      "rows 9-11 exist to measure.",
  );
  process.exit(1);
}

const machO = {
  arch,
  // `codeSignature` is false and `symbols.local` is 0 by construction — the
  // row exits above otherwise. They are recorded so that a reader of the JSON
  // can see the file was checked rather than assumed, and so that the other
  // symbol counts are there to read.
  codeSignature: loaded.codeSignature,
  symbols: loaded.symbols,
  // Per segment: `vmSize` is the page-rounded virtual size, `fileSize` the
  // bytes it occupies in the file, and `sections[].size` the byte-granular
  // section sizes — the only numbers here that are not quantized.
  segments: loaded.segments,
  vmTotal: loaded.segments.reduce((sum, s) => sum + s.vmSize, 0),
  // Summed over the same load commands this equalled the file size exactly on
  // every fixture it was checked against, signed and unsigned alike.
  // `unityFramework.raw` is that file size, and remains the number the table
  // uses; `fileTotal` is the on-disk figure broken down per segment.
  fileTotal: loaded.segments.reduce((sum, s) => sum + s.fileSize, 0),
};

// --- link map -------------------------------------------------------------
// ld's map attributes every output byte to the object file it came from,
// and archive members appear individually — so summing the members of the
// IL2CPP archives yields "generated code + IL2CPP runtime, engine
// excluded", the direct analogue of Android's libil2cpp.so.
//
// The archive set is a claim about Unity 2022.3's generated project
// (Unity's manual names libGameAssembly.a and il2cpp.a for that version;
// older layouts shipped the runtime as libil2cpp.a). The per-archive
// breakdown below is recorded in full precisely so the first real run can
// correct this set from evidence instead of from the manual.
const IL2CPP_ARCHIVES = new Set(["libGameAssembly.a", "il2cpp.a", "libil2cpp.a"]);

const archiveOf = (objectFile) => {
  const member = /([^/\\]+\.a)\([^)]*\)\s*$/.exec(objectFile);
  return member ? member[1] : null;
};

function parseLinkMap(text) {
  const objects = new Map();
  const byArchive = new Map();
  let attributed = 0;
  let mode = null;
  for (const line of text.split(/\r?\n/)) {
    if (line.startsWith("#")) {
      // Only the section headers switch mode. A `#` line that is not one
      // of them is a column caption — `# Address\tSize\tFile  Name` sits
      // directly under `# Symbols:` — and treating it as a header ends the
      // symbol section before a single symbol has been read.
      if (line.startsWith("# Object files:")) mode = "objects";
      else if (line.startsWith("# Symbols:")) mode = "symbols";
      else if (line.startsWith("# Sections:") || line.startsWith("# Dead Stripped Symbols:")) mode = null;
      continue;
    }
    if (mode === "objects") {
      const entry = /^\[\s*(\d+)\]\s+(.*\S)\s*$/.exec(line);
      if (entry) objects.set(Number(entry[1]), entry[2]);
    } else if (mode === "symbols") {
      const symbol = /^0x[0-9A-Fa-f]+\s+0x([0-9A-Fa-f]+)\s+\[\s*(\d+)\]/.exec(line);
      if (!symbol) continue;
      const size = Number.parseInt(symbol[1], 16);
      const object = objects.get(Number(symbol[2]));
      if (object === undefined) continue;
      const archive = archiveOf(object);
      if (!archive) continue;
      attributed += size;
      byArchive.set(archive, (byArchive.get(archive) ?? 0) + size);
    }
  }
  const archives = Object.fromEntries([...byArchive.entries()].sort((a, b) => b[1] - a[1]));
  const il2cppOnly = [...byArchive.entries()]
    .filter(([name]) => IL2CPP_ARCHIVES.has(name))
    .reduce((sum, [, size]) => sum + size, 0);
  return { archives, attributedToArchives: attributed, il2cppOnly, objectFiles: objects.size };
}

let linkMap = null;
let il2cppOnly = null;
let il2cppOnlyNote = null;
if (derivedDir === null) {
  il2cppOnlyNote = "no --derived directory was given, so no link map was looked for";
} else {
  const maps = [...walk(derivedDir)]
    .filter((e) => !e.directory)
    .map((e) => e.path)
    .filter((path) => /UnityFramework.*LinkMap.*\.txt$/i.test(basename(path)));
  if (maps.length === 1) {
    const parsed = parseLinkMap(readFileSync(maps[0], "utf8"));
    linkMap = { path: maps[0], ...parsed };
    if (parsed.il2cppOnly > 0) {
      il2cppOnly = parsed.il2cppOnly;
    } else {
      il2cppOnlyNote =
        `the link map at ${maps[0]} attributed 0 bytes to ${[...IL2CPP_ARCHIVES].join(", ")} — ` +
        "the IL2CPP output is linked from archives this script does not recognise. The full " +
        "per-archive breakdown is in linkMap.archives; correct IL2CPP_ARCHIVES from it.";
    }
  } else {
    il2cppOnlyNote =
      `expected exactly one UnityFramework link map under ${derivedDir}, found ${maps.length}` +
      (maps.length > 1 ? ` (${maps.join(", ")})` : "") +
      ". il2cppOnly is unavailable for this row; it is not estimated from anything else.";
  }
}

// --- result ---------------------------------------------------------------
const result = {
  ...spec,
  platform: "ios",
  // Which machine ran the Unity editor. The xcodebuild step is identical
  // for both, so any byte difference between two rows that agree on
  // everything but this field is a property of the Unity host.
  unityHost,
  unityFramework,
  globalMetadata,
  // Named `total` and summed the same way as the Android rows so
  // summarize-il2cpp.mjs's delta arithmetic works untouched. It is NOT the
  // same quantity as the Android total — see §7 of the protocol.
  total: {
    raw: unityFramework.raw + globalMetadata.raw,
    gz: unityFramework.gz + globalMetadata.gz,
  },
  il2cppOnly,
  ...(il2cppOnlyNote === null ? {} : { il2cppOnlyNote }),
  linkMap,
  machO,
  appBytes,
  // What the search actually matched, so a reader can check the discovery
  // instead of trusting it.
  paths: {
    app,
    unityFramework: unityFrameworkPath,
    globalMetadata: globalMetadataPath,
  },
  env: {
    unityVersion: arg("unity", null),
    unityHost,
    xcode: arg("xcode", null),
    xcodeBuild: arg("xcode-build", null),
    iosSdk: arg("sdk", null),
    runner: {
      label: arg("runner", null),
      // Set by GitHub on hosted runners; absent locally, which is honest
      // rather than wrong.
      imageOS: process.env.ImageOS ?? null,
      imageVersion: process.env.ImageVersion ?? null,
      arch: process.env.RUNNER_ARCH ?? null,
    },
  },
  validity: printed,
};

mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, `row${rowNumber}-${unityHost}.json`), `${JSON.stringify(result, null, 2)}\n`);
console.log(
  `row ${rowNumber} (${spec.label}, unity host ${unityHost}): UnityFramework ` +
    `${unityFramework.raw.toLocaleString()} B (gz ${unityFramework.gz.toLocaleString()}), metadata ` +
    `${globalMetadata.raw.toLocaleString()} B (gz ${globalMetadata.gz.toLocaleString()}), il2cppOnly ` +
    `${il2cppOnly === null ? "unavailable" : `${il2cppOnly.toLocaleString()} B`}`,
);
if (il2cppOnlyNote !== null) console.log(`row ${rowNumber}: il2cppOnly unavailable — ${il2cppOnlyNote}`);
