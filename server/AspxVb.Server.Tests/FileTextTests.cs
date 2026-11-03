using AspxVb.Server.Site;

namespace AspxVb.Server.Tests;

/// <summary>
/// References come from Windows, where case doesn't matter. Lookups match case-insensitively
/// and always return the on-disk spelling, so every path the server keys on agrees (ADR 0012).
/// </summary>
public sealed class FileTextTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("aspxvb-test-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private string MakeFile(string relative)
    {
        var path = Path.Combine(_temp, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void OnDisk_spells_each_name_as_on_disk()
    {
        var header = MakeFile(Path.Combine("Site", "Controls", "Header.ascx"));
        Assert.Equal(header, FileText.OnDisk(Path.Combine(_temp, "site", "controls", "header.ASCX")));
        Assert.Equal(header, FileText.OnDisk(header));
        Assert.Equal(Path.GetDirectoryName(header), FileText.OnDisk(Path.Combine(_temp, "SITE", "Controls")));
    }

    [Fact]
    public void OnDisk_is_null_for_a_missing_file()
    {
        MakeFile(Path.Combine("Site", "web.config"));
        Assert.Null(FileText.OnDisk(Path.Combine(_temp, "site", "Nope.aspx")));
        Assert.Null(FileText.OnDisk(Path.Combine(_temp, "Gone", "web.config")));
    }

    [Fact]
    public void OnDisk_prefers_the_exact_spelling_when_both_exist()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) return; // one name per spelling there
        var upper = MakeFile(Path.Combine("Site", "Logo.gif"));
        var lower = MakeFile(Path.Combine("Site", "logo.gif"));
        Assert.Equal(upper, FileText.OnDisk(upper));
        Assert.Equal(lower, FileText.OnDisk(lower));
    }

    [Fact]
    public void Resolve_returns_the_on_disk_spelling_of_a_site_rooted_reference()
    {
        var header = MakeFile(Path.Combine("Site", "Controls", "Header.ascx"));
        var page = MakeFile(Path.Combine("Site", "Shop", "Buy.aspx"));
        var root = Path.Combine(_temp, "Site");
        Assert.Equal(header, FileText.Resolve("~/controls/header.ascx", page, root));
        Assert.Equal(header, FileText.Resolve(@"..\CONTROLS\Header.ascx", page, root));
    }
}
