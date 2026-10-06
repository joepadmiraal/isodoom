using System;
using System.Collections.Generic;

namespace IsoDoom.Wad;

/// <summary>
/// The game refuses to start with this IWAD and PWAD combination
/// (vanilla's <c>I_Error</c> in <see cref="ModifiedGame.D_CheckModifiedGame"/>).
/// </summary>
public sealed class ModifiedGameException : Exception
{
    public ModifiedGameException(string message) : base(message)
    {
    }
}

/// <summary>
/// The "modified game" checks of <c>D_DoomMain</c> (d_main.c, "Check for -file
/// in shareware"), run once the IWAD and the <c>-file</c> PWADs are loaded and
/// the IWAD is identified.
/// <para>
/// Vanilla behaviour, kept on purpose (SPEC §12): PWADs are refused with the
/// shareware IWAD, as the shareware licence asks ("Register!"), and a
/// registered IWAD used with PWADs must hold the episode 2-3 lumps. As in
/// Chocolate Doom, Freedoom IWADs skip both checks.
/// </para>
/// </summary>
public static class ModifiedGame
{
    /// <summary>The message for PWADs with the shareware IWAD (d_main.c).</summary>
    public const string SharewareMessage = "You cannot -file with the shareware version. Register!";

    /// <summary>The message for a "registered" IWAD missing registered lumps (d_main.c).</summary>
    public const string NotRegisteredMessage = "This is not the registered version.";

    /// <summary>
    /// The lumps a registered IWAD must have when the game is modified (d_main.c).
    /// Vanilla's list checks <c>e3m3</c> twice and never <c>e3m2</c>; kept as is.
    /// </summary>
    public static IReadOnlyList<string> name { get; } = new[]
    {
        "e2m1", "e2m2", "e2m3", "e2m4", "e2m5", "e2m6", "e2m7", "e2m8", "e2m9",
        "e3m1", "e3m3", "e3m3", "e3m4", "e3m5", "e3m6", "e3m7", "e3m8", "e3m9",
        "dphoof", "bfgga0", "heada1", "cybra1", "spida1d1",
    };

    /// <summary>
    /// Whether PWADs were added (d_main.c/w_main.c: <c>modifiedgame</c>, set by
    /// <c>-file</c>). <c>-deh</c> and <c>-merge</c>, which also set it in
    /// Chocolate Doom, don't exist here yet.
    /// </summary>
    public static bool modifiedgame(WadArchive archive) => archive.Files.Count > 1;

    /// <summary>
    /// Throws when the game must not start with these WADs (d_main.c:
    /// <c>D_DoomMain</c>, "Check for -file in shareware"). The lumps are looked
    /// up in the merged directory, as vanilla's <c>W_CheckNumForName</c> does
    /// after all files are added, so a PWAD can supply a missing one.
    /// </summary>
    /// <exception cref="ModifiedGameException">PWADs with the shareware IWAD, or a registered IWAD without the registered lumps.</exception>
    public static void D_CheckModifiedGame(WadArchive archive, IwadInfo info)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(info);

        // Chocolate Doom: Freedoom is free, so no "Register!" for it.
        if (!modifiedgame(archive) || info.GameVariant is GameVariant.freedoom or GameVariant.freedm)
            return;

        if (info.GameMode == GameMode.shareware)
            throw new ModifiedGameException(SharewareMessage);

        // Check for fake IWAD with right name,
        // but w/o all the lumps of the registered version.
        if (info.GameMode == GameMode.registered)
        {
            foreach (string lump in name)
            {
                if (archive.W_CheckNumForName(lump) < 0)
                    throw new ModifiedGameException(NotRegisteredMessage);
            }
        }
    }
}
