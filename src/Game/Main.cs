using System.Collections.Generic;
using System.IO;
using Godot;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// Entry point of the main scene. T7.1: the game scene (<see cref="LevelScene"/>,
/// the level scene become the game's: its title loop, or with the user
/// argument <c>--level [MAP]</c> straight into a map, and <c>--level-check</c>);
/// the WAD viewer (T1.6) with <c>--viewer</c>, <c>--viewer-check</c> or
/// <c>--viewer-screenshots</c>. T7.2: before the game, the IWAD menu
/// (<see cref="IwadMenu"/>) when several IWADs are found and none was named,
/// or none is found (its file picker), unless nobody can answer it
/// (headless) or a debug argument asks for a level. T1.1b: with
/// <c>--download-shareware</c>, first the shareware <c>DOOM1.WAD</c>
/// (downloaded and verified, as the IWAD menu's button does) becomes the
/// chosen and configured IWAD; the run quits with 1 if it can't.
/// </summary>
public partial class Main : Node
{
    public const string WadViewerScene = "res://scenes/WadViewer.tscn";
    public const string LevelScenePath = "res://scenes/Level.tscn";

    /// <summary>
    /// The release version, from the tag CI exported it for
    /// (<c>tools/stamp-version.sh</c>), or <c>dev</c> for any other build.
    /// </summary>
    public static string Version =>
        ProjectSettings.GetSetting("application/config/version", "").AsString() is { Length: > 0 } version
            ? version
            : "dev";

    public override void _Ready()
    {
        GD.Print($"IsoDoom started (version {Version}, Sim assembly: {typeof(IsoDoom.Sim.SimInfo).Assembly.GetName().Name})");
        if (!WadLocator.HasUserArg("--no-jit-warmup"))
            JitWarmup.Start(); // T8.3: the sim compiled in the background, before its first tics need it
        if (WadLocator.HasUserArg("--download-shareware"))
        {
            DownloadSharewareThenStart();
            return;
        }
        Start();
    }

    /// <summary>
    /// T8.3a: the quit's last word from managed code (the root's exit runs
    /// after its children's: the music thread is stopped by then). Godot's
    /// .NET shutdown leaves the runtime's finalizer thread running while the
    /// engine tears itself down, so a <see cref="GodotObject"/> wrapper
    /// finalized then calls into freed native memory (seen as a segfault at
    /// exit in about 1 run in 20 of the ReadyToRun export, its timing's).
    /// The finalizers are run here, while the engine is whole, and the GC is
    /// then held off (a no-GC region) so none are queued during the teardown.
    /// </summary>
    public override void _ExitTree() => SettleFinalizers();

    private static void SettleFinalizers()
    {
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        try
        {
            System.GC.TryStartNoGCRegion(NoGcBytes); // collects first: finalizers it queues are run below
        }
        catch (System.InvalidOperationException)
        {
            // already in one
        }
        System.GC.WaitForPendingFinalizers();
    }

    /// <summary>What the teardown may allocate before a GC could run again (it allocates little).</summary>
    private const long NoGcBytes = 64L * 1024 * 1024;

    private async void DownloadSharewareThenStart()
    {
        string path;
        try
        {
            path = await WadLocator.DownloadShareware();
        }
        catch (SharewareDownloadException e)
        {
            GD.PrintErr(e.Message);
            GetTree().Quit(1);
            return;
        }
        GD.Print($"The shareware {SharewareDownload.FileName} is verified in {path}");
        WadLocator.ChosenIwad = path;
        WadLocator.SaveConfiguredIwad(path);
        Start();
    }

    private void Start()
    {
        if (UseViewer())
        {
            AddChild(GD.Load<PackedScene>(WadViewerScene).Instantiate());
            return;
        }
        if (IwadChoice() is { } menu)
        {
            menu.Chosen += () =>
            {
                menu.QueueFree();
                OpenGame();
            };
            AddChild(menu);
            return;
        }
        OpenGame();
    }

    private void OpenGame() => AddChild(GD.Load<PackedScene>(LevelScenePath).Instantiate());

    private static bool UseViewer() =>
        WadLocator.HasUserArg("--viewer") || WadLocator.HasUserArg("--viewer-check") || WadLocator.HasUserArg("--viewer-screenshots");

    /// <summary>
    /// The IWAD menu, when there is a choice to make: none named on the
    /// command line or in the environment, a display, no level argument,
    /// and several IWADs found (the configured one too), or none.
    /// </summary>
    private static IwadMenu? IwadChoice()
    {
        if (WadLocator.HasUserArg("--level") || WadLocator.HasUserArg("--level-check") || WadLocator.HasUserArg("--level-script")
            || WadLocator.HasUserArg("--level-screenshot") || DisplayServer.GetName() == "headless")
            return null;
        var locator = new IwadLocator(WadLocator.CreateContext());
        IwadSearchResult found = locator.D_FindIWAD();
        if (found.Source is IwadSource.CommandLine or IwadSource.Environment)
            return null;
        var iwads = new List<string>(locator.D_FindAllIWADs());
        if (found.Path is { } path && !iwads.Exists(p => Path.GetFullPath(p) == Path.GetFullPath(path)))
            iwads.Insert(0, path); // the configured IWAD, outside the search folders
        if (found.Path is null)
            return new IwadMenu(iwads, null, "No IWAD found: download the shareware episode, browse for an IWAD (DOOM1.WAD, DOOM.WAD, DOOM2.WAD, …) or put one in wads/", found.Pwads, offerDownload: true);
        return iwads.Count > 1 ? new IwadMenu(iwads, found.Path, null, found.Pwads) : null;
    }
}
