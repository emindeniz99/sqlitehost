/**
 * Browser wiring for the admin demo: manifest + payload come from file
 * inputs or textareas, the method/table reference and lint findings are
 * rendered into the page. All DOM access happens inside initAdminDemo,
 * which only runs when a document exists — importing this module under
 * Node (the smoke test) is side-effect free.
 */

import { analyzePayload, findingLocation, type AnalysisResult } from "./logic.js";

export { analyzePayload, findingLocation } from "./logic.js";
export type { AnalysisResult } from "./logic.js";

function el(doc: Document, tag: string, className: string, text?: string): HTMLElement {
  const node = doc.createElement(tag);
  node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function renderReference(doc: Document, target: HTMLElement, result: AnalysisResult): void {
  const { metadata } = result;
  target.replaceChildren();
  target.append(
    el(
      doc,
      "h3",
      "host-title",
      `${metadata.namespace}.${metadata.interfaceName} (api level ${metadata.apiLevel})`,
    ),
    el(doc, "p", "host-features", `Features: ${metadata.features.join(", ")}`),
  );

  const methods = el(doc, "div", "methods");
  for (const method of metadata.methods) {
    const card = el(doc, "div", "method-card");
    card.append(
      el(
        doc,
        "h4",
        "method-name",
        `${method.methodName} (handler ${method.handlerName}, api level ${method.apiLevel})`,
      ),
    );
    const rows = el(doc, "ul", "method-rows");
    const row = (text: string) => rows.append(el(doc, "li", "method-row", text));
    row(`call table: ${method.callTable}`);
    row(`result table: ${method.resultTable}`);
    row(`trigger: ${method.queueTrigger}`);
    for (const [property, column] of Object.entries(method.inputColumns)) {
      row(`input ${property} -> ${column}`);
    }
    for (const listField of method.inputListFields) {
      row(`input ${listField.propertyName}[] -> ${listField.childTable}`);
    }
    for (const [property, column] of Object.entries(method.resultColumns)) {
      row(`result ${property} -> ${column}`);
    }
    for (const listField of method.resultListFields) {
      row(`result ${listField.propertyName}[] -> ${listField.childTable}`);
    }
    card.append(rows);
    methods.append(card);
  }
  target.append(methods);

  const tables = el(doc, "ul", "tables");
  for (const table of metadata.tables) {
    tables.append(el(doc, "li", "table-row", `${table.name} (${table.columns.join(", ")})`));
  }
  target.append(el(doc, "h4", "tables-title", "Tables"), tables);
}

function renderFindings(doc: Document, target: HTMLElement, result: AnalysisResult): void {
  target.replaceChildren();
  if (result.findings.length === 0) {
    target.append(el(doc, "p", "verdict ok", "No findings. Payload is publishable."));
    return;
  }
  const list = el(doc, "ul", "findings");
  for (const finding of result.findings) {
    const item = el(doc, "li", `finding ${finding.severity}`);
    item.append(
      el(doc, "span", `severity ${finding.severity}`, finding.severity),
      el(doc, "span", "code", ` ${finding.code}${findingLocation(finding)}: `),
      el(doc, "span", "message", finding.message),
    );
    list.append(item);
  }
  target.append(list);
  target.append(
    result.publishable
      ? el(doc, "p", "verdict ok", "Warnings only. Payload is publishable.")
      : el(doc, "p", "verdict blocked", "Payload is NOT publishable (errors present)."),
  );
}

function wireFileInput(input: HTMLInputElement, textarea: HTMLTextAreaElement): void {
  input.addEventListener("change", () => {
    const file = input.files?.[0];
    if (file === undefined) return;
    void file.text().then((content) => {
      textarea.value = content;
    });
  });
}

/** Wire the page. Exported for testability; auto-runs in a browser. */
export function initAdminDemo(doc: Document): void {
  const manifestText = doc.getElementById("manifest-text") as HTMLTextAreaElement;
  const payloadText = doc.getElementById("payload-text") as HTMLTextAreaElement;
  const referencePane = doc.getElementById("reference") as HTMLElement;
  const findingsPane = doc.getElementById("findings") as HTMLElement;
  const errorPane = doc.getElementById("input-error") as HTMLElement;

  wireFileInput(doc.getElementById("manifest-file") as HTMLInputElement, manifestText);
  wireFileInput(doc.getElementById("payload-file") as HTMLInputElement, payloadText);

  (doc.getElementById("lint-button") as HTMLButtonElement).addEventListener("click", () => {
    errorPane.textContent = "";
    try {
      const result = analyzePayload(manifestText.value, payloadText.value);
      renderReference(doc, referencePane, result);
      renderFindings(doc, findingsPane, result);
    } catch (error) {
      referencePane.replaceChildren();
      findingsPane.replaceChildren();
      errorPane.textContent = `error: ${(error as Error).message}`;
    }
  });
}

if (typeof document !== "undefined") {
  initAdminDemo(document);
}
