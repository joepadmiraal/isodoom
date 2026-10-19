using System;
using System.Collections.Generic;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// P9 (docs/PWAD-VALIDATION.md): the UI graphics a PWAD set replaces are the
/// ones the game draws, and the 400-wide status bar's side panels
/// (T6.13y, <see cref="WideStatusBar"/>) follow the <c>STBAR</c> in use: the
/// 2024 re-release's side art only beside its own wide bar (DTWID, which keeps
/// it), <c>FLAT5_4</c> beside a PWAD's 320-wide one (NEIS over the
/// re-release; Requiem, HR and AV over Doom II). Skips without a set or its IWAD.
/// </summary>
public sealed class PwadUiTests(ITestOutputHelper output)
{
    /// <summary>The bar, its numbers, keys, arms and faces, the message font, the menus' and intermission's patches and the full screens (not AV's <c>STEP*</c> flats outside <c>F_START</c>, P1).</summary>
    private static bool IsUiGraphic(string name) =>
        name.StartsWith("ST", StringComparison.Ordinal) && !name.StartsWith("STEP", StringComparison.Ordinal) || name.StartsWith("M_", StringComparison.Ordinal) || name.StartsWith("WI", StringComparison.Ordinal)
        || name is "TITLEPIC" or "INTERPIC" or "CREDIT" or "HELP" or "HELP1" or "HELP2" or "BOSSBACK" or "VICTORY2" or "ENDPIC" or "PFUB1" or "PFUB2";

    /// <summary>The UI graphics <paramref name="pwads"/> give, each from the last file that has it (<c>-file</c>'s order), decoded.</summary>
    private static Dictionary<string, IndexedImage> PwadGraphics(IEnumerable<string> pwads)
    {
        var found = new Dictionary<string, IndexedImage>(StringComparer.Ordinal);
        foreach (string path in pwads)
        {
            foreach (WadLump lump in WadFile.Open(path).Lumps.Where(l => !l.IsMarker && l.Namespace == LumpNamespace.Global && IsUiGraphic(l.Name)))
                found[lump.Name] = Patch.Decode(lump.Data.Span, lump.Name);
        }
        return found;
    }

    private static void AssertSame(IndexedImage expected, IndexedImage? actual, string what)
    {
        Assert.True(actual is not null, $"{what}: not loaded");
        Assert.True((expected.Width, expected.Height, expected.LeftOffset, expected.TopOffset) == (actual.Width, actual.Height, actual.LeftOffset, actual.TopOffset)
            && expected.Pixels.AsSpan().SequenceEqual(actual.Pixels) && expected.Opaque.AsSpan().SequenceEqual(actual.Opaque),
            $"{what}: not the PWAD's");
    }

    [Theory]
    [MemberData(nameof(PwadSets.Folders), MemberType = typeof(PwadSets))]
    public void TheGameDrawsThePwadsUiGraphics(string folder)
    {
        (string iwad, string[] pwads) = PwadSets.Get(folder).Require();
        var wad = WadArchive.Open(iwad, pwads);
        Dictionary<string, IndexedImage> pwad = PwadGraphics(pwads);

        // the status bar's lumps (st_stuff.c ST_loadUnloadGraphics) and the message font (hu_stuff.c HU_Init)
        StStuff.Graphics g = StStuff.Graphics.Load(wad);
        var loaded = new Dictionary<string, IndexedImage?>(StringComparer.Ordinal)
        {
            ["STBAR"] = g.sbar,
            ["STTPRCNT"] = g.tallpercent,
            ["STARMS"] = g.armsbg,
            ["STTMINUS"] = g.sttminus,
        };
        for (int i = 0; i < 10; i++)
        {
            loaded[$"STTNUM{i}"] = g.tallnum[i];
            loaded[$"STYSNUM{i}"] = g.shortnum[i];
        }
        for (int i = 0; i < 6; i++)
            loaded[$"STGNUM{i + 2}"] = g.arms[i][0];
        for (int i = 0; i < g.keys.Length; i++)
            loaded[$"STKEYS{i}"] = g.keys[i];
        for (int i = 0; i < g.facebacks.Length; i++)
            loaded[$"STFB{i}"] = g.facebacks[i];
        for (int i = 0; i < StStuff.ST_NUMFACES; i++)
            loaded[StStuff.FaceName(i)] = g.faces[i];
        var hu = new HuStuff(wad);
        for (int i = 0; i < HuStuff.HU_FONTSIZE; i++)
            loaded[$"STCFN{HuStuff.HU_FONTSTART + i:D3}"] = hu.hu_font[i];

        // the menus, the intermission, the finale and the title loop (ScreenGraphics.Patch)
        var screens = new ScreenGraphics(wad, hu);
        int bar = 0, others = 0;
        foreach ((string name, IndexedImage image) in pwad.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (loaded.TryGetValue(name, out IndexedImage? st))
            {
                AssertSame(image, st, $"{folder} {name} (status bar or font)");
                bar++;
            }
            AssertSame(image, screens.Patch(name), $"{folder} {name} (screens)");
            others++;
        }
        output.WriteLine($"{folder}: {others} UI graphics from the PWADs, {bar} of them the status bar's or the font's: {string.Join(' ', pwad.Keys.Order(StringComparer.Ordinal))}");
        int expectedBar = folder switch
        {
            // STBAR, STARMS, the numbers (tall, short, the arms' grey 2-7, minus, percent) and the 63 font characters
            "av" => 93,
            "hr" => 2,
            // the face backgrounds STFB0-3 (drawn in a netgame only, as vanilla's) too
            "requiem" => 34,
            "neis" => 30,
            _ => 0,
        };
        Assert.Equal(expectedBar, bar);
    }

    [Theory]
    [MemberData(nameof(PwadSets.Folders), MemberType = typeof(PwadSets))]
    public void TheWideBarsPanelsFollowTheStbarInUse(string folder)
    {
        (string iwad, string[] pwads) = PwadSets.Get(folder).Require();
        var wad = WadArchive.Open(iwad, pwads);
        WadLump stbarLump = wad.Find("STBAR")!;
        IndexedImage stbar = Patch.Decode(stbarLump.Data.Span, stbarLump.Name);
        bool pwadBar = pwads.Any(p => stbarLump.File.Name == System.IO.Path.GetFileName(p));
        IndexedImage iwadBar = Patch.Decode(WadFile.Open(iwad).Lumps.Last(l => l.Name == "STBAR").Data.Span, "STBAR");

        var st = new StStuff(StStuff.Graphics.Load(wad), new DoomRandom());
        var p = new player_t { readyweapon = weapontype_t.wp_pistol, pendingweapon = weapontype_t.wp_nochange, health = 100 };
        st.ST_Start(p);
        st.ST_Ticker();
        st.ST_Drawer(true);
        var wide = new WideStatusBar(wad, WideStatusBar.DefaultWidth);
        wide.Draw(st.Screen, null, default);

        // the re-release's side art only beside its own wide bar: never beside a PWAD's
        Assert.Equal(pwadBar ? false : iwadBar.Width >= WideStatusBar.DefaultWidth, wide.WadArt);
        if (folder is "neis" or "requiem" or "hr" or "av")
            Assert.True(pwadBar, $"{folder}: its STBAR is not the one in use");
        if (folder == "dtwid" && iwadBar.Width >= WideStatusBar.DefaultWidth)
            Assert.True(wide.WadArt, "DTWID keeps the re-release's bar, so its side art");

        // vanilla's bar in the middle is the STBAR in use (its first row, left of STARMS: no widget there), centred when wide
        for (int x = 0; x < 104; x++)
            Assert.Equal(stbar[x + (stbar.Width - HudScreen.SCREENWIDTH) / 2, 0], st.Screen.Pixels[x]);

        // the panels' insides (off the bevels): FLAT5_4 tiled, or the art cropped from the wide STBAR's middle
        byte[] flat = wad.Find("FLAT5_4", LumpNamespace.Flats)!.Data.Span[..4096].ToArray();
        HudScreen s = wide.Screen;
        int r = WideStatusBar.Resolution, side = wide.Side, checkedPixels = 0;
        for (int y = 0; y < StStuff.ST_HEIGHT - 1; y++)
        {
            foreach (int x in Enumerable.Range(1, side - 2).Concat(Enumerable.Range(side + HudScreen.SCREENWIDTH + 1, side - 2)))
            {
                byte expected = wide.WadArt ? stbar[x + (stbar.Width - wide.Width) / 2, y] : flat[(y % 64) * 64 + x % 64];
                Assert.Equal(expected, s.Pixels[y * r * s.Width + x * r]);
                checkedPixels++;
            }
        }
        output.WriteLine($"{folder}: STBAR {stbar.Width} wide from {stbarLump.File.Name}; panels {(wide.WadArt ? "the re-release's art" : "FLAT5_4")}, {checkedPixels} pixels checked");
    }
}
