using System;
using System.Collections.Generic;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// The full screens of the game states (T7.1): vanilla's 320×200 screen
/// (<see cref="HudScreen"/>) drawn with the WAD's patches, flats and the
/// message font (<see cref="HuStuff.hu_font"/>), cached by name. Draws the
/// title loop's pages (d_main.c <c>D_PageDrawer</c>), the pause graphic
/// (<c>D_Display</c>'s <c>M_PAUSE</c>), and what the intermission and the
/// finale draw. A lump the WAD lacks (the synthetic IWAD's) draws nothing,
/// or its stand-in text. No Godot types: the tests link it.
/// </summary>
public sealed class ScreenGraphics
{
    private readonly WadArchive? _wad;
    private readonly IndexedImage?[] _font;
    private readonly Dictionary<string, IndexedImage?> _patches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IndexedImage?> _flats = new(StringComparer.OrdinalIgnoreCase);

    public ScreenGraphics(WadArchive? wad, HuStuff? hu)
    {
        this._wad = wad;
        _font = hu?.hu_font ?? new IndexedImage?[HuStuff.HU_FONTSIZE];
    }

    /// <summary>The patch lump <paramref name="name"/>, or null when the WAD lacks it (or it is no patch).</summary>
    public IndexedImage? Patch(string name)
    {
        if (_patches.TryGetValue(name, out IndexedImage? image))
            return image;
        image = null;
        if (_wad?.Find(name) is { } lump)
        {
            try
            {
                image = Wad.Graphics.Patch.Decode(lump.Data.Span, lump.Name);
            }
            catch (WadFormatException)
            {
            }
        }
        _patches[name] = image;
        return image;
    }

    /// <summary>The flat <paramref name="name"/> (the flats' namespace), or null.</summary>
    public IndexedImage? Flat(string name)
    {
        if (_flats.TryGetValue(name, out IndexedImage? image))
            return image;
        image = null;
        if (_wad?.Find(name, LumpNamespace.Flats) is { } lump)
        {
            try
            {
                image = Wad.Graphics.Flat.Decode(lump.Data.Span, lump.Name);
            }
            catch (WadFormatException)
            {
            }
        }
        _flats[name] = image;
        return image;
    }

    /// <summary>d_main.c <c>D_PageDrawer</c>: patch <paramref name="name"/> at 0, 0 (a full-screen picture).</summary>
    public void DrawPage(HudScreen screen, string name) => screen.V_DrawPatch(0, 0, Patch(name));

    /// <summary>
    /// d_main.c <c>D_Display</c>'s pause graphic: <c>M_PAUSE</c> centred at
    /// the top of the view (the view is the whole screen here), over
    /// whatever shows; "PAUSE" in the message font without it.
    /// </summary>
    public void DrawPause(HudScreen screen)
    {
        if (Patch("M_PAUSE") is { } pause)
            screen.V_DrawPatch((HudScreen.SCREENWIDTH - 68) / 2, 4, pause);
        else
            DrawText(screen, (HudScreen.SCREENWIDTH - TextWidth("PAUSE")) / 2, 4, "PAUSE");
    }

    /// <summary>f_finale.c <c>F_TextWrite</c>'s background: flat <paramref name="name"/> tiled over the screen (black without it).</summary>
    public void TileFlat(HudScreen screen, string name)
    {
        IndexedImage? flat = Flat(name);
        for (int y = 0; y < screen.Height; y++)
        {
            for (int x = 0; x < screen.Width; x++)
            {
                int i = y * screen.Width + x;
                screen.Pixels[i] = flat is null ? (byte)0 : flat[x & 63, (y + screen.Top) & 63];
                screen.Opaque[i] = 1;
            }
        }
    }

    /// <summary>The message font's height (<c>STCFN033</c>'s; 7 without the font).</summary>
    public int FontHeight => _font[0]?.Height ?? 7;

    private IndexedImage? Glyph(char ch)
    {
        int c = char.ToUpperInvariant(ch);
        return c > ' ' && c >= HuStuff.HU_FONTSTART && c <= HuStuff.HU_FONTEND ? _font[c - HuStuff.HU_FONTSTART] : null;
    }

    /// <summary>Whether the message font has <paramref name="ch"/> (upper case; not a space).</summary>
    public bool HasGlyph(char ch) => Glyph(ch) is not null;

    /// <summary>A character's width in the message font (4 for a space or one the font lacks, as hu_lib.c and f_finale.c).</summary>
    public int CharWidth(char ch) => Glyph(ch)?.Width ?? 4;

    /// <summary>Draws a character of the message font with its top left at <paramref name="x"/>, <paramref name="y"/>.</summary>
    public void DrawChar(HudScreen screen, int x, int y, char ch) => screen.V_DrawPatch(x, y, Glyph(ch));

    /// <summary>The width of <paramref name="text"/> in the message font.</summary>
    public int TextWidth(string text)
    {
        int w = 0;
        foreach (char ch in text)
            w += CharWidth(ch);
        return w;
    }

    /// <summary><paramref name="text"/> in the message font (upper case) from <paramref name="x"/>, <paramref name="y"/>.</summary>
    public void DrawText(HudScreen screen, int x, int y, string text)
    {
        foreach (char ch in text)
        {
            DrawChar(screen, x, y, ch);
            x += CharWidth(ch);
        }
    }

    /// <summary>Patch <paramref name="patch"/> at <paramref name="x"/>, <paramref name="y"/>, or <paramref name="text"/> there without it.</summary>
    public void DrawLabel(HudScreen screen, int x, int y, string patch, string text)
    {
        if (Patch(patch) is { } p)
            screen.V_DrawPatch(x, y, p);
        else
            DrawText(screen, x, y, text);
    }
}
