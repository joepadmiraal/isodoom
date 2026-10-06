using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

// p_spec.c: the sector and line lookup helpers the specials use (T5.1).
// Line triggers, P_SpawnSpecials and P_UpdateSpecials come with T5.2 onwards.
public sealed partial class World
{
    /// <summary>
    /// p_spec.c <c>MAX_ADJOINING_SECTORS</c>: the size of
    /// <see cref="P_FindNextHighestFloor"/>'s height list (vanilla overruns it;
    /// see there).
    /// </summary>
    public const int MAX_ADJOINING_SECTORS = 20;

    /// <summary>
    /// p_spec.c <c>getSide</c>: will return a side_t* given the number of the
    /// current sector, the line number, and the side (0/1) that you want.
    /// </summary>
    public side_t getSide(int currentSector, int line, int side) =>
        sides[sectors[currentSector].lines[line].sidenum[side]];

    /// <summary>
    /// p_spec.c <c>getSector</c>: will return a sector_t* given the number of
    /// the current sector, the line number and the side (0/1) that you want.
    /// </summary>
    public sector_t getSector(int currentSector, int line, int side) =>
        sides[sectors[currentSector].lines[line].sidenum[side]].sector;

    /// <summary>
    /// p_spec.c <c>twoSided</c>: given the sector number and the line number,
    /// it will tell you whether the line is two-sided or not
    /// (<see cref="Line.ML_TWOSIDED"/>, nonzero for two-sided, as vanilla's int).
    /// </summary>
    public int twoSided(int sector, int line) => sectors[sector].lines[line].flags & Line.ML_TWOSIDED;

    /// <summary>
    /// p_spec.c <c>getNextSector</c>: return sector_t * of sector next to
    /// current, null if not two-sided line.
    /// </summary>
    public static sector_t? getNextSector(line_t line, sector_t sec)
    {
        if ((line.flags & Line.ML_TWOSIDED) == 0)
            return null;

        if (line.frontsector == sec)
            return line.backsector;

        return line.frontsector;
    }

    /// <summary>p_spec.c <c>P_FindLowestFloorSurrounding</c>: FIND LOWEST FLOOR HEIGHT IN SURROUNDING SECTORS (its own included).</summary>
    public static int P_FindLowestFloorSurrounding(sector_t sec)
    {
        int floor = sec.floorheight;

        for (int i = 0; i < sec.linecount; i++)
        {
            line_t check = sec.lines[i];
            sector_t? other = getNextSector(check, sec);

            if (other == null)
                continue;

            if (other.floorheight < floor)
                floor = other.floorheight;
        }
        return floor;
    }

    /// <summary>p_spec.c <c>P_FindHighestFloorSurrounding</c>: FIND HIGHEST FLOOR HEIGHT IN SURROUNDING SECTORS (-500 units with none).</summary>
    public static int P_FindHighestFloorSurrounding(sector_t sec)
    {
        int floor = -500 * Fixed.FRACUNIT;

        for (int i = 0; i < sec.linecount; i++)
        {
            line_t check = sec.lines[i];
            sector_t? other = getNextSector(check, sec);

            if (other == null)
                continue;

            if (other.floorheight > floor)
                floor = other.floorheight;
        }
        return floor;
    }

    /// <summary>
    /// p_spec.c <c>P_FindNextHighestFloor</c>: FIND NEXT HIGHEST FLOOR IN
    /// SURROUNDING SECTORS, the lowest neighbouring floor above
    /// <paramref name="currentheight"/> (fixed_t), or <paramref name="currentheight"/>
    /// with none.
    /// <para>
    /// Vanilla (the DOS executables) keeps the heights in a stack array of
    /// <see cref="MAX_ADJOINING_SECTORS"/> and writes past it: Chocolate Doom's
    /// emulation (by entryway) is ported, as the vanilla reference
    /// (<c>tools/VanillaRef</c>) runs it: the 22nd higher neighbour also
    /// overwrites the height compared against, and a 23rd crashes vanilla, an
    /// error here (<see cref="WadFormatException"/>). linuxdoom-1.10 instead
    /// stops at 20 (SPEC §12 T5.1).
    /// </para>
    /// </summary>
    public static int P_FindNextHighestFloor(sector_t sec, int currentheight)
    {
        int height = currentheight;
        System.Span<int> heightlist = stackalloc int[MAX_ADJOINING_SECTORS + 2];
        int h = 0;

        for (int i = 0; i < sec.linecount; i++)
        {
            line_t check = sec.lines[i];
            sector_t? other = getNextSector(check, sec);

            if (other == null)
                continue;

            if (other.floorheight > height)
            {
                // Emulation of memory (stack) overflow
                if (h == MAX_ADJOINING_SECTORS + 1)
                {
                    height = other.floorheight;
                }
                else if (h == MAX_ADJOINING_SECTORS + 2)
                {
                    // Fatal overflow: game crashes at 22 sectors
                    throw new WadFormatException($"{sec}: more than 22 adjoining sectors (P_FindNextHighestFloor). Vanilla will crash here");
                }

                heightlist[h++] = other.floorheight;
            }
        }

        // Find lowest height in list
        if (h == 0)
            return currentheight;

        int min = heightlist[0];

        // Range checking?
        for (int i = 1; i < h; i++)
        {
            if (heightlist[i] < min)
                min = heightlist[i];
        }

        return min;
    }

    /// <summary>p_spec.c <c>P_FindLowestCeilingSurrounding</c>: FIND LOWEST CEILING IN THE SURROUNDING SECTORS (<c>MAXINT</c> with none).</summary>
    public static int P_FindLowestCeilingSurrounding(sector_t sec)
    {
        int height = int.MaxValue;

        for (int i = 0; i < sec.linecount; i++)
        {
            line_t check = sec.lines[i];
            sector_t? other = getNextSector(check, sec);

            if (other == null)
                continue;

            if (other.ceilingheight < height)
                height = other.ceilingheight;
        }
        return height;
    }

    /// <summary>p_spec.c <c>P_FindHighestCeilingSurrounding</c>: FIND HIGHEST CEILING IN THE SURROUNDING SECTORS (0 with none).</summary>
    public static int P_FindHighestCeilingSurrounding(sector_t sec)
    {
        int height = 0;

        for (int i = 0; i < sec.linecount; i++)
        {
            line_t check = sec.lines[i];
            sector_t? other = getNextSector(check, sec);

            if (other == null)
                continue;

            if (other.ceilingheight > height)
                height = other.ceilingheight;
        }
        return height;
    }

    /// <summary>
    /// p_spec.c <c>P_FindSectorFromLineTag</c>: RETURN NEXT SECTOR # THAT LINE
    /// TAG REFERS TO, the first sector after <paramref name="start"/> whose tag
    /// is the line's (start with -1), or -1.
    /// </summary>
    public int P_FindSectorFromLineTag(line_t line, int start)
    {
        for (int i = start + 1; i < sectors.Length; i++)
        {
            if (sectors[i].tag == line.tag)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// p_spec.c <c>P_FindMinSurroundingLight</c>: Find minimum light from an
    /// adjacent sector, at most <paramref name="max"/>.
    /// </summary>
    public static int P_FindMinSurroundingLight(sector_t sector, int max)
    {
        int min = max;
        for (int i = 0; i < sector.linecount; i++)
        {
            line_t line = sector.lines[i];
            sector_t? check = getNextSector(line, sector);

            if (check == null)
                continue;

            if (check.lightlevel < min)
                min = check.lightlevel;
        }
        return min;
    }
}
