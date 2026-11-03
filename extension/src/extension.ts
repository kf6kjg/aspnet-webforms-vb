// VS Code wiring for stage 2: definition providers (Ctrl+click / F12) and the
// Visual Studio style View Code (F7) / View Markup (Shift+F7) commands. The logic lives
// in navigation.ts; this file only converts between offsets and editor positions.

import * as vscode from "vscode";
import { Host, Jump, codeBehindFor, definitionInCodeBehind, definitionInMarkup, markupFor } from "./navigation";
import { checkMarkup } from "./diagnostics";

const host: Host = {
  async readDir(dir) {
    try {
      return (await vscode.workspace.fs.readDirectory(vscode.Uri.file(dir))).map(([name]) => name);
    } catch {
      return undefined;
    }
  },
  async readText(file) {
    // Open documents first: unsaved edits win, and VS Code handles the encoding.
    try {
      return (await vscode.workspace.openTextDocument(vscode.Uri.file(file))).getText();
    } catch {
      return undefined;
    }
  },
};

async function toLinks(document: vscode.TextDocument, jump: Jump | undefined): Promise<vscode.LocationLink[]> {
  if (!jump) return [];
  const origin = new vscode.Range(document.positionAt(jump.origin.start), document.positionAt(jump.origin.end));
  const links: vscode.LocationLink[] = [];
  for (const t of jump.targets) {
    const uri = vscode.Uri.file(t.file);
    let range = new vscode.Range(0, 0, 0, 0);
    if (t.span) {
      const doc = await vscode.workspace.openTextDocument(uri);
      range = new vscode.Range(doc.positionAt(t.span.start), doc.positionAt(t.span.end));
    }
    links.push({ originSelectionRange: origin, targetUri: uri, targetRange: range, targetSelectionRange: range });
  }
  return links;
}

const codeBehindSelector: vscode.DocumentSelector = [
  { language: "vb", pattern: "**/*.aspx.vb" },
  { language: "vb", pattern: "**/*.ascx.vb" },
  { language: "vb", pattern: "**/*.master.vb" },
];

async function open(file: string | undefined, notFound: string): Promise<void> {
  if (file) await vscode.window.showTextDocument(vscode.Uri.file(file));
  else void vscode.window.showInformationMessage(notFound);
}

/** Markup reference problems for open .aspx/.ascx/.master documents. */
function registerDiagnostics(context: vscode.ExtensionContext): void {
  const collection = vscode.languages.createDiagnosticCollection("aspx-vb");
  const timers = new Map<string, NodeJS.Timeout>();

  const run = async (doc: vscode.TextDocument): Promise<void> => {
    const version = doc.version;
    const missingFiles = vscode.workspace.getConfiguration("aspxVb", doc.uri).get<boolean>("diagnostics.missingFiles", true);
    const found = await checkMarkup(host, doc.uri.fsPath, doc.getText(), { missingFiles });
    if (doc.isClosed || doc.version !== version) return;
    collection.set(
      doc.uri,
      found.map((p) => {
        const range = new vscode.Range(doc.positionAt(p.span.start), doc.positionAt(p.span.end));
        const d = new vscode.Diagnostic(range, p.message, p.severity === "error" ? vscode.DiagnosticSeverity.Error : vscode.DiagnosticSeverity.Warning);
        d.source = "ASP.NET";
        d.code = p.code;
        return d;
      }),
    );
  };
  const schedule = (doc: vscode.TextDocument): void => {
    if (doc.languageId !== "aspx-vb") return;
    const key = doc.uri.toString();
    clearTimeout(timers.get(key));
    timers.set(key, setTimeout(() => { timers.delete(key); void run(doc); }, 400));
  };
  // A code-behind edit or a file appearing/disappearing can change any open page's result.
  const recheckAll = (): void => vscode.workspace.textDocuments.forEach(schedule);

  context.subscriptions.push(
    collection,
    vscode.workspace.onDidOpenTextDocument(schedule),
    vscode.workspace.onDidChangeTextDocument((e) => (e.document.languageId === "aspx-vb" ? schedule(e.document) : e.document.languageId === "vb" && recheckAll())),
    vscode.workspace.onDidSaveTextDocument(recheckAll),
    vscode.workspace.onDidCloseTextDocument((doc) => collection.delete(doc.uri)),
    vscode.workspace.onDidCreateFiles(recheckAll),
    vscode.workspace.onDidDeleteFiles(recheckAll),
    vscode.workspace.onDidRenameFiles(recheckAll),
    vscode.workspace.onDidChangeConfiguration((e) => e.affectsConfiguration("aspxVb") && recheckAll()),
  );
  recheckAll();
}

export function activate(context: vscode.ExtensionContext): void {
  registerDiagnostics(context);
  context.subscriptions.push(
    vscode.languages.registerDefinitionProvider(
      { language: "aspx-vb" },
      { provideDefinition: async (doc, pos) => toLinks(doc, await definitionInMarkup(host, doc.uri.fsPath, doc.getText(), doc.offsetAt(pos))) },
    ),
    vscode.languages.registerDefinitionProvider(codeBehindSelector, {
      provideDefinition: async (doc, pos) => toLinks(doc, await definitionInCodeBehind(host, doc.uri.fsPath, doc.getText(), doc.offsetAt(pos))),
    }),
    vscode.commands.registerCommand("aspxVb.viewCode", async () => {
      const doc = vscode.window.activeTextEditor?.document;
      if (!doc) return;
      await open(await codeBehindFor(host, doc.uri.fsPath, doc.getText()), "No code-behind file found for this page.");
    }),
    vscode.commands.registerCommand("aspxVb.viewMarkup", async () => {
      const doc = vscode.window.activeTextEditor?.document;
      if (!doc) return;
      await open(await markupFor(host, doc.uri.fsPath), "No markup file found for this code-behind.");
    }),
  );
}

export function deactivate(): void {}
