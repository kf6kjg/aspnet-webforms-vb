using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace AspxVb.Server.Site;

/// <summary>Reads the developer's files: UTF-8 when valid, otherwise Windows-1252 (files saved by older Visual Studio).</summary>
public static class FileText
{
    static FileText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return StrictUtf8.GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(bytes); }
    }

    public static SourceText ReadSource(string path) => SourceText.From(Read(path), Encoding.UTF8);

    // Windows and macOS file systems ignore case, so an existing path may still be spelled differently from disk.
    private static readonly bool CaseInsensitiveFileSystem = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private static readonly EnumerationOptions IgnoringCase = new() { MatchCasing = MatchCasing.CaseInsensitive, MatchType = MatchType.Simple, AttributesToSkip = 0 };

    /// <summary>
    /// <paramref name="path"/> spelled as it exists on disk, matching each segment
    /// case-insensitively (the developer's references come from Windows). Every path the
    /// server keys on goes through this, so they agree on every OS (ADR 0012).
    /// </summary>
    public static string? OnDisk(string path)
    {
        var full = Path.GetFullPath(path);
        if (!CaseInsensitiveFileSystem && (File.Exists(full) || Directory.Exists(full))) return full;
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current)) return null;
            // The pattern only narrows the listing (a name may hold * or ?); the exact spelling wins when both exist.
            var names = Directory.EnumerateFileSystemEntries(current, part, IgnoringCase).Select(Path.GetFileName).ToList();
            var match = names.FirstOrDefault(n => n == part) ?? names.FirstOrDefault(n => part.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (match == null) return null;
            current = Path.Combine(current, match);
        }
        return current;
    }

    /// <summary>`~/x` and `/x` are site-rooted; anything else is relative to the referencing file.</summary>
    public static string? Resolve(string reference, string fromFile, string siteRoot)
    {
        var r = reference.Replace('\\', '/');
        var target = r.StartsWith("~/") ? Path.Combine(siteRoot, r[2..])
            : r.StartsWith('/') ? Path.Combine(siteRoot, r[1..])
            : Path.Combine(Path.GetDirectoryName(fromFile)!, r);
        return OnDisk(target);
    }
}
