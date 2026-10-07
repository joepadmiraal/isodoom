using Godot;

namespace IsoDoom.Game;

/// <summary>
/// Entry point of the main scene. T7.1: the game scene (<see cref="LevelScene"/>,
/// the level scene become the game's: its title loop, or with the user
/// argument <c>--level [MAP]</c> straight into a map, and <c>--level-check</c>);
/// the WAD viewer (T1.6) with <c>--viewer</c> or <c>--viewer-check</c>, and
/// when no IWAD is found, so its file picker can choose one (saved for the
/// next start; the IWAD menu is T7.2's).
/// </summary>
public partial class Main : Node
{
    public const string WadViewerScene = "res://scenes/WadViewer.tscn";
    public const string LevelScenePath = "res://scenes/Level.tscn";

    public override void _Ready()
    {
        GD.Print($"IsoDoom started (Sim assembly: {typeof(IsoDoom.Sim.SimInfo).Assembly.GetName().Name})");
        AddChild(GD.Load<PackedScene>(UseViewer() ? WadViewerScene : LevelScenePath).Instantiate());
    }

    private static bool UseViewer()
    {
        if (WadLocator.HasUserArg("--viewer") || WadLocator.HasUserArg("--viewer-check") || WadLocator.HasUserArg("--viewer-screenshots"))
            return true;
        if (WadLocator.HasUserArg("--level") || WadLocator.HasUserArg("--level-check"))
            return false;
        return WadLocator.Find(out _).Path is null;
    }
}
