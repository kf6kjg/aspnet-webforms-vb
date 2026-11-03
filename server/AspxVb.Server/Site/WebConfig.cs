using System.Xml.Linq;

namespace AspxVb.Server.Site;

/// <summary>The parts of a site's web.config that affect compilation. Never reads app settings or connection strings.</summary>
public sealed class WebConfig
{
    public bool? Strict { get; private init; }
    public bool? Explicit { get; private init; }
    public bool Debug { get; private init; }
    public bool ClearAssemblies { get; private init; }
    public List<string> Assemblies { get; } = [];
    public bool ClearNamespaces { get; private init; }
    public List<string> Namespaces { get; } = [];

    public static WebConfig Load(string siteRoot)
    {
        var path = Directory.EnumerateFiles(siteRoot).FirstOrDefault(f => Path.GetFileName(f).Equals("web.config", StringComparison.OrdinalIgnoreCase));
        if (path == null) return new WebConfig();
        XDocument doc;
        try { doc = XDocument.Parse(FileText.Read(path)); }
        catch { return new WebConfig(); }
        foreach (var el in doc.Descendants()) el.Name = el.Name.LocalName;

        var web = doc.Root?.Element("system.web");
        var comp = web?.Element("compilation");
        var assemblies = comp?.Element("assemblies");
        var namespaces = web?.Element("pages")?.Element("namespaces");
        var config = new WebConfig
        {
            Strict = ParseBool((string?)comp?.Attribute("strict")),
            Explicit = ParseBool((string?)comp?.Attribute("explicit")),
            Debug = ParseBool((string?)comp?.Attribute("debug")) ?? false,
            ClearAssemblies = assemblies?.Element("clear") != null,
            ClearNamespaces = namespaces?.Element("clear") != null,
        };
        foreach (var add in assemblies?.Elements("add") ?? [])
            if ((string?)add.Attribute("assembly") is { } a) config.Assemblies.Add(a.Split(',')[0].Trim());
        foreach (var add in namespaces?.Elements("add") ?? [])
            if ((string?)add.Attribute("namespace") is { } n) config.Namespaces.Add(n.Trim());
        return config;
    }

    private static bool? ParseBool(string? s) => bool.TryParse(s, out var b) ? b : null;
}
