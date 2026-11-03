using AspxVb.Server.Features;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

namespace AspxVb.Server.Tests;

/// <summary>Automatic End If / End Sub / Next … after Enter, as in Visual Studio (ADR 0008).</summary>
public class EndConstructsTests
{
    /// <summary>Runs Enter-after-line `opener` (0-based) on the lines; returns the resulting lines, or null when nothing is inserted.</summary>
    private static string[]? Enter(int opener, params string[] lines)
    {
        var text = SourceText.From(string.Join("\r\n", lines));
        var root = VisualBasicSyntaxTree.ParseText(text).GetRoot();
        var line = text.ToString(text.Lines[opener].Span);
        var change = EndConstructs.AfterEnter(root, text, opener, line[..(line.Length - line.TrimStart().Length)]);
        return change == null ? null : text.WithChanges(change.Value.Change).ToString().Split("\r\n");
    }

    [Fact]
    public void If_then_gets_End_If_below_the_new_line()
    {
        var result = Enter(2, "Class C", " Sub M()", "  If x Then", "   ", " End Sub", "End Class");
        Assert.Equal(["Class C", " Sub M()", "  If x Then", "   ", "  End If", " End Sub", "End Class"], result!);
    }

    [Theory]
    [InlineData("  For i = 1 To 10", "  Next")]
    [InlineData("  For Each s In list", "  Next")]
    [InlineData("  While x", "  End While")]
    [InlineData("  Do", "  Loop")]
    [InlineData("  Select Case x", "  End Select")]
    [InlineData("  With obj", "  End With")]
    [InlineData("  Using r = Open()", "  End Using")]
    [InlineData("  SyncLock obj", "  End SyncLock")]
    public void Statement_blocks_get_their_end(string opener, string end)
    {
        var result = Enter(2, "Class C", " Sub M()", opener, "   ", " End Sub", "End Class");
        Assert.Equal(end, result![4]);
    }

    [Fact]
    public void Try_gets_Catch_and_End_Try()
    {
        var result = Enter(2, "Class C", " Sub M()", "  Try", "   ", " End Sub", "End Class");
        Assert.Equal(["  Catch ex As Exception", "  End Try"], result![4..6]);
    }

    [Fact]
    public void A_new_Sub_between_methods_gets_End_Sub()
    {
        var result = Enter(3, "Class C", " Sub A()", " End Sub", " Private Sub B(ByVal x As Integer)", "  ", " Sub D()", " End Sub", "End Class");
        Assert.Equal(" End Sub", result![5]);
        Assert.Equal(" Function F() As String", Enter(1, "Class C", " Function F() As String", "  ", "End Class")![1]);
        Assert.Equal(" End Function", Enter(1, "Class C", " Function F() As String", "  ", "End Class")![3]);
    }

    [Fact]
    public void Type_blocks_use_canonical_casing()
    {
        Assert.Equal("End Class", Enter(0, "class Foo", " ")![2]);
        Assert.Equal("End Module", Enter(0, "Module M", " ")![2]);
        Assert.Equal("End Namespace", Enter(0, "Namespace N", " ")![2]);
    }

    [Fact]
    public void Nothing_when_the_block_is_already_closed_or_not_a_block()
    {
        Assert.Null(Enter(2, "Class C", " Sub M()", "  If x Then", "   ", "  End If", " End Sub", "End Class"));
        Assert.Null(Enter(2, "Class C", " Sub M()", "  If x Then y = 1", "  ", " End Sub", "End Class"));
        Assert.Null(Enter(1, "Class C", " Property Name As String", " ", "End Class"));
        Assert.Null(Enter(3, "Class C", " Sub M()", "  If a Then", "   If b Then", "    ", "   End If", "  End If", " End Sub", "End Class"));
        Assert.Null(Enter(4, "Class C", " Sub M()", "  If a Then", "   Foo()", "  ElseIf b Then", "   ", "  End If", " End Sub", "End Class"));
    }

    [Fact]
    public void A_new_block_inside_one_of_the_same_kind_gets_its_end()
    {
        // The parser pairs the new inner If with the outer End If; the outer If is the one left open.
        var result = Enter(3, "Class C", " Sub M()", "  If a Then", "   If b Then", "    ", "   Foo()", "  End If", " End Sub", "End Class");
        Assert.Equal(["   If b Then", "    ", "   End If", "   Foo()", "  End If"], result![3..8]);
        Assert.Equal("   Next", Enter(3, "Class C", " Sub M()", "  For i = 1 To 3", "   For j = 1 To 3", "    ", "   Foo()", "  Next", " End Sub", "End Class")![5]);
        Assert.Equal("   End Try", Enter(3, "Class C", " Sub M()", "  Try", "   Try", "    ", "   Foo()", "  Catch", "  End Try", " End Sub", "End Class")![6]);
    }

    [Fact]
    public void End_of_file_without_a_final_newline()
    {
        Assert.Equal(["class Foo", " ", "End Class"], Enter(0, "class Foo", " ")!);
    }

    [Fact]
    public void Splitting_a_single_line_If_makes_a_block()
    {
        // Enter after "Then" (VS Code indents the rest) or before "Trace" (it doesn't, say): same result.
        string[] expected = ["  If Trace.IsEnabled Then", "   Trace.Warn(\"OrderForm\", \"SQLCmd: \" + SQLCmd.ToString())", "  End If", " End Sub"];
        Assert.Equal(expected, Enter(2, "Class C", " Sub M()", "  If Trace.IsEnabled Then", "   Trace.Warn(\"OrderForm\", \"SQLCmd: \" + SQLCmd.ToString())", " End Sub", "End Class")![2..6]);
        Assert.Equal(expected, Enter(2, "Class C", " Sub M()", "  If Trace.IsEnabled Then", "Trace.Warn(\"OrderForm\", \"SQLCmd: \" + SQLCmd.ToString())", " End Sub", "End Class")![2..6]);
        // The rest can be several statements.
        Assert.Equal(["   a() : b()", "  End If"], Enter(2, "Class C", " Sub M()", "  If x Then", " a() : b()", " End Sub", "End Class")![3..5]);
    }

    [Fact]
    public void The_cursor_stays_in_front_of_the_moved_code()
    {
        var text = SourceText.From(string.Join("\r\n", "Class C", " Sub M()", "  If x Then", "Foo()", " End Sub", "End Class"));
        var insertion = EndConstructs.AfterEnter(VisualBasicSyntaxTree.ParseText(text).GetRoot(), text, 2, "  ")!.Value;
        var position = text.WithChanges(insertion.Change).Lines.GetLinePosition(insertion.Cursor);
        Assert.Equal((3, 3), (position.Line, position.Character));

        text = SourceText.From(string.Join("\r\n", "Class C", " Sub M()", "  If x Then", "   ", " End Sub", "End Class"));
        insertion = EndConstructs.AfterEnter(VisualBasicSyntaxTree.ParseText(text).GetRoot(), text, 2, "  ")!.Value;
        position = text.WithChanges(insertion.Change).Lines.GetLinePosition(insertion.Cursor);
        Assert.Equal((3, 3), (position.Line, position.Character));
    }

    [Fact]
    public void Nothing_when_Enter_splits_a_line_of_a_closed_block()
    {
        Assert.Null(Enter(2, "Class C", " Sub M()", "  If x Then", "   Foo()", "  End If", " End Sub", "End Class"));
        Assert.Null(Enter(1, "Class C", " Sub M()", "  ' a comment moved down", " End Sub", "End Class"));
        // Implicit line continuation: the If statement goes on past the finished line.
        Assert.Null(Enter(2, "Class C", " Sub M()", "  If a AndAlso", "   b Then Foo()", " End Sub", "End Class"));
    }
}
