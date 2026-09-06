/**
 * Build-time replacement for `@typespec/compiler`'s
 * `package-manger/npm-registry.js`, which is the one module in the
 * playground's dependency graph that calls `fetch()` — at a hardcoded
 * `https://registry.npmjs.org`.
 *
 * "No server, no network" is the playground's load-bearing promise: it
 * is what makes pasting a private host definition into the page safe.
 * That promise was held up by reachability alone — `runPipeline` calls
 * `compileHostLibrary` directly and never touches package resolution —
 * with a live sink sitting in the shipped artifact behind it. This is
 * the second line of defence: the sink is not in the bundle at all, so
 * a future compiler version that starts resolving packages fails loudly
 * here instead of quietly reaching the network from a user's browser.
 *
 * The exports mirror the real module's, name for name. esbuild errors
 * when the bundle asks a replacement for a name it does not export, so
 * a signature change upstream breaks the build rather than slipping
 * through — the same property the `node:path` alias relies on.
 */

function refuse(): never {
  throw new Error(
    "the playground makes no network requests: @typespec/compiler's npm registry " +
      "client is stubbed out at build time (src/no-npm-registry.ts)",
  );
}

export function getNpmRegistry(): string {
  refuse();
}

export async function fetchPackageManifest(
  _packageName: string,
  _version: string,
): Promise<never> {
  refuse();
}

export function fetchLatestPackageManifest(_packageName: string): Promise<never> {
  refuse();
}
