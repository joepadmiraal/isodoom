using IsoDoom.Map;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Sim;

// p_floor.c: floor animation code: T_MovePlane, which doors (T5.3), floors,
// lifts and ceilings share, and the floors (T_MoveFloor, EV_DoFloor,
// EV_BuildStairs; T5.5). EV_DoDonut is p_spec.c's (World.Spec.cs).
public sealed partial class World
{
    /// <summary>
    /// r_data.c's texture list, for <c>textureheight[]</c>: only
    /// <see cref="floor_e.raiseToTexture"/> reads it (the sim keeps texture
    /// names). Set by whoever loads the level (the level scene, the route
    /// tests); a raise-to-texture floor without it is an error.
    /// </summary>
    public Textures? textures;

    /// <summary>
    /// r_data.c <c>textureheight[R_TextureNumForName(name)]</c> (fixed_t): the
    /// "no texture" <c>-</c> is texture 0, whose height vanilla reads too
    /// (DOOM1's <c>AASTINKY</c>: 72).
    /// </summary>
    public int textureheight(string name)
    {
        if (textures == null)
            throw new System.InvalidOperationException("World.textures is not set: raiseToTexture needs the texture heights.");
        int num = textures.R_TextureNumForName(name);
        return textures.TextureDefs[num].Height << Fixed.FRACBITS;
    }

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

    /// <summary>
    /// p_floor.c <c>T_MoveFloor</c>: MOVE A FLOOR TO IT'S DESTINATION (UP OR
    /// DOWN). <c>sfx_stnmov</c> every 8 tics; at the destination the sector
    /// is free again, a donut's rising slime or a lower-and-change floor
    /// takes its new flat and special, and <c>sfx_pstop</c>.
    /// </summary>
    public void T_MoveFloor(floormove_t floor)
    {
        result_e res = T_MovePlane(floor.sector, floor.speed, floor.floordestheight, floor.crush, 0, floor.direction);

        if ((leveltime & 7) == 0)
            S_StartSound(floor.sector, sfxenum_t.sfx_stnmov);

        if (res == result_e.pastdest)
        {
            floor.sector.specialdata = null;

            if (floor.direction == 1)
            {
                switch (floor.type)
                {
                    case floor_e.donutRaise:
                        floor.sector.special = (short)floor.newspecial;
                        ChangeFloorPic(floor);
                        break;
                }
            }
            else if (floor.direction == -1)
            {
                switch (floor.type)
                {
                    case floor_e.lowerAndChange:
                        floor.sector.special = (short)floor.newspecial;
                        ChangeFloorPic(floor);
                        break;
                }
            }
            P_RemoveThinker(floor);

            S_StartSound(floor.sector, sfxenum_t.sfx_pstop);
        }
    }

    /// <summary>
    /// <c>floor-&gt;sector-&gt;floorpic = floor-&gt;texture</c>; a texture that
    /// <see cref="EV_VerticalDoor"/> overwrote (vanilla's flat -1, null here)
    /// leaves the flat as it is (SPEC §12 T5.5).
    /// </summary>
    private static void ChangeFloorPic(floormove_t floor)
    {
        if (floor.texture != null)
            floor.sector.floorpic = floor.texture;
    }

    /// <summary>
    /// <c>sectors[secnum].lines[i]</c> as vanilla reads it: the sectors'
    /// line lists are consecutive slices of one buffer (p_setup.c
    /// <c>P_GroupLines</c>'s <c>linebuffer</c>, in sector order), so an index
    /// past the sector's own lines reads the next sectors'.
    /// <see cref="EV_DoFloor"/>'s <see cref="floor_e.lowerAndChange"/> does
    /// (its loop bound follows the neighbour it looked at); past the last
    /// sector's lines is an error.
    /// </summary>
    private line_t SectorLine(int secnum, int i)
    {
        while (i >= sectors[secnum].linecount)
        {
            i -= sectors[secnum].linecount;
            if (++secnum >= sectors.Length)
                throw new WadFormatException("EV_DoFloor: read past the last sector's lines (vanilla reads garbage here)");
        }
        return sectors[secnum].lines[i];
    }

    /// <summary>
    /// p_floor.c <c>EV_DoFloor</c>: HANDLE FLOOR TYPES. Starts a
    /// <see cref="floormove_t"/> of <paramref name="floortype"/> in every
    /// sector tagged like <paramref name="line"/> that is not already moving;
    /// returns 1 when it started one.
    /// </summary>
    public int EV_DoFloor(line_t line, floor_e floortype)
    {
        int secnum = -1;
        int rtn = 0;

        while ((secnum = P_FindSectorFromLineTag(line, secnum)) >= 0)
        {
            sector_t sec = sectors[secnum];

            // ALREADY MOVING?  IF SO, KEEP GOING...
            if (sec.specialdata != null)
                continue;

            // new floor thinker
            rtn = 1;
            var floor = new floormove_t();
            P_AddThinker(floor);
            sec.specialdata = floor;
            floor.function = think_t.T_MoveFloor;
            floor.type = floortype;
            floor.crush = false;

            switch (floortype)
            {
                case floor_e.lowerFloor:
                    floor.direction = -1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED;
                    floor.floordestheight = P_FindHighestFloorSurrounding(sec);
                    break;

                case floor_e.lowerFloorToLowest:
                    floor.direction = -1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED;
                    floor.floordestheight = P_FindLowestFloorSurrounding(sec);
                    break;

                case floor_e.turboLower:
                    floor.direction = -1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED * 4;
                    floor.floordestheight = P_FindHighestFloorSurrounding(sec);
                    if (floor.floordestheight != sec.floorheight)
                        floor.floordestheight += 8 * Fixed.FRACUNIT;
                    break;

                case floor_e.raiseFloorCrush:
                case floor_e.raiseFloor:
                    if (floortype == floor_e.raiseFloorCrush)
                        floor.crush = true;
                    floor.direction = 1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED;
                    floor.floordestheight = P_FindLowestCeilingSurrounding(sec);
                    if (floor.floordestheight > sec.ceilingheight)
                        floor.floordestheight = sec.ceilingheight;
                    floor.floordestheight -= (8 * Fixed.FRACUNIT) * (floortype == floor_e.raiseFloorCrush ? 1 : 0);
                    break;

                case floor_e.raiseFloorTurbo:
                    floor.direction = 1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED * 4;
                    floor.floordestheight = P_FindNextHighestFloor(sec, sec.floorheight);
                    break;

                case floor_e.raiseFloorToNearest:
                    floor.direction = 1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED;
                    floor.floordestheight = P_FindNextHighestFloor(sec, sec.floorheight);
                    break;

                case floor_e.raiseFloor24:
                    floor.direction = 1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED;
                    floor.floordestheight = floor.sector.floorheight + 24 * Fixed.FRACUNIT;
                    break;

                case floor_e.raiseFloor512:
                    floor.direction = 1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED;
                    floor.floordestheight = floor.sector.floorheight + 512 * Fixed.FRACUNIT;
                    break;

                case floor_e.raiseFloor24AndChange:
                    floor.direction = 1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED;
                    floor.floordestheight = floor.sector.floorheight + 24 * Fixed.FRACUNIT;
                    sec.floorpic = line.frontsector!.floorpic;
                    sec.special = line.frontsector!.special;
                    break;

                case floor_e.raiseToTexture:
                {
                    int minsize = int.MaxValue;

                    floor.direction = 1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED;
                    for (int i = 0; i < sec.linecount; i++)
                    {
                        if (twoSided(secnum, i) != 0)
                        {
                            // (side->bottomtexture >= 0 always holds: "-" is texture 0)
                            side_t side = getSide(secnum, i, 0);
                            if (textureheight(side.bottomtexture) < minsize)
                                minsize = textureheight(side.bottomtexture);
                            side = getSide(secnum, i, 1);
                            if (textureheight(side.bottomtexture) < minsize)
                                minsize = textureheight(side.bottomtexture);
                        }
                    }
                    floor.floordestheight = unchecked(floor.sector.floorheight + minsize);
                    break;
                }

                case floor_e.lowerAndChange:
                    floor.direction = -1;
                    floor.sector = sec;
                    floor.speed = FloorMove.FLOORSPEED;
                    floor.floordestheight = P_FindLowestFloorSurrounding(sec);
                    floor.texture = sec.floorpic;
                    // (newspecial stays as Z_Malloc found it in vanilla when no
                    // neighbour is at the destination; 0 here, SPEC §12 T5.5.)

                    // Vanilla's loop bound is sec->linecount, and sec becomes
                    // each two-sided neighbour looked at, while the lines are
                    // still read from sector secnum's list (SectorLine).
                    for (int i = 0; i < sec.linecount; i++)
                    {
                        line_t check = SectorLine(secnum, i);
                        if ((check.flags & Line.ML_TWOSIDED) != 0)
                        {
                            if (sides[check.sidenum[0]].sector.Index == secnum)
                            {
                                sec = sides[check.sidenum[1]].sector;

                                if (sec.floorheight == floor.floordestheight)
                                {
                                    floor.texture = sec.floorpic;
                                    floor.newspecial = sec.special;
                                    break;
                                }
                            }
                            else
                            {
                                sec = sides[check.sidenum[0]].sector;

                                if (sec.floorheight == floor.floordestheight)
                                {
                                    floor.texture = sec.floorpic;
                                    floor.newspecial = sec.special;
                                    break;
                                }
                            }
                        }
                    }
                    break;

                default:
                    break;
            }
        }
        return rtn;
    }

    /// <summary>
    /// p_floor.c <c>EV_BuildStairs</c>: BUILD A STAIRCASE! From each tagged
    /// sector not already moving, raise it by a step and follow the two-sided
    /// lines whose front is the current step and whose back has the first
    /// step's flat, each next step one step higher. A busy next step still
    /// counts its height (vanilla) and the search goes on from the same step.
    /// </summary>
    public int EV_BuildStairs(line_t line, stair_e type)
    {
        int secnum = -1;
        int rtn = 0;
        int stairsize = 0;
        int speed = 0;

        while ((secnum = P_FindSectorFromLineTag(line, secnum)) >= 0)
        {
            sector_t sec = sectors[secnum];

            // ALREADY MOVING?  IF SO, KEEP GOING...
            if (sec.specialdata != null)
                continue;

            // new floor thinker
            rtn = 1;
            var floor = new floormove_t();
            P_AddThinker(floor);
            sec.specialdata = floor;
            floor.function = think_t.T_MoveFloor;
            floor.direction = 1;
            floor.sector = sec;
            switch (type)
            {
                case stair_e.build8:
                    speed = FloorMove.FLOORSPEED / 4;
                    stairsize = 8 * Fixed.FRACUNIT;
                    break;
                case stair_e.turbo16:
                    speed = FloorMove.FLOORSPEED * 4;
                    stairsize = 16 * Fixed.FRACUNIT;
                    break;
            }
            floor.speed = speed;
            int height = sec.floorheight + stairsize;
            floor.floordestheight = height;

            string texture = sec.floorpic;

            // Find next sector to raise
            // 1.	Find 2-sided line with same sector side[0]
            // 2.	Other side is the next sector to raise
            bool ok;
            do
            {
                ok = false;
                for (int i = 0; i < sec.linecount; i++)
                {
                    if ((sec.lines[i].flags & Line.ML_TWOSIDED) == 0)
                        continue;

                    sector_t tsec = sec.lines[i].frontsector!;
                    int newsecnum = tsec.Index;

                    if (secnum != newsecnum)
                        continue;

                    tsec = sec.lines[i].backsector!;
                    newsecnum = tsec.Index;

                    if (tsec.floorpic != texture)
                        continue;

                    height += stairsize;

                    if (tsec.specialdata != null)
                        continue;

                    sec = tsec;
                    secnum = newsecnum;
                    floor = new floormove_t();

                    P_AddThinker(floor);

                    sec.specialdata = floor;
                    floor.function = think_t.T_MoveFloor;
                    floor.direction = 1;
                    floor.sector = sec;
                    floor.speed = speed;
                    floor.floordestheight = height;
                    ok = true;
                    break;
                }
            } while (ok);
        }
        return rtn;
    }
}
