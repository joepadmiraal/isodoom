using System.Collections.Generic;
using System.Linq;
using IsoDoom.Audio;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Wad;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T7.9a: m_cheat.c's sequences (<see cref="cheatseq_t"/>, scrambled as
/// vanilla's) and st_stuff.c's cheats (<see cref="MCheat"/>,
/// <see cref="World.ST_Cheat"/>, the game flow's <c>idclev</c> and
/// <c>idmus</c>) typed through the game flow (<see cref="GameFlow.ST_Responder"/>)
/// on the synthetic IWAD's maps: each goes in the next tic's <c>ticcmd</c>
/// and acts before it, with vanilla's message and refusals.
/// </summary>
public class CheatTests
{
    // ---- the sequences ----

    [Fact]
    public void TheSequencesAreVanillasCheatsScrambled()
    {
        var c = new MCheat();
        Assert.Equal("idmus##", c.cheat_mus.Text());
        Assert.Equal("idchoppers", c.cheat_choppers.Text());
        Assert.Equal("iddqd", c.cheat_god.Text());
        Assert.Equal("idkfa", c.cheat_ammo.Text());
        Assert.Equal("idfa", c.cheat_ammonokey.Text());
        Assert.Equal("idspispopd", c.cheat_noclip.Text());
        Assert.Equal("idclip", c.cheat_commercial_noclip.Text());
        Assert.Equal(["idbeholdv", "idbeholds", "idbeholdi", "idbeholdr", "idbeholda", "idbeholdl", "idbehold"], c.cheat_powerup.Select(s => s.Text()));
        Assert.Equal("idclev##", c.cheat_clev.Text());
        Assert.Equal("idmypos", c.cheat_mypos.Text());
        // SCRAMBLE is a permutation of the bytes
        Assert.Equal(256, Enumerable.Range(0, 256).Select(cheatseq_t.SCRAMBLE).Distinct().Count());
    }

    private static bool Type(cheatseq_t seq, string keys)
    {
        bool rc = false;
        foreach (char k in keys)
            rc = seq.cht_CheckCheat((byte)k);
        return rc;
    }

    [Fact]
    public void CheckCheatMatchesAsVanillas()
    {
        var c = new MCheat();
        Assert.True(Type(c.cheat_god, "iddqd"));
        Assert.True(Type(c.cheat_god, "xyziddqd")); // after other keys
        Assert.False(Type(c.cheat_god, "iiddqd")); // a wrong key starts again without being the first letter
        Assert.Equal(0, c.cheat_god.p);
        Assert.False(Type(c.cheat_god, "iddq"));
        Assert.True(Type(c.cheat_god, "d"));
        Assert.False(Type(c.cheat_god, "IDDQD")); // upper case is another key code
        Assert.True(Type(c.cheat_god, "iddqd")); // matched again
    }

    [Fact]
    public void AParameterCheatTakesAnyTwoKeysAndGivesThemBack()
    {
        var c = new MCheat();
        Assert.False(Type(c.cheat_clev, "idclev1"));
        Assert.True(Type(c.cheat_clev, "x"));
        Assert.Equal("1x"u8.ToArray(), c.cheat_clev.cht_GetParam());
        Assert.Equal("idclev##", c.cheat_clev.Text()); // cleared
        Assert.True(Type(c.cheat_clev, "idclev24"));
        Assert.Equal("24"u8.ToArray(), c.cheat_clev.cht_GetParam());
    }

    [Fact]
    public void CheckFindsEachCheatWithItsDigitsAndReadsOnlyTheAllowedOnes()
    {
        var c = new MCheat();
        var matched = new List<(cheat_t, byte)>();
        void Keys(string keys, bool allowed = true, GameMission mission = GameMission.doom)
        {
            matched.Clear();
            foreach (char k in keys)
                c.Check(MCheat.KeyCode(k), allowed, true, mission, matched);
        }

        Keys("idmus12");
        Assert.Equal([(cheat_t.cht_mus, (byte)12)], matched);
        Keys("idmusa1");
        Assert.Equal([(cheat_t.cht_mus, MCheat.NOTDIGITS)], matched);
        Keys("idclev19");
        Assert.Equal([(cheat_t.cht_clev, (byte)19)], matched);
        Keys("idbeholds");
        Assert.Equal([(cheat_t.cht_behold, (byte)0), (cheat_t.cht_powerup_s, (byte)0)], matched);
        Keys("idclip"); // Doom's noclip is idspispopd
        Assert.Empty(matched);
        Keys("idspispopd");
        Assert.Equal([(cheat_t.cht_noclip, (byte)0)], matched);
        Keys("idclip", mission: GameMission.doom2);
        Assert.Equal([(cheat_t.cht_noclip, (byte)0)], matched);
        Keys("idchoppersidmyposidkfaidfaiddqd");
        Assert.Equal([cheat_t.cht_choppers, cheat_t.cht_mypos, cheat_t.cht_ammo, cheat_t.cht_ammonokey, cheat_t.cht_god], matched.Select(m => m.Item1));
        // on nightmare only idclev reads the keys
        Keys("iddqdidclev12", allowed: false);
        Assert.Equal([(cheat_t.cht_clev, (byte)12)], matched);
        Assert.Equal(0, c.cheat_god.p);
        // a key that is no letter or digit starts every sequence again
        Keys("idd");
        Assert.Equal(3, c.Typed());
        c.Check(MCheat.KeyCode(MMenu.KEY_ENTER), true, true, GameMission.doom, matched);
        Assert.Equal(0, c.Typed());
    }

    // ---- through the game flow ----

    private static (GameFlow Flow, GameFlowTests.Host Host, World World) Level(skill_t skill = skill_t.sk_medium,
        GameMode mode = GameMode.shareware, GameMission mission = GameMission.none)
    {
        var host = new GameFlowTests.Host(mode);
        var flow = new GameFlow(host, mode, new DoomRandom(), GameVariant.vanilla, mission);
        flow.G_InitNewMap(skill, "E1M1");
        flow.G_Ticker(default);
        return (flow, host, host.World!);
    }

    /// <summary>Types <paramref name="keys"/> and runs a tic of <paramref name="cmd"/>, then one per cheat still waiting; returns the messages they left.</summary>
    private static List<string> Cheat(GameFlow flow, string keys, ticcmd_t cmd = default)
    {
        foreach (char k in keys)
            flow.ST_Responder(k);
        var events = new List<sim_event_t>();
        flow.G_Ticker(cmd);
        flow.World?.DrainEvents(events);
        while (cmd.buttons == 0 && flow.CheatsPending > 0)
        {
            flow.G_Ticker(default);
            flow.World?.DrainEvents(events);
        }
        return [.. events.Where(e => e.type == simevent_t.se_message).Select(e => e.message!)];
    }

    private static player_t Player(World world) => world.players[world.consoleplayer];

    [Fact]
    public void IddqdTogglesGodModeAndHealsToAHundred()
    {
        (GameFlow flow, _, World world) = Level();
        player_t p = Player(world);
        p.health = p.mo!.health = 30;
        Assert.Equal([World.STSTR_DQDON], Cheat(flow, "iddqd"));
        Assert.Equal((player_t.CF_GODMODE, 100, 100), (p.cheats & player_t.CF_GODMODE, p.health, p.mo.health));
        Assert.Equal([World.STSTR_DQDOFF], Cheat(flow, "iddqd"));
        Assert.Equal(0, p.cheats & player_t.CF_GODMODE);
    }

    [Fact]
    public void IdkfaAndIdfaGiveTheArsenal()
    {
        (GameFlow flow, _, World world) = Level();
        player_t p = Player(world);
        Assert.Equal([World.STSTR_FAADDED], Cheat(flow, "idfa"));
        Assert.All(p.weaponowned, Assert.True);
        Assert.Equal(p.maxammo, p.ammo);
        Assert.Equal((200, 2), (p.armorpoints, p.armortype));
        Assert.All(p.cards, Assert.False);
        Assert.Equal([World.STSTR_KFAADDED], Cheat(flow, "idkfa"));
        Assert.All(p.cards, Assert.True);
    }

    [Fact]
    public void TheNoclipCheatIsTheMissions()
    {
        (GameFlow flow, _, World world) = Level();
        Assert.Empty(Cheat(flow, "idclip"));
        Assert.Equal([World.STSTR_NCON], Cheat(flow, "idspispopd"));
        Assert.Equal(player_t.CF_NOCLIP, Player(world).cheats & player_t.CF_NOCLIP);
        Assert.True((Player(world).mo!.flags & mobjflag_t.MF_NOCLIP) != 0); // P_PlayerThink's
        Assert.Equal([World.STSTR_NCOFF], Cheat(flow, "idspispopd"));

        (flow, _, _) = Level(mission: GameMission.doom2);
        Assert.Empty(Cheat(flow, "idspispopd"));
        Assert.Equal([World.STSTR_NCON], Cheat(flow, "idclip"));
    }

    [Fact]
    public void IdbeholdTogglesThePowerUps()
    {
        (GameFlow flow, _, World world) = Level();
        player_t p = Player(world);
        Assert.Equal([World.STSTR_BEHOLD], Cheat(flow, "idbehold"));
        Cheat(flow, "v");
        Assert.Equal(World.INVULNTICS - 1, p.powers[(int)powertype_t.pw_invulnerability]); // given, then a tic ran
        Assert.Equal([World.STSTR_BEHOLD, World.STSTR_BEHOLDX], Cheat(flow, "idbeholdv"));
        Assert.Equal(0, p.powers[(int)powertype_t.pw_invulnerability]); // set to 1: it ran out in the tic
        p.health = p.mo!.health = 40;
        Assert.Equal([World.STSTR_BEHOLD, World.STSTR_BEHOLDX], Cheat(flow, "idbeholds"));
        Assert.Equal((2, 100), (p.powers[(int)powertype_t.pw_strength], p.health)); // given 1, counting up
        Cheat(flow, "idbeholds");
        Assert.Equal(0, p.powers[(int)powertype_t.pw_strength]);
        Cheat(flow, "idbeholdi");
        Assert.True((p.mo.flags & mobjflag_t.MF_SHADOW) != 0);
        Cheat(flow, "idbeholda");
        Assert.Equal(1, p.powers[(int)powertype_t.pw_allmap]);
        Cheat(flow, "idbeholdl");
        Assert.True(p.powers[(int)powertype_t.pw_infrared] > 0);
        Cheat(flow, "idbeholdr");
        Assert.True(p.powers[(int)powertype_t.pw_ironfeet] > 0);
    }

    [Fact]
    public void IdchoppersAndIdmypos()
    {
        (GameFlow flow, _, World world) = Level();
        player_t p = Player(world);
        Assert.Equal([World.STSTR_CHOPPERS], Cheat(flow, "idchoppers"));
        Assert.True(p.weaponowned[(int)weapontype_t.wp_chainsaw]);
        Assert.Equal(0, p.powers[(int)powertype_t.pw_invulnerability]); // vanilla's "true": a tic

        mobj_t mo = p.mo!;
        string expected = $"ang=0x{mo.angle:x};x,y=(0x{(uint)mo.x:x},0x{(uint)mo.y:x})";
        Assert.Equal([expected], Cheat(flow, "idmypos"));
    }

    [Fact]
    public void TheCheatGoesInTheNextTicsTiccmdUnlessItHasASpecialButton()
    {
        (GameFlow flow, _, World world) = Level();
        var pause = new ticcmd_t { buttons = buttoncode_t.BT_SPECIAL | buttoncode_t.BTS_PAUSE };
        Assert.Empty(Cheat(flow, "iddqd", pause)); // the pause goes first
        Assert.True(flow.paused);
        Assert.Equal(1, flow.CheatsPending);
        int tic = world.leveltime;
        // paused: the cheat works, its message shows, the world holds
        Assert.Equal([World.STSTR_DQDON], Cheat(flow, ""));
        Assert.Equal((tic, 0), (world.leveltime, flow.CheatsPending));
        // the encoding
        var cmd = new ticcmd_t { forwardmove = 25, buttons = buttoncode_t.BT_ATTACK };
        World.ST_CheatTiccmd(ref cmd, cheat_t.cht_clev, 19);
        Assert.Equal((cheat_t.cht_clev, (byte)19, (sbyte)25), (World.ST_TiccmdCheat(cmd), cmd.chatchar, cmd.forwardmove));
        Assert.Equal(cheat_t.cht_none, World.ST_TiccmdCheat(pause));
    }

    [Fact]
    public void OnNightmareOnlyIdclevWorks()
    {
        (GameFlow flow, GameFlowTests.Host host, World world) = Level(skill_t.sk_nightmare);
        Assert.Empty(Cheat(flow, "iddqdidkfaidbeholdvidchoppers"));
        Assert.Equal(0, Player(world).cheats);
        Assert.False(Player(world).weaponowned[(int)weapontype_t.wp_chainsaw]);
        Cheat(flow, "idclev12");
        Assert.Equal(("new E1M2", skill_t.sk_nightmare), (host.Loads[^1], host.World!.gameskill));
    }

    [Fact]
    public void IdclevStartsANewGameOnTheMapBeforeTheTic()
    {
        (GameFlow flow, GameFlowTests.Host host, World world) = Level(skill_t.sk_hard);
        Player(world).cheats |= player_t.CF_GODMODE;
        Cheat(flow, "idclev12");
        Assert.Equal(["new E1M1", "new E1M2"], host.Loads);
        World next = host.World!;
        Assert.NotSame(world, next);
        Assert.Equal(("E1M2", skill_t.sk_hard, 1, 0), (next.level.Name, next.gameskill, next.leveltime, Player(next).cheats));
        Assert.Equal(gamestate_t.GS_LEVEL, flow.gamestate);

        // refused, silently: another episode or map 0 on the shareware, a map the WAD lacks, not digits
        foreach (string keys in new[] { "idclev21", "idclev10", "idclev19", "idclev1x", "idclev00" })
        {
            Assert.Empty(Cheat(flow, keys));
            Assert.Equal(2, host.Loads.Count);
        }
    }

    [Theory]
    [InlineData(GameMode.registered, "idclev39", "E3M9")]
    [InlineData(GameMode.registered, "idclev41", null)]
    [InlineData(GameMode.retail, "idclev41", "E4M1")]
    [InlineData(GameMode.retail, "idclev51", null)]
    [InlineData(GameMode.commercial, "idclev32", "MAP32")]
    [InlineData(GameMode.commercial, "idclev40", "MAP40")]
    [InlineData(GameMode.commercial, "idclev41", null)]
    public void IdclevTakesTheGameModesMaps(GameMode mode, string keys, string? expected)
    {
        (GameFlow flow, GameFlowTests.Host host, _) = Level(mode: mode);
        host.Extra.UnionWith(["E3M9", "E4M1", "E5M1", "MAP32", "MAP40", "MAP41"]);
        Cheat(flow, keys);
        Assert.Equal(expected is null ? "new E1M1" : "new " + expected, host.Loads[^1]);
    }

    [Theory]
    [InlineData(GameMode.shareware, "idmus11", musicenum_t.mus_e1m1)]
    [InlineData(GameMode.shareware, "idmus19", musicenum_t.mus_e1m9)]
    [InlineData(GameMode.retail, "idmus39", musicenum_t.mus_e3m9)]
    [InlineData(GameMode.retail, "idmus45", musicenum_t.mus_introa)] // vanilla's 32nd song
    [InlineData(GameMode.retail, "idmus46", null)]
    [InlineData(GameMode.retail, "idmus10", null)] // vanilla's I_Error
    [InlineData(GameMode.retail, "idmus00", null)]
    [InlineData(GameMode.retail, "idmus1a", null)]
    [InlineData(GameMode.commercial, "idmus01", musicenum_t.mus_runnin)]
    [InlineData(GameMode.commercial, "idmus32", musicenum_t.mus_ultima)]
    [InlineData(GameMode.commercial, "idmus00", musicenum_t.mus_introa)] // mus_runnin - 1, as vanilla's
    [InlineData(GameMode.commercial, "idmus36", null)]
    public void IdmusChangesTheSongOrSaysImpossible(GameMode mode, string keys, musicenum_t? expected)
    {
        (GameFlow flow, _, World world) = Level(mode: mode);
        var songs = new List<musicenum_t>();
        flow.Sound = new SSound(null) { MusicChanged = c => songs.Add(c.musicnum) };
        Assert.Equal(expected is null ? [World.STSTR_NOMUS] : [World.STSTR_MUS], Cheat(flow, keys));
        Assert.Equal(expected is { } song ? [song] : [], songs);
        Assert.NotNull(world);
    }

    [Fact]
    public void NoCheatOffTheLevel()
    {
        (GameFlow flow, GameFlowTests.Host host, _) = Level();
        flow.D_StartTitle(null);
        Assert.Empty(flow.ST_Responder('i'));
        Assert.Equal(0, flow.Cheats.Typed());
        Assert.Null(host.World);
    }
}
