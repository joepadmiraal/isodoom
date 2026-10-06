using IsoDoom.Game;
using Xunit;

namespace IsoDoom.Tests.Game;

/// <summary>T4.9: <see cref="InputLatches"/> holds presses between tics, edges once per frame.</summary>
public class InputLatchesTests
{
    [Fact]
    public void AFrameRunningTwoTicsTogglesRunOnce()
    {
        // A frame at 20 fps runs about two tics: the frame's poll, then one poll per tic,
        // while Godot's "just pressed" stays true for the whole frame.
        var latches = new InputLatches();
        var builder = new TiccmdBuilder();
        latches.Poll(7, false, false, true, 0);
        for (int tic = 0; tic < 2; tic++)
        {
            latches.Poll(7, false, false, true, 0);
            if (latches.RunToggle)
                builder.RunToggled = !builder.RunToggled;
            latches.Clear();
        }
        Assert.True(builder.RunToggled);
    }

    [Fact]
    public void EdgesLatchUntilATicTakesThem()
    {
        var latches = new InputLatches();
        latches.Poll(1, false, false, true, 3);
        latches.Poll(2, false, false, false, 0); // released before the next tic: still latched
        Assert.True(latches.RunToggle);
        Assert.Equal(3, latches.Weapon);
        latches.Clear();
        Assert.False(latches.RunToggle);
        Assert.Equal(0, latches.Weapon);
        latches.Poll(3, false, false, true, 5); // a new press in a later frame
        Assert.True(latches.RunToggle);
        Assert.Equal(5, latches.Weapon);
    }

    [Fact]
    public void HeldButtonsLatchOnEveryPoll()
    {
        var latches = new InputLatches();
        latches.Poll(1, false, false, false, 0);
        latches.Poll(1, true, true, false, 0); // pressed by the second poll of the frame
        Assert.True(latches.Attack);
        Assert.True(latches.Use);
        latches.Clear();
        latches.Poll(1, true, false, false, 0); // still held at the frame's next tic
        Assert.True(latches.Attack);
        Assert.False(latches.Use);
    }
}
