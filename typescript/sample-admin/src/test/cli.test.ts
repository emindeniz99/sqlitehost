import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);

const DEMO_BIN = fileURLToPath(new URL("../../bin/demo.mjs", import.meta.url));

function fixturePath(relative: string): string {
  return fileURLToPath(new URL(`../../../../fixtures/${relative}`, import.meta.url));
}

async function runDemoBin(
  args: string[],
): Promise<{ code: number; stdout: string; stderr: string }> {
  try {
    const { stdout, stderr } = await execFileAsync(process.execPath, [DEMO_BIN, ...args]);
    return { code: 0, stdout, stderr };
  } catch (error) {
    const failure = error as { code?: number; stdout?: string; stderr?: string };
    return { code: failure.code ?? -1, stdout: failure.stdout ?? "", stderr: failure.stderr ?? "" };
  }
}

test("demo prints the reference and passes example-001", async () => {
  const { code, stdout } = await runDemoBin([
    fixturePath("payloads/valid/example-001-read-then-conditional-write.json"),
  ]);
  assert.equal(code, 0);
  assert.match(stdout, /Example\.Game\.GameHostMethods/);
  assert.match(stdout, /getValue \(handler GetValue, api level 1\)/);
  assert.match(stdout, /call_get_values__input_keys/);
  assert.match(stdout, /No findings\. Payload is publishable\./);
});

test("demo reports lint errors for an invalid fixture and exits 1", async () => {
  const { code, stdout } = await runDemoBin([
    fixturePath("payloads/invalid/missing-binding.json"),
  ]);
  assert.equal(code, 1);
  assert.match(stdout, /error missing-binding \[step read, statement 0\]/);
  assert.match(stdout, /Payload is NOT publishable/);
});

test("demo reports warnings without blocking publish", async () => {
  const { code, stdout } = await runDemoBin([
    fixturePath("payloads/valid/example-005-unused-required-method.json"),
  ]);
  assert.equal(code, 0);
  assert.match(stdout, /warning unused-required-method/);
  assert.match(stdout, /Warnings only\. Payload is publishable\./);
});

test("demo exits 2 without arguments", async () => {
  const { code, stdout } = await runDemoBin([]);
  assert.equal(code, 2);
  assert.match(stdout, /usage:/);
});

// -- malformed input, not a crash ---------------------------------------------
// parseHostManifest checks four fields and then casts, and JSON.parse
// throws on anything that is not JSON at all. Both sat outside the
// try/catch that already turned an unreadable file into `error: ...`
// and exit 2, so pointing the demo at the wrong file dumped a Node
// stack trace — which reads as a tool that broke rather than an input
// that was wrong, and prints internal paths while doing it.

test("demo reports a malformed manifest instead of crashing", async () => {
  const { code, stdout, stderr } = await runDemoBin([
    fixturePath("payloads/valid/example-001-read-then-conditional-write.json"),
    fixturePath("payloads/valid/example-001-read-then-conditional-write.json"),
  ]);
  assert.equal(code, 2);
  assert.match(stdout, /error: .*manifestVersion/);
  assert.doesNotMatch(stderr, /at .*\.js:/, `stack trace on stderr: ${stderr}`);
});

test("demo reports a payload that is not JSON instead of crashing", async () => {
  // A file that exists and reads fine, so the pre-existing IO catch
  // does not cover it — JSON.parse is where it fails.
  const { code, stdout, stderr } = await runDemoBin([DEMO_BIN]);
  assert.equal(code, 2);
  assert.match(stdout, /error: /);
  assert.doesNotMatch(stderr, /at .*\.js:/, `stack trace on stderr: ${stderr}`);
});
