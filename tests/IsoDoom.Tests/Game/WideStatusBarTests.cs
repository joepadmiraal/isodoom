using System;
using System.Linq;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Tests.Support;
using IsoDoom.Tools.SyntheticIwad;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>
/// T6.13y: the status bar 400 wide (<see cref="WideStatusBar"/>,
/// <see cref="ScreenLayout.ForWideBar"/>): vanilla's 320 columns in its
/// middle pixel for pixel, a panel each side, the ready weapon's icon in the
/// left one's box, and the layout filling the window's width, shorter than
/// vanilla's bar. The level check draws it with a renderer.
/// </summary>
public class WideStatusBarTests
{
    [Theory]
    [InlineData(1280, 800, true, 0, 677, 1280, 123)]
    [InlineData(1280, 800, false, 0, 698, 1280, 102)]
    [InlineData(1920, 1080, true, 0, 896, 1920, 184)]
    public void TheWideBarFillsTheWidthShorter(int w, int h, bool aspect43, int x, int y, int bw, int bh)
    {
        ScreenLayout bar = ScreenLayout.ForWideBar(w, h, WideStatusBar.DefaultWidth, 0, aspect43);
        Assert.Equal((x, y, bw, bh), bar.Rect(-WideStatusBar.BoxWidth, StStuff.ST_Y, WideStatusBar.DefaultWidth, StStuff.ST_HEIGHT));
        // vanilla's column 0 where the middle starts
        Assert.Equal(WideStatusBar.BoxWidth * bar.ScaleX, bar.OriginX, 3);
        Assert.True(bh < ScreenLayout.ForHud(w, h, 0, aspect43).Rect(0, StStuff.ST_Y, HudScreen.SCREENWIDTH, StStuff.ST_HEIGHT).H);
    }

    [Theory]
    [InlineData(2560, 1080, true, 280, 888, 2000, 192)]
    [InlineData(800, 600, false, 0, 536, 800, 64)]
    public void NeverTallerThanVanillasBar(int w, int h, bool aspect43, int x, int y, int bw, int bh)
    {
        // at the HUD's whole scale where filling the width would make it taller (21:9: centred)
        ScreenLayout bar = ScreenLayout.ForWideBar(w, h, WideStatusBar.DefaultWidth, 0, aspect43);
        Assert.Equal((x, y, bw, bh), bar.Rect(-WideStatusBar.BoxWidth, StStuff.ST_Y, WideStatusBar.DefaultWidth, StStuff.ST_HEIGHT));
        Assert.Equal(ScreenLayout.ForHud(w, h, 0, aspect43).Rect(0, StStuff.ST_Y, HudScreen.SCREENWIDTH, StStuff.ST_HEIGHT).H, bh);
    }

    [Fact]
    public void AFixedScaleCentresTheBar()
    {
        ScreenLayout bar = ScreenLayout.ForWideBar(1920, 1080, WideStatusBar.DefaultWidth, 4, false);
        Assert.Equal((160, 952, 1600, 128), bar.Rect(-WideStatusBar.BoxWidth, StStuff.ST_Y, WideStatusBar.DefaultWidth, StStuff.ST_HEIGHT));
    }

    [Fact]
    public void TheMiddleIsVanillasAndThePanelsTheFlat()
    {
        var wad = new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);
        var st = new StStuff(StStuff.Graphics.Load(wad), new DoomRandom());
        var p = new player_t { readyweapon = weapontype_t.wp_pistol, pendingweapon = weapontype_t.wp_nochange, health = 100 };
        st.ST_Start(p);
        st.ST_Ticker();
        st.ST_Drawer(true);
        var wide = new WideStatusBar(wad, WideStatusBar.DefaultWidth);
        Assert.Equal(WideStatusBar.BoxWidth, wide.Side);
        Assert.False(wide.WadArt);
        wide.Draw(st.Screen, null, default);
        HudScreen s = wide.Screen;
        Assert.Equal((WideStatusBar.DefaultWidth * 2, StStuff.ST_HEIGHT * 2), (s.Width, s.Height));
        Assert.DoesNotContain(s.Opaque, o => o == 0);
        for (int y = 0; y < s.Height; y++)
        {
            for (int x = 0; x < s.Width; x++)
            {
                int bx = x / 2 - wide.Side, by = y / 2;
                if (bx is >= 0 and < HudScreen.SCREENWIDTH)
                    Assert.Equal(st.Screen.Pixels[by * st.Screen.Width + bx], s.Pixels[y * s.Width + x]);
            }
        }
        // the bevels: the left edge light, the weapon box's right edge, the bar's bottom and right edge dark
        Assert.Equal(WideStatusBar.Light, s.Pixels[2 * s.Width]);
        Assert.Equal(WideStatusBar.Divider, s.Pixels[2 * s.Width + (wide.Side - 1) * 2]);
        Assert.Equal(WideStatusBar.Dark, s.Pixels[(s.Height - 1) * s.Width + 2]);
        Assert.Equal(WideStatusBar.Dark, s.Pixels[2 * s.Width + s.Width - 1]);
    }

    [Fact]
    public void Doom2TheIconSitsInTheBoxDimmedWhileItComesUp()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        var icon = new WeaponIcon(wad, Colormap.Load(wad), Playpal.Load(wad).GetPalette(0));
        var st = new StStuff(StStuff.Graphics.Load(wad), new DoomRandom());
        var wide = new WideStatusBar(wad, WideStatusBar.DefaultWidth);
        foreach (weapontype_t weapon in Enumerable.Range(0, (int)weapontype_t.NUMWEAPONS).Select(i => (weapontype_t)i))
        {
            var p = new player_t { readyweapon = weapon, pendingweapon = weapontype_t.wp_nochange, health = 100 };
            Array.Fill(p.weaponowned, true);
            p.psprites[World.ps_weapon].state = Info.weaponinfo[(int)weapon].readystate;
            st.ST_Start(p);
            st.ST_Ticker();
            st.ST_Drawer(true);
            foreach (bool dim in new[] { false, true })
            {
                if (dim)
                    p.psprites[World.ps_weapon].state = Info.weaponinfo[(int)weapon].upstate;
                (IndexedImage? image, bool dimmed) = WideStatusBar.IconFor(icon, p);
                Assert.Equal(dim, dimmed);
                Assert.NotNull(image);
                wide.Draw(st.Screen, image, dimmed ? icon.DimMap : default);
                (int x, int y) = wide.IconPlace(image)!.Value;
                // wholly inside the box, left of vanilla's AMMO box
                Assert.True(x >= (wide.Side - WideStatusBar.BoxWidth) * 2 && x + image.Width <= wide.Side * 2 && y >= 0 && y + image.Height <= wide.Screen.Height);
                for (int py = 0; py < image.Height; py++)
                {
                    for (int px = 0; px < image.Width; px++)
                    {
                        if (image.IsOpaque(px, py))
                            Assert.Equal(dim ? icon.DimMap[image[px, py]] : image[px, py], wide.Screen.Pixels[(y + py) * wide.Screen.Width + x + px]);
                    }
                }
            }
        }
        // none while dead or with the option off
        var dead = new player_t { readyweapon = weapontype_t.wp_pistol, playerstate = playerstate_t.PST_DEAD };
        Assert.Null(WideStatusBar.IconFor(icon, dead).Icon);
        icon.Enabled = false;
        Assert.Null(WideStatusBar.IconFor(icon, new player_t { readyweapon = weapontype_t.wp_pistol }).Icon);
    }

    [Fact]
    public void DoomTheReReleasesWideBarArtFillsThePanels()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom());
        if (wad.Find("STBAR") is not { } lump || Patch.Decode(lump.Data.Span, lump.Name) is not { Width: >= WideStatusBar.DefaultWidth } art)
            return; // an original release: 320 wide
        var wide = new WideStatusBar(wad, WideStatusBar.DefaultWidth);
        Assert.True(wide.WadArt);
        var middle = new HudScreen(StStuff.ST_Y, StStuff.ST_HEIGHT);
        wide.Draw(middle, null, default);
        int crop = (art.Width - WideStatusBar.DefaultWidth) / 2;
        // the right panel past its first column: the art's own pixels
        for (int y = 0; y < StStuff.ST_HEIGHT; y++)
        {
            for (int x = wide.Side + HudScreen.SCREENWIDTH; x < WideStatusBar.DefaultWidth; x++)
                Assert.Equal(art[x + crop, y], wide.Screen.Pixels[y * 2 * wide.Screen.Width + x * 2]);
        }
    }
}
