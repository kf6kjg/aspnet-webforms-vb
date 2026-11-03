# spike

Throwaway feasibility check. Not used by the extension or server.

Loads the sample site's code-behind (`Default.aspx.vb`, `Controls/Header.ascx.vb`,
`App_Code/TextHelpers.vb`) plus **hand-written** virtual partial classes declaring
the markup controls into a Roslyn VB compilation, references .NET Framework 4.8
reference assemblies from the `Microsoft.NETFramework.ReferenceAssemblies.net48`
NuGet package, then prints:

1. diagnostics without the virtual partials (expected: "not declared" errors),
2. diagnostics with them (expected: clean) and an in-memory emit,
3. the same with an injected typo (expected: one real error),
4. completions at three positions via `CompletionService`.

```sh
mise run spike
# optional: mise exec -- dotnet run --project spike -- <ref-assembly-dir> <site-dir>
```

Results: [docs/spike-results.md](../docs/spike-results.md).
