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
