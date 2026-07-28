/**
 * Browser tests for the playground page. Scoped to this package: the
 * only thing under test is web-dist/, which `npm run build:web` writes
 * and e2e/fixtures.ts serves over loopback.
 *
 * Chromium only, headless. The page is plain DOM with no vendor-specific
 * APIs, so a second engine would cost minutes of CI for no new signal;
 * what these tests add over the Node suites is "a real browser at all",
 * not "every browser".
 *
 * @playwright/test is pinned rather than caret-ranged because the browser
 * binaries are provisioned outside this repo (PLAYWRIGHT_BROWSERS_PATH):
 * a minor bump moves the expected Chromium revision and the pre-installed
 * one stops resolving. See README.md "Browser tests".
 */

import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  // Bundle parse plus a full TypeSpec compile is the slowest thing here;
  // the generous timeouts are for a loaded CI box, not for flakiness.
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  // No retries on purpose: a browser test that only passes on the second
  // attempt is a bug report, not a pass.
  retries: 0,
  reporter: [["list"]],
  use: {
    browserName: "chromium",
    headless: true,
    trace: "retain-on-failure",
  },
});
