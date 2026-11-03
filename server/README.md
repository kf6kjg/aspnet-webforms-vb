# server

Language server for the extension (stage 3). It's a .NET 10 process speaking LSP over
stdio and hosting an in-memory Roslyn VB workspace per Web Site, compiled the way ASP.NET
4.x does it. See [ADR 0002](../docs/adr/0002-roslyn-workspace-with-nuget-reference-assemblies.md),
[ADR 0004](../docs/adr/0004-compile-like-aspnet-4x-web-site.md),
[ADR 0006](../docs/adr/0006-language-server-implementation.md), and
[ADR 0007](../docs/adr/0007-runtime-via-dotnet-install-tool-and-user-supplied-gac-assemblies.md).

| Path | What |
| --- | --- |
| `AspxVb.Server/Site/` | The workspace model: markup parsing, generated control partials, web-reference proxies, references and options from `web.config` |
| `AspxVb.Server/Features/` | Completion, diagnostics, definition (including markup and metadata targets), hover |
| `AspxVb.Server/Lsp/` | The LSP surface (`StreamJsonRpc`) |
| `AspxVb.Server/Check.cs` | `--check <folder>`: compile every page of every site and summarize |
| `AspxVb.Server.Tests/` | Tests against `samples/`, including an end-to-end LSP session with the real process |

From the repo root:

```sh
mise run server:test    # unit + end-to-end tests
mise run server:check   # compile all real sites under $SITES_ROOT (read-only); set CHECK_DETAIL=BC30451;… to list every instance
                        # and ASPXVB_ASSEMBLY_FOLDERS=dir;… for GAC-only assemblies such as MySql.Data
mise run server:build   # publish the portable .dll into extension/server/ (ext:package does this too)
```
