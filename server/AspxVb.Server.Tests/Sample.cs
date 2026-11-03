using AspxVb.Server.Site;

namespace AspxVb.Server.Tests;

/// <summary>Paths into samples/SampleSite, which the tests read but never modify.</summary>
public static class Sample
{
    public static readonly string Root = FindRoot();

    public static string File(string relative) => Path.Combine(Root, relative);

    public static SiteWorkspace Site() => new(Root);

    /// <summary>Offset just after the n-th occurrence of <paramref name="needle"/> plus <paramref name="delta"/>.</summary>
    public static int At(string text, string needle, int delta = 0)
    {
        var i = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(i >= 0, $"'{needle}' not found");
        return i + needle.Length + delta;
    }

    private static string FindRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, "samples", "SampleSite");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("samples/SampleSite not found above the test directory.");
    }
}
