using System;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// T6.13y: the status bar wider than vanilla's 320 (SPEC §7.6; not vanilla,
/// presentation only), so on a wide window it fills the width at a smaller
/// scale and takes fewer rows. Vanilla's bar (<see cref="StStuff.Screen"/>, or
/// T6.13x's copy with the ARMS digit brightened) sits in the middle as it is;
/// a panel on each side: the 2024 re-release's own side art where its
/// <c>STBAR</c> is wide enough, else <c>FLAT5_4</c> tiled (the bar's grey
/// grain, in every IWAD) with the bar's bevels. The left panel's last
/// <see cref="BoxWidth"/> columns, beside the AMMO box (at 400 wide the whole
/// panel), hold the ready weapon's icon (T6.13x's, at half the HUD's scale:
/// <see cref="Resolution"/>), centred. No Godot types: the tests link it.
/// </summary>
public sealed class WideStatusBar
{
    /// <summary>The width <c>hud/bar_width</c> defaults to (the user's pick): a <see cref="BoxWidth"/> panel each side.</summary>
    public const int DefaultWidth = HudScreen.SCREENWIDTH + 2 * BoxWidth;

    /// <summary>The strip's pixels per screen pixel across and down (the icon's, <see cref="WeaponIcon.Resolution"/>).</summary>
    public const int Resolution = WeaponIcon.Resolution;

    /// <summary>The weapon box's width in screen pixels (<c>STARMS</c>'s).</summary>
    public const int BoxWidth = 40;

    /// <summary>The bevel colours (<c>STBAR</c>'s): a panel's first column, the bar's last column and bottom row, the weapon box's right edge, and the column right of the box's left edge.</summary>
    public const byte Light = 96, Dark = 109, Divider = 111, Shade = 103;

    private readonly IndexedImage? _wideArt;
    private readonly byte[]? _flat;

    /// <summary>The panels' art from <paramref name="wad"/>: its <c>STBAR</c> when wider than 320, else <c>FLAT5_4</c> (none: palette index 0).</summary>
    public WideStatusBar(WadArchive wad, int width)
    {
        Width = Math.Max(width, HudScreen.SCREENWIDTH);
        if (Decode(wad, "STBAR") is { Width: > HudScreen.SCREENWIDTH } bar && bar.Width >= Width)
            _wideArt = bar;
        else if (wad.Find("FLAT5_4") is { Data.Length: >= 4096 } flat)
            _flat = flat.Data.Span[..4096].ToArray();
        Screen = new HudScreen(StStuff.ST_Y, StStuff.ST_HEIGHT * Resolution, Width * Resolution);
    }

    /// <summary>The bar's width in screen pixels.</summary>
    public int Width { get; }

    /// <summary>Each side panel's width in screen pixels (the left one's last <see cref="BoxWidth"/> are the weapon box).</summary>
    public int Side => (Width - HudScreen.SCREENWIDTH) / 2;

    /// <summary>Whether the panels show the WAD's own wide <c>STBAR</c>.</summary>
    public bool WadArt => _wideArt is not null;

    /// <summary>The strip as last drawn: screen rows 168–199, <see cref="Width"/> wide, at <see cref="Resolution"/>.</summary>
    public HudScreen Screen { get; }

    /// <summary>Draws <paramref name="bar"/> (320 × 32) in the middle, the panels, and <paramref name="icon"/> (strip pixels; null: none) in the weapon box through <paramref name="map"/>.</summary>
    public void Draw(HudScreen bar, IndexedImage? icon, ReadOnlySpan<byte> map)
    {
        int side = Side, box = Math.Max(0, side - BoxWidth);
        for (int y = 0; y < StStuff.ST_HEIGHT; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int bx = x - side;
                byte c = bx is >= 0 and < HudScreen.SCREENWIDTH ? bar.Pixels[y * bar.Width + bx] : Panel(x, y, side, box);
                Put(x, y, c);
            }
        }
        if (icon is null || IconPlace(icon) is not (int ix, int iy))
            return;
        bool mapped = map.Length >= 256;
        for (int py = 0; py < icon.Height; py++)
        {
            for (int px = 0; px < icon.Width; px++)
            {
                if (icon.IsOpaque(px, py))
                    Screen.Pixels[(iy + py) * Screen.Width + ix + px] = mapped ? map[icon[px, py]] : icon[px, py];
            }
        }
    }

    /// <summary>Where <see cref="Draw"/> puts <paramref name="icon"/> in <see cref="Screen"/> (strip pixels: centred in the weapon box), or null without a box.</summary>
    public (int X, int Y)? IconPlace(IndexedImage icon) => Side < BoxWidth ? null
        : ((Side - BoxWidth) * Resolution + (BoxWidth * Resolution - icon.Width) / 2, (StStuff.ST_HEIGHT * Resolution - icon.Height) / 2);

    /// <summary>A panel pixel: the art, then the bevels (the outer edges and the weapon box's).</summary>
    private byte Panel(int x, int y, int side, int box)
    {
        if (_wideArt is null)
        {
            if (y == StStuff.ST_HEIGHT - 1 || x == Width - 1)
                return Dark;
            if (x == 0 || x == box || x == side + HudScreen.SCREENWIDTH)
                return Light;
        }
        if (side >= BoxWidth)
        {
            if (x == side - 1 || (box > 0 && x == box - 1))
                return Divider;
            if (x == box + 1 && box > 0)
                return Shade;
        }
        if (_wideArt is { } art)
            return art[x + (art.Width - Width) / 2, y];
        return _flat is null ? (byte)0 : _flat[(y % 64) * 64 + x % 64];
    }

    private void Put(int x, int y, byte c)
    {
        for (int dy = 0; dy < Resolution; dy++)
        {
            int row = (y * Resolution + dy) * Screen.Width + x * Resolution;
            for (int dx = 0; dx < Resolution; dx++)
            {
                Screen.Pixels[row + dx] = c;
                Screen.Opaque[row + dx] = 1;
            }
        }
    }

    private static IndexedImage? Decode(WadArchive wad, string name)
    {
        if (wad.Find(name) is not { } lump)
            return null;
        try
        {
            return Patch.Decode(lump.Data.Span, lump.Name);
        }
        catch (WadFormatException)
        {
            return null;
        }
    }

    /// <summary>The icon the weapon box shows for <paramref name="player"/> (T6.13x's pick) and whether dimmed, or null.</summary>
    public static (IndexedImage? Icon, bool Dim) IconFor(WeaponIcon weapon, player_t? player)
    {
        if (!weapon.Enabled || player is null)
            return (null, false);
        (weapontype_t w, bool dim) = WeaponIcon.Pick(player);
        return w >= weapontype_t.NUMWEAPONS ? (null, false) : (weapon.Icon(w), dim);
    }
}
