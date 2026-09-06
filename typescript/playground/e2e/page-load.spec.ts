/**
 * WHY: "no server, no network" is the playground's load-bearing promise —
 * it is what makes pasting a private host definition into the page safe,
 * and it is stated on the page itself. src/test/web-bundle.test.ts covers
 * the build: the bundle carries no network sink, and the pipeline runs
 * with fetch/XHR/WebSocket/EventSource replaced by throwing stubs.
 * Whether the LOADED page reaches off-origin — favicons, fonts, a
 * redirect, anything the browser itself initiates — is a question only a
 * browser answers, which is what this file is for.
 *
 * The same test doubles as the smoke test for module loading: a 2.3 MB
 * ESM bundle that throws on evaluation would leave the page silent and
 * blank, which no Node-side assertion can see.
 */

import { expect, test } from "./fixtures.js";

test("the page loads with no console errors and no uncaught exceptions", async ({
  playground,
  activity,
}) => {
  await expect(playground.locator("h1")).toHaveText("SqliteHost playground");
  expect(activity.consoleErrors).toEqual([]);
});

test("the page fetches itself and its bundle, and nothing else", async ({
  playground,
  activity,
  origin,
}) => {
  // Exact, not "starts with the origin": an extra request to the test
  // server would still mean the page grew a dependency it did not have,
  // and every URL being loopback is what proves the offline claim.
  await expect(playground.locator("#output")).not.toBeEmpty();
  expect(activity.requestUrls).toEqual([`${origin}/`, `${origin}/bundle.js`]);
  expect(activity.badResponses).toEqual([]);
});
