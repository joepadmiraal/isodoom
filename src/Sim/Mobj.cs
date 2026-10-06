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

    // ---- Interpolation (not vanilla: presentation state, as source ports'
    // oldx/oldy/oldz/oldangle and interp, e.g. Crispy Doom's p_mobj.h; SPEC §12 T4.7).
    // Not in the checksum and never read by the sim.

    /// <summary>Position (fixed_t) and facing at the start of the last tic (<see cref="World.P_StoreInterpolation"/>).</summary>
    public int oldx, oldy, oldz;

    /// <inheritdoc cref="oldx"/>
    public uint oldangle;

    /// <summary>
    /// Whether the presentation may interpolate from <see cref="oldx"/>… to
    /// the current position: false for a mobj spawned during the last tic
    /// (the default) and after a teleport (T5.6) or a debug move
    /// (<see cref="World.PlaceMobj"/>), which then draw where they are.
    /// </summary>
    public bool interp;

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

    /// <summary>The floor flat's name (vanilla's flat number <c>floorpic</c>).</summary>
    public string floorpic { get => map.FloorPic; set => map.FloorPic = value; }

    /// <summary>The ceiling flat's name (vanilla's flat number <c>ceilingpic</c>); <see cref="World.SKYFLATNAME"/> is the sky.</summary>
    public string ceilingpic { get => map.CeilingPic; set => map.CeilingPic = value; }

    public short special { get => map.Special; set => map.Special = value; }

    public short tag { get => map.Tag; set => map.Tag = value; }

    /// <summary>List of mobjs in sector (through <see cref="mobj_t.snext"/>).</summary>
    public mobj_t? thinglist;

    /// <summary>
    /// thinker_t for reversable actions: the door, floor, lift or ceiling
    /// moving the sector (vanilla's <c>void*</c>), null when idle (T5.3).
    /// </summary>
    public thinker_t? specialdata;

    /// <summary>
    /// The lines with this sector on either side (<c>lines</c>, <c>linecount</c>
    /// is its length), in line order, as <c>P_GroupLines</c> lists them (the
    /// map's <see cref="Sector.Lines"/>).
    /// </summary>
    public line_t[] lines = System.Array.Empty<line_t>();

    /// <summary><c>linecount</c>.</summary>
    public int linecount => lines.Length;

    /// <summary>Mapblock bounding box for height changes (<c>blockbox</c>, indexed by <see cref="BBox"/>).</summary>
    public int[] blockbox => map.BlockBox;

    // ---- Interpolation (not vanilla: presentation state, as source ports'
    // interpolated sector planes; SPEC §12 T4.7, T5.1). Not in the checksum
    // and never read by the sim.

    /// <summary>Floor and ceiling heights (fixed_t) at the start of the last tic (<see cref="World.P_StoreInterpolation"/>).</summary>
    public int oldfloorheight, oldceilingheight;

    /// <summary>
    /// Not vanilla: remembers the current heights as the start of the tic, so
    /// the presentation draws them as they are until the sim moves them
    /// (<see cref="World.P_StoreInterpolation"/>, and after a debug move).
    /// </summary>
    public void StoreInterpolation()
    {
        oldfloorheight = floorheight;
        oldceilingheight = ceilingheight;
    }

    public override string ToString() => map.ToString();
}

/// <summary>
/// r_defs.h <c>side_t</c>, the sim's view: the map's <see cref="Side"/>
/// (whose texture names and offsets the sim changes in place: switches
/// T5.4, scrolling walls T5.7; the presentation reads them, SPEC §12 T5.1)
/// with its <see cref="sector"/> as a <see cref="sector_t"/>. Textures are
/// names, as the map's flats (vanilla's texture numbers; <c>-</c> is none).
/// </summary>
public sealed class side_t
{
    internal side_t(Side map, sector_t sector)
    {
        this.map = map;
        this.sector = sector;
    }

    /// <summary>The level's sidedef, shared with the presentation.</summary>
    public readonly Side map;

    public int Index => map.Index;

    /// <summary>fixed_t: add this to the calculated texture column.</summary>
    public int textureoffset { get => map.TextureOffset; set => map.TextureOffset = value; }

    /// <summary>fixed_t: add this to the calculated texture top.</summary>
    public int rowoffset { get => map.RowOffset; set => map.RowOffset = value; }

    /// <summary>Texture name (vanilla's texture number); <c>-</c> is none.</summary>
    public string toptexture { get => map.TopTexture; set => map.TopTexture = value; }

    public string bottomtexture { get => map.BottomTexture; set => map.BottomTexture = value; }

    public string midtexture { get => map.MidTexture; set => map.MidTexture = value; }

    /// <summary>Sector the SideDef is facing.</summary>
    public readonly sector_t sector;

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

/// <summary>
/// r_defs.h <c>line_t</c>, the sim's view: the map's <see cref="Line"/>
/// (whose flags, special and tag the sim changes in place) with its sectors
/// as <see cref="sector_t"/>, plus the sim's <see cref="validcount"/>.
/// Vertices are the map's <see cref="Vertex"/> (<c>v1.X</c> for vanilla's
/// <c>v1-&gt;x</c>).
/// </summary>
public sealed class line_t
{
    internal line_t(Line map, sector_t? frontsector, sector_t? backsector)
    {
        this.map = map;
        this.frontsector = frontsector;
        this.backsector = backsector;
    }

    /// <summary>The level's line, shared with the presentation.</summary>
    public readonly Line map;

    public int Index => map.Index;

    public Vertex v1 => map.V1;
    public Vertex v2 => map.V2;

    /// <summary>fixed_t, <c>v2-&gt;x - v1-&gt;x</c>.</summary>
    public int dx => map.Dx;

    /// <summary>fixed_t, <c>v2-&gt;y - v1-&gt;y</c>.</summary>
    public int dy => map.Dy;

    /// <summary>The <c>ML_*</c> flags (<see cref="Line.ML_BLOCKING"/>…).</summary>
    public short flags { get => map.Flags; set => map.Flags = value; }

    public short special { get => map.Special; set => map.Special = value; }

    public short tag { get => map.Tag; set => map.Tag = value; }

    /// <summary>Front and back sidedef numbers, -1 for none.</summary>
    public int[] sidenum => map.SideNum;

    /// <summary>fixed_t bounding box, indexed by <see cref="BBox"/>.</summary>
    public int[] bbox => map.BBox;

    public SlopeType slopetype => map.SlopeType;

    /// <summary>The front side's sector, null without a front side.</summary>
    public readonly sector_t? frontsector;

    /// <summary>The back side's sector, null without a back side (one-sided).</summary>
    public readonly sector_t? backsector;

    /// <summary>If == <see cref="World.validcount"/>, already checked.</summary>
    public int validcount;

    public override string ToString() => map.ToString();
}
