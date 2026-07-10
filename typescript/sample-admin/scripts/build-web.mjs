// Bundle the browser admin demo into web-dist/: index.html (copied
// verbatim from src/web) + bundle.js (esbuild, ESM, all workspace deps
// inlined — the page has no external network dependencies).
import { build } from "esbuild";
import { copyFileSync, mkdirSync } from "node:fs";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("..", import.meta.url));

await build({
  entryPoints: [`${root}src/web/main.ts`],
  bundle: true,
  format: "esm",
  platform: "neutral",
  target: "es2022",
  outfile: `${root}web-dist/bundle.js`,
});

mkdirSync(`${root}web-dist`, { recursive: true });
copyFileSync(`${root}src/web/index.html`, `${root}web-dist/index.html`);
