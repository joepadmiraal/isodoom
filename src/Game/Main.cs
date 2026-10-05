using Godot;

namespace IsoDoom.Game;

/// <summary>Entry point of the main scene. Empty for now (T0.1).</summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        GD.Print($"IsoDoom started (Sim assembly: {typeof(IsoDoom.Sim.SimInfo).Assembly.GetName().Name})");
    }
}
