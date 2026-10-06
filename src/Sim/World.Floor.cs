namespace IsoDoom.Sim;

// p_floor.c: T_MovePlane, which doors (T5.3), floors, lifts and ceilings
// (T5.5) share. EV_DoFloor, EV_BuildStairs and EV_DoDonut come with T5.5.
public sealed partial class World
{
    /// <summary>
    /// p_floor.c <c>T_MovePlane</c>: Move a plane (floor or ceiling) and check
    /// for crushing. Moves <paramref name="sector"/>'s floor
    /// (<paramref name="floorOrCeiling"/> 0) or ceiling (1) by
    /// <paramref name="speed"/> towards <paramref name="dest"/> (fixed_t) in
    /// <paramref name="direction"/> (1 up, -1 down), fitting the things in it
    /// (<see cref="P_ChangeSector"/>). Reaching <paramref name="dest"/> is
    /// <see cref="result_e.pastdest"/> (a thing that does not fit there puts
    /// the plane back where it was, still <c>pastdest</c>, as vanilla); a step
    /// that a thing does not fit is undone (<see cref="result_e.crushed"/>),
    /// except with <paramref name="crush"/> for a rising floor or a lowering
    /// ceiling, which stay (the things get crushed); a rising ceiling never
    /// is.
    /// </summary>
    public result_e T_MovePlane(sector_t sector, int speed, int dest, bool crush, int floorOrCeiling, int direction)
    {
        bool flag;
        int lastpos;

        switch (floorOrCeiling)
        {
            case 0:
                // FLOOR
                switch (direction)
                {
                    case -1:
                        // DOWN
                        if (sector.floorheight - speed < dest)
                        {
                            lastpos = sector.floorheight;
                            sector.floorheight = dest;
                            flag = P_ChangeSector(sector, crush);
                            if (flag)
                            {
                                sector.floorheight = lastpos;
                                P_ChangeSector(sector, crush);
                                //return crushed;
                            }
                            return result_e.pastdest;
                        }
                        else
                        {
                            lastpos = sector.floorheight;
                            sector.floorheight -= speed;
                            flag = P_ChangeSector(sector, crush);
                            if (flag)
                            {
                                sector.floorheight = lastpos;
                                P_ChangeSector(sector, crush);
                                return result_e.crushed;
                            }
                        }
                        break;

                    case 1:
                        // UP
                        if (sector.floorheight + speed > dest)
                        {
                            lastpos = sector.floorheight;
                            sector.floorheight = dest;
                            flag = P_ChangeSector(sector, crush);
                            if (flag)
                            {
                                sector.floorheight = lastpos;
                                P_ChangeSector(sector, crush);
                                //return crushed;
                            }
                            return result_e.pastdest;
                        }
                        else
                        {
                            // COULD GET CRUSHED
                            lastpos = sector.floorheight;
                            sector.floorheight += speed;
                            flag = P_ChangeSector(sector, crush);
                            if (flag)
                            {
                                if (crush)
                                    return result_e.crushed;
                                sector.floorheight = lastpos;
                                P_ChangeSector(sector, crush);
                                return result_e.crushed;
                            }
                        }
                        break;
                }
                break;

            case 1:
                // CEILING
                switch (direction)
                {
                    case -1:
                        // DOWN
                        if (sector.ceilingheight - speed < dest)
                        {
                            lastpos = sector.ceilingheight;
                            sector.ceilingheight = dest;
                            flag = P_ChangeSector(sector, crush);

                            if (flag)
                            {
                                sector.ceilingheight = lastpos;
                                P_ChangeSector(sector, crush);
                                //return crushed;
                            }
                            return result_e.pastdest;
                        }
                        else
                        {
                            // COULD GET CRUSHED
                            lastpos = sector.ceilingheight;
                            sector.ceilingheight -= speed;
                            flag = P_ChangeSector(sector, crush);

                            if (flag)
                            {
                                if (crush)
                                    return result_e.crushed;
                                sector.ceilingheight = lastpos;
                                P_ChangeSector(sector, crush);
                                return result_e.crushed;
                            }
                        }
                        break;

                    case 1:
                        // UP
                        if (sector.ceilingheight + speed > dest)
                        {
                            lastpos = sector.ceilingheight;
                            sector.ceilingheight = dest;
                            flag = P_ChangeSector(sector, crush);
                            if (flag)
                            {
                                sector.ceilingheight = lastpos;
                                P_ChangeSector(sector, crush);
                                //return crushed;
                            }
                            return result_e.pastdest;
                        }
                        else
                        {
                            sector.ceilingheight += speed;
                            P_ChangeSector(sector, crush);
                            // (vanilla's "UNUSED" #if 0: a rising ceiling is never crushed)
                        }
                        break;
                }
                break;
        }
        return result_e.ok;
    }
}
