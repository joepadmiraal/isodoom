namespace IsoDoom.Wad.Graphics;

/// <summary>
/// Vanilla's sprite name table (info.c <c>sprnames</c>, Doom v1.9), in
/// <c>spritenum_t</c> order (info.h): index 0 is <c>SPR_TROO</c>, 28 is
/// <c>SPR_PLAY</c>, 57 is <c>SPR_BAR1</c>. This is game data compiled into the
/// executable, not WAD data. The sim's port of info.c must keep this order,
/// since its <c>state_t.sprite</c> values index <see cref="Sprites.SpriteDefs"/>.
/// </summary>
public static class SpriteNames
{
    /// <summary>info.h <c>NUMSPRITES</c>.</summary>
    public const int NUMSPRITES = 138;

    /// <summary>info.c.</summary>
    public static readonly string[] sprnames =
    [
        "TROO", "SHTG", "PUNG", "PISG", "PISF", "SHTF", "SHT2", "CHGG", "CHGF", "MISG",
        "MISF", "SAWG", "PLSG", "PLSF", "BFGG", "BFGF", "BLUD", "PUFF", "BAL1", "BAL2",
        "PLSS", "PLSE", "MISL", "BFS1", "BFE1", "BFE2", "TFOG", "IFOG", "PLAY", "POSS",
        "SPOS", "VILE", "FIRE", "FATB", "FBXP", "SKEL", "MANF", "FATT", "CPOS", "SARG",
        "HEAD", "BAL7", "BOSS", "BOS2", "SKUL", "SPID", "BSPI", "APLS", "APBX", "CYBR",
        "PAIN", "SSWV", "KEEN", "BBRN", "BOSF", "ARM1", "ARM2", "BAR1", "BEXP", "FCAN",
        "BON1", "BON2", "BKEY", "RKEY", "YKEY", "BSKU", "RSKU", "YSKU", "STIM", "MEDI",
        "SOUL", "PINV", "PSTR", "PINS", "MEGA", "SUIT", "PMAP", "PVIS", "CLIP", "AMMO",
        "ROCK", "BROK", "CELL", "CELP", "SHEL", "SBOX", "BPAK", "BFUG", "MGUN", "CSAW",
        "LAUN", "PLAS", "SHOT", "SGN2", "COLU", "SMT2", "GOR1", "POL2", "POL5", "POL4",
        "POL3", "POL1", "POL6", "GOR2", "GOR3", "GOR4", "GOR5", "SMIT", "COL1", "COL2",
        "COL3", "COL4", "CAND", "CBRA", "COL6", "TRE1", "TRE2", "ELEC", "CEYE", "FSKU",
        "COL5", "TBLU", "TGRN", "TRED", "SMBT", "SMGT", "SMRT", "HDB1", "HDB2", "HDB3",
        "HDB4", "HDB5", "HDB6", "POB1", "POB2", "BRS1", "TLMP", "TLP2",
    ];
}
