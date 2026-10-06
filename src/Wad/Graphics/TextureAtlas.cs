using System;
using System.Collections.Generic;

namespace IsoDoom.Wad.Graphics;

/// <summary>A rectangle of a <see cref="TextureAtlas"/>, in texels.</summary>
public readonly record struct AtlasRect(int X, int Y, int Width, int Height);

/// <summary>
/// Packs indexed images (wall textures and flats) into one indexed atlas
/// image, so the level renderer binds a single texture for every surface
/// (SPEC §12, T2.5). Shelf packing: images sorted by height (then width,
/// then input order, so the layout is deterministic), placed left to right
/// in rows as tall as their first image. Images are copied with their
/// opacity mask; the unused area is transparent index 0. There is no
/// padding: the shader computes integer texel coordinates and wraps them
/// itself (<c>texelFetch</c>), so no sample ever crosses a rectangle's edge.
/// </summary>
public sealed class TextureAtlas
{
    /// <summary>The widest atlas made (the usual GPU texture size limit).</summary>
    public const int MaxSize = 16384;

    private TextureAtlas(IndexedImage image, AtlasRect[] rects)
    {
        Image = image;
        Rects = rects;
    }

    /// <summary>The atlas (indices and opacity).</summary>
    public IndexedImage Image { get; }

    /// <summary>Where each input image sits, in input order.</summary>
    public IReadOnlyList<AtlasRect> Rects { get; }

    /// <summary>
    /// The shelf layout for images of the given sizes: the rectangles (input
    /// order) and the atlas size. The width is the smallest power of two at
    /// least the widest image and at least the square root of the total area
    /// (at least 64), so the atlas is roughly square.
    /// </summary>
    public static (AtlasRect[] Rects, int Width, int Height) Layout(IReadOnlyList<(int Width, int Height)> sizes)
    {
        long area = 0;
        int widest = 1;
        foreach ((int w, int h) in sizes)
        {
            if (w <= 0 || h <= 0)
                throw new ArgumentException($"image size {w}x{h} is empty");
            area += (long)w * h;
            widest = Math.Max(widest, w);
        }
        int width = 64;
        while (width < widest || (long)width * width < area)
            width *= 2;
        if (width > MaxSize)
            throw new ArgumentException($"atlas would be {width} texels wide (more than {MaxSize})");

        int[] order = new int[sizes.Count];
        for (int i = 0; i < order.Length; i++)
            order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int c = sizes[b].Height.CompareTo(sizes[a].Height);
            if (c == 0)
                c = sizes[b].Width.CompareTo(sizes[a].Width);
            return c != 0 ? c : a.CompareTo(b);
        });

        var rects = new AtlasRect[sizes.Count];
        int x = 0, y = 0, shelf = 0;
        foreach (int i in order)
        {
            (int w, int h) = sizes[i];
            if (x + w > width)
            {
                y += shelf;
                x = 0;
                shelf = 0;
            }
            rects[i] = new AtlasRect(x, y, w, h);
            x += w;
            shelf = Math.Max(shelf, h);
        }
        int height = Math.Max(1, y + shelf);
        if (height > MaxSize)
            throw new ArgumentException($"atlas would be {height} texels tall (more than {MaxSize})");
        return (rects, width, height);
    }

    /// <summary>Packs <paramref name="images"/> (see the class remarks).</summary>
    public static TextureAtlas Build(IReadOnlyList<IndexedImage> images)
    {
        var sizes = new (int, int)[images.Count];
        for (int i = 0; i < images.Count; i++)
            sizes[i] = (images[i].Width, images[i].Height);
        (AtlasRect[] rects, int width, int height) = Layout(sizes);

        byte[] pixels = new byte[width * height];
        byte[] opaque = new byte[width * height];
        for (int i = 0; i < images.Count; i++)
        {
            IndexedImage image = images[i];
            AtlasRect r = rects[i];
            for (int row = 0; row < image.Height; row++)
            {
                int src = row * image.Width;
                int dst = (r.Y + row) * width + r.X;
                Array.Copy(image.Pixels, src, pixels, dst, image.Width);
                Array.Copy(image.Opaque, src, opaque, dst, image.Width);
            }
        }
        return new TextureAtlas(new IndexedImage(width, height, 0, 0, pixels, opaque), rects);
    }
}
