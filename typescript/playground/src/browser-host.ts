/**
 * A TypeSpec CompilerHost backed entirely by memory — no file system,
 * no network. It serves the .tsp snapshot that scripts/gen-vfs.mjs
 * embedded at build time (src/generated/vfs.json) plus the one source
 * file the user is editing, and answers getJsImport from statically
 * imported decorator modules.
 *
 * The shape follows the compiler's own in-memory test host
 * (@typespec/compiler/dist/src/testing/test-compiler-host.js); that
 * module is not reusable here because it imports node:url and the Node
 * host at module scope.
 */

import * as typespecCompiler from "@typespec/compiler";
import { createSourceFile, getAnyExtensionFromPath } from "@typespec/compiler";
import type { CompilerHost, SourceFileKind } from "@typespec/compiler";
import * as sqliteHostLibrary from "@sqlite-host/typespec";
// Deep imports on purpose: the compiler's std library .tsp files import
// these two modules by path, and the package's public $decorators export
// is *not* a substitute — it drops TypeSpec.indexer and
// TypeSpec.docFromComment (see dist/src/index.js), and without `indexer`
// no array-typed model property (KeyQueryItem[]) can compile. Relative
// paths escape the package the same way from src/ and from dist/, and
// bypass the exports map that would otherwise block the subpath.
import * as compilerIntrinsicDecorators from "../node_modules/@typespec/compiler/dist/src/lib/intrinsic/tsp-index.js";
import * as compilerStdDecorators from "../node_modules/@typespec/compiler/dist/src/lib/tsp-index.js";
import vfs from "./generated/vfs.json" with { type: "json" };

/** Absolute virtual path of the file the playground compiles. */
export const ENTRYPOINT = vfs.entrypoint;

/** The sample host definition the editor starts from. */
export const SAMPLE_SOURCE = vfs.sample;

function notFound(path: string): Error & { code: string } {
  return Object.assign(new Error(`File ${path} not found.`), { code: "ENOENT" });
}

/**
 * Build a CompilerHost whose only writable file is the entrypoint,
 * pre-populated with `source`.
 */
export function createBrowserCompilerHost(source: string): CompilerHost {
  const files = new Map<string, string>(Object.entries(vfs.files));
  files.set(ENTRYPOINT, source);

  // Path -> module, for the `import "./x.js"` statements inside the
  // .tsp files above. The compiler only ever reaches these through
  // getJsImport, so the virtual file itself can stay empty (the test
  // host does the same).
  //
  // The compiler's own entrypoint is listed because compilation starts
  // by resolving "@typespec/compiler" from the entrypoint's directory
  // to check for a version mismatch; that resolution walks the virtual
  // node_modules and requires the package's main to exist. Mapping it
  // to the module we are actually running keeps the answer honest.
  const jsImports = new Map<string, unknown>([
    [`${vfs.compilerPackageRoot}/dist/src/index.js`, typespecCompiler],
    [
      `${vfs.compilerPackageRoot}/dist/src/lib/intrinsic/tsp-index.js`,
      compilerIntrinsicDecorators,
    ],
    [`${vfs.compilerPackageRoot}/dist/src/lib/tsp-index.js`, compilerStdDecorators],
    [`${vfs.libraryPackageRoot}/dist/index.js`, sqliteHostLibrary],
  ]);
  for (const path of jsImports.keys()) {
    files.set(path, "");
  }

  return {
    async readUrl(url) {
      const contents = files.get(url);
      if (contents === undefined) throw notFound(url);
      return createSourceFile(contents, url);
    },
    async readFile(path) {
      const contents = files.get(path);
      if (contents === undefined) throw notFound(path);
      return createSourceFile(contents, path);
    },
    async writeFile(path, content) {
      files.set(path, content);
    },
    async readDir(path) {
      const entries = new Set<string>();
      for (const key of files.keys()) {
        if (!key.startsWith(`${path}/`)) continue;
        const rest = key.slice(path.length + 1);
        const slash = rest.indexOf("/");
        entries.add(slash === -1 ? rest : rest.slice(0, slash));
      }
      return [...entries];
    },
    async rm(path, options) {
      if (options?.recursive && !files.has(path)) {
        for (const key of [...files.keys()]) {
          if (key.startsWith(`${path}/`)) files.delete(key);
        }
      } else {
        files.delete(path);
      }
    },
    async mkdirp(path) {
      return path;
    },
    async stat(path) {
      if (files.has(path)) {
        return { isDirectory: () => false, isFile: () => true };
      }
      for (const key of files.keys()) {
        if (key.startsWith(`${path}/`)) {
          return { isDirectory: () => true, isFile: () => false };
        }
      }
      throw notFound(path);
    },
    // No symlinks in the virtual file system.
    async realpath(path) {
      return path;
    },
    getExecutionRoot() {
      return vfs.compilerPackageRoot;
    },
    getLibDirs() {
      return [`${vfs.compilerPackageRoot}/lib/std`];
    },
    async getJsImport(path) {
      const module = jsImports.get(path);
      if (module === undefined) {
        throw Object.assign(new Error(`Module ${path} not found`), {
          code: "ERR_MODULE_NOT_FOUND",
        });
      }
      return module as Record<string, unknown>;
    },
    getSourceFileKind(path): SourceFileKind | undefined {
      switch (getAnyExtensionFromPath(path)) {
        case ".tsp":
          return "typespec";
        case ".js":
        case ".mjs":
          return "js";
        default:
          return undefined;
      }
    },
    // node:url stand-ins. Virtual paths are always absolute POSIX
    // paths, so the round trip is a plain prefix swap.
    fileURLToPath(url) {
      if (!url.startsWith("file://")) {
        throw new Error(`Not a file URL: ${url}`);
      }
      return decodeURIComponent(url.slice("file://".length));
    },
    pathToFileURL(path) {
      return `file://${encodeURI(path)}`;
    },
    logSink: { log: () => {} },
  };
}
