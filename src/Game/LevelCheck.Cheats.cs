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

    /// <summary>T7.9b: a pad's direction pressed and let go as the pad's state (polled by the menus, <see cref="LevelScene.PollMenuPad"/>), or a button through the menus' glue.</summary>
    private void PadTap(JoyButton button)
    {
        if (button is JoyButton.DpadUp or JoyButton.DpadDown or JoyButton.DpadLeft or JoyButton.DpadRight)
        {
            Input.ParseInputEvent(Held(new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = true }));
            Input.FlushBufferedEvents();
            _scene.PollMenuPad(0.1);
            Input.ParseInputEvent(Held(new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = false }));
            Input.FlushBufferedEvents();
            _scene.PollMenuPad(0.1);
            return;
        }
        _scene.MenuEvent(PadEvent(button));
        _scene.MenuEvent(Held(new InputEventJoypadButton { ButtonIndex = button, Pressed = false }));
    }

    /// <summary>T7.9b: the pad's D-pad down until the skull is on the item <paramref name="text"/> of the menu up; false when it isn't there.</summary>
    private bool PadToItem(string text)
    {
        MMenu menu = _scene.Menu;
        for (int i = 0; i <= menu.currentMenu.numitems; i++)
        {
            if (menu.currentMenu.menuitems[menu.itemOn].text == text)
                return true;
            PadTap(JoyButton.DpadDown);
        }
        return false;
    }

    /// <summary>T7.9b: the pad's Start, then Options, More Options and Cheats with the D-pad and A; false when it didn't get there.</summary>
    private bool PadToCheatsPage()
    {
        MMenu menu = _scene.Menu;
        if (!menu.Active)
            PadTap(JoyButton.Start);
        foreach ((string page, string item) in new[] { ("main", "OPTIONS"), ("options", "MORE OPTIONS..."), ("setup", "CHEATS...") })
        {
            if (menu.StateName != page || !PadToItem(item))
                return false;
            PadTap(JoyButton.A);
        }
        return menu.StateName == "cheats";
    }

    /// <summary>
    /// T7.9b: the cheats page with the pad only: the Start button, the D-pad
    /// and A through the menus' glue reach More Options → Cheats, and each of
    /// its items works its cheat as typed (the effect after the tic, the
    /// message line, the page still up): god mode, the arsenals, the
    /// mission's noclip on and off, the six power-ups, the chainsaw, the
    /// position, a song picked with the D-pad, the automap's iddt while the
    /// automap (the pad's Y) shows and refused while it doesn't, and a map
    /// picked to warp to (a new game, the menus closed). On nightmare the
    /// page refuses all but the warp, as vanilla's.
    /// </summary>
    private void CheckCheatPage()
    {
        GameFlow flow = _scene.Flow;
        MMenu menu = flow.Menu;
        string Step(string what) => $"cheats page (T7.9b): {what}: menu {menu.StateText()}, game {flow.StateText()}, message line \"{_scene.HudMessage}\"";

        _scene.StartNewGame(skill_t.sk_medium);
        flow.G_DoGameActions();
        if (_scene.World is not { } world || _scene.PlayerMobj is null)
        {
            Fail(Step("no new game"));
            return;
        }
        player_t p = world.players[world.consoleplayer];
        if (!PadToCheatsPage())
        {
            Fail(Step("the pad did not reach the cheats page"));
            return;
        }

        // Puts the skull on the item with the D-pad, A selects it, then the tics its cheats wait for.
        bool Select(string item, string? message, string what)
        {
            if (!PadToItem(item))
            {
                Fail(Step($"no item {item}"));
                return false;
            }
            PadTap(JoyButton.A);
            if (flow.CheatsPending == 0)
            {
                Fail(Step($"{item} ({what}): no cheat sent"));
                return false;
            }
            while (flow.CheatsPending > 0)
                _scene.Tic(default);
            if (message is not null && _scene.HudMessage != message)
            {
                Fail(Step($"{item} ({what}): expected the message \"{message}\""));
                return false;
            }
            if (menu.StateName != "cheats")
            {
                Fail(Step($"{item} ({what}): the page went"));
                return false;
            }
            return true;
        }

        p.health = p.mo!.health = 50;
        if (Select("GOD MODE", World.STSTR_DQDON, "god mode") && ((p.cheats & player_t.CF_GODMODE) == 0 || p.health != 100))
            Fail(Step("no god mode"));
        Select("GOD MODE", World.STSTR_DQDOFF, "god mode off");
        System.Array.Fill(p.cards, false);
        if (Select("WEAPONS AND AMMO", World.STSTR_FAADDED, "idfa") && (!System.Array.TrueForAll(p.weaponowned, w => w) || System.Array.Exists(p.cards, c => c)))
            Fail(Step("idfa: not every weapon, or keys"));
        if (Select("WEAPONS, AMMO, KEYS", World.STSTR_KFAADDED, "idkfa") && !System.Array.TrueForAll(p.cards, c => c))
            Fail(Step("idkfa: not every key"));
        if (Select("NO CLIPPING", World.STSTR_NCON, "noclip") && (p.cheats & player_t.CF_NOCLIP) == 0) // the mobj's flag follows in P_PlayerThink, once the menu lets the world go
            Fail(Step("noclip: the player mobj clips"));
        Select("NO CLIPPING", World.STSTR_NCOFF, "noclip off");
        foreach ((string item, powertype_t power) in new[]
        {
            ("INVULNERABILITY", powertype_t.pw_invulnerability), ("BERSERK", powertype_t.pw_strength), ("INVISIBILITY", powertype_t.pw_invisibility),
            ("RADIATION SUIT", powertype_t.pw_ironfeet), ("COMPUTER MAP", powertype_t.pw_allmap), ("LIGHT AMP", powertype_t.pw_infrared),
        })
        {
            p.powers[(int)power] = 0;
            if (Select(item, World.STSTR_BEHOLDX, item.ToLowerInvariant()) && p.powers[(int)power] == 0)
                Fail(Step($"{item}: no power"));
        }
        p.weaponowned[(int)weapontype_t.wp_chainsaw] = false;
        if (Select("CHAINSAW", World.STSTR_CHOPPERS, "idchoppers") && !p.weaponowned[(int)weapontype_t.wp_chainsaw])
            Fail(Step("idchoppers: no chainsaw"));
        if (Select("SHOW POSITION", null, "idmypos") && _scene.HudMessage?.StartsWith("ang=0x", System.StringComparison.Ordinal) != true)
            Fail(Step("idmypos: no position"));

        // idmus: the next song the picker offers (the D-pad's right), played
        var songs = flow.CheatSongs();
        if (songs.Count > 0 && PadToItem("MUSIC"))
        {
            int at = System.Math.Max(songs.FindIndex(s => s.Name == menu.currentMenu.menuitems[menu.itemOn].value!()), 0);
            PadTap(JoyButton.DpadRight);
            (string digits, string name) = songs[(at + 1) % songs.Count];
            if (Select("MUSIC", World.STSTR_MUS, "idmus") && _scene.Sound is { } sound
                && !string.Equals(SSound.MusicName(sound.mus_requested), name, System.StringComparison.OrdinalIgnoreCase))
                Fail(Step($"idmus{digits}: {_scene.MusicText()}, expected {name}"));
        }

        // iddt: refused with the automap closed; with it open (the pad's Y), each select steps it
        if (!PadToItem("MAP DETAIL"))
            Fail(Step("no MAP DETAIL"));
        PadTap(JoyButton.A);
        if (_scene.Automap is not { } am || am.cheating != 0 || menu.LastSound != sfxenum_t.sfx_oof)
            Fail(Step("iddt with the automap closed was not refused"));
        else
        {
            while (menu.Active)
                PadTap(JoyButton.B);
            _scene.AutomapEvent(Held(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = true }));
            _scene.AutomapEvent(Held(new InputEventJoypadButton { ButtonIndex = JoyButton.Y, Pressed = false }));
            if (!am.automapactive || !PadToCheatsPage() || !PadToItem("MAP DETAIL"))
                Fail(Step("the automap (the pad's Y), then the cheats page"));
            foreach (int step in new[] { 1, 2, 0 })
            {
                PadTap(JoyButton.A);
                if (am.cheating != step || flow.CheatsPending != 0)
                    Fail(Step($"iddt from the page: step {am.cheating}, expected {step} (and no ticcmd)"));
            }
            _scene.CloseAutomap();
        }

        // idclev: the next map the picker offers (the D-pad's right), a new game before the tic, the menus closed
        var maps = flow.CheatMaps();
        string from = world.level.Name;
        int here = maps.FindIndex(m => m.Name == from);
        if (maps.Count > 1 && here >= 0 && PadToItem("WARP TO"))
        {
            PadTap(JoyButton.DpadRight);
            string to = maps[(here + 1) % maps.Count].Name;
            PadTap(JoyButton.A);
            while (flow.CheatsPending > 0)
                _scene.Tic(default);
            if (_scene.World is not { } next || ReferenceEquals(next, world) || next.level.Name != to || menu.Active)
                Fail(Step($"warp: {_scene.World?.level.Name}, expected a new game on {to} with the menus closed"));
        }

        // nightmare: everything refused but the warp
        _scene.StartNewGame(skill_t.sk_nightmare);
        flow.G_DoGameActions();
        if (_scene.World is not { } hard || _scene.PlayerMobj is null || !PadToCheatsPage())
        {
            Fail(Step("no new game on nightmare, or no cheats page"));
            return;
        }
        if (menu.CheatsHint() != MMenu.CHEATSNIGHTMARE)
            Fail(Step($"nightmare's hint: {menu.CheatsHint()}"));
        foreach (string item in new[] { "GOD MODE", "WEAPONS, AMMO, KEYS", "INVULNERABILITY", "MUSIC" })
        {
            if (!PadToItem(item))
                continue;
            PadTap(JoyButton.A);
            if (flow.CheatsPending != 0 || menu.LastSound != sfxenum_t.sfx_oof)
                Fail(Step($"{item} on nightmare was not refused"));
        }
        if (hard.players[hard.consoleplayer].cheats != 0)
            Fail(Step("a cheat on nightmare"));
        string second = World.MapName(_scene.GameMode, 1, 2);
        if (_scene.HasLump(second) && PadToItem("WARP TO"))
        {
            while (menu.currentMenu.menuitems[menu.itemOn].value!() != second)
            {
                PadTap(JoyButton.DpadRight);
                if (menu.currentMenu.menuitems[menu.itemOn].value!() == hard.level.Name)
                    break;
            }
            PadTap(JoyButton.A);
            while (flow.CheatsPending > 0)
                _scene.Tic(default);
            if (_scene.World?.level.Name != second || _scene.World.gameskill != skill_t.sk_nightmare)
                Fail(Step($"warp on nightmare: {_scene.World?.level.Name}, expected {second}"));
        }
        while (menu.Active)
            PadTap(JoyButton.B);
        GD.Print("Level check: cheats page (T7.9b): reached with the pad; god mode, idfa, idkfa, noclip, the six power-ups, the chainsaw, the position, "
            + "a song, iddt with the automap open (refused closed) and a warp, each from the pad; nightmare's refusals and its warp checked");
    }
}
