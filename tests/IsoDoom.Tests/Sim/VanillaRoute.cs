using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Sim;

/// <summary>
/// T4.8: a route, a <c>ticcmd</c> sequence (input only) played by vanilla
/// (<c>tools/VanillaRef/routes.sh</c>, as a v1.9 demo) and by the sim, compared tic by tic.
/// <para>
/// A <c>.route</c> file (in <c>Routes/</c>) is text: <c>#</c> starts a
/// comment; header lines <c>iwad synthetic|doom1|testmap</c> (required),
/// <c>map ExMy</c> (default <c>E1M1</c>; for <c>testmap</c> the name of a
/// <see cref="RouteTestMaps"/> map, required, T4.8a), <c>skill 1-5</c> (default 3) and
/// <c>start X Y ANGLE</c> (T5.6: player 1 starts at map point X, Y facing ANGLE
/// degrees instead of at its map start: <see cref="RouteStart.Place"/>) and
/// <c>exit normal|secret</c> (T5.9: the route's last tic, and no other, leaves the
/// level by its exit or secret exit, in vanilla and the sim);
/// then one line per <c>ticcmd</c>: <c>FORWARD SIDE TURN BUTTONS [xCOUNT]</c>,
/// <c>forwardmove</c> and <c>sidemove</c> as signed bytes, <c>TURN</c> the
/// demo's signed angleturn byte (<c>angleturn = TURN &lt;&lt; 8</c>: a demo's
/// angleturn is 8 bits wide), <c>xCOUNT</c> repeats the line. The game has no
/// monsters (the demo's <c>nomonsters</c>) unless the header has a line
/// <c>monsters</c> (T6.4), and player 1 alone. Between the ticcmds (T6.4),
/// <c>damage X Y AMOUNT</c> (player 1 hurts the live shootable thing spawned
/// at map point X, Y by AMOUNT, as a shot would) and <c>alert</c> (player 1
/// makes a noise, as a shot would) and (T6.5) <c>rocket</c> (player 1 fires
/// a rocket, <c>P_SpawnPlayerMissile</c>) run at the start of the next tic, before
/// the players think (<see cref="RouteEvent"/>): stand-ins for the player's
/// shots from before its weapons were ported (T6.6; <c>BT_ATTACK</c> fires
/// them since, and <c>BT_CHANGE</c> changes them). A route may go through the
/// player's death and reborn (T6.12: use when dead reloads the map,
/// <see cref="Reborn"/>): the events still count by the route's tic, and the
/// <c>start</c> header places the player again on the reloaded map.
/// </para>
/// <para>
/// The reference dump (<c>NAME.vanilla</c>; dump.c) has one line per tic,
/// after the tic: <c>leveltime</c>, the <c>ticcmd</c> vanilla read
/// (<c>forwardmove sidemove angleturn buttons</c>), then player 1's mobj
/// <c>x y z momx momy momz angle</c>, <c>viewz</c>, the <c>P_Random</c>
/// index, and the mobj's <c>state</c> and <c>tics</c>, then (T5.3) the
/// <c>sectors</c> whose floor or ceiling height differs from the map's
/// <c>SECTORS</c> lump, as <c>SECTOR:FLOOR:CEILING</c> (fixed_t) joined by
/// commas, or <c>-</c> for none (doors, lifts), then (T5.4) the
/// <c>textures</c> of sidedefs that differ from the map's <c>SIDEDEFS</c>
/// lump, as <c>SIDE:PART:NAME</c> (<c>PART</c> <c>t</c>, <c>m</c> or
/// <c>b</c>) joined by commas, or <c>-</c> for none (switches), then (T5.6) the
/// teleport <c>fogs</c> (<c>MT_TFOG</c> mobjs) in thinker order as
/// <c>X:Y:Z:STATE</c> joined by commas, or <c>-</c> for none, then (T5.7) the
/// sectors whose light level differs from the map's <c>SECTORS</c> lump, as
/// <c>SECTOR:LIGHT</c> joined by commas, or <c>-</c> for none (the light
/// specials), then (T5.8) the player's <c>health</c>, its mobj's health
/// (<c>mohealth</c>), <c>armorpoints</c>, <c>armortype</c>, <c>cards</c>
/// (bit <c>i</c> for <c>card_t</c> <c>i</c>) and <c>secretcount</c>, then (T6.1)
/// the <c>inventory</c>, <c>ITEMCOUNT:CLIP:SHELL:CELL:MISL:OWNED:BACKPACK</c>
/// (<c>OWNED</c>: bit <c>i</c> for <c>weapontype_t</c> <c>i</c>), and the mobjs' states, <c>things</c>: <c>COUNT:HASH</c> over every mobj in
/// thinker order (<see cref="ThingsHash"/>: every map thing animates as in
/// vanilla), then (T6.6) the <c>weapon</c>: <c>READY:PENDING:EXTRALIGHT:REFIRE:ATTACKDOWN</c>
/// and the weapon's and the flash's psprite as <c>STATE:TICS:SX:SY</c>
/// (<see cref="Weapon"/>), then (T6.8) the <c>powers</c>:
/// <c>INVULN:STRENGTH:INVIS:IRONFEET:ALLMAP:INFRARED:DAMAGECOUNT:BONUSCOUNT:FIXEDCOLORMAP:PALETTE</c>
/// (<see cref="Powers"/>; the palette vanilla's <c>ST_doPaletteStuff</c> sets after the tic), then (T6.10)
/// the <c>sounds</c>: every <c>S_StartSound</c> call of the tic and <c>P_RemoveMobj</c>'s
/// <c>S_StopSound</c>, in order (<see cref="Sounds"/>), then (T6.11) the <c>hud</c>
/// after <c>ST_Ticker</c> and <c>HU_Ticker</c>:
/// <c>FACE:FACECOUNT:KEY0:KEY1:KEY2:READY:MSGON:MSGCOUNTER:TEXT:PIXELS</c>
/// (<see cref="Hud"/>: the status bar's face and its count, key boxes and
/// ready ammo, the message line's state and text hash, and the hash of the
/// pixels both draw; the pixels are compared only where the test has
/// DOOM1.WAD's graphics, <see cref="HudGraphics"/>), then (T5.9)
/// <c>exit</c>: 0, or 1 (2) when the tic left the level by its exit (secret
/// exit), i.e. <c>gameaction</c> is <c>ga_completed</c>. For the synthetic
/// IWAD and the test maps it is committed beside the route (generated content); for DOOM1.WAD
/// it is WAD-derived and lives in
/// <see cref="DumpDirEnvVar"/> (default <c>~/.cache/isodoom/vanilla-routes</c>).
/// </para>
/// </summary>
public sealed class VanillaRoute
{
    /// <summary>The directory of DOOM1.WAD's route dumps (routes.sh writes them there).</summary>
    public const string DumpDirEnvVar = "ISODOOM_VANILLA_ROUTES";

    /// <summary>The columns of a dump line.</summary>
    public static readonly string[] Columns =
        ["leveltime", "forwardmove", "sidemove", "angleturn", "buttons", "x", "y", "z", "momx", "momy", "momz", "angle", "viewz", "prndindex", "state", "tics", "sectors", "textures", "fogs", "lights", "health", "mohealth", "armorpoints", "armortype", "cards", "secretcount", "inventory", "things", "weapon", "powers", "sounds", "hud", "exit"];

    public string Name { get; }
    public string Path { get; }
    /// <summary><c>synthetic</c>, <c>doom1</c> or <c>testmap</c>.</summary>
    public string Iwad { get; }
    public string Map { get; }
    public skill_t Skill { get; }
    /// <summary>The <c>start</c> header (map units, degrees), or null for the map's player 1 start (T5.6).</summary>
    public (int X, int Y, int Angle)? Start { get; }
    /// <summary>The <c>exit</c> header (T5.9): 1 for <c>normal</c>, 2 for <c>secret</c>, 0 without one (the dump's <c>exit</c> column).</summary>
    public int Exit { get; }
    public IReadOnlyList<ticcmd_t> Cmds { get; }
    /// <summary>The parsed file: the <c>monsters</c> header and the events (T6.4).</summary>
    public RouteFile File { get; }
    /// <summary>The <c>monsters</c> header (T6.4).</summary>
    public bool Monsters => File.Monsters;

    private VanillaRoute(string path, string iwad, string map, skill_t skill, (int, int, int)? start, int exit, IReadOnlyList<ticcmd_t> cmds, RouteFile file)
    {
        File = file;
        Start = start;
        Exit = exit;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Path = path;
        Iwad = iwad;
        Map = map;
        Skill = skill;
        Cmds = cmds;
    }

    /// <summary>The routes directory (<c>tests/IsoDoom.Tests/Sim/Routes</c>).</summary>
    public static string Dir =>
        System.IO.Path.Combine(TestWads.RepoRoot ?? throw new InvalidOperationException("Repo root not found."),
            "tests", "IsoDoom.Tests", "Sim", "Routes");

    /// <summary>The names of every route in <see cref="Dir"/>, sorted.</summary>
    public static IEnumerable<string> Names() =>
        Directory.GetFiles(Dir, "*.route").Select(p => System.IO.Path.GetFileNameWithoutExtension(p)).Order(StringComparer.Ordinal);

    public static VanillaRoute Load(string name) => Parse(System.IO.Path.Combine(Dir, name + ".route"));

    /// <summary>
    /// T6.13: DOOM1.WAD's demo lump <paramref name="lump"/> (<c>DEMO1</c>-<c>DEMO3</c>) as a
    /// route (<see cref="RouteFile.FromDemo"/>, the vanilla-input adapter),
    /// named after the lump in lower case: its dump is <c>demo1.vanilla</c> in
    /// <see cref="DumpDirEnvVar"/>, written by <c>tools/VanillaRef/demos.sh</c>.
    /// Skips without DOOM1.WAD.
    /// </summary>
    public static VanillaRoute Demo(string lump)
    {
        var wad = WadArchive.Open(TestWads.RequireDoom1());
        var route = RouteFile.FromDemo(wad.W_CacheLumpName(lump).Span, lump);
        return new VanillaRoute(lump.ToLowerInvariant(), "doom1", route.Map!, (skill_t)(route.Skill - 1), null, 0, route.Cmds, route);
    }

    /// <summary>Parses a route file with the game's <see cref="RouteFile"/> (T5.10: the level script plays routes too) and checks its header.</summary>
    public static VanillaRoute Parse(string path)
    {
        var route = RouteFile.Parse(path);
        string? iwad = route.Iwad, map = route.Map;
        if (iwad is not ("synthetic" or "doom1" or "testmap"))
            throw new FormatException($"{path}: needs \"iwad synthetic|doom1|testmap\" and a skill of 1-5");
        if (iwad == "testmap")
        {
            if (map is null || !RouteTestMaps.Maps.ContainsKey(map))
                throw new FormatException($"{path}: \"iwad testmap\" needs \"map NAME\" of RouteTestMaps.Maps");
        }
        else
        {
            map = (map ?? "E1M1").ToUpperInvariant();
        }
        return new VanillaRoute(path, iwad, map, (skill_t)(route.Skill - 1), route.Start, route.Exit, route.Cmds, route);
    }

    /// <summary>
    /// The reference dump's lines: the committed one for the synthetic IWAD,
    /// DOOM1.WAD's from <see cref="DumpDirEnvVar"/> (skips the test when DOOM1.WAD or the dump is absent).
    /// </summary>
    public string[] Reference()
    {
        string path;
        if (Iwad != "doom1")
        {
            path = System.IO.Path.ChangeExtension(Path, ".vanilla");
        }
        else
        {
            TestWads.RequireDoom1();
            string? dir = Environment.GetEnvironmentVariable(DumpDirEnvVar);
            if (string.IsNullOrEmpty(dir))
                dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "isodoom", "vanilla-routes");
            path = System.IO.Path.Combine(dir, Name + ".vanilla");
            if (!System.IO.File.Exists(path))
                Assert.Skip($"No vanilla dump {path}: run tools/VanillaRef/routes.sh (or set {DumpDirEnvVar}).");
        }
        return [.. System.IO.File.ReadAllLines(path).Where(l => l.Length > 0)];
    }

    /// <summary>A new game on the route's map with every tweak off (<see cref="Tweaks.Vanilla"/>), as the demo starts it.</summary>
    public World NewWorld() => NewWorld(out _);

    /// <summary><see cref="NewWorld()"/>, with the route's WAD (<see cref="Reborn"/> reloads the map from it).</summary>
    public World NewWorld(out WadArchive wad)
    {
        wad = Iwad switch
        {
            "synthetic" => new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]),
            "testmap" => new WadArchive([WadFile.FromBytes(RouteTestMaps.Get(Map).Build(), Map + ".wad")]),
            _ => WadArchive.Open(TestWads.RequireDoom1()),
        };
        var world = new World(new SpawnSettings(GameMode.shareware, Skill, nomonsters: !Monsters), Tweaks.Vanilla)
        {
            textures = wad.W_CheckNumForName("TEXTURE1") >= 0 ? Textures.R_InitTextures(wad) : null,
        };
        world.P_InitPicAnims(world.textures, PicAnims.FlatNames(wad)); // P_Init's (T5.7)
        world.G_DoLoadLevel(Level.Load(wad, MapLump));
        if (Start is { } s)
            RouteStart.Place(world, s.X, s.Y, s.Angle);
        return world;
    }

    /// <summary>The route's map lump (a test map's is <c>E1M1</c>).</summary>
    public string MapLump => Iwad == "testmap" ? "E1M1" : Map;

    /// <summary>
    /// T6.12: the game action of a reborn (<see cref="gameaction_t.ga_loadlevel"/>,
    /// <see cref="World.G_DoReborn"/>), as vanilla's <c>G_Ticker</c> runs it
    /// before the next tic: the map afresh from <paramref name="wad"/>
    /// (<see cref="World.G_DoLoadLevel"/>), and the <c>start</c> header
    /// again (dump.c's <c>dump_pretic</c> places the player whenever
    /// <c>leveltime</c> is 0). Returns whether there was one.
    /// </summary>
    public bool Reborn(World world, WadArchive wad)
    {
        if (world.gameaction != gameaction_t.ga_loadlevel)
            return false;
        world.G_DoLoadLevel(Level.Load(wad, MapLump));
        if (Start is { } s)
            RouteStart.Place(world, s.X, s.Y, s.Angle);
        return true;
    }

    /// <summary>
    /// The sim's state after a tic, as a dump line; <paramref name="mapHeights"/>
    /// are the sectors' floor and ceiling heights as the map has them
    /// (<see cref="MapHeights"/>), <paramref name="mapTextures"/> the sidedefs'
    /// textures (<see cref="MapTextures"/>), <paramref name="mapLights"/> the
    /// sectors' light levels (<see cref="MapLights"/>).
    /// </summary>
    public static string Line(World world, (int Floor, int Ceiling)[] mapHeights, string[] mapTextures, short[] mapLights, string hud)
    {
        player_t p = world.players[world.consoleplayer];
        mobj_t mo = p.mo!;
        ticcmd_t c = p.cmd;
        string fields = string.Join(' ', new long[]
        {
            world.leveltime, c.forwardmove, c.sidemove, c.angleturn, c.buttons,
            mo.x, mo.y, mo.z, mo.momx, mo.momy, mo.momz, mo.angle, p.viewz, world.random.prndindex, (long)mo.state, mo.tics,
        }.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        var moved = new List<string>();
        for (int i = 0; i < world.sectors.Length; i++)
        {
            sector_t sec = world.sectors[i];
            if (sec.floorheight != mapHeights[i].Floor || sec.ceilingheight != mapHeights[i].Ceiling)
                moved.Add(string.Create(CultureInfo.InvariantCulture, $"{i}:{sec.floorheight}:{sec.ceilingheight}"));
        }
        string[] textures = MapTextures(world);
        var changed = new List<string>();
        for (int i = 0; i < textures.Length; i++)
        {
            if (!string.Equals(textures[i], mapTextures[i], StringComparison.OrdinalIgnoreCase))
                changed.Add(string.Create(CultureInfo.InvariantCulture, $"{i / 3}:{"tmb"[i % 3]}:{textures[i].ToUpperInvariant()}"));
        }
        var fogs = new List<string>();
        foreach (mobj_t m in world.Mobjs())
        {
            if (m.type == mobjtype_t.MT_TFOG)
                fogs.Add(string.Create(CultureInfo.InvariantCulture, $"{m.x}:{m.y}:{m.z}:{(int)m.state}"));
        }
        var lights = new List<string>();
        for (int i = 0; i < world.sectors.Length; i++)
        {
            if (world.sectors[i].lightlevel != mapLights[i])
                lights.Add(string.Create(CultureInfo.InvariantCulture, $"{i}:{world.sectors[i].lightlevel}"));
        }
        int cards = 0;
        for (int i = 0; i < p.cards.Length; i++)
            cards |= p.cards[i] ? 1 << i : 0;
        string status = string.Join(' ', new[] { p.health, mo.health, p.armorpoints, p.armortype, cards, p.secretcount }
            .Select(v => v.ToString(CultureInfo.InvariantCulture)));
        int exit = world.gameaction == gameaction_t.ga_completed ? world.secretexit ? 2 : 1 : 0;
        return fields + " " + (moved.Count == 0 ? "-" : string.Join(',', moved)) + " " + (changed.Count == 0 ? "-" : string.Join(',', changed))
            + " " + (fogs.Count == 0 ? "-" : string.Join(',', fogs)) + " " + (lights.Count == 0 ? "-" : string.Join(',', lights))
            + " " + status + " " + Inventory(p) + " " + ThingsHash(world) + " " + Weapon(p) + " " + Powers(p) + " " + Sounds(world)
            + " " + hud + " " + exit.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The dump's <c>weapon</c> column (T6.6): <c>READY:PENDING:EXTRALIGHT:REFIRE:ATTACKDOWN</c>
    /// and each psprite's <c>STATE:TICS:SX:SY</c> (state −1 for none).
    /// </summary>
    public static string Weapon(player_t p)
    {
        var v = new List<int> { (int)p.readyweapon, (int)p.pendingweapon, p.extralight, p.refire, p.attackdown ? 1 : 0 };
        foreach (pspdef_t psp in p.psprites)
            v.AddRange([psp.state == statenum_t.S_NULL ? -1 : (int)psp.state, psp.tics, psp.sx, psp.sy]);
        return string.Join(':', v.Select(x => x.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// The dump's <c>powers</c> column (T6.8): the six <c>powers</c>
    /// (<c>powertype_t</c> order), <c>damagecount</c>, <c>bonuscount</c>,
    /// <c>fixedcolormap</c> and the palette st_stuff.c's <c>ST_doPaletteStuff</c>
    /// picks (<see cref="StStuff.ST_doPaletteStuff"/>), joined by colons.
    /// </summary>
    public static string Powers(player_t p)
    {
        var v = new List<int>(p.powers) { p.damagecount, p.bonuscount, p.fixedcolormap, StStuff.ST_doPaletteStuff(p) };
        return string.Join(':', v.Select(x => x.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// The dump's <c>sounds</c> column (T6.10): the tic's sound events
    /// (<see cref="World.events"/>: <c>S_StartSound</c> and <c>S_StopSound</c>,
    /// with those of the route's events before the tic) in order, as
    /// <c>NAME@ORIGIN</c> (<c>NAME</c> the <c>sfxenum_t</c> name without
    /// <c>sfx_</c>, or <c>stop</c>; <c>ORIGIN</c> <c>-</c> for none,
    /// <c>sN</c> for sector N, <c>TYPE:X:Y</c> for a mobj, at the event's
    /// position) joined by commas, or <c>-</c> for none. Vanilla's <c>?</c>
    /// (a garbage origin) matches any origin (<see cref="MatchSounds"/>).
    /// </summary>
    public static string Sounds(World world)
    {
        var list = new List<string>();
        foreach (sim_event_t e in world.events)
        {
            if (e.type is not (simevent_t.se_startsound or simevent_t.se_stopsound))
                continue;
            string name = e.type == simevent_t.se_stopsound ? "stop" : e.sound.sfx.ToString()["sfx_".Length..];
            string where = e.sound.sector is { } sec ? "s" + sec.Index.ToString(CultureInfo.InvariantCulture)
                : e.sound.origin is { } mo ? string.Create(CultureInfo.InvariantCulture, $"{(int)mo.type}:{e.x}:{e.y}")
                : "-";
            list.Add(name + "@" + where);
        }
        return list.Count == 0 ? "-" : string.Join(',', list);
    }

    /// <summary>
    /// The sim's <c>sounds</c> column with each entry that vanilla's has as
    /// <c>NAME@?</c> (its garbage origin: p_spec.c's button release, SPEC §12
    /// T5.4) replaced by vanilla's, when the names match: the origin is not compared there.
    /// </summary>
    public static string MatchSounds(string vanilla, string sim)
    {
        if (!vanilla.Contains("@?", StringComparison.Ordinal))
            return sim;
        string[] v = vanilla.Split(','), a = sim.Split(',');
        for (int i = 0; i < Math.Min(v.Length, a.Length); i++)
        {
            if (v[i].EndsWith("@?", StringComparison.Ordinal) && a[i].StartsWith(v[i][..^1], StringComparison.Ordinal))
                a[i] = v[i];
        }
        return string.Join(',', a);
    }

    /// <summary>
    /// The dump's <c>hud</c> column (T6.11), after the status bar's and the
    /// message line's tickers: <c>st_faceindex</c>, <c>st_facecount</c>, the
    /// three <c>keyboxes</c>, the ready weapon's ammo as drawn
    /// (<see cref="StStuff.largeammo"/> for none), <c>message_on</c>,
    /// <c>message_counter</c>, the FNV-1a of the message's text
    /// (<see cref="HuStuff.TextHash"/>) and of the pixels both draw
    /// (<see cref="HudScreen.Hash"/>: the bar, then the message line), 8 hex
    /// digits each; the pixels' hash is <c>-</c> without graphics.
    /// </summary>
    public static string Hud(StStuff st, HuStuff hu, bool pixels)
    {
        string pix = "-";
        if (pixels)
        {
            st.ST_Drawer();
            hu.HU_Drawer();
            pix = hu.Screen.Hash(st.Screen.Hash()).ToString("x8", CultureInfo.InvariantCulture);
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"{st.st_faceindex}:{st.st_facecount}:{st.keyboxes[0]}:{st.keyboxes[1]}:{st.keyboxes[2]}:{st.w_ready_num}:{(hu.message_on ? 1 : 0)}:{hu.message_counter}:{HuStuff.TextHash(hu.text):x8}:{pix}");
    }

    /// <summary>
    /// The WAD whose status bar and font vanilla drew a route with
    /// (T6.11): DOOM1.WAD, with the synthetic IWAD or the test map over it
    /// as <c>-file</c> as <c>routes.py</c> plays them (the synthetic IWAD's
    /// <c>STBAR</c> and three font glyphs replace DOOM1's); null without DOOM1.WAD.
    /// </summary>
    public WadArchive? HudGraphics()
    {
        if (TestWads.Doom1Path is not string doom1)
            return null;
        var files = new List<WadFile> { WadFile.Open(doom1) };
        if (Iwad == "synthetic")
            files.Add(WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName));
        else if (Iwad == "testmap")
            files.Add(WadFile.FromBytes(RouteTestMaps.Get(Map).Build(), Map + ".wad"));
        return new WadArchive(files);
    }

    /// <summary>The environment variables of <see cref="WriteHud"/> (dump.c's <c>DUMP_HUD_DIR</c>/<c>DUMP_HUD_TICS</c>).</summary>
    public const string HudDirEnvVar = "ISODOOM_HUD_DIR", HudTicsEnvVar = "ISODOOM_HUD_TICS";

    /// <summary>
    /// T6.11 (a debugging aid): with <see cref="HudDirEnvVar"/> and
    /// <see cref="HudTicsEnvVar"/> (<c>N,M,…</c> or <c>all</c>) set, the
    /// HUD as drawn after those tics goes to <c>DIR/ROUTE-simhudN.ppm</c> in
    /// dump.c's <c>hudN.ppm</c> layout (320×48 in the tic's palette: the
    /// message line, black where nothing is drawn, over the status bar), for
    /// comparing with vanilla's (WAD-derived: keep them out of the repo).
    /// </summary>
    private static void WriteHud(string name, WadArchive graphics, World world, StStuff st, HuStuff hu)
    {
        string? dir = Environment.GetEnvironmentVariable(HudDirEnvVar), tics = Environment.GetEnvironmentVariable(HudTicsEnvVar);
        string tic = world.leveltime.ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(tics) || (tics != "all" && !tics.Split(',').Contains(tic)))
            return;
        byte[] pal = Playpal.Load(graphics).GetPalette(StStuff.ST_doPaletteStuff(world.players[world.consoleplayer])).ToArray();
        using FileStream f = System.IO.File.Create(System.IO.Path.Combine(dir, $"{name}-simhud{tic}.ppm"));
        f.Write(Encoding.ASCII.GetBytes($"P6\n{HudScreen.SCREENWIDTH} {HuStuff.ScreenRows + StStuff.ST_HEIGHT}\n255\n"));
        byte[] black = new byte[3];
        foreach (HudScreen screen in new[] { hu.Screen, st.Screen })
        {
            for (int i = 0; i < screen.Pixels.Length; i++)
            {
                int c = screen.Pixels[i] * 3;
                f.Write(screen.Opaque[i] != 0 ? pal.AsSpan(c, 3) : black);
            }
        }
    }

    /// <summary>The dump's <c>inventory</c> column (T6.1).</summary>
    public static string Inventory(player_t p)
    {
        int owned = 0;
        for (int i = 0; i < (int)weapontype_t.NUMWEAPONS; i++)
            owned |= p.weaponowned[i] ? 1 << i : 0;
        return string.Join(':', new[] { p.itemcount, p.ammo[0], p.ammo[1], p.ammo[2], p.ammo[3], owned, p.backpack ? 1 : 0 }
            .Select(v => v.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// The dump's <c>things</c> column (T6.1): the count of mobjs, a colon and
    /// the 32-bit FNV-1a (word-wise, 8 hex digits) of each one's type, state,
    /// tics, x, y, z, angle, flags and health, in thinker order (dump.c's).
    /// </summary>
    public static string ThingsHash(World world)
    {
        uint hash = 2166136261u;
        int count = 0;
        foreach (mobj_t m in world.Mobjs())
        {
            foreach (int v in new[] { (int)m.type, (int)m.state, m.tics, m.x, m.y, m.z, unchecked((int)m.angle), (int)m.flags, m.health })
                hash = unchecked((hash ^ (uint)v) * 16777619u);
            count++;
        }
        return string.Create(CultureInfo.InvariantCulture, $"{count}:{hash:x8}");
    }

    /// <summary>The sectors' light levels of a world before its first tic: the map's (the light thinkers spawn without changing them).</summary>
    public static short[] MapLights(World world) => [.. world.sectors.Select(s => s.lightlevel)];

    /// <summary>The sidedefs' textures, top, middle and bottom of each in sidedef order (before the first tic: the map's).</summary>
    public static string[] MapTextures(World world) =>
        [.. world.sides.SelectMany(s => new[] { s.toptexture, s.midtexture, s.bottomtexture })];

    /// <summary>The sectors' floor and ceiling heights (fixed_t) of a world before its first tic: the map's.</summary>
    public static (int Floor, int Ceiling)[] MapHeights(World world) =>
        [.. world.sectors.Select(s => (s.floorheight, s.ceilingheight))];

    /// <summary>
    /// Plays the route in a new world and fails at the first tic whose state
    /// differs from vanilla's, naming the differing columns.
    /// </summary>
    public void Check()
    {
        string[] expected = Reference();
        World world = NewWorld(out WadArchive wad);
        (int, int)[] mapHeights = MapHeights(world);
        string[] mapTextures = MapTextures(world);
        short[] mapLights = MapLights(world);
        // T6.11: the status bar and message line, as vanilla's P_SpawnPlayer starts them (after M_ClearRandom in G_InitNew).
        WadArchive? graphics = HudGraphics();
        var st = new StStuff(graphics is null ? new StStuff.Graphics() : StStuff.Graphics.Load(graphics), new DoomRandom());
        var hu = new HuStuff(graphics);
        st.ST_Start(world.players[world.consoleplayer]);
        hu.HU_Start();
        Assert.True(expected.Length == Cmds.Count,
            $"{Name}: the dump has {expected.Length} tics, the route {Cmds.Count}: rerun tools/VanillaRef/routes.sh.");
        for (int tic = 0; tic < Cmds.Count; tic++)
        {
            if (Reborn(world, wad))
            {
                // T6.12: vanilla's P_SpawnPlayer starts them again for the reborn console player.
                st.ST_Start(world.players[world.consoleplayer]);
                hu.HU_Start();
            }
            File.RunEvents(world, tic);
            world.G_Ticker(Cmds[tic]);
            st.ST_Ticker();
            string? message = null;
            foreach (sim_event_t ev in world.events)
            {
                if (ev.type == simevent_t.se_message && ev.player == world.consoleplayer)
                    message = ev.message;
            }
            hu.HU_Ticker(message);
            string actual = Line(world, mapHeights, mapTextures, mapLights, Hud(st, hu, graphics is not null));
            if (graphics is not null)
                WriteHud(Name, graphics, world, st, hu);
            if (actual == expected[tic])
                continue;
            string[] e = expected[tic].Split(' '), a = actual.Split(' ');
            int sounds = Array.IndexOf(Columns, "sounds"), hud = Array.IndexOf(Columns, "hud");
            if (e.Length == Columns.Length && a.Length == Columns.Length)
            {
                a[sounds] = MatchSounds(e[sounds], a[sounds]);
                if (graphics is null && a[hud].EndsWith(":-", StringComparison.Ordinal))
                    a[hud] = a[hud][..^1] + e[hud][(e[hud].LastIndexOf(':') + 1)..]; // no graphics: the pixels are not compared
                actual = string.Join(' ', a);
                if (actual == expected[tic])
                    continue;
            }
            var msg = new StringBuilder();
            if (e.AsSpan(1..5).SequenceEqual(a.AsSpan(1..5)) && e[0] == a[0])
                msg.Append(CultureInfo.InvariantCulture, $"{Name}: tic {tic + 1} differs from vanilla");
            else
                msg.Append(CultureInfo.InvariantCulture, $"{Name}: tic {tic + 1}'s ticcmd differs from the dump's (stale dump? rerun tools/VanillaRef/routes.sh)");
            for (int i = 0; i < Columns.Length; i++)
            {
                if (i >= e.Length || e[i] != a[i])
                    msg.Append(CultureInfo.InvariantCulture, $"\n  {Columns[i]}: vanilla {(i < e.Length ? e[i] : "-")}, sim {a[i]}");
            }
            msg.Append(CultureInfo.InvariantCulture, $"\n  vanilla: {expected[tic]}\n  sim:     {actual}");
            msg.Append(CultureInfo.InvariantCulture, $"\n  sim overruns so far (not emulated): intercepts {world.interceptoverruns}, spechit {world.spechitoverruns}");
            Assert.Fail(msg.ToString());
        }
        // T5.9: the exit column is checked on every tic above; the header says the last tic leaves the level.
        string last = expected.Length > 0 ? expected[^1].Split(' ')[^1] : "0";
        Assert.True(last == Exit.ToString(CultureInfo.InvariantCulture),
            $"{Name}: the last tic's exit is {last} in vanilla, the route's header says {Exit} (0 none, 1 normal, 2 secret)");
    }
}
