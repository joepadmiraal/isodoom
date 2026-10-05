using System;
using System.Collections.Generic;

namespace IsoDoom.Wad.Graphics;

/// <summary>
/// One frame of a sprite (r_defs.h <c>spriteframe_t</c>). When
/// <see cref="Rotate"/> is false the frame looks the same from every angle and
/// all eight <see cref="Lump"/> entries hold the same rotation-0 lump. When it
/// is true, entry <c>r</c> is the view from rotation <c>r + 1</c> (1 = front,
/// counter-clockwise in 45° steps, as in the lump names). <see cref="Flip"/>
/// marks the second half of a mirrored pair (the <c>A8</c> of <c>TROOA2A8</c>),
/// which is drawn mirrored horizontally. Lumps are indices into
/// <see cref="WadArchive.Lumps"/> (vanilla stores <c>lump - firstspritelump</c>).
/// </summary>
public sealed class SpriteFrame
{
    internal SpriteFrame(bool rotate, int[] lump, bool[] flip)
    {
        Rotate = rotate;
        Lump = lump;
        Flip = flip;
    }

    public bool Rotate { get; }
    public IReadOnlyList<int> Lump { get; }
    public IReadOnlyList<bool> Flip { get; }
}

/// <summary>A sprite and its frames A, B, … (r_defs.h <c>spritedef_t</c>).</summary>
public sealed class SpriteDef
{
    internal SpriteDef(string name, SpriteFrame[] frames)
    {
        Name = name;
        Frames = frames;
    }

    /// <summary>The four-letter sprite name, e.g. <c>TROO</c>.</summary>
    public string Name { get; }

    /// <summary>r_defs.h <c>numframes</c>; 0 when the WAD has no lumps for this sprite.</summary>
    public int NumFrames => Frames.Count;

    /// <summary>r_defs.h <c>spriteframes</c>; index 0 is frame A.</summary>
    public IReadOnlyList<SpriteFrame> Frames { get; }
}

/// <summary>
/// The sprite index: the sprite namespace's lumps grouped into sprites, frames
/// and rotations, as r_things.c <c>R_InitSpriteDefs</c> does.
/// <para>
/// Sprite lump names are <c>NNNNFR</c> or <c>NNNNFRFR</c>: a four-letter
/// sprite name, a frame letter (<c>A</c> = frame 0, up to 29 frames) and a
/// rotation digit (<c>0</c> = all angles, <c>1</c>–<c>8</c> = one angle). The
/// optional second frame/rotation pair reuses the same patch mirrored, so
/// <c>TROOA2A8</c> is rotation 2 of frame A and, flipped, rotation 8.
/// </para>
/// <para>
/// PWAD sprites replace IWAD sprites per frame and rotation, not per lump
/// (Chocolate Doom's w_merge.c intent): each file's lumps are installed into a
/// table of their own with vanilla's checks, then laid over the result of the
/// earlier files slot by slot. A PWAD <c>TROOA2</c> therefore takes over
/// rotation 2 of frame A while rotation 8 still comes, mirrored, from the
/// IWAD's <c>TROOA2A8</c>.
/// </para>
/// </summary>
public sealed class Sprites
{
    /// <summary>r_things.c: frames A to ']' (vanilla's <c>sprtemp[29]</c>).</summary>
    public const int MaxFrames = 29;

    private readonly SpriteDef[] _sprites;
    private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);

    private Sprites(SpriteDef[] sprites)
    {
        _sprites = sprites;
        for (int i = 0; i < sprites.Length; i++)
            _indexByName.TryAdd(sprites[i].Name, i);
    }

    /// <summary>r_things.c <c>sprites[]</c>, in the order of the name list.</summary>
    public IReadOnlyList<SpriteDef> SpriteDefs => _sprites;

    /// <summary>r_things.c <c>numsprites</c>.</summary>
    public int NumSprites => _sprites.Length;

    /// <summary>The sprite called <paramref name="name"/> (case-insensitive), or null if it is not in the name list.</summary>
    public SpriteDef? Find(string name) =>
        _indexByName.TryGetValue(name.ToUpperInvariant(), out int i) ? _sprites[i] : null;

    /// <summary>
    /// r_things.c <c>R_InitSprites</c>/<c>R_InitSpriteDefs</c>: builds a
    /// <see cref="SpriteDef"/> for every name in <paramref name="namelist"/>
    /// (default: info.c <see cref="SpriteNames.sprnames"/>). Sprite lumps whose
    /// name is not in the list are ignored, as in vanilla. Throws
    /// <see cref="WadFormatException"/> where vanilla calls <c>I_Error</c>:
    /// a bad frame or rotation character, two lumps for one rotation in the
    /// same file, a rotation-0 lump mixed with rotations in the same file, a
    /// missing frame before the last one, or a rotated frame missing rotations.
    /// </summary>
    public static Sprites R_InitSprites(WadArchive wad, IReadOnlyList<string>? namelist = null)
    {
        namelist ??= SpriteNames.sprnames;

        // The sprite lumps of each file, in directory order. A name repeated in
        // one file collapses to its last copy (as WadArchive's namespace merge does).
        List<List<int>> perFile = new();
        Dictionary<WadFile, int> fileIndex = new();
        for (int i = 0; i < wad.Files.Count; i++)
        {
            fileIndex[wad.Files[i]] = i;
            perFile.Add(new List<int>());
        }
        for (int l = 0; l < wad.NumLumps; l++)
        {
            WadLump lump = wad.Lumps[l];
            if (lump.Namespace != LumpNamespace.Sprites || lump.IsMarker)
                continue;
            List<int> list = perFile[fileIndex[lump.File]];
            int dup = list.FindIndex(x => wad.Lumps[x].Name == lump.Name);
            if (dup >= 0)
                list.RemoveAt(dup);
            list.Add(l);
        }

        SpriteDef[] sprites = new SpriteDef[namelist.Count];
        for (int i = 0; i < namelist.Count; i++)
            sprites[i] = R_InitSpriteDef(wad, WadFile.NormalizeName(namelist[i]), perFile);
        return new Sprites(sprites);
    }

    /// <summary>One iteration of r_things.c <c>R_InitSpriteDefs</c>'s sprite loop.</summary>
    private static SpriteDef R_InitSpriteDef(WadArchive wad, string spritename, List<List<int>> perFile)
    {
        // The merged result: per frame and rotation slot, the lump, its flip
        // and whether it came from a rotation lump (1-8) rather than rotation 0.
        int[,] lump = new int[MaxFrames, 8];
        bool[,] flip = new bool[MaxFrames, 8];
        bool[,] fromRotation = new bool[MaxFrames, 8];
        for (int f = 0; f < MaxFrames; f++)
            for (int r = 0; r < 8; r++)
                lump[f, r] = -1;

        foreach (List<int> fileLumps in perFile)
        {
            SpriteTemp[]? sprtemp = null;
            foreach (int l in fileLumps)
            {
                string name = wad.Lumps[l].Name;
                if (name.Length < 4 || string.CompareOrdinal(name, 0, spritename, 0, 4) != 0)
                    continue;
                if (name.Length != 6 && name.Length != 8)
                    throw new WadFormatException($"R_InstallSpriteLump: Bad frame characters in lump {name}");

                sprtemp ??= SpriteTemp.NewTable();
                R_InstallSpriteLump(sprtemp, spritename, name, l, name[4] - 'A', name[5] - '0', false);
                if (name.Length == 8)
                    R_InstallSpriteLump(sprtemp, spritename, name, l, name[6] - 'A', name[7] - '0', true);
            }
            if (sprtemp is null)
                continue;

            // Lay this file's slots over the earlier files'.
            for (int f = 0; f < MaxFrames; f++)
            {
                for (int r = 0; r < 8; r++)
                {
                    if (sprtemp[f].Lump[r] == -1)
                        continue;
                    lump[f, r] = sprtemp[f].Lump[r];
                    flip[f, r] = sprtemp[f].Flip[r];
                    fromRotation[f, r] = sprtemp[f].Rotate == 1;
                }
            }
        }

        int maxframe = -1;
        for (int f = 0; f < MaxFrames; f++)
            for (int r = 0; r < 8; r++)
                if (lump[f, r] != -1)
                    maxframe = f;

        // Check the frames that were found for completeness (r_things.c).
        SpriteFrame[] frames = new SpriteFrame[maxframe + 1];
        for (int f = 0; f <= maxframe; f++)
        {
            bool any = false, rotate = false, complete = true;
            for (int r = 0; r < 8; r++)
            {
                any |= lump[f, r] != -1;
                rotate |= lump[f, r] != -1 && fromRotation[f, r];
                complete &= lump[f, r] != -1;
            }
            if (!any)
                throw new WadFormatException($"R_InitSprites: No patches found for {spritename} frame {(char)('A' + f)}");
            if (!complete)
                throw new WadFormatException($"R_InitSprites: Sprite {spritename} frame {(char)('A' + f)} is missing rotations");

            int[] frameLumps = new int[8];
            bool[] frameFlips = new bool[8];
            for (int r = 0; r < 8; r++)
            {
                frameLumps[r] = lump[f, r];
                frameFlips[r] = flip[f, r];
            }
            frames[f] = new SpriteFrame(rotate, frameLumps, frameFlips);
        }
        return new SpriteDef(spritename, frames);
    }

    /// <summary>r_things.c (installs one frame/rotation of a lump into one file's table).</summary>
    private static void R_InstallSpriteLump(SpriteTemp[] sprtemp, string spritename, string lumpName, int lump, int frame, int rotation, bool flipped)
    {
        if (frame < 0 || frame >= MaxFrames || rotation < 0 || rotation > 8)
            throw new WadFormatException($"R_InstallSpriteLump: Bad frame characters in lump {lumpName}");

        char frameChar = (char)('A' + frame);
        SpriteTemp t = sprtemp[frame];
        if (rotation == 0)
        {
            // The lump should be used for all rotations.
            if (t.Rotate == 0)
                throw new WadFormatException($"R_InitSprites: Sprite {spritename} frame {frameChar} has multip rot=0 lump");
            if (t.Rotate == 1)
                throw new WadFormatException($"R_InitSprites: Sprite {spritename} frame {frameChar} has rotations and a rot=0 lump");

            t.Rotate = 0;
            for (int r = 0; r < 8; r++)
            {
                t.Lump[r] = lump;
                t.Flip[r] = flipped;
            }
            return;
        }

        // The lump is only used for one rotation.
        if (t.Rotate == 0)
            throw new WadFormatException($"R_InitSprites: Sprite {spritename} frame {frameChar} has rotations and a rot=0 lump");

        t.Rotate = 1;
        rotation--; // make 0 based
        if (t.Lump[rotation] != -1)
            throw new WadFormatException($"R_InitSprites: Sprite {spritename} : {frameChar} : {(char)('1' + rotation)} has two lumps mapped to it");

        t.Lump[rotation] = lump;
        t.Flip[rotation] = flipped;
    }

    /// <summary>r_things.c <c>sprtemp[]</c> entry; <see cref="Rotate"/> is -1 (unset), 0 or 1 as in vanilla.</summary>
    private sealed class SpriteTemp
    {
        public int Rotate = -1;
        public readonly int[] Lump = { -1, -1, -1, -1, -1, -1, -1, -1 };
        public readonly bool[] Flip = new bool[8];

        public static SpriteTemp[] NewTable()
        {
            SpriteTemp[] table = new SpriteTemp[MaxFrames];
            for (int i = 0; i < table.Length; i++)
                table[i] = new SpriteTemp();
            return table;
        }
    }
}
