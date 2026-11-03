using System.Collections.Concurrent;
using System.Text.Json;
using AspxVb.Server.Features;
using AspxVb.Server.Site;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Threading;
using StreamJsonRpc;

namespace AspxVb.Server.Lsp;

/// <summary>
/// The Language Server Protocol surface: document sync, completion (+ resolve), definition,
/// hover, and diagnostics for .vb files of Web Sites. Markup documents are synced too, so
/// edits to controls update the generated partial class; their own checks run in the extension.
/// </summary>
public sealed class LspServer
{
    private readonly JsonRpc _rpc;
    private SiteRegistry _sites = new([]);
    private readonly ConcurrentDictionary<string, bool> _missingAssembliesReported = new(StringComparer.Ordinal);
    private readonly LanguageFeatures _features = new();
    private readonly ConcurrentDictionary<string, string> _openFiles = new(StringComparer.Ordinal); // path -> uri
    private readonly ConcurrentDictionary<string, string> _paths = new(StringComparer.Ordinal); // uri -> path on disk
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingDiagnostics = new(StringComparer.Ordinal);

    private LspServer(Stream input, Stream output)
    {
        var formatter = new SystemTextJsonFormatter();
        formatter.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        formatter.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        _rpc = new JsonRpc(new HeaderDelimitedMessageHandler(output, input, formatter));
        // Handle messages in arrival order, so a completion request sees the edit sent before it.
        _rpc.SynchronizationContext = new NonConcurrentSynchronizationContext(sticky: false);
        _rpc.AddLocalRpcTarget(this, new JsonRpcTargetOptions { UseSingleObjectParameterDeserialization = true });
    }

    public static async Task RunAsync()
    {
        var server = new LspServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
        server._rpc.StartListening();
        await server._rpc.Completion;
    }

    private static void Log(string message) => Console.Error.WriteLine(message);

    // ---- lifecycle ----

    [JsonRpcMethod("initialize")]
    public object Initialize(JsonElement p)
    {
        // initializationOptions.assemblyFolders: where to find assemblies the server loads from its GAC (ADR 0007).
        var folders = p.TryGetProperty("initializationOptions", out var options) && options.ValueKind == JsonValueKind.Object
                      && options.TryGetProperty("assemblyFolders", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToList()
            : [];
        _sites = new SiteRegistry(folders);
        return Capabilities();
    }

    private static object Capabilities() => new
    {
        capabilities = new
        {
            textDocumentSync = new { openClose = true, change = 1, save = new { includeText = false } },
            completionProvider = new { triggerCharacters = new[] { "." }, resolveProvider = true },
            definitionProvider = true,
            hoverProvider = true,
            documentFormattingProvider = true,
            documentRangeFormattingProvider = true,
            // Pretty listing: Enter formats the line just finished (ADR 0008).
            documentOnTypeFormattingProvider = new { firstTriggerCharacter = "\n" },
        },
        serverInfo = new { name = "aspx-vb", version = typeof(LspServer).Assembly.GetName().Version?.ToString() },
    };

    [JsonRpcMethod("initialized")]
    public void Initialized(JsonElement _) { }

    [JsonRpcMethod("shutdown")]
    public object? Shutdown() => null;

    [JsonRpcMethod("exit")]
    public void Exit() => Environment.Exit(0);

    // ---- document sync ----

    [JsonRpcMethod("textDocument/didOpen")]
    public void DidOpen(JsonElement p)
    {
        var doc = p.GetProperty("textDocument");
        var uri = doc.GetProperty("uri").GetString()!;
        if (PathOf(uri) is not { } path || _sites.SiteFor(path) is not { } site) return;
        _openFiles[path] = uri;
        site.SetText(path, doc.GetProperty("text").GetString()!);
        ScheduleDiagnostics(site);
    }

    [JsonRpcMethod("textDocument/didChange")]
    public void DidChange(JsonElement p)
    {
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
        var changes = p.GetProperty("contentChanges");
        if (PathOf(uri) is not { } path || _sites.SiteFor(path) is not { } site || changes.GetArrayLength() == 0) return;
        site.SetText(path, changes[changes.GetArrayLength() - 1].GetProperty("text").GetString()!);
        ScheduleDiagnostics(site);
    }

    [JsonRpcMethod("textDocument/didSave")]
    public void DidSave(JsonElement p)
    {
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
        if (PathOf(uri) is { } path && _sites.SiteFor(path) is { } site) ScheduleDiagnostics(site);
    }

    [JsonRpcMethod("textDocument/didClose")]
    public void DidClose(JsonElement p)
    {
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
        if (PathOf(uri) is not { } path) return;
        _openFiles.TryRemove(path, out _);
        if (_sites.SiteFor(path) is { } site)
        {
            site.Close(path);
            ScheduleDiagnostics(site);
        }
        _ = _rpc.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics", new { uri, diagnostics = Array.Empty<object>() });
    }

    // ---- diagnostics ----

    /// <summary>Recompute every open .vb file of the site shortly after the last edit (any edit can affect the others).</summary>
    private void ScheduleDiagnostics(SiteWorkspace site)
    {
        var cts = new CancellationTokenSource();
        if (_pendingDiagnostics.TryGetValue(site.Root, out var previous)) previous.Cancel();
        _pendingDiagnostics[site.Root] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token);
                foreach (var (path, uri) in _openFiles.ToArray())
                {
                    if (!path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) || !path.StartsWith(site.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                    var doc = site.GetDocument(path);
                    var problems = doc == null ? [] : await _features.DiagnosticsAsync(doc, cts.Token);
                    cts.Token.ThrowIfCancellationRequested();
                    // Errors in a site whose web.config names assemblies we can't find: tell the user how to provide them (once per site).
                    if (problems.Any(d => d.IsError) && site.Settings.UnresolvedAssemblies.Count > 0 && _missingAssembliesReported.TryAdd(site.Root, true))
                        await _rpc.NotifyWithParameterObjectAsync("aspxVb/missingAssemblies", new { siteRoot = site.Root, assemblies = site.Settings.UnresolvedAssemblies });
                    await _rpc.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics", new
                    {
                        uri,
                        diagnostics = problems.Select(d => new
                        {
                            range = Range(d.Range),
                            severity = d.IsError ? 1 : 2,
                            code = d.Code,
                            source = "vbc",
                            message = d.Message,
                        }),
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log($"diagnostics: {e}"); }
        });
    }

    // ---- language features ----

    private (SiteWorkspace Site, Microsoft.CodeAnalysis.Document Document, int Offset)? Locate(JsonElement p)
    {
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
        if (PathOf(uri) is not { } path || _sites.SiteFor(path) is not { } site || site.GetDocument(path) is not { } doc) return null;
        var pos = p.GetProperty("position");
        if (!doc.TryGetText(out var text)) text = doc.GetTextAsync().Result;
        var line = Math.Min(pos.GetProperty("line").GetInt32(), text.Lines.Count - 1);
        var offset = Math.Min(text.Lines[line].Start + pos.GetProperty("character").GetInt32(), text.Lines[line].End);
        return (site, doc, offset);
    }

    [JsonRpcMethod("textDocument/completion")]
    public async Task<object?> Completion(JsonElement p, CancellationToken ct)
    {
        if (Locate(p) is not { } at) return null;
        var (items, incomplete, span) = await _features.CompletionAsync(at.Document, at.Offset, ct);
        var text = await at.Document.GetTextAsync(ct);
        var range = RangeOf(text, span);
        return new
        {
            isIncomplete = incomplete,
            items = items.Select(i => new
            {
                label = i.Label,
                kind = i.Kind,
                sortText = i.SortText,
                filterText = i.FilterText,
                textEdit = new { range, newText = i.InsertText },
                data = i.Index,
            }),
        };
    }

    [JsonRpcMethod("completionItem/resolve")]
    public async Task<JsonElement> CompletionResolve(JsonElement item, CancellationToken ct)
    {
        if (!item.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Number) return item;
        var detail = await _features.CompletionDetailAsync(data.GetInt32(), ct);
        if (detail == null) return item;
        var resolved = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.GetRawText())!;
        resolved["documentation"] = JsonSerializer.SerializeToElement(new { kind = "markdown", value = $"```vb\n{detail}\n```" });
        return JsonSerializer.SerializeToElement(resolved);
    }

    [JsonRpcMethod("textDocument/definition")]
    public async Task<object> Definition(JsonElement p, CancellationToken ct)
    {
        if (Locate(p) is not { } at) return Array.Empty<object>();
        var spans = await _features.DefinitionAsync(at.Site, at.Document, at.Offset, ct);
        return spans.Select(s => new { uri = s.File.StartsWith("aspxvb-metadata:") ? s.File : new Uri(s.File).AbsoluteUri, range = Range(s) }).ToList();
    }

    [JsonRpcMethod("textDocument/hover")]
    public async Task<object?> Hover(JsonElement p, CancellationToken ct)
    {
        if (Locate(p) is not { } at) return null;
        var markdown = await _features.HoverAsync(at.Document, at.Offset, ct);
        return markdown == null ? null : new { contents = new { kind = "markdown", value = markdown } };
    }

    // ---- formatting (ADR 0008) ----

    private (Microsoft.CodeAnalysis.Document Document, SourceText Text)? DocumentOf(JsonElement p)
    {
        var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
        if (PathOf(uri) is not { } path || _sites.SiteFor(path) is not { } site || site.GetDocument(path) is not { } doc) return null;
        return (doc, doc.GetTextAsync().Result);
    }

    private static int Offset(SourceText text, JsonElement position)
    {
        var line = Math.Min(position.GetProperty("line").GetInt32(), text.Lines.Count - 1);
        return Math.Min(text.Lines[line].Start + position.GetProperty("character").GetInt32(), text.Lines[line].End);
    }

    private async Task<object> EditsAsync(Microsoft.CodeAnalysis.Document doc, SourceText text, TextSpan span, CancellationToken ct) =>
        (await Formatting.FormatAsync(doc, span, ct)).Select(c => new { range = RangeOf(text, c.Span), newText = c.NewText }).ToList();

    [JsonRpcMethod("textDocument/formatting")]
    public async Task<object> FormatDocument(JsonElement p, CancellationToken ct) =>
        DocumentOf(p) is { } d ? await EditsAsync(d.Document, d.Text, new TextSpan(0, d.Text.Length), ct) : Array.Empty<object>();

    [JsonRpcMethod("textDocument/rangeFormatting")]
    public async Task<object> FormatRange(JsonElement p, CancellationToken ct)
    {
        if (DocumentOf(p) is not { } d) return Array.Empty<object>();
        var range = p.GetProperty("range");
        var start = Offset(d.Text, range.GetProperty("start"));
        var end = Offset(d.Text, range.GetProperty("end"));
        return await EditsAsync(d.Document, d.Text, TextSpan.FromBounds(start, Math.Max(start, end)), ct);
    }

    /// <summary>Enter was pressed: format the line that was just finished, as Visual Studio's pretty listing does.</summary>
    [JsonRpcMethod("textDocument/onTypeFormatting")]
    public async Task<object> FormatOnType(JsonElement p, CancellationToken ct)
    {
        if (DocumentOf(p) is not { } d) return Array.Empty<object>();
        var line = p.GetProperty("position").GetProperty("line").GetInt32() - 1;
        if (line < 0 || line >= d.Text.Lines.Count) return Array.Empty<object>();
        return await EditsAsync(d.Document, d.Text, d.Text.Lines[line].Span, ct);
    }

    /// <summary>
    /// Enter was pressed after a block opener: the edit that adds its end (End If, End Sub,
    /// Next, …) below the cursor's line, and the cursor's new position, or null. This is not
    /// part of on-type formatting because VS Code shrinks formatting edits to a minimal diff.
    /// An insertion at the start of the next line then becomes an insertion at the cursor,
    /// and the cursor ends up after the end (ADR 0009). The extension applies the edit and
    /// puts the cursor back.
    /// </summary>
    [JsonRpcMethod("aspxVb/endConstruct")]
    public async Task<object?> EndConstruct(JsonElement p, CancellationToken ct)
    {
        if (DocumentOf(p) is not { } d) return null;
        var line = p.GetProperty("position").GetProperty("line").GetInt32() - 1;
        if (line < 0 || line >= d.Text.Lines.Count) return null;
        // The opener's indentation after pretty listing; the on-type edit may not have reached us yet.
        var span = d.Text.Lines[line].Span;
        var finished = (await Formatting.FormatAsync(d.Document, span, ct)).FirstOrDefault(c => c.Span == span).NewText ?? d.Text.ToString(span);
        var indent = finished[..(finished.Length - finished.TrimStart(' ', '\t').Length)];
        if (await d.Document.GetSyntaxRootAsync(ct) is not { } root || EndConstructs.AfterEnter(root, d.Text, line, indent) is not { } end) return null;
        var cursor = d.Text.WithChanges(end.Change).Lines.GetLinePosition(end.Cursor);
        return new { range = RangeOf(d.Text, end.Change.Span), newText = end.Change.NewText, cursor = new { line = cursor.Line, character = cursor.Character } };
    }

    /// <summary>Text of a generated metadata view (go to definition on a framework or Bin symbol).</summary>
    [JsonRpcMethod("aspxVb/metadataSource")]
    public object MetadataSource(JsonElement p) =>
        new { text = _features.MetadataViewText(p.GetProperty("uri").GetString()!) ?? "' This view is no longer available. Go to definition again to regenerate it." };

    // ---- helpers ----

    /// <summary>The file's path as spelled on disk, so it matches the paths the sites find themselves (ADR 0012).</summary>
    private string? PathOf(string uri)
    {
        if (_paths.TryGetValue(uri, out var known)) return known;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || !u.IsFile) return null;
        // A file not saved yet keeps its URI's spelling, and is looked up again once it exists.
        if (FileText.OnDisk(u.LocalPath) is not { } path) return u.LocalPath;
        return _paths[uri] = path;
    }

    private static object Range(Features.Span s) => new
    {
        start = new { line = s.StartLine, character = s.StartChar },
        end = new { line = s.EndLine, character = s.EndChar },
    };

    private static object RangeOf(SourceText text, TextSpan span)
    {
        var s = text.Lines.GetLinePosition(span.Start);
        var e = text.Lines.GetLinePosition(span.End);
        return new { start = new { line = s.Line, character = s.Character }, end = new { line = e.Line, character = e.Character } };
    }
}
