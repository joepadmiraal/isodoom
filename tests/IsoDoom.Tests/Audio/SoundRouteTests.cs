using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Audio;
using IsoDoom.Sim;
using IsoDoom.Tests.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Audio;

/// <summary>
/// T7.7: every route's sounds (the events whose sequence T6.10 checks
/// against vanilla's <c>S_StartSound</c> calls tic by tic) played through
/// <see cref="SSound"/> as the level scene plays them, one update a tic,
/// each sound as long as its lump: each start takes a channel at its tic
/// unless vanilla's rules drop it (out of hearing, every channel busy with
/// better priorities), at the right volume for its distance and on the
/// right side of the screen (the game camera's: map north-west is up), and
/// each removed mobj's sound stops.
/// </summary>
public class SoundRouteTests
{
    public static TheoryData<string> Routes() => new(VanillaRoute.Names());

    /// <summary>The game camera's screen-up direction (map north-west), the listener's angle in the default stereo.</summary>
    private const uint ScreenUp = 0x60000000; // 135°

    private sealed class LumpDevice(WadArchive wad) : ISoundDevice
    {
        private readonly Dictionary<sfxenum_t, double> _seconds = new();

        public double I_StartSound(sfxenum_t sfx, int cnum, int vol, int sep)
        {
            if (!_seconds.TryGetValue(sfx, out double s))
            {
                int lump = wad.W_CheckNumForName(SoundInfo.LumpName(sfx));
                s = lump >= 0 && DmxSound.TryDecode(wad.W_CacheLumpNum(lump).Span, out _) is DmxSound d ? d.Seconds : 0;
                _seconds[sfx] = s;
            }
            return s;
        }

        public void I_StopSound(int cnum) { }

        public void I_UpdateSoundParams(int cnum, int vol, int sep) { }

        public void I_PauseSounds(bool paused) { }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EverySoundPlaysAtItsTicAndPlace(string name)
    {
        VanillaRoute route = VanillaRoute.Load(name);
        World world = route.NewWorld(out WadArchive wad);
        var sound = new SSound(new LumpDevice(wad)) { snd_SfxVolume = 127 };
        var events = new List<sim_event_t>();
        int starts = 0, played = 0, sided = 0;
        for (int tic = 0; tic < route.Cmds.Count; tic++)
        {
            if (route.Reborn(world, wad))
                sound.S_Start(); // P_SetupLevel's
            route.File.RunEvents(world, tic);
            world.G_Ticker(route.Cmds[tic]);
            sound.gamemap = world.gamemap;
            mobj_t mo = world.players[0].mo!;
            var listener = new sound_listener_t(mo, mo.x, mo.y, ScreenUp);
            events.Clear();
            world.DrainEvents(events);
            foreach (sim_event_t e in events)
            {
                string what = $"{name} tic {tic + 1}: {SSound.SfxName(e.sound.sfx)}";
                object? origin = (object?)e.sound.origin ?? e.sound.sector;
                if (e.type == simevent_t.se_stopsound)
                {
                    sound.S_StopSound(origin);
                    Assert.DoesNotContain(sound.channels, c => c.sfxinfo is not null && c.origin == origin);
                    continue;
                }
                if (e.type != simevent_t.se_startsound)
                    continue;
                starts++;
                sfxinfo_t info = SoundInfo.S_sfx[(int)e.sound.sfx];
                var busy = sound.channels.Where(c => c.sfxinfo is not null && (origin is null || c.origin != origin)).Select(c => c.sfxinfo!.priority).ToList();
                sound_start_t st = sound.S_StartSound(origin, e.x, e.y, e.sound.sfx, listener);

                double dx = (e.x - mo.x) / 65536.0, dy = (e.y - mo.y) / 65536.0, dist = Math.Sqrt(dx * dx + dy * dy);
                bool local = origin is null || origin == mo;
                if (st.result == sound_result_t.sr_inaudible)
                {
                    Assert.False(local, $"{what}: a sound with no origin or from the player went unheard");
                    Assert.True(dist > 1060 && world.gamemap != 8, $"{what}: unheard at {dist:0} units");
                    continue;
                }
                if (st.result == sound_result_t.sr_nochannel)
                {
                    // every other channel held a sound of better priority
                    Assert.Equal(SSound.DefaultChannels, busy.Count);
                    Assert.All(busy, p => Assert.True(p < info.priority, $"{what}: no channel, though one held priority {p} (it has {info.priority})"));
                    continue;
                }
                if (st.result == sound_result_t.sr_nolump)
                    Assert.NotEqual("doom1", route.Iwad); // DOOM1 has every lump its routes play; the synthetic IWAD has only DSPISTOL
                else
                    played++;
                Assert.Same(origin, sound.channels[st.cnum].origin);
                Assert.Equal(e.sound.sfx, sound.channels[st.cnum].sfx);
                if (local || dist < 180)
                {
                    Assert.Equal(127, st.vol);
                    if (local)
                        Assert.Equal(128, st.sep);
                    continue;
                }
                // the volume for the distance (vanilla's approximate distance is within 12% of the true one) ...
                double near = Math.Clamp(127 * (1200 - dist * 0.88) / 1000, 0, 127), far = Math.Clamp(127 * (1200 - dist * 1.12) / 1000, 0, 127);
                if (world.gamemap != 8)
                    Assert.InRange(st.vol, (int)far - 1, (int)Math.Ceiling(near) + 1);
                // ... and the side of the screen: up is map north-west, right map north-east
                double right = (dx + dy) / Math.Sqrt(2) / dist;
                if (right > 0.3)
                    Assert.True(st.sep > 128, $"{what}: on the screen's right ({dx:0}, {dy:0}) but sep {st.sep}");
                else if (right < -0.3)
                    Assert.True(st.sep < 128, $"{what}: on the screen's left ({dx:0}, {dy:0}) but sep {st.sep}");
                sided++;
            }
            sound.Advance(1.0 / SimInfo.TICRATE);
            sound.S_UpdateSounds(listener);
            // a channel's origin is still in the world (removed mobjs stopped theirs)
            Assert.All(sound.channels, c => Assert.True(c.origin is not mobj_t m || m.function != think_t.REMOVED, $"{name} tic {tic + 1}: a channel follows a removed mobj"));
        }
        if (route.Iwad == "doom1")
            Assert.True(played >= starts / 2, $"{name}: {played} of {starts} sounds played");
        _ = sided;
    }
}
