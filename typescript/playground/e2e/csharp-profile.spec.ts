/**
 * WHY: the profile selector is the one control whose whole purpose is to
 * change what you are looking at, and the one the Node tests cannot
 * exercise — they call emitCSharp per profile directly and never go
 * through a <select>. A selector that is wired to `change` but reads a
 * stale value, or renders before the option is applied, would still show
 * three profiles and always emit classic. The size profiles are a
 * documented IL2CPP/code-size feature (docs/csharp-api.md); showing the
 * wrong one is worse than not offering the choice.
 */

import { expect, tab, test } from "./fixtures.js";

test("switching from classic to ultra changes the emitted C#", async ({ playground }) => {
  await tab(playground, "csharp").click();
  await expect(playground.locator("#profile")).toBeVisible();
  await expect(playground.locator("#profile")).toHaveValue("classic");

  const classic = await playground.locator("#output").textContent();
  expect(classic).toContain("class GetValueInput");

  await playground.locator("#profile").selectOption("ultra");
  const ultra = await playground.locator("#output").textContent();
  expect(ultra).not.toBe(classic);

  // ultra's defining trait: no per-method DTO file at all, so the file
  // selector must lose that entry and the pane must land on another file.
  const files = await playground.locator("#file option").allTextContents();
  expect(files).not.toContain("HostMethodDtos.g.cs");
  expect(ultra).not.toContain("class GetValueInput");
});

test("the profile selector is offered only on the C# tab", async ({ playground }) => {
  // It is the only tab with profiles; leaving it visible elsewhere would
  // suggest the manifest or the DDL had size variants too.
  await expect(playground.locator("#profile")).toBeHidden();
  await tab(playground, "csharp").click();
  await expect(playground.locator("#profile")).toBeVisible();
  await tab(playground, "java").click();
  await expect(playground.locator("#profile")).toBeHidden();
});
