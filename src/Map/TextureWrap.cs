namespace IsoDoom.Map;

/// <summary>How wall textures repeat (a presentation option, SPEC §12, T2.5).</summary>
public enum WallTextureTiling
{
    /// <summary>
    /// As vanilla draws walls (default): columns wrap at r_data.c
    /// <c>texturewidthmask</c> (the largest power of two not above the
    /// width), rows at 128 (r_draw.c <c>R_DrawColumn</c>'s <c>&amp; 127</c>).
    /// A texture less than 128 tall shows "tutti-frutti" below its last row:
    /// vanilla reads past the end of the column there. That is emulated by
    /// reading on through the texture stored column by column, as vanilla's
    /// composite buffer is (row <c>h</c> of column <c>x</c> is row 0 of
    /// column <c>x + 1</c>, wrapping to column 0 after the last one); vanilla
    /// shows other garbage for columns it draws straight from a patch lump.
    /// </summary>
    Vanilla,

    /// <summary>Columns and rows wrap at the texture's real width and height (most source ports).</summary>
    TextureSize,
}

/// <summary>
/// The texel lookup of the level shader (<c>shaders/level.gdshader</c>) as
/// integer C#, the CPU reference for checks (T2.6). Columns and rows are
/// whole texels: a wall's <c>(TextureOffset + d) &gt;&gt; FRACBITS</c> and
/// <c>(TextureTop − z) &gt;&gt; FRACBITS</c> (<see cref="WallSection"/>), a
/// flat's map position.
/// </summary>
public static class TextureWrap
{
    /// <summary>r_data.c <c>R_InitTextures</c>: <c>texturewidthmask</c>, the largest power of two not above <paramref name="width"/>, minus one.</summary>
    public static int TextureWidthMask(int width)
    {
        int j = 1;
        while (j * 2 <= width)
            j <<= 1;
        return j - 1;
    }

    /// <summary>The texel (column, row) of a solid wall texture of the given size at an unwrapped column and row.</summary>
    public static (int Column, int Row) WallTexel(int column, int row, int width, int height, WallTextureTiling tiling)
    {
        if (tiling == WallTextureTiling.TextureSize)
            return (Mod(column, width), Mod(row, height));
        column &= TextureWidthMask(width); // r_data.c R_GetColumn
        row &= 127; // r_draw.c R_DrawColumn
        if (row >= height)
        {
            // Read on through the column-major texture (tutti-frutti).
            int i = (column * height + row) % (width * height);
            column = i / height;
            row = i % height;
        }
        return (column, row);
    }

    /// <summary>
    /// The texel (column, row) of a flat at map position (<paramref name="x"/>,
    /// <paramref name="y"/>) (fixed_t): r_draw.c <c>R_DrawSpan</c> with
    /// r_plane.c's <c>xfrac = x</c>, <c>yfrac = −y</c>, so flats are aligned
    /// to the 64-unit grid and map north is up in the flat.
    /// </summary>
    public static (int Column, int Row) FlatTexel(int x, int y) =>
        ((x >> Fixed.FRACBITS) & 63, ((-y) >> Fixed.FRACBITS) & 63);

    private static int Mod(int a, int n)
    {
        int m = a % n;
        return m < 0 ? m + n : m;
    }
}
