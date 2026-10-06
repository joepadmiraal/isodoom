using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_doors.c: door animation code (opening/closing), T5.3. The sounds go to
// the sound events (World.Sound.cs), the key messages to player_t.message.
public sealed partial class World
{
    // d_englsh.h: the locked door messages.
    public const string PD_BLUEO = "You need a blue key to activate this object";
    public const string PD_REDO = "You need a red key to activate this object";
    public const string PD_YELLOWO = "You need a yellow key to activate this object";
    public const string PD_BLUEK = "You need a blue key to open this door";
    public const string PD_REDK = "You need a red key to open this door";
    public const string PD_YELLOWK = "You need a yellow key to open this door";

    /// <summary>p_doors.c <c>T_VerticalDoor</c>: T_VerticalDoor, a door's thinker each tic.</summary>
    public void T_VerticalDoor(vldoor_t door)
    {
        result_e res;

        switch (door.direction)
        {
            case 0:
                // WAITING
                if (--door.topcountdown == 0)
                {
                    switch (door.type)
                    {
                        case vldoor_e.vld_blazeRaise:
                            door.direction = -1; // time to go back down
                            S_StartSound(door.sector, sfxenum_t.sfx_bdcls);
                            break;

                        case vldoor_e.vld_normal:
                            door.direction = -1; // time to go back down
                            S_StartSound(door.sector, sfxenum_t.sfx_dorcls);
                            break;

                        case vldoor_e.vld_close30ThenOpen:
                            door.direction = 1;
                            S_StartSound(door.sector, sfxenum_t.sfx_doropn);
                            break;

                        default:
                            break;
                    }
                }
                break;

            case 2:
                //  INITIAL WAIT
                if (--door.topcountdown == 0)
                {
                    switch (door.type)
                    {
                        case vldoor_e.vld_raiseIn5Mins:
                            door.direction = 1;
                            door.type = vldoor_e.vld_normal;
                            S_StartSound(door.sector, sfxenum_t.sfx_doropn);
                            break;

                        default:
                            break;
                    }
                }
                break;

            case -1:
                // DOWN
                res = T_MovePlane(door.sector, door.speed, door.sector.floorheight, false, 1, door.direction);
                if (res == result_e.pastdest)
                {
                    switch (door.type)
                    {
                        case vldoor_e.vld_blazeRaise:
                        case vldoor_e.vld_blazeClose:
                            door.sector.specialdata = null;
                            P_RemoveThinker(door); // unlink and free
                            S_StartSound(door.sector, sfxenum_t.sfx_bdcls);
                            break;

                        case vldoor_e.vld_normal:
                        case vldoor_e.vld_close:
                            door.sector.specialdata = null;
                            P_RemoveThinker(door); // unlink and free
                            break;

                        case vldoor_e.vld_close30ThenOpen:
                            door.direction = 0;
                            door.topcountdown = 35 * 30;
                            break;

                        default:
                            break;
                    }
                }
                else if (res == result_e.crushed)
                {
                    switch (door.type)
                    {
                        case vldoor_e.vld_blazeClose:
                        case vldoor_e.vld_close: // DO NOT GO BACK UP!
                            break;

                        default:
                            door.direction = 1;
                            S_StartSound(door.sector, sfxenum_t.sfx_doropn);
                            break;
                    }
                }
                break;

            case 1:
                // UP
                res = T_MovePlane(door.sector, door.speed, door.topheight, false, 1, door.direction);

                if (res == result_e.pastdest)
                {
                    switch (door.type)
                    {
                        case vldoor_e.vld_blazeRaise:
                        case vldoor_e.vld_normal:
                            door.direction = 0; // wait at top
                            door.topcountdown = door.topwait;
                            break;

                        case vldoor_e.vld_close30ThenOpen:
                        case vldoor_e.vld_blazeOpen:
                        case vldoor_e.vld_open:
                            door.sector.specialdata = null;
                            P_RemoveThinker(door); // unlink and free
                            break;

                        default:
                            break;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// p_doors.c <c>EV_DoLockedDoor</c>: Move a locked door up/down (the
    /// blazing switch doors 99 and 133-137): a player without the line's key
    /// (card or skull, <see cref="player_t.cards"/>, given by T5.8) gets a
    /// message and an "oof"; others never open it. Then <see cref="EV_DoDoor"/>.
    /// </summary>
    public int EV_DoLockedDoor(line_t line, vldoor_e type, mobj_t thing)
    {
        player_t? p = thing.player;

        if (p == null)
            return 0;

        switch (line.special)
        {
            case 99: // Blue Lock
            case 133:
                if (!p.cards[(int)card_t.it_bluecard] && !p.cards[(int)card_t.it_blueskull])
                {
                    p.message = PD_BLUEO;
                    S_StartSound((mobj_t?)null, sfxenum_t.sfx_oof);
                    return 0;
                }
                break;

            case 134: // Red Lock
            case 135:
                if (!p.cards[(int)card_t.it_redcard] && !p.cards[(int)card_t.it_redskull])
                {
                    p.message = PD_REDO;
                    S_StartSound((mobj_t?)null, sfxenum_t.sfx_oof);
                    return 0;
                }
                break;

            case 136: // Yellow Lock
            case 137:
                if (!p.cards[(int)card_t.it_yellowcard] && !p.cards[(int)card_t.it_yellowskull])
                {
                    p.message = PD_YELLOWO;
                    S_StartSound((mobj_t?)null, sfxenum_t.sfx_oof);
                    return 0;
                }
                break;
        }

        return EV_DoDoor(line, type);
    }

    /// <summary>
    /// p_doors.c <c>EV_DoDoor</c>: starts a door of <paramref name="type"/> in
    /// every sector tagged like <paramref name="line"/> that has no special
    /// moving it (<see cref="sector_t.specialdata"/>); returns 1 when it
    /// started one. Opening doors go to 4 units below the lowest surrounding
    /// ceiling.
    /// </summary>
    public int EV_DoDoor(line_t line, vldoor_e type)
    {
        int secnum = -1;
        int rtn = 0;

        while ((secnum = P_FindSectorFromLineTag(line, secnum)) >= 0)
        {
            sector_t sec = sectors[secnum];
            if (sec.specialdata != null)
                continue;

            // new door thinker
            rtn = 1;
            var door = new vldoor_t();
            P_AddThinker(door);
            sec.specialdata = door;

            door.function = think_t.T_VerticalDoor;
            door.sector = sec;
            door.type = type;
            door.topwait = VDoor.VDOORWAIT;
            door.speed = VDoor.VDOORSPEED;

            switch (type)
            {
                case vldoor_e.vld_blazeClose:
                    door.topheight = P_FindLowestCeilingSurrounding(sec);
                    door.topheight -= 4 * Fixed.FRACUNIT;
                    door.direction = -1;
                    door.speed = VDoor.VDOORSPEED * 4;
                    S_StartSound(door.sector, sfxenum_t.sfx_bdcls);
                    break;

                case vldoor_e.vld_close:
                    door.topheight = P_FindLowestCeilingSurrounding(sec);
                    door.topheight -= 4 * Fixed.FRACUNIT;
                    door.direction = -1;
                    S_StartSound(door.sector, sfxenum_t.sfx_dorcls);
                    break;

                case vldoor_e.vld_close30ThenOpen:
                    door.topheight = sec.ceilingheight;
                    door.direction = -1;
                    S_StartSound(door.sector, sfxenum_t.sfx_dorcls);
                    break;

                case vldoor_e.vld_blazeRaise:
                case vldoor_e.vld_blazeOpen:
                    door.direction = 1;
                    door.topheight = P_FindLowestCeilingSurrounding(sec);
                    door.topheight -= 4 * Fixed.FRACUNIT;
                    door.speed = VDoor.VDOORSPEED * 4;
                    if (door.topheight != sec.ceilingheight)
                        S_StartSound(door.sector, sfxenum_t.sfx_bdopn);
                    break;

                case vldoor_e.vld_normal:
                case vldoor_e.vld_open:
                    door.direction = 1;
                    door.topheight = P_FindLowestCeilingSurrounding(sec);
                    door.topheight -= 4 * Fixed.FRACUNIT;
                    if (door.topheight != sec.ceilingheight)
                        S_StartSound(door.sector, sfxenum_t.sfx_doropn);
                    break;

                default:
                    break;
            }
        }
        return rtn;
    }

    /// <summary>
    /// p_doors.c <c>EV_VerticalDoor</c>: open a door manually, no tag value
    /// (the door is the line's back sector; only front sides are used). The
    /// locked ones (26-28, 32-34) need the key, which only a player can have.
    /// A raise door (1, 26-28, 117) already moving goes back up when
    /// closing, and closes at once when opening or open (players only: "bad
    /// guys never close doors"). The others start a <see cref="vldoor_t"/>;
    /// the open-and-stay ones (31-34, 118) clear the line's special.
    /// <para>
    /// The sector's <see cref="sector_t.specialdata"/> can be another special
    /// than a door (a lift or a floor: T5.5); vanilla then reads and writes
    /// the door's direction in that thinker's memory (SPEC §12 T5.3).
    /// </para>
    /// </summary>
    public void EV_VerticalDoor(line_t line, mobj_t thing)
    {
        const int side = 0; // only front sides can be used

        //	Check for locks
        player_t? player = thing.player;

        switch (line.special)
        {
            case 26: // Blue Lock
            case 32:
                if (player == null)
                    return;

                if (!player.cards[(int)card_t.it_bluecard] && !player.cards[(int)card_t.it_blueskull])
                {
                    player.message = PD_BLUEK;
                    S_StartSound((mobj_t?)null, sfxenum_t.sfx_oof);
                    return;
                }
                break;

            case 27: // Yellow Lock
            case 34:
                if (player == null)
                    return;

                if (!player.cards[(int)card_t.it_yellowcard] && !player.cards[(int)card_t.it_yellowskull])
                {
                    player.message = PD_YELLOWK;
                    S_StartSound((mobj_t?)null, sfxenum_t.sfx_oof);
                    return;
                }
                break;

            case 28: // Red Lock
            case 33:
                if (player == null)
                    return;

                if (!player.cards[(int)card_t.it_redcard] && !player.cards[(int)card_t.it_redskull])
                {
                    player.message = PD_REDK;
                    S_StartSound((mobj_t?)null, sfxenum_t.sfx_oof);
                    return;
                }
                break;
        }

        // if the sector has an active thinker, use it
        sector_t sec = sides[line.sidenum[side ^ 1]].sector;
        vldoor_t door;

        if (sec.specialdata != null)
        {
            switch (line.special)
            {
                case 1: // ONLY FOR "RAISE" DOORS, NOT "OPEN"s
                case 26:
                case 27:
                case 28:
                case 117:
                    // Only doors exist yet; a lift or floor here is T5.5's (SPEC §12 T5.3).
                    door = (vldoor_t)sec.specialdata;
                    if (door.direction == -1)
                    {
                        door.direction = 1; // go back up
                    }
                    else
                    {
                        if (thing.player == null)
                            return; // JDC: bad guys never close doors

                        door.direction = -1; // start going down immediately
                    }
                    return;
            }
        }

        // for proper sound
        switch (line.special)
        {
            case 117: // BLAZING DOOR RAISE
            case 118: // BLAZING DOOR OPEN
                S_StartSound(sec, sfxenum_t.sfx_bdopn);
                break;

            case 1: // NORMAL DOOR SOUND
            case 31:
                S_StartSound(sec, sfxenum_t.sfx_doropn);
                break;

            default: // LOCKED DOOR SOUND
                S_StartSound(sec, sfxenum_t.sfx_doropn);
                break;
        }

        // new door thinker
        door = new vldoor_t();
        P_AddThinker(door);
        sec.specialdata = door;
        door.function = think_t.T_VerticalDoor;
        door.sector = sec;
        door.direction = 1;
        door.speed = VDoor.VDOORSPEED;
        door.topwait = VDoor.VDOORWAIT;

        switch (line.special)
        {
            case 1:
            case 26:
            case 27:
            case 28:
                door.type = vldoor_e.vld_normal;
                break;

            case 31:
            case 32:
            case 33:
            case 34:
                door.type = vldoor_e.vld_open;
                line.special = 0;
                break;

            case 117: // blazing door raise
                door.type = vldoor_e.vld_blazeRaise;
                door.speed = VDoor.VDOORSPEED * 4;
                break;
            case 118: // blazing door open
                door.type = vldoor_e.vld_blazeOpen;
                line.special = 0;
                door.speed = VDoor.VDOORSPEED * 4;
                break;
        }

        // find the top and bottom of the movement range
        door.topheight = P_FindLowestCeilingSurrounding(sec);
        door.topheight -= 4 * Fixed.FRACUNIT;
    }

    /// <summary>
    /// p_doors.c <c>P_SpawnDoorCloseIn30</c>: Spawn a door that closes after
    /// 30 seconds (sector special 10, cleared): a raise door already waiting
    /// open.
    /// </summary>
    public void P_SpawnDoorCloseIn30(sector_t sec)
    {
        var door = new vldoor_t();

        P_AddThinker(door);

        sec.specialdata = door;
        sec.special = 0;

        door.function = think_t.T_VerticalDoor;
        door.sector = sec;
        door.direction = 0;
        door.type = vldoor_e.vld_normal;
        door.speed = VDoor.VDOORSPEED;
        door.topcountdown = 30 * 35;
        // (vanilla leaves topheight and topwait as Z_Malloc found them; a
        // closing raise door reads neither, 0 here.)
    }

    /// <summary>
    /// p_doors.c <c>P_SpawnDoorRaiseIn5Mins</c>: Spawn a door that opens after
    /// 5 minutes (sector special 14, cleared), then acts as a raise door.
    /// </summary>
    public void P_SpawnDoorRaiseIn5Mins(sector_t sec, int secnum)
    {
        var door = new vldoor_t();

        P_AddThinker(door);

        sec.specialdata = door;
        sec.special = 0;

        door.function = think_t.T_VerticalDoor;
        door.sector = sec;
        door.direction = 2;
        door.type = vldoor_e.vld_raiseIn5Mins;
        door.speed = VDoor.VDOORSPEED;
        door.topheight = P_FindLowestCeilingSurrounding(sec);
        door.topheight -= 4 * Fixed.FRACUNIT;
        door.topwait = VDoor.VDOORWAIT;
        door.topcountdown = 5 * 60 * 35;
    }
}
