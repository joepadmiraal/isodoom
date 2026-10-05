using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>T1.2: IWAD identification, on DOOM1.WAD and on synthetic IWADs.</summary>
public class IwadIdentificationTests
{
    private static WadBuilder Iwad(params string[] lumps) =>
        new WadBuilder(WadType.Iwad).Lump("PLAYPAL", 0).Markers(lumps);

    [Fact]
    public void Doom1IsShareware()
    {
        IwadInfo info = IwadIdentification.D_IdentifyVersion(WadArchive.Open(TestWads.RequireDoom1()));
        Assert.Equal(GameMode.shareware, info.GameMode);
        Assert.Equal(GameMission.doom, info.GameMission);
        Assert.Equal("DOOM Shareware", info.Description);
    }

    [Theory]
    [InlineData(GameMode.shareware, GameMission.doom, "E1M1", "E1M9")]
    [InlineData(GameMode.registered, GameMission.doom, "E1M1", "E2M1", "E3M1", "E3M9")]
    [InlineData(GameMode.retail, GameMission.doom, "E1M1", "E2M1", "E3M1", "E4M1", "E4M9")]
    [InlineData(GameMode.commercial, GameMission.doom2, "MAP01", "MAP32")]
    [InlineData(GameMode.commercial, GameMission.pack_tnt, "MAP01", "MAP32", "P_START", "REDTNT2", "P_END")]
    [InlineData(GameMode.commercial, GameMission.pack_plut, "MAP01", "MAP32", "P_START", "CAMO1", "P_END")]
    public void IdentifiesFromLumps(GameMode mode, GameMission mission, params string[] lumps)
    {
        IwadInfo info = IwadIdentification.D_IdentifyVersion(Iwad(lumps).ToWadFile());
        Assert.Equal(new IwadInfo(mode, mission), info);
    }

    [Fact]
    public void DescriptionsAreDistinct()
    {
        Assert.Equal("DOOM Registered", new IwadInfo(GameMode.registered, GameMission.doom).Description);
        Assert.Equal("The Ultimate DOOM", new IwadInfo(GameMode.retail, GameMission.doom).Description);
        Assert.Equal("DOOM 2: Hell on Earth", new IwadInfo(GameMode.commercial, GameMission.doom2).Description);
        Assert.Equal("Final DOOM: TNT - Evilution", new IwadInfo(GameMode.commercial, GameMission.pack_tnt).Description);
        Assert.Equal("Final DOOM: The Plutonia Experiment", new IwadInfo(GameMode.commercial, GameMission.pack_plut).Description);
    }

    [Fact]
    public void FirstMapMarkerPicksTheMission()
    {
        // Chocolate Doom: whichever of MAP01 and E1M1 comes first in the directory wins.
        Assert.Equal(GameMission.doom2, IwadIdentification.D_IdentifyVersion(Iwad("MAP01", "E1M1").ToWadFile()).GameMission);
        Assert.Equal(GameMission.doom, IwadIdentification.D_IdentifyVersion(Iwad("E1M1", "MAP01").ToWadFile()).GameMission);
    }

    [Fact]
    public void NamesAreCaseInsensitive()
    {
        IwadInfo info = IwadIdentification.D_IdentifyVersion(Iwad("e1m1", "e3m1").ToWadFile());
        Assert.Equal(GameMode.registered, info.GameMode);
    }

    [Fact]
    public void PwadsDoNotChangeTheGameMode()
    {
        // A PWAD adding E4M1 or MAP01 leaves shareware as shareware.
        WadArchive archive = new(new[]
        {
            Iwad("E1M1").ToWadFile("doom1.wad"),
            new WadBuilder().Markers("MAP01", "E3M1", "E4M1").ToWadFile("mod.wad"),
        });
        Assert.Equal(new IwadInfo(GameMode.shareware, GameMission.doom), IwadIdentification.D_IdentifyVersion(archive));
    }

    [Fact]
    public void RejectsPwadAsIwad()
    {
        WadArchive archive = new(new[] { new WadBuilder(WadType.Pwad).Markers("E1M1").ToWadFile() });
        Assert.Throws<WadFormatException>(() => IwadIdentification.D_IdentifyVersion(archive));
    }

    [Fact]
    public void RejectsIwadWithoutLevels()
    {
        Assert.Throws<WadFormatException>(() => IwadIdentification.D_IdentifyVersion(Iwad("E2M1", "MAP02").ToWadFile()));
    }
}
