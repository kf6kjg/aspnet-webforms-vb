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

    /// <summary>
    /// <paramref name="path"/> as it exists on disk, matching each segment case-insensitively
    /// when the exact name is missing (the developer's references come from Windows).
    /// </summary>
    public static string? OnDisk(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full) || Directory.Exists(full)) return full;
        var current = "/";
        foreach (var part in full.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var exact = Path.Combine(current, part);
            if (File.Exists(exact) || Directory.Exists(exact)) { current = exact; continue; }
            if (!Directory.Exists(current)) return null;
            var match = Directory.EnumerateFileSystemEntries(current).FirstOrDefault(e => Path.GetFileName(e).Equals(part, StringComparison.OrdinalIgnoreCase));
            if (match == null) return null;
            current = match;
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
