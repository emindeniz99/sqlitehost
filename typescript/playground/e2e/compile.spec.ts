/**
 * WHY: the page is supposed to be useful before you touch it — it
 * prefills the sample host definition and compiles it on load, so the
 * first thing a visitor sees is real generated output. That behavior is
 * three separate things wired together (the prefill, the auto-run, the
 * render), and a break in any of them still leaves a page that "loads".
 *
 * The manifest is compared byte for byte against the committed golden
 * because the playground's other promise is that its output *is* the
 * CLI's output. parity.test.ts pins that for the pipeline under Node;
 * this pins that nothing between the pipeline and the DOM — the render
 * path, the file selector's choice of default file — quietly alters it.
 */

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

import { expect, FAILURE_MESSAGE, tab, test, type TabId } from "./fixtures.js";

/** <repo root>/fixtures/…, from typescript/playground/e2e/. */
const MANIFEST_GOLDEN = readFileSync(
  fileURLToPath(new URL("../../../fixtures/manifests/sample-host.manifest.json", import.meta.url)),
  "utf8",
);

const ALL_TABS: readonly TabId[] = ["manifest", "ddl", "csharp", "java", "typescript"];

test("the prefilled sample compiles on load, with no diagnostics", async ({ playground }) => {
  await expect(playground.locator("#source")).not.toBeEmpty();
  await expect(playground.locator("#diagnostics")).toHaveText("No diagnostics.");
});

test("the Manifest tab shows the committed golden byte for byte", async ({ playground }) => {
  // textContent, not innerText: innerText applies CSS-aware whitespace
  // rules and would hide exactly the kind of drift this test is for.
  expect(await playground.locator("#output").textContent()).toBe(MANIFEST_GOLDEN);
});

test("every output tab has generated content", async ({ playground }) => {
  for (const id of ALL_TABS) {
    await tab(playground, id).click();
    const contents = await playground.locator("#output").textContent();
    expect(contents, `${id} tab is empty`).toBeTruthy();
    expect(contents, `${id} tab shows the failure placeholder`).not.toBe(FAILURE_MESSAGE);
  }
});
