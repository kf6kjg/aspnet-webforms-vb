using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace AspxVb.Server.Site;

/// <summary>
/// The in-memory Roslyn workspace for one Web Site (ADR 0002, ADR 0004): App_Code is one
/// project; each page or user control is its own project holding its CodeFile code-behind
/// plus the generated control partial, referencing App_Code and the user controls it
/// registers. Page projects are created on first use. Nothing is written to disk.
/// </summary>
public sealed class SiteWorkspace
{
    private static readonly Lazy<ControlTypes> Types = new(() =>
        new ControlTypes(VisualBasicCompilation.Create("framework", references: CompilationSettings.FrameworkOnly())));

    private sealed class PageProject
    {
        public required string MarkupPath { get; init; }
        public required ProjectId ProjectId { get; init; }
        public required string Inherits { get; init; }
        public required string CodeBehindPath { get; init; }
        public required DocumentId VirtualDocument { get; init; }
        public required string ShapeKey { get; init; }
        public required List<string> Controls { get; init; }
        public IReadOnlyList<FieldOrigin> Fields { get; set; } = [];
    }

    private readonly object _gate = new();
    private readonly ProjectId _appCode;
    private readonly string _appCodeDir;
    private readonly Dictionary<string, PageProject> _pages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DocumentId> _documents = new(StringComparer.Ordinal);
    private readonly Dictionary<DocumentId, PageProject> _virtualDocuments = [];
    private readonly Dictionary<DocumentId, string> _webReferenceDocuments = []; // generated proxy -> .wsdl
    private readonly Dictionary<string, string> _overlays = new(StringComparer.Ordinal);
    private int _assemblyCounter;

    public string Root { get; }
    public AdhocWorkspace Workspace { get; } = new(MefHostServices.DefaultHost);
    public CompilationSettings Settings { get; }

    public SiteWorkspace(string root, IReadOnlyList<string>? assemblyFolders = null)
    {
        Root = root;
        Settings = new CompilationSettings(root, WebConfig.Load(root), assemblyFolders ?? []);
        _appCodeDir = FileText.OnDisk(Path.Combine(root, "App_Code")) ?? Path.Combine(root, "App_Code");
        _appCode = ProjectId.CreateNewId("App_Code");
        var docs = Directory.Exists(_appCodeDir)
            ? Directory.EnumerateFiles(_appCodeDir, "*.vb", SearchOption.AllDirectories).Select(f => DocumentFor(_appCode, f, Read(f))).ToList()
            : [];
        // Web reference proxies compile with App_Code, as App_WebReferences does on the server.
        foreach (var proxy in WebReferences.Generate(root))
        {
            var path = proxy.WsdlPath + ".g.vb";
            var info = DocumentInfo.Create(DocumentId.CreateNewId(_appCode, path), Path.GetFileName(path), filePath: path,
                loader: TextLoader.From(TextAndVersion.Create(SourceText.From(proxy.Code), VersionStamp.Create())));
            docs.Add(info);
            _webReferenceDocuments[info.Id] = proxy.WsdlPath;
        }
        Workspace.AddProject(ProjectInfo.Create(_appCode, VersionStamp.Create(), "App_Code", "App_Code", LanguageNames.VisualBasic,
            compilationOptions: Settings.Options, parseOptions: Settings.ParseOptions, documents: docs, metadataReferences: Settings.References)
            .WithAnalyzerConfigDocuments([EditorConfigFor(_appCode)]));
    }

    private DocumentInfo DocumentFor(ProjectId project, string path, string text)
    {
        var info = DocumentInfo.Create(DocumentId.CreateNewId(project, path), Path.GetFileName(path), filePath: path,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create(), path)));
        _documents[path] = info.Id;
        return info;
    }

    /// <summary>The developer's formatting settings, as an in-memory .editorconfig at the site root (ADR 0008).</summary>
    private DocumentInfo EditorConfigFor(ProjectId project)
    {
        var path = Path.Combine(Root, ".editorconfig");
        return DocumentInfo.Create(DocumentId.CreateNewId(project, path), ".editorconfig", filePath: path,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(Features.Formatting.EditorConfig), VersionStamp.Create())));
    }

    private string Read(string path) => _overlays.TryGetValue(path, out var text) ? text : FileText.Read(path);

    private bool InAppCode(string path) => path.StartsWith(_appCodeDir + "/", StringComparison.OrdinalIgnoreCase);

    private static bool IsMarkup(string path) => Path.GetExtension(path).ToLowerInvariant() is ".aspx" or ".ascx" or ".master";

    /// <summary>The Roslyn document for a real .vb file of this site, creating its project if needed; null when it isn't compiled (loose or backup files).</summary>
    public Document? GetDocument(string path)
    {
        lock (_gate)
        {
            if (!_documents.ContainsKey(path))
            {
                if (InAppCode(path) && path.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) && (File.Exists(path) || _overlays.ContainsKey(path)))
                    ApplyOrThrow(Workspace.CurrentSolution.AddDocument(DocumentFor(_appCode, path, Read(path))));
                else
                    foreach (var markup in MarkupCandidatesFor(path)) if (EnsurePage(markup, []) is { CodeBehindPath: var cb } && cb == path) break;
            }
            return _documents.TryGetValue(path, out var id) ? Workspace.CurrentSolution.GetDocument(id) : null;
        }
    }

    /// <summary>Buy.aspx.vb → Buy.aspx first (the convention); otherwise any markup in the folder whose CodeFile names it.</summary>
    private IEnumerable<string> MarkupCandidatesFor(string codeBehind)
    {
        if (!codeBehind.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)) yield break;
        var conventional = codeBehind[..^3];
        if (IsMarkup(conventional) && File.Exists(conventional)) yield return conventional;
        var dir = Path.GetDirectoryName(codeBehind)!;
        foreach (var m in Directory.EnumerateFiles(dir).Where(IsMarkup).Where(m => m != conventional))
            if (Markup.MainDirective(Read(m))?.Get("CodeFile") is { } cf && FileText.Resolve(cf.Value, m, Root) == codeBehind)
                yield return m;
    }

    /// <summary>Makes sure a page/control project exists for a markup file (used for check mode and markup edits).</summary>
    public bool EnsureMarkup(string markupPath)
    {
        lock (_gate) return EnsurePage(markupPath, []) != null;
    }

    private string ShapeOf(string markupPath, string text)
    {
        var main = Markup.MainDirective(text);
        var regs = Markup.Registrations(text).Select(r => $"{r.TagPrefix}:{r.TagName}={FileText.Resolve(r.Src.Value, markupPath, Root)}");
        return $"{main?.Get("CodeFile")?.Value}|{main?.Get("Inherits")?.Value}|{string.Join(";", regs)}";
    }

    private PageProject? EnsurePage(string markupPath, HashSet<string> visiting)
    {
        if (_pages.TryGetValue(markupPath, out var existing)) return existing;
        if (!visiting.Add(markupPath) || !File.Exists(markupPath) && !_overlays.ContainsKey(markupPath)) return null;

        var text = Read(markupPath);
        var main = Markup.MainDirective(text);
        var inherits = main?.Get("Inherits")?.Value;
        var codeBehind = main?.Get("CodeFile") is { } cf ? FileText.Resolve(cf.Value, markupPath, Root) : null;
        // No code-behind, a C# one, or one already claimed by another markup file (backup copies sharing a CodeFile).
        if (inherits == null || codeBehind == null || !codeBehind.EndsWith(".vb", StringComparison.OrdinalIgnoreCase) || _documents.ContainsKey(codeBehind))
            return null;

        var controls = new List<PageProject>();
        var userControlTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in Markup.Registrations(text))
        {
            if (FileText.Resolve(reg.Src.Value, markupPath, Root) is not { } controlMarkup) continue;
            if (EnsurePage(controlMarkup, visiting) is not { } control) continue;
            controls.Add(control);
            userControlTypes[$"{reg.TagPrefix}:{reg.TagName}"] = control.Inherits;
        }

        var generated = Generate(markupPath, text, inherits, userControlTypes);
        var projectId = ProjectId.CreateNewId(Path.GetRelativePath(Root, markupPath));
        var codeDoc = DocumentFor(projectId, codeBehind, Read(codeBehind));
        var virtualPath = markupPath + ".g.vb";
        var virtualDoc = DocumentInfo.Create(DocumentId.CreateNewId(projectId, virtualPath), Path.GetFileName(virtualPath), filePath: virtualPath,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(generated.Text), VersionStamp.Create())));
        var refs = new List<ProjectReference> { new(_appCode) };
        refs.AddRange(controls.Select(c => new ProjectReference(c.ProjectId)));
        Workspace.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), Path.GetRelativePath(Root, markupPath),
            $"App_Web_{++_assemblyCounter}", LanguageNames.VisualBasic,
            compilationOptions: Settings.Options, parseOptions: Settings.ParseOptions,
            documents: [codeDoc, virtualDoc], projectReferences: refs, metadataReferences: Settings.References)
            .WithAnalyzerConfigDocuments([EditorConfigFor(projectId)]));

        var page = new PageProject
        {
            MarkupPath = markupPath, ProjectId = projectId, Inherits = inherits, CodeBehindPath = codeBehind,
            VirtualDocument = virtualDoc.Id, ShapeKey = ShapeOf(markupPath, text), Controls = controls.Select(c => c.MarkupPath).ToList(),
            Fields = generated.Fields,
        };
        _pages[markupPath] = page;
        _virtualDocuments[virtualDoc.Id] = page;
        return page;
    }

    private static GeneratedPartial Generate(string markupPath, string text, string inherits, Dictionary<string, string> userControlTypes) =>
        VirtualPartial.Generate(Path.GetFileName(markupPath), inherits, Markup.ServerControls(text), Types.Value,
            (prefix, name) => userControlTypes.GetValueOrDefault($"{prefix}:{name}"));

    /// <summary>An open document's text changed (or it was opened). Markup edits regenerate the control partial.</summary>
    public void SetText(string path, string text)
    {
        lock (_gate)
        {
            _overlays[path] = text;
            Refresh(path);
        }
    }

    /// <summary>The document was closed: fall back to what's on disk.</summary>
    public void Close(string path)
    {
        lock (_gate)
        {
            _overlays.Remove(path);
            if (File.Exists(path)) Refresh(path);
        }
    }

    private void Refresh(string path)
    {
        if (_documents.TryGetValue(path, out var id))
            ApplyOrThrow(Workspace.CurrentSolution.WithDocumentText(id, SourceText.From(Read(path))));
        if (!_pages.TryGetValue(path, out var page)) return;

        var text = Read(path);
        if (ShapeOf(path, text) != page.ShapeKey)
        {
            RemovePage(page);
            return; // recreated on next use with the new CodeFile/Inherits/registrations
        }
        var userControlTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in Markup.Registrations(text))
            if (FileText.Resolve(reg.Src.Value, path, Root) is { } m && _pages.TryGetValue(m, out var c))
                userControlTypes[$"{reg.TagPrefix}:{reg.TagName}"] = c.Inherits;
        var generated = Generate(path, text, page.Inherits, userControlTypes);
        page.Fields = generated.Fields;
        ApplyOrThrow(Workspace.CurrentSolution.WithDocumentText(page.VirtualDocument, SourceText.From(generated.Text)));
    }

    private void RemovePage(PageProject page)
    {
        foreach (var dependent in _pages.Values.Where(p => p.Controls.Contains(page.MarkupPath)).ToList()) RemovePage(dependent);
        if (!_pages.Remove(page.MarkupPath)) return;
        _documents.Remove(page.CodeBehindPath);
        _virtualDocuments.Remove(page.VirtualDocument);
        ApplyOrThrow(Workspace.CurrentSolution.RemoveProject(page.ProjectId));
    }

    private void ApplyOrThrow(Solution solution)
    {
        if (!Workspace.TryApplyChanges(solution)) throw new InvalidOperationException("Workspace rejected a change.");
    }

    /// <summary>For a location in generated code: the markup control a field declares (file, ID span), or the .wsdl a proxy came from.</summary>
    public (string File, int Start, int End)? MarkupOrigin(Location location)
    {
        lock (_gate)
        {
            if (location.SourceTree == null) return null;
            var docId = Workspace.CurrentSolution.GetDocumentId(location.SourceTree);
            if (docId != null && _webReferenceDocuments.TryGetValue(docId, out var wsdl)) return (wsdl, 0, 0);
            if (docId == null || !_virtualDocuments.TryGetValue(docId, out var page)) return null;
            var field = page.Fields.FirstOrDefault(f => location.SourceSpan.Start >= f.VirtualStart && location.SourceSpan.Start <= f.VirtualEnd);
            return field == null ? (page.MarkupPath, 0, 0) : (page.MarkupPath, field.MarkupIdStart, field.MarkupIdEnd);
        }
    }

    public bool IsGenerated(DocumentId id)
    {
        lock (_gate) return _virtualDocuments.ContainsKey(id) || _webReferenceDocuments.ContainsKey(id);
    }

    /// <summary>Paths of compiled .vb files: App_Code plus every code-behind created so far.</summary>
    public IReadOnlyList<string> CompiledFiles()
    {
        lock (_gate) return _documents.Keys.ToList();
    }

    /// <summary>The site root for a file: the nearest folder with a web.config that also has App_Code or Bin, else the nearest with a web.config.</summary>
    public static string? FindRoot(string file)
    {
        string? nearest = null;
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(file)); dir != null; dir = Path.GetDirectoryName(dir))
        {
            string[] entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).ToArray()!; }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            if (!entries.Any(e => e.Equals("web.config", StringComparison.OrdinalIgnoreCase))) continue;
            nearest ??= dir;
            if (entries.Any(e => e.Equals("App_Code", StringComparison.OrdinalIgnoreCase) || e.Equals("Bin", StringComparison.OrdinalIgnoreCase))) return dir;
        }
        return nearest;
    }
}
