# 0012. Run on Windows too; warn when a reference's case differs from disk

Date: 2026-11-03 · Status: Accepted

## Context

The extension was built for VS Code on Linux. The project director asked for it to work
on Windows too. Both halves assumed POSIX paths: they split paths on `/`, walked them from
`/`, and tested "is this file in the site?" with `root + "/"`. On Windows that broke
navigation, filled every page with false "not found" problems, and kept the server from
publishing any VB diagnostics.

Case was the other difference. The developer's references don't always match on-disk
casing, and IIS doesn't care. On Linux, a lookup that missed fell back to a
case-insensitive scan and so returned the on-disk spelling. On Windows the exact path
already "exists" in any spelling, so the server got the reference's spelling instead, and
its exact-match path keys stopped agreeing (a page whose `CodeFile=` differed in case got no
IntelliSense). The project director also pointed out that a reference that only works because
of case-insensitivity would break if the site ever ran on a case-sensitive file system.

Measured on the developer's sites (read-only, 1,630 markup files): 5,899 references in
1,257 files differ in case from disk, but they come from only 55 distinct references. 98%
differ only in a folder name, mostly `/styles/` and `/scripts/` against `Styles` and
`Scripts` on disk. Six are `CodeFile=` values; no `@Register Src=` differs.

## Decision

- **Paths are OS-neutral.** Both lookups (`resolveOnDisk` in the extension,
  `FileText.OnDisk` in the server) walk from the path's real root (`/`, `c:\`,
  `\\server\share\`) and accept either separator. Folder checks use the platform separator.
- **Lookups always return the on-disk spelling**, on every OS. Each name matches exactly
  when it can and case-insensitively otherwise. The server's fast exact-path check is
  kept only on case-sensitive file systems, where a hit proves the case is right.
- **The server keys every path by its on-disk spelling**, including paths from document
  URIs. VS Code on Windows keeps the spelling a folder was opened with, so the server
  resolves each URI on disk (cached per URI) before using it.
- **The markup checks warn on case mismatches.** A `CodeFile=`, `@Register Src=` or URL
  reference that resolves only by ignoring case is a `case-mismatch` warning, which shows
  the reference spelled as on disk. The URL warnings follow the
  `aspxVb.diagnostics.missingFiles` setting. Navigation and compilation still use the file,
  as IIS would.
- `~` in `aspxVb.assemblyFolders` is the OS home folder (`os.homedir()`), not `$HOME`.

## Consequences

- One build of the `.vsix` works on both OSes. The server was already a portable `.dll`
  ([ADR 0007](0007-runtime-via-dotnet-install-tool-and-user-supplied-gac-assemblies.md)).
- Nothing has run on real Windows yet. The unit tests use POSIX paths. The Windows path
  handling was checked by running the extension's resolvers with `path.win32` against a
  simulated case-insensitive file system (drive letters, a UNC share, a folder opened in the
  wrong case), and the server's by a test that opens a document through a URI spelled
  differently from disk.
- The developer's sites start with about 5,900 case warnings. Renaming a few folders on
  disk, or fixing the references, clears most of them.
- On Windows, each lookup lists one folder per path segment, with the name as a filter, to
  learn the real spelling. That costs more on a network share than the old exact check.
