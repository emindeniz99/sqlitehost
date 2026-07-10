import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

/** Absolute path of a file under projects/sqlitehost/fixtures/. */
export function fixturePath(relative: string): string {
  return fileURLToPath(new URL(`../../../../fixtures/${relative}`, import.meta.url));
}

export function readFixture(relative: string): string {
  return readFileSync(fixturePath(relative), "utf8");
}

export function listValidPayloads(): string[] {
  return readdirSync(fixturePath("payloads/valid"))
    .filter((name) => name.endsWith(".json"))
    .sort();
}
