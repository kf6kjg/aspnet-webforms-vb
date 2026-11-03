using Microsoft.CodeAnalysis;

namespace AspxVb.Server.Site;

/// <summary>
/// Maps a server tag to the .NET type ASP.NET would declare its field as: asp: tags to
/// System.Web controls (looked up case-insensitively in the reference assemblies, since
/// the developer writes &lt;asp:button&gt; too), HTML tags to System.Web.UI.HtmlControls as
/// HtmlTagNameToTypeMapper does, user controls to their code-behind class.
/// </summary>
public sealed class ControlTypes
{
    private readonly Dictionary<string, string> _aspTypes;

    private static readonly string[] AspNamespaces =
    [
        "System.Web.UI.WebControls", "System.Web.UI", "System.Web.UI.WebControls.WebParts", "System.Web.DynamicData",
    ];

    public ControlTypes(Compilation frameworkCompilation)
    {
        _aspTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var control = frameworkCompilation.GetTypeByMetadataName("System.Web.UI.Control");
        foreach (var ns in AspNamespaces)
        {
            foreach (var type in FindNamespace(frameworkCompilation.GlobalNamespace, ns)?.GetTypeMembers() ?? [])
            {
                if (type.DeclaredAccessibility == Accessibility.Public && !type.IsAbstract && InheritsFrom(type, control))
                    _aspTypes.TryAdd(type.Name, type.ToDisplayString());
            }
        }
    }

    private static INamespaceSymbol? FindNamespace(INamespaceSymbol root, string dotted)
    {
        var current = root;
        foreach (var part in dotted.Split('.'))
        {
            current = current.GetNamespaceMembers().FirstOrDefault(n => n.Name == part);
            if (current == null) return null;
        }
        return current;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol? baseType)
    {
        for (var t = type.BaseType; t != null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, baseType)) return true;
        return false;
    }

    /// <summary>Fully qualified type name for the tag, or null when unknown.</summary>
    public string? TypeFor(ServerControl control, Func<string, string, string?> userControlType)
    {
        var tag = control.Tag;
        if (tag.Prefix == null) return "System.Web.UI.HtmlControls." + HtmlType(tag, control.InServerHead);
        if (tag.Prefix.Equals("asp", StringComparison.OrdinalIgnoreCase))
            return _aspTypes.GetValueOrDefault(tag.Name);
        return userControlType(tag.Prefix, tag.Name) ?? "System.Web.UI.UserControl";
    }

    // HtmlTagNameToTypeMapper, plus HtmlHeadBuilder's title/link/meta inside a server <head>.
    private static string HtmlType(Tag tag, bool inServerHead) => tag.Name.ToLowerInvariant() switch
    {
        "title" when inServerHead => "HtmlTitle",
        "link" when inServerHead => "HtmlLink",
        "meta" when inServerHead => "HtmlMeta",
        "a" => "HtmlAnchor",
        "area" => "HtmlArea",
        "audio" => "HtmlAudio",
        "button" => "HtmlButton",
        "embed" => "HtmlEmbed",
        "form" => "HtmlForm",
        "head" => "HtmlHead",
        "iframe" => "HtmlIframe",
        "img" => "HtmlImage",
        "select" => "HtmlSelect",
        "source" => "HtmlSource",
        "table" => "HtmlTable",
        "td" or "th" => "HtmlTableCell",
        "textarea" => "HtmlTextArea",
        "tr" => "HtmlTableRow",
        "track" => "HtmlTrack",
        "video" => "HtmlVideo",
        "input" => (tag.Get("type")?.Value ?? "text").ToLowerInvariant() switch
        {
            "button" => "HtmlInputButton",
            "checkbox" => "HtmlInputCheckBox",
            "file" => "HtmlInputFile",
            "hidden" => "HtmlInputHidden",
            "image" => "HtmlInputImage",
            "password" => "HtmlInputPassword",
            "radio" => "HtmlInputRadioButton",
            "reset" => "HtmlInputReset",
            "submit" => "HtmlInputSubmit",
            "text" => "HtmlInputText",
            _ => "HtmlInputGenericControl",
        },
        _ => "HtmlGenericControl",
    };
}
