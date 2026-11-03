// Read-only inventory of the developer's Web Site projects: which syntax and
// conventions the real code bases use, so the extension supports that minimum.
//
//   dotnet run scripts/scan-sites.cs -- [root] [outDir]
//
// root defaults to $SITES_ROOT, outDir to .scan/. Writes report.json (per-site detail)
// and report.md (aggregates). Never writes under root. Never reads connectionStrings or
// appSettings values out of web.config.
//
// Optional environment:
//   SITES_DRIVE_MAP  "W:=/mnt/share;V:=/mnt/other" — the .sln files name sites by absolute
//                    Windows path on a mapped drive (W:\WebApps\...).
//   SCAN_EXCLUDE     "a/b;c" — root-relative folders to skip (third-party samples etc.).

#:package Microsoft.CodeAnalysis.VisualBasic@5.9.0
#:property PublishAot=false

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

const string WebSiteProjectType = "E24C65DC-7377-472B-9ABA-BC803B73C61A";
var skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "obj", "PrecompiledWeb", ".vs", ".git", "node_modules", "aspnet_client", "_UpgradeReport_Files", "packages" };
var markupExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".aspx", ".ascx", ".master" };
var directiveOnlyExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".asmx", ".ashx", ".asax", ".svc" };

var root = Path.GetFullPath(args.Length > 0 ? args[0]
    : Environment.GetEnvironmentVariable("SITES_ROOT") ?? throw new ArgumentException("Pass a root folder or set SITES_ROOT."));
var outDir = Path.GetFullPath(args.Length > 1 ? args[1] : ".scan");
if (outDir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("outDir must not be under root.");
var driveMap = (Environment.GetEnvironmentVariable("SITES_DRIVE_MAP") ?? "")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(e => e.Split('=', 2)).ToDictionary(p => p[0].TrimEnd(':').ToUpperInvariant(), p => p[1]);
var excluded = (Environment.GetEnvironmentVariable("SCAN_EXCLUDE") ?? "")
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(e => Path.GetFullPath(Path.Combine(root, e))).ToList();
Console.Error.WriteLine($"Scanning {root}");

var enumAll = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };
var enumTop = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };

// ---- 1. Solutions and the Web Site projects they point at. ----
var solutions = new List<Dictionary<string, object?>>();
var siteSources = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase); // site dir -> sln paths
var siteSlnProps = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
var projectLine = new Regex("""^Project\("\{(?<type>[^}]+)\}"\)\s*=\s*"(?<name>[^"]*)",\s*"(?<path>[^"]*)",\s*"\{(?<id>[^}]+)\}"\s*$""", RegexOptions.Multiline);
foreach (var sln in Directory.EnumerateFiles(root, "*.sln", enumAll).Where(p => !InSkippedDir(p) && !IsExcluded(p)))
{
    var text = ReadText(sln);
    var projects = new List<Dictionary<string, object?>>();
    foreach (Match m in projectLine.Matches(text))
    {
        var type = m.Groups["type"].Value.ToUpperInvariant();
        var path = m.Groups["path"].Value;
        var entry = new Dictionary<string, object?> { ["type"] = TypeName(type), ["name"] = m.Groups["name"].Value, ["path"] = path };
        if (type == WebSiteProjectType)
        {
            var endIdx = text.IndexOf("EndProject", m.Index, StringComparison.Ordinal);
            var block = text[m.Index..(endIdx < 0 ? text.Length : endIdx)];
            var props = Regex.Matches(block, """^\s*(?<k>[\w.]+)\s*=\s*"(?<v>[^"]*)"\s*$""", RegexOptions.Multiline)
                .GroupBy(p => p.Groups["k"].Value).ToDictionary(g => g.Key, g => g.First().Groups["v"].Value);
            entry["props"] = props.Where(p => !p.Key.Contains("AspNetCompiler") || p.Key.EndsWith("PhysicalPath"))
                .ToDictionary(p => p.Key, p => p.Value);
            var drive = Regex.Match(path, @"^(?<d>[A-Za-z]):\\(?<rest>.*)$");
            string? dir = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? null
                : drive.Success ? (driveMap.TryGetValue(drive.Groups["d"].Value.ToUpperInvariant(), out var mount)
                    ? Path.Combine(mount, drive.Groups["rest"].Value.Replace('\\', '/')) : null)
                : Path.Combine(Path.GetDirectoryName(sln)!, path.Replace('\\', '/'));
            dir = dir == null ? null : OnDiskCase(Path.GetFullPath(dir).TrimEnd('/'));
            entry["resolved"] = dir;
            if (dir != null)
            {
                if (Directory.Exists(dir) && !IsExcluded(dir))
                {
                    (siteSources.TryGetValue(dir, out var l) ? l : siteSources[dir] = []).Add(Rel(sln));
                    siteSlnProps.TryAdd(dir, props);
                }
            }
        }
        projects.Add(entry);
    }
    solutions.Add(new() { ["sln"] = Rel(sln), ["projects"] = projects });
}

// ---- 2. Folders that look like sites but no .sln points at. ----
var orphanCandidates = Directory.EnumerateFiles(root, "web.config", enumAll)
    .Where(p => !InSkippedDir(p) && !IsExcluded(p))
    .Select(p => Path.GetDirectoryName(p)!)
    .Where(d => Directory.EnumerateFiles(d, "*.as?x", enumTop).Any())
    .Where(d => !siteSources.Keys.Any(s => d.Equals(s, StringComparison.OrdinalIgnoreCase) || IsUnder(d, s)))
    .ToList();
// Keep only the outermost orphan in a nested chain.
orphanCandidates = orphanCandidates.Where(d => !orphanCandidates.Any(o => o != d && IsUnder(d, o))).ToList();
var siteDirs = siteSources.Keys.Concat(orphanCandidates).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d).ToList();

// ---- 3. Per-site detail. ----
var sites = new List<Site>();
foreach (var dir in siteDirs)
{
    Console.Error.WriteLine($"  {Rel(dir)}");
    sites.Add(ScanSite(dir));
}

// ---- 4. Output. ----
Directory.CreateDirectory(outDir);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
File.WriteAllText(Path.Combine(outDir, "report.json"),
    JsonSerializer.Serialize(new { root, scannedAt = DateTime.Now, solutions, sites }, jsonOptions));
File.WriteAllText(Path.Combine(outDir, "report.md"), RenderMarkdown());
Console.Error.WriteLine($"Wrote {Path.Combine(outDir, "report.md")} and report.json");
return;

// =====================================================================

Site ScanSite(string dir)
{
    var s = new Site { Dir = Rel(dir), Solutions = siteSources.GetValueOrDefault(dir) ?? [], LikelyBackup = LooksLikeBackup(Rel(dir)) };
    if (siteSlnProps.TryGetValue(dir, out var props))
    {
        s.SlnTargetFramework = props.GetValueOrDefault("TargetFrameworkMoniker")?.Replace("%3D", "=");
        s.SlnProjectReferences = props.GetValueOrDefault("ProjectReferences");
    }
    var nestedSites = siteDirs.Where(o => o != dir && IsUnder(o, dir)).ToList();
    var files = new List<string>();
    CollectFiles(dir, files, nestedSites);

    foreach (var f in files) Bump(s.ExtCounts, Path.GetExtension(f).ToLowerInvariant() is { Length: > 0 } e ? e : "(none)");
    s.DesignerVbFiles = files.Count(f => f.EndsWith(".designer.vb", StringComparison.OrdinalIgnoreCase));
    s.ProjectFiles = files.Where(f => f.EndsWith("proj", StringComparison.OrdinalIgnoreCase)).Select(f => Rel(f)).ToList();

    // web.config at the site root
    var webConfig = Directory.EnumerateFiles(dir, "web.config", enumTop).FirstOrDefault();
    if (webConfig != null) s.WebConfig = ReadWebConfig(webConfig);
    s.NestedWebConfigs = files.Count(f => Path.GetFileName(f).Equals("web.config", StringComparison.OrdinalIgnoreCase)) - (webConfig != null ? 1 : 0);

    // Bin
    var bin = Directory.EnumerateDirectories(dir, "bin", enumTop).FirstOrDefault();
    if (bin != null)
    {
        foreach (var dll in Directory.EnumerateFiles(bin, "*.dll", enumTop)) s.Bin.Add(ReadAssembly(dll));
        s.BinRefreshFiles = Directory.EnumerateFiles(bin, "*.refresh", enumTop).Count();
    }

    // App_Code
    var appCode = Directory.EnumerateDirectories(dir, "App_Code", enumTop).FirstOrDefault();
    if (appCode != null)
        s.AppCodeSubdirs = Directory.EnumerateDirectories(appCode, "*", enumTop).Select(Path.GetFileName).ToList()!;

    // Markup
    var codeFileTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var inheritsSeen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var f in files.Where(f => markupExts.Contains(Path.GetExtension(f)) || directiveOnlyExts.Contains(Path.GetExtension(f))))
    {
        var text = ReadText(f);
        var isMarkup = markupExts.Contains(Path.GetExtension(f));
        foreach (Match d in Regex.Matches(text, @"<%@\s*(?<name>\w+)?(?<attrs>.*?)%>", RegexOptions.Singleline))
        {
            var name = d.Groups["name"].Success ? d.Groups["name"].Value : "(default)";
            // "<%@ Language=VB %>" style: no directive name, first token is an attribute.
            var attrsText = d.Groups["attrs"].Value;
            if (attrsText.TrimStart().StartsWith('=')) { attrsText = name + attrsText; name = "(default)"; }
            var dirKey = Canon(name);
            Bump(s.Directives, dirKey);
            var attrs = Attrs(attrsText);
            foreach (var a in attrs.Keys) Bump(s.DirectiveAttrs, $"{dirKey}.{a}");
            if (attrs.TryGetValue("Language", out var lang)) Bump(s.Languages, lang);
            if (attrs.TryGetValue("AutoEventWireup", out var aew)) Bump(s.AutoEventWireup, $"{dirKey} {aew.ToLowerInvariant()}");
            if (dirKey is "Page" or "Control" or "Master" or "WebService" or "WebHandler" or "Application")
            {
                var link = attrs.ContainsKey("CodeFile") ? "CodeFile" : attrs.ContainsKey("CodeBehind") ? "CodeBehind"
                    : attrs.ContainsKey("Src") ? "Src" : attrs.ContainsKey("Inherits") ? "Inherits only" : "none (inline)";
                Bump(s.CodeLinkage, $"{dirKey}: {link}");
                if (attrs.TryGetValue("CodeFile", out var cf))
                    codeFileTargets.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, cf.Replace('\\', '/'))));
                if (attrs.TryGetValue("Inherits", out var inh)) inheritsSeen[inh] = inheritsSeen.GetValueOrDefault(inh) + 1;
            }
            if (dirKey == "Register")
            {
                Bump(s.RegisterForms, attrs.ContainsKey("Src") ? "Src+TagName" : attrs.ContainsKey("Namespace") ? (attrs.ContainsKey("Assembly") ? "Namespace+Assembly" : "Namespace only") : "other");
                if (attrs.TryGetValue("Src", out var src)) Bump(s.RegisterSrcStyles, src.StartsWith("~") ? "~/ rooted" : src.StartsWith("/") ? "/ rooted" : "relative");
            }
            if (dirKey == "Import" && attrs.TryGetValue("Namespace", out var ns)) Bump(s.Imports, ns);
        }
        if (!isMarkup) continue;

        // One ordered pass over tags so we know whether a control sits inside a template
        // (<ItemTemplate> etc.), where ASP.NET generates no page field for it.
        var templateDepth = 0;
        foreach (Match t in Regex.Matches(text, @"<(?<close>/)?(?:(?<prefix>[A-Za-z]\w*):)?(?<tag>[A-Za-z]\w*)(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*)>"))
        {
            var tag = t.Groups["tag"].Value;
            var hasPrefix = t.Groups["prefix"].Success;
            var selfClosing = t.Groups["attrs"].Value.TrimEnd().EndsWith('/');
            if (!hasPrefix && tag.EndsWith("Template", StringComparison.OrdinalIgnoreCase))
            {
                if (t.Groups["close"].Success) templateDepth = Math.Max(0, templateDepth - 1);
                else if (!selfClosing) { templateDepth++; Bump(s.Templates, tag); }
                continue;
            }
            if (t.Groups["close"].Success) continue;
            var attrs = Attrs(t.Groups["attrs"].Value);
            var server = attrs.TryGetValue("runat", out var r) && r.Equals("server", StringComparison.OrdinalIgnoreCase);
            if (hasPrefix)
            {
                var prefixTag = $"{t.Groups["prefix"].Value.ToLowerInvariant()}:{tag}";
                if (!server) { Bump(s.NonServerPrefixedTags, prefixTag); continue; }
                Bump(s.ServerTags, prefixTag);
                foreach (var a in attrs.Keys.Where(a => a.StartsWith("On") && a.Length > 2 && char.IsUpper(a[2]))) Bump(s.EventAttrs, a);
            }
            else
            {
                if (!server) continue;
                var lower = tag.ToLowerInvariant();
                if (lower == "script") { Bump(s.Blocks, "<script runat=server>"); continue; }
                Bump(s.HtmlServerTags, lower + (lower == "input" && attrs.TryGetValue("type", out var ty) ? $"[{ty.ToLowerInvariant()}]" : ""));
            }
            if (attrs.ContainsKey("ID")) { if (templateDepth > 0) s.ServerControlsInTemplates++; else s.ServerControlsWithId++; }
        }
        foreach (Match b in Regex.Matches(text, @"<%(?<kind>--|[=#:$@]|)"))
            Bump(s.Blocks, b.Groups["kind"].Value switch
            {
                "--" => "<%-- --%>", "=" => "<%= %>", "#" => "<%# %>", ":" => "<%: %>", "$" => "<%$ %>", "@" => "<%@ %>", _ => "<% %>",
            });
    }
    s.DuplicateInherits = inheritsSeen.Where(kv => kv.Value > 1).Select(kv => $"{kv.Key} x{kv.Value}").ToList();

    // VB code
    var vbFiles = files.Where(f => f.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)).ToList();
    var vb10 = new VisualBasicParseOptions(LanguageVersion.VisualBasic10);
    var latest = new VisualBasicParseOptions(LanguageVersion.Latest);
    foreach (var f in vbFiles)
    {
        var text = ReadText(f);
        var inAppCode = appCode != null && IsUnder(f, appCode);
        if (!inAppCode && !codeFileTargets.Contains(Path.GetFullPath(f))) s.LooseVbFiles.Add(Rel(f));
        var tree = VisualBasicSyntaxTree.ParseText(text, latest, f);
        if (tree.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } err)
            s.VbFilesWithParseErrors.Add($"{Rel(f)}({err.Location.GetLineSpan().StartLinePosition.Line + 1}): {err.Id} {err.GetMessage()}");
        foreach (var d in VisualBasicSyntaxTree.ParseText(text, vb10, f).GetDiagnostics().Where(d => d.Id == "BC36716"))
            Bump(s.VbFeaturesBeyondVb10, Regex.Replace(d.GetMessage(), @"^Visual Basic 10(\.0)? does not support ", "").TrimEnd('.'));
        foreach (Match m in Regex.Matches(text, @"^[ \t]*Option[ \t]+(Strict|Explicit|Infer|Compare)\b[ \t]*(\w*)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            Bump(s.OptionStatements, $"{Canon(m.Groups[1].Value)} {(m.Groups[2].Length > 0 ? Canon(m.Groups[2].Value) : "(bare = On)")}");
        foreach (Match m in Regex.Matches(text, @"^\s*Imports\s+([\w.=\s]+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            Bump(s.VbImports, m.Groups[1].Value);
        s.HandlesClauses += Regex.Matches(text, @"\)\s+Handles\s+", RegexOptions.IgnoreCase).Count;
        s.NamespaceDeclarations += Regex.Matches(text, @"^\s*Namespace\s+\w", RegexOptions.Multiline | RegexOptions.IgnoreCase).Count;
    }
    return s;
}

void CollectFiles(string dir, List<string> files, List<string> nestedSites)
{
    try
    {
        files.AddRange(Directory.EnumerateFiles(dir, "*", enumTop));
        foreach (var sub in Directory.EnumerateDirectories(dir, "*", enumTop))
            if (!skipDirs.Contains(Path.GetFileName(sub)) && !nestedSites.Contains(sub, StringComparer.OrdinalIgnoreCase))
                CollectFiles(sub, files, nestedSites);
    }
    catch (IOException) { }
}

Dictionary<string, object?> ReadWebConfig(string path)
{
    var result = new Dictionary<string, object?>();
    XDocument doc;
    try { doc = XDocument.Parse(ReadText(path)); }
    catch (Exception e) { result["parseError"] = e.Message; return result; }
    // Strip namespaces; some web.configs declare xmlns on <configuration>.
    foreach (var el in doc.Descendants()) el.Name = el.Name.LocalName;
    var sw = doc.Root?.Element("system.web");
    var comp = sw?.Element("compilation");
    if (comp != null)
    {
        result["compilation"] = comp.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
        result["assemblies"] = comp.Element("assemblies")?.Elements().Select(e => $"{e.Name.LocalName} {(string?)e.Attribute("assembly")}".Trim()).ToList();
        result["codeSubDirectories"] = comp.Element("codeSubDirectories")?.Elements().Select(e => (string?)e.Attribute("directoryName")).ToList();
        result["buildProviders"] = comp.Element("buildProviders")?.Elements().Select(e => (string?)e.Attribute("extension")).ToList();
    }
    var pages = sw?.Element("pages");
    if (pages != null)
    {
        result["pages"] = pages.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);
        result["namespaces"] = pages.Element("namespaces")?.Elements().Select(e => $"{e.Name.LocalName} {(string?)e.Attribute("namespace")}".Trim()).ToList();
        result["controls"] = pages.Element("controls")?.Elements()
            .Select(e => string.Join(" ", e.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"))).ToList();
    }
    result["httpRuntime.targetFramework"] = (string?)sw?.Element("httpRuntime")?.Attribute("targetFramework");
    result["codedom"] = doc.Root?.Element("system.codedom")?.Element("compilers")?.Elements()
        .Select(e => string.Join(" ", e.Attributes().Where(a => a.Name.LocalName is "language" or "extension" or "type" or "compilerOptions" or "warningLevel")
            .Select(a => $"{a.Name.LocalName}={a.Value}")
            .Concat(e.Elements("providerOption").Select(p => $"{(string?)p.Attribute("name")}={(string?)p.Attribute("value")}")))).ToList();
    result["locationElements"] = doc.Root?.Elements("location").Count();
    return result;
}

Dictionary<string, object?> ReadAssembly(string path)
{
    var info = new Dictionary<string, object?> { ["file"] = Path.GetFileName(path), ["bytes"] = new FileInfo(path).Length };
    try
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) { info["kind"] = "native"; return info; }
        var md = pe.GetMetadataReader();
        info["kind"] = pe.PEHeaders.CorHeader!.Flags.HasFlag(CorFlags.ILOnly) ? "managed" : "mixed-mode";
        info["runtime"] = md.MetadataVersion;
        info["targetFramework"] = TargetFramework(md);
        var refs = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToList();
        info["refsNetstandard"] = refs.Contains("netstandard");
        info["refsSystemRuntime"] = refs.Contains("System.Runtime");
    }
    catch (Exception e) { info["error"] = e.GetType().Name; }
    return info;
}

static string? TargetFramework(MetadataReader md)
{
    foreach (var h in md.GetAssemblyDefinition().GetCustomAttributes())
    {
        var attr = md.GetCustomAttribute(h);
        if (attr.Constructor.Kind != HandleKind.MemberReference) continue;
        var parent = md.GetMemberReference((MemberReferenceHandle)attr.Constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference || md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name) != "TargetFrameworkAttribute") continue;
        var blob = md.GetBlobReader(attr.Value);
        blob.ReadUInt16();
        return blob.ReadSerializedString();
    }
    return null;
}

static Dictionary<string, string> Attrs(string text)
{
    var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (Match m in Regex.Matches(text, @"(?<k>[\w][\w.:-]*)\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s""'>]+))"))
        d.TryAdd(m.Groups["k"].Value, m.Groups["v"].Value);
    return d;
}

string RenderMarkdown()
{
    var live = sites.Where(s => !s.LikelyBackup).ToList();
    var sb = new StringBuilder();
    sb.AppendLine($"# Site scan\n\nRoot: `{root}` · {DateTime.Now:yyyy-MM-dd HH:mm}\n");
    sb.AppendLine($"{solutions.Count} .sln files; {sites.Count} site folders ({siteSources.Count} referenced by a .sln, {orphanCandidates.Count} found by web.config + markup only); " +
                  $"{sites.Count - live.Count} look like backups and are left out of the aggregates below.\n");

    sb.AppendLine("## Sites\n\n| Site | .sln | sln TFM | web.config TFM | aspx/ascx/master | .vb | designer.vb | proj files | Bin dlls | server IDs (top/in templates) |\n|---|---|---|---|---|---|---|---|---|---|");
    foreach (var s in sites)
    {
        var comp = s.WebConfig?.GetValueOrDefault("compilation") as Dictionary<string, string>;
        sb.AppendLine($"| {(s.LikelyBackup ? "~~" + s.Dir + "~~" : s.Dir)} | {s.Solutions.Count} | {s.SlnTargetFramework?.Replace(".NETFramework,Version=", "")} | {comp?.GetValueOrDefault("targetFramework")} | " +
                      $"{s.ExtCounts.GetValueOrDefault(".aspx")}/{s.ExtCounts.GetValueOrDefault(".ascx")}/{s.ExtCounts.GetValueOrDefault(".master")} | {s.ExtCounts.GetValueOrDefault(".vb")} | {s.DesignerVbFiles} | {s.ProjectFiles.Count} | {s.Bin.Count} | {s.ServerControlsWithId}/{s.ServerControlsInTemplates} |");
    }

    void Section(string title, Func<Site, IEnumerable<string>> keys, int top = 40)
    {
        var agg = new Dictionary<string, (int Count, int Sites)>();
        foreach (var s in live)
            foreach (var g in keys(s).GroupBy(k => k))
                agg[g.Key] = (agg.GetValueOrDefault(g.Key).Count + g.Count(), agg.GetValueOrDefault(g.Key).Sites + 1);
        sb.AppendLine($"\n## {title}\n");
        if (agg.Count == 0) { sb.AppendLine("(none)"); return; }
        sb.AppendLine("| Item | Count | Sites |\n|---|---|---|");
        foreach (var kv in agg.OrderByDescending(kv => kv.Value.Count).Take(top)) sb.AppendLine($"| `{kv.Key.Replace("|", "\\|")}` | {kv.Value.Count} | {kv.Value.Sites} |");
        if (agg.Count > top) sb.AppendLine($"| … {agg.Count - top} more in report.json | | |");
    }
    IEnumerable<string> Expand(Dictionary<string, int> d) => d.SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value));
    IEnumerable<string> WcList(Site s, string key) => (s.WebConfig?.GetValueOrDefault(key) as IEnumerable<string?>)?.OfType<string>() ?? [];

    Section("File extensions", s => Expand(s.ExtCounts));
    Section("Code linkage in directives", s => Expand(s.CodeLinkage));
    Section("AutoEventWireup values", s => Expand(s.AutoEventWireup));
    Section("Directive languages", s => Expand(s.Languages));
    Section("Directives", s => Expand(s.Directives));
    Section("Directive attributes", s => Expand(s.DirectiveAttrs), 60);
    Section("@Register forms", s => Expand(s.RegisterForms));
    Section("@Register Src path styles", s => Expand(s.RegisterSrcStyles));
    Section("Server tags (prefix:Tag, runat=server)", s => Expand(s.ServerTags), 80);
    Section("HTML tags with runat=server", s => Expand(s.HtmlServerTags));
    Section("Event attributes on server tags", s => Expand(s.EventAttrs));
    Section("Template tags (controls inside get no page field)", s => Expand(s.Templates));
    Section("Prefixed tags without runat=server (sub-elements)", s => Expand(s.NonServerPrefixedTags));
    Section("Code blocks in markup", s => Expand(s.Blocks));
    Section("VB features beyond VB 10 (from Roslyn parse)", s => Expand(s.VbFeaturesBeyondVb10));
    Section("Option statements in .vb", s => Expand(s.OptionStatements));
    Section("Imports in .vb", s => Expand(s.VbImports));
    Section("Imports in markup (@Import)", s => Expand(s.Imports));
    Section("web.config compilation attributes", s => (s.WebConfig?.GetValueOrDefault("compilation") as Dictionary<string, string>)?.Select(kv => $"{kv.Key}={kv.Value}") ?? []);
    Section("web.config <assemblies>", s => WcList(s, "assemblies"));
    Section("web.config <namespaces>", s => WcList(s, "namespaces"));
    Section("web.config <pages><controls>", s => WcList(s, "controls"));
    Section("web.config <system.codedom> compilers", s => WcList(s, "codedom"));
    Section("web.config httpRuntime targetFramework", s => s.WebConfig?.GetValueOrDefault("httpRuntime.targetFramework") is string v ? [v] : []);
    Section("App_Code subfolders", s => s.AppCodeSubdirs);
    Section("Bin DLLs", s => s.Bin.Select(b => $"{b["file"]} ({b.GetValueOrDefault("kind")}, {b.GetValueOrDefault("targetFramework") ?? b.GetValueOrDefault("runtime")}{(b.GetValueOrDefault("refsNetstandard") is true ? ", netstandard" : "")})"), 120);

    sb.AppendLine("\n## Per-site oddities\n");
    foreach (var s in live)
    {
        var notes = new List<string>();
        if (s.Solutions.Count == 0) notes.Add("no .sln points here");
        if (!string.IsNullOrEmpty(s.SlnProjectReferences)) notes.Add($"sln ProjectReferences: `{s.SlnProjectReferences}`");
        if (s.ProjectFiles.Count > 0) notes.Add($"project files inside: {string.Join(", ", s.ProjectFiles)}");
        if (s.DesignerVbFiles > 0) notes.Add($"{s.DesignerVbFiles} .designer.vb");
        if (s.DuplicateInherits.Count > 0) notes.Add($"duplicate Inherits: {string.Join(", ", s.DuplicateInherits.Take(10))}");
        if (s.LooseVbFiles.Count > 0) notes.Add($"{s.LooseVbFiles.Count} .vb files outside App_Code not named by any CodeFile (e.g. {string.Join(", ", s.LooseVbFiles.Take(3))})");
        if (s.VbFilesWithParseErrors.Count > 0) notes.Add($"{s.VbFilesWithParseErrors.Count} .vb files with parse errors: {string.Join("; ", s.VbFilesWithParseErrors.Take(3))}");
        if (s.NestedWebConfigs > 0) notes.Add($"{s.NestedWebConfigs} nested web.config");
        if (s.NamespaceDeclarations > 0) notes.Add($"{s.NamespaceDeclarations} Namespace blocks in .vb");
        if (s.WebConfig?.GetValueOrDefault("parseError") is string pe) notes.Add($"web.config parse error: {pe}");
        if (s.BinRefreshFiles > 0) notes.Add($"{s.BinRefreshFiles} Bin/*.refresh");
        if (s.HandlesClauses > 0) notes.Add($"{s.HandlesClauses} Handles clauses");
        if (notes.Count > 0) sb.AppendLine($"- **{s.Dir}**: {string.Join("; ", notes)}");
    }
    return sb.ToString();
}

string Rel(string p) => Path.GetRelativePath(root, p);
bool IsExcluded(string p) => excluded.Any(e => p.Equals(e, StringComparison.OrdinalIgnoreCase) || IsUnder(p, e));
// .sln paths don't always match on-disk casing (W:\Webapps vs WebApps); the share is
// case-insensitive, but our path comparisons are not, so normalize to what's on disk.
static string OnDiskCase(string path)
{
    var current = "/";
    foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
    {
        var match = Directory.Exists(current) ? Directory.EnumerateFileSystemEntries(current)
            .FirstOrDefault(e => Path.GetFileName(e).Equals(part, StringComparison.OrdinalIgnoreCase)) : null;
        current = match ?? Path.Combine(current, part);
    }
    return current;
}
bool InSkippedDir(string p) => Path.GetRelativePath(root, p).Split('/').SkipLast(1).Any(skipDirs.Contains);
static bool IsUnder(string p, string dir) => p.StartsWith(dir.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
static bool LooksLikeBackup(string rel) => Regex.IsMatch(rel, @"(^|/)([^/]*Backup[^/]*|Old\w*|[^/]*[ _-](Copy|Original\w*|Old))(/|$)", RegexOptions.IgnoreCase);
static void Bump(Dictionary<string, int> d, string key) => d[key] = d.GetValueOrDefault(key) + 1;
static string Canon(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
static string TypeName(string guid) => guid switch
{
    WebSiteProjectType => "WebSite",
    "F184B08F-C81C-45F6-A57F-5ABD9991F28F" => "VB project",
    "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC" => "C# project",
    "2150E333-8FDC-42A3-9474-1A3956D46DE8" => "Solution folder",
    "349C5851-65DF-11DA-9384-00065B846F21" => "Web Application",
    _ => guid,
};
static string ReadText(string path)
{
    var bytes = File.ReadAllBytes(path);
    try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'); }
    catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
}

class Site
{
    public string Dir { get; set; } = "";
    public List<string> Solutions { get; set; } = [];
    public bool LikelyBackup { get; set; }
    public string? SlnTargetFramework { get; set; }
    public string? SlnProjectReferences { get; set; }
    public Dictionary<string, int> ExtCounts { get; } = [];
    public int DesignerVbFiles { get; set; }
    public List<string> ProjectFiles { get; set; } = [];
    public Dictionary<string, object?>? WebConfig { get; set; }
    public int NestedWebConfigs { get; set; }
    public List<Dictionary<string, object?>> Bin { get; } = [];
    public int BinRefreshFiles { get; set; }
    public List<string> AppCodeSubdirs { get; set; } = [];
    public Dictionary<string, int> Directives { get; } = [];
    public Dictionary<string, int> DirectiveAttrs { get; } = [];
    public Dictionary<string, int> Languages { get; } = [];
    public Dictionary<string, int> CodeLinkage { get; } = [];
    public Dictionary<string, int> AutoEventWireup { get; } = [];
    public Dictionary<string, int> RegisterForms { get; } = [];
    public Dictionary<string, int> RegisterSrcStyles { get; } = [];
    public Dictionary<string, int> Imports { get; } = [];
    public Dictionary<string, int> ServerTags { get; } = [];
    public Dictionary<string, int> HtmlServerTags { get; } = [];
    public int ServerControlsWithId { get; set; }
    public int ServerControlsInTemplates { get; set; }
    public Dictionary<string, int> NonServerPrefixedTags { get; } = [];
    public Dictionary<string, int> EventAttrs { get; } = [];
    public Dictionary<string, int> Templates { get; } = [];
    public Dictionary<string, int> Blocks { get; } = [];
    public List<string> DuplicateInherits { get; set; } = [];
    public List<string> LooseVbFiles { get; } = [];
    public List<string> VbFilesWithParseErrors { get; } = [];
    public Dictionary<string, int> VbFeaturesBeyondVb10 { get; } = [];
    public Dictionary<string, int> OptionStatements { get; } = [];
    public Dictionary<string, int> VbImports { get; } = [];
    public int HandlesClauses { get; set; }
    public int NamespaceDeclarations { get; set; }
}
