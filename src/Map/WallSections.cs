using System;
using System.Collections.Generic;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Map;

/// <summary>The kind of a <see cref="WallSection"/> (which texture slot of its sidedef it draws).</summary>
public enum WallSectionKind
{
    /// <summary>One-sided line: the middle texture from floor to ceiling.</summary>
    Middle,

    /// <summary>Two-sided line: the upper texture, from the back ceiling up to the front ceiling.</summary>
    Upper,

    /// <summary>Two-sided line: the lower texture, from the front floor up to the back floor.</summary>
    Lower,

    /// <summary>
    /// Two-sided line: the masked middle texture (grates, fences) in the
    /// opening, drawn once (not tiled vertically) and clipped to the opening.
    /// </summary>
    MaskedMiddle,
}

/// <summary>A sector plane a <see cref="PlaneRef"/> refers to, seen from a sidedef.</summary>
public enum WallPlane
{
    /// <summary>The floor of the side's own sector (vanilla's <c>frontsector</c>).</summary>
    FrontFloor,

    /// <summary>The ceiling of the side's own sector.</summary>
    FrontCeiling,

    /// <summary>The floor of the sector behind the side (<c>backsector</c>).</summary>
    BackFloor,

    /// <summary>The ceiling of the sector behind the side.</summary>
    BackCeiling,

    /// <summary>The higher of the two floors (the bottom of the opening; masked middles).</summary>
    HigherFloor,

    /// <summary>The lower of the two ceilings (the top of the opening; masked middles).</summary>
    LowerCeiling,
}

/// <summary>
/// A height given as a sector plane plus a constant (fixed_t), so that it
/// follows the plane when the sim moves it (SPEC §7.2 <i>Moving sectors</i>).
/// </summary>
public readonly record struct PlaneRef(WallPlane Plane, int Offset)
{
    /// <summary>fixed_t: the height for the given sectors' current planes.</summary>
    public int Evaluate(Sector front, Sector? back) => Plane switch
    {
        WallPlane.FrontFloor => front.FloorHeight + Offset,
        WallPlane.FrontCeiling => front.CeilingHeight + Offset,
        WallPlane.BackFloor => Back(back).FloorHeight + Offset,
        WallPlane.BackCeiling => Back(back).CeilingHeight + Offset,
        WallPlane.HigherFloor => Math.Max(front.FloorHeight, Back(back).FloorHeight) + Offset,
        WallPlane.LowerCeiling => Math.Min(front.CeilingHeight, Back(back).CeilingHeight) + Offset,
        _ => throw new InvalidOperationException($"unknown plane {Plane}"),
    };

    private static Sector Back(Sector? back) =>
        back ?? throw new InvalidOperationException("a back plane needs a back sector");

    public override string ToString()
    {
        if (Offset == 0)
            return Plane.ToString();
        string units = (Offset & (Fixed.FRACUNIT - 1)) == 0 ? $"{Math.Abs(Offset >> Fixed.FRACBITS)}" : $"{Math.Abs((long)Offset)}/65536";
        return $"{Plane} {(Offset < 0 ? "-" : "+")} {units}";
    }
}

/// <summary>
/// One wall quad of a linedef side (SPEC §7.2 <i>Walls</i>), with the section
/// and pegging rules of r_segs.c <c>R_StoreWallRange</c>.
/// <para>
/// <b>Geometry.</b> The quad runs from <see cref="V1"/> to <see cref="V2"/> in
/// the side's direction (the linedef's direction for the front side, reversed
/// for the back side), so the side's sector is on the right, as for segs. It
/// spans <see cref="Bottom"/> to <see cref="Top"/>; a renderer evaluates them
/// for the current sector heights and draws nothing where <c>Top ≤ Bottom</c>.
/// Upper, lower and middle sections are also clipped to the front sector's
/// floor-to-ceiling span, as vanilla's column loop clips them to the floor and
/// ceiling it marks (an upper behind a floor that rose above the back ceiling,
/// a lower under a ceiling that came down below the back floor).
/// </para>
/// <para>
/// <b>Texture.</b> <see cref="Texture"/> is a texture number of
/// <see cref="Textures"/>; 0 means none (vanilla's <c>-</c>, which maps to
/// texture 0, is never drawn: a hall of mirrors). The texture column at a
/// point <c>d</c> map units along the quad from <see cref="V1"/> is
/// <c>(TextureOffset + d) &gt;&gt; FRACBITS</c>, wrapped, and the texture row at
/// height <c>z</c> is <c>(TextureTop − z) &gt;&gt; FRACBITS</c>: <see cref="TextureTop"/>
/// is the height of the texture's row 0 (vanilla's <c>rw_*texturemid</c> plus
/// <c>viewz</c>, the side's <c>rowoffset</c> included). Solid sections tile
/// vertically; a <see cref="WallSectionKind.MaskedMiddle"/> is drawn once,
/// from <see cref="TextureTop"/> down by the texture height, and clipped to
/// the opening (r_segs.c <c>R_RenderMaskedSegRange</c>, r_things.c
/// <c>R_DrawMaskedColumn</c>).
/// </para>
/// </summary>
public sealed class WallSection
{
    internal WallSection(Line line, int side, Side sidedef, Sector? back, WallSectionKind kind, int texture, int textureHeight,
        PlaneRef bottom, PlaneRef top, PlaneRef textureTop, bool bottomPegged)
    {
        Line = line;
        Side = side;
        SideDef = sidedef;
        BackSector = back;
        Kind = kind;
        Texture = texture;
        TextureHeight = textureHeight;
        TextureOffset = sidedef.TextureOffset;
        Bottom = bottom;
        Top = top;
        TextureTop = textureTop;
        BottomPegged = bottomPegged;
    }

    public Line Line { get; }

    /// <summary>0 for the linedef's front (right) side, 1 for its back (left) side.</summary>
    public int Side { get; }

    public Side SideDef { get; }

    public WallSectionKind Kind { get; }

    /// <summary>The start of the quad, in the side's direction.</summary>
    public Vertex V1 => Side == 0 ? Line.V1 : Line.V2;

    /// <summary>The end of the quad.</summary>
    public Vertex V2 => Side == 0 ? Line.V2 : Line.V1;

    /// <summary>The side's sector (vanilla's <c>frontsector</c> for its segs).</summary>
    public Sector FrontSector => SideDef.Sector;

    /// <summary>
    /// The sector behind the side (the seg's <c>backsector</c>): the other side's
    /// sector for a line with <see cref="Line.ML_TWOSIDED"/> and two sides, else null.
    /// </summary>
    public Sector? BackSector { get; }

    /// <summary>Texture number (<c>R_TextureNumForName</c>); 0 is none.</summary>
    public int Texture { get; }

    /// <summary>fixed_t: the texture's height (r_data.c <c>textureheight[]</c>), 0 for none.</summary>
    public int TextureHeight { get; }

    /// <summary>fixed_t: the side's horizontal texture offset (<c>textureoffset</c>) when built.</summary>
    public int TextureOffset { get; }

    public PlaneRef Bottom { get; }

    public PlaneRef Top { get; }

    /// <summary>The height of the texture's row 0 (see the class remarks).</summary>
    public PlaneRef TextureTop { get; }

    /// <summary>
    /// Whether the texture's bottom is pegged to a plane, so <see cref="TextureTop"/>'s
    /// offset includes <see cref="TextureHeight"/> (r_segs.c's <c>textureheight[] + rowoffset</c>
    /// cases: a lower-unpegged middle or masked middle, a pegged upper). A
    /// renderer whose texture changes at run time (switches, SPEC §12 T5.1)
    /// adds the new texture's height instead: <see cref="TextureTopFor"/>.
    /// </summary>
    public bool BottomPegged { get; }

    /// <summary>fixed_t: <see cref="TextureTop"/> for the current planes with a texture of <paramref name="textureHeight"/> (fixed_t) in place of this one.</summary>
    public int TextureTopFor(int textureHeight) =>
        TextureTop.Evaluate(FrontSector, BackSector) + (BottomPegged ? textureHeight - TextureHeight : 0);

    /// <summary>Whether the texture repeats vertically (all kinds but <see cref="WallSectionKind.MaskedMiddle"/>).</summary>
    public bool TilesVertically => Kind != WallSectionKind.MaskedMiddle;

    /// <summary>
    /// fixed_t: the drawn span for the sectors' current heights (bottom, top),
    /// clipped as the class remarks say (to the front sector's span, and for a
    /// masked middle also to the texture's rows); empty when <c>top ≤ bottom</c>.
    /// A missing texture (0) still has a span.
    /// </summary>
    public (int Bottom, int Top) Span()
    {
        int bottom = Bottom.Evaluate(FrontSector, BackSector);
        int top = Top.Evaluate(FrontSector, BackSector);
        bottom = Math.Max(bottom, FrontSector.FloorHeight);
        top = Math.Min(top, FrontSector.CeilingHeight);
        if (Kind == WallSectionKind.MaskedMiddle)
        {
            int texTop = TextureTop.Evaluate(FrontSector, BackSector);
            top = Math.Min(top, texTop);
            bottom = Math.Max(bottom, texTop - TextureHeight);
        }
        return (bottom, top);
    }

    public override string ToString() =>
        $"line {Line.Index} side {Side} {Kind}: tex {Texture}, {Bottom} .. {Top}, row 0 at {TextureTop}, x offset {TextureOffset >> Fixed.FRACBITS}";
}

/// <summary>A wall section that vanilla would draw at load time but whose texture is <c>-</c> (a hall of mirrors).</summary>
public readonly record struct MissingWallTexture(int Line, int Side, WallSectionKind Kind)
{
    public override string ToString() => $"line {Line} side {Side} {Kind}";
}

/// <summary>
/// The wall list of a level (SPEC §7.2 <i>Walls</i>): the upper, lower,
/// middle and masked middle sections of every linedef side, ported from
/// r_segs.c <c>R_StoreWallRange</c> (section choice and pegging) and
/// <c>R_RenderMaskedSegRange</c> (masked middle pegging).
/// <para>
/// Built <b>per linedef side</b>, not per seg, so the BSP's seg splits add no
/// vertices; a side no seg runs along is left out (vanilla never draws it).
/// Heights are <see cref="PlaneRef"/>s (a plane plus a constant), so a
/// renderer can move doors and lifts by changing sector heights alone. For
/// the same reason a two-sided side always gets an upper and a lower section,
/// even where they have no height at load time (a lift that lowers, stairs
/// that rise): vanilla's conditions (<c>worldhigh &lt; worldtop</c>,
/// <c>worldlow &gt; worldbottom</c>) are exactly "the evaluated span is not
/// empty". The exception is the sky rule, which depends on flats, not
/// heights: with an <c>F_SKY1</c> ceiling on both sides there is no upper
/// section (vanilla sets <c>worldtop = worldhigh</c>).
/// </para>
/// <para>
/// Integer only (fixed_t), so it stays in <c>IsoDoom.Map</c> under the
/// determinism scan (SPEC §12).
/// </para>
/// </summary>
public sealed class WallSections
{
    /// <summary>r_sky.h <c>SKYFLATNAME</c>: a ceiling with this flat is the sky.</summary>
    public const string SKYFLATNAME = "F_SKY1";

    private WallSections(WallSection[] sections, MissingWallTexture[] missing)
    {
        Sections = sections;
        Missing = missing;
    }

    /// <summary>All sections, in line order, then side (front, back), then upper, lower, middle/masked middle.</summary>
    public IReadOnlyList<WallSection> Sections { get; }

    /// <summary>
    /// The sections vanilla draws at load time (non-empty <see cref="WallSection.Span"/>)
    /// whose texture is <c>-</c>: vanilla shows a hall-of-mirrors effect there.
    /// Masked middles are never missing (<c>-</c> there means no masked middle).
    /// </summary>
    public IReadOnlyList<MissingWallTexture> Missing { get; }

    /// <summary>
    /// Builds the wall sections of <paramref name="level"/>, resolving texture
    /// names with <paramref name="textures"/>. As vanilla's
    /// <c>P_LoadSideDefs</c>, all three texture names of every side must exist
    /// (<c>-</c> is texture 0); an unknown name throws <see cref="WadFormatException"/>
    /// (vanilla's <c>I_Error</c> in <c>R_TextureNumForName</c>).
    /// </summary>
    public static WallSections Build(Level level, Textures textures)
    {
        var hasSeg = new bool[level.Lines.Length, 2];
        foreach (Seg seg in level.Segs)
            hasSeg[seg.LineDef.Index, seg.Side] = true;

        var sections = new List<WallSection>();
        var missing = new List<MissingWallTexture>();
        foreach (Line line in level.Lines)
        {
            for (int side = 0; side < 2; side++)
            {
                if (line.SideNum[side] == -1 || !hasSeg[line.Index, side])
                    continue;
                Side sidedef = level.Sides[line.SideNum[side]];
                Sector? back = null;
                if ((line.Flags & Line.ML_TWOSIDED) != 0 && line.SideNum[side ^ 1] != -1)
                    back = level.Sides[line.SideNum[side ^ 1]].Sector; // p_setup.c P_LoadSegs
                int top = TextureNum(textures, sidedef.TopTexture, sidedef);
                int bottom = TextureNum(textures, sidedef.BottomTexture, sidedef);
                int mid = TextureNum(textures, sidedef.MidTexture, sidedef);
                int first = sections.Count;
                R_StoreWallRange(line, side, sidedef, back, textures, top, bottom, mid, sections);
                for (int i = first; i < sections.Count; i++)
                {
                    WallSection s = sections[i];
                    if (s.Texture == 0 && s.Kind != WallSectionKind.MaskedMiddle)
                    {
                        (int b, int t) = s.Span();
                        if (t > b)
                            missing.Add(new MissingWallTexture(line.Index, side, s.Kind));
                    }
                }
            }
        }
        return new WallSections(sections.ToArray(), missing.ToArray());
    }

    private static int TextureNum(Textures textures, string name, Side side)
    {
        int num = textures.R_CheckTextureNumForName(name);
        if (num == -1)
            throw new WadFormatException($"R_TextureNumForName: {name} not found (sidedef {side.Index})");
        return num;
    }

    // r_segs.c R_StoreWallRange: which sections a side has, and where their
    // texture's row 0 sits (rw_midtexturemid, rw_toptexturemid,
    // rw_bottomtexturemid, made relative to world z instead of viewz). For the
    // masked middle, R_RenderMaskedSegRange's dc_texturemid.
    private static void R_StoreWallRange(Line linedef, int side, Side sidedef, Sector? backsector, Textures textures,
        int toptexture, int bottomtexture, int midtexture, List<WallSection> sections)
    {
        int rowoffset = sidedef.RowOffset;
        WallSection Section(WallSectionKind kind, int texnum, WallPlane bottom, WallPlane top, PlaneRef texturemid, bool bottomPegged = false) =>
            new(linedef, side, sidedef, backsector, kind, texnum, TextureHeight(textures, texnum),
                new PlaneRef(bottom, 0), new PlaneRef(top, 0), texturemid, bottomPegged);
        if (backsector is null)
        {
            // single sided line
            PlaneRef texturemid = (linedef.Flags & Line.ML_DONTPEGBOTTOM) != 0
                ? new PlaneRef(WallPlane.FrontFloor, TextureHeight(textures, midtexture) + rowoffset) // bottom of texture at floor
                : new PlaneRef(WallPlane.FrontCeiling, rowoffset); // top of texture at ceiling
            sections.Add(Section(WallSectionKind.Middle, midtexture, WallPlane.FrontFloor, WallPlane.FrontCeiling, texturemid,
                (linedef.Flags & Line.ML_DONTPEGBOTTOM) != 0));
            return;
        }

        // two sided line
        Sector frontsector = sidedef.Sector;
        // worldtop: the front ceiling, or the back ceiling when both are sky
        // ("hack to allow height changes in outdoor areas").
        bool sky = frontsector.CeilingPic == SKYFLATNAME && backsector.CeilingPic == SKYFLATNAME;
        WallPlane worldtop = sky ? WallPlane.BackCeiling : WallPlane.FrontCeiling;

        if (!sky)
        {
            // upper: worldhigh < worldtop
            PlaneRef toptexturemid = (linedef.Flags & Line.ML_DONTPEGTOP) != 0
                ? new PlaneRef(WallPlane.FrontCeiling, rowoffset) // top of texture at top
                : new PlaneRef(WallPlane.BackCeiling, TextureHeight(textures, toptexture) + rowoffset); // bottom of texture at bottom
            sections.Add(Section(WallSectionKind.Upper, toptexture, WallPlane.BackCeiling, WallPlane.FrontCeiling, toptexturemid,
                (linedef.Flags & Line.ML_DONTPEGTOP) == 0));
        }

        // lower: worldlow > worldbottom
        PlaneRef bottomtexturemid = (linedef.Flags & Line.ML_DONTPEGBOTTOM) != 0
            ? new PlaneRef(worldtop, rowoffset) // bottom of texture at bottom, i.e. aligned with the (world)top
            : new PlaneRef(WallPlane.BackFloor, rowoffset); // top of texture at top
        sections.Add(Section(WallSectionKind.Lower, bottomtexture, WallPlane.FrontFloor, WallPlane.BackFloor, bottomtexturemid));

        if (midtexture != 0)
        {
            // r_segs.c R_RenderMaskedSegRange
            PlaneRef maskedtexturemid = (linedef.Flags & Line.ML_DONTPEGBOTTOM) != 0
                ? new PlaneRef(WallPlane.HigherFloor, TextureHeight(textures, midtexture) + rowoffset)
                : new PlaneRef(WallPlane.LowerCeiling, rowoffset);
            sections.Add(Section(WallSectionKind.MaskedMiddle, midtexture, WallPlane.HigherFloor, WallPlane.LowerCeiling, maskedtexturemid,
                (linedef.Flags & Line.ML_DONTPEGBOTTOM) != 0));
        }
    }

    /// <summary>fixed_t: r_data.c <c>textureheight[]</c>.</summary>
    private static int TextureHeight(Textures textures, int texnum) =>
        texnum == 0 ? 0 : textures.TextureDefs[texnum].Height << Fixed.FRACBITS;
}
