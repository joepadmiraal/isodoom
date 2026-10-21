using System;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// The ready weapon in the HUD (T6.13x, SPEC §7.6, §12; not vanilla, which
/// shows it as the first-person sprite this game does not draw, SPEC §6.3
/// #4). Its icon: the weapon's pickup sprite (the fist's and pistol's, which
/// have none, their first-person ready frames trimmed and scaled down to the
/// icon box), at half the HUD's scale (<see cref="Resolution"/>), in its own
/// strip of vanilla's screen above the status bar, so the bar's pixels stay
/// vanilla's: with the bar just above its ammo counter, with the fullscreen
/// HUD just above the ammo at the right. The weapon asked for (while the old
/// one lowers) and then the one raised show dimmed until it is up; nothing
/// while the player is dead. And, with <see cref="Digit"/>, the bar's ARMS
/// digit of the weapon brighter, in a copy of the bar (<see cref="Bar"/>):
/// <see cref="StStuff.Screen"/> itself stays vanilla's. No Godot types: the
/// tests link it.
/// </summary>
public sealed class WeaponIcon
{
    /// <summary>The strip's first screen row (the icons are at most <see cref="BoxHeight"/> strip pixels tall).</summary>
    public const int Top = 120;

    /// <summary>The strip's pixels per screen pixel across and down: the icons at half the HUD's scale.</summary>
    public const int Resolution = 2;

    /// <summary>The icon box in strip pixels: the widest pickup sprite (<c>SHOTA0</c>, 63) and the tallest (<c>BFUGA0</c>, 36).</summary>
    public const int BoxWidth = 64, BoxHeight = 36;

    /// <summary>The bar's icon: left edge and bottom row (screen pixels; one row clear of the bar).</summary>
    public const int BarX = 2, BarBottom = StStuff.ST_Y - 2;

    /// <summary>The fullscreen HUD's icon: right edge (the ammo's) and bottom row (screen pixels).</summary>
    public const int FullRight = StStuff.FS_AMMOX, FullBottom = StStuff.FS_Y - 3;

    /// <summary>The COLORMAP row a weapon still coming up is drawn through.</summary>
    public const int DimRow = 14;

    /// <summary>How far each colour of the highlighted ARMS digit goes towards white (then the palette's nearest), in percent.</summary>
    public const int BrightenPercent = 60;

    /// <summary>The first-person ready frames of the fist and the pistol.</summary>
    private static readonly string[] _handSprites = ["PUNGA0", "PISGA0"];

    private readonly IndexedImage?[] _icons = new IndexedImage?[(int)weapontype_t.NUMWEAPONS];
    private readonly byte[]? _dim;
    private readonly byte[]? _bright;
    private readonly HudScreen _bar = new(StStuff.ST_Y, StStuff.ST_HEIGHT);

    /// <summary>
    /// The icons from <paramref name="wad"/>'s sprites (null where it lacks
    /// one: drawn as nothing), dimmed through <paramref name="colormap"/>'s
    /// <see cref="DimRow"/>, the digit brightened in <paramref name="palette"/>
    /// (PLAYPAL's first, 768 bytes; empty: no highlight).
    /// </summary>
    public WeaponIcon(WadArchive wad, Colormap? colormap, ReadOnlySpan<byte> palette)
    {
        _dim = colormap is { Count: > DimRow } ? colormap.GetMap(DimRow).ToArray() : null;
        _bright = palette.Length >= 768 ? Brighten(palette) : null;
        for (int i = 0; i < _icons.Length; i++)
            _icons[i] = HeldWeapons.PickupSprites[i] is string name ? Get(wad, name) : HandIcon(wad, _handSprites[i]);
    }

    /// <summary>Whether the icon shows (<c>hud/weapon_icon</c>).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the bar's ARMS digit of the weapon is drawn brighter (<c>hud/weapon_digit</c>).</summary>
    public bool Digit { get; set; } = true;

    /// <summary>The strip as last drawn: screen rows <see cref="Top"/>–199 at <see cref="Resolution"/>, see-through where nothing is.</summary>
    public HudScreen Screen { get; } = new(Top, (HudScreen.SCREENHEIGHT - Top) * Resolution, HudScreen.SCREENWIDTH * Resolution);

    /// <summary>The status bar with the weapon's digit brightened, as last drawn, or null when <see cref="StStuff.Screen"/> shows as it is.</summary>
    public HudScreen? Bar { get; private set; }

    /// <summary>The weapon <see cref="Draw"/> last drew (<see cref="weapontype_t.NUMWEAPONS"/> for none) and whether dimmed.</summary>
    public weapontype_t Shown { get; private set; } = weapontype_t.NUMWEAPONS;

    public bool Dimmed { get; private set; }

    /// <summary>The ARMS slot (0–5: weapons 2–7) <see cref="Draw"/> last brightened, or −1.</summary>
    public int Highlighted { get; private set; } = -1;

    /// <summary>The icon of <paramref name="weapon"/> (strip pixels), or null where the WAD lacks it.</summary>
    public IndexedImage? Icon(weapontype_t weapon) => _icons[(int)weapon];

    /// <summary>The dimming map (<see cref="DimRow"/>; empty without a COLORMAP).</summary>
    public ReadOnlySpan<byte> DimMap => _dim;

    /// <summary>The digit's brightening map (empty without a palette).</summary>
    public ReadOnlySpan<byte> BrightMap => _bright;

    /// <summary>
    /// The weapon <paramref name="player"/>'s icon shows and whether dimmed:
    /// the one asked for while the old one lowers, then the one raised until
    /// it is ready; none while dead.
    /// </summary>
    public static (weapontype_t Weapon, bool Dim) Pick(player_t player)
    {
        if (player.playerstate != playerstate_t.PST_LIVE)
            return (weapontype_t.NUMWEAPONS, false);
        if (player.pendingweapon is not weapontype_t.wp_nochange and < weapontype_t.NUMWEAPONS)
            return (player.pendingweapon, true);
        weapontype_t ready = player.readyweapon;
        if (ready >= weapontype_t.NUMWEAPONS)
            return (weapontype_t.NUMWEAPONS, false);
        return (ready, player.psprites[World.ps_weapon].state == Info.weaponinfo[(int)ready].upstate);
    }

    /// <summary>The ARMS slot of <paramref name="weapon"/> (st_stuff.c's <c>w_arms[i]</c>: weapon <c>i + 1</c>, the super shotgun the shotgun's), or −1 (the fist and the chainsaw have none).</summary>
    public static int ArmsSlot(weapontype_t weapon) => weapon switch
    {
        weapontype_t.wp_supershotgun => (int)weapontype_t.wp_shotgun - 1,
        >= weapontype_t.wp_pistol and <= weapontype_t.wp_bfg => (int)weapon - 1,
        _ => -1,
    };

    /// <summary>Where <see cref="Draw"/> puts <paramref name="weapon"/>'s icon in <see cref="Screen"/> (strip pixels), or null.</summary>
    public (int X, int Y, int W, int H)? Place(weapontype_t weapon, HudMode mode)
    {
        if (mode == HudMode.Off || weapon >= weapontype_t.NUMWEAPONS || _icons[(int)weapon] is not IndexedImage icon)
            return null;
        int x = mode == HudMode.Bar ? BarX * Resolution : FullRight * Resolution - icon.Width;
        int y = ((mode == HudMode.Bar ? BarBottom : FullBottom) + 1 - Top) * Resolution - icon.Height;
        return (x, y, icon.Width, icon.Height);
    }

    /// <summary>
    /// Draws the icon of <paramref name="st"/>'s player's weapon for the HUD
    /// <paramref name="mode"/> (none with <see cref="HudMode.Off"/> or
    /// <see cref="Enabled"/> off) and, with <see cref="Digit"/>, <see cref="Bar"/>
    /// from the bar as <see cref="StStuff.ST_Drawer"/> last drew it.
    /// </summary>
    public void Draw(StStuff? st, HudMode mode)
    {
        Screen.Clear();
        Shown = weapontype_t.NUMWEAPONS;
        Dimmed = false;
        Highlighted = -1;
        Bar = null;
        if (st?.plyr is not player_t player)
            return;
        (weapontype_t weapon, bool dim) = Pick(player);
        if (weapon >= weapontype_t.NUMWEAPONS)
            return;
        if (Enabled && Place(weapon, mode) is { } at)
        {
            Shown = weapon;
            Dimmed = dim;
            Blit(Screen, at.X, at.Y, _icons[(int)weapon]!, dim ? _dim : null);
        }
        int slot = ArmsSlot(weapon);
        if (!Digit || _bright is null || slot < 0 || !st.st_armson || !player.weaponowned[(int)weapon] || st.G.arms[slot][1] is not IndexedImage digit)
            return;
        // the yellow digit st_stuff.c's w_arms[slot] draws for a weapon owned, brightened over a copy of the bar
        Array.Copy(st.Screen.Pixels, _bar.Pixels, _bar.Pixels.Length);
        Array.Copy(st.Screen.Opaque, _bar.Opaque, _bar.Opaque.Length);
        int dx = StStuff.ST_ARMSX + slot % 3 * StStuff.ST_ARMSXSPACE - digit.LeftOffset;
        int dy = StStuff.ST_ARMSY + slot / 3 * StStuff.ST_ARMSYSPACE - digit.TopOffset - StStuff.ST_Y;
        Blit(_bar, dx, dy, digit, _bright);
        Highlighted = slot;
        Bar = _bar;
    }

    private static void Blit(HudScreen screen, int x, int y, IndexedImage image, byte[]? map)
    {
        for (int py = 0; py < image.Height; py++)
        {
            int sy = y + py;
            if (sy < 0 || sy >= screen.Height)
                continue;
            for (int px = 0; px < image.Width; px++)
            {
                int sx = x + px;
                if (sx < 0 || sx >= screen.Width || !image.IsOpaque(px, py))
                    continue;
                byte c = image[px, py];
                screen.Pixels[sy * screen.Width + sx] = map is null ? c : map[c];
                screen.Opaque[sy * screen.Width + sx] = 1;
            }
        }
    }

    /// <summary>Each palette colour moved <see cref="BrightenPercent"/> towards white, as the palette's nearest index.</summary>
    private static byte[] Brighten(ReadOnlySpan<byte> palette)
    {
        byte[] map = new byte[256];
        for (int c = 0; c < 256; c++)
        {
            int r = Up(palette[c * 3]), g = Up(palette[c * 3 + 1]), b = Up(palette[c * 3 + 2]);
            int best = 0, bestDistance = int.MaxValue;
            for (int i = 0; i < 256; i++)
            {
                int dr = palette[i * 3] - r, dg = palette[i * 3 + 1] - g, db = palette[i * 3 + 2] - b;
                int distance = dr * dr + dg * dg + db * db;
                if (distance < bestDistance)
                    (best, bestDistance) = (i, distance);
            }
            map[c] = (byte)best;
        }
        return map;

        static int Up(int v) => v + (255 - v) * BrightenPercent / 100;
    }

    private static IndexedImage? Get(WadArchive wad, string name)
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

    /// <summary>A first-person frame trimmed to its opaque pixels and scaled down (nearest, never blurred) to fit the icon box, its aspect kept.</summary>
    private static IndexedImage? HandIcon(WadArchive wad, string name)
    {
        if (Get(wad, name) is not IndexedImage frame)
            return null;
        frame = Trim(frame);
        int w = Math.Min(BoxWidth, frame.Width), h = Math.Max(1, frame.Height * w / frame.Width);
        if (h > BoxHeight)
            (w, h) = (Math.Max(1, frame.Width * BoxHeight / frame.Height), BoxHeight);
        return Scale(frame, w, h);
    }

    private static IndexedImage Scale(IndexedImage src, int w, int h)
    {
        byte[] pixels = new byte[w * h], opaque = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            int sy = y * src.Height / h;
            for (int x = 0; x < w; x++)
            {
                int sx = x * src.Width / w;
                pixels[y * w + x] = src[sx, sy];
                opaque[y * w + x] = src.Opaque[sy * src.Width + sx];
            }
        }
        return new IndexedImage(w, h, 0, 0, pixels, opaque);
    }

    private static IndexedImage Trim(IndexedImage src)
    {
        int x0 = src.Width, y0 = src.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                if (src.IsOpaque(x, y))
                    (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
            }
        }
        if (x1 < 0)
            return src;
        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        byte[] pixels = new byte[w * h], opaque = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            Array.Copy(src.Pixels, (y0 + y) * src.Width + x0, pixels, y * w, w);
            Array.Copy(src.Opaque, (y0 + y) * src.Width + x0, opaque, y * w, w);
        }
        return new IndexedImage(w, h, 0, 0, pixels, opaque);
    }
}
