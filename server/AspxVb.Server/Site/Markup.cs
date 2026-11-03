using System.Text.RegularExpressions;

namespace AspxVb.Server.Site;

// Light parsing of .aspx/.ascx/.master markup, ported from extension/src/markup.ts so the
// server and the extension agree on what a directive, tag or server control is.

public sealed record Attr(string Name, string Value, int ValueStart)
{
    public int ValueEnd => ValueStart + Value.Length;
}

public sealed record Directive(string Name, int Start, int End, IReadOnlyList<Attr> Attrs)
{
    public Attr? Get(string name) => Markup.Get(Attrs, name);
}

public sealed record Tag(string? Prefix, string Name, int NameStart, int Start, int End, IReadOnlyList<Attr> Attrs, bool Closing, bool SelfClosing)
{
    public Attr? Get(string name) => Markup.Get(Attrs, name);
    public bool IsServer => string.Equals(Get("runat")?.Value, "server", StringComparison.OrdinalIgnoreCase);
}

public sealed record Registration(string TagPrefix, string TagName, Attr Src);

/// <summary>
/// A runat="server" control with an ID, outside any multi-instance template. InServerHead:
/// inside &lt;head runat="server"&gt;, where ASP.NET makes title/link/meta typed controls.
/// </summary>
public sealed record ServerControl(Tag Tag, Attr Id, bool InServerHead);

public static partial class Markup
{
    [GeneratedRegex("""([A-Za-z_][\w.:-]*)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))""")]
    private static partial Regex AttrPattern();

    [GeneratedRegex(@"<%--[\s\S]*?--%>|<%[\s\S]*?%>")]
    private static partial Regex ServerBlockPattern();

    [GeneratedRegex(@"<%@\s*([\s\S]*?)%>")]
    private static partial Regex DirectivePattern();

    [GeneratedRegex("""<(/?)([A-Za-z][\w.-]*)(?::([A-Za-z][\w.-]*))?((?:"[^"]*"|'[^']*'|[^'">])*)>""")]
    private static partial Regex TagPattern();

    public static Attr? Get(IReadOnlyList<Attr> attrs, string name) =>
        attrs.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    private static List<Attr> ParseAttrs(string text, int baseOffset)
    {
        var attrs = new List<Attr>();
        foreach (Match m in AttrPattern().Matches(text))
        {
            var g = m.Groups[2].Success ? m.Groups[2] : m.Groups[3].Success ? m.Groups[3] : m.Groups[4];
            attrs.Add(new Attr(m.Groups[1].Value, g.Value, baseOffset + g.Index));
        }
        return attrs;
    }

    /// <summary>
    /// Blank out &lt;% … %&gt; blocks and server comments (same length, newlines kept), so tag
    /// parsing doesn't trip over quotes or '&gt;' in server code. Controls inside a server
    /// comment disappear, as they do for ASP.NET.
    /// </summary>
    public static string Mask(string text) =>
        ServerBlockPattern().Replace(text, m => Regex.Replace(m.Value, @"[^\n]", " "));

    public static List<Directive> Directives(string text)
    {
        var result = new List<Directive>();
        foreach (Match m in DirectivePattern().Matches(text))
        {
            var body = m.Groups[1];
            var nameMatch = Regex.Match(body.Value, @"^([A-Za-z]+)\b(?!\s*=)");
            var name = nameMatch.Success ? nameMatch.Groups[1].Value : "";
            result.Add(new Directive(name, m.Index, m.Index + m.Length, ParseAttrs(body.Value[name.Length..], body.Index + name.Length)));
        }
        return result;
    }

    /// <summary>The @Page, @Control or @Master directive, which carries CodeFile and Inherits.</summary>
    public static Directive? MainDirective(string text) =>
        Directives(text).FirstOrDefault(d => d.Name.Equals("Page", StringComparison.OrdinalIgnoreCase)
                                          || d.Name.Equals("Control", StringComparison.OrdinalIgnoreCase)
                                          || d.Name.Equals("Master", StringComparison.OrdinalIgnoreCase));

    public static List<Registration> Registrations(string text) =>
        Directives(text)
            .Where(d => d.Name.Equals("Register", StringComparison.OrdinalIgnoreCase))
            .Select(d => (Prefix: d.Get("TagPrefix"), Name: d.Get("TagName"), Src: d.Get("Src")))
            .Where(r => r.Prefix != null && r.Name != null && r.Src != null)
            .Select(r => new Registration(r.Prefix!.Value, r.Name!.Value, r.Src!))
            .ToList();

    /// <summary>All tags in already-masked markup.</summary>
    public static List<Tag> Tags(string masked)
    {
        var result = new List<Tag>();
        foreach (Match m in TagPattern().Matches(masked))
        {
            var hasPrefix = m.Groups[3].Success;
            var nameStart = m.Index + 1 + m.Groups[1].Length;
            var attrsGroup = m.Groups[4];
            result.Add(new Tag(
                hasPrefix ? m.Groups[2].Value : null,
                hasPrefix ? m.Groups[3].Value : m.Groups[2].Value,
                nameStart,
                m.Index,
                m.Index + m.Length,
                ParseAttrs(attrsGroup.Value, attrsGroup.Index),
                m.Groups[1].Value == "/",
                attrsGroup.Value.TrimEnd().EndsWith('/')));
        }
        return result;
    }

    /// <summary>
    /// Server controls that get a field in the generated partial class: runat="server" with an
    /// ID, first occurrence of each ID, not inside a repeating template (ItemTemplate etc.,
    /// where ASP.NET creates one control per row and no field). ContentTemplate is single-
    /// instance, so its controls do get fields.
    /// </summary>
    public static List<ServerControl> ServerControls(string text)
    {
        var result = new List<ServerControl>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depth = 0;
        var serverHead = false;
        foreach (var t in Tags(Mask(text)))
        {
            if (t.Prefix == null && t.Name.Equals("head", StringComparison.OrdinalIgnoreCase))
                serverHead = !t.Closing && t.IsServer;
            if (t.Prefix == null && t.Name.EndsWith("Template", StringComparison.OrdinalIgnoreCase)
                && !t.Name.Equals("ContentTemplate", StringComparison.OrdinalIgnoreCase))
            {
                if (t.Closing) depth = Math.Max(0, depth - 1);
                else if (!t.SelfClosing) depth++;
                continue;
            }
            if (t.Closing || depth > 0 || !t.IsServer) continue;
            if (t.Prefix?.Equals("asp", StringComparison.OrdinalIgnoreCase) == true && t.Name.Equals("Content", StringComparison.OrdinalIgnoreCase)) continue;
            var id = t.Get("ID");
            if (id == null || !SyntaxFacts.IsIdentifier(id.Value) || !seen.Add(id.Value)) continue;
            result.Add(new ServerControl(t, id, serverHead));
        }
        return result;
    }

    private static class SyntaxFacts
    {
        public static bool IsIdentifier(string s) => Regex.IsMatch(s, @"^[A-Za-z_]\w*$");
    }
}
