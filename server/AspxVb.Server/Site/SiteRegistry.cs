namespace AspxVb.Server.Site;

/// <summary>One SiteWorkspace per site root, created when a file of that site is first seen.</summary>
public sealed class SiteRegistry(IReadOnlyList<string> assemblyFolders)
{
    private readonly Dictionary<string, SiteWorkspace> _sites = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public SiteWorkspace? SiteFor(string file)
    {
        var root = SiteWorkspace.FindRoot(file);
        if (root == null) return null;
        lock (_gate)
        {
            if (!_sites.TryGetValue(root, out var site))
            {
                site = new SiteWorkspace(root, assemblyFolders);
                _sites[root] = site;
            }
            return site;
        }
    }

    public IReadOnlyList<SiteWorkspace> All()
    {
        lock (_gate) return _sites.Values.ToList();
    }
}
