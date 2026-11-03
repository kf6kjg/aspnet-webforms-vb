using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace AspxVb.Server.Features;

/// <summary>
/// Visual Studio's VB "pretty listing", approximated with Roslyn's public APIs: case
/// correction (keywords and identifiers take their declared casing) plus the formatter
/// (spacing, indentation; one-space indent from the site's in-memory .editorconfig).
/// Comments never move: the whitespace before a trailing comment and the indentation of a
/// whole-line comment are kept, because the developer places comments at fixed columns.
/// </summary>
public static class Formatting
{
    /// <summary>The settings the developer uses in Visual Studio, as .editorconfig (ADR 0008).</summary>
    /// <remarks>CRLF: his files come from Windows; without it the formatter writes the platform's LF after line continuations.</remarks>
    public const string EditorConfig = "root = true\n\n[*.vb]\nindent_style = space\nindent_size = 1\ntab_width = 1\nend_of_line = crlf\n";

    public static async Task<IReadOnlyList<TextChange>> FormatAsync(Document document, TextSpan span, CancellationToken ct)
    {
        var original = await document.GetTextAsync(ct);
        var root = await document.GetSyntaxRootAsync(ct);
        var cased = await CaseCorrectAsync(document, span, ct);
        var formatted = await (await Formatter.FormatAsync(cased, span, cancellationToken: ct)).GetTextAsync(ct);
        if (root == null || formatted.Lines.Count != original.Lines.Count)
            return (await (await Formatter.FormatAsync(cased, span, cancellationToken: ct)).GetTextChangesAsync(document, ct)).ToList();

        // One edit per changed line; comments stay exactly where they were.
        var comments = CommentStarts(root, original, span);
        var changes = new List<TextChange>();
        var first = original.Lines.GetLineFromPosition(span.Start).LineNumber;
        var last = original.Lines.GetLineFromPosition(Math.Max(span.Start, span.End - 1)).LineNumber;
        for (var i = first; i <= last; i++)
        {
            var line = original.Lines[i];
            var before = original.ToString(line.Span);
            var after = formatted.ToString(formatted.Lines[i].Span);
            if (comments.TryGetValue(i, out var column))
            {
                var comment = before[column..].TrimEnd();
                var code = before[..column];
                if (code.Trim().Length == 0) continue; // whole-line comment: leave the line alone
                var gap = code[code.TrimEnd().Length..];
                after = after.EndsWith(comment) ? after[..^comment.Length].TrimEnd() + gap + comment : before;
            }
            if (after != before) changes.Add(new TextChange(line.Span, after));
        }
        return changes;
    }

    /// <summary>Column of the comment on each line (by line number) within the span.</summary>
    private static Dictionary<int, int> CommentStarts(SyntaxNode root, SourceText text, TextSpan span)
    {
        var result = new Dictionary<int, int>();
        foreach (var trivia in root.DescendantTrivia(span).Where(t => t.IsKind(SyntaxKind.CommentTrivia)))
        {
            var line = text.Lines.GetLineFromPosition(trivia.SpanStart);
            result.TryAdd(line.LineNumber, trivia.SpanStart - line.Start);
        }
        return result;
    }

    /// <summary>
    /// Keywords take their canonical spelling (end if → End If); identifiers that bind to a
    /// symbol take its declared casing (response.write → Response.Write, txtname → txtName).
    /// </summary>
    private static async Task<Document> CaseCorrectAsync(Document document, TextSpan span, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        var model = await document.GetSemanticModelAsync(ct);
        if (root == null || model == null) return document;
        var changes = new List<TextChange>();
        foreach (var token in root.DescendantTokens(span))
        {
            if (!token.Span.IntersectsWith(span)) continue;
            string? wanted = null;
            if (SyntaxFacts.IsKeywordKind(token.Kind()))
                wanted = SyntaxFacts.GetText(token.Kind());
            else if (token.IsKind(SyntaxKind.IdentifierToken) && token.Parent is IdentifierNameSyntax name && !token.Text.StartsWith('['))
                wanted = model.GetSymbolInfo(name, ct).Symbol?.Name;
            if (!string.IsNullOrEmpty(wanted) && token.Text != wanted && string.Equals(token.Text, wanted, StringComparison.OrdinalIgnoreCase))
                changes.Add(new TextChange(token.Span, wanted));
        }
        return changes.Count == 0 ? document : document.WithText((await document.GetTextAsync(ct)).WithChanges(changes));
    }

}
