using System;
using System.Collections.Generic;

namespace IsoDoom.Map;

// The level structures of r_defs.h as p_setup.c fills them. Coordinates,
// heights and offsets are fixed_t (16.16, see Fixed); angles are BAM
// (angle_t, a full turn is 2^32). Vanilla's pointers between the structures
// are object references here, and every structure also knows its index.
// Texture and flat names stay names: resolving them to texture/flat numbers
// (R_TextureNumForName, R_FlatNumForName) is left to the renderer and the sim.

/// <summary>A map thing as stored in <c>THINGS</c> (doomdata.h <c>mapthing_t</c>): raw map units and degrees.</summary>
public readonly record struct MapThing(short X, short Y, short Angle, short Type, short Options)
{
    /// <summary>doomdata.h <c>MTF_EASY</c>: present on skills 1 and 2.</summary>
    public const short MTF_EASY = 1;

    /// <summary>doomdata.h <c>MTF_NORMAL</c>: present on skill 3.</summary>
    public const short MTF_NORMAL = 2;

    /// <summary>doomdata.h <c>MTF_HARD</c>: present on skills 4 and 5.</summary>
    public const short MTF_HARD = 4;

    /// <summary>doomdata.h <c>MTF_AMBUSH</c>: deaf monster.</summary>
    public const short MTF_AMBUSH = 8;

    // p_mobj.c P_SpawnMapThing also skips things with options bit 16 outside
    // netgames ("not in single player"); vanilla has no name for it.
}

/// <summary>r_defs.h <c>vertex_t</c>.</summary>
public sealed class Vertex
{
    internal Vertex(int index, int x, int y)
    {
        Index = index;
        X = x;
        Y = y;
    }

    public int Index { get; }

    /// <summary>fixed_t.</summary>
    public int X { get; }

    /// <summary>fixed_t.</summary>
    public int Y { get; }

    public override string ToString() => $"vertex {Index} ({X >> Fixed.FRACBITS}, {Y >> Fixed.FRACBITS})";
}

/// <summary>r_defs.h <c>sector_t</c> (the map data part; the sim adds thing lists, sound targets and specials later).</summary>
public sealed class Sector
{
    internal Sector(int index) => Index = index;

    public int Index { get; }

    /// <summary>fixed_t; moved by the sim (doors, lifts, crushers).</summary>
    public int FloorHeight { get; set; }

    /// <summary>fixed_t; moved by the sim.</summary>
    public int CeilingHeight { get; set; }

    /// <summary>Flat name (<c>floorpic</c>), upper case; changed by some specials.</summary>
    public string FloorPic { get; set; } = "";

    /// <summary>Flat name (<c>ceilingpic</c>), upper case; <c>F_SKY1</c> is the sky.</summary>
    public string CeilingPic { get; set; } = "";

    public short LightLevel { get; set; }

    public short Special { get; set; }

    public short Tag { get; set; }

    /// <summary>
    /// The lines with this sector on either side (<c>lines</c>/<c>linecount</c>,
    /// built by <c>P_GroupLines</c>), in line order.
    /// </summary>
    public IReadOnlyList<Line> Lines { get; internal set; } = Array.Empty<Line>();

    /// <summary>
    /// The sector's line bounding box in blockmap blocks, widened by
    /// <c>MAXRADIUS</c> and clamped to the blockmap (<c>blockbox</c>, indexed by <see cref="BBox"/>).
    /// </summary>
    public int[] BlockBox { get; } = new int[4];

    /// <summary>fixed_t; centre of the line bounding box (<c>soundorg.x</c>), where sector sounds come from.</summary>
    public int SoundOrgX { get; internal set; }

    /// <summary>fixed_t (<c>soundorg.y</c>).</summary>
    public int SoundOrgY { get; internal set; }

    public override string ToString() => $"sector {Index}";
}

/// <summary>r_defs.h <c>side_t</c> (a sidedef).</summary>
public sealed class Side
{
    internal Side(int index, Sector sector)
    {
        Index = index;
        Sector = sector;
    }

    public int Index { get; }

    /// <summary>fixed_t, horizontal (<c>textureoffset</c>); changed by scrolling walls.</summary>
    public int TextureOffset { get; set; }

    /// <summary>fixed_t, vertical (<c>rowoffset</c>).</summary>
    public int RowOffset { get; set; }

    /// <summary>Upper texture name (<c>toptexture</c>), upper case; <c>-</c> is none. Changed by switches.</summary>
    public string TopTexture { get; set; } = "-";

    /// <summary>Lower texture name (<c>bottomtexture</c>).</summary>
    public string BottomTexture { get; set; } = "-";

    /// <summary>Middle texture name (<c>midtexture</c>).</summary>
    public string MidTexture { get; set; } = "-";

    public Sector Sector { get; }

    public override string ToString() => $"side {Index}";
}

/// <summary>r_defs.h <c>slopetype_t</c>: the direction class of a line, for quick side tests.</summary>
public enum SlopeType
{
    ST_HORIZONTAL,
    ST_VERTICAL,
    ST_POSITIVE,
    ST_NEGATIVE,
}

/// <summary>r_defs.h <c>line_t</c> (a linedef).</summary>
public sealed class Line
{
    /// <summary>doomdata.h <c>ML_BLOCKING</c>: solid, blocks everything.</summary>
    public const short ML_BLOCKING = 1;

    /// <summary>doomdata.h <c>ML_BLOCKMONSTERS</c>.</summary>
    public const short ML_BLOCKMONSTERS = 2;

    /// <summary>doomdata.h <c>ML_TWOSIDED</c>: has a back side (and may be see-through).</summary>
    public const short ML_TWOSIDED = 4;

    /// <summary>doomdata.h <c>ML_DONTPEGTOP</c>: upper texture unpegged.</summary>
    public const short ML_DONTPEGTOP = 8;

    /// <summary>doomdata.h <c>ML_DONTPEGBOTTOM</c>: lower texture unpegged.</summary>
    public const short ML_DONTPEGBOTTOM = 16;

    /// <summary>doomdata.h <c>ML_SECRET</c>: drawn as one-sided on the automap.</summary>
    public const short ML_SECRET = 32;

    /// <summary>doomdata.h <c>ML_SOUNDBLOCK</c>: blocks sound propagation (needs two).</summary>
    public const short ML_SOUNDBLOCK = 64;

    /// <summary>doomdata.h <c>ML_DONTDRAW</c>: never drawn on the automap.</summary>
    public const short ML_DONTDRAW = 128;

    /// <summary>doomdata.h <c>ML_MAPPED</c>: already seen, drawn on the automap.</summary>
    public const short ML_MAPPED = 256;

    internal Line(int index, Vertex v1, Vertex v2)
    {
        Index = index;
        V1 = v1;
        V2 = v2;
    }

    public int Index { get; }

    public Vertex V1 { get; }

    public Vertex V2 { get; }

    /// <summary>fixed_t, <c>V2.X - V1.X</c>.</summary>
    public int Dx { get; internal set; }

    /// <summary>fixed_t, <c>V2.Y - V1.Y</c>.</summary>
    public int Dy { get; internal set; }

    /// <summary>The <c>ML_*</c> flags; the sim sets <see cref="ML_MAPPED"/>.</summary>
    public short Flags { get; set; }

    /// <summary>Line special; the sim clears it when a one-shot special fires.</summary>
    public short Special { get; set; }

    public short Tag { get; set; }

    /// <summary>
    /// <c>sidenum[2]</c>: the front (right) and back (left) sidedef numbers, -1
    /// for none. Read as unsigned 16-bit numbers with <c>0xFFFF</c> as none.
    /// </summary>
    public int[] SideNum { get; } = new int[2];

    /// <summary>fixed_t bounding box (<c>bbox</c>, indexed by <see cref="BBox"/>).</summary>
    public int[] BBox { get; } = new int[4];

    public SlopeType SlopeType { get; internal set; }

    /// <summary>The front side's sector (<c>frontsector</c>), null without a front side.</summary>
    public Sector? FrontSector { get; internal set; }

    /// <summary>The back side's sector (<c>backsector</c>), null without a back side.</summary>
    public Sector? BackSector { get; internal set; }

    public override string ToString() => $"line {Index}";
}

/// <summary>
/// r_defs.h <c>seg_t</c>: the part of a linedef side that lies in one
/// subsector, as the node builder cut it.
/// </summary>
public sealed class Seg
{
    internal Seg(int index, Vertex v1, Vertex v2, Line linedef, int side, Side sidedef)
    {
        Index = index;
        V1 = v1;
        V2 = v2;
        LineDef = linedef;
        Side = side;
        SideDef = sidedef;
    }

    public int Index { get; }

    public Vertex V1 { get; }

    public Vertex V2 { get; }

    /// <summary>fixed_t: distance along the linedef side from its start to <see cref="V1"/> (<c>offset</c>).</summary>
    public int Offset { get; internal set; }

    /// <summary>BAM direction from <see cref="V1"/> to <see cref="V2"/> (<c>angle</c>: the lump's 16 bits shifted left 16).</summary>
    public uint Angle { get; internal set; }

    /// <summary>0 when the seg runs along the linedef's front side, 1 along its back side (the lump's <c>side</c>; not kept by vanilla).</summary>
    public int Side { get; }

    public Side SideDef { get; }

    public Line LineDef { get; }

    /// <summary>The sector this seg faces into (<c>frontsector</c>).</summary>
    public Sector FrontSector => SideDef.Sector;

    /// <summary>The sector behind it for a two-sided linedef (<c>backsector</c>), else null.</summary>
    public Sector? BackSector { get; internal set; }

    public override string ToString() => $"seg {Index}";
}

/// <summary>r_defs.h <c>subsector_t</c>: a convex BSP leaf, its segs a run of the seg array.</summary>
public sealed class Subsector
{
    internal Subsector(int index, int numLines, int firstLine)
    {
        Index = index;
        NumLines = numLines;
        FirstLine = firstLine;
    }

    public int Index { get; }

    /// <summary>The sector of its first seg's sidedef (set by <c>P_GroupLines</c>).</summary>
    public Sector Sector { get; internal set; } = null!;

    /// <summary>Number of segs (<c>numlines</c>).</summary>
    public int NumLines { get; }

    /// <summary>Index of the first seg (<c>firstline</c>).</summary>
    public int FirstLine { get; }

    public override string ToString() => $"subsector {Index}";
}

/// <summary>
/// r_defs.h <c>node_t</c>: a BSP node. The partition line runs from
/// (<see cref="X"/>, <see cref="Y"/>) along (<see cref="Dx"/>, <see cref="Dy"/>);
/// child 0 is on its right (front) side, child 1 on its left (back) side.
/// </summary>
public sealed class Node
{
    /// <summary>r_bsp.h / doomdata.h <c>NF_SUBSECTOR</c>: a child with this bit is a subsector number.</summary>
    public const int NF_SUBSECTOR = 0x8000;

    internal Node(int index) => Index = index;

    public int Index { get; }

    /// <summary>fixed_t.</summary>
    public int X { get; internal set; }

    /// <summary>fixed_t.</summary>
    public int Y { get; internal set; }

    /// <summary>fixed_t.</summary>
    public int Dx { get; internal set; }

    /// <summary>fixed_t.</summary>
    public int Dy { get; internal set; }

    /// <summary>fixed_t bounding box of each child (<c>bbox[2][4]</c>, indexed by <see cref="BBox"/>).</summary>
    public int[][] BBox { get; } = { new int[4], new int[4] };

    /// <summary>
    /// <c>children[2]</c>: a node number, or a subsector number with
    /// <see cref="NF_SUBSECTOR"/> set. Unsigned 16-bit, as in the lump.
    /// </summary>
    public int[] Children { get; } = new int[2];

    public override string ToString() => $"node {Index}";
}
