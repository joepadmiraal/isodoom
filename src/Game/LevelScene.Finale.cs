using System.Collections.Generic;
using System.Linq;
using Godot;
using IsoDoom.Render;

namespace IsoDoom.Game;

// T10.5: the cast call's input in the game scene. g_game.c G_Responder
// gives every event to f_finale.c's F_Responder in the finale (after the
// menus' M_Responder); in the cast call a key pressed is the cast's (it
// kills the member shown) and never reaches the ticcmd, while a mouse
// button or a pad's goes on to the ticcmd, whose buttons start the cast over
// (F_Ticker), as vanilla's.
public partial class LevelScene
{
    // the keys the cast call took, until released and a tic built
    private readonly HashSet<Key> _castKeys = [];

    private bool CastCall => _flow is { gamestate: gamestate_t.GS_FINALE } flow && flow.Finale.finalestage == FFinale.F_STAGE_CAST;

    /// <summary>
    /// f_finale.c <c>F_Responder</c> for an input event the menus did not
    /// take: in the cast call a key pressed (not a function key, the menus'
    /// in vanilla; its repeats taken too, without effect) goes to
    /// <see cref="FFinale.F_Responder"/>. Returns whether the cast took it.
    /// </summary>
    public bool CastEvent(InputEvent e)
    {
        if (!CastCall || e is not InputEventKey { Pressed: true } key || IsFunctionKey(key))
            return false;
        _castKeys.Add(KeyOf(key));
        if (key.Echo)
            return true;
        bool taken = _flow!.Finale.F_Responder(true);
        if (taken)
            GD.Print($"Level: cast call: key, {FFinale.castorder[_flow.Finale.castnum].name} {(_flow.Finale.castdeath ? "dies" : "walks")}");
        return taken;
    }

    /// <summary>
    /// T10.5: in the cast call the keys it took press no button (vanilla's
    /// keys never reach <c>G_Responder</c>'s ticcmd there): while one was down
    /// since the last tic, fire, use and the pause count only if a mouse
    /// button or a pad holds them, and no weapon key.
    /// </summary>
    private TiccmdInput CastFilter(TiccmdInput input)
    {
        if (!CastCall)
        {
            _castKeys.Clear();
            return input;
        }
        bool keyed = _castKeys.Count > 0;
        _castKeys.RemoveWhere(k => !Input.IsKeyPressed(k) && !Input.IsPhysicalKeyPressed(k));
        if (!keyed)
            return input;
        return input with
        {
            Attack = input.Attack && NonKeyHolds(GameInput.Attack),
            Use = input.Use && NonKeyHolds(GameInput.Use),
            Pause = input.Pause && NonKeyHolds(GameInput.Pause),
            Weapon = 0,
        };
    }

    /// <summary>Whether a mouse button or a pad's button or axis bound to <paramref name="action"/> holds it now.</summary>
    private static bool NonKeyHolds(string action) =>
        InputMap.HasAction(StringNames.Get(action)) && Render.GodotRefs.ActionEvents(action).Any(e => e switch
        {
            InputEventMouseButton mb => Input.IsMouseButtonPressed(mb.ButtonIndex),
            InputEventJoypadButton jb => Input.GetConnectedJoypads().Any(id => Input.IsJoyButtonPressed(id, jb.ButtonIndex)),
            InputEventJoypadMotion jm => Input.GetConnectedJoypads().Any(id => Input.GetJoyAxis(id, jm.Axis) * Mathf.Sign(jm.AxisValue) >= 0.5f),
            _ => false,
        });
}
