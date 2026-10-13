using System;
using System.Collections.Generic;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// T7.9b: the automap's side of the cheats page: am_map.c's <c>iddt</c>,
/// which its responder reads only while the automap shows. The level scene
/// implements it (<see cref="GameFlow.AutomapCheats"/>); the tests fake it.
/// </summary>
public interface ICheatAutomap
{
    /// <summary>Whether the automap shows (its responder reads the keys).</summary>
    bool AutomapShown { get; }

    /// <summary>am_map.c <c>cheating</c>: 0, 1 (every line) or 2 (and the things).</summary>
    int AutomapCheating { get; }

    /// <summary>Types <paramref name="keys"/> into the automap's responder (<see cref="AmMap.TypeCheat"/>); whether <c>iddt</c> matched.</summary>
    bool TypeAutomapCheat(string keys);
}

// T7.9b: the cheats page (More Options → Cheats), for a pad (the Steam Deck):
// each item types its cheat's keys through st_stuff.c's reader
// (GameFlow.TypeCheat → ST_Responder), so the cheat goes in the next ticcmd
// and vanilla's refusals hold (nothing on nightmare but idclev, nothing off a
// level); iddt goes to the automap's responder, read only while it shows.
// idclev and idmus pick their two digits from the maps and songs they take.
// Not vanilla's (it has no cheat menu; SPEC §12 T7.9b).
public sealed partial class MMenu
{
    /// <summary>T7.9b: the cheats page.</summary>
    public menu_t CheatsDef { get; private set; } = null!;

    // T7.9b: the pickers' choices (indexes into GameFlow.CheatMaps and CheatSongs).
    private int _clevPick, _musPick;

    // T7.9b: a menu item's sound in place of the pistol (a refused cheat's oof), set by its routine.
    private sfxenum_t? _itemSound;

    /// <summary>The page's hint while a cheat would be refused: off a level, and on nightmare (where only <c>idclev</c> works).</summary>
    public const string CHEATSLEVELONLY = "ONLY IN A GAME", CHEATSNIGHTMARE = "NIGHTMARE: ONLY WARP";

    /// <summary>The cheats page's hint otherwise.</summary>
    public const string CHEATSKEYS = "SELECT: CHEAT   LEFT / RIGHT: PICK";

    private void CheatMenus()
    {
        CheatsDef = TextMenu("cheats", "CHEATS", SetupDef, []);
        string noclip = flow.gamemission == GameMission.doom ? "idspispopd" : "idclip";
        var items = new List<menuitem_t>
        {
            CheatItem("GOD MODE", "iddqd", 'g', p => OnOff((p.cheats & player_t.CF_GODMODE) != 0)),
            CheatItem("WEAPONS, AMMO, KEYS", "idkfa", 'k'),
            CheatItem("WEAPONS AND AMMO", "idfa", 'f'),
            CheatItem("NO CLIPPING", noclip, 'n', p => OnOff((p.cheats & player_t.CF_NOCLIP) != 0)),
            PowerItem("INVULNERABILITY", 'v', powertype_t.pw_invulnerability, 'i'),
            PowerItem("BERSERK", 's', powertype_t.pw_strength, 'b'),
            PowerItem("INVISIBILITY", 'i', powertype_t.pw_invisibility, '\0'),
            PowerItem("RADIATION SUIT", 'r', powertype_t.pw_ironfeet, 'r'),
            PowerItem("COMPUTER MAP", 'a', powertype_t.pw_allmap, 'c'),
            PowerItem("LIGHT AMP", 'l', powertype_t.pw_infrared, 'l'),
            CheatItem("CHAINSAW", "idchoppers", '\0'),
            CheatItem("SHOW POSITION", "idmypos", 'p'),
            new(1, "", _ => AutomapCheat(), 'd', "MAP DETAIL")
            {
                value = () => flow.AutomapCheats is { AutomapShown: true } am ? am.AutomapCheating switch { 0 => "OFF", 1 => "LINES", _ => "THINGS" } : "MAP CLOSED",
            },
            new(2, "", choice => _clevPick = Step(_clevPick, flow.CheatMaps().Count, choice), 'w', "WARP TO")
            {
                value = () => Picked(flow.CheatMaps(), _clevPick),
                select = () => PickedCheat("idclev", flow.CheatMaps(), _clevPick, close: true),
            },
            new(2, "", choice => _musPick = Step(_musPick, flow.CheatSongs().Count, choice), 'm', "MUSIC")
            {
                value = () => Picked(flow.CheatSongs(), _musPick),
                select = () => PickedCheat("idmus", flow.CheatSongs(), _musPick, close: false),
            },
        };
        SetTextItems(CheatsDef, [.. items]);
    }

    private menuitem_t CheatItem(string label, string keys, char alphaKey, Func<player_t, string>? state = null) =>
        new(1, "", _ => Cheat(keys, close: false), alphaKey, label)
        {
            value = state is null ? null : () => flow.World is { } w && w.players[w.consoleplayer].mo is not null ? state(w.players[w.consoleplayer]) : "",
        };

    private menuitem_t PowerItem(string label, char letter, powertype_t power, char alphaKey) =>
        CheatItem(label, "idbehold" + letter, alphaKey, p => OnOff(p.powers[(int)power] != 0));

    private static string OnOff(bool on) => on ? "ON" : "OFF";

    /// <summary>The cheats page from More Options: the pickers on the map played and its song's digits, when they have them.</summary>
    private void Cheats(int choice)
    {
        if (flow.World is { } w)
        {
            List<(string Digits, string Name)> maps = flow.CheatMaps(), songs = flow.CheatSongs();
            int map = maps.FindIndex(m => m.Name == w.level.Name);
            if (map >= 0)
            {
                _clevPick = map;
                int song = songs.FindIndex(s => s.Digits == maps[map].Digits);
                if (song >= 0)
                    _musPick = song;
            }
        }
        M_SetupNextMenu(CheatsDef);
    }

    /// <summary>Types cheat <paramref name="keys"/> through the game flow's reader (vanilla's refusals); a refusal sounds <c>sfx_oof</c>. With <paramref name="close"/> the menus close after it matched (<c>idclev</c>: a new game).</summary>
    private void Cheat(string keys, bool close)
    {
        if (!flow.TypeCheat(keys))
        {
            _itemSound = sfxenum_t.sfx_oof;
            return;
        }
        Changes++;
        if (close)
            M_ClearMenus();
    }

    /// <summary><c>iddt</c>: its keys to the automap's responder, which reads them only while it shows.</summary>
    private void AutomapCheat()
    {
        if (flow.gamestate != gamestate_t.GS_LEVEL || flow.AutomapCheats is not { AutomapShown: true } am || !am.TypeAutomapCheat("iddt"))
            _itemSound = sfxenum_t.sfx_oof;
        Changes++;
    }

    private void PickedCheat(string cheat, List<(string Digits, string Name)> choices, int pick, bool close)
    {
        if (choices.Count == 0)
        {
            _itemSound = sfxenum_t.sfx_oof;
            return;
        }
        Cheat(cheat + choices[Math.Clamp(pick, 0, choices.Count - 1)].Digits, close);
    }

    private static int Step(int pick, int count, int choice) =>
        count == 0 ? 0 : ((Math.Clamp(pick, 0, count - 1) + (choice == 0 ? -1 : 1)) % count + count) % count;

    private static string Picked(List<(string Digits, string Name)> choices, int pick) =>
        choices.Count == 0 ? "NONE" : choices[Math.Clamp(pick, 0, choices.Count - 1)].Name;

    /// <summary>The cheats page's hint: why its cheats are refused, or how to work it.</summary>
    public string CheatsHint() =>
        flow.gamestate != gamestate_t.GS_LEVEL || flow.World is not { } w || w.players[w.consoleplayer].mo is null ? CHEATSLEVELONLY
            : !w.ST_CheatsAllowed ? CHEATSNIGHTMARE
            : CHEATSKEYS;
}
