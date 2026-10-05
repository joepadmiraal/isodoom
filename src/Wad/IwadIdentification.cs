using System;
using System.Collections.Generic;

namespace IsoDoom.Wad;

/// <summary>The identified game of an IWAD.</summary>
public sealed record IwadInfo(GameMode GameMode, GameMission GameMission)
{
    /// <summary>A human-readable name for the identified game.</summary>
    public string Description => (GameMode, GameMission) switch
    {
        (GameMode.shareware, _) => "DOOM Shareware",
        (GameMode.registered, _) => "DOOM Registered",
        (GameMode.retail, _) => "The Ultimate DOOM",
        (_, GameMission.pack_tnt) => "Final DOOM: TNT - Evilution",
        (_, GameMission.pack_plut) => "Final DOOM: The Plutonia Experiment",
        (_, GameMission.doom2) => "DOOM 2: Hell on Earth",
        _ => "Unknown",
    };

    public override string ToString() => $"{Description} ({GameMode}, {GameMission})";
}

/// <summary>
/// Identifies the game from the lumps of the IWAD (SPEC §2).
/// <para>
/// Vanilla linuxdoom's <c>IdentifyVersion</c> (d_main.c) only checks which IWAD
/// <em>file names</em> exist. This ports Chocolate Doom's lump-based
/// <c>D_IdentifyVersion</c> (d_main.c) instead: the first of <c>MAP01</c> or
/// <c>E1M1</c> in the directory picks DOOM II or DOOM 1; for DOOM 1,
/// <c>E4M1</c> means Ultimate, <c>E3M1</c> registered, otherwise shareware.
/// Chocolate Doom tells TNT and Plutonia apart from DOOM II by file name; here
/// the mission comes from lumps unique to each IWAD (the <c>REDTNT2</c> and
/// <c>CAMO1</c> patches), so a renamed file is still identified.
/// </para>
/// </summary>
public static class IwadIdentification
{
    /// <summary>
    /// Identifies the archive's IWAD: the first file, which must be an <c>IWAD</c>.
    /// Only the IWAD's own lumps count, so a PWAD that adds <c>MAP01</c> or
    /// <c>E4M1</c> cannot change the game mode.
    /// </summary>
    /// <exception cref="WadFormatException">The first file is a PWAD, or holds no E1M1 or MAP01.</exception>
    public static IwadInfo D_IdentifyVersion(WadArchive archive) => D_IdentifyVersion(archive.Files[0]);

    /// <summary>Identifies <paramref name="iwad"/> from its lumps (d_main.c: <c>D_IdentifyVersion</c>).</summary>
    /// <exception cref="WadFormatException">The file is a PWAD, or holds no E1M1 or MAP01.</exception>
    public static IwadInfo D_IdentifyVersion(WadFile iwad)
    {
        if (iwad.Type != WadType.Iwad)
            throw new WadFormatException($"{iwad.Name}: is a PWAD; the first WAD must be an IWAD.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        GameMission gamemission = GameMission.none;
        foreach (WadLump lump in iwad.Lumps)
        {
            names.Add(lump.Name);
            if (gamemission == GameMission.none)
            {
                if (lump.Name == "MAP01")
                    gamemission = GameMission.doom2;
                else if (lump.Name == "E1M1")
                    gamemission = GameMission.doom;
            }
        }

        if (gamemission == GameMission.none)
            throw new WadFormatException($"{iwad.Name}: Unknown or invalid IWAD file (no E1M1 or MAP01).");

        GameMode gamemode;
        if (gamemission == GameMission.doom)
        {
            // Doom 1.  But which version?
            if (names.Contains("E4M1"))
                gamemode = GameMode.retail;       // Ultimate Doom
            else if (names.Contains("E3M1"))
                gamemode = GameMode.registered;
            else
                gamemode = GameMode.shareware;
        }
        else
        {
            // Doom 2 of some kind.
            gamemode = GameMode.commercial;
            if (names.Contains("REDTNT2"))
                gamemission = GameMission.pack_tnt;
            else if (names.Contains("CAMO1"))
                gamemission = GameMission.pack_plut;
        }

        return new IwadInfo(gamemode, gamemission);
    }
}
