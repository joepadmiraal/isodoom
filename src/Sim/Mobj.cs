using IsoDoom.Map;

namespace IsoDoom.Sim;

/// <summary>
/// p_mobj.h <c>mobj_t</c>: a map object (thing) in the world. Positions,
/// heights and momentum are fixed_t, <see cref="angle"/> is BAM. Vanilla's
/// <c>state_t* state</c> is the state's number here
/// (<c>Info.states[(int)state]</c>); <see cref="statenum_t.S_NULL"/> is what
/// vanilla's <c>(state_t*)S_NULL</c> stores on removal.
/// </summary>
public sealed class mobj_t : thinker_t
{
    // Info for drawing: position.
    public int x;
    public int y;
    public int z;

    // More list: links in sector (if needed).
    public mobj_t? snext;
    public mobj_t? sprev;

    // More drawing info: to determine current sprite.
    /// <summary>Orientation (BAM).</summary>
    public uint angle;
    /// <summary>Used to find patch_t and flip value.</summary>
    public spritenum_t sprite;
    /// <summary>Might be ORed with <see cref="Info.FF_FULLBRIGHT"/>.</summary>
    public int frame;

    // Interaction info, by BLOCKMAP. Links in blocks (if needed).
    public mobj_t? bnext;
    public mobj_t? bprev;

    public subsector_t subsector = null!;

    /// <summary>The closest interval over all contacted sectors.</summary>
    public int floorz;
    public int ceilingz;

    /// <summary>For movement checking.</summary>
    public int radius;
    public int height;

    /// <summary>Momentums, used to update position.</summary>
    public int momx;
    public int momy;
    public int momz;

    /// <summary>If == validcount, already checked.</summary>
    public int validcount;

    public mobjtype_t type;
    /// <summary>&amp;mobjinfo[mobj-&gt;type]</summary>
    public mobjinfo_t info = null!;

    /// <summary>State tic counter.</summary>
    public int tics;
    public statenum_t state;
    public mobjflag_t flags;
    public int health;

    /// <summary>Movement direction, movement generation (zig-zagging): 0-7.</summary>
    public int movedir;
    /// <summary>When 0, select a new dir.</summary>
    public int movecount;

    /// <summary>Thing being chased/attacked (or NULL); also the originator for missiles.</summary>
    public mobj_t? target;

    /// <summary>Reaction time: if non 0, don't attack yet. Used by player to freeze a bit after teleporting.</summary>
    public int reactiontime;

    /// <summary>If &gt;0, the target will be chased no matter what (even if shot).</summary>
    public int threshold;

    /// <summary>Additional info record for player avatars only. Only valid if type == MT_PLAYER.</summary>
    public player_t? player;

    /// <summary>Player number last looked for.</summary>
    public int lastlook;

    /// <summary>For nightmare respawn.</summary>
    public MapThing spawnpoint;

    /// <summary>Thing being chased/attacked for tracers.</summary>
    public mobj_t? tracer;

    public override string ToString() => $"{type} ({x >> Fixed.FRACBITS}, {y >> Fixed.FRACBITS}, {z >> Fixed.FRACBITS})";
}

/// <summary>
/// r_defs.h <c>sector_t</c>, the sim's view: the map's <see cref="Sector"/>
/// (whose heights, light and flats the sim changes in place, SPEC §12 T4.2)
/// plus the sim's own per-sector state.
/// </summary>
public sealed class sector_t
{
    internal sector_t(Sector map) => this.map = map;

    /// <summary>The level's sector, shared with the presentation.</summary>
    public readonly Sector map;

    public int Index => map.Index;

    /// <summary>fixed_t.</summary>
    public int floorheight { get => map.FloorHeight; set => map.FloorHeight = value; }

    /// <summary>fixed_t.</summary>
    public int ceilingheight { get => map.CeilingHeight; set => map.CeilingHeight = value; }

    public short lightlevel { get => map.LightLevel; set => map.LightLevel = value; }

    public short special { get => map.Special; set => map.Special = value; }

    public short tag { get => map.Tag; set => map.Tag = value; }

    /// <summary>List of mobjs in sector (through <see cref="mobj_t.snext"/>).</summary>
    public mobj_t? thinglist;

    public override string ToString() => map.ToString();
}

/// <summary>
/// r_defs.h <c>subsector_t</c>, the sim's view: the map's <see cref="Subsector"/>
/// with its <see cref="sector"/> as a <see cref="sector_t"/>.
/// </summary>
public sealed class subsector_t
{
    internal subsector_t(Subsector map, sector_t sector)
    {
        this.map = map;
        this.sector = sector;
    }

    public readonly Subsector map;
    public readonly sector_t sector;

    public int Index => map.Index;
    public int numlines => map.NumLines;
    public int firstline => map.FirstLine;

    public override string ToString() => map.ToString();
}
