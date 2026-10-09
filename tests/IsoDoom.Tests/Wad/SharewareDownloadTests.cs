using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>T1.1b: the shareware download's unpacking, verification and mirror fallback, offline (a faked HTTP handler).</summary>
public sealed class SharewareDownloadTests : IDisposable
{
    private static readonly byte[] _wad = MakeWad();
    private static readonly SharewareDownload.ExpectedFile _expected = Expect(_wad);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "isodoom-shareware-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static byte[] MakeWad()
    {
        byte[] wad = new byte[100_000];
        new Random(19).NextBytes(wad);
        return wad;
    }

    [SuppressMessage("Security", "CA5351", Justification = "The expected file's MD5, as SharewareDownload checks it")]
    private static SharewareDownload.ExpectedFile Expect(byte[] data) =>
        new(data.LongLength, Convert.ToHexStringLower(MD5.HashData(data)), Convert.ToHexStringLower(SHA256.HashData(data)));

    /// <summary>A zip of <paramref name="entries"/>, after <paramref name="stub"/> bytes (a self-extractor's offsets count from the file's start, as ZipArchive writes them).</summary>
    private static byte[] Zip(byte[] stub, params (string Name, byte[] Data)[] entries)
    {
        var stream = new MemoryStream();
        stream.Write(stub);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] data) in entries)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(data);
            }
        }
        return stream.ToArray();
    }

    /// <summary>As id's <c>doom19s.zip</c>: an installer, a stub then a zip holding the WAD, cut in two as <c>DOOMS_19.1</c> and <c>DOOMS_19.2</c>.</summary>
    private static byte[] Installer(byte[] wad)
    {
        byte[] exe = Zip("MZ stub of a self-extractor"u8.ToArray(), ("README.TXT", "hi"u8.ToArray()), ("DOOM1.WAD", wad));
        int cut = exe.Length / 2;
        return Zip([], ("INSTALL.BAT", "DEICE.EXE"u8.ToArray()), ("DOOMS_19.1", exe[..cut]), ("DOOMS_19.2", exe[cut..]));
    }

    [Fact]
    public void ExtractWad_TakesTheWadOutOfTheSplitInstaller() =>
        Assert.Equal(_wad, SharewareDownload.ExtractWad(new MemoryStream(Installer(_wad)), _expected));

    [Fact]
    public void ExtractWad_TakesAWadZippedDirectly() =>
        Assert.Equal(_wad, SharewareDownload.ExtractWad(new MemoryStream(Zip([], ("doom1.wad", _wad))), _expected));

    [Fact]
    public void ExtractWad_RefusesAnotherWad()
    {
        byte[] other = (byte[])_wad.Clone();
        other[500] ^= 1;
        var e = Assert.Throws<SharewareDownloadException>(() => SharewareDownload.ExtractWad(new MemoryStream(Installer(other)), _expected));
        Assert.Contains("hash", e.Message, StringComparison.Ordinal);
        Assert.Throws<SharewareDownloadException>(() => SharewareDownload.ExtractWad(new MemoryStream(Zip([], ("DOOM1.WAD", _wad[1..]))), _expected));
    }

    [Fact]
    public void ExtractWad_RefusesAnArchiveWithoutIt()
    {
        Assert.Throws<SharewareDownloadException>(() => SharewareDownload.ExtractWad(new MemoryStream(Zip([], ("DOOM2.WAD", _wad))), _expected));
        // An installer inside an installer is not looked into.
        byte[] nested = Zip([], ("DOOMS_19.1", Installer(_wad)), ("DOOMS_19.2", []));
        Assert.Throws<SharewareDownloadException>(() => SharewareDownload.ExtractWad(new MemoryStream(nested), _expected));
        Assert.Throws<InvalidDataException>(() => SharewareDownload.ExtractWad(new MemoryStream("not a zip"u8.ToArray()), _expected));
    }

    [Fact]
    public async Task DownloadAsync_FallsBackThroughTheMirrors()
    {
        var handler = new FakeHandler(new()
        {
            ["a.example"] = () => new HttpResponseMessage(HttpStatusCode.NotFound),
            ["b.example"] = () => Ok(Installer(_wad[..^1])),
            ["c.example"] = () => Ok(Installer(_wad)),
            ["d.example"] = () => throw new InvalidOperationException("not reached"),
        });
        var reports = new List<SharewareDownload.Progress>();
        string path = await SharewareDownload.DownloadAsync(new HttpClient(handler), _dir, new SyncProgress(reports.Add),
            Mirrors("a", "b", "c", "d"), _expected, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(_dir, "DOOM1.WAD"), path);
        Assert.Equal(_wad, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Equal(["a.example", "b.example", "c.example"], handler.Requested);
        SharewareDownload.Progress last = reports[^1];
        Assert.Equal("c.example", last.Mirror.Host);
        Assert.Equal(last.Total, last.Received);
    }

    [Fact]
    public async Task DownloadAsync_NamesEveryMirrorsFailure()
    {
        var handler = new FakeHandler(new()
        {
            ["a.example"] = () => throw new HttpRequestException("no route to host"),
            ["b.example"] = () => Ok(Zip([], ("README.TXT", []))),
            ["c.example"] = () => Ok(new byte[SharewareDownload.MaxDownload + 1]),
        });
        var e = await Assert.ThrowsAsync<SharewareDownloadException>(() => SharewareDownload.DownloadAsync(new HttpClient(handler), _dir,
            cancel: TestContext.Current.CancellationToken, mirrors: Mirrors("a", "b", "c"), expected: _expected));
        Assert.Contains("a.example: no route to host", e.Message, StringComparison.Ordinal);
        Assert.Contains("b.example: no DOOM1.WAD", e.Message, StringComparison.Ordinal);
        Assert.Contains("c.example:", e.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_dir, "DOOM1.WAD")));
    }

    [Fact]
    public async Task DownloadAsync_KeepsAVerifiedCopyAndReplacesAnotherFile()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "DOOM1.WAD");
        File.WriteAllBytes(path, _wad);
        var handler = new FakeHandler(new() { ["a.example"] = () => Ok(Installer(_wad)) });
        CancellationToken cancel = TestContext.Current.CancellationToken;

        Assert.Equal(path, await SharewareDownload.DownloadAsync(new HttpClient(handler), _dir, cancel: cancel, mirrors: Mirrors("a"), expected: _expected));
        Assert.Empty(handler.Requested);

        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.False(SharewareDownload.IsVerified(path, _expected));
        await SharewareDownload.DownloadAsync(new HttpClient(handler), _dir, cancel: cancel, mirrors: Mirrors("a"), expected: _expected);
        Assert.Equal(["a.example"], handler.Requested);
        Assert.True(SharewareDownload.IsVerified(path, _expected));
    }

    [Fact]
    public void Doom1_IsTheShareware19Wad()
    {
        string path = TestWads.RequireDoom1();
        Assert.True(SharewareDownload.IsVerified(path));
        byte[] wad = File.ReadAllBytes(path);
        Assert.Equal(wad, SharewareDownload.ExtractWad(new MemoryStream(Installer(wad)), SharewareDownload.Doom1));
    }

    [Fact]
    public void Mirrors_AreHttpsIdgamesCopies()
    {
        Assert.NotEmpty(SharewareDownload.Mirrors);
        foreach (Uri mirror in SharewareDownload.Mirrors)
        {
            Assert.Equal("https", mirror.Scheme);
            Assert.EndsWith("/idstuff/doom/doom19s.zip", mirror.AbsolutePath, StringComparison.Ordinal);
        }
    }

    private static Uri[] Mirrors(params string[] hosts) => Array.ConvertAll(hosts, h => new Uri($"https://{h}.example/doom19s.zip"));

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private sealed class FakeHandler(Dictionary<string, Func<HttpResponseMessage>> responses) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!.Host);
            return Task.FromResult(responses[request.RequestUri.Host]());
        }
    }

    /// <summary>Reports at once (<see cref="Progress{T}"/> posts to the thread pool).</summary>
    private sealed class SyncProgress(Action<SharewareDownload.Progress> report) : IProgress<SharewareDownload.Progress>
    {
        public void Report(SharewareDownload.Progress value) => report(value);
    }
}
