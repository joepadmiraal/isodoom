using Godot;

namespace IsoDoom.Game;

/// <summary>
/// Entry point of the main scene. Until the game shell exists (M7) it opens
/// the WAD viewer (T1.6), which shows a message when no IWAD is found.
/// </summary>
public partial class Main : Node
{
    public const string WadViewerScene = "res://scenes/WadViewer.tscn";

    public override void _Ready()
    {
        GD.Print($"IsoDoom started (Sim assembly: {typeof(IsoDoom.Sim.SimInfo).Assembly.GetName().Name})");
        AddChild(GD.Load<PackedScene>(WadViewerScene).Instantiate());
    }
}
