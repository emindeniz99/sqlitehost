/**
 * Demo admin CLI: loads a host manifest (the sample host by default)
 * and a script payload, prints a method/table reference from the
 * authoring metadata, and prints static lint findings with severities.
 *
 * Usage: demo.mjs <payload.json> [manifest.json]
 * Exit code: 0 when the payload is publishable (no errors), 1 otherwise,
 * 2 on usage/IO problems.
 */

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

import {
  isPublishable,
  lintScript,
  loadHostMetadata,
  parseHostManifest,
  type HostMetadata,
  type LintFinding,
} from "@sqlite-host/authoring";

const SAMPLE_MANIFEST_URL = new URL(
  "../../../fixtures/manifests/sample-host.manifest.json",
  import.meta.url,
);

function printReference(metadata: HostMetadata, print: (line: string) => void): void {
  print(`Host: ${metadata.namespace}.${metadata.interfaceName} (api level ${metadata.apiLevel})`);
  print(`Features: ${metadata.features.join(", ")}`);
  print("");
  print("Methods:");
  for (const method of metadata.methods) {
    print(
      `  ${method.methodName} (handler ${method.handlerName}, api level ${method.apiLevel})`,
    );
    print(`    call table:   ${method.callTable}`);
    print(`    result table: ${method.resultTable}`);
    print(`    trigger:      ${method.queueTrigger}`);
    for (const [property, column] of Object.entries(method.inputColumns)) {
      print(`    input  ${property} -> ${column}`);
    }
    for (const listField of method.inputListFields) {
      print(`    input  ${listField.propertyName}[] -> ${listField.childTable}`);
    }
    for (const [property, column] of Object.entries(method.resultColumns)) {
      print(`    result ${property} -> ${column}`);
    }
    for (const listField of method.resultListFields) {
      print(`    result ${listField.propertyName}[] -> ${listField.childTable}`);
    }
  }
  print("");
  print("Tables:");
  for (const table of metadata.tables) {
    print(`  ${table.name} (${table.columns.join(", ")})`);
  }
}

function printFindings(findings: LintFinding[], print: (line: string) => void): void {
  if (findings.length === 0) {
    print("No findings. Payload is publishable.");
    return;
  }
  for (const finding of findings) {
    const location =
      finding.stepId !== undefined
        ? ` [step ${finding.stepId}, statement ${finding.statementIndex}]`
        : "";
    print(`${finding.severity} ${finding.code}${location}: ${finding.message}`);
  }
  print(
    isPublishable(findings)
      ? "Warnings only. Payload is publishable."
      : "Payload is NOT publishable (errors present).",
  );
}

/** Run the demo; returns the process exit code. */
export function runDemo(argv: string[], print: (line: string) => void): number {
  const [payloadPath, manifestPath] = argv;
  if (payloadPath === undefined) {
    print("usage: demo.mjs <payload.json> [manifest.json]");
    return 2;
  }

  let manifestJson: string;
  let payloadJson: string;
  try {
    manifestJson = readFileSync(
      manifestPath ?? fileURLToPath(SAMPLE_MANIFEST_URL),
      "utf8",
    );
    payloadJson = readFileSync(payloadPath, "utf8");
  } catch (error) {
    print(`error: ${(error as Error).message}`);
    return 2;
  }

  const manifest = parseHostManifest(manifestJson);
  printReference(loadHostMetadata(manifest), print);

  print("");
  print(`Lint findings for ${payloadPath}:`);
  const findings = lintScript(JSON.parse(payloadJson), manifest);
  printFindings(findings, print);
  return isPublishable(findings) ? 0 : 1;
}
