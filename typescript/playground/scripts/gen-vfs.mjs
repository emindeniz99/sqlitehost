// Build-time step for the playground: snapshot every .tsp source the
// TypeSpec compiler needs at compile time into src/generated/vfs.json,
// so the browser bundle can compile a host definition with no file
// system and no network.
//
// What goes in:
//   - @typespec/compiler's own lib/**.tsp (intrinsics + std library)
//     and its package.json (the compiler resolves its execution root
//     through it),
//   - @sqlite-host/typespec's lib/**.tsp and package.json (the
//     `import "@sqlite-host/typespec"` in a host definition resolves
//     through the package's `typespec` export condition),
//   - the sample host definition used to prefill the editor.
//
// What does NOT go in: JavaScript. The decorator implementations reach
// the compiler through CompilerHost.getJsImport, which src/browser-host.ts
// answers from statically imported modules — see the note there.
import { readFile, readdir, mkdir, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const packageRoot = fileURLToPath(new URL("..", import.meta.url));
const projectRoot = join(packageRoot, "..", "..");
const require = createRequire(import.meta.url);

// @typespec/compiler's exports map has no "./package.json" entry, so
// resolve the main entrypoint (dist/src/index.js) and walk up to the
// package root instead.
const compilerRoot = dirname(dirname(dirname(require.resolve("@typespec/compiler"))));
const libraryRoot = join(projectRoot, "typespec", "library");

/** Virtual root the playground compiles in. Must be absolute. */
const ROOT = "/playground";
const COMPILER_PACKAGE_ROOT = `${ROOT}/node_modules/@typespec/compiler`;
const LIBRARY_PACKAGE_ROOT = `${ROOT}/node_modules/@sqlite-host/typespec`;

/** @type {Record<string, string>} */
const files = {};

/** Copy every .tsp under realDir into the virtual tree at virtualDir. */
async function addTspTree(realDir, virtualDir) {
  for (const entry of (await readdir(realDir, { withFileTypes: true })).sort((a, b) =>
    a.name < b.name ? -1 : 1,
  )) {
    const real = join(realDir, entry.name);
    const virtual = `${virtualDir}/${entry.name}`;
    if (entry.isDirectory()) {
      await addTspTree(real, virtual);
    } else if (entry.name.endsWith(".tsp")) {
      files[virtual] = await readFile(real, "utf8");
    }
  }
}

async function addFile(realPath, virtualPath) {
  files[virtualPath] = await readFile(realPath, "utf8");
}

await addTspTree(join(compilerRoot, "lib"), `${COMPILER_PACKAGE_ROOT}/lib`);
await addFile(join(compilerRoot, "package.json"), `${COMPILER_PACKAGE_ROOT}/package.json`);
await addTspTree(join(libraryRoot, "lib"), `${LIBRARY_PACKAGE_ROOT}/lib`);
await addFile(join(libraryRoot, "package.json"), `${LIBRARY_PACKAGE_ROOT}/package.json`);

const sample = await readFile(
  join(projectRoot, "typespec", "examples", "sample-host-methods.tsp"),
  "utf8",
);

const outDir = join(packageRoot, "src", "generated");
await mkdir(outDir, { recursive: true });
await writeFile(
  join(outDir, "vfs.json"),
  JSON.stringify(
    {
      root: ROOT,
      entrypoint: `${ROOT}/main.tsp`,
      compilerPackageRoot: COMPILER_PACKAGE_ROOT,
      libraryPackageRoot: LIBRARY_PACKAGE_ROOT,
      sample,
      files,
    },
    null,
    2,
  ) + "\n",
  "utf8",
);

console.log(`gen-vfs: ${Object.keys(files).length} files -> src/generated/vfs.json`);
