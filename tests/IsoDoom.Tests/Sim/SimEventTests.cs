using System.Collections.Generic;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T6.10: the sim's output-only event queue (<see cref="World.events"/>):
/// sounds started and stopped, messages; the checksum does not depend on
/// whether anything drains it. The sequence of <c>S_StartSound</c> calls is
/// compared with vanilla's in every route (<see cref="VanillaRoute"/>'s <c>sounds</c> column).
/// </summary>
public class SimEventTests
{
    private const int FRACUNIT = 1 << 16;

    private static int F(int units) => units * FRACUNIT;

    private static uint Deg(int degrees) => (uint)((ulong)degrees * 0x100000000UL / 360);

    private static World Specials(Tweaks? tweaks = null)
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), tweaks ?? Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M2"));
        return world;
    }

    private static mobj_t Player(World world) => world.players[0].mo!;

    [Theory]
    [InlineData("synthetic-monsters")]
    [InlineData("synthetic-keys")]
    [InlineData("synthetic-doors")]
    [InlineData("testmap-weapons")]
    public void TheChecksumIsTheSameWhetherAnythingListens(string name)
    {
        VanillaRoute route = VanillaRoute.Load(name);
        World listened = route.NewWorld(), ignored = route.NewWorld();
        var drained = new List<sim_event_t>();
        for (int tic = 0; tic < route.Cmds.Count; tic++)
        {
            route.File.RunEvents(listened, tic);
            route.File.RunEvents(ignored, tic);
            listened.G_Ticker(route.Cmds[tic]);
            ignored.G_Ticker(route.Cmds[tic]);
            Assert.Equal(ignored.Checksum(), listened.Checksum());

            // The queue nobody drains holds this tic's events only, the same as the drained one's.
            Assert.All(ignored.events, e => Assert.Equal(ignored.leveltime - 1, e.tic));
            int start = drained.Count;
            Assert.Equal(ignored.events.Count, listened.DrainEvents(drained));
            Assert.Empty(listened.events);
            Assert.Equal(ignored.events.Select(e => (e.type, e.tic, e.sound.sfx, e.x, e.y, e.player, e.message)),
                drained.Skip(start).Select(e => (e.type, e.tic, e.sound.sfx, e.x, e.y, e.player, e.message)));
        }
        Assert.Contains(drained, e => e.type == simevent_t.se_startsound);
    }

    [Fact]
    public void APickupQueuesItsSoundAndMessage()
    {
        VanillaRoute route = VanillaRoute.Load("synthetic-keys");
        World world = route.NewWorld();
        var drained = new List<sim_event_t>();
        for (int tic = 0; tic < route.Cmds.Count; tic++)
        {
            route.File.RunEvents(world, tic);
            world.G_Ticker(route.Cmds[tic]);
            Assert.Null(world.players[0].message); // taken after the tic (HU_Ticker)
            world.DrainEvents(drained);
        }
        sim_event_t message = drained.First(e => e.type == simevent_t.se_message);
        Assert.Equal(0, message.player);
        // In the same tic: the pickup's stop (P_RemoveMobj), its sound, then the message after the tic.
        Assert.Equal(new[] { simevent_t.se_stopsound, simevent_t.se_startsound, simevent_t.se_message },
            drained.Where(e => e.tic == message.tic).Select(e => e.type).TakeLast(3));
        Assert.Contains(drained, e => e.tic == message.tic && e.sound.sfx == sfxenum_t.sfx_itemup && e.sound.origin is null);
    }

    [Fact]
    public void EventsCarryTheOriginsPositionWhenTheySounded()
    {
        World world = Specials();
        mobj_t mo = world.P_SpawnMobj(F(40), F(-64), 0, mobjtype_t.MT_BARREL);
        world.events.Clear();
        world.S_StartSound(mo, sfxenum_t.sfx_barexp);
        world.P_RemoveMobj(mo);
        sector_t sec = world.sectors[3];
        world.S_StartSound(sec, sfxenum_t.sfx_doropn);
        world.S_StartSound((mobj_t?)null, sfxenum_t.sfx_itemup);
        Assert.Equal(new[]
        {
            new sim_event_t(simevent_t.se_startsound, 0, new sound_event_t(sfxenum_t.sfx_barexp, mo, null), F(40), F(-64), -1, null),
            new sim_event_t(simevent_t.se_stopsound, 0, new sound_event_t(sfxenum_t.sfx_None, mo, null), F(40), F(-64), -1, null),
            new sim_event_t(simevent_t.se_startsound, 0, new sound_event_t(sfxenum_t.sfx_doropn, null, sec), sec.map.SoundOrgX, sec.map.SoundOrgY, -1, null),
            new sim_event_t(simevent_t.se_startsound, 0, new sound_event_t(sfxenum_t.sfx_itemup, null, null), 0, 0, -1, null),
        }, world.events);

        // An event made between tics belongs to the next tic and survives its start; then it is dropped.
        world.G_Ticker(new ticcmd_t());
        Assert.Equal(4, world.events.Count(e => e.tic == 0));
        world.G_Ticker(new ticcmd_t());
        Assert.DoesNotContain(world.events, e => e.tic == 0);
    }

    [Fact]
    public void TheUseGruntWaitsForTheFallback()
    {
        // The synthetic E1M1's west room (x and y -128..128): its north wall (line 0) is an S1
        // exit, the others plain walls.
        static World Use(Tweaks? tweaks, int x, int y, int degrees)
        {
            var w = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), tweaks ?? Tweaks.Vanilla);
            w.G_DoLoadLevel(Level.Load(new WadArchive(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) }), "E1M1"));
            w.PlaceMobj(Player(w), F(x), F(y), Deg(degrees));
            w.events.Clear();
            w.P_UseLines(w.players[0]);
            return w;
        }

        // Facing the south wall 28 units away: vanilla grunts (PTR_UseTraverse's sfx_noway, from the player).
        World world = Use(null, 0, -100, 270);
        Assert.Equal(new[] { new sound_event_t(sfxenum_t.sfx_noway, Player(world), null) }, world.StartedSounds());

        // With the use fallback, only when the fallback finds no line either: the exit is 228 units away.
        var tweaks = new Tweaks { UseFallback = true };
        world = Use(tweaks, 0, -100, 270);
        Assert.Null(world.UseFallbackLine(Player(world)));
        Assert.Equal(new[] { new sound_event_t(sfxenum_t.sfx_noway, Player(world), null) }, world.StartedSounds());

        // At (-100, 100) aiming at 150° the trace meets the west wall, but the exit is 28 units
        // north, 60° off the aim: the fallback uses it, silently.
        world = Use(tweaks, -100, 100, 150);
        Assert.Equal(gameaction_t.ga_completed, world.gameaction);
        Assert.DoesNotContain(world.StartedSounds(), s => s.sfx == sfxenum_t.sfx_noway);
        Assert.Equal(new[] { sfxenum_t.sfx_noway }, Use(null, -100, 100, 150).StartedSounds().Select(s => s.sfx)); // vanilla grunts

        // No wall on the trace, nothing within reach: silence, as vanilla.
        world = Use(tweaks, 0, 0, 270);
        Assert.Empty(world.StartedSounds());
        Assert.Empty(Use(null, 0, 0, 270).StartedSounds());
    }
}
