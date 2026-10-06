using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_ceilng.c: ceilings and crushers: T_MoveCeiling, EV_DoCeiling,
// P_ActivateInStasisCeiling, EV_CeilingCrushStop and the active ceiling list
// (T5.5).
public sealed partial class World
{
    /// <summary>p_ceilng.c <c>activeceilings</c>: the ceilings that can be stopped and restarted, <see cref="CeilingMove.MAXCEILINGS"/> slots.</summary>
    public readonly ceiling_t?[] activeceilings = new ceiling_t?[CeilingMove.MAXCEILINGS];

    /// <summary>
    /// p_ceilng.c <c>T_MoveCeiling</c>: a ceiling's thinker each tic.
    /// <c>sfx_stnmov</c> every 8 tics (not the silent crusher); crushers go
    /// up and down for ever (slowed to 1/8 while crushing, the normal and
    /// silent ones; the fast one keeps its speed); the others are done at
    /// their destination.
    /// </summary>
    public void T_MoveCeiling(ceiling_t ceiling)
    {
        result_e res;

        switch (ceiling.direction)
        {
            case 0:
                // IN STASIS
                break;

            case 1:
                // UP
                res = T_MovePlane(ceiling.sector, ceiling.speed, ceiling.topheight, false, 1, ceiling.direction);

                if ((leveltime & 7) == 0)
                {
                    switch (ceiling.type)
                    {
                        case ceiling_e.silentCrushAndRaise:
                            break;
                        default:
                            S_StartSound(ceiling.sector, sfxenum_t.sfx_stnmov);
                            // ?
                            break;
                    }
                }

                if (res == result_e.pastdest)
                {
                    switch (ceiling.type)
                    {
                        case ceiling_e.raiseToHighest:
                            P_RemoveActiveCeiling(ceiling);
                            break;

                        case ceiling_e.silentCrushAndRaise:
                        case ceiling_e.fastCrushAndRaise:
                        case ceiling_e.crushAndRaise:
                            if (ceiling.type == ceiling_e.silentCrushAndRaise)
                                S_StartSound(ceiling.sector, sfxenum_t.sfx_pstop);
                            ceiling.direction = -1;
                            break;

                        default:
                            break;
                    }
                }
                break;

            case -1:
                // DOWN
                res = T_MovePlane(ceiling.sector, ceiling.speed, ceiling.bottomheight, ceiling.crush != 0, 1, ceiling.direction);

                if ((leveltime & 7) == 0)
                {
                    switch (ceiling.type)
                    {
                        case ceiling_e.silentCrushAndRaise: break;
                        default:
                            S_StartSound(ceiling.sector, sfxenum_t.sfx_stnmov);
                            break;
                    }
                }

                if (res == result_e.pastdest)
                {
                    switch (ceiling.type)
                    {
                        // (vanilla falls through from each case to the next)
                        case ceiling_e.silentCrushAndRaise:
                        case ceiling_e.crushAndRaise:
                        case ceiling_e.fastCrushAndRaise:
                            if (ceiling.type == ceiling_e.silentCrushAndRaise)
                                S_StartSound(ceiling.sector, sfxenum_t.sfx_pstop);
                            if (ceiling.type != ceiling_e.fastCrushAndRaise)
                                ceiling.speed = CeilingMove.CEILSPEED;
                            ceiling.direction = 1;
                            break;

                        case ceiling_e.lowerAndCrush:
                        case ceiling_e.lowerToFloor:
                            P_RemoveActiveCeiling(ceiling);
                            break;

                        default:
                            break;
                    }
                }
                else // ( res != pastdest )
                {
                    if (res == result_e.crushed)
                    {
                        switch (ceiling.type)
                        {
                            case ceiling_e.silentCrushAndRaise:
                            case ceiling_e.crushAndRaise:
                            case ceiling_e.lowerAndCrush:
                                ceiling.speed = CeilingMove.CEILSPEED / 8;
                                break;

                            default:
                                break;
                        }
                    }
                }
                break;
        }
    }

    /// <summary>
    /// p_ceilng.c <c>EV_DoCeiling</c>: Move a ceiling up/down and all
    /// around! The crushers first restart the stopped ones of the line's
    /// tag; then every tagged sector not already moving gets a
    /// <see cref="ceiling_t"/> (listed in <see cref="activeceilings"/>);
    /// returns 1 when one started.
    /// </summary>
    public int EV_DoCeiling(line_t line, ceiling_e type)
    {
        int secnum = -1;
        int rtn = 0;

        //	Reactivate in-stasis ceilings...for certain types.
        switch (type)
        {
            case ceiling_e.fastCrushAndRaise:
            case ceiling_e.silentCrushAndRaise:
            case ceiling_e.crushAndRaise:
                P_ActivateInStasisCeiling(line);
                break;
            default:
                break;
        }

        while ((secnum = P_FindSectorFromLineTag(line, secnum)) >= 0)
        {
            sector_t sec = sectors[secnum];
            if (sec.specialdata != null)
                continue;

            // new door thinker
            rtn = 1;
            var ceiling = new ceiling_t();
            P_AddThinker(ceiling);
            sec.specialdata = ceiling;
            ceiling.function = think_t.T_MoveCeiling;
            ceiling.sector = sec;
            ceiling.crush = 0;

            switch (type)
            {
                case ceiling_e.fastCrushAndRaise:
                    ceiling.crush = 1;
                    ceiling.topheight = sec.ceilingheight;
                    ceiling.bottomheight = sec.floorheight + (8 * Fixed.FRACUNIT);
                    ceiling.direction = -1;
                    ceiling.speed = CeilingMove.CEILSPEED * 2;
                    break;

                // (vanilla falls through from the crushers to the lowering ones)
                case ceiling_e.silentCrushAndRaise:
                case ceiling_e.crushAndRaise:
                case ceiling_e.lowerAndCrush:
                case ceiling_e.lowerToFloor:
                    if (type is ceiling_e.silentCrushAndRaise or ceiling_e.crushAndRaise)
                    {
                        ceiling.crush = 1;
                        ceiling.topheight = sec.ceilingheight;
                    }
                    ceiling.bottomheight = sec.floorheight;
                    if (type != ceiling_e.lowerToFloor)
                        ceiling.bottomheight += 8 * Fixed.FRACUNIT;
                    ceiling.direction = -1;
                    ceiling.speed = CeilingMove.CEILSPEED;
                    break;

                case ceiling_e.raiseToHighest:
                    ceiling.topheight = P_FindHighestCeilingSurrounding(sec);
                    ceiling.direction = 1;
                    ceiling.speed = CeilingMove.CEILSPEED;
                    break;
            }

            ceiling.tag = sec.tag;
            ceiling.type = type;
            P_AddActiveCeiling(ceiling);
        }
        return rtn;
    }

    /// <summary>
    /// p_ceilng.c <c>P_AddActiveCeiling</c>: Add an active ceiling, in the
    /// first free slot; with none it is simply not listed (vanilla and
    /// Chocolate Doom: it then moves but can never be stopped).
    /// </summary>
    public void P_AddActiveCeiling(ceiling_t c)
    {
        for (int i = 0; i < CeilingMove.MAXCEILINGS; i++)
        {
            if (activeceilings[i] == null)
            {
                activeceilings[i] = c;
                return;
            }
        }
    }

    /// <summary>
    /// p_ceilng.c <c>P_RemoveActiveCeiling</c>: Remove a ceiling's thinker.
    /// One that is not listed (see <see cref="P_AddActiveCeiling"/>) keeps
    /// its thinker and its sector's <see cref="sector_t.specialdata"/>
    /// (vanilla: it stays at its destination and the sector stays busy for ever).
    /// </summary>
    public void P_RemoveActiveCeiling(ceiling_t c)
    {
        for (int i = 0; i < CeilingMove.MAXCEILINGS; i++)
        {
            if (activeceilings[i] == c)
            {
                c.sector.specialdata = null;
                P_RemoveThinker(c);
                activeceilings[i] = null;
                break;
            }
        }
    }

    /// <summary>p_ceilng.c <c>P_ActivateInStasisCeiling</c>: Restart a ceiling that's in-stasis (of the line's tag).</summary>
    public void P_ActivateInStasisCeiling(line_t line)
    {
        for (int i = 0; i < CeilingMove.MAXCEILINGS; i++)
        {
            ceiling_t? c = activeceilings[i];
            if (c != null && c.tag == line.tag && c.direction == 0)
            {
                c.direction = c.olddirection;
                c.function = think_t.T_MoveCeiling;
            }
        }
    }

    /// <summary>
    /// p_ceilng.c <c>EV_CeilingCrushStop</c>: Stop a ceiling from crushing!
    /// Every listed moving ceiling of the line's tag goes in stasis; returns
    /// 1 when one did.
    /// </summary>
    public int EV_CeilingCrushStop(line_t line)
    {
        int rtn = 0;
        for (int i = 0; i < CeilingMove.MAXCEILINGS; i++)
        {
            ceiling_t? c = activeceilings[i];
            if (c != null && c.tag == line.tag && c.direction != 0)
            {
                c.olddirection = c.direction;
                c.function = think_t.NULL;
                c.direction = 0; // in-stasis
                rtn = 1;
            }
        }

        return rtn;
    }
}
