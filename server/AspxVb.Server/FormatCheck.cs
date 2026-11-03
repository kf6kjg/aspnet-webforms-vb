using AspxVb.Server.Features;
using AspxVb.Server.Site;
using Microsoft.CodeAnalysis.Text;

namespace AspxVb.Server;

/// <summary>
/// `--format-check &lt;folder&gt;`: run the formatter over every compiled .vb file of every site
/// (read-only) and report what it would change. The bar for matching the developer's style:
/// next to nothing on code he wrote with Visual Studio's pretty listing. Folders in
/// $SCAN_EXCLUDE (root-relative, ';'-separated) are skipped.
/// </summary>
public static class FormatCheck
{
    public static async Task<int> RunAsync(string folder)
    {
        folder = Path.GetFullPath(folder);
        var excluded = (Environment.GetEnvironmentVariable("SCAN_EXCLUDE") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => Path.GetFullPath(Path.Combine(folder, e)) + "/").ToList();
        var registry = new SiteRegistry([]);
        foreach (var markup in Directory.EnumerateFiles(folder, "*.as?x", SearchOption.AllDirectories))
            if (!markup.Contains("/obj/") && !excluded.Any(e => markup.StartsWith(e, StringComparison.Ordinal))
                && Path.GetExtension(markup).ToLowerInvariant() is ".aspx" or ".ascx")
                registry.SiteFor(markup)?.EnsureMarkup(markup);

        int files = 0, changedFiles = 0, totalLines = 0;
        var kinds = new Dictionary<string, (int Count, List<string> Samples)>();
        var perFile = new Dictionary<(string File, string Kind), int>();
        foreach (var site in registry.All().OrderBy(s => s.Root))
        {
            foreach (var file in site.CompiledFiles())
            {
                if (site.GetDocument(file) is not { } doc) continue;
                var text = await doc.GetTextAsync();
                files++;
                totalLines += text.Lines.Count;
                var changes = await Formatting.FormatAsync(doc, new TextSpan(0, text.Length), default);
                if (changes.Count == 0) continue;
                changedFiles++;
                var after = text.WithChanges(changes);
                foreach (var n in changes.Select(c => text.Lines.GetLineFromPosition(c.Span.Start).LineNumber).Distinct())
                {
                    var b = text.ToString(text.Lines[n].SpanIncludingLineBreak);
                    var a = AfterLine(text, after, changes, n);
                    var kind = Classify(b, a);
                    perFile[(Path.GetRelativePath(folder, file), kind)] = perFile.GetValueOrDefault((Path.GetRelativePath(folder, file), kind)) + 1;
                    var entry = kinds.GetValueOrDefault(kind, (0, new List<string>()));
                    if (entry.Item2.Count < 8) entry.Item2.Add($"{Path.GetRelativePath(folder, file)}({n + 1})\n      - {Show(b)}\n      + {Show(a)}");
                    kinds[kind] = (entry.Item1 + 1, entry.Item2);
                }
            }
        }
        Console.WriteLine($"{files} files, {totalLines} lines; formatting whole files would touch {kinds.Values.Sum(k => k.Count)} lines in {changedFiles} files");
        foreach (var (kind, (count, samples)) in kinds.OrderByDescending(k => k.Value.Count))
        {
            var byFile = perFile.Where(f => f.Key.Kind == kind).OrderByDescending(f => f.Value).ToList();
            Console.WriteLine($"\n{count,7}  {kind}  ({byFile.Count} files; most: {string.Join(", ", byFile.Take(5).Select(f => $"{Path.GetFileName(f.Key.File)} {f.Value}"))})");
            foreach (var s in samples) Console.WriteLine($"    {s}");
        }
        return 0;
    }

    private static string Classify(string before, string after)
    {
        static string Lead(string s) => s[..(s.Length - s.TrimStart(' ', '\t').Length)];
        var b = before.TrimEnd('\r', '\n');
        var a = after.TrimEnd('\r', '\n');
        if (b == a) return "line ending";
        if (b.TrimEnd() == a.TrimEnd() && Lead(b) == Lead(a)) return "trailing whitespace";
        if (Lead(b) != Lead(a))
        {
            if (b.Trim() != a.Trim()) return "indentation + other";
            if (b.TrimStart().StartsWith('\'')) return "indentation only: whole-line comment";
            if (b.TrimEnd().EndsWith(" _") || b.TrimEnd() == "_") return "indentation only: line ending in _";
            return "indentation only: code";
        }
        if (string.Equals(b, a, StringComparison.OrdinalIgnoreCase)) return "casing";
        if (b.Replace(" ", "") == a.Replace(" ", "")) return "spacing within line";
        if (string.Equals(b.Replace(" ", ""), a.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)) return "spacing + casing";
        return "other";
    }

    /// <summary>Line n of the original text, with its line break, after applying the changes that touch it.</summary>
    private static string AfterLine(SourceText before, SourceText after, IReadOnlyList<TextChange> changes, int n)
    {
        var line = before.Lines[n];
        var delta = changes.Where(c => c.Span.End <= line.Start).Sum(c => c.NewText!.Length - c.Span.Length);
        var l = after.Lines.GetLineFromPosition(Math.Max(0, line.Start + delta));
        return after.ToString(l.SpanIncludingLineBreak);
    }

    private static string Show(string s) => s.Replace("\t", "→").Replace(" ", "·").Replace("\r", "␍").Replace("\n", "␊");
}
