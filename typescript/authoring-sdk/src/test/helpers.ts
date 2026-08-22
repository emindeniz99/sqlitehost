import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

/** Absolute path of a file under the repo's fixtures/ directory. */
export function fixturePath(relative: string): string {
  return fileURLToPath(new URL(`../../../../fixtures/${relative}`, import.meta.url));
}

export function readFixture(relative: string): string {
  return readFileSync(fixturePath(relative), "utf8");
}
