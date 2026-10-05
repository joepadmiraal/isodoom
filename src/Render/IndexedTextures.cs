using System;
using Godot;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// Uploads 8-bit indexed graphics and the palette tables to the GPU (SPEC §5.2:
/// graphics stay indexed, the palette is applied in <c>shaders/palette.gdshader</c>).
/// <list type="bullet">
/// <item>Index images become <see cref="Image.Format.Rg8"/> textures: R is the
/// palette index, G is 255 where the pixel is drawn and 0 where it is
/// transparent (index 0 is black, so it can't double as "clear").</item>
/// <item><c>PLAYPAL</c> becomes an RGB8 texture, 256 wide, one row per palette.</item>
/// <item><c>COLORMAP</c> becomes an R8 texture, 256 wide, one row per map.</item>
/// </list>
/// All of them must be sampled with nearest filtering and no mipmaps.
/// </summary>
public static class IndexedTextures
{
    public const string ShaderPath = "res://shaders/palette.gdshader";

    /// <summary>The RG8 bytes for <paramref name="image"/>: (index, opacity * 255) per pixel, row-major.</summary>
    public static byte[] ToRg8(IndexedImage image)
    {
        byte[] data = new byte[image.Pixels.Length * 2];
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            data[i * 2] = image.Pixels[i];
            data[i * 2 + 1] = image.Opaque[i] != 0 ? (byte)255 : (byte)0;
        }
        return data;
    }

    /// <summary>The index image as an RG8 <see cref="Image"/> (no mipmaps).</summary>
    public static Image CreateImage(IndexedImage image) =>
        Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rg8, ToRg8(image));

    /// <summary>The index image as an RG8 texture for the palette shader.</summary>
    public static ImageTexture CreateTexture(IndexedImage image) => ImageTexture.CreateFromImage(CreateImage(image));

    /// <summary>PLAYPAL as an RGB8 texture: 256 x <see cref="Playpal.Count"/>.</summary>
    public static ImageTexture CreatePlaypalTexture(Playpal playpal) =>
        ImageTexture.CreateFromImage(Image.CreateFromData(256, playpal.Count, false, Image.Format.Rgb8, playpal.Data.ToArray()));

    /// <summary>COLORMAP as an R8 texture: 256 x <see cref="Colormap.Count"/>.</summary>
    public static ImageTexture CreateColormapTexture(Colormap colormap) =>
        ImageTexture.CreateFromImage(Image.CreateFromData(Colormap.MapSize, colormap.Count, false, Image.Format.R8, colormap.Data.ToArray()));

    /// <summary>
    /// A palette shader material bound to the two lookup textures. Items that
    /// share the material share its palette and colormap selection.
    /// </summary>
    public static ShaderMaterial CreatePaletteMaterial(Texture2D playpal, Texture2D colormap)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
        material.SetShaderParameter("playpal", playpal);
        material.SetShaderParameter("colormap", colormap);
        SetPalette(material, 0);
        SetColormap(material, 0);
        return material;
    }

    /// <summary>Selects the PLAYPAL palette (0 normal, 1–8 red, 9–12 gold, 13 radiation suit).</summary>
    public static void SetPalette(ShaderMaterial material, int palette) =>
        material.SetShaderParameter("palette_index", palette);

    /// <summary>Selects the COLORMAP row (0–31 light, 32 invulnerability).</summary>
    public static void SetColormap(ShaderMaterial material, int map) =>
        material.SetShaderParameter("colormap_index", map);

    /// <summary>
    /// The colormap the viewer uses for a sector light level (0–255): a
    /// straight 8 light units per map, <c>(255 - light) / 8</c>, so 255 is
    /// map 0 (full bright) and 0–7 is map 31. No distance diminishing; the
    /// level renderer decides its own mapping (SPEC §13, question 2).
    /// </summary>
    public static int ViewerLightToColormap(int light) =>
        (Colormap.NUMCOLORMAPS - 1) - (Math.Clamp(light, 0, 255) >> 3);
}
