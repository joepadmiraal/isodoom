using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Audio;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.9b: the cheats page (More Options → Cheats, <see cref="MMenu.CheatsDef"/>)
/// worked with the menus' keys as a pad's (select, left, right): each item
/// types its cheat through the game flow's reader, so it goes in the next
/// tic's <c>ticcmd</c> with vanilla's refusals (nothing on nightmare but
/// <c>idclev</c>, nothing off a level); the pickers give <c>idclev</c> and
/// <c>idmus</c> their digits; <c>iddt</c> goes to the automap while it shows.
/// </summary>
public class CheatPageTests
{
    private const int Esc = MMenu.KEY_ESCAPE, Left = MMenu.KEY_LEFTARROW, Right = MMenu.KEY_RIGHTARROW;
    private const int A = MMenu.KEY_PAD_ACCEPT, B = MMenu.KEY_PAD_CANCEL;

    private static (GameFlow Flow, GameFlowTests.Host Host, World World) Level(skill_t skill = skill_t.sk_medium,
        GameMode mode = GameMode.shareware, string map = "E1M1") // the synthetic IWAD's maps, whatever the mode
    {
        var host = new GameFlowTests.Host(mode);
        var flow = new GameFlow(host, mode, new DoomRandom(), GameVariant.vanilla);
        flow.G_InitNewMap(skill, map);
        flow.G_Ticker(new ticcmd_t());
        return (flow, host, host.World!);
    }

    /// <summary>Opens the menus and goes to the cheats page as the keys do (Options, More Options, Cheats).</summary>
    private static MMenu OpenPage(GameFlow flow)
    {
        MMenu menu = flow.Menu;
        foreach (int key in new[] { Esc, 'o', MMenu.KEY_ENTER, 'o', MMenu.KEY_ENTER, 'e', A })
            menu.M_Responder(key);
        Assert.Equal("cheats", menu.StateName);
        return menu;
    }

    /// <summary>Selects the page's item <paramref name="text"/> with the pad's A, then runs the tics the cheats wait for; returns the messages they left.</summary>
    private static List<string> Select(GameFlow flow, string text)
    {
        MMenu menu = flow.Menu;
        menu.itemOn = (short)Array.FindIndex(menu.currentMenu.menuitems, i => i.text == text);
        Assert.True(menu.itemOn >= 0, text);
        menu.M_Responder(A);
        var events = new List<sim_event_t>();
        do
        {
            flow.G_Ticker(new ticcmd_t());
            flow.World?.DrainEvents(events);
        }
        while (flow.CheatsPending > 0);
        return [.. events.Where(e => e.type == simevent_t.se_message).Select(e => e.message!)];
    }

    private static string Value(MMenu menu, string text) => menu.currentMenu.menuitems.First(i => i.text == text).value!();

    private static player_t Player(World world) => world.players[world.consoleplayer];

    [Fact]
    public void MoreOptionsHasTheCheatsPageAndItFitsTheScreen()
    {
        (GameFlow flow, _, _) = Level();
        MMenu menu = OpenPage(flow);
        Assert.Equal(
            ["GOD MODE", "WEAPONS, AMMO, KEYS", "WEAPONS AND AMMO", "NO CLIPPING", "INVULNERABILITY", "BERSERK", "INVISIBILITY",
                "RADIATION SUIT", "COMPUTER MAP", "LIGHT AMP", "CHAINSAW", "SHOW POSITION", "MAP DETAIL", "WARP TO", "MUSIC"],
            menu.CheatsDef.menuitems.Select(i => i.text));
        Assert.True(menu.CheatsDef.y + menu.CheatsDef.numitems * menu.CheatsDef.lineHeight <= MMenu.TEXTHINTY);
        Assert.Equal(MMenu.CHEATSKEYS, menu.CheatsHint());
        menu.M_Responder(B);
        Assert.Equal("setup", menu.StateName);
    }

    [Fact]
    public void TheTogglesWorkAsTypedWithTheMenuUp()
    {
        (GameFlow flow, _, World world) = Level();
        MMenu menu = OpenPage(flow);
        player_t p = Player(world);
        p.health = p.mo!.health = 30;
        Assert.Equal("OFF", Value(menu, "GOD MODE"));
        Assert.Equal([World.STSTR_DQDON], Select(flow, "GOD MODE"));
        Assert.Equal((player_t.CF_GODMODE, 100, "ON"), (p.cheats & player_t.CF_GODMODE, p.health, Value(menu, "GOD MODE")));
        Assert.Equal(sfxenum_t.sfx_pistol, menu.LastSound);
        Assert.Equal([World.STSTR_DQDOFF], Select(flow, "GOD MODE"));
        Assert.Equal("OFF", Value(menu, "GOD MODE"));

        Assert.Equal([World.STSTR_FAADDED], Select(flow, "WEAPONS AND AMMO"));
        Assert.All(p.cards, Assert.False);
        Assert.Equal([World.STSTR_KFAADDED], Select(flow, "WEAPONS, AMMO, KEYS"));
        Assert.All(p.cards, Assert.True);

        Assert.Equal([World.STSTR_NCON], Select(flow, "NO CLIPPING")); // Doom's idspispopd
        Assert.Equal("ON", Value(menu, "NO CLIPPING"));
        Assert.Equal([World.STSTR_NCOFF], Select(flow, "NO CLIPPING"));

        foreach ((string item, powertype_t power) in new[]
        {
            ("INVULNERABILITY", powertype_t.pw_invulnerability), ("BERSERK", powertype_t.pw_strength), ("INVISIBILITY", powertype_t.pw_invisibility),
            ("RADIATION SUIT", powertype_t.pw_ironfeet), ("COMPUTER MAP", powertype_t.pw_allmap), ("LIGHT AMP", powertype_t.pw_infrared),
        })
        {
            Assert.Equal([World.STSTR_BEHOLD, World.STSTR_BEHOLDX], Select(flow, item)); // as typed: idbehold matches on the way
            Assert.True(p.powers[(int)power] != 0, item);
            Assert.Equal("ON", Value(menu, item));
        }
        // again: vanilla's toggle takes berserk away, leaves the others one tic (the world holds under the menu)
        Select(flow, "BERSERK");
        Assert.Equal((0, "OFF"), (p.powers[(int)powertype_t.pw_strength], Value(menu, "BERSERK")));
        Select(flow, "INVULNERABILITY");
        Assert.Equal(1, p.powers[(int)powertype_t.pw_invulnerability]);

        p.weaponowned[(int)weapontype_t.wp_chainsaw] = false;
        Assert.Equal([World.STSTR_CHOPPERS], Select(flow, "CHAINSAW"));
        Assert.True(p.weaponowned[(int)weapontype_t.wp_chainsaw]);
        Assert.StartsWith("ang=0x", Select(flow, "SHOW POSITION").Single(), StringComparison.Ordinal);
        Assert.Equal("cheats", menu.StateName); // the page stays up
    }

    [Fact]
    public void KeysTypedBeforeDontSpoilThePagesCheat()
    {
        (GameFlow flow, _, World world) = Level();
        flow.ST_Responder('i');
        flow.ST_Responder('d');
        flow.ST_Responder('c'); // idc… under way, and idclev's parameter
        foreach (char c in "idclev1")
            flow.ST_Responder(c);
        OpenPage(flow);
        Assert.Equal([World.STSTR_DQDON], Select(flow, "GOD MODE"));
        Assert.Equal(player_t.CF_GODMODE, Player(world).cheats & player_t.CF_GODMODE);
    }

    [Fact]
    public void TheNoclipItemIsTheMissions()
    {
        (GameFlow flow, _, World world) = Level(mode: GameMode.commercial);
        OpenPage(flow);
        Assert.Equal([World.STSTR_NCON], Select(flow, "NO CLIPPING")); // idclip
        Assert.Equal(player_t.CF_NOCLIP, Player(world).cheats & player_t.CF_NOCLIP);
    }

    [Fact]
    public void OnNightmareOnlyWarpWorks()
    {
        (GameFlow flow, GameFlowTests.Host host, World world) = Level(skill_t.sk_nightmare);
        MMenu menu = OpenPage(flow);
        Assert.Equal(MMenu.CHEATSNIGHTMARE, menu.CheatsHint());
        foreach (string item in new[] { "GOD MODE", "WEAPONS, AMMO, KEYS", "NO CLIPPING", "INVULNERABILITY", "CHAINSAW", "SHOW POSITION", "MUSIC" })
        {
            Assert.Empty(Select(flow, item));
            Assert.Equal(sfxenum_t.sfx_oof, menu.LastSound);
        }
        Assert.Equal(0, Player(world).cheats);
        Assert.False(Player(world).weaponowned[(int)weapontype_t.wp_chainsaw]);
        host.Extra.Add("D_E1M1");
        Assert.Empty(Select(flow, "MUSIC"));
        Assert.Equal(sfxenum_t.sfx_oof, menu.LastSound);

        menu.itemOn = (short)Array.FindIndex(menu.currentMenu.menuitems, i => i.text == "WARP TO");
        Assert.Equal("E1M1", Value(menu, "WARP TO")); // the map played
        menu.M_Responder(Right);
        Assert.Equal("E1M2", Value(menu, "WARP TO"));
        Select(flow, "WARP TO");
        Assert.Equal(("new E1M2", skill_t.sk_nightmare), (host.Loads[^1], host.World!.gameskill));
        Assert.Equal("closed", menu.StateName); // a new game: the menus go
    }

    [Fact]
    public void OffALevelNothingWorks()
    {
        (GameFlow flow, GameFlowTests.Host host, _) = Level();
        flow.D_StartTitle(null);
        MMenu menu = OpenPage(flow);
        Assert.Equal(MMenu.CHEATSLEVELONLY, menu.CheatsHint());
        Select(flow, "GOD MODE");
        Assert.Equal((sfxenum_t.sfx_oof, 0), (menu.LastSound, flow.CheatsPending));
        Select(flow, "WARP TO");
        Assert.Equal((sfxenum_t.sfx_oof, 0, "new E1M1"), (menu.LastSound, flow.CheatsPending, host.Loads[^1]));
    }

    [Fact]
    public void TheWarpPickerOffersTheMapsIdclevTakesAndWraps()
    {
        (GameFlow flow, GameFlowTests.Host host, _) = Level();
        host.Extra.UnionWith(["E1M3", "E1M9", "E2M1"]); // E2M1: not the shareware game's
        Assert.Equal([("11", "E1M1"), ("12", "E1M2"), ("13", "E1M3"), ("19", "E1M9")], flow.CheatMaps());
        MMenu menu = OpenPage(flow);
        menu.itemOn = (short)Array.FindIndex(menu.currentMenu.menuitems, i => i.text == "WARP TO");
        Assert.Equal("E1M1", Value(menu, "WARP TO"));
        menu.M_Responder(Left);
        Assert.Equal("E1M9", Value(menu, "WARP TO"));
        Assert.Equal(sfxenum_t.sfx_stnmov, menu.LastSound);
        menu.M_Responder(Right);
        menu.M_Responder(Right);
        menu.M_Responder(Right);
        Assert.Equal("E1M3", Value(menu, "WARP TO"));
        Select(flow, "WARP TO");
        Assert.Equal(("new E1M3", "closed"), (host.Loads[^1], menu.StateName));

        (GameFlow flow2, GameFlowTests.Host host2, _) = Level(mode: GameMode.commercial);
        host2.Extra.UnionWith(["MAP01", "MAP32", "MAP40", "MAP41"]);
        Assert.Equal([("01", "MAP01"), ("32", "MAP32"), ("40", "MAP40")], flow2.CheatMaps());
    }

    /// <summary>
    /// P7 (SPEC §12 "P7"): a PWAD's MAP33 is idclev33's, the warp picker's and
    /// the Host Game list's, as vanilla's idclev takes it; ExM0 is none of
    /// them (vanilla's idclev refuses map 0) and a new game on map 0 starts
    /// on map 1 (G_InitNew's clamp, -warp's): only --level loads it.
    /// </summary>
    [Fact]
    public void AMap33IsIdclevsAndExM0IsNot()
    {
        (GameFlow doom2, GameFlowTests.Host host2, _) = Level(mode: GameMode.commercial);
        host2.Extra.Add("MAP33");
        Assert.Contains(("33", "MAP33"), doom2.CheatMaps());
        Assert.Contains("MAP33", doom2.HostMaps());
        Assert.Equal((1, 33), doom2.ST_ClevMap(33));

        (GameFlow doom, GameFlowTests.Host host, _) = Level(mode: GameMode.retail);
        host.Extra.UnionWith(["E1M0", "E4M0"]);
        Assert.DoesNotContain(doom.CheatMaps(), m => m.Name.EndsWith("M0", StringComparison.Ordinal));
        Assert.DoesNotContain(doom.HostMaps(), m => m.EndsWith("M0", StringComparison.Ordinal));
        Assert.Null(doom.ST_ClevMap(10));
        Assert.Null(doom.ST_ClevMap(40));
        doom.G_InitNew(skill_t.sk_medium, 1, 0);
        Assert.Equal("new E1M1", host.Loads[^1]);
    }

    [Fact]
    public void TheMusicPickerOffersTheSongsTheWadHasAndPlaysOne()
    {
        (GameFlow flow, GameFlowTests.Host host, World world) = Level(map: "E1M2");
        host.Extra.UnionWith(["D_E1M1", "D_E1M2", "D_E1M9", "D_E2M1"]); // E2M1's: idmus takes it, the WAD has it (vanilla's shareware too)
        var songs = new List<musicenum_t>();
        flow.Sound = new SSound(null) { MusicChanged = c => songs.Add(c.musicnum) };
        Assert.Equal([("11", "E1M1"), ("12", "E1M2"), ("19", "E1M9"), ("21", "E2M1"), ("42", "INTRO")], flow.CheatSongs()); // "20" is E1M9's again; the synthetic IWAD has D_INTRO
        MMenu menu = OpenPage(flow);
        Assert.Equal("E1M2", Value(menu, "MUSIC")); // the map played's
        menu.itemOn = (short)Array.FindIndex(menu.currentMenu.menuitems, i => i.text == "MUSIC");
        menu.M_Responder(Right);
        Assert.Equal("E1M9", Value(menu, "MUSIC"));
        Assert.Equal([World.STSTR_MUS], Select(flow, "MUSIC"));
        Assert.Equal(musicenum_t.mus_e1m9, songs[^1]);
        Assert.Equal("cheats", menu.StateName);
        Assert.NotNull(world);
    }

    private sealed class Automap(AmMap am) : ICheatAutomap
    {
        public bool AutomapShown => am.automapactive;
        public int AutomapCheating => am.cheating;
        public bool TypeAutomapCheat(string keys) => am.TypeCheat(keys);
    }

    [Fact]
    public void MapDetailIsTheAutomapsIddtWhileItShows()
    {
        (GameFlow flow, _, World world) = Level();
        var wad = new WadArchive([WadFile.FromBytes(Tools.SyntheticIwad.SyntheticIwad.Build(), Tools.SyntheticIwad.SyntheticIwad.DefaultFileName)]);
        var am = new AmMap(wad) { World = world };
        flow.AutomapCheats = new Automap(am);
        MMenu menu = OpenPage(flow);
        Assert.Equal("MAP CLOSED", Value(menu, "MAP DETAIL"));
        Select(flow, "MAP DETAIL");
        Assert.Equal((sfxenum_t.sfx_oof, 0), (menu.LastSound, am.cheating)); // closed: the automap doesn't read it
        am.AM_Responder(AmMap.evtype_t.ev_keydown, am.key_map_toggle);
        am.AM_Responder(AmMap.evtype_t.ev_keydown, 0, 'i'); // a key typed before doesn't spoil it
        Assert.Equal("OFF", Value(menu, "MAP DETAIL"));
        foreach (string step in new[] { "LINES", "THINGS", "OFF" })
        {
            Select(flow, "MAP DETAIL");
            Assert.Equal((sfxenum_t.sfx_pistol, step), (menu.LastSound, Value(menu, "MAP DETAIL")));
        }
        Assert.Equal(0, flow.CheatsPending); // presentation only: no ticcmd
        Assert.Equal(0, Player(world).cheats);
    }
}
