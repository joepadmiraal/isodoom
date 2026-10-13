using Godot;
using IsoDoom.Audio;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

// T7.9a: the cheats typed through the scene's input glue (no renderer
// needed): on a new game, each cheat's keys go to the cheat reader as the
// keyboard's (CheatEvent), the keys past a cheat's "id" taken from the debug
// keys; the cheat goes with the next tic and does what vanilla's does, its
// message on the message line: iddqd, idkfa, the mission's noclip,
// idbeholdv and idbehold, idchoppers, idmypos, idmus (the second map's song)
// and idclev (the second map, a new game before the tic). On nightmare the
// cheats on the player are refused and idclev still works.
public partial class LevelCheck
{
    private void CheckCheats()
    {
        GameFlow flow = _scene.Flow;
        bool commercial = _scene.GameMode == GameMode.commercial;
        string second = World.MapName(_scene.GameMode, 1, 2);

        string Step(string what) => $"cheats (T7.9a): {what}: game {flow.StateText()}, message line \"{_scene.HudMessage}\"";

        _scene.StartNewGame(skill_t.sk_medium);
        flow.G_DoGameActions();
        if (_scene.World is not { } world || flow.gamestate != gamestate_t.GS_LEVEL || _scene.PlayerMobj is null)
        {
            Fail(Step("no new game"));
            return;
        }
        player_t p = world.players[world.consoleplayer];

        // Types the keys; true when every key past the cheat's "id" was taken from the debug keys.
        bool Type(string keys)
        {
            bool taken = true;
            for (int i = 0; i < keys.Length; i++)
            {
                Key key = keys[i] is >= '0' and <= '9' ? Key.Key0 + (keys[i] - '0') : Key.A + (keys[i] - 'a');
                bool took = _scene.CheatEvent(KeyEvent(key));
                if (i >= 2 && !took || i < 2 && took)
                    taken = false;
            }
            return taken;
        }

        bool Cheat(string keys, string? message, string what)
        {
            if (!Type(keys))
            {
                Fail(Step($"{keys}: the keys past \"id\" were not all taken from the debug keys"));
                return false;
            }
            if (flow.CheatsPending == 0)
            {
                Fail(Step($"{keys}: no cheat matched"));
                return false;
            }
            while (flow.CheatsPending > 0)
                _scene.Tic(default);
            if (message is not null && _scene.HudMessage != message)
            {
                Fail(Step($"{keys} ({what}): the message line shows \"{_scene.HudMessage}\", expected \"{message}\""));
                return false;
            }
            return true;
        }

        p.health = p.mo!.health = 50;
        if (!Cheat("iddqd", World.STSTR_DQDON, "god mode"))
            return;
        if ((p.cheats & player_t.CF_GODMODE) == 0 || p.health != 100 || p.mo.health != 100)
            Fail(Step("iddqd: no god mode, or the health not 100"));

        if (!Cheat("idkfa", World.STSTR_KFAADDED, "the arsenal"))
            return;
        if (!System.Array.TrueForAll(p.weaponowned, w => w) || !System.Array.TrueForAll(p.cards, c => c) || p.armorpoints != 200)
            Fail(Step("idkfa: not every weapon, key and 200 armor"));

        string noclip = flow.gamemission == GameMission.doom ? "idspispopd" : "idclip";
        if (!Cheat(noclip, World.STSTR_NCON, "no clipping"))
            return;
        if ((p.mo.flags & mobjflag_t.MF_NOCLIP) == 0)
            Fail(Step($"{noclip}: the player mobj clips"));
        Cheat(noclip, World.STSTR_NCOFF, "clipping");

        if (!Cheat("idbeholdv", World.STSTR_BEHOLDX, "invulnerability"))
            return;
        if (p.powers[(int)powertype_t.pw_invulnerability] <= World.INVULNTICS - 3)
            Fail(Step($"idbeholdv: invulnerability {p.powers[(int)powertype_t.pw_invulnerability]} tics"));

        p.weaponowned[(int)weapontype_t.wp_chainsaw] = false;
        if (Cheat("idchoppers", World.STSTR_CHOPPERS, "the chainsaw") && !p.weaponowned[(int)weapontype_t.wp_chainsaw])
            Fail(Step("idchoppers: no chainsaw"));

        if (Cheat("idmypos", null, "the position") && _scene.HudMessage?.StartsWith("ang=0x", System.StringComparison.Ordinal) != true)
            Fail(Step("idmypos: no position"));

        // idmus: the second map's song
        if (!Cheat(commercial ? "idmus02" : "idmus12", World.STSTR_MUS, "the music"))
            return;
        musicenum_t song = commercial ? musicenum_t.mus_stalks : musicenum_t.mus_e1m2;
        if (_scene.Sound is { } sound && sound.mus_requested != song)
            Fail(Step($"idmus: {_scene.MusicText()}, expected {SSound.MusicName(song)}"));

        // idclev: a new game on the second map, before the tic
        if (_scene.HasLump(second))
        {
            Cheat(commercial ? "idclev02" : "idclev12", null, "the level");
            if (_scene.World is not { } next || ReferenceEquals(next, world) || next.level.Name != second || next.leveltime != 1
                || next.gameskill != skill_t.sk_medium || flow.gamestate != gamestate_t.GS_LEVEL)
                Fail(Step($"idclev: {_scene.World?.level.Name} at tic {_scene.World?.leveltime}, expected a new game on {second} at tic 1"));
        }

        // nightmare: the cheats on the player refused (their keys not even read), idclev works
        _scene.StartNewGame(skill_t.sk_nightmare);
        flow.G_DoGameActions();
        if (_scene.World is not { } hard || _scene.PlayerMobj is null)
        {
            Fail(Step("no new game on nightmare"));
            return;
        }
        Type("iddqd");
        if (flow.CheatsPending != 0 || flow.Cheats.Typed() != 0)
            Fail(Step("iddqd on nightmare was read"));
        string first = hard.level.Name;
        if (_scene.HasLump(second))
        {
            Cheat(commercial ? "idclev02" : "idclev12", null, "the level on nightmare");
            if (_scene.World?.level.Name != second || _scene.World.gameskill != skill_t.sk_nightmare)
                Fail(Step($"idclev on nightmare: {_scene.World?.level.Name}, expected {second} from {first}"));
        }
        GD.Print($"Level check: cheats (T7.9a): iddqd, idkfa, {noclip}, idbeholdv, idchoppers, idmypos, idmus, idclev and nightmare's refusals checked");
    }
}
