// Feasibility spike: can a Roslyn VB compilation of a Web Site code-behind,
// plus a synthetic partial class declaring the markup controls, bind against
// .NET Framework 4.8 reference assemblies on Linux and give completions?
//
// The virtual partial classes below are hand-written. Generating them from
// markup is the language server's job, not the spike's.

using System.Diagnostics;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;

var meta = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
    .ToDictionary(a => a.Key, a => a.Value!);
var refDir = args.Length > 0 ? args[0] : meta["RefAssemblyDir"];
var siteDir = args.Length > 1 ? args[1] : meta["SampleSiteDir"];
Console.WriteLine($"Runtime:        {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine($"Reference dir:  {Path.GetFullPath(refDir)}");
Console.WriteLine($"Sample site:    {Path.GetFullPath(siteDir)}");

var sw = Stopwatch.StartNew();

// Roughly the <compilation><assemblies> list from the .NET 4.x root web.config,
// plus Microsoft.VisualBasic which vbc always references.
string[] frameworkAssemblies =
[
    "mscorlib", "Microsoft.VisualBasic", "Microsoft.CSharp", "System", "System.Configuration",
    "System.Core", "System.Data", "System.Data.DataSetExtensions", "System.Drawing",
    "System.EnterpriseServices", "System.Runtime.Serialization", "System.ServiceModel",
    "System.ComponentModel.DataAnnotations", "System.Web", "System.Web.ApplicationServices",
    "System.Web.DynamicData", "System.Web.Extensions", "System.Web.Services", "System.Xml",
    "System.Xml.Linq",
];
var references = frameworkAssemblies
    .Select(name => (MetadataReference)MetadataReference.CreateFromFile(Path.Combine(refDir, name + ".dll")))
    .ToList();

// Web Site compilation defaults: Option Strict Off, Explicit On, Infer On, and the
// namespaces ASP.NET imports for every page (root web.config + this site's web.config).
var globalImports = new[]
{
    "Microsoft.VisualBasic", "System", "System.Collections", "System.Collections.Generic",
    "System.Collections.Specialized", "System.Configuration", "System.Linq", "System.Text",
    "System.Text.RegularExpressions", "System.Web", "System.Web.Caching", "System.Web.Security",
    "System.Web.SessionState", "System.Web.UI", "System.Web.UI.HtmlControls",
    "System.Web.UI.WebControls", "System.Web.UI.WebControls.WebParts",
}.Select(GlobalImport.Parse);
var compilationOptions = new VisualBasicCompilationOptions(
        OutputKind.DynamicallyLinkedLibrary,
        globalImports: globalImports,
        optionStrict: OptionStrict.Off,
        optionExplicit: true,
        optionInfer: true)
    .WithMetadataImportOptions(MetadataImportOptions.Public);
var parseOptions = new VisualBasicParseOptions(LanguageVersion.Latest);

var codeFiles = new[]
{
    "Default.aspx.vb",
    "Controls/Header.ascx.vb",
    "App_Code/TextHelpers.vb",
}.Select(rel => (Path: Path.Combine(siteDir, rel), Text: File.ReadAllText(Path.Combine(siteDir, rel)))).ToList();

// What the ASP.NET build provider would generate from Default.aspx and Header.ascx.
var virtualFiles = new List<(string Path, string Text)>
{
    ("virtual://Default.aspx.g.vb", """
        Partial Class _Default
            Protected WithEvents ucHeader As Global.Controls_Header
            Protected WithEvents lblName As Global.System.Web.UI.WebControls.Label
            Protected WithEvents txtName As Global.System.Web.UI.WebControls.TextBox
            Protected WithEvents btnSave As Global.System.Web.UI.WebControls.Button
            Protected WithEvents btnClear As Global.System.Web.UI.WebControls.Button
            Protected WithEvents lblResult As Global.System.Web.UI.WebControls.Label
        End Class
        """),
    ("virtual://Header.ascx.g.vb", """
        Partial Class Controls_Header
            Protected WithEvents litTitle As Global.System.Web.UI.WebControls.Literal
        End Class
        """),
};
Console.WriteLine($"Setup:          {sw.ElapsedMilliseconds} ms");

// --- 1. Diagnostics without the virtual partials: expect "not declared" errors. ---
sw.Restart();
var without = Compile(codeFiles);
Console.WriteLine();
Console.WriteLine($"== Diagnostics WITHOUT virtual partial classes ({sw.ElapsedMilliseconds} ms) ==");
PrintDiagnostics(without);

// --- 2. Diagnostics with them: expect a clean compile, and a successful in-memory emit. ---
sw.Restart();
var with = Compile([.. codeFiles, .. virtualFiles]);
Console.WriteLine();
Console.WriteLine($"== Diagnostics WITH virtual partial classes ({sw.ElapsedMilliseconds} ms) ==");
PrintDiagnostics(with);
using (var pe = new MemoryStream())
{
    var emit = with.Emit(pe);
    Console.WriteLine($"In-memory emit: {(emit.Success ? "succeeded" : "FAILED")} ({pe.Length} bytes, nothing written to disk)");
}

// --- 2b. A real mistake must still be reported once the false positives are gone. ---
var typo = codeFiles.Select(f => f.Path.EndsWith("Default.aspx.vb")
    ? (f.Path, f.Text.Replace("TextHelpers.Clean(txtName.Text)", "TextHelpers.Clean(txtName.Txet)"))
    : f).ToList();
Console.WriteLine();
Console.WriteLine("== Diagnostics WITH virtual partials, after an in-memory typo (txtName.Txet) ==");
PrintDiagnostics(Compile([.. typo, .. virtualFiles]));

// --- 3. Completions through a Workspace (needs the MEF-composed Features layer). ---
sw.Restart();
var workspace = new AdhocWorkspace(MefHostServices.DefaultHost);
var projectInfo = ProjectInfo.Create(
    ProjectId.CreateNewId(), VersionStamp.Create(), "SampleSite", "SampleSite", LanguageNames.VisualBasic,
    compilationOptions: compilationOptions, parseOptions: parseOptions, metadataReferences: references);
var project = workspace.AddProject(projectInfo);
Document? defaultDoc = null;
foreach (var (path, text) in codeFiles.Concat(virtualFiles))
{
    var doc = workspace.AddDocument(DocumentInfo.Create(
        DocumentId.CreateNewId(project.Id), Path.GetFileName(path), filePath: path,
        loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create()))));
    if (path.EndsWith("Default.aspx.vb")) defaultDoc = doc;
}
defaultDoc = workspace.CurrentSolution.GetDocument(defaultDoc!.Id)!;
var completion = CompletionService.GetService(defaultDoc)
    ?? throw new InvalidOperationException("No VB CompletionService: Features assembly not composed into MEF host.");
Console.WriteLine();
Console.WriteLine($"== Completions (workspace setup {sw.ElapsedMilliseconds} ms) ==");

var source = (await defaultDoc.GetTextAsync()).ToString();
await ShowCompletions("member access on a generated control field: `txtName.|`",
    source.IndexOf("txtName.Text)") + "txtName.".Length, expect: ["Text", "MaxLength", "TextMode"]);
await ShowCompletions("member access on a user control field: `ucHeader.|`",
    source.IndexOf("ucHeader.Title") + "ucHeader.".Length, expect: ["Title"]);
await ShowCompletions("identifier at statement start inside btnSave_Click",
    source.IndexOf("lblResult.Text = TextHelpers"), expect: ["txtName", "lblResult", "TextHelpers", "IsPostBack", "Response"]);

return;

VisualBasicCompilation Compile(IEnumerable<(string Path, string Text)> files) =>
    VisualBasicCompilation.Create("App_Web_spike",
        files.Select(f => VisualBasicSyntaxTree.ParseText(f.Text, parseOptions, f.Path)),
        references, compilationOptions);

void PrintDiagnostics(Compilation compilation)
{
    var diagnostics = compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).ToList();
    if (diagnostics.Count == 0) Console.WriteLine("(no errors or warnings)");
    foreach (var d in diagnostics)
    {
        var span = d.Location.GetLineSpan();
        Console.WriteLine($"{Path.GetFileName(span.Path)}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {d.Severity} {d.Id}: {d.GetMessage()}");
    }
}

async Task ShowCompletions(string label, int position, string[] expect)
{
    var timer = Stopwatch.StartNew();
    var list = await completion.GetCompletionsAsync(defaultDoc, position);
    var names = list.ItemsList.Select(i => i.DisplayText).ToHashSet();
    var missing = expect.Where(e => !names.Contains(e)).ToList();
    Console.WriteLine($"-- {label}");
    Console.WriteLine($"   {names.Count} items in {timer.ElapsedMilliseconds} ms; first 15: {string.Join(", ", names.Order().Take(15))}");
    Console.WriteLine(missing.Count == 0
        ? $"   expected items present: {string.Join(", ", expect)}"
        : $"   MISSING expected items: {string.Join(", ", missing)}");
}
