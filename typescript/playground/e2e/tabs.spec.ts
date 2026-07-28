/**
 * WHY: the tab bar is built at runtime by main.ts, and each button's
 * only job is to point the output pane at a different slice of the same
 * compile result. A mis-wired listener (every button rendering the
 * manifest, say) is invisible to the Node tests, which never build a
 * tab bar — and to a screenshot, since all five panes look alike.
 *
 * Each tab is therefore checked against something only that emitter can
 * produce, and against the selected state the CSS keys off.
 */

import { expect, tab, test } from "./fixtures.js";

test("the DDL tab shows the schema script", async ({ playground }) => {
  await tab(playground, "ddl").click();
  await expect(playground.locator("#output")).toContainText("CREATE TABLE");
  await expect(tab(playground, "ddl")).toHaveAttribute("aria-selected", "true");
  await expect(tab(playground, "manifest")).toHaveAttribute("aria-selected", "false");
});

test("the Java tab shows generated Java sources", async ({ playground }) => {
  await tab(playground, "java").click();
  await expect(playground.locator("#output")).toContainText("package io.sqlitehost");
});

test("the file selector appears only for tabs that emit several files", async ({ playground }) => {
  // The manifest and the DDL are one document each; hiding a selector
  // with a single option is the difference between a control and noise.
  await expect(playground.locator("#file")).toBeHidden();
  await tab(playground, "typescript").click();
  await expect(playground.locator("#file")).toBeVisible();

  const files = playground.locator("#file option");
  await expect(files).toHaveText([
    "runtime-types/src/generated/envelope.ts",
    "authoring-sdk/src/generated/sample-host.ts",
  ]);

  // Picking the second file must swap the pane, not just the label:
  // the per-host authoring module declares the sample's own methods,
  // which the shared envelope module knows nothing about.
  await expect(playground.locator("#output")).toContainText("SCRIPT_ENGINE_V1");
  await playground.locator("#file").selectOption("authoring-sdk/src/generated/sample-host.ts");
  await expect(playground.locator("#output")).toContainText("interface GetValueInput");
});
