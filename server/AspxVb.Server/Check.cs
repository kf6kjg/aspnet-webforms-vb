using System.Diagnostics;
using AspxVb.Server.Features;
using AspxVb.Server.Site;

namespace AspxVb.Server;

/// <summary>
/// `--check &lt;folder&gt;`: compile every page and App_Code file of every site under a folder
/// (read-only) and summarize the diagnostics, to spot false errors from missing references
/// or imports. Folders in $SCAN_EXCLUDE (root-relative, ';'-separated) are skipped.
/// $CHECK_DETAIL (e.g. "BC30451;BC30002") lists every instance of those IDs, not just four.
/// $ASPXVB_ASSEMBLY_FOLDERS (';'-separated) plays the role of the aspxVb.assemblyFolders setting.
/// </summary>
public static class Check
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase) { "obj", "PrecompiledWeb", ".vs", ".git", "node_modules", "aspnet_client" };

    public static async Task<int> RunAsync(string folder)
    {
        folder = Path.GetFullPath(folder);
        var excluded = (Environment.GetEnvironmentVariable("SCAN_EXCLUDE") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => Path.GetFullPath(Path.Combine(folder, e)) + Path.DirectorySeparatorChar).ToList();
        var registry = new SiteRegistry((Environment.GetEnvironmentVariable("ASPXVB_ASSEMBLY_FOLDERS") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var features = new LanguageFeatures();
        var clock = Stopwatch.StartNew();

        var markups = Walk(folder).Where(f => !excluded.Any(e => f.StartsWith(e, StringComparison.Ordinal))).ToList();
        foreach (var markup in markups) registry.SiteFor(markup)?.EnsureMarkup(markup);
        Console.WriteLine($"{markups.Count} markup files, {registry.All().Count} sites, projects built in {clock.Elapsed.TotalSeconds:F1}s");

        var detail = (Environment.GetEnvironmentVariable("CHECK_DETAIL") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        var byId = new Dictionary<string, (int Count, List<string> Examples)>();
        foreach (var site in registry.All().OrderBy(s => s.Root))
        {
            int errors = 0, warnings = 0, files = 0;
            foreach (var file in site.CompiledFiles())
            {
                if (site.GetDocument(file) is not { } doc) continue;
                files++;
                foreach (var p in await features.DiagnosticsAsync(doc, CancellationToken.None))
                {
                    if (p.IsError) errors++; else warnings++;
                    var key = $"{(p.IsError ? "error" : "warning")} {p.Code}";
                    var entry = byId.GetValueOrDefault(key, (0, new List<string>()));
                    if (entry.Item2.Count < 4 || detail.Contains(p.Code)) entry.Item2.Add($"{Path.GetRelativePath(folder, p.Range.File)}({p.Range.StartLine + 1}): {p.Message}");
                    byId[key] = (entry.Item1 + 1, entry.Item2);
                }
            }
            var unresolved = site.Settings.UnresolvedAssemblies.Count > 0 ? $"; unresolved assemblies: {string.Join(", ", site.Settings.UnresolvedAssemblies)}" : "";
            Console.WriteLine($"  {Path.GetRelativePath(folder, site.Root)}: {files} files, {errors} errors, {warnings} warnings{unresolved}");
        }

        Console.WriteLine($"\nBy diagnostic ({clock.Elapsed.TotalSeconds:F1}s total):");
        foreach (var (key, (count, examples)) in byId.OrderByDescending(kv => kv.Key.StartsWith("error")).ThenByDescending(kv => kv.Value.Count))
        {
            Console.WriteLine($"{count,6}  {key}");
            foreach (var e in examples) Console.WriteLine($"          {e}");
        }
        return 0;
    }

    private static IEnumerable<string> Walk(string dir)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var e in entries)
        {
            if (Directory.Exists(e)) { if (!SkipDirs.Contains(Path.GetFileName(e))) foreach (var f in Walk(e)) yield return f; }
            else if (Path.GetExtension(e).ToLowerInvariant() is ".aspx" or ".ascx" or ".master") yield return e;
        }
    }
}
