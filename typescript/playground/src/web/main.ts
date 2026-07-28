/**
 * Browser wiring for the playground: the textarea drives runPipeline on
 * a debounce, and the selected tab decides which generated artifact the
 * output pane shows. All DOM access happens inside initPlayground,
 * which only runs when a document exists — importing this module under
 * Node (the bundle smoke test) is side-effect free.
 */

import {
  CSHARP_PROFILES,
  DDL_FILE_NAME,
  MANIFEST_FILE_NAME,
  runPipeline,
  type PlaygroundDiagnostic,
  type PlaygroundFile,
  type PlaygroundResult,
} from "../pipeline.js";
import { SAMPLE_SOURCE } from "../browser-host.js";

export { CSHARP_PROFILES, runPipeline } from "../pipeline.js";
export { SAMPLE_SOURCE } from "../browser-host.js";

/** Milliseconds of quiet typing before the pipeline runs again. */
export const DEBOUNCE_MS = 400;

type TabId = "manifest" | "ddl" | "csharp" | "java" | "typescript";

const TABS: readonly { id: TabId; label: string }[] = [
  { id: "manifest", label: "Manifest" },
  { id: "ddl", label: "DDL" },
  { id: "csharp", label: "C#" },
  { id: "java", label: "Java" },
  { id: "typescript", label: "TypeScript" },
];

/**
 * The files a tab can show, or a single unnamed document. Keeping this
 * a plain projection of the pipeline output makes the rendering code
 * uniform across the five tabs.
 */
function filesForTab(result: PlaygroundResult, tab: TabId, profile: string): PlaygroundFile[] {
  if (!result.ok) return [];
  const { output } = result;
  switch (tab) {
    case "manifest":
      return [{ path: MANIFEST_FILE_NAME, contents: output.manifest }];
    case "ddl":
      return [{ path: DDL_FILE_NAME, contents: output.ddl }];
    case "csharp":
      return output.csharp[profile as (typeof CSHARP_PROFILES)[number]] ?? [];
    case "java":
      return output.java;
    case "typescript":
      return output.typescript;
  }
}

/** "12:5 invalid-ref:" — the position is dropped when there is none. */
function describe(diagnostic: PlaygroundDiagnostic): string {
  const position =
    diagnostic.line === undefined ? "" : `${diagnostic.line}:${diagnostic.column} `;
  return ` ${position}${diagnostic.code}: `;
}

/** Wire the page. Exported for testability; auto-runs in a browser. */
export function initPlayground(doc: Document): void {
  const sourceInput = doc.getElementById("source") as HTMLTextAreaElement;
  const tabBar = doc.getElementById("tabs") as HTMLElement;
  const profileSelect = doc.getElementById("profile") as HTMLSelectElement;
  const fileSelect = doc.getElementById("file") as HTMLSelectElement;
  const outputPane = doc.getElementById("output") as HTMLElement;
  const statusPane = doc.getElementById("status") as HTMLElement;
  const diagnosticsPane = doc.getElementById("diagnostics") as HTMLElement;

  let activeTab: TabId = "manifest";
  let activeFile = "";
  let latest: PlaygroundResult | undefined;
  // Monotonic run id: an older compile finishing after a newer one must
  // not overwrite the newer output.
  let runId = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;

  for (const { id, label } of TABS) {
    const button = doc.createElement("button");
    button.type = "button";
    button.className = "tab";
    button.textContent = label;
    button.setAttribute("role", "tab");
    button.dataset.tab = id;
    button.addEventListener("click", () => {
      activeTab = id;
      activeFile = "";
      render();
    });
    tabBar.append(button);
  }

  for (const profile of CSHARP_PROFILES) {
    const option = doc.createElement("option");
    option.value = profile;
    option.textContent = `profile: ${profile}`;
    profileSelect.append(option);
  }
  profileSelect.addEventListener("change", () => {
    activeFile = "";
    render();
  });
  fileSelect.addEventListener("change", () => {
    activeFile = fileSelect.value;
    render();
  });

  function renderDiagnostics(diagnostics: PlaygroundDiagnostic[]): void {
    diagnosticsPane.replaceChildren();
    if (diagnostics.length === 0) {
      const clean = doc.createElement("p");
      clean.className = "clean";
      clean.textContent = "No diagnostics.";
      diagnosticsPane.append(clean);
      return;
    }
    const list = doc.createElement("ul");
    for (const diagnostic of diagnostics) {
      const item = doc.createElement("li");
      const severity = doc.createElement("span");
      severity.className = `severity ${diagnostic.severity}`;
      severity.textContent = diagnostic.severity;
      const location = doc.createElement("span");
      location.className = "loc";
      location.textContent = describe(diagnostic);
      const message = doc.createElement("span");
      message.textContent = diagnostic.message;
      item.append(severity, location, message);
      list.append(item);
    }
    diagnosticsPane.append(list);
  }

  function render(): void {
    for (const button of tabBar.querySelectorAll("button")) {
      button.setAttribute("aria-selected", String(button.dataset.tab === activeTab));
    }
    profileSelect.hidden = activeTab !== "csharp";

    const result = latest;
    if (result === undefined) {
      outputPane.textContent = "";
      fileSelect.hidden = true;
      return;
    }

    renderDiagnostics(result.diagnostics);
    const files = filesForTab(result, activeTab, profileSelect.value);
    fileSelect.hidden = files.length < 2;
    fileSelect.replaceChildren();
    for (const file of files) {
      const option = doc.createElement("option");
      option.value = file.path;
      option.textContent = file.path;
      fileSelect.append(option);
    }
    if (files.length === 0) {
      outputPane.textContent = result.ok
        ? ""
        : "Compilation failed — see the diagnostics below.";
      return;
    }
    const selected = files.find((file) => file.path === activeFile) ?? files[0];
    activeFile = selected.path;
    fileSelect.value = selected.path;
    outputPane.textContent = selected.contents;
  }

  function compileNow(): void {
    const id = ++runId;
    statusPane.textContent = "compiling…";
    statusPane.classList.add("stale");
    void runPipeline(sourceInput.value).then(
      (result) => {
        if (id !== runId) return;
        latest = result;
        statusPane.textContent = result.ok ? "up to date" : "compile errors";
        statusPane.classList.remove("stale");
        render();
      },
      (error: unknown) => {
        if (id !== runId) return;
        statusPane.textContent = "pipeline error";
        statusPane.classList.remove("stale");
        outputPane.textContent = String(error);
      },
    );
  }

  sourceInput.addEventListener("input", () => {
    if (timer !== undefined) clearTimeout(timer);
    timer = setTimeout(compileNow, DEBOUNCE_MS);
  });

  sourceInput.value = SAMPLE_SOURCE;
  render();
  compileNow();
}

if (typeof document !== "undefined") {
  initPlayground(document);
}
