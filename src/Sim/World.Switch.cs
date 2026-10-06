using System;

namespace IsoDoom.Sim;

// p_switch.c: the switch list (P_InitSwitchList), the switch textures and
// button timers (P_ChangeSwitchTexture, P_StartButton; T5.4) and the use
// triggers (P_UseSpecialLine, T5.2). The countdown is in P_UpdateSpecials
// (World.Spec.cs); Switches.cs has the texture table.
public sealed partial class World
{
    /// <summary>p_spec.h <c>MAXBUTTONS</c>: 4 players, 4 buttons each at once, max.</summary>
    public const int MAXBUTTONS = 16;

    /// <summary>p_spec.h <c>BUTTONTIME</c>: 1 second, in tics.</summary>
    public const int BUTTONTIME = 35;

    /// <summary>
    /// p_switch.c <c>switchlist</c>: the switch textures of the game mode in
    /// pairs (off, on), by name (vanilla's texture numbers; see
    /// <see cref="P_InitSwitchList"/>). <see cref="numswitches"/> pairs.
    /// </summary>
    public readonly string[] switchlist = new string[Switches.MAXSWITCHES * 2];

    /// <summary>p_switch.c <c>numswitches</c>: the number of pairs in <see cref="switchlist"/>.</summary>
    public int numswitches;

    /// <summary>p_switch.c <c>buttonlist</c>: the pressed buttons; a free slot has <c>btimer</c> 0.</summary>
    public readonly button_t[] buttonlist = NewButtonList();

    private static button_t[] NewButtonList()
    {
        var list = new button_t[MAXBUTTONS];
        for (int i = 0; i < MAXBUTTONS; i++)
            list[i] = new button_t();
        return list;
    }

    /// <summary>
    /// p_switch.c <c>P_InitSwitchList</c>: only called at game initialization
    /// (the <see cref="World"/> constructor, vanilla's <c>P_Init</c>). The
    /// pairs of <see cref="Switches.alphSwitchList"/> whose episode set the
    /// game mode has (<see cref="Switches.For"/>). The sim keeps texture
    /// names, so a pair the WAD lacks is kept (it matches no side) where
    /// vanilla's <c>R_TextureNumForName</c> errors (SPEC §12 T5.4).
    /// </summary>
    public void P_InitSwitchList()
    {
        int index = 0;
        foreach (switchlist_t s in Switches.For(gamemode))
        {
            switchlist[index++] = s.name1;
            switchlist[index++] = s.name2;
        }
        numswitches = index / 2;
    }

    /// <summary>
    /// p_switch.c <c>P_StartButton</c>: start a button counting down till it
    /// turns off. Nothing when the line's button is already pressed; vanilla
    /// errors when all <see cref="MAXBUTTONS"/> slots are taken.
    /// </summary>
    public void P_StartButton(line_t line, bwhere_e w, string texture, int time)
    {
        // See if button is already pressed
        for (int i = 0; i < MAXBUTTONS; i++)
        {
            if (buttonlist[i].btimer != 0 && buttonlist[i].line == line)
                return;
        }

        for (int i = 0; i < MAXBUTTONS; i++)
        {
            if (buttonlist[i].btimer == 0)
            {
                buttonlist[i].line = line;
                buttonlist[i].where = w;
                buttonlist[i].btexture = texture;
                buttonlist[i].btimer = time;
                buttonlist[i].soundorg = line.frontsector;
                return;
            }
        }

        throw new InvalidOperationException("P_StartButton: no button slots left!");
    }

    /// <summary>
    /// p_switch.c <c>P_ChangeSwitchTexture</c>: function that changes wall
    /// texture. Tell it if switch is ok to use again (1=yes, it's a button).
    /// A switch (<paramref name="useAgain"/> 0) clears the line's special.
    /// The first texture of the front side (top, middle, bottom) that is in
    /// <see cref="switchlist"/>, in list order, becomes its partner, with
    /// <c>sfx_swtchn</c>; a button also starts its timer
    /// (<see cref="P_StartButton"/>, the texture it switches back to). As
    /// vanilla: the exit sound <c>sfx_swtchx</c> never plays (the special
    /// is already 0 when the exit switch checks for 11), and the sound comes
    /// from <c>buttonlist[0]</c>'s origin, not the line's (none while that
    /// slot is free; SPEC §12 T5.4).
    /// </summary>
    public void P_ChangeSwitchTexture(line_t line, int useAgain)
    {
        if (useAgain == 0)
            line.special = 0;

        side_t side = sides[line.sidenum[0]];
        string texTop = side.toptexture;
        string texMid = side.midtexture;
        string texBot = side.bottomtexture;

        sfxenum_t sound = sfxenum_t.sfx_swtchn;

        // EXIT SWITCH?
        if (line.special == 11)
            sound = sfxenum_t.sfx_swtchx;

        for (int i = 0; i < numswitches * 2; i++)
        {
            if (SameTexture(switchlist[i], texTop))
            {
                StartButtonSound(sound);
                side.toptexture = switchlist[i ^ 1];

                if (useAgain != 0)
                    P_StartButton(line, bwhere_e.top, switchlist[i], BUTTONTIME);

                return;
            }
            else
            {
                if (SameTexture(switchlist[i], texMid))
                {
                    StartButtonSound(sound);
                    side.midtexture = switchlist[i ^ 1];

                    if (useAgain != 0)
                        P_StartButton(line, bwhere_e.middle, switchlist[i], BUTTONTIME);

                    return;
                }
                else
                {
                    if (SameTexture(switchlist[i], texBot))
                    {
                        StartButtonSound(sound);
                        side.bottomtexture = switchlist[i ^ 1];

                        if (useAgain != 0)
                            P_StartButton(line, bwhere_e.bottom, switchlist[i], BUTTONTIME);

                        return;
                    }
                }
            }
        }
    }

    // S_StartSound(buttonlist->soundorg, sound): the first slot's origin, whichever line it holds.
    private void StartButtonSound(sfxenum_t sound)
    {
        if (buttonlist[0].soundorg is { } sector)
            S_StartSound(sector, sound);
        else
            S_StartSound((mobj_t?)null, sound);
    }

    // Vanilla compares texture numbers; the sim compares the names (case-insensitively, as R_TextureNumForName).
    private static bool SameTexture(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// p_spec.c <c>P_UpdateSpecials</c>' <c>DO BUTTONS</c> part: count the
    /// pressed buttons down; one that reaches 0 gets its texture back, with
    /// <c>sfx_swtchn</c> from its line's front sector (vanilla passes the
    /// address of the <c>soundorg</c> field, a garbage origin; SPEC §12
    /// T5.4), and its slot is freed.
    /// </summary>
    private void P_UpdateButtons()
    {
        //	DO BUTTONS
        for (int i = 0; i < MAXBUTTONS; i++)
        {
            button_t b = buttonlist[i];
            if (b.btimer != 0)
            {
                b.btimer--;
                if (b.btimer == 0)
                {
                    side_t side = sides[b.line!.sidenum[0]];
                    switch (b.where)
                    {
                        case bwhere_e.top:
                            side.toptexture = b.btexture!;
                            break;

                        case bwhere_e.middle:
                            side.midtexture = b.btexture!;
                            break;

                        case bwhere_e.bottom:
                            side.bottomtexture = b.btexture!;
                            break;
                    }
                    if (b.soundorg is { } sector)
                        S_StartSound(sector, sfxenum_t.sfx_swtchn);
                    else
                        S_StartSound((mobj_t?)null, sfxenum_t.sfx_swtchn);
                    b.Clear();
                }
            }
        }
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
