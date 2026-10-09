using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace IsoDoom.Wad;

/// <summary>
/// T1.1b: the optional shareware download (SPEC §5.1, §12). Fetches id
/// Software's own shareware v1.9 archive, <c>doom19s.zip</c>, from the
/// idgames mirrors (<c>idstuff/doom/</c>), takes <c>DOOM1.WAD</c> out of it
/// and keeps it only if its size, MD5 and SHA-256 are the v1.9 file's.
/// <para>
/// The archive is id's DOS installer: <c>DEICE.EXE</c> joins
/// <c>DOOMS_19.1</c> and <c>DOOMS_19.2</c> into <c>DOOMS_19.EXE</c>, a
/// PKZIP self-extractor whose zip (deflate) holds <c>DOOM1.WAD</c>, so
/// <see cref="ZipArchive"/> reads both levels. A zip holding
/// <c>DOOM1.WAD</c> directly is taken too.
/// </para>
/// </summary>
public static class SharewareDownload
{
    public const string FileName = "DOOM1.WAD";

    /// <summary>The idgames mirrors of <c>idstuff/doom/doom19s.zip</c>, tried in turn (all served the same file, 2026-10-09).</summary>
    public static readonly IReadOnlyList<Uri> Mirrors =
    [
        new("https://www.gamers.org/pub/idgames/idstuff/doom/doom19s.zip"),
        new("https://ftp.fu-berlin.de/pc/games/idgames/idstuff/doom/doom19s.zip"),
        new("https://youfailit.net/pub/idgames/idstuff/doom/doom19s.zip"),
        new("https://ftpmirror1.infania.net/pub/idgames/idstuff/doom/doom19s.zip"),
    ];

    /// <summary>The shareware v1.9 <c>DOOM1.WAD</c>.</summary>
    public static readonly ExpectedFile Doom1 = new(
        4196020,
        "f0cefca49926d00903cf57551d901abe",
        "1d7d43be501e67d927e415e0b8f3e29c3bf33075e859721816f652a526cac771");

    /// <summary>The largest download accepted (<c>doom19s.zip</c> is 2,450,688 bytes).</summary>
    public const long MaxDownload = 8 * 1024 * 1024;

    /// <summary>The size and hashes (lower-case hex) a file must have.</summary>
    public sealed record ExpectedFile(long Size, string Md5, string Sha256)
    {
        [SuppressMessage("Security", "CA5351", Justification = "MD5 names the WAD, as the Doom community lists it; SHA-256 is the check against tampering")]
        public bool Matches(byte[] data) =>
            data.LongLength == Size
            && Convert.ToHexStringLower(MD5.HashData(data)) == Md5
            && Convert.ToHexStringLower(SHA256.HashData(data)) == Sha256;
    }

    /// <summary>How far the download is: the mirror, the bytes received and the total, when the server gave one.</summary>
    public readonly record struct Progress(Uri Mirror, long Received, long? Total);

    /// <summary>True if <paramref name="path"/> exists and is <paramref name="expected"/> (default <see cref="Doom1"/>).</summary>
    public static bool IsVerified(string path, ExpectedFile? expected = null)
    {
        expected ??= Doom1;
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == expected.Size && expected.Matches(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Puts a verified <see cref="FileName"/> in <paramref name="directory"/>
    /// and returns its path: the one already there if it verifies, else the
    /// first mirror's that does. Throws <see cref="SharewareDownloadException"/>
    /// naming each mirror's failure when none gives it.
    /// </summary>
    public static async Task<string> DownloadAsync(HttpClient http, string directory, IProgress<Progress>? progress = null,
        IReadOnlyList<Uri>? mirrors = null, ExpectedFile? expected = null, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        expected ??= Doom1;
        string path = Path.Combine(directory, FileName);
        if (IsVerified(path, expected))
            return path;

        var errors = new List<string>();
        foreach (Uri mirror in mirrors ?? Mirrors)
        {
            try
            {
                byte[] zip = await FetchAsync(http, mirror, progress, cancel).ConfigureAwait(false);
                byte[] wad = ExtractWad(new MemoryStream(zip, writable: false), expected);
                Directory.CreateDirectory(directory);
                string part = path + ".part";
                await File.WriteAllBytesAsync(part, wad, cancel).ConfigureAwait(false);
                File.Move(part, path, overwrite: true);
                return path;
            }
            catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or SharewareDownloadException
                || (e is TaskCanceledException && !cancel.IsCancellationRequested)) // a timeout
            {
                errors.Add($"{mirror.Host}: {e.Message}");
            }
        }
        throw new SharewareDownloadException($"Could not download the shareware {FileName}: {string.Join("; ", errors)}");
    }

    private static async Task<byte[]> FetchAsync(HttpClient http, Uri mirror, IProgress<Progress>? progress, CancellationToken cancel)
    {
        using HttpResponseMessage response = await http.GetAsync(mirror, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        if (total > MaxDownload)
            throw new SharewareDownloadException($"{total} bytes, more than the {MaxDownload} expected");
        using Stream body = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        var data = new MemoryStream();
        byte[] buffer = new byte[64 * 1024];
        int read;
        progress?.Report(new Progress(mirror, 0, total));
        while ((read = await body.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
        {
            if (data.Length + read > MaxDownload)
                throw new SharewareDownloadException($"more than the {MaxDownload} bytes expected");
            data.Write(buffer, 0, read);
            progress?.Report(new Progress(mirror, data.Length, total));
        }
        return data.ToArray();
    }

    /// <summary>
    /// Takes <see cref="FileName"/> out of a zip holding it, or holding
    /// <c>DOOMS_19.1</c> and <c>DOOMS_19.2</c> (id's split self-extractor,
    /// joined), and returns it if it is <paramref name="expected"/>.
    /// </summary>
    public static byte[] ExtractWad(Stream zip, ExpectedFile expected) => ExtractWad(zip, expected, nested: false);

    private static byte[] ExtractWad(Stream zip, ExpectedFile expected, bool nested)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (Find(archive, FileName) is { } entry)
        {
            if (entry.Length != expected.Size)
                throw new SharewareDownloadException($"{FileName} is {entry.Length} bytes, not the {expected.Size} of the shareware v1.9 one");
            byte[] wad = Read(entry, expected.Size);
            if (!expected.Matches(wad))
                throw new SharewareDownloadException($"{FileName} is not the shareware v1.9 one (its hash differs)");
            return wad;
        }
        if (!nested && Find(archive, "DOOMS_19.1") is { } first && Find(archive, "DOOMS_19.2") is { } second)
        {
            if (first.Length + second.Length > MaxDownload)
                throw new SharewareDownloadException("the installer is larger than expected");
            var joined = new MemoryStream();
            joined.Write(Read(first, first.Length));
            joined.Write(Read(second, second.Length));
            joined.Position = 0;
            return ExtractWad(joined, expected, nested: true);
        }
        throw new SharewareDownloadException($"no {FileName} in the archive");
    }

    private static ZipArchiveEntry? Find(ZipArchive archive, string name)
    {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }

    /// <summary>Reads an entry of <paramref name="length"/> bytes (its stated length, already bounded), failing if it holds another amount.</summary>
    private static byte[] Read(ZipArchiveEntry entry, long length)
    {
        byte[] data = new byte[length];
        using Stream stream = entry.Open();
        stream.ReadExactly(data);
        if (stream.ReadByte() >= 0)
            throw new InvalidDataException($"{entry.FullName} is longer than its stated {length} bytes");
        return data;
    }
}

/// <summary>The shareware download failed (T1.1b).</summary>
public sealed class SharewareDownloadException : Exception
{
    public SharewareDownloadException()
    {
    }

    public SharewareDownloadException(string message) : base(message)
    {
    }

    public SharewareDownloadException(string message, Exception inner) : base(message, inner)
    {
    }
}
