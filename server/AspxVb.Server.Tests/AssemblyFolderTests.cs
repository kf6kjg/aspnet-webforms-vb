using AspxVb.Server.Features;
using AspxVb.Server.Site;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace AspxVb.Server.Tests;

/// <summary>
/// Assemblies the developer's server loads from its GAC (MySql.Data) aren't shipped; the user
/// points aspxVb.assemblyFolders at a folder holding them (ADR 0007).
/// </summary>
public sealed class AssemblyFolderTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("aspxvb-test-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    private string MakeSite()
    {
        var site = Directory.CreateDirectory(Path.Combine(_temp, "site")).FullName;
        File.WriteAllText(Path.Combine(site, "web.config"), """
            <configuration><system.web>
              <compilation debug="true" strict="false" explicit="true" targetFramework="4.6.1">
                <assemblies><add assembly="MySql.Data, Version=6.10.6.0, Culture=neutral, PublicKeyToken=C5687FC88969C44D" /></assemblies>
              </compilation>
              <pages><namespaces><add namespace="MySql.Data.MySqlClient" /></namespaces></pages>
            </system.web></configuration>
            """);
        Directory.CreateDirectory(Path.Combine(site, "App_Code"));
        File.WriteAllText(Path.Combine(site, "App_Code", "Db.vb"), """
            Public Module Db
                Public Function Open() As MySqlConnection
                    Return New MySqlConnection("server=localhost")
                End Function
            End Module
            """);
        return site;
    }

    /// <summary>A stand-in MySql.Data.dll with just the type the site uses.</summary>
    private string MakeAssemblyFolder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_temp, "assemblies")).FullName;
        var source = "Namespace MySql.Data.MySqlClient\n Public Class MySqlConnection\n  Public Sub New(s As String)\n  End Sub\n End Class\nEnd Namespace";
        var compilation = VisualBasicCompilation.Create("MySql.Data", [VisualBasicSyntaxTree.ParseText(source)],
            CompilationSettings.FrameworkOnly(), new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.True(compilation.Emit(Path.Combine(folder, "MySql.Data.dll")).Success);
        return folder;
    }

    [Fact]
    public async Task Without_the_assembly_the_site_reports_it_unresolved_and_code_using_it_errors()
    {
        var root = MakeSite();
        var site = new SiteWorkspace(root);
        Assert.Equal(["MySql.Data"], site.Settings.UnresolvedAssemblies);
        var problems = await new LanguageFeatures().DiagnosticsAsync(site.GetDocument(Path.Combine(root, "App_Code", "Db.vb"))!, default);
        Assert.Contains(problems, p => p.IsError && p.Code == "BC30002");
    }

    [Fact]
    public async Task An_assembly_folder_supplies_it()
    {
        var root = MakeSite();
        var site = new SiteWorkspace(root, [MakeAssemblyFolder()]);
        Assert.Empty(site.Settings.UnresolvedAssemblies);
        Assert.Empty(await new LanguageFeatures().DiagnosticsAsync(site.GetDocument(Path.Combine(root, "App_Code", "Db.vb"))!, default));
    }
}
