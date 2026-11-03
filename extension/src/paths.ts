// Site-relative path resolution for a Web Site project. Windows paths are case-
// insensitive and the developer's references don't always match on-disk casing, so
// lookups fall back to a case-insensitive directory scan. File access goes through an
// injected interface so this runs both in the extension host and under plain node.

import * as path from "node:path";

export interface DirReader {
  /** Entry names in `dir`, or undefined if it doesn't exist. */
  readDir(dir: string): Promise<string[] | undefined>;
}

/** `absPath` as it exists on disk, matching each segment case-insensitively if needed. */
export async function resolveOnDisk(fs: DirReader, absPath: string): Promise<string | undefined> {
  const parts = path.resolve(absPath).split("/").filter((p) => p.length > 0);
  let current = "/";
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

const codeBehindPattern = /\.(aspx|ascx|master)\.vb$/i;

export function isCodeBehind(file: string): boolean {
  return codeBehindPattern.test(file);
}

/** Buy.aspx.vb → Buy.aspx (the developer's convention: code-behind is markup + ".vb"). */
export function markupForCodeBehind(file: string): string | undefined {
  return isCodeBehind(file) ? file.slice(0, -3) : undefined;
}
