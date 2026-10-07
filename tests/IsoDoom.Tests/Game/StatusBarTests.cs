using System;
using IsoDoom.Game;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Tests.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T6.11: st_stuff.c's status bar (<see cref="StStuff"/>) and hu_stuff.c's
/// message line (<see cref="HuStuff"/>). The route tests compare both with
/// vanilla on every tic of every route (<see cref="VanillaRoute.Hud"/>: the
/// face, keys, ready ammo, message and, with DOOM1.WAD, every pixel); these
/// pin the face's priorities and the drawing rules case by case.
/// </summary>
public class StatusBarTests
{
    private static WadArchive Synthetic() => new(new[] { WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName) });

    private static World NewWorld()
    {
        var world = new World(new SpawnSettings(GameMode.shareware, skill_t.sk_medium), Tweaks.Vanilla);
        world.G_DoLoadLevel(Level.Load(Synthetic(), "E1M2"));
        return world;
    }

    private static (World World, player_t Player, StStuff St) Start()
    {
        World world = NewWorld();
        player_t p = world.players[world.consoleplayer];
        var st = new StStuff(new StStuff.Graphics(), new DoomRandom());
        st.ST_Start(p);
        return (world, p, st);
    }

    [Theory]
    [InlineData(0, "STFST00")]
    [InlineData(2, "STFST02")]
    [InlineData(3, "STFTR00")]
    [InlineData(4, "STFTL00")]
    [InlineData(5, "STFOUCH0")]
    [InlineData(6, "STFEVL0")]
    [InlineData(7, "STFKILL0")]
    [InlineData(StStuff.ST_FACESTRIDE * 4 + 1, "STFST41")]
    [InlineData(StStuff.ST_GODFACE, "STFGOD0")]
    [InlineData(StStuff.ST_DEADFACE, "STFDEAD0")]
    public void FacesAreInVanillasOrder(int index, string name) => Assert.Equal(name, StStuff.FaceName(index));

    [Theory]
    [InlineData(100, 0)]
    [InlineData(200, 0)]
    [InlineData(80, 0)]
    [InlineData(79, 8)]
    [InlineData(60, 8)]
    [InlineData(59, 16)]
    [InlineData(20, 24)]
    [InlineData(19, 32)]
    [InlineData(1, 32)]
    public void ThePainOffsetFollowsTheHealth(int health, int offset)
    {
        (_, player_t p, StStuff st) = Start();
        p.health = health;
        Assert.Equal(offset, st.ST_calcPainOffset());
    }

    [Fact]
    public void TheFaceGlancesAtRandomEveryHalfSecond()
    {
        (World world, _, StStuff st) = Start();
        var faces = new int[40];
        for (int i = 0; i < faces.Length; i++)
        {
            st.ST_Ticker();
            faces[i] = st.st_faceindex;
        }
        // M_Random's first entries are 8, 109, ...: the first tic picks 8 % 3, then the face holds ST_STRAIGHTFACECOUNT tics.
        Assert.Equal(DoomRandom.rndtable[1] % 3, faces[0]);
        Assert.All(faces[..StStuff.ST_STRAIGHTFACECOUNT], f => Assert.Equal(faces[0], f));
        Assert.Equal(DoomRandom.rndtable[1 + StStuff.ST_STRAIGHTFACECOUNT] % 3, faces[StStuff.ST_STRAIGHTFACECOUNT]);
        // The status bar's own M_Random index moved; the world's indices did not.
        Assert.Equal(faces.Length, st.Random.rndindex);
        Assert.Equal(0, world.random.rndindex);
    }

    [Fact]
    public void ADeadPlayerShowsTheDeadFace()
    {
        (_, player_t p, StStuff st) = Start();
        p.health = 0;
        p.damagecount = 10;
        p.bonuscount = 6;
        st.ST_Ticker();
        Assert.Equal(StStuff.ST_DEADFACE, st.st_faceindex);
    }

    [Fact]
    public void ANewWeaponGivesTheEvilGrin()
    {
        (_, player_t p, StStuff st) = Start();
        st.ST_Ticker();
        p.health = 70;
        p.bonuscount = World.BONUSADD;
        p.weaponowned[(int)weapontype_t.wp_shotgun] = true;
        st.ST_Ticker();
        Assert.Equal(StStuff.ST_FACESTRIDE + StStuff.ST_EVILGRINOFFSET, st.st_faceindex);
        Assert.Equal(StStuff.ST_EVILGRINCOUNT - 1, st.st_facecount);
        // Another pickup without a new weapon: the grin keeps its count.
        st.ST_Ticker();
        Assert.Equal(StStuff.ST_EVILGRINCOUNT - 2, st.st_facecount);
    }

    [Theory]
    [InlineData(-64, 0, StStuff.ST_RAMPAGEOFFSET)] // straight ahead (the player faces west): head-on
    [InlineData(-64, 16, StStuff.ST_RAMPAGEOFFSET)] // within 45° of ahead: head-on
    [InlineData(0, 64, StStuff.ST_TURNOFFSET)] // to the right (north of a west-facing player): STFTR
    [InlineData(0, -64, StStuff.ST_TURNOFFSET + 1)] // to the left (south): STFTL
    public void AnAttackerTurnsTheFace(int dx, int dy, int offset)
    {
        (World world, player_t p, StStuff st) = Start();
        st.ST_Ticker();
        mobj_t mo = p.mo!;
        mo.angle = Tables.ANG180; // facing west
        int ax = mo.x + dx * Fixed.FRACUNIT, ay = mo.y + dy * Fixed.FRACUNIT;
        p.attacker = world.P_SpawnMobj(ax, ay, World.ONFLOORZ, mobjtype_t.MT_POSSESSED);
        p.damagecount = 10;
        p.health = 90;
        st.ST_Ticker();
        Assert.Equal(offset, st.st_faceindex); // health 90: pain offset 0
        Assert.Equal(StStuff.ST_TURNCOUNT - 1, st.st_facecount);
        Assert.Equal(7, st.priority);
    }

    [Fact]
    public void HurtingYourselfShowsTheRampageFace()
    {
        (_, player_t p, StStuff st) = Start();
        st.ST_Ticker();
        p.damagecount = 10;
        p.health = 50;
        p.attacker = p.mo; // a barrel of one's own, a damage floor (null)
        st.ST_Ticker();
        Assert.Equal(StStuff.ST_FACESTRIDE * 2 + StStuff.ST_RAMPAGEOFFSET, st.st_faceindex);
        Assert.Equal(6, st.priority);
    }

    /// <summary>Vanilla's quirk: "ouch" shows when the health went up by more than 20 while hurt, i.e. on a level's first tic (<c>st_oldhealth</c> −1).</summary>
    [Fact]
    public void TheOuchFaceShowsOnlyWhenTheHealthWentUp()
    {
        (_, player_t p, StStuff st) = Start();
        p.damagecount = 10;
        st.ST_Ticker(); // st_oldhealth -1: 100 - -1 > 20
        Assert.Equal(StStuff.ST_OUCHOFFSET, st.st_faceindex);
        st.priority = 0;
        st.st_facecount = 0;
        p.health = 40; // a big loss: no ouch
        st.ST_Ticker();
        Assert.Equal(StStuff.ST_FACESTRIDE * 2 + StStuff.ST_RAMPAGEOFFSET, st.st_faceindex);
    }

    [Fact]
    public void HoldingFireForTwoSecondsShowsTheRampageFace()
    {
        (_, player_t p, StStuff st) = Start();
        p.attackdown = true;
        for (int i = 0; i < StStuff.ST_RAMPAGEDELAY; i++)
        {
            st.ST_Ticker();
            Assert.NotEqual(5, st.priority);
        }
        st.ST_Ticker();
        Assert.Equal((5, StStuff.ST_RAMPAGEOFFSET), (st.priority, st.st_faceindex));
    }

    [Fact]
    public void InvulnerabilityShowsTheGodFace()
    {
        (_, player_t p, StStuff st) = Start();
        p.powers[(int)powertype_t.pw_invulnerability] = 100;
        st.ST_Ticker();
        Assert.Equal(StStuff.ST_GODFACE, st.st_faceindex);
        p.powers[(int)powertype_t.pw_invulnerability] = 0;
        p.cheats = player_t.CF_GODMODE;
        st.priority = 0;
        st.ST_Ticker();
        Assert.Equal(StStuff.ST_GODFACE, st.st_faceindex);
    }

    [Fact]
    public void TheKeyBoxesShowASkullOverItsCard()
    {
        (_, player_t p, StStuff st) = Start();
        p.cards[(int)card_t.it_bluecard] = true;
        p.cards[(int)card_t.it_redcard] = true;
        p.cards[(int)card_t.it_redskull] = true;
        st.ST_Ticker();
        Assert.Equal(new[] { 0, -1, 5 }, st.keyboxes);
    }

    [Fact]
    public void AWeaponWithoutAmmoShowsNoAmmo()
    {
        (_, player_t p, StStuff st) = Start();
        st.ST_Ticker();
        Assert.Equal(p.ammo[(int)ammotype_t.am_clip], st.w_ready_num);
        p.readyweapon = weapontype_t.wp_fist;
        st.ST_Ticker();
        Assert.Equal(StStuff.largeammo, st.w_ready_num);
    }

    /// <summary>The synthetic IWAD has <c>STBAR</c> and three glyphs only: the bar draws as its <c>STBAR</c>, the rest is missing and draws nothing.</summary>
    [Fact]
    public void DrawsWithWhatTheWadHas()
    {
        WadArchive wad = Synthetic();
        World world = NewWorld();
        var st = new StStuff(StStuff.Graphics.Load(wad), new DoomRandom());
        Assert.NotNull(st.G.sbar);
        Assert.Null(st.G.tallnum[0]);
        st.ST_Drawer();
        Assert.All(st.Screen.Opaque, o => Assert.Equal(0, o)); // before ST_Start: nothing
        st.ST_Start(world.players[0]);
        st.ST_Ticker();
        st.ST_Drawer();
        IndexedImage bar = st.G.sbar!;
        for (int y = 0; y < Math.Min(bar.Height, StStuff.ST_HEIGHT); y++)
        {
            for (int x = 0; x < Math.Min(bar.Width, StStuff.ST_WIDTH); x++)
            {
                int sx = x - bar.LeftOffset, sy = y - bar.TopOffset;
                if (bar.IsOpaque(x, y) && sx >= 0 && sx < StStuff.ST_WIDTH && sy >= 0 && sy < StStuff.ST_HEIGHT)
                    Assert.Equal(bar[x, y], st.Screen.Pixels[sy * StStuff.ST_WIDTH + sx]);
            }
        }
    }

    /// <summary>With DOOM1.WAD: the numbers are right-aligned in their digits, a weapon without ammo leaves the bar as it is, and the arms show vanilla's quirk.</summary>
    [Fact]
    public void DrawsTheNumbersAsVanilla()
    {
        WadArchive wad = WadArchive.Open(TestWads.RequireDoom1());
        World world = NewWorld();
        player_t p = world.players[0];
        var st = new StStuff(StStuff.Graphics.Load(wad), new DoomRandom());
        st.ST_Start(p);
        st.ST_Ticker();
        st.ST_Drawer();
        IndexedImage zero = st.G.tallnum[0]!, five = st.G.tallnum[5]!;
        // "50" ends at ST_AMMOX: the 0 just left of it, the 5 left of that.
        Assert.True(Covers(st.Screen, zero, StStuff.ST_AMMOX - zero.Width, StStuff.ST_AMMOY));
        Assert.True(Covers(st.Screen, five, StStuff.ST_AMMOX - 2 * zero.Width, StStuff.ST_AMMOY));
        // The fist: the ammo's digits back to the bar.
        p.readyweapon = weapontype_t.wp_fist;
        st.ST_Ticker();
        st.ST_Drawer();
        IndexedImage bar = st.G.sbar!;
        for (int y = StStuff.ST_AMMOY; y < StStuff.ST_AMMOY + zero.Height; y++)
        {
            for (int x = StStuff.ST_AMMOX - 3 * zero.Width; x < StStuff.ST_AMMOX; x++)
                Assert.Equal(bar[x, y - StStuff.ST_Y], st.Screen.Pixels[(y - StStuff.ST_Y) * StStuff.ST_WIDTH + x]);
        }
        // Drawing again without a tic changes nothing.
        byte[] before = (byte[])st.Screen.Pixels.Clone();
        st.ST_Drawer();
        Assert.Equal(before, st.Screen.Pixels);
    }

    private static bool Covers(HudScreen screen, IndexedImage patch, int x, int y)
    {
        for (int py = 0; py < patch.Height; py++)
        {
            for (int px = 0; px < patch.Width; px++)
            {
                if (patch.IsOpaque(px, py)
                    && screen.Pixels[(y - patch.TopOffset + py - screen.Top) * HudScreen.SCREENWIDTH + x - patch.LeftOffset + px] != patch[px, py])
                    return false;
            }
        }
        return true;
    }

    // ---- hu_stuff.c ----

    [Fact]
    public void AMessageShowsForFourSeconds()
    {
        var hu = new HuStuff(null);
        hu.HU_Start();
        hu.HU_Ticker(World.GOTREDSKULL);
        Assert.Equal(World.GOTREDSKULL, hu.Message);
        for (int i = 1; i < HuStuff.HU_MSGTIMEOUT; i++)
        {
            hu.HU_Ticker(null);
            Assert.NotNull(hu.Message);
        }
        hu.HU_Ticker(null);
        Assert.Null(hu.Message);
    }

    [Fact]
    public void ANewMessageReplacesTheLastAndStartsItsTimeAgain()
    {
        var hu = new HuStuff(null);
        hu.HU_Ticker("one");
        for (int i = 0; i < 100; i++)
            hu.HU_Ticker(null);
        hu.HU_Ticker("two");
        Assert.Equal(("two", HuStuff.HU_MSGTIMEOUT), (hu.Message, hu.message_counter));
        hu.HU_Ticker(new string('x', 100));
        Assert.Equal(HuStuff.HU_MAXLINELENGTH, hu.Message!.Length);
    }

    [Fact]
    public void ALevelStartHidesTheMessageButKeepsItsCounter()
    {
        var hu = new HuStuff(null);
        hu.HU_Ticker("one");
        hu.HU_Start();
        Assert.Null(hu.Message);
        Assert.Equal(HuStuff.HU_MSGTIMEOUT, hu.message_counter);
    }

    /// <summary>The synthetic IWAD's font has A, B and C: lower case draws upper case, anything else advances 4 pixels.</summary>
    [Fact]
    public void DrawsTheMessageInTheFont()
    {
        var hu = new HuStuff(Synthetic());
        IndexedImage a = hu.hu_font['A' - HuStuff.HU_FONTSTART]!, b = hu.hu_font['B' - HuStuff.HU_FONTSTART]!, c = hu.hu_font['C' - HuStuff.HU_FONTSTART]!;
        hu.HU_Ticker("ab c!");
        hu.HU_Drawer();
        Assert.True(Covers(hu.Screen, a, 0, 0));
        Assert.True(Covers(hu.Screen, b, a.Width, 0));
        Assert.True(Covers(hu.Screen, c, a.Width + b.Width + 4, 0));
        int right = a.Width + b.Width + 4 + c.Width;
        for (int y = 0; y < hu.Screen.Height; y++)
        {
            for (int x = right; x < HudScreen.SCREENWIDTH; x++)
                Assert.Equal(0, hu.Screen.Opaque[y * HudScreen.SCREENWIDTH + x]);
        }
        // Off: nothing.
        for (int i = 0; i < HuStuff.HU_MSGTIMEOUT; i++)
            hu.HU_Ticker(null);
        hu.HU_Drawer();
        Assert.All(hu.Screen.Opaque, o => Assert.Equal(0, o));
    }

    [Fact]
    public void TheMessageStopsAtTheScreensEdge()
    {
        var hu = new HuStuff(Synthetic());
        int w = hu.hu_font['A' - HuStuff.HU_FONTSTART]!.Width;
        hu.HU_Ticker(new string('A', 80));
        hu.HU_Drawer();
        int last = HudScreen.SCREENWIDTH / w * w; // the glyphs that fit whole
        for (int y = 0; y < hu.Screen.Height; y++)
        {
            for (int x = last; x < HudScreen.SCREENWIDTH; x++)
                Assert.Equal(0, hu.Screen.Opaque[y * HudScreen.SCREENWIDTH + x]);
        }
    }

    /// <summary>
    /// The status bar only reads the sim: playing a route with the bar and
    /// message line ticking after every tic or without them gives the same
    /// checksum on every tic, and the world's <c>M_Random</c> index stays 0
    /// (the bar's runs in its own <see cref="DoomRandom"/>).
    /// </summary>
    [Theory]
    [InlineData("synthetic-monsters")]
    [InlineData("testmap-pickups")]
    public void TheSimDoesNotDependOnTheStatusBar(string name)
    {
        VanillaRoute route = VanillaRoute.Load(name);
        World plain = route.NewWorld(), watched = route.NewWorld();
        var st = new StStuff(StStuff.Graphics.Load(Synthetic()), new DoomRandom());
        var hu = new HuStuff(Synthetic());
        st.ST_Start(watched.players[0]);
        hu.HU_Start();
        for (int tic = 0; tic < route.Cmds.Count; tic++)
        {
            route.File.RunEvents(plain, tic);
            route.File.RunEvents(watched, tic);
            plain.G_Ticker(route.Cmds[tic]);
            watched.G_Ticker(route.Cmds[tic]);
            st.ST_Ticker();
            string? message = null;
            foreach (sim_event_t e in watched.events)
            {
                if (e.type == simevent_t.se_message)
                    message = e.message;
            }
            hu.HU_Ticker(message);
            st.ST_Drawer();
            st.ST_DrawFullscreen();
            hu.HU_Drawer();
            Assert.Equal(plain.Checksum(), watched.Checksum());
        }
        Assert.Equal(0, watched.random.rndindex);
        Assert.Equal(route.Cmds.Count & 255, st.Random.rndindex);
    }

    /// <summary>g_game.c's turbo check (ported with the status bar: it is a message): a forwardmove over 50 gets "Green: is turbo!" at the next tic whose gametic is a multiple of 128.</summary>
    [Fact]
    public void TurboGetsAMessage()
    {
        World world = NewWorld();
        player_t p = world.players[0];
        string? seen = null;
        int at = -1;
        for (int tic = 0; tic < 140 && seen is null; tic++)
        {
            world.G_Ticker(new ticcmd_t { forwardmove = (sbyte)(tic == 3 ? 100 : 0) });
            foreach (sim_event_t e in world.events)
            {
                if (e.type == simevent_t.se_message)
                    (seen, at) = (e.message, tic);
            }
        }
        Assert.Equal(("Green:  is turbo!", 128), (seen, at));
        Assert.Null(p.message);
    }
}
