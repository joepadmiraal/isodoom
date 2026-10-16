namespace IsoDoom.Render;

/// <summary>
/// The players' colours (T11.10): r_draw.c <c>R_InitTranslationTables</c>'
/// three tables, which <c>R_DrawTranslatedColumn</c> applies to a sprite's
/// texels (before its colormap) when its mobj has <c>MF_TRANSLATION</c>
/// bits (p_mobj.c <c>P_SpawnPlayer</c> sets player <c>n</c>'s to <c>n</c>):
/// the green ramp 0x70–0x7F becomes grey (0x60–0x6F, table 1, player 2),
/// brown (0x40–0x4F, table 2, player 3) or red (0x20–0x2F, table 3, player 4);
/// every other index stays. Table 0 is no translation (player 1, green).
/// The CPU reference for <c>shaders/sprite.gdshaderinc</c>, which computes
/// the same mapping (SPEC §12 T11.10).
/// </summary>
public static class PlayerTranslation
{
    /// <summary>The tables (1–3; 0 is none), as <c>MF_TRANSLATION &gt;&gt; MF_TRANSSHIFT</c>.</summary>
    public const int Tables = 4;

    // r_draw.c R_InitTranslationTables: the ramp each table maps 0x70–0x7F to.
    private static readonly int[] _base = [0x70, 0x60, 0x40, 0x20];

    /// <summary>Palette index <paramref name="index"/> through translation table <paramref name="table"/> (0–3).</summary>
    public static byte Translate(byte index, int table) =>
        table is > 0 and < Tables && index is >= 0x70 and <= 0x7f ? (byte)(_base[table] + (index & 0xf)) : index;

    /// <summary>Whether table <paramref name="table"/> changes palette index <paramref name="index"/>.</summary>
    public static bool Changes(byte index, int table) => Translate(index, table) != index;
}
