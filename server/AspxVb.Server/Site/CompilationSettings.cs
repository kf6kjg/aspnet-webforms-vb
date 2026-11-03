using System.Collections.Concurrent;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace AspxVb.Server.Site;

/// <summary>
/// References and compiler options for one site, matching what ASP.NET 4.x with the in-box
/// VB compiler uses (ADR 0004): root web.config defaults plus the site's web.config, Bin,
/// .NET Framework 4.6.1 reference assemblies, and user-configured folders for assemblies the
/// server loads from its GAC (ADR 0007).
/// </summary>
public sealed class CompilationSettings
{
    public static readonly string RefsDir = Path.Combine(AppContext.BaseDirectory, "refs");
    private static readonly string FrameworkDir = Path.Combine(RefsDir, "net461");
    private static readonly ConcurrentDictionary<string, MetadataReference> Cache = new(StringComparer.Ordinal);

    // <compilation><assemblies> in the .NET 4.x root web.config, plus Microsoft.VisualBasic (vbc adds it).
    private static readonly string[] DefaultAssemblies =
    [
        "mscorlib", "Microsoft.VisualBasic", "Microsoft.CSharp", "System", "System.Configuration", "System.Web",
        "System.Data", "System.Web.Services", "System.Xml", "System.Drawing", "System.EnterpriseServices",
        "System.IdentityModel", "System.Runtime.Serialization", "System.ServiceModel", "System.ServiceModel.Activation",
        "System.ServiceModel.Web", "System.Activities", "System.ServiceModel.Activities", "System.WorkflowServices",
        "System.Core", "System.Web.Extensions", "System.Data.DataSetExtensions", "System.Xml.Linq",
        "System.ComponentModel.DataAnnotations", "System.Web.DynamicData", "System.Web.ApplicationServices", "System.Xaml",
    ];

    // <pages><namespaces> in the .NET 4.x root web.config, plus Microsoft.VisualBasic (vbc imports it).
    private static readonly string[] DefaultNamespaces =
    [
        "Microsoft.VisualBasic", "System", "System.Collections", "System.Collections.Generic", "System.Collections.Specialized",
        "System.ComponentModel.DataAnnotations", "System.Configuration", "System.Linq", "System.Text",
        "System.Text.RegularExpressions", "System.Web", "System.Web.Caching", "System.Web.DynamicData",
        "System.Web.SessionState", "System.Web.Security", "System.Web.Profile", "System.Web.UI",
        "System.Web.UI.WebControls", "System.Web.UI.WebControls.WebParts", "System.Web.UI.HtmlControls", "System.Xml.Linq",
    ];

    public IReadOnlyList<MetadataReference> References { get; }
    public VisualBasicCompilationOptions Options { get; }
    public VisualBasicParseOptions ParseOptions { get; }
    /// <summary>web.config assemblies found in neither the framework, Bin, nor the assembly folders.</summary>
    public IReadOnlyList<string> UnresolvedAssemblies { get; }

    public CompilationSettings(string siteRoot, WebConfig config, IReadOnlyList<string> assemblyFolders)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // simple name -> file
        var unresolved = new List<string>();

        foreach (var dll in ManagedDlls(Path.Combine(siteRoot, "Bin"))) paths[Path.GetFileNameWithoutExtension(dll)] = dll;
        // GAC beats Bin for framework assemblies, as on the server.
        var names = (config.ClearAssemblies ? ["mscorlib", "Microsoft.VisualBasic"] : DefaultAssemblies).Concat(config.Assemblies);
        foreach (var name in names)
        {
            var framework = Path.Combine(FrameworkDir, name + ".dll");
            if (File.Exists(framework)) paths[name] = framework;
            else if (paths.ContainsKey(name)) continue;
            else if (assemblyFolders.Select(f => FileText.OnDisk(Path.Combine(f, name + ".dll"))).FirstOrDefault(p => p != null) is { } found) paths[name] = found;
            else unresolved.Add(name);
        }
        foreach (var facade in Directory.EnumerateFiles(Path.Combine(FrameworkDir, "Facades"), "*.dll"))
            paths.TryAdd(Path.GetFileNameWithoutExtension(facade), facade);

        References = paths.Values.Select(Reference).ToList();
        UnresolvedAssemblies = unresolved;

        var imports = (config.ClearNamespaces ? ["Microsoft.VisualBasic"] : DefaultNamespaces).Concat(config.Namespaces)
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(GlobalImport.Parse);
        Options = new VisualBasicCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                globalImports: imports,
                optionStrict: config.Strict == true ? OptionStrict.On : OptionStrict.Off,
                optionExplicit: config.Explicit ?? true,
                optionInfer: true)
            .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default);

        var symbols = new List<KeyValuePair<string, object>> { new("TRACE", true), new("_MYTYPE", "Web") };
        if (config.Debug) symbols.Add(new("DEBUG", true));
        ParseOptions = new VisualBasicParseOptions(LanguageVersion.VisualBasic11, preprocessorSymbols: symbols);
    }

    /// <summary>References for type lookups that don't depend on a site (control type index).</summary>
    public static IReadOnlyList<MetadataReference> FrameworkOnly() =>
        DefaultAssemblies.Select(n => Path.Combine(FrameworkDir, n + ".dll")).Where(File.Exists).Select(Reference).ToList();

    private static MetadataReference Reference(string path) => Cache.GetOrAdd(path, p =>
    {
        var xml = Path.ChangeExtension(p, ".xml");
        return MetadataReference.CreateFromFile(p, documentation: File.Exists(xml) ? XmlDocumentationProvider.CreateFromFile(xml) : null);
    });

    private static IEnumerable<string> ManagedDlls(string binDir)
    {
        var dir = FileText.OnDisk(binDir);
        if (dir == null || !Directory.Exists(dir)) yield break;
        foreach (var dll in Directory.EnumerateFiles(dir, "*.dll"))
        {
            bool managed;
            try
            {
                using var stream = File.OpenRead(dll);
                using var pe = new PEReader(stream);
                managed = pe.HasMetadata;
            }
            catch { managed = false; }
            if (managed) yield return dll;
        }
    }
}
