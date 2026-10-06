namespace IsoDoom.Wad;

// Members keep their vanilla spelling so ported code such as
// `gamemode == commercial` diffs cleanly against the original.

/// <summary>
/// Which game the IWAD is, as far as game logic cares (doomdef.h: <c>GameMode_t</c>).
/// </summary>
public enum GameMode
{
    /// <summary>DOOM 1 shareware, episode 1 only (DOOM1.WAD).</summary>
    shareware,

    /// <summary>DOOM 1 registered, episodes 1-3 (DOOM.WAD before v1.9 Ultimate).</summary>
    registered,

    /// <summary>DOOM II and Final Doom: MAPxx levels (DOOM2.WAD, TNT.WAD, PLUTONIA.WAD).</summary>
    commercial,

    /// <summary>The Ultimate DOOM, episodes 1-4 (DOOM.WAD v1.9 Ultimate).</summary>
    retail,

    /// <summary>Not identified (vanilla's initial value).</summary>
    indetermined,
}

/// <summary>
/// Which IWAD of a game mode it is, for level names, intermission text and the
/// like (doomdef.h: <c>GameMission_t</c>).
/// </summary>
public enum GameMission
{
    /// <summary>DOOM 1 (any of shareware, registered, retail).</summary>
    doom,

    /// <summary>DOOM II.</summary>
    doom2,

    /// <summary>Final Doom: TNT: Evilution.</summary>
    pack_tnt,

    /// <summary>Final Doom: The Plutonia Experiment.</summary>
    pack_plut,

    /// <summary>Not identified.</summary>
    none,
}

/// <summary>
/// Which release of an IWAD it is, on top of <see cref="GameMode"/> and
/// <see cref="GameMission"/> (Chocolate Doom d_mode.h: <c>GameVariant_t</c>;
/// not in vanilla).
/// </summary>
public enum GameVariant
{
    /// <summary>An original id Software IWAD.</summary>
    vanilla,

    /// <summary>Freedoom: Phase 1 or 2 (has a <c>FREEDOOM</c> lump).</summary>
    freedoom,

    /// <summary>FreeDM, Freedoom's deathmatch IWAD (has <c>FREEDOOM</c> and <c>FREEDM</c> lumps).</summary>
    freedm,

    /// <summary>The Doom 3 BFG Edition IWADs (have a <c>DMENUPIC</c> lump).</summary>
    bfgedition,
}
