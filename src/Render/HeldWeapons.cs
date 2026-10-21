using System;
using System.Collections.Generic;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// The weapons the players' sprites hold (T6.14b, SPEC §12 "T6.14 Player
/// weapons"): each weapon's pickup sprite, looked up in the loaded WAD (so a
/// PWAD's replacement is used), with our own anchors and scale; the pistol,
/// which has no side view in any IWAD, our own art; the fist nothing (empty
/// hands). No Godot types: the tests link it.
/// </summary>
public static class HeldWeapons
{
    /// <summary>The pickup sprites, in <see cref="weapontype_t"/> order (null for the fist and the pistol, which have none). Shared with the HUD's weapon icon.</summary>
    public static IReadOnlyList<string?> PickupSprites { get; } =
        [null, null, "SHOTA0", "MGUNA0", "LAUNA0", "PLASA0", "BFUGA0", "CSAWA0", "SGN2A0"];

    /// <summary>The <see cref="HeldWeapon.Source"/> of our own pistol art.</summary>
    public const string PistolName = "#PISTOL";

    /// <summary>The first index of the player translation's green ramp (r_draw.c), which no held weapon's texel may use.</summary>
    public const int RampFirst = 0x70, RampLast = 0x7f;

    /// <summary>
    /// Per weapon (in <see cref="weapontype_t"/> order), our numbers: the grip
    /// and the muzzle as fractions of the image's width and height (so a
    /// PWAD's sprite of another size still gets sensible anchors), and the
    /// scale: the player's texels per weapon texel. Picked by eye from the
    /// enlarged sprites against the marine's side view (SPEC §12 "T6.14
    /// Player weapons").
    /// </summary>
    private static readonly Anchor?[] _anchors =
    [
        null,                                          // wp_fist: empty hands
        new(0.15, 0.68, 1.00, 0.25, 0.70),             // wp_pistol (our art): its grip; the slide's front
        new(0.30, 0.58, 1.00, 0.25, 0.50),             // wp_shotgun SHOTA0: the trigger; the barrel's end
        new(0.26, 0.66, 1.00, 0.45, 0.50),             // wp_chaingun MGUNA0: the handle under the body; the barrels' centre
        new(0.22, 0.62, 1.00, 0.38, 0.50),             // wp_missile LAUNA0: the handle; the tube's mouth
        new(0.24, 0.62, 1.00, 0.42, 0.50),             // wp_plasma PLASA0: the handle; the barrel's end
        new(0.30, 0.74, 1.00, 0.74, 0.45),             // wp_bfg BFUGA0: the handle; the emitters' centre (smaller: 0.5 covers the torso; 0.4 was too small, the user's review)
        new(0.22, 0.64, 1.00, 0.66, 0.45),             // wp_chainsaw CSAWA0: the engine's rear; the bar's tip
        new(0.33, 0.62, 1.00, 0.22, 0.50),             // wp_supershotgun SGN2A0: the trigger; the barrels' end
    ];

    private sealed record Anchor(double GripX, double GripY, double MuzzleX, double MuzzleY, double Scale);

    // Our own art (GPL-2.0-or-later, made for IsoDoom; no id pixels): a small
    // side-view pistol pointing right, slide over a grip and trigger guard,
    // in Doom's palette's greys. '.' is transparent.
    private static readonly string[] _pistolRows =
    [
        "..xxxxxxxxxxxx.",
        ".xhhhhhhhhhhhhx",
        ".xllllllllllllx",
        ".xmmmmmmmmmmmx.",
        ".xdmmxkkx......",
        "xddmx.k.x......",
        "xddx..xx.......",
        "xxx............",
    ];


    private static readonly Dictionary<char, byte> _pistolColours = new()
    {
        ['x'] = 0x00, // black outline
        ['h'] = 0x5d, // highlight
        ['l'] = 0x63, // slide
        ['m'] = 0x68, // frame
        ['d'] = 0x6c, // grip, shade
        ['k'] = 0x6f, // trigger
    };

    /// <summary>
    /// The held weapons in <see cref="weapontype_t"/> order: null for the fist
    /// and for a weapon whose pickup sprite <paramref name="wad"/> lacks (or
    /// cannot decode). <paramref name="playpal"/> is PLAYPAL (its first
    /// palette, 768 bytes, is used) for the ramp remapping.
    /// </summary>
    public static HeldWeapon?[] Load(WadArchive wad, byte[] playpal)
    {
        ArgumentNullException.ThrowIfNull(wad);
        ArgumentNullException.ThrowIfNull(playpal);
        if (playpal.Length < Playpal.PaletteSize)
            throw new ArgumentException($"A palette is {Playpal.PaletteSize} bytes.", nameof(playpal));
        byte[] remap = RampRemap(playpal);
        var weapons = new HeldWeapon?[(int)weapontype_t.NUMWEAPONS];
        for (int i = 0; i < weapons.Length; i++)
        {
            if (_anchors[i] is not Anchor a)
                continue;
            weapontype_t weapon = (weapontype_t)i;
            IndexedImage? image;
            string source;
            if (weapon == weapontype_t.wp_pistol)
            {
                image = Pistol();
                source = PistolName;
            }
            else
            {
                source = PickupSprites[i]!;
                image = Decode(wad, source);
            }
            if (image is null)
                continue;
            image = Remap(image, remap);
            weapons[i] = new HeldWeapon(weapon, image,
                (a.GripX * image.Width, a.GripY * image.Height),
                (a.MuzzleX * image.Width, a.MuzzleY * image.Height),
                a.Scale)
            { Source = source };
        }
        return weapons;
    }

    /// <summary>Our pistol art (15 × 8), offsets 0.</summary>
    public static IndexedImage Pistol()
    {
        int w = _pistolRows[0].Length, h = _pistolRows.Length;
        byte[] pixels = new byte[w * h], opaque = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                char c = _pistolRows[y][x];
                if (c == '.')
                    continue;
                pixels[y * w + x] = _pistolColours[c];
                opaque[y * w + x] = 1;
            }
        }
        return new IndexedImage(w, h, 0, 0, pixels, opaque);
    }

    /// <summary>
    /// For each palette index, itself, or for the green ramp (0x70–0x7F) the
    /// nearest colour of <paramref name="playpal"/>'s first palette outside
    /// the ramp (squared RGB distance; the lowest index on a tie).
    /// </summary>
    public static byte[] RampRemap(ReadOnlySpan<byte> playpal)
    {
        byte[] map = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            map[i] = (byte)i;
            if (i is < RampFirst or > RampLast)
                continue;
            int best = -1, bestDistance = int.MaxValue;
            for (int j = 0; j < 256; j++)
            {
                if (j is >= RampFirst and <= RampLast)
                    continue;
                int dr = playpal[i * 3] - playpal[j * 3];
                int dg = playpal[i * 3 + 1] - playpal[j * 3 + 1];
                int db = playpal[i * 3 + 2] - playpal[j * 3 + 2];
                int distance = dr * dr + dg * dg + db * db;
                if (distance < bestDistance)
                {
                    best = j;
                    bestDistance = distance;
                }
            }
            map[i] = (byte)best;
        }
        return map;
    }

    private static IndexedImage Remap(IndexedImage image, byte[] map)
    {
        byte[] pixels = new byte[image.Pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = map[image.Pixels[i]];
        return new IndexedImage(image.Width, image.Height, image.LeftOffset, image.TopOffset, pixels, (byte[])image.Opaque.Clone());
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
}
