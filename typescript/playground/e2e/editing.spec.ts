/**
 * WHY: a playground is a half-typed buffer most of the time, so the
 * error path is the common path. Three things have to hold for the page
 * to be usable while you type, and none of them exist under Node:
 *
 *  - a broken source must point at *your* line and column, not just say
 *    "failed" — the diagnostics panel is the only feedback the page has;
 *  - the failure must be visible on whichever tab you happen to be on,
 *    rather than leaving stale output that looks like it still compiles;
 *  - fixing the source must recover on its own, and the recompile must
 *    be debounced — a compile per keystroke would freeze the editor,
 *    since the TypeSpec compiler runs on the UI thread.
 */

import { expect, FAILURE_MESSAGE, STATUS_ERRORS, STATUS_READY, tab, test } from "./fixtures.js";

/** A model referring to a type that does not exist, appended to the sample. */
const BROKEN_TAIL = ["model BrokenModel {", "  value: NoSuchType;", "}"];

interface DebounceLog {
  /** Timestamp of every `input` event the textarea saw. */
  inputs: number[];
  /** Every text the status pane took, with the time it took it. */
  statusChanges: { text: string; at: number }[];
}

declare global {
  interface Window {
    __debounceLog?: DebounceLog;
  }
}

test("a broken source reports line:column and every tab shows the failure", async ({
  playground,
}) => {
  const sample = await playground.locator("#source").inputValue();
  const broken = `${sample}\n${BROKEN_TAIL.join("\n")}\n`;
  // The position is computed from the text we typed rather than
  // hard-coded, so the assertion stays about "points at my source".
  const brokenLines = broken.split("\n");
  const line = brokenLines.findIndex((text) => text.includes("NoSuchType")) + 1;
  const column = brokenLines[line - 1].indexOf("NoSuchType") + 1;

  await playground.locator("#source").fill(broken);
  await expect(playground.locator("#status")).toHaveText(STATUS_ERRORS);

  const diagnostic = playground.locator("#diagnostics li").first();
  await expect(diagnostic.locator(".severity")).toHaveText("error");
  await expect(diagnostic.locator(".loc")).toHaveText(`${line}:${column} invalid-ref:`);
  await expect(diagnostic).toContainText("NoSuchType");

  // Stale output on a failed compile is the dangerous UX here: it reads
  // as success. Every tab must say the compile failed instead.
  for (const id of ["manifest", "ddl", "csharp", "java", "typescript"] as const) {
    await tab(playground, id).click();
    await expect(playground.locator("#output"), `${id} tab kept stale output`).toHaveText(
      FAILURE_MESSAGE,
    );
    await expect(playground.locator("#file")).toBeHidden();
  }
});

test("restoring the source recompiles and clears the diagnostics", async ({ playground }) => {
  const sample = await playground.locator("#source").inputValue();

  await playground.locator("#source").fill("not typespec at all {{{");
  await expect(playground.locator("#status")).toHaveText(STATUS_ERRORS);

  await playground.locator("#source").fill(sample);
  await expect(playground.locator("#status")).toHaveText(STATUS_READY);
  await expect(playground.locator("#diagnostics")).toHaveText("No diagnostics.");
  await expect(playground.locator("#output")).toContainText('"engine": "sqlite-host-v1"');
});

test("a burst of keystrokes compiles once, after the typing quiets", async ({ playground }) => {
  await playground.evaluate(() => {
    const status = document.getElementById("status") as HTMLElement;
    const source = document.getElementById("source") as HTMLTextAreaElement;
    const log: DebounceLog = { inputs: [], statusChanges: [] };
    window.__debounceLog = log;
    new MutationObserver(() => {
      log.statusChanges.push({ text: status.textContent ?? "", at: performance.now() });
    }).observe(status, { childList: true, characterData: true, subtree: true });
    source.addEventListener("input", () => log.inputs.push(performance.now()));
  });

  const typed = "// e2e";
  await playground.locator("#source").focus();
  await playground.keyboard.press("Control+End");
  await playground.locator("#source").pressSequentially(typed, { delay: 0 });
  // Wait on the recorded transitions, not on the status text: the pane
  // still reads "up to date" from the load-time compile, so a text
  // assertion would pass before the edit was ever compiled.
  await playground.waitForFunction(
    (ready) => (window.__debounceLog?.statusChanges ?? []).some((c) => c.text === ready),
    STATUS_READY,
  );

  const log = (await playground.evaluate(() => window.__debounceLog)) as DebounceLog;
  expect(log.inputs, "keystrokes did not reach the textarea").toHaveLength(typed.length);

  const burstMs = log.inputs[log.inputs.length - 1] - log.inputs[0];
  expect(burstMs, `keystroke burst took ${burstMs}ms, longer than the 400ms debounce`).toBeLessThan(
    400,
  );

  // One compile for the whole burst — this is the debounce doing its job.
  const compiles = log.statusChanges.filter((change) => change.text === "compiling…");
  expect(compiles).toHaveLength(1);

  // …and it started only after typing stopped. setTimeout never fires
  // early, so this is a lower bound on the real 400ms with slack for
  // timer coarsening, not a sleep.
  const quietMs = compiles[0].at - log.inputs[log.inputs.length - 1];
  expect(quietMs, `recompiled ${quietMs}ms after the last keystroke`).toBeGreaterThan(350);
});
