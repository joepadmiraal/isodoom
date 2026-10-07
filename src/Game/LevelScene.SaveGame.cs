using System;
using System.IO;
using Godot;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

// T7.6: saving and loading in the game scene: the save slots' directory
// (one per IWAD, as Chocolate Doom's), and the game flow's host for a load:
// a trial load of the save on a fresh copy of its map, then the map shown
// with the save's world (GameFlow.G_DoLoadGame).
public partial class LevelScene
{
    /// <summary>The default save directory's parent (Chocolate Doom's <c>savegames/</c> in its config directory).</summary>
    public const string SaveRoot = "user://savegames";

    // The IWAD's path (the save directory is named after it).
    private string? _iwadPath;

    // A temporary save directory of this run (the checks, scripts, headless runs), removed at the end.
    private string? _tempSaveDir;

    // The save being loaded (IGameHost.G_LoadGame): StartWorld builds its world.
    private SaveGameFile? _loading;

    /// <summary>
    /// The save slots' directory: <c>--savedir=DIR</c>, else
    /// <see cref="SaveRoot"/>/<c>IWAD</c> (the IWAD's file name in lower
    /// case), but in headless runs and under the checks, the level script and
    /// screenshots, which save to a temporary directory of their own (removed
    /// at the end), so the player's saves never change their results and
    /// they never touch the player's saves (as the settings, T7.3).
    /// </summary>
    private void InitSaves()
    {
        if (WadLocator.GetUserArg("--savedir") is string arg)
            Flow.SaveDir = Path.GetFullPath(ProjectSettings.GlobalizePath(arg));
        else if (IsCheckRun || WadLocator.HasUserArg("--level-script") || WadLocator.HasUserArg("--level-screenshot")
                 || DisplayServer.GetName() == "headless")
            Flow.SaveDir = _tempSaveDir = Path.Combine(Path.GetTempPath(), $"isodoom-saves-{System.Environment.ProcessId}");
        else
            Flow.SaveDir = ProjectSettings.GlobalizePath($"{SaveRoot}/{Path.GetFileName(_iwadPath ?? "iwad").ToLowerInvariant()}");
        Flow.Log = text => GD.Print($"Level: {text}");
        GD.Print($"Saves: {Flow.SaveDir}{(_tempSaveDir is not null ? " (this run's own)" : "")}");
    }

    public override void _ExitTree()
    {
        RemoveTempSaves();
        GetTree().AutoAcceptQuit = true; // T7.8e: CloseRequested's, while the scene is up
    }

    /// <summary>Removes this run's temporary save directory.</summary>
    private void RemoveTempSaves()
    {
        if (_tempSaveDir is not null && Directory.Exists(_tempSaveDir))
        {
            try
            {
                Directory.Delete(_tempSaveDir, true);
            }
            catch (IOException e)
            {
                GD.PushWarning($"Saves: {_tempSaveDir}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// <see cref="IGameHost.G_CheckLoadGame"/>: the save's map is in the WAD
    /// and its world loads on a fresh copy of it (<see cref="SaveGameFile.LoadWorld"/>);
    /// the game shown is left as it is.
    /// </summary>
    void IGameHost.G_CheckLoadGame(SaveGameFile save)
    {
        if (Wad is not { } wad || !HasLump(save.Map))
            throw new SaveGameException(SaveGameFile.OTHERGAME);
        try
        {
            LoadSaveWorld(save, Level.Load(wad, save.Map));
        }
        catch (Exception e) when (e is WadFormatException or System.Collections.Generic.KeyNotFoundException)
        {
            throw new SaveGameException(SaveGameFile.OTHERGAME, e);
        }
    }

    /// <summary>
    /// <see cref="IGameHost.G_LoadGame"/>: the save's map shown afresh
    /// (<see cref="LoadMap(string, World?)"/>) with the save's world
    /// (<see cref="StartWorld"/>), on its skill (<see cref="Skill"/> from now on).
    /// </summary>
    bool IGameHost.G_LoadGame(SaveGameFile save)
    {
        Skill = save.Skill;
        LevelEnded = null;
        _loading = save;
        try
        {
            return TryLoad(save.Map, null) && World is not null;
        }
        finally
        {
            _loading = null;
        }
    }

    /// <summary>
    /// The save's world on <paramref name="level"/>: as a new game's (the
    /// textures and flats of <c>P_Init</c>, the WAD's <c>MAP31</c>), with the
    /// save's settings and tweaks, but the aim assist's cone the option's
    /// (it applies at once during a game too, T7.3; SPEC §12 T7.6).
    /// </summary>
    private World LoadSaveWorld(SaveGameFile save, Level level)
    {
        World world = save.LoadWorld(level, Textures, FlatNames(Wad!));
        world.map31exists = Wad!.W_CheckNumForName("MAP31") >= 0;
        if (world.tweaks.AbsoluteAiming == Tweaks.AbsoluteAiming)
            world.tweaks = world.tweaks with { AimAssistCone = Tweaks.AimAssistCone };
        return world;
    }
}
