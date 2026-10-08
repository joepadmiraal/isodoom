using System.Linq;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Wad;

/// <summary>T1.2a: d_main.c's modified-game checks (PWADs with the shareware or a fake registered IWAD).</summary>
public class ModifiedGameTests
{
    private static readonly string[] _registeredLumps =
        [.. ModifiedGame.name.Select(n => n.ToUpperInvariant()).Distinct()];

    private static WadFile Iwad(params string[] lumps) =>
        new WadBuilder(WadType.Iwad).Lump("PLAYPAL", 0).Markers(lumps).ToWadFile("iwad.wad");

    private static WadFile Pwad(params string[] lumps) => new WadBuilder().Markers(lumps).ToWadFile("mod.wad");

    private static void Check(params WadFile[] files)
    {
        var archive = new WadArchive(files);
        ModifiedGame.D_CheckModifiedGame(archive, IwadIdentification.D_IdentifyVersion(archive));
    }

    private static string[] Registered(params string[] except) =>
        ["E1M1", .. _registeredLumps.Where(n => !except.Contains(n))];

    [Fact]
    public void SharewareWithoutPwadsIsFine() => Check(Iwad("E1M1"));

    [Fact]
    public void SharewareRefusesPwads()
    {
        ModifiedGameException e = Assert.Throws<ModifiedGameException>(() => Check(Iwad("E1M1"), Pwad("E1M2")));
        Assert.Equal(ModifiedGame.SharewareMessage, e.Message);
    }

    [Fact]
    public void SharewareRefusesEvenAnEmptyPwad()
    {
        Assert.Throws<ModifiedGameException>(() => Check(Iwad("E1M1"), Pwad()));
    }

    [Fact]
    public void RegisteredWithAllLumpsAcceptsPwads() => Check(Iwad(Registered()), Pwad("E1M2"));

    [Theory]
    [InlineData("E2M5")]
    [InlineData("E3M9")]
    [InlineData("DPHOOF")]
    [InlineData("SPIDA1D1")]
    public void FakeRegisteredRefusesPwads(string missing)
    {
        ModifiedGameException e = Assert.Throws<ModifiedGameException>(() => Check(Iwad(Registered(missing)), Pwad("E1M2")));
        Assert.Equal(ModifiedGame.NotRegisteredMessage, e.Message);
    }

    [Fact]
    public void FakeRegisteredWithoutPwadsIsNotChecked() => Check(Iwad("E1M1", "E3M1"));

    [Fact]
    public void VanillaNeverChecksE3M2() => Check(Iwad(Registered("E3M2")), Pwad("E1M2"));

    [Fact]
    public void PwadCanSupplyMissingRegisteredLumps()
    {
        // Vanilla looks the lumps up after all files are added.
        Check(Iwad(Registered("CYBRA1")), Pwad("CYBRA1"));
    }

    [Theory]
    [InlineData("E1M1", "E3M1", "E4M1")] // retail
    [InlineData("MAP01")] // commercial
    public void OtherModesAcceptPwads(params string[] lumps) => Check(Iwad(lumps), Pwad("MAP02"));

    [Theory]
    [InlineData("FREEDOOM")]
    [InlineData("FREEDOOM", "FREEDM")]
    public void FreedoomSkipsTheChecks(params string[] variantLumps)
    {
        // A Freedoom IWAD that would identify as shareware or as a fake registered IWAD.
        Check(Iwad(["E1M1", .. variantLumps]), Pwad("E1M2"));
        Check(Iwad(["E1M1", "E3M1", .. variantLumps]), Pwad("E1M2"));
    }

    [Fact]
    public void BfgEditionIsChecked()
    {
        Assert.Throws<ModifiedGameException>(() => Check(Iwad("E1M1", "DMENUPIC"), Pwad("E1M2")));
    }

    [Fact]
    public void RealSharewareRefusesPwads()
    {
        var doom1 = WadFile.Open(TestWads.RequireDoom1());
        Check(doom1);
        Assert.Throws<ModifiedGameException>(() => Check(doom1, Pwad("E1M1")));
    }

    [Fact]
    public void RealDoom2AcceptsPwads() => Check(WadFile.Open(TestWads.RequireDoom2()), Pwad("MAP01"));
}
