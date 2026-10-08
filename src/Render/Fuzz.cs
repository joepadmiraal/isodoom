using System.Diagnostics.CodeAnalysis;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// Partial invisibility's fuzz (T6.9, SPEC §6.3 #6, §12 T6.9): the CPU side
/// of the sprite shader's <c>FUZZ</c> variant (<c>shaders/sprite_fuzz.gdshader</c>)
/// for things with <c>MF_SHADOW</c> (<see cref="ThingSprites.FlagShadow"/>).
/// As r_draw.c <c>R_DrawFuzzColumn</c>, every opaque texel shows what is drawn
/// one patch row below or above it on screen (<see cref="fuzzoffset"/> at
/// <c>fuzzpos</c> = (<see cref="Phase"/> + column × height + row) mod 50;
/// runs of rows above compound, <see cref="Source"/>), matched to its nearest
/// palette index (<see cref="NearestIndex"/>) and mapped through COLORMAP row
/// <see cref="FuzzColormap"/>. The level check
/// compares the drawn pixels with this.
/// </summary>
public static class Fuzz
{
    /// <summary>r_draw.c <c>FUZZTABLE</c>.</summary>
    public const int FUZZTABLE = 50;

    /// <summary>r_draw.c <c>R_DrawFuzzColumn</c>'s colormap (<c>colormaps[6*256 + …]</c>).</summary>
    public const int FuzzColormap = 6;

    /// <summary>
    /// r_draw.c <c>fuzzoffset</c>, in rows: +1 (<c>FUZZOFF</c>, the row
    /// below) or −1 (the row above). The shader's <c>FUZZ_OFFSETS</c>.
    /// </summary>
    [SuppressMessage("Style", "IDE1006", Justification = "Vanilla name (r_draw.c)")]
    public static readonly int[] fuzzoffset =
    [
        1, -1, 1, -1, 1, 1, -1,
        1, 1, -1, 1, 1, 1, -1,
        1, 1, 1, -1, -1, -1, -1,
        1, -1, -1, 1, 1, 1, 1, -1,
        1, -1, 1, 1, -1, -1, 1,
        1, -1, -1, -1, -1, 1, 1,
        1, 1, -1, 1, 1, -1, 1,
    ];

    /// <summary>
    /// The shader's <c>fuzz_phase</c> (vanilla's <c>fuzzpos</c> at a fuzzed
    /// thing's first texel) for <paramref name="leveltime"/>: vanilla's
    /// <c>fuzzpos</c> runs on through every fuzzed pixel of every frame, so the
    /// pattern shimmers; here it steps by 23 (coprime with 50, so every phase
    /// comes round) each tic, deterministic for screenshots.
    /// </summary>
    public static int Phase(int leveltime) => (int)((uint)leveltime * 23u % FUZZTABLE);

    /// <summary>The row offset (+1 below, −1 above) of the texel in column <paramref name="col"/> and row <paramref name="row"/> of a patch <paramref name="height"/> rows high, its columns as drawn on screen (left to right).</summary>
    public static int Offset(int phase, int col, int row, int height) =>
        fuzzoffset[(int)(((long)phase + (long)col * height + row) % FUZZTABLE)];

    /// <summary>
    /// Where the fuzzed texel in column <paramref name="col"/> (as drawn on
    /// screen) and row <paramref name="row"/> of a patch <paramref name="height"/>
    /// rows high takes its colour from: <c>R_DrawFuzzColumn</c> reads the row
    /// below (<c>+FUZZOFF</c>, not drawn yet) or above (<c>−FUZZOFF</c>); the
    /// row above is already fuzzed when it is opaque (in the column's post),
    /// so a run of <c>−FUZZOFF</c> entries walks up, darkening once more per
    /// row. Returns the source row's offset from <paramref name="row"/> (in
    /// patch rows, read from what was drawn before the thing; 0 when the row
    /// above read this row's pixel before it was fuzzed) and how many
    /// times colormap <see cref="FuzzColormap"/> applies (at least 1). The
    /// shader's walk in its <c>FUZZ</c> fragment.
    /// </summary>
    public static (int RowOffset, int Darkenings) Source(int phase, int col, int row, int height, System.Func<int, bool> opaqueRow)
    {
        int j = row, darkenings = 1;
        for (int n = 0; n < FUZZTABLE; n++)
        {
            if (Offset(phase, col, j, height) > 0)
                return (j + 1 - row, darkenings);
            if (!opaqueRow(j - 1))
                return (j - 1 - row, darkenings);
            j--;
            darkenings++;
        }
        return (1, 1); // (unreachable: the table has +FUZZOFF entries)
    }

    /// <summary>The index of palette <paramref name="palette"/>'s colour nearest to (<paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>); the lowest of equals, as the shader's <c>nearest_index</c>.</summary>
    public static int NearestIndex(Playpal playpal, int palette, int r, int g, int b)
    {
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < 256; i++)
        {
            (byte pr, byte pg, byte pb) = playpal.GetColor(palette, i);
            int d = (pr - r) * (pr - r) + (pg - g) * (pg - g) + (pb - b) * (pb - b);
            if (d < bestD)
            {
                bestD = d;
                best = i;
                if (d == 0)
                    break;
            }
        }
        return best;
    }
}
