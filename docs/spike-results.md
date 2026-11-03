# Feasibility spike results

Date: 2026-11-02 · Code: the throwaway `spike/` console app, since removed (it's in the
git history at commit `2fc0b6c`)

## Question

Can a Roslyn VB compilation of a Web Site code-behind, plus a virtual partial class
declaring the markup's server controls, be built **on Linux** against .NET Framework
reference assemblies (including `System.Web`), and give correct diagnostics and
completions?

## Answer: yes

Tested on Ubuntu 26.04.1, .NET SDK 10.0.403 (runtime 10.0.14), Roslyn
`Microsoft.CodeAnalysis.VisualBasic.Features` / `.Workspaces` 5.9.0, and
`Microsoft.NETFramework.ReferenceAssemblies.net48` 1.0.3. No Mono, no Windows, no
.NET Framework runtime needed: Roslyn only reads the reference DLLs as metadata.

| Check | Result |
| --- | --- |
| Without virtual partials | 8 errors, as expected: `BC30451 'txtName' is not declared` etc., plus `BC30506 Handles clause requires a WithEvents variable` |
| With virtual partials | 0 errors, 0 warnings; in-memory emit succeeds (nothing written to disk) |
| With an injected typo `txtName.Txet` | Exactly 1 error: `BC30456 'Txet' is not a member of 'TextBox'` |
| Completion after `txtName.` | 73 `TextBox` members, including `Text`, `MaxLength`, `TextMode` |
| Completion after `ucHeader.` (user control) | 62 members, including the code-behind's own `Title` property |
| Completion at statement start | Includes `txtName`, `lblResult`, `TextHelpers` (App_Code), `IsPostBack`, `Response` (from `Page`) |

Timings on this machine: compilation and diagnostics run in tens of milliseconds;
building the MEF workspace takes about 0.6 s; the first completion takes about 0.4 s
(JIT warm-up) and later ones about 30 ms. That is a three-file site; real sites will
be slower and have not been measured.

## What broke

Nothing in the approach. The only problem was incidental: the reference-assemblies
package's MSBuild props are meant to retarget a `net48` build. The spike is `net10.0`
and only wants the files on disk, so it references the package with
`ExcludeAssets="all"` and `GeneratePathProperty="true"`.

## What the spike did not prove

Treat these as open risks for the language server, not as solved:

- **The virtual partials were hand-written.** Generating them from markup (control
  IDs, tag-to-type mapping, `@Register` resolution) is not tested.
- **No `Bin/*.dll` was referenced.** The sample has no third-party DLL. DLLs built
  against `netstandard` would also need `Facades/netstandard.dll`, which the spike does
  not add.
- **`web.config` is not read.** The reference list and global imports are hardcoded
  approximations of the .NET 4.x root `web.config` defaults.
- **The language version is a guess.** The spike uses `LanguageVersion.Latest`. If the
  real sites compile with the in-box .NET 4.x `vbc` (VB 10) rather than the Roslyn
  CodeDom provider, newer syntax would be accepted here but rejected by IIS.
- **The whole site is one compilation.** Real ASP.NET compiles each folder separately,
  so pages cannot see each other's classes without `<%@ Reference %>`. This setup is more
  permissive: it can miss some real errors, but it should not report false ones.
- **The site scan has since answered some of these;** see [scan-findings.md](scan-findings.md).
- **The user-control field type is an assumption.** The partial declares `ucHeader` as
  the control's code-behind class (`Controls_Header`), not the generated
  `ASP.controls_header_ascx`.
- **The ASP.NET-generated extras are left out**, such as the `Profile` and
  `ApplicationInstance` properties.
- **No LSP, incremental updates, or large-site performance testing.**

## Fallback (not currently needed)

Approach-level blockers in order of likelihood, each with its fallback:

1. **The sites target a framework version the NuGet packages don't cover**, or the dev
   uses a non-standard assembly: copy
   `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.x`
   from any Windows machine and point the server at it. (Packages exist for .NET 2.0
   through 4.8.1.)
2. **Something really does need the Windows .NET Framework runtime**, which nothing in
   this design does today: run the language server on a Windows host and have the
   extension connect to it over TCP or SSH stdio. Stages 1 and 2 (grammar, navigation)
   don't need the server at all.
