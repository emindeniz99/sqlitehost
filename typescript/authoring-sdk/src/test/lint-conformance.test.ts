import assert from "node:assert/strict";
import { test } from "node:test";

import { isPublishable, lintScript, parseHostManifest } from "../index.js";
import { readFixture } from "./helpers.js";

interface ExpectedFinding {
  code: string;
  validators: string[];
}

interface ExpectationCase {
  payload: string;
  /** Relative to fixtures/payloads/; defaults to the top-level manifest. */
  manifest?: string;
  valid: boolean;
  errors: ExpectedFinding[];
  warnings: ExpectedFinding[];
}

const expectations = JSON.parse(readFixture("payloads/expectations.json")) as {
  manifest: string;
  cases: ExpectationCase[];
};

// A case may bind to its own manifest. Nearly every one uses the sample
// host, but a rule about a method's API level needs a host with a method
// above the level a script may declare, and the sample host has none.
const manifests = new Map<string, ReturnType<typeof parseHostManifest>>();
function manifestFor(relative: string) {
  let parsed = manifests.get(relative);
  if (parsed === undefined) {
    parsed = parseHostManifest(readFixture(`payloads/${relative}`));
    manifests.set(relative, parsed);
  }
  return parsed;
}

function typescriptCodes(expected: ExpectedFinding[]): string[] {
  return expected
    .filter((finding) => finding.validators.includes("typescript"))
    .map((finding) => finding.code)
    .sort();
}

for (const expectationCase of expectations.cases) {
  test(`conformance: ${expectationCase.payload}`, () => {
    const payload = JSON.parse(readFixture(`payloads/${expectationCase.payload}`));
    const findings = lintScript(
      payload,
      manifestFor(expectationCase.manifest ?? expectations.manifest),
    );
    const errors: string[] = findings
      .filter((f) => f.severity === "error")
      .map((f) => f.code)
      .sort();
    const warnings: string[] = findings
      .filter((f) => f.severity === "warning")
      .map((f) => f.code)
      .sort();

    // Exact match, both severities, valid and invalid alike. Containment
    // used to be enough on invalid payloads, and every validator
    // divergence found in two audit rounds walked through that gap: an
    // unexpected extra finding is either a fixture that carries more than
    // one fault or a validator that disagrees with its peer. The
    // comparison is over sorted lists rather than sets, so a code
    // reported twice must be expected twice.
    const detail = `findings: ${JSON.stringify(findings)}`;
    assert.deepStrictEqual(errors, typescriptCodes(expectationCase.errors), detail);
    assert.deepStrictEqual(warnings, typescriptCodes(expectationCase.warnings), detail);

    if (expectationCase.valid) {
      assert.ok(isPublishable(findings));
    } else if (errors.length > 0) {
      assert.ok(!isPublishable(findings));
    }
  });
}
