# 0002. Synthetic in-memory Roslyn workspace on Linux, using NuGet reference assemblies

Date: 2026-11-02 · Status: Accepted (the "all `.vb` files" source list is superseded by [0004](0004-compile-like-aspnet-4x-web-site.md))

## Context

Code-behind files don't compile on their own: the control fields come from markup,
and no project file lists sources or references. Real language features (diagnostics,
completion) need a full semantic model, and the editor runs on Linux, where neither
the .NET Framework nor `System.Web` exists at runtime.

## Decision

The language server is a .NET (currently `net10.0`) process that builds a Roslyn VB
workspace in memory and writes nothing to disk:

- Sources: every `.vb` file in the site, including `App_Code`.
- References: `Bin/*.dll`, `web.config` `<assemblies>`, and .NET Framework reference
  assemblies from the `Microsoft.NETFramework.ReferenceAssemblies.*` NuGet packages.
- For each `.aspx` / `.ascx` / `.master` file, a virtual partial class declaring its
  `runat="server"` controls as `Protected WithEvents` fields.

The [feasibility spike](../spike-results.md) confirmed this works on Linux with no
Windows, Mono, or .NET Framework runtime: diagnostics are correct, and completions
include the generated fields and their members.

## Consequences

- Development and use need only the .NET SDK.
- The server is only as accurate as the virtual partials and reference list it builds.
  The spike's known gaps (language version, one compilation for the whole site, the
  user-control field type, `netstandard` facades) are listed in the spike results and
  must stay documented until they're resolved.
- If a hard blocker turns up, the fallback is to host the server on Windows and connect
  to it remotely. This ADR would then be superseded.
