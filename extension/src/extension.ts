// VS Code wiring. Stage 2: definition providers (Ctrl+click / F12), markup reference
// checks, and the Visual Studio style View Code (F7) / View Markup (Shift+F7) commands;
// the logic lives in navigation.ts and diagnostics.ts. Stage 3: starts the language server
// (server/AspxVb.Server) for completion, diagnostics, definition and hover in VB files.

import * as fs from "node:fs";
import * as path from "node:path";
import * as vscode from "vscode";
import { LanguageClient, LanguageClientOptions, ServerOptions, TransportKind } from "vscode-languageclient/node";
import { Host, Jump, codeBehindFor, definitionInCodeBehind, definitionInMarkup, markupFor } from "./navigation";
import { checkMarkup } from "./diagnostics";

let client: LanguageClient | undefined;

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
    // Handles -> markup control; the language server answers this itself once it's running.
    vscode.languages.registerDefinitionProvider(codeBehindSelector, {
      provideDefinition: async (doc, pos) =>
        client?.isRunning() ? [] : toLinks(doc, await definitionInCodeBehind(host, doc.uri.fsPath, doc.getText(), doc.offsetAt(pos))),
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
  context.subscriptions.push(
    // Go to definition on framework/Bin symbols opens a read-only view generated by the server.
    vscode.workspace.registerTextDocumentContentProvider("aspxvb-metadata", {
      provideTextDocumentContent: async (uri) =>
        client ? (await client.sendRequest<{ text: string }>("aspxVb/metadataSource", { uri: uri.toString() })).text : "",
    }),
    // New settings take effect by restarting the server (sites are rebuilt with them).
    vscode.workspace.onDidChangeConfiguration(async (e) => {
      if (!e.affectsConfiguration("aspxVb.assemblyFolders") && !e.affectsConfiguration("aspxVb.server.path")) return;
      const old = client;
      client = undefined;
      await old?.stop();
      await startLanguageServer(context);
    }),
  );
  registerEndConstructs(context);
  void startLanguageServer(context);
}

/**
 * Enter after a block opener (If … Then, Sub, For, …), including one that splits a
 * single-line If: ask the server for the missing end, apply its edit, and put the cursor
 * where it says (ADR 0009). This isn't an on-type formatting edit because VS Code would
 * shift that insertion onto the cursor and carry the cursor past it.
 */
function registerEndConstructs(context: vscode.ExtensionContext): void {
  context.subscriptions.push(
    vscode.workspace.onDidChangeTextDocument((e) => {
      const editor = vscode.window.activeTextEditor;
      if (!client?.isRunning() || e.document.languageId !== "vb" || e.reason !== undefined) return;
      if (editor?.document !== e.document || editor.selections.length !== 1 || e.contentChanges.length !== 1) return;
      const change = e.contentChanges[0];
      const indent = /^\r?\n([ \t]*)$/.exec(change.text);
      if (!indent) return;
      const position = new vscode.Position(change.range.start.line + 1, indent[1].length);
      // After every listener has seen the change, so the client has queued it ahead of the request.
      setTimeout(() => void insertEnd(editor, position), 0);
    }),
  );
}

type LspPosition = { line: number; character: number };

async function insertEnd(editor: vscode.TextEditor, position: vscode.Position): Promise<void> {
  const doc = editor.document;
  const lineCount = doc.lineCount;
  const line = doc.lineAt(position.line).text;
  const edit = await client?.sendRequest<{ range: { start: LspPosition; end: LspPosition }; newText: string; cursor: LspPosition } | null>(
    "aspxVb/endConstruct",
    { textDocument: { uri: doc.uri.toString() }, position },
  );
  // Pretty listing may re-case the opener meanwhile; give up only if lines moved or the new line was typed on.
  if (!edit || doc.isClosed || doc.lineCount !== lineCount || doc.lineAt(position.line).text !== line) return;
  const at = (p: LspPosition): vscode.Position => new vscode.Position(p.line, p.character);
  if (await editor.edit((b) => b.replace(new vscode.Range(at(edit.range.start), at(edit.range.end)), edit.newText))) {
    editor.selection = new vscode.Selection(at(edit.cursor), at(edit.cursor));
  }
}

const extensionId = "kf6kjg.aspnet-webforms-vb";

/**
 * A .NET 10+ `dotnet` for the server, via the .NET Install Tool (ADR 0007): an existing
 * install if one qualifies, otherwise a private runtime it downloads and keeps updated.
 */
async function acquireDotnet(): Promise<string | undefined> {
  try {
    const found = await vscode.commands.executeCommand<{ dotnetPath: string } | undefined>("dotnet.findPath", {
      acquireContext: { version: "10.0", requestingExtensionId: extensionId, architecture: process.arch, mode: "runtime" },
      versionSpecRequirement: "greater_than_or_equal",
      rejectPreviews: true,
    });
    if (found?.dotnetPath) return found.dotnetPath;
    const acquired = await vscode.commands.executeCommand<{ dotnetPath: string } | undefined>("dotnet.acquire", {
      version: "10.0",
      requestingExtensionId: extensionId,
      mode: "runtime",
    });
    return acquired?.dotnetPath;
  } catch (e) {
    console.error("aspx-vb: .NET Install Tool failed", e);
    return undefined;
  }
}

/** The server shipped in the extension, or the aspxVb.server.path override (a .dll, or an executable run as-is). */
async function serverCommand(context: vscode.ExtensionContext): Promise<{ command: string; args: string[] } | string> {
  const configured = vscode.workspace.getConfiguration("aspxVb").get<string>("server.path", "").trim();
  const file = configured || context.asAbsolutePath(path.join("server", "AspxVb.Server.dll"));
  if (!fs.existsSync(file)) return `language server not found at ${file}`;
  if (!file.endsWith(".dll")) return { command: file, args: [] };
  const dotnet = await acquireDotnet();
  return dotnet ? { command: dotnet, args: [file] } : "couldn't get a .NET runtime from the .NET Install Tool (see its output panel)";
}

/** aspxVb.assemblyFolders with ~ expanded. */
function assemblyFolders(): string[] {
  const home = process.env.HOME ?? "";
  return vscode.workspace.getConfiguration("aspxVb").get<string[]>("assemblyFolders", [])
    .map((f) => f.trim())
    .filter((f) => f.length > 0)
    .map((f) => (f === "~" || f.startsWith("~/") ? path.join(home, f.slice(1)) : f));
}

async function startLanguageServer(context: vscode.ExtensionContext): Promise<void> {
  const command = await serverCommand(context);
  if (typeof command === "string") {
    void vscode.window.showWarningMessage(`ASP.NET Web Forms (VB): ${command}, so IntelliSense and VB error marking are off. Highlighting and navigation still work.`);
    return;
  }
  const serverOptions: ServerOptions = { ...command, transport: TransportKind.stdio };
  const clientOptions: LanguageClientOptions = {
    // Markup is synced too, so edits to controls update the code-behind's view of them.
    documentSelector: [
      { scheme: "file", language: "vb" },
      { scheme: "file", language: "aspx-vb" },
    ],
    initializationOptions: { assemblyFolders: assemblyFolders() },
    outputChannelName: "ASP.NET Web Forms (VB) language server",
  };
  client = new LanguageClient("aspxVb", "ASP.NET Web Forms (VB)", serverOptions, clientOptions);
  client.onNotification("aspxVb/missingAssemblies", (p: { siteRoot: string; assemblies: string[] }) => void reportMissingAssemblies(context, p));
  await client.start();
}

/** web.config names assemblies the editor can't find and a file has errors: explain how to provide them (ADR 0007). */
async function reportMissingAssemblies(context: vscode.ExtensionContext, p: { siteRoot: string; assemblies: string[] }): Promise<void> {
  const muted = context.globalState.get<string[]>("aspxVb.mutedMissingAssemblies", []);
  const names = p.assemblies.filter((a) => !muted.includes(a));
  if (names.length === 0) return;
  const list = names.join(", ");
  const choice = await vscode.window.showWarningMessage(
    `${path.basename(p.siteRoot)}: web.config references ${list}, which the editor can't find, so code using ${names.length === 1 ? "it" : "them"} shows errors.`,
    "How to fix",
    "Don't show again",
  );
  if (choice === "How to fix") {
    await vscode.commands.executeCommand("markdown.showPreview", vscode.Uri.joinPath(context.extensionUri, "MISSING-ASSEMBLIES.md"));
  } else if (choice === "Don't show again") {
    await context.globalState.update("aspxVb.mutedMissingAssemblies", [...muted, ...names]);
  }
}

export function deactivate(): Thenable<void> | undefined {
  return client?.stop();
}
