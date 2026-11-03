# 0003. mise for tool versions and project tasks

Date: 2026-11-02 · Status: Accepted

## Context

The repo needs the .NET SDK (spike, scanner, server) and Node.js (extension), at known
versions, on a machine where neither may be installed system-wide. It also has a few
commands people need to remember (run the spike, scan the sites), and some machine-
specific settings (where the site share is mounted).

## Decision

Use [mise-en-place](https://mise.jdx.dev/):

- `mise.toml` pins tool versions (`dotnet`, `node`) and defines the project's tasks
  (`mise run spike`, `mise run scan`, and later build/test/package tasks).
- `mise.local.toml` (gitignored) holds machine-specific environment such as
  `SITES_ROOT`, `SITES_DRIVE_MAP`, and `SCAN_EXCLUDE`.
- `mise.toml` is the only place the SDK version is pinned: no `global.json`, `.nvmrc`,
  or similar.
- Small scripts are .NET file-based apps (`dotnet run scripts/foo.cs`), so they need no
  third toolchain and can use Roslyn directly.

## Consequences

- Setup is `mise install` and nothing else; a new task means editing `mise.toml`.
- Running `dotnet` outside mise picks up whatever SDK is on `PATH`, which may not match.
- A fresh clone needs `mise trust` before mise will load the config.
