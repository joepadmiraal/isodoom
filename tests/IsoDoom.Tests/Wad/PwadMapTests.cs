using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>P12a: <see cref="WadArchive.IsPwadMap"/> decides by the file of the map's header lump.</summary>
public class PwadMapTests
{
    private static WadFile Iwad() => new WadBuilder(WadType.Iwad).Markers("MAP01", "MAP02", "MAP03").ToWadFile("iwad.wad");

    private static WadFile Pwad(params string[] maps) => new WadBuilder().Markers(maps).ToWadFile("mod.wad");

    [Fact]
    public void IwadAloneHasNoPwadMaps()
    {
        var archive = new WadArchive([Iwad()]);
        Assert.False(archive.IsPwadMap("MAP01"));
        Assert.False(archive.IsPwadMap("MAP04"));
    }

    [Fact]
    public void OnlyTheMapsAPwadReplacesOrAddsAreItsOwn()
    {
        var archive = new WadArchive([Iwad(), Pwad("MAP01", "MAP33")]);
        Assert.True(archive.IsPwadMap("MAP01"));
        Assert.True(archive.IsPwadMap("map33"));
        Assert.False(archive.IsPwadMap("MAP02"));
        Assert.False(archive.IsPwadMap("MAP03"));
        Assert.False(archive.IsPwadMap("MAP04"));
    }
}
