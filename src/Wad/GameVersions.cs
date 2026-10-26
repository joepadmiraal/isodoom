using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace IsoDoom.Wad;

/// <summary>
/// The executable to emulate (T10.7; SPEC §12 T10.7): Chocolate Doom's
/// d_main.c <c>InitGameVersion</c> (here <see cref="D_SetGameVersion"/>):
/// the IWAD's default, or <c>-gameversion NAME</c>, and the game mode and
/// mission an older executable demotes the IWAD to.
/// </summary>
public static class GameVersions
{
    /// <summary>
    /// d_main.c <c>gameversions[]</c>: each version's name and its
    /// <c>-gameversion</c> argument, those emulated here only
    /// (<see cref="Emulated"/>): v1.2, v1.5, Hacx and Chex Quest have
    /// behaviour the sim does not port, so they are refused.
    /// </summary>
    [SuppressMessage("Style", "IDE1006", Justification = "Vanilla name (Chocolate Doom d_main.c)")]
    public static readonly IReadOnlyList<(string Description, string Cmdline, GameVersion Version)> gameversions =
    [
        ("Doom 1.666", "1.666", GameVersion.exe_doom_1_666),
        ("Doom 1.7/1.7a", "1.7", GameVersion.exe_doom_1_7),
        ("Doom 1.8", "1.8", GameVersion.exe_doom_1_8),
        ("Doom 1.9", "1.9", GameVersion.exe_doom_1_9),
        ("Ultimate Doom", "ultimate", GameVersion.exe_ultimate),
        ("Final Doom", "final", GameVersion.exe_final),
        ("Final Doom (alt)", "final2", GameVersion.exe_final2),
    ];

    /// <summary>
    /// Whether the sim and the game emulate <paramref name="version"/>:
    /// v1.666 to Final Doom's alternate executable. v1.666 to v1.9 differ in
    /// nothing ported but the music driver's version (Chocolate Doom's
    /// <c>S_Init</c>: v1.666's OPL driver, not emulated; SPEC §12 T10.7).
    /// </summary>
    public static bool Emulated(GameVersion version) =>
        version is >= GameVersion.exe_doom_1_666 and <= GameVersion.exe_final2 and not GameVersion.exe_hacx;

    /// <summary>The <c>-gameversion</c> argument of <paramref name="version"/> (<c>final</c>, …), or its enum name for one not emulated.</summary>
    public static string Cmdline(GameVersion version) =>
        gameversions.FirstOrDefault(v => v.Version == version).Cmdline ?? version.ToString();

    /// <summary>The description of <paramref name="version"/> (d_main.c <c>PrintGameVersion</c>'s: "Final Doom").</summary>
    public static string Description(GameVersion version) =>
        gameversions.FirstOrDefault(v => v.Version == version).Description ?? version.ToString();

    /// <summary>The version a <c>-gameversion</c> argument names (<c>1.9</c>, <c>ultimate</c>, <c>final</c>, …), or null for an unknown or not emulated one.</summary>
    public static GameVersion? Parse(string cmdline)
    {
        foreach (var v in gameversions)
        {
            if (string.Equals(v.Cmdline, cmdline, StringComparison.OrdinalIgnoreCase))
                return v.Version;
        }
        return null;
    }

    /// <summary>
    /// The version Chocolate Doom picks for an IWAD without <c>-gameversion</c>
    /// (d_main.c <c>InitGameVersion</c>, "Determine automatically"):
    /// Final Doom's first executable (<see cref="GameVersion.exe_final"/>,
    /// "having this as the default should mean that it plays back most demos
    /// correctly") for TNT and Plutonia, The Ultimate Doom's for
    /// <see cref="GameMode.retail"/>, v1.9 for the rest. Chocolate Doom then
    /// reads an older version from the IWAD's demos (v1.666's Doom II); not
    /// here (SPEC §12 T10.7): the sim plays them alike, and the reference's
    /// routes run as v1.9.
    /// </summary>
    public static GameVersion Default(GameMode gamemode, GameMission gamemission) => gamemode switch
    {
        GameMode.retail => GameVersion.exe_ultimate,
        GameMode.commercial when gamemission is GameMission.pack_tnt or GameMission.pack_plut => GameVersion.exe_final,
        _ => GameVersion.exe_doom_1_9,
    };

    /// <summary>
    /// T7.14: Chocolate Doom g_game.c <c>G_VanillaVersionCode</c>: the
    /// version byte a demo of <paramref name="gameversion"/> carries (106 for
    /// v1.666, 107 for v1.7, 108 for v1.8; "all other versions are variants
    /// on v1.9": 109). v1.2 has none (Chocolate Doom's <c>I_Error</c>); it is
    /// not emulated (<see cref="Emulated"/>).
    /// </summary>
    [SuppressMessage("Style", "IDE1006", Justification = "Vanilla name (Chocolate Doom g_game.c)")]
    public static int G_VanillaVersionCode(GameVersion gameversion) => gameversion switch
    {
        GameVersion.exe_doom_1_666 => 106,
        GameVersion.exe_doom_1_7 => 107,
        GameVersion.exe_doom_1_8 => 108,
        _ => 109,
    };

    /// <summary>
    /// T7.14: the version byte of the IWAD's own demos, as Chocolate Doom's
    /// d_main.c <c>InitGameVersion</c> reads it ("Detect version from demo
    /// lump"): the first of <c>DEMO1</c>–<c>DEMO3</c> in <paramref name="wad"/>
    /// whose byte is v1.666's to v1.9's (Doom II v1.666's IWAD: 106), or null.
    /// The game only logs it beside a demo of another version (the sim plays
    /// v1.666 to v1.9 alike, SPEC §12 T10.7; a demo needs its IWAD's maps).
    /// </summary>
    public static int? IwadDemoVersion(WadArchive wad)
    {
        ArgumentNullException.ThrowIfNull(wad);
        for (int i = 1; i <= 3; i++)
        {
            string name = $"DEMO{i}";
            if (wad.W_CheckNumForName(name) > 0 && wad.W_CacheLumpName(name).Span is [>= 106 and <= 109, ..] demo)
                return demo[0];
        }
        return null;
    }

    /// <summary>
    /// d_main.c <c>InitGameVersion</c>: the version to emulate for
    /// <paramref name="iwad"/> (<paramref name="cmdline"/>, <c>-gameversion</c>'s
    /// argument, else <see cref="Default"/>), and the IWAD as that executable
    /// plays it: "the original exe does not support retail" (before
    /// The Ultimate Doom, <see cref="GameMode.retail"/> plays as
    /// <see cref="GameMode.registered"/>) and "EXEs prior to the Final Doom
    /// exes do not support Final Doom" (TNT and Plutonia play as Doom II:
    /// its level names and texts).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="cmdline"/> names no emulated version.</exception>
    [SuppressMessage("Style", "IDE1006", Justification = "Vanilla name (Chocolate Doom d_main.c InitGameVersion)")]
    public static (GameVersion Version, IwadInfo Iwad) D_SetGameVersion(IwadInfo iwad, string? cmdline = null)
    {
        GameVersion gameversion;
        if (cmdline is not null)
        {
            gameversion = Parse(cmdline) ?? throw new ArgumentException(
                $"Unknown game version '{cmdline}'. Valid versions: "
                + string.Join(", ", gameversions.Select(v => $"{v.Cmdline} ({v.Description})")) + ".");
        }
        else
        {
            gameversion = Default(iwad.GameMode, iwad.GameMission);
        }

        // The original exe does not support retail - 4th episode not supported
        if (gameversion < GameVersion.exe_ultimate && iwad.GameMode == GameMode.retail)
            iwad = iwad with { GameMode = GameMode.registered };

        // EXEs prior to the Final Doom exes do not support Final Doom.
        if (gameversion < GameVersion.exe_final && iwad.GameMode == GameMode.commercial
            && iwad.GameMission is GameMission.pack_tnt or GameMission.pack_plut)
            iwad = iwad with { GameMission = GameMission.doom2 };

        return (gameversion, iwad);
    }
}
