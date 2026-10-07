using System;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// A strip of vanilla's 320-wide screen (<c>screens[0]</c>) the HUD draws
/// into (T6.11): palette indices, row-major, with a mask of the pixels drawn
/// (the rest shows the level behind). <see cref="Top"/> is the strip's first
/// screen row, so the status bar (rows 168–199) draws with vanilla's
/// coordinates. No Godot types: the tests link it.
/// </summary>
public sealed class HudScreen
{
    /// <summary>doomdef.h <c>SCREENWIDTH</c>.</summary>
    public const int SCREENWIDTH = 320;

    /// <summary>doomdef.h <c>SCREENHEIGHT</c>.</summary>
    public const int SCREENHEIGHT = 200;

    public HudScreen(int top, int height)
    {
        Top = top;
        Height = height;
        Pixels = new byte[SCREENWIDTH * height];
        Opaque = new byte[SCREENWIDTH * height];
    }

    /// <summary>The screen row of the strip's first row.</summary>
    public int Top { get; }

    public int Width => SCREENWIDTH;
    public int Height { get; }

    /// <summary>Palette indices, <c>y * 320 + x</c> (y from <see cref="Top"/>).</summary>
    public byte[] Pixels { get; }

    /// <summary>1 where something is drawn, 0 where the strip is see-through.</summary>
    public byte[] Opaque { get; }

    /// <summary>Clears the strip to see-through.</summary>
    public void Clear()
    {
        Array.Clear(Pixels);
        Array.Clear(Opaque);
    }

    /// <summary>
    /// v_video.c <c>V_DrawPatch</c>: <paramref name="patch"/> at screen point
    /// <paramref name="x"/>, <paramref name="y"/> less its offsets; the
    /// pixels outside the strip are dropped (vanilla's are errors). A null
    /// patch (a lump the WAD lacks, e.g. the synthetic IWAD's) draws nothing.
    /// </summary>
    public void V_DrawPatch(int x, int y, IndexedImage? patch)
    {
        if (patch is null)
            return;
        x -= patch.LeftOffset;
        y -= patch.TopOffset + Top;
        for (int py = 0; py < patch.Height; py++)
        {
            int sy = y + py;
            if (sy < 0 || sy >= Height)
                continue;
            for (int px = 0; px < patch.Width; px++)
            {
                int sx = x + px;
                if (sx < 0 || sx >= SCREENWIDTH || !patch.IsOpaque(px, py))
                    continue;
                Pixels[sy * SCREENWIDTH + sx] = patch[px, py];
                Opaque[sy * SCREENWIDTH + sx] = 1;
            }
        }
    }

    /// <summary>
    /// v_video.c <c>V_CopyRect</c> from <paramref name="source"/> (the status
    /// bar's backing screen, same layout) at the same place: the rectangle
    /// <paramref name="x"/>, <paramref name="y"/> (screen rows),
    /// <paramref name="width"/> × <paramref name="height"/>.
    /// </summary>
    public void V_CopyRect(HudScreen source, int x, int y, int width, int height)
    {
        for (int sy = Math.Max(y - Top, 0); sy < Math.Min(y - Top + height, Height); sy++)
        {
            for (int sx = Math.Max(x, 0); sx < Math.Min(x + width, SCREENWIDTH); sx++)
            {
                Pixels[sy * SCREENWIDTH + sx] = source.Pixels[sy * SCREENWIDTH + sx];
                Opaque[sy * SCREENWIDTH + sx] = source.Opaque[sy * SCREENWIDTH + sx];
            }
        }
    }

    /// <summary>
    /// The strip as 32-bit words, the palette index of each pixel drawn and
    /// 256 for a see-through one, hashed with FNV-1a word-wise (the route
    /// dump's <c>hud</c> column, dump.c's <c>hud_hash</c>), continuing <paramref name="hash"/>.
    /// </summary>
    public uint Hash(uint hash = 2166136261u)
    {
        for (int i = 0; i < Pixels.Length; i++)
            hash = unchecked((hash ^ (Opaque[i] != 0 ? Pixels[i] : 256u)) * 16777619u);
        return hash;
    }

    /// <summary>The strip as RGBA8 through one PLAYPAL palette (768 bytes); see-through pixels are (0, 0, 0, 0).</summary>
    public byte[] ToRgba(ReadOnlySpan<byte> palette, byte[]? into = null)
    {
        byte[] rgba = into ?? new byte[Pixels.Length * 4];
        for (int i = 0; i < Pixels.Length; i++)
        {
            if (Opaque[i] == 0)
            {
                rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = rgba[i * 4 + 3] = 0;
                continue;
            }
            int c = Pixels[i] * 3;
            rgba[i * 4] = palette[c];
            rgba[i * 4 + 1] = palette[c + 1];
            rgba[i * 4 + 2] = palette[c + 2];
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }
}
