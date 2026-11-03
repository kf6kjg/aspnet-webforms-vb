# 0001. Hardcode one developer's conventions; Web Site model only

Date: 2026-11-02 · Status: Accepted

## Context

The target is one developer's legacy ASP.NET Web Forms code bases in VB.NET. They use
the "Web Site" project model: a `.sln` that points at a folder, with no `.vbproj` and
no `.designer.vb` files. Pages, controls, and master pages link to their code-behind
through `CodeFile=` and `Inherits=` in the `@Page` / `@Control` / `@Master` directive.
Control fields such as `txtName` aren't declared anywhere on disk, because ASP.NET
generates them at runtime.

A general Web Forms tool would have to support Web Application projects, C#, designer
files, `CodeBehind=`, every `web.config` variation, and more. None of that helps this
developer.

## Decision

Support only what these code bases use. Hardcode their conventions (Web Site model, VB,
`CodeFile=`, the standard .NET 4.x defaults) instead of making them configurable or
detecting them. When a real code base shows a convention the tool doesn't handle, add
that one case.

## Consequences

- The code stays small, and each stage can be built and used on its own.
- Other projects will probably not work, and that's acceptable. The README says so.
- What these code bases actually use is not yet measured. A scanner to find out which
  syntax they really use, so we can support that minimum, is a planned idea (see README).
