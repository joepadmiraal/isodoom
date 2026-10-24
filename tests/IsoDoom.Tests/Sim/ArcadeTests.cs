using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T8.12: the arcade mode's sim tweaks (SPEC §6.3 #7):
/// <see cref="Tweaks.AutoDoors"/> on the synthetic specials map's door (E1M2:
/// room R x 0..128, the door sector x 128..160, line 76 a DR manual door
/// facing R; see <see cref="DoorTests"/>) and <see cref="Tweaks.InfiniteAmmo"/>.
/// </summary>
public class ArcadeTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static World Specials(Tweaks tweaks)
    {
        WadArchive wad = Wad();
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), tweaks) { textures = Textures.R_InitTextures(wad) };
        world.P_InitPicAnims(world.textures, PicAnims.FlatNames(wad)); // as SaveGameTests.Load, so a save loads
        world.G_DoLoadLevel(Level.Load(wad, "E1M2"));
        world.PlaceMobj(world.players[0].mo!, F(100), F(196), 0); // in room R facing the door (east)
        return world;
    }

    private static WadArchive Wad() => new([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);

    private static sector_t DoorSec(World world) => world.sectors[SyntheticIwad.DoorSector];

    /// <summary>Runs <paramref name="tics"/> tics walking east (forward) or standing; returns the sounds started.</summary>
    private static sfxenum_t[] Walk(World world, int tics, bool forward = true)
    {
        var sounds = new System.Collections.Generic.List<sfxenum_t>();
        for (int i = 0; i < tics; i++)
        {
            world.G_Ticker(new ticcmd_t { forwardmove = (sbyte)(forward ? 25 : 0) });
            sounds.AddRange(world.StartedSounds().Select(s => s.sfx));
        }
        return [.. sounds];
    }

    [Fact]
    public void WalkingIntoADoorOpensItOnlyWithTheTweak()
    {
        World vanilla = Specials(Tweaks.Vanilla);
        Walk(vanilla, 40);
        Assert.Null(DoorSec(vanilla).specialdata);
        Assert.Equal(0, DoorSec(vanilla).ceilingheight);

        World arcade = Specials(Tweaks.Vanilla.WithArcade());
        sfxenum_t[] sounds = Walk(arcade, 40);
        vldoor_t door = Assert.IsType<vldoor_t>(DoorSec(arcade).specialdata);
        Assert.Equal(1, door.direction); // still opening: pushing on did not close it again
        Assert.Equal(1, sounds.Count(s => s == sfxenum_t.sfx_doropn));
        Assert.True(DoorSec(arcade).ceilingheight > 0);

        // It opens, the player walks through, the door waits and closes behind it.
        Walk(arcade, 60);
        Assert.True(arcade.players[0].mo!.x > F(160));
    }

    [Fact]
    public void PushingAClosingDoorReopensIt()
    {
        World world = Specials(Tweaks.Vanilla.WithArcade());
        world.P_UseLines(world.players[0]);
        vldoor_t door = Assert.IsType<vldoor_t>(DoorSec(world).specialdata);
        Walk(world, 63 + VDoor.VDOORWAIT + 40, forward: false);
        Assert.Equal(-1, door.direction);
        Assert.True(DoorSec(world).ceilingheight < F(56)); // too low to walk under

        Walk(world, 10);
        Assert.Equal(1, door.direction);
    }

    [Fact]
    public void ALockedDoorRefusesOncePerPushAndOpensWithItsKey()
    {
        World world = Specials(Tweaks.Vanilla.WithArcade());
        world.lines[SyntheticIwad.DoorLineR].special = 26; // DR blue
        sfxenum_t[] sounds = Walk(world, 30);
        Assert.Null(DoorSec(world).specialdata);
        Assert.Equal(1, sounds.Count(s => s == sfxenum_t.sfx_oof));

        // Let go for a tic, push again: refused again, once.
        Walk(world, 1, forward: false);
        sounds = Walk(world, 30);
        Assert.Equal(1, sounds.Count(s => s == sfxenum_t.sfx_oof));

        world.players[0].cards[(int)card_t.it_bluecard] = true;
        Walk(world, 2);
        Assert.IsType<vldoor_t>(DoorSec(world).specialdata);
    }

    [Fact]
    public void ASaveKeepsTheTweaksAndTheRefusalsPush()
    {
        World world = Specials(Tweaks.Vanilla.WithArcade());
        world.lines[SyntheticIwad.DoorLineR].special = 26;
        Walk(world, 30);
        int tic = world.players[0].autodoortic;
        Assert.Equal(world.leveltime, tic); // pushed last tic

        World loaded = SaveGameTests.Load(SaveGameTests.Save(world), Wad());
        Assert.Equal(Tweaks.Vanilla.WithArcade(), loaded.tweaks);
        Assert.Equal(tic, loaded.players[0].autodoortic);
        Assert.DoesNotContain(sfxenum_t.sfx_oof, Walk(loaded, 5)); // still the same push
    }

    [Theory]
    [InlineData(false, 50)]
    [InlineData(true, 50)]
    public void FiringUsesNoAmmoWithTheTweak(bool arcade, int start)
    {
        var wad = new WadArchive([WadFile.FromBytes(TestMap.Strip(0, 0, 256, new TestMap.Room(512, 0, 128)).Player(64, 128).Build(), "testmap.wad")]);
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), arcade ? Tweaks.TopDown.WithArcade() : Tweaks.TopDown);
        world.G_DoLoadLevel(Level.Load(wad, "E1M1"));
        player_t p = world.players[0];
        Assert.Equal(start, p.ammo[(int)ammotype_t.am_clip]);
        for (int i = 0; i < 105; i++)
            world.G_Ticker(new ticcmd_t { buttons = buttoncode_t.BT_ATTACK });
        if (arcade)
            Assert.Equal(start, p.ammo[(int)ammotype_t.am_clip]);
        else
            Assert.True(p.ammo[(int)ammotype_t.am_clip] < start - 5);
    }
}
