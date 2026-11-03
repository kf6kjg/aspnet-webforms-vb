# 0007. .NET runtime from the .NET Install Tool; GAC-only assemblies supplied by the user

Date: 2026-11-02 · Status: Accepted. Supersedes the shipping decision in
[0006](0006-language-server-implementation.md) and the NuGet `MySql.Data` stand-in in
[0004](0004-compile-like-aspnet-4x-web-site.md).

## Context

ADR 0006 shipped the server self-contained for linux-x64, which made the `.vsix` about
68 MB, tied it to one platform, and meant .NET security fixes only arrived when the
extension was rebuilt. Microsoft's C# extension solves the same problem without asking
users to install anything: it depends on the **.NET Install Tool** extension
(`ms-dotnettools.vscode-dotnet-runtime`). That tool finds a suitable .NET on the machine
or downloads a private runtime and keeps it updated.

ADR 0004 bundled `MySql.Data` 6.10.6 from NuGet as a stand-in for the copy in the
server's GAC. The project director decided not to ship it: the user should supply such
assemblies, with instructions shown when one is missing and causes errors.

## Decision

- **Runtime:**
  - The server is published framework-dependent: a portable `.dll` with
    `RollForward=Major`, English resources only.
  - The extension declares `ms-dotnettools.vscode-dotnet-runtime` in
    `extensionDependencies`.
  - At startup the extension calls `dotnet.findPath` for .NET 10 or newer (runtime). If
    nothing qualifies, it calls `dotnet.acquire`. This is the order the C# extension uses.
  - `aspxVb.server.path` can still point at another build: a `.dll`, or an executable run
    as-is.
- **GAC-only assemblies:**
  - Nothing beyond the .NET Framework reference assemblies is bundled.
  - The `aspxVb.assemblyFolders` setting lists folders to search for assemblies that
    `web.config` names but that are in neither `Bin` nor the framework.
  - When an open file of such a site has errors, the server sends `aspxVb/missingAssemblies`
    once per site per session. The extension then shows a warning with **How to fix**
    (opens `MISSING-ASSEMBLIES.md`) and **Don't show again** buttons.
  - The instructions cover getting the DLL from NuGet or from the server's GAC. They warn
    against putting it in the site's `Bin`, which is deployed.
  - `server:check` reads the same folders from `$ASPXVB_ASSEMBLY_FOLDERS`.

## Consequences

- The `.vsix` is about 29 MB and platform-neutral, and the runtime gets security fixes
  without a new extension release.
- First start needs network access if no .NET 10+ is installed (the Install Tool downloads
  about 30 MB once). Installing the `.vsix` from a file also pulls in the Install Tool from
  the marketplace.
- Until the user configures a folder, sites that use MySQL show many errors (about 2,800
  across 10 of the developer's sites). The warning appears the first time.
- The VS Code side of runtime acquisition can only be exercised inside VS Code. The
  server itself is tested by running the published `.dll` with a .NET 10 runtime.
