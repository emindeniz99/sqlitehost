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
  valid: boolean;
  errors: ExpectedFinding[];
  warnings: ExpectedFinding[];
}

const expectations = JSON.parse(readFixture("payloads/expectations.json")) as {
  cases: ExpectationCase[];
};
const manifest = parseHostManifest(readFixture("manifests/sample-host.manifest.json"));

function typescriptCodes(expected: ExpectedFinding[]): string[] {
  return expected
    .filter((finding) => finding.validators.includes("typescript"))
    .map((finding) => finding.code);
}

for (const expectationCase of expectations.cases) {
  test(`conformance: ${expectationCase.payload}`, () => {
    const payload = JSON.parse(readFixture(`payloads/${expectationCase.payload}`));
    const findings = lintScript(payload, manifest);
    const errors: string[] = findings
      .filter((f) => f.severity === "error")
      .map((f) => f.code);
    const warnings: string[] = findings
      .filter((f) => f.severity === "warning")
      .map((f) => f.code);

    if (expectationCase.valid) {
      // Valid payloads: zero errors and exactly the expected warnings.
      assert.deepStrictEqual(errors, [], `unexpected errors: ${JSON.stringify(findings)}`);
      assert.deepStrictEqual(
        [...warnings].sort(),
        typescriptCodes(expectationCase.warnings).sort(),
      );
      assert.ok(isPublishable(findings));
    } else {
      // Invalid payloads: every typescript-validated code must be present
      // (extra findings are allowed by docs/validation.md).
      for (const code of typescriptCodes(expectationCase.errors)) {
        assert.ok(
          errors.includes(code),
          `expected error ${code}, got ${JSON.stringify(findings)}`,
        );
      }
      if (typescriptCodes(expectationCase.errors).length > 0) {
        assert.ok(!isPublishable(findings));
      }
    }
  });
}
