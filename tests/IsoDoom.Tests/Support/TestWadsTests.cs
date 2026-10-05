using System;
using System.IO;
using System.Text;
using Xunit;

namespace IsoDoom.Tests.Support;

public class TestWadsTests
{
    [Fact]
    public void RepoRootIsFound()
    {
        Assert.NotNull(TestWads.RepoRoot);
        Assert.True(File.Exists(Path.Combine(TestWads.RepoRoot!, "project.godot")));
    }

    [Fact]
    public void Doom1IsTheSharewareIwad()
    {
        string path = TestWads.RequireDoom1();

        // Header (w_wad.c: wadinfo_t): "IWAD", numlumps, infotableofs; little-endian.
        byte[] header = new byte[12];
        using (FileStream stream = File.OpenRead(path))
            stream.ReadExactly(header);
        Assert.Equal("IWAD", Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal(1264, BitConverter.ToInt32(header, 4));
    }
}
