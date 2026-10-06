using Godot;

namespace IsoDoom.Game;

/// <summary>
/// Entry point of the main scene. Until the game shell exists (M7) it opens
/// the WAD viewer (T1.6), which shows a message when no IWAD is found, or,
/// with the user argument <c>--level [MAP]</c>, the level scene (T2.5).
/// </summary>
public partial class Main : Node
{
    public const string WadViewerScene = "res://scenes/WadViewer.tscn";
    public const string LevelScenePath = "res://scenes/Level.tscn";

    public override void _Ready()
    {
        GD.Print($"IsoDoom started (Sim assembly: {typeof(IsoDoom.Sim.SimInfo).Assembly.GetName().Name})");
        string scene = WadLocator.HasUserArg("--level") ? LevelScenePath : WadViewerScene;
        AddChild(GD.Load<PackedScene>(scene).Instantiate());
    }
}
