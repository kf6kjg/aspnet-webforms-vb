// Site-relative path resolution for a Web Site project. Windows paths are case-
// insensitive and the developer's references don't always match on-disk casing, so
// lookups match each name case-insensitively and return the on-disk spelling, on every
// OS (ADR 0012). File access goes through an injected interface so this runs both in
// the extension host and under plain node.

import * as path from "node:path";

export interface DirReader {
  /** Entry names in `dir`, or undefined if it doesn't exist. */
  readDir(dir: string): Promise<string[] | undefined>;
}

/** `absPath` as it exists on disk, matching each segment case-insensitively if needed. */
export async function resolveOnDisk(fs: DirReader, absPath: string): Promise<string | undefined> {
  const full = path.resolve(absPath);
  const root = path.parse(full).root;
  const parts = full.slice(root.length).split(/[\\/]/).filter((p) => p.length > 0);
  let current = root;
  for (const part of parts) {
    const entries = await fs.readDir(current);
    if (!entries) return undefined;
    const match = entries.find((e) => e === part) ?? entries.find((e) => e.toLowerCase() === part.toLowerCase());
    if (!match) return undefined;
    current = path.join(current, match);
  }
  return current;
}

/** Nearest folder at or above `fromDir` (not above `stopAt`) that holds a web.config. */
export async function findSiteRoot(fs: DirReader, fromDir: string, stopAt?: string): Promise<string | undefined> {
  let dir = path.resolve(fromDir);
  for (;;) {
    const entries = await fs.readDir(dir);
    if (entries?.some((e) => e.toLowerCase() === "web.config")) return dir;
    const parent = path.dirname(dir);
    if (parent === dir || (stopAt !== undefined && dir === path.resolve(stopAt))) return undefined;
    dir = parent;
  }
}

/** `~/x` and `/x` are site-rooted; anything else is relative to the referencing file. */
export function resolveReference(ref: string, fileDir: string, siteRoot: string | undefined): string {
  const r = ref.replace(/\\/g, "/");
  if (r.startsWith("~/")) return path.join(siteRoot ?? fileDir, r.slice(2));
  if (r.startsWith("/")) return path.join(siteRoot ?? fileDir, r.slice(1));
  return path.join(fileDir, r);
}

/** `ref` with each name spelled as in `found`, the path it resolved to (for case warnings). */
export function respelled(ref: string, found: string): string {
  const names = found.split(/[\\/]/);
  const parts = ref.split(/([\\/])/);
  for (let i = parts.length - 1, j = names.length - 1; i >= 0 && j >= 0; i--) {
    const p = parts[i];
    if (p === "..") break;
    if (p === "" || p === "/" || p === "\\" || p === "." || p === "~") continue;
    parts[i] = names[j--];
  }
  return parts.join("");
}

const codeBehindPattern = /\.(aspx|ascx|master)\.vb$/i;

export function isCodeBehind(file: string): boolean {
  return codeBehindPattern.test(file);
}

/** Buy.aspx.vb → Buy.aspx (the developer's convention: code-behind is markup + ".vb"). */
export function markupForCodeBehind(file: string): string | undefined {
  return isCodeBehind(file) ? file.slice(0, -3) : undefined;
}
