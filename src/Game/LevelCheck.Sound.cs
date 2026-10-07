using System;
using System.Linq;
using Godot;
using IsoDoom.Audio;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

// T7.7: the sound effects through the scene (no renderer needed; Godot's
// dummy audio driver under --headless): every sound of the table whose lump
// DMX plays decodes into its stream; a sound the sim starts from a mobj west
// of the player (up and to the left on the game camera's screen) takes a
// channel at the tic it was made in, at vanilla's volume for its distance and
// on the left, its player set to the stream, volume and pan; moved east, it
// follows to the right; the mobj's removal stops it; a menu sound plays in
// the centre at full volume; the focus pause holds the players; the sounds
// end by themselves; a level load stops them all.
public partial class LevelCheck
{
    private void CheckSound()
    {
        if (_scene.Sound is not { } sound || _scene.SfxDevice is not { } device || _scene.Wad is not { } wad)
        {
            Fail("sound: the scene has no sound");
            return;
        }
        string map = RenderMapName();
        _scene.LoadMap(map);
        if (sound.channels.Any(c => c.sfxinfo is not null))
            Fail("sound: a channel still plays after a level load (S_Start)");

        // every sound of the table: its lump's stream, as DMX plays it
        int streams = 0, missing = 0;
        for (int i = 1; i < (int)sfxenum_t.NUMSFX; i++)
        {
            var sfx = (sfxenum_t)i;
            int lump = wad.W_CheckNumForName(SoundInfo.LumpName(sfx));
            DmxSound? expected = lump >= 0 ? DmxSound.TryDecode(wad.W_CacheLumpNum(lump).Span, out _) : null;
            (AudioStreamWav? stream, double seconds) = device.StreamFor(sfx);
            if (expected is null)
            {
                missing++;
                if (stream is not null)
                    Fail($"sound: {SoundInfo.LumpName(sfx)}: a stream for a lump that is no playable sound");
                continue;
            }
            streams++;
            if (stream is null || stream.MixRate != expected.SampleRate || stream.Format != AudioStreamWav.FormatEnum.Format8Bits || stream.Stereo
                || !stream.Data.AsSpan().SequenceEqual(expected.ToSigned8()) || Math.Abs(seconds - expected.Seconds) > 1e-9)
                Fail($"sound: {SoundInfo.LumpName(sfx)}: the stream is not the lump's sound");
        }

        if (_scene.World is not { } world || _scene.PlayerMobj is not { } me)
        {
            Fail($"sound: {map}: no player");
            return;
        }
        _scene.UpdateSound(0); // the options' volume (sfxVolume 8: 64)
        int full = sound.snd_SfxVolume;
        // a sound from 300 units west: vanilla's volume for the distance; up and to the left on the game camera's screen
        mobj_t barrel = world.P_SpawnMobj(me.x - 300 * 65536, me.y, World.ONFLOORZ, mobjtype_t.MT_BARREL);
        world.S_StartSound(barrel, sfxenum_t.sfx_pistol);
        _scene.Tic(new ticcmd_t());
        int cnum = Array.FindIndex(sound.channels, c => c.origin == barrel);
        int vol = full * (1200 - 300) / 1000;
        if (cnum < 0 || sound.channels[cnum].sfx != sfxenum_t.sfx_pistol)
        {
            Fail($"sound: {map}: the sound from 300 units west did not take a channel: {_scene.ChannelsText()}");
            return;
        }
        SSound.channel_t ch = sound.channels[cnum];
        AudioStreamPlayer player = device.Player(cnum);
        if (ch.vol != vol || ch.sep is < 55 or > 67)
            Fail($"sound: {map}: 300 units west: volume {ch.vol} (expected {vol}), separation {ch.sep} (expected about 61: left)");
        if (player.Stream != device.StreamFor(sfxenum_t.sfx_pistol).Stream || player.Bus != $"{device.SfxBus}{cnum}"
            || Math.Abs(player.VolumeDb - device.VolumeDb(ch.vol)) > 1e-4 || Math.Abs(device.Panner(cnum).Pan - SfxPlayer.Pan(ch.sep)) > 1e-6
            || device.Panner(cnum).Pan >= 0)
            Fail($"sound: {map}: channel {cnum}'s player: bus {player.Bus}, {player.VolumeDb} dB, pan {device.Panner(cnum).Pan}");
        if (AudioServer.GetBusIndex(player.Bus) is var bus && (bus < 0 || AudioServer.GetBusSend(bus) != device.SfxBus))
            Fail($"sound: {map}: channel {cnum}'s bus {player.Bus} does not send to {device.SfxBus}");

        // it follows its mobj: moved to the east, it is on the right
        barrel.x = me.x + 300 * 65536;
        _scene.UpdateSound(0);
        if (ch.sep <= 128 || device.Panner(cnum).Pan <= 0 || ch.vol != vol)
            Fail($"sound: {map}: moved east, separation {ch.sep}, pan {device.Panner(cnum).Pan}, volume {ch.vol}");

        // the mobj's removal stops it (P_RemoveMobj's S_StopSound, an event of the next tic)
        world.P_RemoveMobj(barrel);
        _scene.Tic(new ticcmd_t());
        if (sound.channels.Any(c => c.origin == barrel) || player.Playing && player.Stream == device.StreamFor(sfxenum_t.sfx_pistol).Stream)
            Fail($"sound: {map}: the removed mobj's sound still plays");

        // a menu sound: no origin, full volume, the centre
        ((IMenuHost)_scene).StartSound(sfxenum_t.sfx_pistol);
        int menu = Array.FindIndex(sound.channels, c => c.sfx == sfxenum_t.sfx_pistol && c.origin is null);
        if (menu < 0 || sound.channels[menu].vol != full || sound.channels[menu].sep != SSound.NORM_SEP || Math.Abs(device.Panner(menu).Pan) > 0.01f)
            Fail($"sound: {map}: the menu sound: {_scene.ChannelsText()}");

        // the focus pause holds every player; the game's pause only the music (vanilla)
        sound.PauseAll(true);
        bool held = device.Player(menu).StreamPaused;
        sound.PauseAll(false);
        if (!held || device.Player(menu).StreamPaused)
            Fail($"sound: {map}: the focus pause did not hold and free the players");

        // the sounds end by themselves (each channel counts its sound's length)
        _scene.UpdateSound(10);
        _scene.UpdateSound(0);
        if (sound.channels.Any(c => c.sfxinfo is not null))
            Fail($"sound: {map}: channels still play 10 s on: {_scene.ChannelsText()}");

        // a level load stops every sound
        ((IMenuHost)_scene).StartSound(sfxenum_t.sfx_pistol);
        _scene.LoadMap(map);
        if (sound.channels.Any(c => c.sfxinfo is not null))
            Fail($"sound: {map}: a level load left a channel playing");

        GD.Print($"Level check: sound: {streams} sounds decoded ({missing} of the table not in the WAD or not playable), {sound.Results[(int)sound_result_t.sr_played]} started on a channel since the check began");
    }
}
