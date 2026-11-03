using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace AspxVb.Server.Features;

/// <summary>
/// Visual Studio's automatic end constructs: after Enter on a line that opens a block (If …
/// Then, Sub, For, Try, …), insert the end below the new line at the opener's indentation.
/// The end is kept only if re-parsing shows it closes the new block and leaves one block
/// fewer unclosed. A new inner If takes the enclosing If's End If in the parse, so the block
/// that's really missing its end may be an outer one. Property is left out: on its own line
/// it can be a complete auto-property.
/// Enter can also split a single-line form (`If c Then Foo()` → `If c Then` + `Foo()`). The
/// rest of the line becomes the block's body, indented one space, and the end goes below it.
/// </summary>
public static class EndConstructs
{
    /// <summary>The edit to make, and where the cursor goes in the edited text.</summary>
    public readonly record struct Insertion(TextChange Change, int Cursor);

    /// <param name="previousLine">The line Enter was pressed on (now finished).</param>
    /// <param name="openerIndent">That line's indentation after formatting.</param>
    public static Insertion? AfterEnter(SyntaxNode root, SourceText text, int previousLine, string openerIndent)
    {
        var cursorLine = previousLine + 1;
        if (cursorLine >= text.Lines.Count) return null;
        var line = text.Lines[previousLine];
        if (line.Span.IsEmpty) return null;

        // The statement that ends on the finished line.
        var token = root.FindToken(line.End - 1);
        while (token.SpanStart > line.End && token != default) token = token.GetPreviousToken();
        if (token.Span.End < line.Start) return null;
        var statement = token.Parent?.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement == null || statement.SpanStart < line.Start || statement.Span.End > line.End) return null;

        var end = EndFor(statement);
        if (end == null) return null;
        var newLine = text.ToString().Contains("\r\n") ? "\r\n" : "\n";
        var block = string.Join(newLine, end.Select(l => openerIndent + l)) + newLine;
        var cursor = text.Lines[cursorLine];
        var rest = text.ToString(cursor.Span).TrimStart(' ', '\t');
        var body = rest.Length == 0 ? text.ToString(cursor.Span) : openerIndent + " " + rest;
        var change = new TextChange(TextSpan.FromBounds(cursor.Start, cursor.EndIncludingLineBreak),
            body + newLine + (cursor.EndIncludingLineBreak == cursor.End ? block.TrimEnd('\r', '\n') : block));

        var after = root.SyntaxTree.WithChangedText(text.WithChanges(change)).GetRoot();
        if (MissingEnds(after) >= MissingEnds(root)) return null;
        var opener = after.FindNode(statement.Span).AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault(s => s.RawKind == statement.RawKind && s.SpanStart == statement.SpanStart);
        var endsAt = cursor.Start + body.Length;
        if (opener?.Parent is not { } closed || closed.Span.End <= endsAt || closed.Span.End > cursor.Start + change.NewText!.Length) return null;
        return new Insertion(change, rest.Length == 0 ? endsAt : endsAt - rest.Length);
    }

    /// <summary>Block ends the parser had to make up (End If, End Sub, Next, Loop, …).</summary>
    private static int MissingEnds(SyntaxNode root) =>
        root.DescendantNodes().Count(n => n.IsMissing && n is EndBlockStatementSyntax or NextStatementSyntax or LoopStatementSyntax);

    /// <summary>The lines that end the block <paramref name="opener"/> starts, or null when it doesn't start one.</summary>
    private static string[]? EndFor(StatementSyntax opener) => opener.Parent switch
    {
        MultiLineIfBlockSyntax b when b.IfStatement == opener => ["End If"],
        MethodBlockSyntax b when b.BlockStatement == opener => [b.IsKind(SyntaxKind.FunctionBlock) ? "End Function" : "End Sub"],
        ConstructorBlockSyntax b when b.BlockStatement == opener => ["End Sub"],
        ForBlockSyntax b when b.ForStatement == opener => ["Next"],
        ForEachBlockSyntax b when b.ForEachStatement == opener => ["Next"],
        WhileBlockSyntax b when b.WhileStatement == opener => ["End While"],
        DoLoopBlockSyntax b when b.DoStatement == opener => ["Loop"],
        SelectBlockSyntax b when b.SelectStatement == opener => ["End Select"],
        TryBlockSyntax b when b.TryStatement == opener => ["Catch ex As Exception", "End Try"],
        WithBlockSyntax b when b.WithStatement == opener => ["End With"],
        UsingBlockSyntax b when b.UsingStatement == opener => ["End Using"],
        SyncLockBlockSyntax b when b.SyncLockStatement == opener => ["End SyncLock"],
        TypeBlockSyntax b when b.BlockStatement == opener => [$"End {SyntaxFacts.GetText(b.BlockStatement.DeclarationKeyword.Kind())}"],
        EnumBlockSyntax b when b.EnumStatement == opener => ["End Enum"],
        NamespaceBlockSyntax b when b.NamespaceStatement == opener => ["End Namespace"],
        _ => null,
    };
}
