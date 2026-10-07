using System;

namespace IsoDoom.Game;

/// <summary>
/// The presses <see cref="GameInput"/> holds between tics (T4.6), in plain C#
/// so the tests can link it. Held buttons (fire, use) latch on every poll;
/// edges (the run toggle, a weapon key, the pause key) only on the first poll of a frame:
/// Godot's "just pressed" stays true for the whole frame, and a frame that
/// runs several tics polls once per tic, so without this a frame running two
/// tics flipped the run toggle twice (T4.9: the toggle was lost at 17–25 fps).
/// </summary>
public sealed class InputLatches
{
    private ulong? _frame;

    /// <summary>Fire was held at a poll since the last <see cref="Clear"/>.</summary>
    public bool Attack { get; private set; }

    /// <summary>Use was held at a poll since the last <see cref="Clear"/>.</summary>
    public bool Use { get; private set; }

    /// <summary>The run toggle was pressed in a frame since the last <see cref="Clear"/>.</summary>
    public bool RunToggle { get; private set; }

    /// <summary>The weapon slot pressed since the last <see cref="Clear"/> (1–8), or 0.</summary>
    public int Weapon { get; private set; }

    /// <summary>T6.6: the next (+1) or previous (−1) weapon asked for since the last <see cref="Clear"/> (the last one asked), or 0.</summary>
    public int WeaponStep { get; private set; }

    /// <summary>T7.1: the pause key was pressed in a frame since the last <see cref="Clear"/>.</summary>
    public bool Pause { get; private set; }

    /// <summary>T6.6: latches a next (+1) or previous (−1) weapon press (an input event: the mouse wheel's presses last no frame).</summary>
    public void StepWeapon(int direction) => WeaponStep = Math.Sign(direction);

    /// <summary>
    /// Latches one poll in process frame <paramref name="frame"/>: the held
    /// buttons, and on the frame's first poll only, the edges pressed in it.
    /// </summary>
    public void Poll(ulong frame, bool attackHeld, bool useHeld, bool runTogglePressed, int weaponPressed, bool pausePressed = false)
    {
        Attack |= attackHeld;
        Use |= useHeld;
        if (_frame == frame)
            return;
        _frame = frame;
        RunToggle |= runTogglePressed;
        Pause |= pausePressed;
        if (weaponPressed != 0)
            Weapon = weaponPressed;
    }

    /// <summary>Drops the latched presses (a tic took them).</summary>
    public void Clear()
    {
        Attack = Use = RunToggle = Pause = false;
        Weapon = WeaponStep = 0;
    }
}
