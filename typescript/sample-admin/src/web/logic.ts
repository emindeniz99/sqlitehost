/**
 * DOM-free core of the browser admin demo: parse a host manifest and a
 * script payload from JSON text, derive the method/table reference, and
 * run the static lint. Kept separate from the DOM wiring so the bundle
 * can be imported (and smoke-tested) under Node.
 */

import {
  isPublishable,
  lintScript,
  loadHostMetadata,
  parseHostManifest,
  type HostMetadata,
  type LintFinding,
} from "@sqlite-host/authoring";

export interface AnalysisResult {
  metadata: HostMetadata;
  findings: LintFinding[];
  publishable: boolean;
}

/**
 * Parse the manifest and payload JSON texts and lint the payload.
 * Throws (SyntaxError / manifest parse errors) on malformed input; the
 * DOM layer surfaces the message.
 */
export function analyzePayload(manifestJson: string, payloadJson: string): AnalysisResult {
  const manifest = parseHostManifest(manifestJson);
  const findings = lintScript(JSON.parse(payloadJson), manifest);
  return {
    metadata: loadHostMetadata(manifest),
    findings,
    publishable: isPublishable(findings),
  };
}

/** Human-readable location suffix for a finding, or "" when global. */
export function findingLocation(finding: LintFinding): string {
  return finding.stepId !== undefined
    ? ` [step ${finding.stepId}, statement ${finding.statementIndex}]`
    : "";
}
