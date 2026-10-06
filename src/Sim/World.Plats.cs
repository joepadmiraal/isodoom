using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_plats.c: plats (lifts): T_PlatRaise, EV_DoPlat, P_ActivateInStasis,
// EV_StopPlat and the active plat list (T5.5).
public sealed partial class World
{
    /// <summary>p_plats.c <c>activeplats</c>: the lifts that can be stopped and restarted (perpetual ones), <see cref="Plat.MAXPLATS"/> slots.</summary>
    public readonly plat_t?[] activeplats = new plat_t?[Plat.MAXPLATS];

    /// <summary>
    /// p_plats.c <c>T_PlatRaise</c>: Move a plat up and down. Going up, a
    /// non-crushing lift that something blocks goes back down; at the top a
    /// one-way lift (down-wait-up-stay, raise-and-change) is done. A lift
    /// waits <see cref="plat_t.wait"/> tics at either end, then goes the
    /// other way.
    /// </summary>
    public void T_PlatRaise(plat_t plat)
    {
        result_e res;

        switch (plat.status)
        {
            case plat_e.up:
                res = T_MovePlane(plat.sector, plat.speed, plat.high, plat.crush, 0, 1);

                if (plat.type == plattype_e.raiseAndChange || plat.type == plattype_e.raiseToNearestAndChange)
                {
                    if ((leveltime & 7) == 0)
                        S_StartSound(plat.sector, sfxenum_t.sfx_stnmov);
                }

                if (res == result_e.crushed && !plat.crush)
                {
                    plat.count = plat.wait;
                    plat.status = plat_e.down;
                    S_StartSound(plat.sector, sfxenum_t.sfx_pstart);
                }
                else
                {
                    if (res == result_e.pastdest)
                    {
                        plat.count = plat.wait;
                        plat.status = plat_e.waiting;
                        S_StartSound(plat.sector, sfxenum_t.sfx_pstop);

                        switch (plat.type)
                        {
                            case plattype_e.blazeDWUS:
                            case plattype_e.downWaitUpStay:
                                P_RemoveActivePlat(plat);
                                break;

                            case plattype_e.raiseAndChange:
                            case plattype_e.raiseToNearestAndChange:
                                P_RemoveActivePlat(plat);
                                break;

                            default:
                                break;
                        }
                    }
                }
                break;

            case plat_e.down:
                res = T_MovePlane(plat.sector, plat.speed, plat.low, false, 0, -1);

                if (res == result_e.pastdest)
                {
                    plat.count = plat.wait;
                    plat.status = plat_e.waiting;
                    S_StartSound(plat.sector, sfxenum_t.sfx_pstop);
                }
                break;

            case plat_e.waiting:
                if (--plat.count == 0)
                {
                    if (plat.sector.floorheight == plat.low)
                        plat.status = plat_e.up;
                    else
                        plat.status = plat_e.down;
                    S_StartSound(plat.sector, sfxenum_t.sfx_pstart);
                }
                break;

            case plat_e.in_stasis:
                break;
        }
    }

    /// <summary>
    /// p_plats.c <c>EV_DoPlat</c>: Do Platforms. "amount" is only used for
    /// SOME platforms (<see cref="plattype_e.raiseAndChange"/>'s height in
    /// units). A perpetual lift first restarts the stopped ones of the line's
    /// tag; then every tagged sector not already moving gets a
    /// <see cref="plat_t"/> (listed in <see cref="activeplats"/>); returns 1
    /// when one started.
    /// </summary>
    public int EV_DoPlat(line_t line, plattype_e type, int amount)
    {
        int secnum = -1;
        int rtn = 0;

        //	Activate all <type> plats that are in_stasis
        switch (type)
        {
            case plattype_e.perpetualRaise:
                P_ActivateInStasis(line.tag);
                break;

            default:
                break;
        }

        while ((secnum = P_FindSectorFromLineTag(line, secnum)) >= 0)
        {
            sector_t sec = sectors[secnum];

            if (sec.specialdata != null)
                continue;

            // Find lowest & highest floors around sector
            rtn = 1;
            var plat = new plat_t();
            P_AddThinker(plat);

            plat.type = type;
            plat.sector = sec;
            plat.sector.specialdata = plat;
            plat.function = think_t.T_PlatRaise;
            plat.crush = false;
            plat.tag = line.tag;

            switch (type)
            {
                case plattype_e.raiseToNearestAndChange:
                    plat.speed = Plat.PLATSPEED / 2;
                    sec.floorpic = sides[line.sidenum[0]].sector.floorpic;
                    plat.high = P_FindNextHighestFloor(sec, sec.floorheight);
                    plat.wait = 0;
                    plat.status = plat_e.up;
                    // NO MORE DAMAGE, IF APPLICABLE
                    sec.special = 0;

                    S_StartSound(sec, sfxenum_t.sfx_stnmov);
                    break;

                case plattype_e.raiseAndChange:
                    plat.speed = Plat.PLATSPEED / 2;
                    sec.floorpic = sides[line.sidenum[0]].sector.floorpic;
                    plat.high = sec.floorheight + amount * Fixed.FRACUNIT;
                    plat.wait = 0;
                    plat.status = plat_e.up;

                    S_StartSound(sec, sfxenum_t.sfx_stnmov);
                    break;

                case plattype_e.downWaitUpStay:
                    plat.speed = Plat.PLATSPEED * 4;
                    plat.low = P_FindLowestFloorSurrounding(sec);

                    if (plat.low > sec.floorheight)
                        plat.low = sec.floorheight;

                    plat.high = sec.floorheight;
                    plat.wait = Plat.TICRATE * Plat.PLATWAIT;
                    plat.status = plat_e.down;
                    S_StartSound(sec, sfxenum_t.sfx_pstart);
                    break;

                case plattype_e.blazeDWUS:
                    plat.speed = Plat.PLATSPEED * 8;
                    plat.low = P_FindLowestFloorSurrounding(sec);

                    if (plat.low > sec.floorheight)
                        plat.low = sec.floorheight;

                    plat.high = sec.floorheight;
                    plat.wait = Plat.TICRATE * Plat.PLATWAIT;
                    plat.status = plat_e.down;
                    S_StartSound(sec, sfxenum_t.sfx_pstart);
                    break;

                case plattype_e.perpetualRaise:
                    plat.speed = Plat.PLATSPEED;
                    plat.low = P_FindLowestFloorSurrounding(sec);

                    if (plat.low > sec.floorheight)
                        plat.low = sec.floorheight;

                    plat.high = P_FindHighestFloorSurrounding(sec);

                    if (plat.high < sec.floorheight)
                        plat.high = sec.floorheight;

                    plat.wait = Plat.TICRATE * Plat.PLATWAIT;
                    plat.status = (plat_e)(P_Random() & 1);

                    S_StartSound(sec, sfxenum_t.sfx_pstart);
                    break;
            }
            P_AddActivePlat(plat);
        }
        return rtn;
    }

    /// <summary>p_plats.c <c>P_ActivateInStasis</c>: restarts the stopped lifts of <paramref name="tag"/> where they were.</summary>
    public void P_ActivateInStasis(int tag)
    {
        for (int i = 0; i < Plat.MAXPLATS; i++)
        {
            plat_t? plat = activeplats[i];
            if (plat != null && plat.tag == tag && plat.status == plat_e.in_stasis)
            {
                plat.status = plat.oldstatus;
                plat.function = think_t.T_PlatRaise;
            }
        }
    }

    /// <summary>p_plats.c <c>EV_StopPlat</c>: stops the moving or waiting lifts of the line's tag (in stasis: their thinker does nothing).</summary>
    public void EV_StopPlat(line_t line)
    {
        for (int j = 0; j < Plat.MAXPLATS; j++)
        {
            plat_t? plat = activeplats[j];
            if (plat != null && plat.status != plat_e.in_stasis && plat.tag == line.tag)
            {
                plat.oldstatus = plat.status;
                plat.status = plat_e.in_stasis;
                plat.function = think_t.NULL;
            }
        }
    }

    /// <summary>p_plats.c <c>P_AddActivePlat</c>: lists a lift in the first free slot; with none, vanilla's error (as Chocolate Doom).</summary>
    public void P_AddActivePlat(plat_t plat)
    {
        for (int i = 0; i < Plat.MAXPLATS; i++)
        {
            if (activeplats[i] == null)
            {
                activeplats[i] = plat;
                return;
            }
        }
        throw new System.InvalidOperationException("P_AddActivePlat: no more plats!");
    }

    /// <summary>p_plats.c <c>P_RemoveActivePlat</c>: frees the lift's sector, removes its thinker and its slot; not listed is vanilla's error.</summary>
    public void P_RemoveActivePlat(plat_t plat)
    {
        for (int i = 0; i < Plat.MAXPLATS; i++)
        {
            if (plat == activeplats[i])
            {
                plat.sector.specialdata = null;
                P_RemoveThinker(plat);
                activeplats[i] = null;

                return;
            }
        }
        throw new System.InvalidOperationException("P_RemoveActivePlat: can't find plat!");
    }
}
