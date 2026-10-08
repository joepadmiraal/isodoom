using System;
using System.Collections.Generic;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Sim;

/// <summary>
/// p_spec.c <c>animdef_t</c>: source animation definition, a sequence from
/// <see cref="startname"/> to <see cref="endname"/> (every texture or flat
/// between them in WAD order) showing each frame <see cref="speed"/> tics.
/// </summary>
public readonly record struct animdef_t(bool istexture, string endname, string startname, int speed);

/// <summary>
/// p_spec.c <c>anim_t</c>: an animation found in the WAD
/// (<see cref="World.P_InitPicAnims"/>): texture or flat numbers
/// <see cref="basepic"/> to <see cref="picnum"/>.
/// </summary>
public sealed record anim_t(bool istexture, int picnum, int basepic, int numpics, int speed);

/// <summary>
/// p_spec.c's animated textures and flats (T5.7): the <c>animdefs</c> table
/// and <c>P_InitPicAnims</c>' lookup, which both the sim
/// (<see cref="World.P_InitPicAnims"/>, the translation tables) and the
/// presentation (every frame of a sequence the level draws goes in its atlas)
/// use.
/// </summary>
public static class PicAnims
{
    /// <summary>p_spec.c <c>MAXANIMS</c>.</summary>
    public const int MAXANIMS = 32;

    /// <summary>
    /// p_spec.c <c>animdefs</c>: Floor/ceiling animation sequences, defined by
    /// first and last frame, i.e. the flat (64x64 tile) name to be used. The
    /// full animation sequence is given using all the flats between the start
    /// and end entry, in the order found in the WAD file. (Vanilla's
    /// terminating entry left out.)
    /// </summary>
    public static readonly animdef_t[] animdefs =
    [
        new(false, "NUKAGE3", "NUKAGE1", 8),
        new(false, "FWATER4", "FWATER1", 8),
        new(false, "SWATER4", "SWATER1", 8),
        new(false, "LAVA4", "LAVA1", 8),
        new(false, "BLOOD3", "BLOOD1", 8),

        // DOOM II flat animations.
        new(false, "RROCK08", "RROCK05", 8),
        new(false, "SLIME04", "SLIME01", 8),
        new(false, "SLIME08", "SLIME05", 8),
        new(false, "SLIME12", "SLIME09", 8),

        new(true, "BLODGR4", "BLODGR1", 8),
        new(true, "SLADRIP3", "SLADRIP1", 8),

        new(true, "BLODRIP4", "BLODRIP1", 8),
        new(true, "FIREWALL", "FIREWALA", 8),
        new(true, "GSTFONT3", "GSTFONT1", 8),
        new(true, "FIRELAVA", "FIRELAV3", 8),
        new(true, "FIREMAG3", "FIREMAG1", 8),
        new(true, "FIREBLU2", "FIREBLU1", 8),
        new(true, "ROCKRED3", "ROCKRED1", 8),

        new(true, "BFALL4", "BFALL1", 8),
        new(true, "SFALL4", "SFALL1", 8),
        new(true, "WFALL4", "WFALL1", 8),
        new(true, "DBRAIN4", "DBRAIN1", 8),
    ];

    /// <summary>
    /// p_spec.c <c>P_InitPicAnims</c>' lookup: the <see cref="animdefs"/>
    /// whose start the WAD has (a texture of <paramref name="textures"/>, none
    /// when null; a flat of <paramref name="flats"/>, <see cref="FlatNames"/>:
    /// vanilla's <c>firstflat</c>..<c>lastflat</c>), as texture or flat numbers. As vanilla, a missing end or a
    /// cycle of fewer than two frames is an error (<see cref="WadFormatException"/>).
    /// </summary>
    public static List<anim_t> Resolve(Textures? textures, IReadOnlyList<string> flats)
    {
        var anims = new List<anim_t>();
        foreach (animdef_t def in animdefs)
        {
            int picnum, basepic;
            if (def.istexture)
            {
                // different episode ?
                if (textures is null || textures.R_CheckTextureNumForName(def.startname) == -1)
                    continue;

                picnum = textures.R_CheckTextureNumForName(def.endname);
                if (picnum == -1)
                    throw new WadFormatException($"R_TextureNumForName: {def.endname} not found");
                basepic = textures.R_CheckTextureNumForName(def.startname);
            }
            else
            {
                if (FlatNum(flats, def.startname) == -1)
                    continue;

                picnum = FlatNum(flats, def.endname);
                if (picnum == -1)
                    throw new WadFormatException($"R_FlatNumForName: {def.endname} not found");
                basepic = FlatNum(flats, def.startname);
            }

            int numpics = picnum - basepic + 1;
            if (numpics < 2)
                throw new WadFormatException($"P_InitPicAnims: bad cycle from {def.startname} to {def.endname}");

            anims.Add(new anim_t(def.istexture, picnum, basepic, numpics, def.speed));
        }
        return anims;
    }

    /// <summary>
    /// The frames of every animation <see cref="Resolve"/> finds, by name, in
    /// order: the texture sequences, then the flat sequences (for the
    /// presentation's atlas groups).
    /// </summary>
    public static (List<string[]> Textures, List<string[]> Flats) Sequences(Textures? textures, IReadOnlyList<string> flats)
    {
        var tex = new List<string[]>();
        var flat = new List<string[]>();
        foreach (anim_t anim in Resolve(textures, flats))
        {
            string[] names = new string[anim.numpics];
            for (int i = 0; i < anim.numpics; i++)
                names[i] = anim.istexture ? textures!.TextureDefs[anim.basepic + i].Name : flats[anim.basepic + i];
            (anim.istexture ? tex : flat).Add(names);
        }
        return (tex, flat);
    }

    /// <summary>
    /// r_data.c <c>R_FlatNumForName</c> without the error: the flat's number
    /// in <paramref name="flats"/> (the last of that name, as
    /// <c>W_CheckNumForName</c> finds the last lump), or -1.
    /// </summary>
    public static int FlatNum(IReadOnlyList<string> flats, string name)
    {
        for (int i = flats.Count - 1; i >= 0; i--)
        {
            if (string.Equals(flats[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// The flats of <paramref name="wad"/> by name, numbered as vanilla's
    /// <c>R_InitFlats</c> numbers them (<c>firstflat</c>..<c>lastflat</c>):
    /// every lump between the last <c>F_START</c> and the last <c>F_END</c>,
    /// inner markers (<c>F1_START</c>, …) included, since an animation's phase
    /// depends on its frames' absolute numbers (<c>P_UpdateSpecials</c>); then
    /// the flats of the merged flat namespace outside that range (a PWAD's,
    /// which vanilla does not merge), in order.
    /// </summary>
    public static List<string> FlatNames(WadArchive wad)
    {
        int start = -1, end = -1;
        for (int i = 0; i < wad.Lumps.Count; i++)
        {
            string name = wad.Lumps[i].Name;
            if (name == "F_START")
                start = i;
            else if (name == "F_END")
                end = i;
        }
        var names = new List<string>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // lookups only (Sim invariants)
        for (int i = start + 1; start >= 0 && i < end; i++)
        {
            names.Add(wad.Lumps[i].Name);
            known.Add(wad.Lumps[i].Name);
        }
        foreach (WadLump lump in wad.GetNamespace(LumpNamespace.Flats))
        {
            if (known.Add(lump.Name))
                names.Add(lump.Name);
        }
        return names;
    }
}
