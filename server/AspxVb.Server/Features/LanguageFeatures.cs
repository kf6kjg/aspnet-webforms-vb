using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using AspxVb.Server.Site;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;

namespace AspxVb.Server.Features;

public sealed record Span(string File, int StartLine, int StartChar, int EndLine, int EndChar);

public sealed record Problem(Span Range, bool IsError, string Code, string Message);

public sealed record CompletionEntry(string Label, string InsertText, string FilterText, string SortText, int Kind, int Index);

/// <summary>Completion, diagnostics, definition and hover on top of a SiteWorkspace. Positions are offsets into the document text.</summary>
public sealed class LanguageFeatures
{
    private readonly ConcurrentDictionary<string, string> _metadataViews = new(StringComparer.Ordinal);
    private (Document Document, ImmutableArray<CompletionItem> Items)? _lastCompletion;

    public async Task<IReadOnlyList<Problem>> DiagnosticsAsync(Document document, CancellationToken ct)
    {
        var model = await document.GetSemanticModelAsync(ct);
        if (model == null) return [];
        return model.GetDiagnostics(cancellationToken: ct)
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning && !d.IsSuppressed && d.Location.IsInSource)
            .Select(d => new Problem(ToSpan(d.Location.GetLineSpan()), d.Severity == DiagnosticSeverity.Error, d.Id, d.GetMessage()))
            .ToList();
    }

    public async Task<(IReadOnlyList<CompletionEntry> Items, bool Incomplete, TextSpan Span)> CompletionAsync(Document document, int position, CancellationToken ct)
    {
        var service = CompletionService.GetService(document);
        if (service == null) return ([], false, default);
        var list = await service.GetCompletionsAsync(document, position, cancellationToken: ct);
        var text = await document.GetTextAsync(ct);
        var typed = text.ToString(list.Span);
        _lastCompletion = (document, list.ItemsList.ToImmutableArray());

        // A statement start offers every type in scope (thousands). Once a character is typed,
        // send only items starting with it and let the client ask again as typing continues.
        var items = new List<CompletionEntry>();
        for (var i = 0; i < list.ItemsList.Count; i++)
        {
            var item = list.ItemsList[i];
            if (typed.Length > 0 && !item.FilterText.StartsWith(typed[0].ToString(), StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(new CompletionEntry(item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix, item.DisplayText,
                item.FilterText, item.SortText, Kind(item.Tags), i));
        }
        return (items, typed.Length > 0, list.Span);
    }

    /// <summary>Signature and documentation for an item from the most recent completion list.</summary>
    public async Task<string?> CompletionDetailAsync(int index, CancellationToken ct)
    {
        if (_lastCompletion is not { } last || index < 0 || index >= last.Items.Length) return null;
        var service = CompletionService.GetService(last.Document);
        var description = service == null ? null : await service.GetDescriptionAsync(last.Document, last.Items[index], ct);
        return description?.Text;
    }

    private static int Kind(ImmutableArray<string> tags)
    {
        foreach (var tag in tags)
        {
            switch (tag)
            {
                case WellKnownTags.Method: case WellKnownTags.ExtensionMethod: return 2;
                case WellKnownTags.Property: return 10;
                case WellKnownTags.Field: return 5;
                case WellKnownTags.Event: return 23;
                case WellKnownTags.Class: case WellKnownTags.Delegate: return 7;
                case WellKnownTags.Structure: return 22;
                case WellKnownTags.Interface: return 8;
                case WellKnownTags.Enum: return 13;
                case WellKnownTags.EnumMember: return 20;
                case WellKnownTags.Constant: return 21;
                case WellKnownTags.Local: case WellKnownTags.Parameter: case WellKnownTags.RangeVariable: return 6;
                case WellKnownTags.Namespace: case WellKnownTags.Module: return 9;
                case WellKnownTags.Keyword: return 14;
                case WellKnownTags.Snippet: return 15;
            }
        }
        return 1;
    }

    /// <summary>
    /// Where the symbol at the position is declared: source files; the markup control for a
    /// generated control field; a generated read-only view for framework and Bin symbols.
    /// </summary>
    public async Task<IReadOnlyList<Span>> DefinitionAsync(SiteWorkspace site, Document document, int position, CancellationToken ct)
    {
        var model = await document.GetSemanticModelAsync(ct);
        if (model == null) return [];
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(model, position, document.Project.Solution.Workspace, ct);
        if (symbol == null) return [];
        if (symbol is IMethodSymbol { ReducedFrom: { } reduced }) symbol = reduced;
        symbol = symbol.OriginalDefinition;

        var result = new List<Span>();
        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                if (site.MarkupOrigin(location) is { } origin)
                {
                    var markup = FileText.ReadSource(origin.File);
                    result.Add(ToSpan(origin.File, markup, origin.Start, origin.End));
                }
                else if (location.SourceTree?.FilePath is { Length: > 0 })
                {
                    result.Add(ToSpan(location.GetLineSpan()));
                }
            }
            else if (location.IsInMetadata && MetadataView.For(symbol) is { } view)
            {
                _metadataViews[Uri.UnescapeDataString(view.Uri)] = view.Text;
                result.Add(new Span(view.Uri, view.Line, 0, view.Line, 0));
            }
        }
        return result;
    }

    /// <summary>Looked up unescaped: VS Code and .NET percent-encode URIs differently.</summary>
    public string? MetadataViewText(string uri) => _metadataViews.GetValueOrDefault(Uri.UnescapeDataString(uri));

    public async Task<string?> HoverAsync(Document document, int position, CancellationToken ct)
    {
        var service = QuickInfoService.GetService(document);
        var info = service == null ? null : await service.GetQuickInfoAsync(document, position, cancellationToken: ct);
        if (info == null || info.Sections.IsEmpty) return null;
        var sb = new StringBuilder();
        foreach (var section in info.Sections)
        {
            if (section.Kind == QuickInfoSectionKinds.Description) sb.Append("```vb\n").Append(section.Text).Append("\n```\n");
            else if (!string.IsNullOrWhiteSpace(section.Text)) sb.Append('\n').Append(section.Text).Append('\n');
        }
        return sb.ToString();
    }

    private static Span ToSpan(FileLinePositionSpan s) =>
        new(s.Path, s.StartLinePosition.Line, s.StartLinePosition.Character, s.EndLinePosition.Line, s.EndLinePosition.Character);

    private static Span ToSpan(string file, SourceText text, int start, int end)
    {
        var s = text.Lines.GetLinePosition(start);
        var e = text.Lines.GetLinePosition(end);
        return new Span(file, s.Line, s.Character, e.Line, e.Character);
    }
}
