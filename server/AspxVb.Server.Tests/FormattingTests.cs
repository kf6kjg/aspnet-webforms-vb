using AspxVb.Server.Features;
using Microsoft.CodeAnalysis.Text;

namespace AspxVb.Server.Tests;

/// <summary>Pretty listing in the developer's style (ADR 0008): one-space indent, VB casing, comments never move, CRLF kept.</summary>
public class FormattingTests
{
    private static readonly string[] Messy =
    [
        "",
        "Partial Class Checkout",
        " Inherits System.Web.UI.Page",
        "",
        " Private Sub Page_Load(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Load",
        "     if pgtitle.innertext=\"\" then                ' aligned comment",
        "  pgtitle.innertext = \"Checkout\"",
        "          ' a comment at a fixed column",
        "  end if",
        " End Sub",
        "End Class",
        "",
    ];

    private static async Task<(SourceText Before, SourceText After, IReadOnlyList<TextChange> Changes)> Format(Func<SourceText, TextSpan> span)
    {
        var site = Sample.Site();
        var path = Sample.File("Checkout.aspx.vb");
        site.SetText(path, string.Join("\r\n", Messy));
        var doc = site.GetDocument(path)!;
        var before = await doc.GetTextAsync();
        var changes = await Formatting.FormatAsync(doc, span(before), default);
        return (before, before.WithChanges(changes), changes);
    }

    [Fact]
    public async Task Formats_like_pretty_listing_and_leaves_comments_where_they_are()
    {
        var (_, after, _) = await Format(t => new TextSpan(0, t.Length));
        var lines = after.ToString().Split("\r\n");
        Assert.Equal("  If PgTitle.InnerText = \"\" Then                ' aligned comment", lines[5]);
        Assert.Equal("   PgTitle.InnerText = \"Checkout\"", lines[6]);
        Assert.Equal("          ' a comment at a fixed column", lines[7]);
        Assert.Equal("  End If", lines[8]);
    }

    [Fact]
    public async Task Keeps_CRLF_line_endings()
    {
        var (before, after, changes) = await Format(t => new TextSpan(0, t.Length));
        Assert.NotEmpty(changes);
        Assert.DoesNotContain(changes, c => c.NewText!.Contains('\n'));
        Assert.Equal(before.Lines.Count, after.Lines.Count);
        Assert.DoesNotContain("\n", after.ToString().Replace("\r\n", ""));
    }

    [Fact]
    public async Task Formatting_one_line_touches_only_that_line()
    {
        var (before, after, changes) = await Format(t => t.Lines[8].Span); // what Enter after "end if" does
        Assert.Equal(["  End If"], changes.Select(c => c.NewText));
        Assert.Equal(before.Lines[5].ToString(), after.Lines[5].ToString());
    }

    [Fact]
    public async Task Already_formatted_code_is_left_alone()
    {
        var site = Sample.Site();
        foreach (var file in new[] { "Default.aspx.vb", "Checkout.aspx.vb", "Controls/Header.ascx.vb", "App_Code/TextHelpers.vb", "App_Code/Mailer.vb" })
        {
            var doc = site.GetDocument(Sample.File(file))!;
            var text = await doc.GetTextAsync();
            Assert.Empty(await Formatting.FormatAsync(doc, new TextSpan(0, text.Length), default));
        }
    }
}
