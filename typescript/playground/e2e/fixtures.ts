/**
 * Harness for the browser tests: a loopback-only static server for
 * web-dist/, plus a page that has already finished its first compile.
 *
 * The server is node:http and nothing else on purpose. The page's claim
 * is that it needs no server and no network; a test rig that pulled in a
 * static-server dependency to prove that would be arguing against
 * itself. It is needed at all because bundle.js is loaded as an ESM
 * module script, and Chromium refuses module scripts from a file: origin.
 *
 * Only index.html and bundle.js are served. Anything else is a 404,
 * which is exactly what page-load.spec.ts is watching for — a request
 * the page should never have made.
 */

import { readFile, stat } from "node:fs/promises";
import { createServer } from "node:http";
import type { AddressInfo } from "node:net";
import { test as base, expect, type Page } from "@playwright/test";

const WEB_DIST = new URL("../web-dist/", import.meta.url);

const SERVED: ReadonlyMap<string, string> = new Map([
  ["/", "index.html"],
  ["/index.html", "index.html"],
  ["/bundle.js", "bundle.js"],
]);

const CONTENT_TYPES: Record<string, string> = {
  "index.html": "text/html; charset=utf-8",
  "bundle.js": "text/javascript; charset=utf-8",
};

/** Status text the page shows once a compile has landed. */
export const STATUS_READY = "up to date";
/** Status text the page shows when the buffer does not compile. */
export const STATUS_ERRORS = "compile errors";

/** The output pane's stand-in when there is nothing to show. */
export const FAILURE_MESSAGE = "Compilation failed — see the diagnostics below.";

export type TabId = "manifest" | "ddl" | "csharp" | "java" | "typescript";

/** The tab button for `id`, by the data attribute main.ts sets on it. */
export function tab(page: Page, id: TabId) {
  return page.locator(`#tabs button[data-tab="${id}"]`);
}

/** Everything the browser said or fetched while a test ran. */
export interface PageActivity {
  /** console.error / console.warning text and uncaught exceptions. */
  consoleErrors: string[];
  /** Every request the page issued, in order. */
  requestUrls: string[];
  /** Requests that failed outright or came back non-2xx. */
  badResponses: string[];
}

async function serveWebDist(): Promise<{ origin: string; close: () => Promise<void> }> {
  // Fail here, with the command to run, rather than let every test fail
  // on an empty page fetched from a server with nothing to serve.
  await stat(new URL("bundle.js", WEB_DIST)).catch(() => {
    throw new Error("web-dist/bundle.js is missing — run `npm run build:web` before the e2e tests");
  });

  const server = createServer((req, res) => {
    const name = SERVED.get(new URL(req.url ?? "/", "http://localhost").pathname);
    if (name === undefined) {
      res.writeHead(404).end("not found");
      return;
    }
    void readFile(new URL(name, WEB_DIST)).then((body) => {
      res.writeHead(200, { "content-type": CONTENT_TYPES[name] }).end(body);
    });
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address() as AddressInfo;
  return {
    origin: `http://127.0.0.1:${port}`,
    close: () =>
      new Promise<void>((resolve) => {
        // The browser holds the connection open with keep-alive, and it
        // outlives this fixture; without dropping it, close() would sit
        // there until the idle timeout expired.
        server.closeAllConnections();
        server.close(() => resolve());
      }),
  };
}

/**
 * `playground` is the page under test: loaded and past its first
 * compile, which is where every test except the load test starts.
 */
export const test = base.extend<
  { activity: PageActivity; playground: Page },
  { origin: string }
>({
  origin: [
    async ({}, use) => {
      const server = await serveWebDist();
      await use(server.origin);
      await server.close();
    },
    { scope: "worker" },
  ],

  activity: async ({ page }, use) => {
    const activity: PageActivity = { consoleErrors: [], requestUrls: [], badResponses: [] };
    page.on("console", (message) => {
      if (message.type() === "error" || message.type() === "warning") {
        activity.consoleErrors.push(`${message.type()}: ${message.text()}`);
      }
    });
    page.on("pageerror", (error) => activity.consoleErrors.push(`uncaught: ${error.message}`));
    page.on("request", (request) => activity.requestUrls.push(request.url()));
    page.on("requestfailed", (request) =>
      activity.badResponses.push(`${request.url()} failed: ${request.failure()?.errorText}`),
    );
    page.on("response", (response) => {
      if (!response.ok()) activity.badResponses.push(`${response.url()} -> ${response.status()}`);
    });
    await use(activity);
  },

  playground: async ({ page, origin, activity }, use) => {
    // Depending on `activity` is what orders its listeners before this
    // navigation, so the load itself is recorded, not just what follows.
    void activity;
    await page.goto(origin);
    await expect(page.locator("#status")).toHaveText(STATUS_READY);
    await use(page);
  },
});

export { expect } from "@playwright/test";
