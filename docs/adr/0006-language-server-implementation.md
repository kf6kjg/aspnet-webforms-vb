# 0006. Language server implementation

Date: 2026-11-02 · Status: Accepted (the self-contained shipping decision is superseded by [0007](0007-runtime-via-dotnet-install-tool-and-user-supplied-gac-assemblies.md))

## Context

Stage 3 needs a process that speaks the Language Server Protocol and hosts the Roslyn
workspace from ADRs 0002 and 0004. The developer installs a `.vsix` on his own Linux
machine, which may not have .NET installed. The real sites contain things the spike
didn't cover, found by compiling every page (`mise run server:check`):
- web-service proxies generated from `App_WebReferences/*.wsdl`
- `<title runat="server">` outside a server `<head>`
- controls whose ID is a VB keyword (`id="return"`)

## Decision

- **Protocol layer:** `StreamJsonRpc` (Microsoft's JSON-RPC library, also used by
  Roslyn's own server) with hand-written LSP message shapes for the few methods we
  need: sync, completion and resolve, definition, hover, and diagnostics. Not the
  OmniSharp LSP library (aging), and not Roslyn's language server (it expects MSBuild
  projects, which Web Sites don't have).
- **Shipping:** the server is published self-contained for linux-x64 into the extension
  (`mise run server:build`), with the .NET Framework 4.6.1 reference assemblies and their
  XML docs, plus the NuGet `MySql.Data` stand-in. The `.vsix` is about 68 MB. No .NET
  install is needed. The `aspxVb.server.path` setting can point at another build.
- **Workspace:**
  - one Roslyn workspace per site root: the nearest folder with a `web.config` that also
    has `App_Code` or `Bin`
  - page and user-control projects are created the first time a file needs them
  - a page project references the projects of the user controls it registers
- **Web references:** proxies are generated in memory from each WSDL, in the shape
  `wsdl.exe` produces, and compiled with App_Code as ASP.NET does. Go to definition on a
  proxy type opens the `.wsdl`.
- **Generated control fields** follow ASP.NET's tag mapping, including `HtmlHeadBuilder`'s
  `title`/`link`/`meta` inside a server `<head>`. Reserved words are escaped (`[return]`).
- **Diagnostics:** errors and warnings exactly as the compiler reports them, for open
  `.vb` files. Nothing is filtered or suppressed.
- **Validation:** `mise run server:check` compiles every page of every site under
  `$SITES_ROOT`. At the time of writing, every error it reports on a live page is a real
  bug in the site's code (undeclared variables, `Handles` on controls that no longer
  exist, syntax errors in dead pages).

## Consequences

- The `.vsix` is large, and most of the size is the runtime and the reference XML docs.
  Trimming the XML to the assemblies sites actually use would cut it substantially if
  size becomes a problem.
- Linux x64 only. Another platform needs another `-r` publish.
- The WSDL proxy generator covers document/literal services (what ASMX emits). An
  unusual WSDL may produce `Object`-typed members instead of real types.
- Any new pattern from the real sites that produces a false error should be fixed in the
  model, never by hiding the diagnostic, and checked with `server:check`.
