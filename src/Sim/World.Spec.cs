using System;
using System.Collections.Generic;
using IsoDoom.Wad.Graphics;
using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

// p_spec.c: the sector and line lookup helpers the specials use (T5.1), the
// walk-over line triggers (P_CrossSpecialLine) and the level's specials
// (P_SpawnSpecials, P_UpdateSpecials; T5.2). The use triggers are in
// World.Switch.cs (p_switch.c), the effects not ported yet in World.Unported.cs.
// EV_DoDonut (p_spec.c's, T5.5) is here too.
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

    /// <summary>p_spec.c <c>MAXLINEANIMS</c>: the most scrolling walls (linedef special 48) a level may have.</summary>
    public const int MAXLINEANIMS = 64;

    /// <summary>p_spec.c <c>numlinespecials</c>: the scrolling walls in <see cref="linespeciallist"/>.</summary>
    public short numlinespecials;

    /// <summary>p_spec.c <c>linespeciallist</c>: the lines <see cref="P_UpdateSpecials"/> animates (special 48).</summary>
    public readonly line_t?[] linespeciallist = new line_t?[MAXLINEANIMS];

    /// <summary>p_spec.c <c>levelTimer</c>: deathmatch's <c>-timer</c> (never set: deathmatch is not ported).</summary>
    public bool levelTimer;

    /// <summary>p_spec.c <c>levelTimeCount</c>: the tics left with <see cref="levelTimer"/>.</summary>
    public int levelTimeCount;

    /// <summary>
    /// p_spec.c <c>P_CrossSpecialLine</c> - TRIGGER. Called every time a thing
    /// origin is about to cross a line with a non 0 special (by
    /// <see cref="P_TryMove"/>, for each line of <see cref="spechit"/> whose
    /// side it changed; <paramref name="side"/> is the side it came from).
    /// Monsters trigger only teleports, raise doors (4) and lifts (10, 88),
    /// missiles nothing. The W1 lines (TRIGGERS) clear their special after
    /// running it, whatever it did (the exits 52 and 124 do not); the WR
    /// lines (RETRIGGERS) keep it.
    /// </summary>
    public void P_CrossSpecialLine(int linenum, int side, mobj_t thing)
    {
        line_t line = lines[linenum];

        //	Triggers that other things can activate
        if (thing.player == null)
        {
            // Things that should NOT trigger specials...
            switch (thing.type)
            {
                case mobjtype_t.MT_ROCKET:
                case mobjtype_t.MT_PLASMA:
                case mobjtype_t.MT_BFG:
                case mobjtype_t.MT_TROOPSHOT:
                case mobjtype_t.MT_HEADSHOT:
                case mobjtype_t.MT_BRUISERSHOT:
                    return;

                default: break;
            }

            bool ok = false;
            switch (line.special)
            {
                case 39:  // TELEPORT TRIGGER
                case 97:  // TELEPORT RETRIGGER
                case 125: // TELEPORT MONSTERONLY TRIGGER
                case 126: // TELEPORT MONSTERONLY RETRIGGER
                case 4:   // RAISE DOOR
                case 10:  // PLAT DOWN-WAIT-UP-STAY TRIGGER
                case 88:  // PLAT DOWN-WAIT-UP-STAY RETRIGGER
                    ok = true;
                    break;
            }
            if (!ok)
                return;
        }

        // Note: could use some const's here.
        switch (line.special)
        {
            // TRIGGERS.
            // All from here to RETRIGGERS.
            case 2:
                // Open Door
                EV_DoDoor(line, vldoor_e.vld_open);
                line.special = 0;
                break;

            case 3:
                // Close Door
                EV_DoDoor(line, vldoor_e.vld_close);
                line.special = 0;
                break;

            case 4:
                // Raise Door
                EV_DoDoor(line, vldoor_e.vld_normal);
                line.special = 0;
                break;

            case 5:
                // Raise Floor
                EV_DoFloor(line, floor_e.raiseFloor);
                line.special = 0;
                break;

            case 6:
                // Fast Ceiling Crush & Raise
                EV_DoCeiling(line, ceiling_e.fastCrushAndRaise);
                line.special = 0;
                break;

            case 8:
                // Build Stairs
                EV_BuildStairs(line, stair_e.build8);
                line.special = 0;
                break;

            case 10:
                // PlatDownWaitUp
                EV_DoPlat(line, plattype_e.downWaitUpStay, 0);
                line.special = 0;
                break;

            case 12:
                // Light Turn On - brightest near
                EV_LightTurnOn(line, 0);
                line.special = 0;
                break;

            case 13:
                // Light Turn On 255
                EV_LightTurnOn(line, 255);
                line.special = 0;
                break;

            case 16:
                // Close Door 30
                EV_DoDoor(line, vldoor_e.vld_close30ThenOpen);
                line.special = 0;
                break;

            case 17:
                // Start Light Strobing
                EV_StartLightStrobing(line);
                line.special = 0;
                break;

            case 19:
                // Lower Floor
                EV_DoFloor(line, floor_e.lowerFloor);
                line.special = 0;
                break;

            case 22:
                // Raise floor to nearest height and change texture
                EV_DoPlat(line, plattype_e.raiseToNearestAndChange, 0);
                line.special = 0;
                break;

            case 25:
                // Ceiling Crush and Raise
                EV_DoCeiling(line, ceiling_e.crushAndRaise);
                line.special = 0;
                break;

            case 30:
                // Raise floor to shortest texture height
                //  on either side of lines.
                EV_DoFloor(line, floor_e.raiseToTexture);
                line.special = 0;
                break;

            case 35:
                // Lights Very Dark
                EV_LightTurnOn(line, 35);
                line.special = 0;
                break;

            case 36:
                // Lower Floor (TURBO)
                EV_DoFloor(line, floor_e.turboLower);
                line.special = 0;
                break;

            case 37:
                // LowerAndChange
                EV_DoFloor(line, floor_e.lowerAndChange);
                line.special = 0;
                break;

            case 38:
                // Lower Floor To Lowest
                EV_DoFloor(line, floor_e.lowerFloorToLowest);
                line.special = 0;
                break;

            case 39:
                // TELEPORT!
                EV_Teleport(line, side, thing);
                line.special = 0;
                break;

            case 40:
                // RaiseCeilingLowerFloor
                EV_DoCeiling(line, ceiling_e.raiseToHighest);
                EV_DoFloor(line, floor_e.lowerFloorToLowest);
                line.special = 0;
                break;

            case 44:
                // Ceiling Crush
                EV_DoCeiling(line, ceiling_e.lowerAndCrush);
                line.special = 0;
                break;

            case 52:
                // EXIT!
                G_ExitLevel();
                break;

            case 53:
                // Perpetual Platform Raise
                EV_DoPlat(line, plattype_e.perpetualRaise, 0);
                line.special = 0;
                break;

            case 54:
                // Platform Stop
                EV_StopPlat(line);
                line.special = 0;
                break;

            case 56:
                // Raise Floor Crush
                EV_DoFloor(line, floor_e.raiseFloorCrush);
                line.special = 0;
                break;

            case 57:
                // Ceiling Crush Stop
                EV_CeilingCrushStop(line);
                line.special = 0;
                break;

            case 58:
                // Raise Floor 24
                EV_DoFloor(line, floor_e.raiseFloor24);
                line.special = 0;
                break;

            case 59:
                // Raise Floor 24 And Change
                EV_DoFloor(line, floor_e.raiseFloor24AndChange);
                line.special = 0;
                break;

            case 104:
                // Turn lights off in sector(tag)
                EV_TurnTagLightsOff(line);
                line.special = 0;
                break;

            case 108:
                // Blazing Door Raise (faster than TURBO!)
                EV_DoDoor(line, vldoor_e.vld_blazeRaise);
                line.special = 0;
                break;

            case 109:
                // Blazing Door Open (faster than TURBO!)
                EV_DoDoor(line, vldoor_e.vld_blazeOpen);
                line.special = 0;
                break;

            case 100:
                // Build Stairs Turbo 16
                EV_BuildStairs(line, stair_e.turbo16);
                line.special = 0;
                break;

            case 110:
                // Blazing Door Close (faster than TURBO!)
                EV_DoDoor(line, vldoor_e.vld_blazeClose);
                line.special = 0;
                break;

            case 119:
                // Raise floor to nearest surr. floor
                EV_DoFloor(line, floor_e.raiseFloorToNearest);
                line.special = 0;
                break;

            case 121:
                // Blazing PlatDownWaitUpStay
                EV_DoPlat(line, plattype_e.blazeDWUS, 0);
                line.special = 0;
                break;

            case 124:
                // Secret EXIT
                G_SecretExitLevel();
                break;

            case 125:
                // TELEPORT MonsterONLY
                if (thing.player == null)
                {
                    EV_Teleport(line, side, thing);
                    line.special = 0;
                }
                break;

            case 130:
                // Raise Floor Turbo
                EV_DoFloor(line, floor_e.raiseFloorTurbo);
                line.special = 0;
                break;

            case 141:
                // Silent Ceiling Crush & Raise
                EV_DoCeiling(line, ceiling_e.silentCrushAndRaise);
                line.special = 0;
                break;

            // RETRIGGERS.  All from here till end.
            case 72:
                // Ceiling Crush
                EV_DoCeiling(line, ceiling_e.lowerAndCrush);
                break;

            case 73:
                // Ceiling Crush and Raise
                EV_DoCeiling(line, ceiling_e.crushAndRaise);
                break;

            case 74:
                // Ceiling Crush Stop
                EV_CeilingCrushStop(line);
                break;

            case 75:
                // Close Door
                EV_DoDoor(line, vldoor_e.vld_close);
                break;

            case 76:
                // Close Door 30
                EV_DoDoor(line, vldoor_e.vld_close30ThenOpen);
                break;

            case 77:
                // Fast Ceiling Crush & Raise
                EV_DoCeiling(line, ceiling_e.fastCrushAndRaise);
                break;

            case 79:
                // Lights Very Dark
                EV_LightTurnOn(line, 35);
                break;

            case 80:
                // Light Turn On - brightest near
                EV_LightTurnOn(line, 0);
                break;

            case 81:
                // Light Turn On 255
                EV_LightTurnOn(line, 255);
                break;

            case 82:
                // Lower Floor To Lowest
                EV_DoFloor(line, floor_e.lowerFloorToLowest);
                break;

            case 83:
                // Lower Floor
                EV_DoFloor(line, floor_e.lowerFloor);
                break;

            case 84:
                // LowerAndChange
                EV_DoFloor(line, floor_e.lowerAndChange);
                break;

            case 86:
                // Open Door
                EV_DoDoor(line, vldoor_e.vld_open);
                break;

            case 87:
                // Perpetual Platform Raise
                EV_DoPlat(line, plattype_e.perpetualRaise, 0);
                break;

            case 88:
                // PlatDownWaitUp
                EV_DoPlat(line, plattype_e.downWaitUpStay, 0);
                break;

            case 89:
                // Platform Stop
                EV_StopPlat(line);
                break;

            case 90:
                // Raise Door
                EV_DoDoor(line, vldoor_e.vld_normal);
                break;

            case 91:
                // Raise Floor
                EV_DoFloor(line, floor_e.raiseFloor);
                break;

            case 92:
                // Raise Floor 24
                EV_DoFloor(line, floor_e.raiseFloor24);
                break;

            case 93:
                // Raise Floor 24 And Change
                EV_DoFloor(line, floor_e.raiseFloor24AndChange);
                break;

            case 94:
                // Raise Floor Crush
                EV_DoFloor(line, floor_e.raiseFloorCrush);
                break;

            case 95:
                // Raise floor to nearest height
                // and change texture.
                EV_DoPlat(line, plattype_e.raiseToNearestAndChange, 0);
                break;

            case 96:
                // Raise floor to shortest texture height
                // on either side of lines.
                EV_DoFloor(line, floor_e.raiseToTexture);
                break;

            case 97:
                // TELEPORT!
                EV_Teleport(line, side, thing);
                break;

            case 98:
                // Lower Floor (TURBO)
                EV_DoFloor(line, floor_e.turboLower);
                break;

            case 105:
                // Blazing Door Raise (faster than TURBO!)
                EV_DoDoor(line, vldoor_e.vld_blazeRaise);
                break;

            case 106:
                // Blazing Door Open (faster than TURBO!)
                EV_DoDoor(line, vldoor_e.vld_blazeOpen);
                break;

            case 107:
                // Blazing Door Close (faster than TURBO!)
                EV_DoDoor(line, vldoor_e.vld_blazeClose);
                break;

            case 120:
                // Blazing PlatDownWaitUpStay.
                EV_DoPlat(line, plattype_e.blazeDWUS, 0);
                break;

            case 126:
                // TELEPORT MonsterONLY.
                if (thing.player == null)
                    EV_Teleport(line, side, thing);
                break;

            case 128:
                // Raise To Nearest Floor
                EV_DoFloor(line, floor_e.raiseFloorToNearest);
                break;

            case 129:
                // Raise Floor Turbo
                EV_DoFloor(line, floor_e.raiseFloorTurbo);
                break;
        }
    }

    /// <summary>
    /// p_spec.c <c>P_ShootSpecialLine</c>: a hitscan by <paramref name="thing"/>
    /// crossed <paramref name="line"/> (T6.3, from <see cref="PTR_ShootTraverse"/>):
    /// the gun lines G1 24 (raise floor), GR 46 (open door, the only one a
    /// monster triggers) and G1 47 (raise to the nearest floor and change),
    /// each switching its texture. From either side, whatever the result.
    /// </summary>
    public void P_ShootSpecialLine(mobj_t thing, line_t line)
    {
        //	Impacts that other things can activate.
        if (thing.player == null)
        {
            bool ok = false;
            switch (line.special)
            {
                case 46:
                    // OPEN DOOR IMPACT
                    ok = true;
                    break;
            }
            if (!ok)
                return;
        }

        switch (line.special)
        {
            case 24:
                // RAISE FLOOR
                EV_DoFloor(line, floor_e.raiseFloor);
                P_ChangeSwitchTexture(line, 0);
                break;

            case 46:
                // OPEN DOOR
                EV_DoDoor(line, vldoor_e.vld_open);
                P_ChangeSwitchTexture(line, 1);
                break;

            case 47:
                // RAISE FLOOR NEAR AND CHANGE
                EV_DoPlat(line, plattype_e.raiseToNearestAndChange, 0);
                P_ChangeSwitchTexture(line, 0);
                break;
        }
    }

    /// <summary>p_spec.c <c>anims</c>: the animations <see cref="P_InitPicAnims"/> found, up to <see cref="lastanim"/>.</summary>
    public readonly anim_t?[] anims = new anim_t?[PicAnims.MAXANIMS];

    /// <summary>p_spec.c <c>lastanim</c> (an index here): the number of <see cref="anims"/>.</summary>
    public int lastanim;

    /// <summary>
    /// r_data.c <c>texturetranslation</c>: the texture drawn for each texture
    /// number (identity but for the animations' frames, which
    /// <see cref="P_UpdateSpecials"/> rotates). Render state the sim never
    /// reads; the presentation re-points the frames' atlas slots by it (T5.7).
    /// </summary>
    public int[] texturetranslation = Array.Empty<int>();

    /// <summary>r_data.c <c>flattranslation</c>: as <see cref="texturetranslation"/> for flat numbers (<see cref="flatnames"/>).</summary>
    public int[] flattranslation = Array.Empty<int>();

    /// <summary>The flat namespace by name, in order: what <see cref="flattranslation"/>'s numbers are (vanilla's <c>firstflat</c> + n).</summary>
    public IReadOnlyList<string> flatnames { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// p_spec.c <c>P_InitPicAnims</c> (vanilla's <c>P_Init</c>, after
    /// <c>R_InitData</c> made the translations identity): the animations of
    /// <see cref="PicAnims.animdefs"/> whose start the WAD has
    /// (<see cref="PicAnims.Resolve"/>: <paramref name="textures"/>, null for
    /// none; <paramref name="flats"/>, the flat namespace in order). Not run
    /// by the constructor, which has no WAD; without it nothing animates.
    /// </summary>
    public void P_InitPicAnims(Textures? textures, IReadOnlyList<string> flats)
    {
        texturetranslation = new int[(textures?.NumTextures ?? 0) + 1];
        for (int i = 0; i < texturetranslation.Length; i++)
            texturetranslation[i] = i;
        flatnames = flats;
        flattranslation = new int[flats.Count + 1];
        for (int i = 0; i < flattranslation.Length; i++)
            flattranslation[i] = i;

        //	Init animation
        Array.Clear(anims);
        lastanim = 0;
        foreach (anim_t anim in PicAnims.Resolve(textures, flats))
            anims[lastanim++] = anim;
    }

    /// <summary>
    /// p_spec.c <c>P_UpdateSpecials</c>: animate planes, scroll walls, etc.,
    /// once a tic after the thinkers (<see cref="P_Ticker"/>): the level
    /// timer (deathmatch only), the animated textures and flats
    /// (<see cref="texturetranslation"/>, <see cref="flattranslation"/>, on
    /// <see cref="leveltime"/>; T5.7), the scrolling walls (special 48: the
    /// front side's texture offset, one unit a tic), then the button timers
    /// (T5.4, <see cref="P_UpdateButtons"/>).
    /// </summary>
    public void P_UpdateSpecials()
    {
        //	LEVEL TIMER
        if (levelTimer)
        {
            levelTimeCount--;
            if (levelTimeCount == 0)
                G_ExitLevel();
        }

        //	ANIMATE FLATS AND TEXTURES GLOBALLY
        for (int a = 0; a < lastanim; a++)
        {
            anim_t anim = anims[a]!;
            for (int i = anim.basepic; i < anim.basepic + anim.numpics; i++)
            {
                int pic = anim.basepic + ((leveltime / anim.speed + i) % anim.numpics);
                if (anim.istexture)
                    texturetranslation[i] = pic;
                else
                    flattranslation[i] = pic;
            }
        }

        //	ANIMATE LINE SPECIALS
        for (int i = 0; i < numlinespecials; i++)
        {
            line_t line = linespeciallist[i]!;
            switch (line.special)
            {
                case 48:
                    // EFFECT FIRSTCOL SCROLL +
                    sides[line.sidenum[0]].textureoffset += Fixed.FRACUNIT;
                    break;
            }
        }

        //	DO BUTTONS
        P_UpdateButtons();
    }

    /// <summary>
    /// p_spec.c <c>P_PlayerInSpecialSector</c>: called every tic frame that
    /// the player origin is in a special sector, once it stands on the floor:
    /// the damage floors (5: 10, 7: 5, 16 and 4: 20, every 32 tics on
    /// <see cref="leveltime"/>; the radiation suit protects, but from 16 and
    /// 4 only when its <c>P_Random</c> roll is 5 or more), a secret (9:
    /// counted once, then the special is cleared) and E1M8's exit floor (11:
    /// god mode off, 20 damage that never kills, the level ends at 10 health
    /// or less). Any other special is vanilla's <c>I_Error</c>.
    /// </summary>
    public void P_PlayerInSpecialSector(player_t player)
    {
        sector_t sector = player.mo!.subsector.sector;

        // Falling, not all the way down yet?
        if (player.mo.z != sector.floorheight)
            return;

        // Has hitten ground.
        switch (sector.special)
        {
            case 5:
                // HELLSLIME DAMAGE
                if (player.powers[(int)powertype_t.pw_ironfeet] == 0)
                {
                    if ((leveltime & 0x1f) == 0)
                        P_DamageMobj(player.mo, null, null, 10);
                }
                break;

            case 7:
                // NUKAGE DAMAGE
                if (player.powers[(int)powertype_t.pw_ironfeet] == 0)
                {
                    if ((leveltime & 0x1f) == 0)
                        P_DamageMobj(player.mo, null, null, 5);
                }
                break;

            case 16:
            // SUPER HELLSLIME DAMAGE
            case 4:
                // STROBE HURT
                if (player.powers[(int)powertype_t.pw_ironfeet] == 0 || P_Random() < 5)
                {
                    if ((leveltime & 0x1f) == 0)
                        P_DamageMobj(player.mo, null, null, 20);
                }
                break;

            case 9:
                // SECRET SECTOR
                player.secretcount++;
                sector.special = 0;
                break;

            case 11:
                // EXIT SUPER DAMAGE! (for E1M8 finale)
                player.cheats &= ~player_t.CF_GODMODE;

                if ((leveltime & 0x1f) == 0)
                    P_DamageMobj(player.mo, null, null, 20);

                if (player.health <= 10)
                    G_ExitLevel();
                break;

            default:
                throw new System.InvalidOperationException($"P_PlayerInSpecialSector: unknown special {sector.special}");
        }
    }

    /// <summary>
    /// p_spec.c <c>P_SpawnSpecials</c>: after the map has been loaded, scan
    /// for specials that spawn thinkers (the end of <see cref="P_SetupLevel"/>).
    /// Sector specials: the lights (T5.7, World.Lights.cs), the timed doors
    /// (T5.3); secrets (9) count into
    /// <see cref="totalsecret"/>. Line effects: the scrolling walls (48),
    /// at most <see cref="MAXLINEANIMS"/> as Chocolate Doom (vanilla
    /// overruns). The active ceilings and lifts (T5.5) and the buttons (T5.4)
    /// are cleared.
    /// </summary>
    public void P_SpawnSpecials()
    {
        // See if -TIMER was specified (deathmatch only, not ported).
        levelTimer = false;

        //	Init special SECTORs.
        for (int i = 0; i < sectors.Length; i++)
        {
            sector_t sector = sectors[i];
            if (sector.special == 0)
                continue;

            switch (sector.special)
            {
                case 1:
                    // FLICKERING LIGHTS
                    P_SpawnLightFlash(sector);
                    break;

                case 2:
                    // STROBE FAST
                    P_SpawnStrobeFlash(sector, LightFlash.FASTDARK, 0);
                    break;

                case 3:
                    // STROBE SLOW
                    P_SpawnStrobeFlash(sector, LightFlash.SLOWDARK, 0);
                    break;

                case 4:
                    // STROBE FAST/DEATH SLIME
                    P_SpawnStrobeFlash(sector, LightFlash.FASTDARK, 0);
                    sector.special = 4;
                    break;

                case 8:
                    // GLOWING LIGHT
                    P_SpawnGlowingLight(sector);
                    break;

                case 9:
                    // SECRET SECTOR
                    totalsecret++;
                    break;

                case 10:
                    // DOOR CLOSE IN 30 SECONDS
                    P_SpawnDoorCloseIn30(sector);
                    break;

                case 12:
                    // SYNC STROBE SLOW
                    P_SpawnStrobeFlash(sector, LightFlash.SLOWDARK, 1);
                    break;

                case 13:
                    // SYNC STROBE FAST
                    P_SpawnStrobeFlash(sector, LightFlash.FASTDARK, 1);
                    break;

                case 14:
                    // DOOR RAISE IN 5 MINUTES
                    P_SpawnDoorRaiseIn5Mins(sector, i);
                    break;

                case 17:
                    P_SpawnFireFlicker(sector);
                    break;
            }
        }

        //	Init line EFFECTs
        numlinespecials = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            switch (lines[i].special)
            {
                case 48:
                    if (numlinespecials >= MAXLINEANIMS)
                        throw new WadFormatException("Too many scrolling wall linedefs! (Vanilla limit is 64)");
                    // EFFECT FIRSTCOL SCROLL+
                    linespeciallist[numlinespecials] = lines[i];
                    numlinespecials++;
                    break;
            }
        }

        //	Init other misc stuff
        for (int i = 0; i < CeilingMove.MAXCEILINGS; i++)
            activeceilings[i] = null;

        for (int i = 0; i < Plat.MAXPLATS; i++)
            activeplats[i] = null;

        for (int i = 0; i < MAXBUTTONS; i++)
            buttonlist[i].Clear();
    }

    /// <summary>
    /// p_spec.c <c>EV_DoDonut</c>: Special Stuff that can not be categorized.
    /// For each tagged sector s1 (the hole) not already moving, s2 is the
    /// sector across s1's first line (the ring); for the first line of s2
    /// whose back sector s3 is not s1, the ring rises to s3's floor and takes
    /// its flat (special 0), and the hole lowers to it, both at half
    /// <see cref="FloorMove.FLOORSPEED"/>. As Chocolate Doom: a one-sided
    /// first line ends the search (vanilla reads invalid memory), and a
    /// one-sided ring line before the outer one is its overrun emulation
    /// (height 0; the flat stays, SPEC §12 T5.5).
    /// </summary>
    public int EV_DoDonut(line_t line)
    {
        int secnum = -1;
        int rtn = 0;

        while ((secnum = P_FindSectorFromLineTag(line, secnum)) >= 0)
        {
            sector_t s1 = sectors[secnum];

            // ALREADY MOVING?  IF SO, KEEP GOING...
            if (s1.specialdata != null)
                continue;

            rtn = 1;
            sector_t? s2 = getNextSector(s1.lines[0], s1);

            // Vanilla Doom does not check if the linedef is one sided.  The
            // game does not crash, but reads invalid memory and causes the
            // sector floor to move "down" to some unknown height.
            // (Chocolate Doom warns and returns.)
            if (s2 == null)
                break;

            for (int i = 0; i < s2.linecount; i++)
            {
                sector_t? s3 = s2.lines[i].backsector;

                if (s3 == s1)
                    continue;

                int s3_floorheight;
                string? s3_floorpic;
                if (s3 == null)
                {
                    // e6y: s3 is NULL, so s3->floorheight is an int at
                    // 0000:0000 and s3->floorpic a short at 0000:0008
                    // (Chocolate Doom's DonutOverrun: 0 and flat 0x16 by
                    // default, as under Windows 98). The sim keeps flat
                    // names, not numbers: the ring keeps its flat.
                    s3_floorheight = DONUT_FLOORHEIGHT_DEFAULT;
                    s3_floorpic = null;
                }
                else
                {
                    s3_floorheight = s3.floorheight;
                    s3_floorpic = s3.floorpic;
                }

                //	Spawn rising slime
                var floor = new floormove_t();
                P_AddThinker(floor);
                s2.specialdata = floor;
                floor.function = think_t.T_MoveFloor;
                floor.type = floor_e.donutRaise;
                floor.crush = false;
                floor.direction = 1;
                floor.sector = s2;
                floor.speed = FloorMove.FLOORSPEED / 2;
                floor.texture = s3_floorpic;
                floor.newspecial = 0;
                floor.floordestheight = s3_floorheight;

                //	Spawn lowering donut-hole
                floor = new floormove_t();
                P_AddThinker(floor);
                s1.specialdata = floor;
                floor.function = think_t.T_MoveFloor;
                floor.type = floor_e.lowerFloor;
                floor.crush = false;
                floor.direction = -1;
                floor.sector = s1;
                floor.speed = FloorMove.FLOORSPEED / 2;
                floor.floordestheight = s3_floorheight;
                break;
            }
        }
        return rtn;
    }

    /// <summary>Chocolate Doom's <c>DONUT_FLOORHEIGHT_DEFAULT</c> (p_spec.c): the overrun's floor height.</summary>
    public const int DONUT_FLOORHEIGHT_DEFAULT = 0x00000000;
}
