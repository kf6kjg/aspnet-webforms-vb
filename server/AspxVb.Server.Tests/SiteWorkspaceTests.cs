using AspxVb.Server.Features;

namespace AspxVb.Server.Tests;

public class SiteWorkspaceTests
{
    private readonly LanguageFeatures _features = new();

    private static async Task<string> GeneratedPartialFor(string codeBehind)
    {
        var site = Sample.Site();
        var doc = site.GetDocument(Sample.File(codeBehind))!;
        var generated = doc.Project.Documents.Single(d => d.FilePath!.EndsWith(".g.vb"));
        return (await generated.GetTextAsync()).ToString();
    }

    [Fact]
    public async Task Generated_partial_declares_markup_controls_with_AspNet_types()
    {
        var text = await GeneratedPartialFor("Default.aspx.vb");
        Assert.Contains("Partial Class _Default", text);
        Assert.Contains("Protected WithEvents ucHeader As Global.Controls_Header", text);
        Assert.Contains("Protected WithEvents txtName As Global.System.Web.UI.WebControls.TextBox", text);
        Assert.Contains("Protected WithEvents btnClear As Global.System.Web.UI.WebControls.Button", text);
    }

    [Fact]
    public async Task Generated_partial_handles_html_controls_keywords_and_head_context()
    {
        var text = await GeneratedPartialFor("PayPal.aspx.vb");
        Assert.Contains("[return] As Global.System.Web.UI.HtmlControls.HtmlInputHidden", text);
        Assert.Contains("PgTitle As Global.System.Web.UI.HtmlControls.HtmlGenericControl", text); // <head> isn't runat=server
        Assert.Contains("Total As Global.System.Web.UI.HtmlControls.HtmlTableCell", text);
        Assert.Contains("TotalRow As Global.System.Web.UI.HtmlControls.HtmlTableRow", text);
        Assert.Contains("btnPay As Global.System.Web.UI.WebControls.Button", text); // <asp:button>
    }

    [Theory]
    [InlineData("Default.aspx.vb")]
    [InlineData("PayPal.aspx.vb")]
    [InlineData("Controls/Header.ascx.vb")]
    [InlineData("App_Code/TextHelpers.vb")]
    [InlineData("App_Code/Mailer.vb")]
    public async Task Sample_site_compiles_without_diagnostics(string file)
    {
        var site = Sample.Site();
        var problems = await _features.DiagnosticsAsync(site.GetDocument(Sample.File(file))!, default);
        Assert.Empty(problems.Select(p => $"{p.Code} {p.Message}"));
    }

    [Fact]
    public async Task Real_mistakes_are_reported_and_closing_restores_disk_text()
    {
        var site = Sample.Site();
        var path = Sample.File("Default.aspx.vb");
        site.SetText(path, File.ReadAllText(path).Replace("txtName.Text)", "txtName.Txet)"));
        var problems = await _features.DiagnosticsAsync(site.GetDocument(path)!, default);
        Assert.Equal(["BC30456"], problems.Select(p => p.Code));
        site.Close(path);
        Assert.Empty(await _features.DiagnosticsAsync(site.GetDocument(path)!, default));
    }

    [Fact]
    public async Task Editing_markup_updates_the_controls_the_code_behind_can_see()
    {
        var site = Sample.Site();
        var markup = Sample.File("Default.aspx");
        var code = Sample.File("Default.aspx.vb");
        site.SetText(code, File.ReadAllText(code).Replace("lblResult.Text = String.Empty", "lblResult.Text = lblExtra.Text"));
        Assert.Contains("BC30451", (await _features.DiagnosticsAsync(site.GetDocument(code)!, default)).Select(p => p.Code));
        site.SetText(markup, File.ReadAllText(markup).Replace("</asp:Content>", "<asp:Label ID=\"lblExtra\" runat=\"server\" />\n</asp:Content>"));
        Assert.Empty(await _features.DiagnosticsAsync(site.GetDocument(code)!, default));
    }

    [Fact]
    public async Task Completion_lists_members_of_a_generated_control_field()
    {
        var site = Sample.Site();
        var doc = site.GetDocument(Sample.File("Default.aspx.vb"))!;
        var text = (await doc.GetTextAsync()).ToString();
        var (items, _, _) = await _features.CompletionAsync(doc, Sample.At(text, "TextHelpers.Clean(txtName."), default);
        Assert.Contains(items, i => i.Label == "Text");
        Assert.Contains(items, i => i.Label == "MaxLength");
        Assert.Contains("Property", await _features.CompletionDetailAsync(items.First(i => i.Label == "Text").Index, default));
    }

    [Fact]
    public async Task Definition_of_a_control_field_lands_on_its_ID_in_the_markup()
    {
        var site = Sample.Site();
        var doc = site.GetDocument(Sample.File("Default.aspx.vb"))!;
        var text = (await doc.GetTextAsync()).ToString();
        var span = Assert.Single(await _features.DefinitionAsync(site, doc, Sample.At(text, "TextHelpers.Clean(txt"), default));
        Assert.Equal(Sample.File("Default.aspx"), span.File);
        var markupLine = File.ReadAllLines(span.File)[span.StartLine];
        Assert.Equal("txtName", markupLine[span.StartChar..span.EndChar]);
    }

    [Fact]
    public async Task Definition_reaches_App_Code_web_references_and_framework_metadata()
    {
        var site = Sample.Site();
        var page = site.GetDocument(Sample.File("Default.aspx.vb"))!;
        var pageText = (await page.GetTextAsync()).ToString();
        var helper = Assert.Single(await _features.DefinitionAsync(site, page, Sample.At(pageText, "TextHelpers", -2), default));
        Assert.Equal(Sample.File("App_Code/TextHelpers.vb"), helper.File);

        var framework = Assert.Single(await _features.DefinitionAsync(site, page, Sample.At(pageText, "If Not IsPost"), default));
        Assert.StartsWith("aspxvb-metadata:/System.Web/", framework.File);
        Assert.Contains("IsPostBack", _features.MetadataViewText(framework.File)!.Split('\n')[framework.StartLine]);

        var mailer = site.GetDocument(Sample.File("App_Code/Mailer.vb"))!;
        var mailerText = (await mailer.GetTextAsync()).ToString();
        var proxy = Assert.Single(await _features.DefinitionAsync(site, mailer, Sample.At(mailerText, "New com.example.mail.Mail"), default));
        Assert.EndsWith("App_WebReferences/com/example/mail/MailService.wsdl", proxy.File);
    }

    [Fact]
    public async Task Hover_describes_the_symbol()
    {
        var site = Sample.Site();
        var doc = site.GetDocument(Sample.File("Default.aspx.vb"))!;
        var text = (await doc.GetTextAsync()).ToString();
        var hover = await _features.HoverAsync(doc, Sample.At(text, "TextHelpers.Clean(txt"), default);
        Assert.Contains("TextBox", hover);
    }

    [Fact]
    public async Task A_backup_copy_no_markup_names_is_not_compiled_and_causes_no_conflict()
    {
        var site = Sample.Site();
        Assert.True(File.Exists(Sample.File("Default.aspx.bak.vb")));
        Assert.Null(site.GetDocument(Sample.File("Default.aspx.bak.vb")));
        Assert.Empty(await _features.DiagnosticsAsync(site.GetDocument(Sample.File("Default.aspx.vb"))!, default));
    }
}
