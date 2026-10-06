namespace IsoDoom.Sim;

// p_switch.c: the use triggers (P_UseSpecialLine) and the once-only rule of
// P_ChangeSwitchTexture (T5.2). The switch textures, P_InitSwitchList and the
// button timers come with T5.4 (Switches.cs has the texture table).
public sealed partial class World
{
    /// <summary>
    /// p_switch.c <c>P_ChangeSwitchTexture</c>: function that changes wall
    /// texture. Tell it if switch is ok to use again (1=yes, it's a button).
    /// Only the once-only rule is ported: a switch (<paramref name="useAgain"/>
    /// 0) clears the line's special. The texture change, its sound and
    /// <c>P_StartButton</c> come with T5.4.
    /// </summary>
    public void P_ChangeSwitchTexture(line_t line, int useAgain)
    {
        if (useAgain == 0)
            line.special = 0;

        // The switch texture, sfx_swtchn/sfx_swtchx and P_StartButton (T5.4).
    }

    /// <summary>
    /// p_switch.c <c>P_UseSpecialLine</c>: called when a thing uses a special
    /// line. Only the front sides of lines are usable (false from the back);
    /// monsters open only manual doors (1, 32-34) that are not
    /// <see cref="IsoDoom.Map.Line.ML_SECRET"/>. Switches (S1) clear the special when
    /// their effect started (<see cref="P_ChangeSwitchTexture"/>), buttons (SR)
    /// and manual doors keep it. Returns true for any other line, which a use
    /// trace then stops at, as vanilla.
    /// </summary>
    public bool P_UseSpecialLine(mobj_t thing, line_t line, int side)
    {
        // Err...
        // Use the back sides of VERY SPECIAL lines...
        if (side != 0)
        {
            switch (line.special)
            {
                case 124:
                    // Sliding door open&close
                    // UNUSED?
                    break;

                default:
                    return false;
            }
        }

        // Switches that other things can activate.
        if (thing.player == null)
        {
            // never open secret doors
            if ((line.flags & IsoDoom.Map.Line.ML_SECRET) != 0)
                return false;

            switch (line.special)
            {
                case 1:  // MANUAL DOOR RAISE
                case 32: // MANUAL BLUE
                case 33: // MANUAL RED
                case 34: // MANUAL YELLOW
                    break;

                default:
                    return false;
            }
        }

        // do something
        switch (line.special)
        {
            // MANUALS
            case 1: // Vertical Door
            case 26: // Blue Door/Locked
            case 27: // Yellow Door /Locked
            case 28: // Red Door /Locked
            case 31: // Manual door open
            case 32: // Blue locked door open
            case 33: // Red locked door open
            case 34: // Yellow locked door open
            case 117: // Blazing door raise
            case 118: // Blazing door open
                EV_VerticalDoor(line, thing);
                break;

            // SWITCHES
            case 7: // Build Stairs
                if (EV_BuildStairs(line, stair_e.build8) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 9: // Change Donut
                if (EV_DoDonut(line) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 11: // Exit level
                P_ChangeSwitchTexture(line, 0);
                G_ExitLevel();
                break;

            case 14: // Raise Floor 32 and change texture
                if (EV_DoPlat(line, plattype_e.raiseAndChange, 32) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 15: // Raise Floor 24 and change texture
                if (EV_DoPlat(line, plattype_e.raiseAndChange, 24) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 18: // Raise Floor to next highest floor
                if (EV_DoFloor(line, floor_e.raiseFloorToNearest) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 20: // Raise Plat next highest floor and change texture
                if (EV_DoPlat(line, plattype_e.raiseToNearestAndChange, 0) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 21: // PlatDownWaitUpStay
                if (EV_DoPlat(line, plattype_e.downWaitUpStay, 0) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 23: // Lower Floor to Lowest
                if (EV_DoFloor(line, floor_e.lowerFloorToLowest) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 29: // Raise Door
                if (EV_DoDoor(line, vldoor_e.vld_normal) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 41: // Lower Ceiling to Floor
                if (EV_DoCeiling(line, ceiling_e.lowerToFloor) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 71: // Turbo Lower Floor
                if (EV_DoFloor(line, floor_e.turboLower) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 49: // Ceiling Crush And Raise
                if (EV_DoCeiling(line, ceiling_e.crushAndRaise) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 50: // Close Door
                if (EV_DoDoor(line, vldoor_e.vld_close) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 51: // Secret EXIT
                P_ChangeSwitchTexture(line, 0);
                G_SecretExitLevel();
                break;

            case 55: // Raise Floor Crush
                if (EV_DoFloor(line, floor_e.raiseFloorCrush) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 101: // Raise Floor
                if (EV_DoFloor(line, floor_e.raiseFloor) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 102: // Lower Floor to Surrounding floor height
                if (EV_DoFloor(line, floor_e.lowerFloor) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 103: // Open Door
                if (EV_DoDoor(line, vldoor_e.vld_open) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 111: // Blazing Door Raise (faster than TURBO!)
                if (EV_DoDoor(line, vldoor_e.vld_blazeRaise) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 112: // Blazing Door Open (faster than TURBO!)
                if (EV_DoDoor(line, vldoor_e.vld_blazeOpen) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 113: // Blazing Door Close (faster than TURBO!)
                if (EV_DoDoor(line, vldoor_e.vld_blazeClose) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 122: // Blazing PlatDownWaitUpStay
                if (EV_DoPlat(line, plattype_e.blazeDWUS, 0) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 127: // Build Stairs Turbo 16
                if (EV_BuildStairs(line, stair_e.turbo16) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 131: // Raise Floor Turbo
                if (EV_DoFloor(line, floor_e.raiseFloorTurbo) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 133: // BlzOpenDoor BLUE
            case 135: // BlzOpenDoor RED
            case 137: // BlzOpenDoor YELLOW
                if (EV_DoLockedDoor(line, vldoor_e.vld_blazeOpen, thing) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            case 140: // Raise Floor 512
                if (EV_DoFloor(line, floor_e.raiseFloor512) != 0)
                    P_ChangeSwitchTexture(line, 0);
                break;

            // BUTTONS
            case 42: // Close Door
                if (EV_DoDoor(line, vldoor_e.vld_close) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 43: // Lower Ceiling to Floor
                if (EV_DoCeiling(line, ceiling_e.lowerToFloor) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 45: // Lower Floor to Surrounding floor height
                if (EV_DoFloor(line, floor_e.lowerFloor) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 60: // Lower Floor to Lowest
                if (EV_DoFloor(line, floor_e.lowerFloorToLowest) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 61: // Open Door
                if (EV_DoDoor(line, vldoor_e.vld_open) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 62: // PlatDownWaitUpStay
                if (EV_DoPlat(line, plattype_e.downWaitUpStay, 1) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 63: // Raise Door
                if (EV_DoDoor(line, vldoor_e.vld_normal) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 64: // Raise Floor to ceiling
                if (EV_DoFloor(line, floor_e.raiseFloor) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 66: // Raise Floor 24 and change texture
                if (EV_DoPlat(line, plattype_e.raiseAndChange, 24) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 67: // Raise Floor 32 and change texture
                if (EV_DoPlat(line, plattype_e.raiseAndChange, 32) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 65: // Raise Floor Crush
                if (EV_DoFloor(line, floor_e.raiseFloorCrush) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 68: // Raise Plat to next highest floor and change texture
                if (EV_DoPlat(line, plattype_e.raiseToNearestAndChange, 0) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 69: // Raise Floor to next highest floor
                if (EV_DoFloor(line, floor_e.raiseFloorToNearest) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 70: // Turbo Lower Floor
                if (EV_DoFloor(line, floor_e.turboLower) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 114: // Blazing Door Raise (faster than TURBO!)
                if (EV_DoDoor(line, vldoor_e.vld_blazeRaise) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 115: // Blazing Door Open (faster than TURBO!)
                if (EV_DoDoor(line, vldoor_e.vld_blazeOpen) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 116: // Blazing Door Close (faster than TURBO!)
                if (EV_DoDoor(line, vldoor_e.vld_blazeClose) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 123: // Blazing PlatDownWaitUpStay
                if (EV_DoPlat(line, plattype_e.blazeDWUS, 0) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 132: // Raise Floor Turbo
                if (EV_DoFloor(line, floor_e.raiseFloorTurbo) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 99: // BlzOpenDoor BLUE
            case 134: // BlzOpenDoor RED
            case 136: // BlzOpenDoor YELLOW
                if (EV_DoLockedDoor(line, vldoor_e.vld_blazeOpen, thing) != 0)
                    P_ChangeSwitchTexture(line, 1);
                break;

            case 138: // Light Turn On
                EV_LightTurnOn(line, 255);
                P_ChangeSwitchTexture(line, 1);
                break;

            case 139: // Light Turn Off
                EV_LightTurnOn(line, 35);
                P_ChangeSwitchTexture(line, 1);
                break;
        }

        return true;
    }

    /// <summary>
    /// Not vanilla: whether <see cref="P_UseSpecialLine"/> does something for
    /// a player using a line with <paramref name="special"/> (a manual door, a
    /// switch or a button), for the use fallback (SPEC §6.3 #3, T5.2).
    /// </summary>
    public static bool IsUseSpecial(int special)
    {
        switch (special)
        {
            case 1:
            case 26:
            case 27:
            case 28:
            case 31:
            case 32:
            case 33:
            case 34:
            case 117:
            case 118:
            case 7:
            case 9:
            case 11:
            case 14:
            case 15:
            case 18:
            case 20:
            case 21:
            case 23:
            case 29:
            case 41:
            case 71:
            case 49:
            case 50:
            case 51:
            case 55:
            case 101:
            case 102:
            case 103:
            case 111:
            case 112:
            case 113:
            case 122:
            case 127:
            case 131:
            case 133:
            case 135:
            case 137:
            case 140:
            case 42:
            case 43:
            case 45:
            case 60:
            case 61:
            case 62:
            case 63:
            case 64:
            case 66:
            case 67:
            case 65:
            case 68:
            case 69:
            case 70:
            case 114:
            case 115:
            case 116:
            case 123:
            case 132:
            case 99:
            case 134:
            case 136:
            case 138:
            case 139:
                return true;

            default:
                return false;
        }
    }
}
