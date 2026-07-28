/**
 * Browser stand-in for `node:path`, aliased in by scripts/build-web.mjs.
 *
 * The shared codegen frontend (codegen/core/src/frontend.ts) imports
 * exactly one thing from it — `resolve`, applied to the entrypoint path
 * — and esbuild fails the build if anything in the bundle asks for a
 * name this module does not export. So the surface stays at one
 * function and grows only when a real caller appears.
 *
 * `resolve` is the real POSIX algorithm, not an approximation, with one
 * deliberate exception: there is no working directory in a browser, so
 * a call that would need one throws instead of inventing a root. The
 * playground only ever passes the absolute virtual entrypoint.
 */

export function resolve(...segments: string[]): string {
  let resolved = "";
  for (let i = segments.length - 1; i >= 0 && !resolved.startsWith("/"); i--) {
    const segment = segments[i];
    if (segment === "") continue;
    resolved = resolved === "" ? segment : `${segment}/${resolved}`;
  }
  if (!resolved.startsWith("/")) {
    throw new Error(
      `Cannot resolve relative path ${JSON.stringify(resolved)}: there is no ` +
        `working directory in the browser. Pass an absolute path.`,
    );
  }

  const parts: string[] = [];
  for (const part of resolved.split("/")) {
    if (part === "" || part === ".") continue;
    if (part === "..") {
      parts.pop();
    } else {
      parts.push(part);
    }
  }
  return `/${parts.join("/")}`;
}
