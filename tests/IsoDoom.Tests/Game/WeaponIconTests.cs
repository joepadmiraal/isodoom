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
/// T6.13x: the ready weapon's icon above the HUD's ammo and its ARMS digit
/// brightened (<see cref="WeaponIcon"/>). The level check draws it with a
/// renderer; these pin which weapon shows, where, dimmed or not, and that
/// the status bar's own pixels stay vanilla's.
/// </summary>
public class WeaponIconTests
{
    private static readonly weapontype_t[] _weapons = [.. Enumerable.Range(0, (int)weapontype_t.NUMWEAPONS).Select(i => (weapontype_t)i)];

    private static player_t Player(weapontype_t ready)
    {
        var p = new player_t { readyweapon = ready, pendingweapon = weapontype_t.wp_nochange, health = 100 };
        Array.Fill(p.weaponowned, true);
        p.psprites[World.ps_weapon].state = Info.weaponinfo[(int)ready].readystate;
        return p;
    }

    [Fact]
    public void PicksTheReadyWeaponThenThePendingOneDimmed()
    {
        player_t p = Player(weapontype_t.wp_shotgun);
        Assert.Equal((weapontype_t.wp_shotgun, false), WeaponIcon.Pick(p));
        // asked for: the new one, dimmed, while the old one lowers
        p.pendingweapon = weapontype_t.wp_missile;
        p.psprites[World.ps_weapon].state = Info.weaponinfo[(int)weapontype_t.wp_shotgun].downstate;
        Assert.Equal((weapontype_t.wp_missile, true), WeaponIcon.Pick(p));
        // raised: still dimmed until it is ready
        (p.readyweapon, p.pendingweapon) = (weapontype_t.wp_missile, weapontype_t.wp_nochange);
        p.psprites[World.ps_weapon].state = Info.weaponinfo[(int)weapontype_t.wp_missile].upstate;
        Assert.Equal((weapontype_t.wp_missile, true), WeaponIcon.Pick(p));
        p.psprites[World.ps_weapon].state = Info.weaponinfo[(int)weapontype_t.wp_missile].readystate;
        Assert.Equal((weapontype_t.wp_missile, false), WeaponIcon.Pick(p));
        p.playerstate = playerstate_t.PST_DEAD;
        Assert.Equal(weapontype_t.NUMWEAPONS, WeaponIcon.Pick(p).Weapon);
    }

    [Theory]
    [InlineData(weapontype_t.wp_fist, -1)]
    [InlineData(weapontype_t.wp_chainsaw, -1)]
    [InlineData(weapontype_t.wp_pistol, 0)]
    [InlineData(weapontype_t.wp_shotgun, 1)]
    [InlineData(weapontype_t.wp_supershotgun, 1)]
    [InlineData(weapontype_t.wp_chaingun, 2)]
    [InlineData(weapontype_t.wp_missile, 3)]
    [InlineData(weapontype_t.wp_plasma, 4)]
    [InlineData(weapontype_t.wp_bfg, 5)]
    public void ArmsSlotsAreTheBarsDigits(weapontype_t weapon, int slot) => Assert.Equal(slot, WeaponIcon.ArmsSlot(weapon));

    [Fact]
    public void WithoutTheSpritesNothingIsDrawn()
    {
        var wad = new WadArchive([WadFile.FromBytes(SyntheticIwad.Build(), SyntheticIwad.DefaultFileName)]);
        var icon = new WeaponIcon(wad, Colormap.Load(wad), Playpal.Load(wad).GetPalette(0));
        var st = new StStuff(StStuff.Graphics.Load(wad), new DoomRandom());
        st.ST_Start(Player(weapontype_t.wp_shotgun));
        st.ST_Ticker();
        st.ST_Drawer();
        icon.Draw(st, HudMode.Bar);
        Assert.Equal(weapontype_t.NUMWEAPONS, icon.Shown);
        Assert.DoesNotContain(icon.Screen.Opaque, o => o != 0);
        Assert.Null(icon.Bar); // no STYSNUM digits
    }

    [Fact]
    public void Doom2EveryWeaponHasAnIconInItsPlace()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        var icon = new WeaponIcon(wad, Colormap.Load(wad), Playpal.Load(wad).GetPalette(0));
        var st = new StStuff(StStuff.Graphics.Load(wad), new DoomRandom());
        foreach (weapontype_t weapon in _weapons)
        {
            IndexedImage image = icon.Icon(weapon) ?? throw new InvalidOperationException($"no icon for {weapon}");
            Assert.InRange(image.Width, 1, WeaponIcon.BoxWidth);
            Assert.InRange(image.Height, 1, WeaponIcon.BoxHeight);
            player_t p = Player(weapon);
            st.ST_Start(p);
            st.ST_Ticker();
            st.ST_Drawer(true);
            uint barHash = st.Screen.Hash();
            foreach (HudMode mode in new[] { HudMode.Bar, HudMode.Full })
            {
                icon.Draw(st, mode);
                Assert.Equal(weapon, icon.Shown);
                Assert.False(icon.Dimmed);
                (int x, int y, int w, int h) = icon.Place(weapon, mode)!.Value;
                // above the bar's ammo (left aligned) or the fullscreen HUD's (right aligned), wholly in the strip
                Assert.True(x >= 0 && y >= 0 && x + w <= icon.Screen.Width && y + h <= icon.Screen.Height);
                if (mode == HudMode.Bar)
                    Assert.Equal(WeaponIcon.BarX * WeaponIcon.Resolution, x);
                else
                    Assert.Equal(StStuff.FS_AMMOX * WeaponIcon.Resolution, x + w);
                Assert.True((y + h) / WeaponIcon.Resolution + WeaponIcon.Top < (mode == HudMode.Bar ? StStuff.ST_Y : StStuff.FS_Y));
                for (int py = 0; py < icon.Screen.Height; py++)
                {
                    for (int px = 0; px < icon.Screen.Width; px++)
                    {
                        bool inside = px >= x && px < x + w && py >= y && py < y + h;
                        bool opaque = inside && image.IsOpaque(px - x, py - y);
                        Assert.Equal(opaque, icon.Screen.Opaque[py * icon.Screen.Width + px] != 0);
                        if (opaque)
                            Assert.Equal(image[px - x, py - y], icon.Screen.Pixels[py * icon.Screen.Width + px]);
                    }
                }
            }
            // the ARMS digit brightened in a copy, the bar itself vanilla's
            Assert.Equal(barHash, st.Screen.Hash());
            int slot = WeaponIcon.ArmsSlot(weapon);
            Assert.Equal(slot, icon.Highlighted);
            if (slot < 0)
            {
                Assert.Null(icon.Bar);
                continue;
            }
            HudScreen bar = icon.Bar!;
            int changed = 0;
            for (int i = 0; i < bar.Pixels.Length; i++)
            {
                if (bar.Pixels[i] == st.Screen.Pixels[i])
                    continue;
                changed++;
                Assert.Equal(icon.BrightMap[st.Screen.Pixels[i]], bar.Pixels[i]);
                int x = i % bar.Width, y = i / bar.Width + StStuff.ST_Y;
                int dx = StStuff.ST_ARMSX + slot % 3 * StStuff.ST_ARMSXSPACE, dy = StStuff.ST_ARMSY + slot / 3 * StStuff.ST_ARMSYSPACE;
                Assert.InRange(x, dx - 8, dx + 8);
                Assert.InRange(y, dy - 8, dy + 8);
            }
            Assert.True(changed > 0, $"{weapon}: the ARMS digit is not brightened");
        }
    }

    [Fact]
    public void Doom2AChangeDimsAndTheOptionsTurnItOff()
    {
        var wad = WadArchive.Open(TestWads.RequireDoom2());
        var icon = new WeaponIcon(wad, Colormap.Load(wad), Playpal.Load(wad).GetPalette(0));
        var st = new StStuff(StStuff.Graphics.Load(wad), new DoomRandom());
        player_t p = Player(weapontype_t.wp_pistol);
        p.pendingweapon = weapontype_t.wp_plasma;
        st.ST_Start(p);
        st.ST_Ticker();
        st.ST_Drawer(true);
        icon.Draw(st, HudMode.Bar);
        Assert.True(icon.Dimmed);
        Assert.Equal(weapontype_t.wp_plasma, icon.Shown);
        (int x, int y, _, _) = icon.Place(weapontype_t.wp_plasma, HudMode.Bar)!.Value;
        IndexedImage image = icon.Icon(weapontype_t.wp_plasma)!;
        for (int py = 0; py < image.Height; py++)
        {
            for (int px = 0; px < image.Width; px++)
            {
                if (image.IsOpaque(px, py))
                    Assert.Equal(icon.DimMap[image[px, py]], icon.Screen.Pixels[(y + py) * icon.Screen.Width + x + px]);
            }
        }
        icon.Draw(st, HudMode.Off);
        Assert.DoesNotContain(icon.Screen.Opaque, o => o != 0);
        icon.Enabled = false;
        icon.Draw(st, HudMode.Bar);
        Assert.DoesNotContain(icon.Screen.Opaque, o => o != 0);
        Assert.NotNull(icon.Bar); // the digit alone
        icon.Digit = false;
        icon.Draw(st, HudMode.Bar);
        Assert.Null(icon.Bar);
        p.playerstate = playerstate_t.PST_DEAD;
        icon.Enabled = icon.Digit = true;
        icon.Draw(st, HudMode.Bar);
        Assert.Equal(weapontype_t.NUMWEAPONS, icon.Shown);
        Assert.Null(icon.Bar);
    }
}
