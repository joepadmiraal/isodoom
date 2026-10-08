using System;
using System.Collections.Generic;

namespace IsoDoom.Map;

/// <summary>
/// What distance light diminishing uses in the level renderer (a
/// presentation option, SPEC §7.4, §12 T2.8). Every mode looks the colormap
/// up in vanilla's tables (<see cref="LightTables"/>); only the distance fed
/// to them differs.
/// </summary>
public enum LightDiminishing
{
    /// <summary>
    /// Sector light only: every surface gets the colormap the tables give at
    /// one fixed reference distance (<see cref="LightTables.DefaultReferenceDistance"/>
    /// unless set), whatever the camera or player do.
    /// </summary>
    None,

    /// <summary>
    /// Default: the horizontal (map x/y) distance from the player, as if the
    /// player's eye looked at every point straight on, so light falls off in
    /// a radius around the player and doesn't change as the camera moves or
    /// the player turns; distances below a minimum (<see cref="LightTables.DefaultNearDistance"/>
    /// unless set, T3.7) count as that minimum. The level scene uses the player
    /// mobj (T4.7), or the free-fly camera's pivot while that camera is current.
    /// </summary>
    Player,

    /// <summary>
    /// The view-space depth from the rendering camera, vanilla's own distance
    /// (perpendicular to the view plane): exact vanilla for a level, eye-level
    /// perspective camera; with the isometric camera, which sits far from the
    /// level, everything gets the far-distance colormap.
    /// </summary>
    Camera,
}

/// <summary>
/// Vanilla's light tables (r_main.c <c>R_InitLightTables</c> for
/// <see cref="zlight"/>, <c>R_ExecuteSetViewSize</c> for
/// <see cref="scalelight"/>) and the colormap choice of r_segs.c (walls,
/// with the fake contrast) and r_plane.c (floors and ceilings), as integer
/// C#: the CPU reference of <c>shaders/level.gdshader</c>, which reads the
/// same tables from a texture. Entries are colormap numbers (0 bright to 31
/// dark) where vanilla keeps pointers into <c>colormaps</c>.
/// </summary>
public sealed class LightTables
{
    // r_main.h / r_main.c
    public const int LIGHTLEVELS = 16;
    public const int LIGHTSEGSHIFT = 4;
    public const int MAXLIGHTSCALE = 48;
    public const int LIGHTSCALESHIFT = 12;
    public const int MAXLIGHTZ = 128;
    public const int LIGHTZSHIFT = 20;
    public const int NUMCOLORMAPS = 32;
    public const int DISTMAP = 2;

    /// <summary>doomdef.h <c>SCREENWIDTH</c>.</summary>
    public const int SCREENWIDTH = 320;

    /// <summary>
    /// The reference distance of <see cref="LightDiminishing.None"/> (map
    /// units; T3.7): the middle of the pool of light
    /// <see cref="LightDiminishing.Player"/> draws around the player, so a
    /// sector looks about as it does near the player in that mode (256, the
    /// screen edge's distance, made every sector a step or two duller).
    /// </summary>
    public const int DefaultReferenceDistance = 128;

    /// <summary>
    /// The shortest distance <see cref="LightDiminishing.Player"/> feeds the
    /// tables (map units; T3.7): about the nearest floor vanilla's view shows
    /// (79 units ahead at eye height 41), so the floor around the player,
    /// which vanilla never draws, isn't a full-bright disc. Walls and sprites
    /// (the player's own too) take the same floor, so walls and floors at a
    /// distance still match (both tables give about <c>startmap - 1280 / d</c>).
    /// </summary>
    public const int DefaultNearDistance = 80;

    /// <summary>
    /// The longest distance worth telling apart (map units, a power of two):
    /// <see cref="zlight"/> stops at <c>MAXLIGHTZ &lt;&lt; LIGHTZSHIFT</c>
    /// (2048) and <see cref="scalelight"/>'s index is 0 from 2560 on (full
    /// view width). The shader clamps distances to it before converting to
    /// fixed_t, so they fit an <see cref="int"/>.
    /// </summary>
    public const int MaxDistanceUnits = 4096;

    private readonly int[] _zlight = new int[LIGHTLEVELS * MAXLIGHTZ];
    private readonly int[] _scalelight = new int[LIGHTLEVELS * MAXLIGHTSCALE];

    private LightTables(int viewwidth, int detailshift)
    {
        ViewWidth = viewwidth;
        DetailShift = detailshift;
    }

    /// <summary>r_main.c <c>viewwidth</c> (in drawn columns: 320 at full size, half that in low detail) and <c>detailshift</c>.</summary>
    public int ViewWidth { get; }
    public int DetailShift { get; }

    /// <summary>r_main.c <c>centerx</c> (<c>viewwidth / 2</c>); <c>projection</c> = <c>centerxfrac</c> = <c>centerx &lt;&lt; FRACBITS</c>.</summary>
    public int CenterX => ViewWidth / 2;

    /// <summary>
    /// Builds both tables for a view <paramref name="viewwidth"/> columns
    /// wide (vanilla: <c>scaledviewwidth &gt;&gt; detailshift</c>; the default
    /// is the full-screen view, screen size 10 or 11, high detail).
    /// </summary>
    public static LightTables R_InitLightTables(int viewwidth = SCREENWIDTH, int detailshift = 0)
    {
        if (viewwidth <= 0 || viewwidth > SCREENWIDTH || detailshift < 0 || detailshift > 1)
            throw new ArgumentOutOfRangeException(nameof(viewwidth), "viewwidth must be 1-320 and detailshift 0 or 1");
        var t = new LightTables(viewwidth, detailshift);

        // r_main.c R_InitLightTables: the floor/ceiling table by distance.
        for (int i = 0; i < LIGHTLEVELS; i++)
        {
            int startmap = ((LIGHTLEVELS - 1 - i) * 2) * NUMCOLORMAPS / LIGHTLEVELS;
            for (int j = 0; j < MAXLIGHTZ; j++)
            {
                int scale = Fixed.FixedDiv(SCREENWIDTH / 2 * Fixed.FRACUNIT, (j + 1) << LIGHTZSHIFT);
                scale >>= LIGHTSCALESHIFT;
                int level = startmap - scale / DISTMAP;
                t._zlight[i * MAXLIGHTZ + j] = Math.Clamp(level, 0, NUMCOLORMAPS - 1);
            }
        }

        // r_main.c R_ExecuteSetViewSize: the wall/sprite table by scale.
        for (int i = 0; i < LIGHTLEVELS; i++)
        {
            int startmap = ((LIGHTLEVELS - 1 - i) * 2) * NUMCOLORMAPS / LIGHTLEVELS;
            for (int j = 0; j < MAXLIGHTSCALE; j++)
            {
                int level = startmap - j * SCREENWIDTH / (viewwidth << detailshift) / DISTMAP;
                t._scalelight[i * MAXLIGHTSCALE + j] = Math.Clamp(level, 0, NUMCOLORMAPS - 1);
            }
        }
        return t;
    }

    /// <summary>r_main.c <c>zlight[light][z]</c>: the colormap of a floor or ceiling span at distance index <paramref name="z"/>.</summary>
    public int zlight(int light, int z) => _zlight[light * MAXLIGHTZ + z];

    /// <summary>r_main.c <c>scalelight[light][scale]</c>: the colormap of a wall column (or sprite) at scale index <paramref name="scale"/>.</summary>
    public int scalelight(int light, int scale) => _scalelight[light * MAXLIGHTSCALE + scale];

    /// <summary>
    /// r_segs.c <c>R_StoreWallRange</c>'s fake contrast: walls along the
    /// map's x axis (<c>v1-&gt;y == v2-&gt;y</c>) one light level darker, along
    /// the y axis (<c>v1-&gt;x == v2-&gt;x</c>) one lighter, others unchanged
    /// (for one-sided and two-sided walls alike; vanilla tests each seg's
    /// vertexes, which lie on its linedef).
    /// </summary>
    public static int FakeContrast(int x1, int y1, int x2, int y2) => y1 == y2 ? -1 : x1 == x2 ? 1 : 0;

    /// <summary>
    /// The scale index a wall at <paramref name="distance"/> (fixed_t) gets:
    /// r_segs.c <c>R_RenderSegLoop</c>'s <c>rw_scale &gt;&gt; LIGHTSCALESHIFT</c>
    /// (capped at <c>MAXLIGHTSCALE − 1</c>), with <c>rw_scale</c> =
    /// <c>FixedDiv(projection, distance)</c>, the scale of a wall seen straight
    /// on. Equals <c>(centerx &lt;&lt; 20) / distance</c>, as the shader computes it.
    /// </summary>
    public int WallScaleIndex(int distance)
    {
        if (distance <= 0)
            return MAXLIGHTSCALE - 1;
        int scale = Fixed.FixedDiv(CenterX << Fixed.FRACBITS, distance);
        return Math.Min(scale >> LIGHTSCALESHIFT, MAXLIGHTSCALE - 1);
    }

    /// <summary>r_plane.c <c>R_MapPlane</c>'s distance index <c>distance &gt;&gt; LIGHTZSHIFT</c>, capped at <c>MAXLIGHTZ − 1</c>.</summary>
    public static int PlaneZIndex(int distance) => distance <= 0 ? 0 : Math.Min(distance >> LIGHTZSHIFT, MAXLIGHTZ - 1);

    /// <summary>
    /// The colormap of a wall in a sector of light <paramref name="lightlevel"/>
    /// at <paramref name="distance"/> (fixed_t): r_segs.c <c>R_StoreWallRange</c>
    /// (<c>lightnum = (lightlevel &gt;&gt; LIGHTSEGSHIFT) + extralight</c>, plus
    /// the fake <paramref name="contrast"/>, clamped to the table) and
    /// <c>R_RenderSegLoop</c> (<c>walllights[index]</c>).
    /// </summary>
    public int WallColormap(int lightlevel, int extralight, int contrast, int distance)
    {
        int lightnum = (lightlevel >> LIGHTSEGSHIFT) + extralight + contrast;
        return scalelight(Math.Clamp(lightnum, 0, LIGHTLEVELS - 1), WallScaleIndex(distance));
    }

    /// <summary>
    /// The colormap of a floor in a sector of light <paramref name="lightlevel"/>
    /// at <paramref name="distance"/> (fixed_t): r_plane.c <c>R_DrawPlanes</c>
    /// (<c>planezlight = zlight[(lightlevel &gt;&gt; LIGHTSEGSHIFT) + extralight]</c>,
    /// clamped) and <c>R_MapPlane</c> (<c>planezlight[distance &gt;&gt; LIGHTZSHIFT]</c>).
    /// </summary>
    public int PlaneColormap(int lightlevel, int extralight, int distance)
    {
        int light = (lightlevel >> LIGHTSEGSHIFT) + extralight;
        return zlight(Math.Clamp(light, 0, LIGHTLEVELS - 1), PlaneZIndex(distance));
    }

    /// <summary>Rows of <see cref="ToBytes"/>: <see cref="zlight"/> in rows 0–15, <see cref="scalelight"/> in rows 16–31.</summary>
    public const int TableWidth = MAXLIGHTZ, TableHeight = 2 * LIGHTLEVELS;

    /// <summary>
    /// Both tables as one <see cref="TableWidth"/> × <see cref="TableHeight"/>
    /// byte image (row-major) for the shader: row <c>i</c> is
    /// <c>zlight[i]</c>, row <c>16 + i</c> is <c>scalelight[i]</c> (then zeros).
    /// </summary>
    public byte[] ToBytes()
    {
        byte[] data = new byte[TableWidth * TableHeight];
        for (int i = 0; i < LIGHTLEVELS; i++)
        {
            for (int j = 0; j < MAXLIGHTZ; j++)
                data[i * TableWidth + j] = (byte)zlight(i, j);
            for (int j = 0; j < MAXLIGHTSCALE; j++)
                data[(LIGHTLEVELS + i) * TableWidth + j] = (byte)scalelight(i, j);
        }
        return data;
    }
}

/// <summary>
/// A stretch of a linedef side whose segs share one fake contrast
/// (<see cref="LightTables.FakeContrast"/>): from <see cref="Start"/> (fixed_t
/// distance from the side's start, its <see cref="WallSection.V1"/>) to the
/// next run's start or the side's end.
/// </summary>
public readonly record struct ContrastRun(int Start, int Contrast);

/// <summary>
/// The fake contrast along every linedef side. Vanilla picks it per seg from
/// the seg's own vertexes (r_segs.c <c>R_StoreWallRange</c>), and a seg of a
/// diagonal line whose vertexes were rounded onto one axis gets the axis's
/// contrast (DOOM1 E1M6 has one, Doom II a few): so a side is split into
/// runs of segs with equal contrast. (T2.8 drew one quad per run; since T2.9
/// the level mesh draws per seg, <see cref="WallPieces"/>, each piece with its
/// seg's contrast, and this stays as the per-side summary its tests pin.)
/// </summary>
public sealed class SideContrasts
{
    private static readonly ContrastRun[] NoRuns = [];
    private readonly ContrastRun[][] _runs; // per line * 2 + side

    private SideContrasts(ContrastRun[][] runs) => _runs = runs;

    /// <summary>Groups the level's segs by linedef side (in seg <c>offset</c> order) into contrast runs.</summary>
    public static SideContrasts Build(Level level)
    {
        var segs = new List<Seg>?[level.Lines.Length * 2];
        foreach (Seg seg in level.Segs)
            (segs[seg.LineDef.Index * 2 + seg.Side] ??= []).Add(seg);
        var runs = new ContrastRun[segs.Length][];
        for (int i = 0; i < segs.Length; i++)
        {
            List<Seg>? list = segs[i];
            if (list is null)
            {
                runs[i] = NoRuns;
                continue;
            }
            list.Sort((a, b) => a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : a.Index.CompareTo(b.Index));
            var side = new List<ContrastRun>();
            foreach (Seg seg in list)
            {
                int contrast = LightTables.FakeContrast(seg.V1.X, seg.V1.Y, seg.V2.X, seg.V2.Y);
                if (side.Count == 0)
                    side.Add(new ContrastRun(0, contrast)); // the first run starts at the side's start
                else if (side[^1].Contrast != contrast)
                    side.Add(new ContrastRun(seg.Offset, contrast));
            }
            runs[i] = [.. side];
        }
        return new SideContrasts(runs);
    }

    /// <summary>The contrast runs of <paramref name="line"/>'s side <paramref name="side"/> (empty when no seg runs along it).</summary>
    public IReadOnlyList<ContrastRun> Runs(Line line, int side) => _runs[line.Index * 2 + side];
}
