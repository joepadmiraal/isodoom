using System;
using System.Diagnostics.CodeAnalysis;
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

    public HudScreen(int top, int height) : this(top, height, SCREENWIDTH)
    {
    }

    /// <summary>
    /// A strip <paramref name="width"/> pixels wide (T7.9: the automap's
    /// frame, vanilla's <c>f_w</c> × <c>f_h</c>, as wide as the window's
    /// pixels at the HUD's scale); the other strips are the screen's 320.
    /// </summary>
    public HudScreen(int top, int height, int width)
    {
        Top = top;
        Height = height;
        Width = width;
        Pixels = new byte[width * height];
        Opaque = new byte[width * height];
    }

    /// <summary>The screen row of the strip's first row.</summary>
    public int Top { get; }

    /// <summary>The strip's width (<see cref="SCREENWIDTH"/> but for the automap's frame, T7.9).</summary>
    public int Width { get; }
    public int Height { get; }

    /// <summary>Palette indices, <c>y * Width + x</c> (y from <see cref="Top"/>).</summary>
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
    /// M9: a patch wider than vanilla's screen (the 2024 re-release's
    /// widescreen <c>STBAR</c>, <c>TITLEPIC</c>, …) is centred on it
    /// (<see cref="WideShift"/>), as the reference's patched one (SPEC §12 M9).
    /// </summary>
    [SuppressMessage("Naming", "CA1707", Justification = "Vanilla name (v_video.c)")]
    public void V_DrawPatch(int x, int y, IndexedImage? patch) => V_DrawPatch(x, y, patch, default);

    /// <summary>
    /// <see cref="V_DrawPatch(int, int, IndexedImage?)"/> with each pixel
    /// through <paramref name="map"/> (a COLORMAP row, 256 bytes; empty: as
    /// it is). Not vanilla (T11.9d: the menus' greyed text).
    /// </summary>
    [SuppressMessage("Naming", "CA1707", Justification = "Vanilla name (v_video.c)")]
    public void V_DrawPatch(int x, int y, IndexedImage? patch, ReadOnlySpan<byte> map)
    {
        if (patch is null)
            return;
        bool mapped = map.Length >= 256;
        x -= patch.LeftOffset + WideShift(patch);
        y -= patch.TopOffset + Top;
        for (int py = 0; py < patch.Height; py++)
        {
            int sy = y + py;
            if (sy < 0 || sy >= Height)
                continue;
            for (int px = 0; px < patch.Width; px++)
            {
                int sx = x + px;
                if (sx < 0 || sx >= Width || !patch.IsOpaque(px, py))
                    continue;
                Pixels[sy * Width + sx] = mapped ? map[patch[px, py]] : patch[px, py];
                Opaque[sy * Width + sx] = 1;
            }
        }
    }

    /// <summary>
    /// M9: how far left a patch wider than vanilla's 320-wide screen is moved so
    /// its middle 320 columns cover it (the 2024 re-release's widescreen
    /// graphics: 560 or 576 wide, their 4:3 picture in the middle); 0 for the others.
    /// </summary>
    public static int WideShift(IndexedImage patch) =>
        patch.Width > SCREENWIDTH ? (patch.Width - SCREENWIDTH) / 2 : 0;

    /// <summary>
    /// v_video.c <c>V_DrawPatchFlipped</c> (T10.5, the cast call's): as
    /// <see cref="V_DrawPatch(int, int, IndexedImage?)"/> with the patch's columns mirrored (column
    /// <c>w - 1 - col</c> at <c>x + col</c>; the offsets as they are).
    /// </summary>
    [SuppressMessage("Naming", "CA1707", Justification = "Vanilla name (v_video.c)")]
    public void V_DrawPatchFlipped(int x, int y, IndexedImage? patch)
    {
        if (patch is null)
            return;
        x -= patch.LeftOffset + WideShift(patch);
        y -= patch.TopOffset + Top;
        for (int py = 0; py < patch.Height; py++)
        {
            int sy = y + py;
            if (sy < 0 || sy >= Height)
                continue;
            for (int col = 0; col < patch.Width; col++)
            {
                int sx = x + col, px = patch.Width - 1 - col;
                if (sx < 0 || sx >= Width || !patch.IsOpaque(px, py))
                    continue;
                Pixels[sy * Width + sx] = patch[px, py];
                Opaque[sy * Width + sx] = 1;
            }
        }
    }

    /// <summary>
    /// v_video.c <c>V_CopyRect</c> from <paramref name="source"/> (the status
    /// bar's backing screen, same layout) at the same place: the rectangle
    /// <paramref name="x"/>, <paramref name="y"/> (screen rows),
    /// <paramref name="width"/> × <paramref name="height"/>.
    /// </summary>
    [SuppressMessage("Naming", "CA1707", Justification = "Vanilla name (v_video.c)")]
    public void V_CopyRect(HudScreen source, int x, int y, int width, int height)
    {
        for (int sy = Math.Max(y - Top, 0); sy < Math.Min(y - Top + height, Height); sy++)
        {
            for (int sx = Math.Max(x, 0); sx < Math.Min(x + width, Width); sx++)
            {
                Pixels[sy * Width + sx] = source.Pixels[sy * Width + sx];
                Opaque[sy * Width + sx] = source.Opaque[sy * Width + sx];
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
