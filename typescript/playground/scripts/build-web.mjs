// Bundle the playground into web-dist/: index.html (copied verbatim
// from src/web) + bundle.js (esbuild, ESM, TypeSpec compiler and every
// emitter inlined — the page has no external network dependencies).
//
// Two deliberate differences from typescript/sample-admin's build-web.mjs,
// which this otherwise mirrors:
//
//   platform "browser", not "neutral" — @typespec/compiler declares a
//   `browser` field remapping its Node host and console sink to browser
//   builds; without platform "browser" esbuild ignores that map and the
//   bundle drags in node:fs, the LSP server transport, and prettier.
//
//   a resolve plugin replacing @typespec/compiler's npm registry client
//   with a throwing stub. It is the only fetch() in the graph, at a
//   hardcoded https://registry.npmjs.org, and it is unreachable today
//   only because runPipeline never resolves packages. "No server, no
//   network" is the page's load-bearing promise, so the sink does not
//   ship at all. esbuild's `alias` cannot express this — the module is
//   imported by a relative path from inside the package, not by a bare
//   specifier — hence the plugin.
//
//   an alias for node:path — the shared codegen frontend imports
//   `resolve` from it. src/browser-node-path.ts supplies the real POSIX
//   algorithm and throws on anything that would need a working
//   directory. esbuild errors if the bundle ever asks that alias for a
//   name it does not export, so a new node:path caller fails the build
//   instead of silently reaching a stub.
import { build } from "esbuild";
import { copyFileSync, mkdirSync } from "node:fs";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("..", import.meta.url));

const stubNpmRegistry = {
  name: "stub-npm-registry",
  setup(pluginBuild) {
    pluginBuild.onResolve({ filter: /[\\/]package-manger[\\/]npm-registry\.js$/ }, () => ({
      path: `${root}src/no-npm-registry.ts`,
    }));
  },
};

await build({
  entryPoints: [`${root}src/web/main.ts`],
  bundle: true,
  format: "esm",
  platform: "browser",
  target: "es2022",
  alias: { "node:path": `${root}src/browser-node-path.ts` },
  plugins: [stubNpmRegistry],
  outfile: `${root}web-dist/bundle.js`,
});

mkdirSync(`${root}web-dist`, { recursive: true });
copyFileSync(`${root}src/web/index.html`, `${root}web-dist/index.html`);
