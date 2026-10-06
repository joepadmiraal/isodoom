using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using IsoDoom.Wad;

namespace IsoDoom.Map;

/// <summary>
/// A map's level data, loaded as p_setup.c <c>P_SetupLevel</c> loads it
/// (Chocolate Doom's order: blockmap, vertexes, sectors, sidedefs, linedefs,
/// subsectors, nodes, segs, <c>P_GroupLines</c>, reject). Things are kept as
/// <see cref="MapThing"/>s; spawning them is the sim's job.
/// <para>
/// Map lumps are found by position after the map's header lump (the last lump
/// with that name), as vanilla's <c>lumpnum + ML_*</c> does; their names are
/// checked too. Lump record counts are the lump size divided by the record
/// size (a trailing partial record is ignored, as in vanilla). Indices between
/// structures are read as unsigned 16-bit numbers (vanilla reads most as
/// signed, which only matters past 32767) and range-checked, so a broken map
/// throws <see cref="WadFormatException"/> instead of reading garbage.
/// </para>
/// </summary>
public sealed class Level
{
    // doomdata.h: lump order after the map header (ML_LABEL = 0).
    public const int ML_THINGS = 1;
    public const int ML_LINEDEFS = 2;
    public const int ML_SIDEDEFS = 3;
    public const int ML_VERTEXES = 4;
    public const int ML_SEGS = 5;
    public const int ML_SSECTORS = 6;
    public const int ML_NODES = 7;
    public const int ML_SECTORS = 8;
    public const int ML_REJECT = 9;
    public const int ML_BLOCKMAP = 10;

    private static readonly string[] LumpNames =
    {
        "", "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
        "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP",
    };

    /// <summary>p_local.h <c>MAXRADIUS</c>: the largest thing radius, for the sector block boxes.</summary>
    public const int MAXRADIUS = 32 * Fixed.FRACUNIT;

    private Level(string name) => Name = name;

    /// <summary>The map header lump name, e.g. <c>E1M1</c> or <c>MAP01</c>.</summary>
    public string Name { get; }

    public MapThing[] Things { get; private set; } = Array.Empty<MapThing>();
    public Vertex[] Vertexes { get; private set; } = Array.Empty<Vertex>();
    public Sector[] Sectors { get; private set; } = Array.Empty<Sector>();
    public Side[] Sides { get; private set; } = Array.Empty<Side>();
    public Line[] Lines { get; private set; } = Array.Empty<Line>();
    public Seg[] Segs { get; private set; } = Array.Empty<Seg>();
    public Subsector[] Subsectors { get; private set; } = Array.Empty<Subsector>();

    /// <summary>The BSP nodes; the root is the last one. Empty for a map that is a single subsector.</summary>
    public Node[] Nodes { get; private set; } = Array.Empty<Node>();

    public Blockmap Blockmap { get; private set; } = null!;
    public Reject Reject { get; private set; } = null!;

    /// <summary>Number of sector-line references made by <c>P_GroupLines</c> (Chocolate Doom's <c>totallines</c>).</summary>
    public int TotalLines { get; private set; }

    /// <summary>
    /// Loads map <paramref name="mapName"/> (<c>ExMy</c> or <c>MAPxx</c>, or any
    /// map header lump name). Throws <see cref="WadFormatException"/> when the
    /// map is missing or its lumps are malformed.
    /// </summary>
    public static Level Load(WadArchive wad, string mapName)
    {
        int lumpnum = wad.W_CheckNumForName(mapName);
        if (lumpnum < 0)
            throw new WadFormatException($"Map {mapName} not found.");
        string name = wad.Lumps[lumpnum].Name;
        for (int ml = ML_THINGS; ml <= ML_BLOCKMAP; ml++)
        {
            if (lumpnum + ml >= wad.NumLumps || wad.Lumps[lumpnum + ml].Name != LumpNames[ml])
                throw new WadFormatException($"Map {name}: lump {ml} after the header is not {LumpNames[ml]}.");
        }

        var level = new Level(name);
        ReadOnlySpan<byte> Lump(int ml) => wad.W_CacheLumpNum(lumpnum + ml).Span;

        level.P_LoadBlockMap(Lump(ML_BLOCKMAP));
        level.P_LoadVertexes(Lump(ML_VERTEXES));
        level.P_LoadSectors(Lump(ML_SECTORS));
        level.P_LoadSideDefs(Lump(ML_SIDEDEFS));
        level.P_LoadLineDefs(Lump(ML_LINEDEFS));
        level.P_LoadSubsectors(Lump(ML_SSECTORS));
        level.P_LoadNodes(Lump(ML_NODES));
        level.P_LoadSegs(Lump(ML_SEGS));
        level.P_GroupLines();
        level.P_LoadReject(Lump(ML_REJECT));
        level.P_LoadThings(Lump(ML_THINGS));
        return level;
    }

    /// <summary>
    /// r_main.c <c>R_PointOnSide</c>: 0 when (<paramref name="x"/>,
    /// <paramref name="y"/>) (fixed_t) is on the front (right) side of the
    /// node's partition line, 1 on the back side.
    /// </summary>
    public static int R_PointOnSide(int x, int y, Node node)
    {
        if (node.Dx == 0)
        {
            if (x <= node.X)
                return node.Dy > 0 ? 1 : 0;
            return node.Dy < 0 ? 1 : 0;
        }
        if (node.Dy == 0)
        {
            if (y <= node.Y)
                return node.Dx < 0 ? 1 : 0;
            return node.Dx > 0 ? 1 : 0;
        }

        int dx = x - node.X;
        int dy = y - node.Y;

        // Try to quickly decide by looking at sign bits.
        if (((node.Dy ^ node.Dx ^ dx ^ dy) & unchecked((int)0x80000000)) != 0)
        {
            if (((node.Dy ^ dx) & unchecked((int)0x80000000)) != 0)
                return 1; // (left is negative)
            return 0;
        }

        int left = Fixed.FixedMul(node.Dy >> Fixed.FRACBITS, dx);
        int right = Fixed.FixedMul(dy, node.Dx >> Fixed.FRACBITS);
        return right < left ? 0 : 1; // front side : back side
    }

    /// <summary>r_main.c <c>R_PointInSubsector</c>: walks the BSP from the root to the subsector holding the point (fixed_t).</summary>
    public Subsector R_PointInSubsector(int x, int y)
    {
        // single subsector is a special case
        if (Nodes.Length == 0)
            return Subsectors[0];
        int nodenum = Nodes.Length - 1;
        while ((nodenum & Node.NF_SUBSECTOR) == 0)
        {
            Node node = Nodes[nodenum];
            nodenum = node.Children[R_PointOnSide(x, y, node)];
        }
        return Subsectors[nodenum & ~Node.NF_SUBSECTOR];
    }

    /// <summary>
    /// The start of player <paramref name="player"/> (0–3), as p_mobj.c
    /// <c>P_SpawnMapThing</c> records it in <c>playerstarts[]</c>: the last
    /// thing of type <c>player + 1</c> in <c>THINGS</c> (vanilla overwrites
    /// earlier ones), or null when the map has none.
    /// </summary>
    public MapThing? PlayerStart(int player)
    {
        if (player is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(player));
        MapThing? start = null;
        foreach (MapThing t in Things)
        {
            if (t.Type == player + 1)
                start = t; // p_mobj.c: playerstarts[mthing->type-1] = *mthing;
        }
        return start;
    }

    // ---- p_setup.c ----

    private static int Count(ReadOnlySpan<byte> lump, int recordSize) => lump.Length / recordSize;

    private static short S16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);

    private static int U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);

    /// <summary>An 8-byte texture or flat name: cut at the first NUL, upper case (lookups are case-insensitive in vanilla).</summary>
    private static string Name8(ReadOnlySpan<byte> raw)
    {
        raw = raw[..8];
        int len = raw.IndexOf((byte)0);
        return Encoding.Latin1.GetString(len < 0 ? raw : raw[..len]).ToUpperInvariant();
    }

    private T At<T>(T[] array, int index, string what, string user)
    {
        if ((uint)index >= (uint)array.Length)
            throw new WadFormatException($"Map {Name}: {user} refers to {what} {index}, but there are {array.Length}.");
        return array[index];
    }

    /// <summary>p_setup.c <c>P_LoadVertexes</c> (<c>mapvertex_t</c>: x, y).</summary>
    private void P_LoadVertexes(ReadOnlySpan<byte> data)
    {
        Vertexes = new Vertex[Count(data, 4)];
        for (int i = 0; i < Vertexes.Length; i++)
        {
            ReadOnlySpan<byte> ml = data.Slice(i * 4, 4);
            Vertexes[i] = new Vertex(i, S16(ml, 0) << Fixed.FRACBITS, S16(ml, 2) << Fixed.FRACBITS);
        }
    }

    /// <summary>p_setup.c <c>P_LoadSectors</c> (<c>mapsector_t</c>: floor and ceiling height, floor and ceiling flat, light, special, tag).</summary>
    private void P_LoadSectors(ReadOnlySpan<byte> data)
    {
        Sectors = new Sector[Count(data, 26)];
        for (int i = 0; i < Sectors.Length; i++)
        {
            ReadOnlySpan<byte> ms = data.Slice(i * 26, 26);
            Sectors[i] = new Sector(i)
            {
                FloorHeight = S16(ms, 0) << Fixed.FRACBITS,
                CeilingHeight = S16(ms, 2) << Fixed.FRACBITS,
                FloorPic = Name8(ms[4..]),
                CeilingPic = Name8(ms[12..]),
                LightLevel = S16(ms, 20),
                Special = S16(ms, 22),
                Tag = S16(ms, 24),
            };
        }
    }

    /// <summary>p_setup.c <c>P_LoadSideDefs</c> (<c>mapsidedef_t</c>: x and y offset, upper, lower and middle texture, sector).</summary>
    private void P_LoadSideDefs(ReadOnlySpan<byte> data)
    {
        Sides = new Side[Count(data, 30)];
        for (int i = 0; i < Sides.Length; i++)
        {
            ReadOnlySpan<byte> msd = data.Slice(i * 30, 30);
            Sides[i] = new Side(i, At(Sectors, U16(msd, 28), "sector", $"sidedef {i}"))
            {
                TextureOffset = S16(msd, 0) << Fixed.FRACBITS,
                RowOffset = S16(msd, 2) << Fixed.FRACBITS,
                TopTexture = Name8(msd[4..]),
                BottomTexture = Name8(msd[12..]),
                MidTexture = Name8(msd[20..]),
            };
        }
    }

    /// <summary>p_setup.c <c>P_LoadLineDefs</c> (<c>maplinedef_t</c>: v1, v2, flags, special, tag, sidenum[2]).</summary>
    private void P_LoadLineDefs(ReadOnlySpan<byte> data)
    {
        Lines = new Line[Count(data, 14)];
        for (int i = 0; i < Lines.Length; i++)
        {
            ReadOnlySpan<byte> mld = data.Slice(i * 14, 14);
            string user = $"linedef {i}";
            Vertex v1 = At(Vertexes, U16(mld, 0), "vertex", user);
            Vertex v2 = At(Vertexes, U16(mld, 2), "vertex", user);
            var ld = new Line(i, v1, v2)
            {
                Flags = S16(mld, 4),
                Special = S16(mld, 6),
                Tag = S16(mld, 8),
                Dx = v2.X - v1.X,
                Dy = v2.Y - v1.Y,
            };

            if (ld.Dx == 0)
                ld.SlopeType = SlopeType.ST_VERTICAL;
            else if (ld.Dy == 0)
                ld.SlopeType = SlopeType.ST_HORIZONTAL;
            else
                ld.SlopeType = Fixed.FixedDiv(ld.Dy, ld.Dx) > 0 ? SlopeType.ST_POSITIVE : SlopeType.ST_NEGATIVE;

            if (v1.X < v2.X)
            {
                ld.BBox[BBox.BOXLEFT] = v1.X;
                ld.BBox[BBox.BOXRIGHT] = v2.X;
            }
            else
            {
                ld.BBox[BBox.BOXLEFT] = v2.X;
                ld.BBox[BBox.BOXRIGHT] = v1.X;
            }
            if (v1.Y < v2.Y)
            {
                ld.BBox[BBox.BOXBOTTOM] = v1.Y;
                ld.BBox[BBox.BOXTOP] = v2.Y;
            }
            else
            {
                ld.BBox[BBox.BOXBOTTOM] = v2.Y;
                ld.BBox[BBox.BOXTOP] = v1.Y;
            }

            for (int s = 0; s < 2; s++)
            {
                int sidenum = U16(mld, 10 + 2 * s);
                ld.SideNum[s] = sidenum == 0xFFFF ? -1 : sidenum;
            }
            if (ld.SideNum[0] != -1)
                ld.FrontSector = At(Sides, ld.SideNum[0], "sidedef", user).Sector;
            if (ld.SideNum[1] != -1)
                ld.BackSector = At(Sides, ld.SideNum[1], "sidedef", user).Sector;
            Lines[i] = ld;
        }
    }

    /// <summary>p_setup.c <c>P_LoadSubsectors</c> (<c>mapsubsector_t</c>: numsegs, firstseg).</summary>
    private void P_LoadSubsectors(ReadOnlySpan<byte> data)
    {
        Subsectors = new Subsector[Count(data, 4)];
        if (Subsectors.Length == 0)
            throw new WadFormatException($"Map {Name} has no subsectors.");
        for (int i = 0; i < Subsectors.Length; i++)
        {
            ReadOnlySpan<byte> ms = data.Slice(i * 4, 4);
            Subsectors[i] = new Subsector(i, U16(ms, 0), U16(ms, 2));
        }
    }

    /// <summary>p_setup.c <c>P_LoadNodes</c> (<c>mapnode_t</c>: x, y, dx, dy, bbox[2][4], children[2]).</summary>
    private void P_LoadNodes(ReadOnlySpan<byte> data)
    {
        Nodes = new Node[Count(data, 28)];
        for (int i = 0; i < Nodes.Length; i++)
        {
            ReadOnlySpan<byte> mn = data.Slice(i * 28, 28);
            var no = new Node(i)
            {
                X = S16(mn, 0) << Fixed.FRACBITS,
                Y = S16(mn, 2) << Fixed.FRACBITS,
                Dx = S16(mn, 4) << Fixed.FRACBITS,
                Dy = S16(mn, 6) << Fixed.FRACBITS,
            };
            for (int j = 0; j < 2; j++)
            {
                int child = U16(mn, 24 + 2 * j);
                // A node child must be an earlier node (the root is last), so the tree has no cycles.
                if ((child & Node.NF_SUBSECTOR) != 0
                    ? (child & ~Node.NF_SUBSECTOR) >= Subsectors.Length
                    : child >= i)
                    throw new WadFormatException($"Map {Name}: node {i} child {j} ({child:X4}) is out of range.");
                no.Children[j] = child;
                for (int k = 0; k < 4; k++)
                    no.BBox[j][k] = S16(mn, 8 + 8 * j + 2 * k) << Fixed.FRACBITS;
            }
            Nodes[i] = no;
        }
    }

    /// <summary>p_setup.c <c>P_LoadSegs</c> (<c>mapseg_t</c>: v1, v2, angle, linedef, side, offset).</summary>
    private void P_LoadSegs(ReadOnlySpan<byte> data)
    {
        Segs = new Seg[Count(data, 12)];
        for (int i = 0; i < Segs.Length; i++)
        {
            ReadOnlySpan<byte> ml = data.Slice(i * 12, 12);
            string user = $"seg {i}";
            Line ldef = At(Lines, U16(ml, 6), "linedef", user);
            int side = S16(ml, 8);
            if (side != 0 && side != 1)
                throw new WadFormatException($"Map {Name}: seg {i} has side {side}.");
            Side sidedef = At(Sides, ldef.SideNum[side], "sidedef", user);
            var li = new Seg(i, At(Vertexes, U16(ml, 0), "vertex", user), At(Vertexes, U16(ml, 2), "vertex", user), ldef, side, sidedef)
            {
                Angle = (uint)U16(ml, 4) << 16,
                Offset = S16(ml, 10) << Fixed.FRACBITS,
            };
            // Vanilla reads sides[-1] for a two-sided line without a back side
            // (Chocolate Doom emulates what that read finds); no back sector here.
            if ((ldef.Flags & Line.ML_TWOSIDED) != 0 && ldef.SideNum[side ^ 1] != -1)
                li.BackSector = At(Sides, ldef.SideNum[side ^ 1], "sidedef", user).Sector;
            Segs[i] = li;
        }
        foreach (Subsector ss in Subsectors)
        {
            if (ss.NumLines == 0 || ss.FirstLine + ss.NumLines > Segs.Length)
                throw new WadFormatException($"Map {Name}: subsector {ss.Index} segs {ss.FirstLine}+{ss.NumLines} are out of range ({Segs.Length} segs).");
        }
    }

    /// <summary>p_setup.c <c>P_LoadBlockMap</c>.</summary>
    private void P_LoadBlockMap(ReadOnlySpan<byte> data)
    {
        short[] lump = new short[data.Length / 2];
        for (int i = 0; i < lump.Length; i++)
            lump[i] = S16(data, i * 2);
        try
        {
            Blockmap = new Blockmap(lump);
        }
        catch (WadFormatException e)
        {
            throw new WadFormatException($"Map {Name}: {e.Message}");
        }
    }

    /// <summary>
    /// p_setup.c <c>P_GroupLines</c>: each subsector's sector, each sector's
    /// lines, sound origin and block box.
    /// </summary>
    private void P_GroupLines()
    {
        // look up sector number for each subsector
        foreach (Subsector ss in Subsectors)
            ss.Sector = Segs[ss.FirstLine].SideDef.Sector;

        // count number of lines in each sector
        var sectorLines = new List<Line>[Sectors.Length];
        for (int i = 0; i < sectorLines.Length; i++)
            sectorLines[i] = new List<Line>();
        TotalLines = 0;
        foreach (Line li in Lines)
        {
            // Vanilla dereferences a missing front sector here.
            if (li.FrontSector is null)
                throw new WadFormatException($"Map {Name}: linedef {li.Index} has no front sidedef.");
            TotalLines++;
            if (li.BackSector is not null && li.BackSector != li.FrontSector)
                TotalLines++;
            // build line tables for each sector (same order as vanilla's per-sector scan of lines[])
            sectorLines[li.FrontSector.Index].Add(li);
            if (li.BackSector is not null && li.BackSector != li.FrontSector)
                sectorLines[li.BackSector.Index].Add(li);
        }

        int[] bbox = new int[4];
        foreach (Sector sector in Sectors)
        {
            BBox.M_ClearBox(bbox);
            foreach (Line li in sectorLines[sector.Index])
            {
                BBox.M_AddToBox(bbox, li.V1.X, li.V1.Y);
                BBox.M_AddToBox(bbox, li.V2.X, li.V2.Y);
            }
            sector.Lines = sectorLines[sector.Index].ToArray();

            unchecked
            {
                // set the degenmobj_t to the middle of the bounding box
                sector.SoundOrgX = (bbox[BBox.BOXRIGHT] + bbox[BBox.BOXLEFT]) / 2;
                sector.SoundOrgY = (bbox[BBox.BOXTOP] + bbox[BBox.BOXBOTTOM]) / 2;

                // adjust bounding box to map blocks
                int block = (bbox[BBox.BOXTOP] - Blockmap.BmapOrgY + MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
                sector.BlockBox[BBox.BOXTOP] = block >= Blockmap.BmapHeight ? Blockmap.BmapHeight - 1 : block;
                block = (bbox[BBox.BOXBOTTOM] - Blockmap.BmapOrgY - MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
                sector.BlockBox[BBox.BOXBOTTOM] = block < 0 ? 0 : block;
                block = (bbox[BBox.BOXRIGHT] - Blockmap.BmapOrgX + MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
                sector.BlockBox[BBox.BOXRIGHT] = block >= Blockmap.BmapWidth ? Blockmap.BmapWidth - 1 : block;
                block = (bbox[BBox.BOXLEFT] - Blockmap.BmapOrgX - MAXRADIUS) >> Blockmap.MAPBLOCKSHIFT;
                sector.BlockBox[BBox.BOXLEFT] = block < 0 ? 0 : block;
            }
        }
    }

    /// <summary>
    /// p_setup.c <c>P_LoadReject</c>, with Chocolate Doom's overrun emulation:
    /// a lump shorter than <c>(numsectors² + 7) / 8</c> bytes is padded with
    /// the bytes vanilla read past its end (<c>PadRejectArray</c>: the next
    /// zone block header, then zeros).
    /// </summary>
    private void P_LoadReject(ReadOnlySpan<byte> data)
    {
        int minlength = (Sectors.Length * Sectors.Length + 7) / 8;
        byte[] matrix = new byte[Math.Max(minlength, data.Length)];
        data.CopyTo(matrix);
        if (data.Length < minlength)
            PadRejectArray(matrix.AsSpan(data.Length, minlength - data.Length), TotalLines);
        Reject = new Reject(matrix);
    }

    /// <summary>Chocolate Doom p_setup.c <c>PadRejectArray</c> (without its <c>-reject_pad_with_ff</c> option).</summary>
    private static void PadRejectArray(Span<byte> array, int totallines)
    {
        // Values to pad the REJECT array with:
        uint[] rejectpad =
        {
            (uint)(((totallines * 4 + 3) & ~3) + 24), // Size
            0,                                        // Part of z_zone block header
            50,                                       // PU_LEVEL
            0x1d4a11,                                 // DOOM_CONST_ZONEID
        };
        for (int i = 0; i < array.Length && i < rejectpad.Length * 4; i++)
            array[i] = (byte)(rejectpad[i / 4] >> (i % 4 * 8));
        // The rest stays 0.
    }

    /// <summary>p_setup.c <c>P_LoadThings</c>'s read of <c>mapthing_t</c> (x, y, angle, type, options); spawning is the sim's.</summary>
    private void P_LoadThings(ReadOnlySpan<byte> data)
    {
        Things = new MapThing[Count(data, 10)];
        for (int i = 0; i < Things.Length; i++)
        {
            ReadOnlySpan<byte> mt = data.Slice(i * 10, 10);
            Things[i] = new MapThing(S16(mt, 0), S16(mt, 2), S16(mt, 4), S16(mt, 6), S16(mt, 8));
        }
    }
}
