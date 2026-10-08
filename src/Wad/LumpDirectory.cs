using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Wad;

/// <summary>What a lump holds, as the WAD viewer's lump list shows it (T1.6a).</summary>
public enum LumpKind
{
    /// <summary>A namespace marker (<c>S_START</c>, <c>F1_END</c>, …) or another zero-size <c>*_START</c>/<c>*_END</c> lump.</summary>
    Marker,

    /// <summary>A map header lump (<c>ExMy</c>, <c>MAPxx</c>, or any lump directly followed by <c>THINGS</c>).</summary>
    MapMarker,

    /// <summary>A map data lump (doomdata.h <c>ML_*</c>: <c>THINGS</c> … <c>BLOCKMAP</c>; also Hexen's <c>BEHAVIOR</c>).</summary>
    MapData,

    /// <summary>A patch-format graphic outside the namespaces (UI, menus, fonts; <see cref="GraphicLumps"/>).</summary>
    Graphic,

    /// <summary>A lump in the flat namespace.</summary>
    Flat,

    /// <summary>A lump in the sprite namespace.</summary>
    Sprite,

    /// <summary>A lump in the wall patch namespace.</summary>
    Patch,

    /// <summary>Wall texture definitions: <c>PNAMES</c>, <c>TEXTURE1</c>, <c>TEXTURE2</c>.</summary>
    TextureDefs,

    /// <summary><c>PLAYPAL</c>.</summary>
    Palette,

    /// <summary><c>COLORMAP</c>.</summary>
    Colormap,

    /// <summary>A sound effect: digitized <c>DS*</c> or PC speaker <c>DP*</c>.</summary>
    Sound,

    /// <summary>Music: <c>D_*</c>, or any global lump with a MUS or MIDI header.</summary>
    Music,

    /// <summary>Music instrument banks: <c>GENMIDI</c> (OPL), <c>DMXGUS</c>/<c>DMXGUSC</c> (Gravis Ultrasound).</summary>
    Instruments,

    /// <summary>A demo (<c>DEMO*</c>).</summary>
    Demo,

    /// <summary>The text-mode exit screen.</summary>
    Endoom,

    /// <summary>Anything else.</summary>
    Other,
}

/// <summary>
/// One row of the lump directory: the lump, its index in the merged archive
/// (<see cref="WadArchive.Lumps"/>), what it is, a short detail, and, when a
/// later file replaces it, the lump that does.
/// </summary>
public sealed record LumpEntry(int Index, WadLump Lump, LumpKind Kind, string Detail, WadLump? OverriddenBy, string? MapName)
{
    /// <summary>True when a later lump replaces this one, so the game never uses it.</summary>
    public bool IsOverridden => OverriddenBy is not null;

    /// <summary>A patch, flat or sprite: something the graphic view can show.</summary>
    public bool IsGraphic => Kind is LumpKind.Graphic or LumpKind.Flat or LumpKind.Sprite or LumpKind.Patch;
}

/// <summary>
/// Classifies every lump of a <see cref="WadArchive"/>, markers, duplicates and
/// overridden lumps included, for the WAD viewer's lump list (T1.6a). Plain C#,
/// no Godot types.
/// <para>
/// Namespaced lumps are classified by their namespace; global lumps by name
/// (the names vanilla loads them by: <c>PLAYPAL</c>, <c>D_*</c>, <c>DS*</c>, …,
/// the same list <see cref="GraphicLumps.IsNonGraphicName"/> uses), then by
/// header (MUS/MIDI music), then by structure (<see cref="Graphics.Patch.IsPatch"/>),
/// so <see cref="LumpKind.Graphic"/> holds exactly the lumps of
/// <see cref="GraphicLumps.FindGlobalPatches"/> plus the ones a later lump overrides.
/// </para>
/// <para>
/// Overriding follows the archive's lookups: a namespaced lump is overridden
/// when the merged namespace holds another lump of that name
/// (<see cref="WadArchive.Find(string, LumpNamespace)"/>), a global lump when a
/// later global lump has its name, and a map's data lumps when a later map of the
/// same name exists (they then point at that map's header). Markers are never
/// overridden.
/// </para>
/// </summary>
public static class LumpDirectory
{
    // doomdata.h ML_* order after the header; BEHAVIOR is Hexen-format maps' extra lump.
    private static readonly string[] _mapLumpNames =
    [
        "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
        "SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP", "BEHAVIOR",
    ];

    /// <summary>Display name of a kind ("map data", "sound", …).</summary>
    public static string KindName(LumpKind kind) => kind switch
    {
        LumpKind.Marker => "marker",
        LumpKind.MapMarker => "map",
        LumpKind.MapData => "map data",
        LumpKind.Graphic => "graphic",
        LumpKind.Flat => "flat",
        LumpKind.Sprite => "sprite",
        LumpKind.Patch => "wall patch",
        LumpKind.TextureDefs => "texture lump",
        LumpKind.Palette => "palette",
        LumpKind.Colormap => "colormap",
        LumpKind.Sound => "sound",
        LumpKind.Music => "music",
        LumpKind.Instruments => "instruments",
        LumpKind.Demo => "demo",
        LumpKind.Endoom => "ENDOOM",
        LumpKind.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Display name of a namespace ("global", "sprites", …).</summary>
    public static string NamespaceName(LumpNamespace ns) => ns switch
    {
        LumpNamespace.Global => "global",
        LumpNamespace.Sprites => "sprites",
        LumpNamespace.Flats => "flats",
        LumpNamespace.Patches => "patches",
        _ => throw new ArgumentOutOfRangeException(nameof(ns)),
    };

    /// <summary>True for the map data lump names (<c>THINGS</c> … <c>BLOCKMAP</c>, <c>BEHAVIOR</c>).</summary>
    public static bool IsMapLumpName(string name) => Array.IndexOf(_mapLumpNames, name) >= 0;

    /// <summary>True for <c>ExMy</c> and <c>MAPxx</c> (digits only).</summary>
    public static bool IsMapName(string name) =>
        (name.Length == 4 && name[0] == 'E' && name[2] == 'M' && char.IsAsciiDigit(name[1]) && char.IsAsciiDigit(name[3]))
        || (name.Length == 5 && name.StartsWith("MAP", StringComparison.Ordinal) && char.IsAsciiDigit(name[3]) && char.IsAsciiDigit(name[4]));

    /// <summary>
    /// Classifies one lump. <paramref name="nextName"/> is the name of the next
    /// lump in the same file (or null), which marks a map header as in
    /// Chocolate Doom/Boom map detection: a global lump followed by <c>THINGS</c>.
    /// Returns the kind and a short detail (music format, sound type, …).
    /// </summary>
    public static (LumpKind Kind, string Detail) Classify(WadLump lump, string? nextName)
    {
        string name = lump.Name;
        ReadOnlySpan<byte> data = lump.Data.Span;
        if (lump.IsMarker)
            return (LumpKind.Marker, "");
        switch (lump.Namespace)
        {
            case LumpNamespace.Sprites: return (LumpKind.Sprite, "");
            case LumpNamespace.Flats: return (LumpKind.Flat, lump.Size >= Flat.Size ? "" : "too short for a flat");
            case LumpNamespace.Patches: return (LumpKind.Patch, "");
        }

        if (IsMapName(name) || nextName == "THINGS")
            return (LumpKind.MapMarker, "");
        if (IsMapLumpName(name))
            return (LumpKind.MapData, "");
        switch (name)
        {
            case "PLAYPAL": return (LumpKind.Palette, $"{lump.Size / Playpal.PaletteSize} palettes");
            case "COLORMAP": return (LumpKind.Colormap, $"{lump.Size / Colormap.MapSize} maps");
            case "ENDOOM": return (LumpKind.Endoom, "80x25 text screen");
            case "PNAMES": case "TEXTURE1": case "TEXTURE2": return (LumpKind.TextureDefs, "");
            case "GENMIDI": return (LumpKind.Instruments, InstrumentsDetail(data));
        }
        if (name.StartsWith("DMXGUS", StringComparison.Ordinal))
            return (LumpKind.Instruments, "Gravis Ultrasound patch map");
        if (name.StartsWith("DEMO", StringComparison.Ordinal))
            return (LumpKind.Demo, "");
        if (name.StartsWith("D_", StringComparison.Ordinal))
            return (LumpKind.Music, MusicDetail(data) ?? "unknown format");
        if (name.StartsWith("DS", StringComparison.Ordinal))
            return (LumpKind.Sound, DigitizedSoundDetail(data));
        if (name.StartsWith("DP", StringComparison.Ordinal))
            return (LumpKind.Sound, "PC speaker");
        if (lump.Size == 0 && (name.EndsWith("_START", StringComparison.Ordinal) || name.EndsWith("_END", StringComparison.Ordinal)))
            return (LumpKind.Marker, "outside its namespace");
        if (MusicDetail(data) is string format)
            return (LumpKind.Music, format);
        if (lump.Size > 0 && !GraphicLumps.IsNonGraphicName(name) && Graphics.Patch.IsPatch(data))
            return (LumpKind.Graphic, "");
        return (LumpKind.Other, lump.Size == 0 ? "empty" : "");
    }

    /// <summary>"MUS" or "MIDI" from the lump header, or null.</summary>
    public static string? MusicFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4 && data[0] == 'M' && data[1] == 'U' && data[2] == 'S' && data[3] == 0x1A)
            return "MUS";
        if (data.Length >= 4 && data[0] == 'M' && data[1] == 'T' && data[2] == 'h' && data[3] == 'd')
            return "MIDI";
        return null;
    }

    // T7.8b: a MUS song's events, length and channels (or why it cannot be read); a MIDI file's format only.
    private static string? MusicDetail(ReadOnlySpan<byte> data)
    {
        if (!MusSong.HasHeader(data))
            return MusicFormat(data);
        return MusSong.TryRead(data, out string? error) is MusSong song ? song.Summary : $"MUS, not readable: {error}";
    }

    // T7.8b: the OPL bank's instruments (or why i_oplmusic.c could not read it).
    private static string InstrumentsDetail(ReadOnlySpan<byte> data) =>
        Genmidi.TryRead(data, out string? error) is Genmidi bank ? bank.Summary : $"OPL instruments, not readable: {error}";

    // DMX digitized sound header: format 3, sample rate, sample count (8 bytes).
    private static string DigitizedSoundDetail(ReadOnlySpan<byte> data)
    {
        if (!DmxSound.HasHeader(data))
            return "digitized (unknown header)";
        // T7.7: what plays (DMX's pads left out), or why DMX would not play it
        if (DmxSound.TryDecode(data, out string? error) is DmxSound sound)
            return $"digitized, {sound.SampleRate} Hz, {sound.Samples.Length} samples, {sound.Seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} s";
        return $"digitized, {BinaryPrimitives.ReadUInt16LittleEndian(data[2..])} Hz, not playable: {error}";
    }

    /// <summary>Every lump of the archive, in load order, classified.</summary>
    public static IReadOnlyList<LumpEntry> Build(WadArchive wad)
    {
        var entries = new LumpEntry[wad.NumLumps];

        // Map header lumps whose name a later map header reuses, by archive index.
        var lastMapByName = new Dictionary<string, int>(StringComparer.Ordinal);
        // The last global lump of each name (WadArchive.Find(name, Global), precomputed).
        var lastGlobalByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var kinds = new (LumpKind Kind, string Detail)[wad.NumLumps];
        int index = 0;
        foreach (WadFile file in wad.Files)
        {
            for (int i = 0; i < file.Lumps.Count; i++, index++)
            {
                WadLump lump = file.Lumps[i];
                kinds[index] = Classify(lump, i + 1 < file.Lumps.Count ? file.Lumps[i + 1].Name : null);
                if (kinds[index].Kind == LumpKind.MapMarker)
                    lastMapByName[lump.Name] = index;
                if (lump.Namespace == LumpNamespace.Global && !lump.IsMarker)
                    lastGlobalByName[lump.Name] = index;
            }
        }

        int currentMap = -1; // map header of the data lumps being walked
        WadFile? currentFile = null;
        for (int i = 0; i < wad.NumLumps; i++)
        {
            WadLump lump = wad.Lumps[i];
            (LumpKind kind, string detail) = kinds[i];
            if (lump.File != currentFile)
            {
                currentFile = lump.File;
                currentMap = -1;
            }
            string? mapName = null;
            WadLump? overriddenBy = null;
            switch (kind)
            {
                case LumpKind.Marker:
                    break;
                case LumpKind.MapMarker:
                    currentMap = i;
                    mapName = lump.Name;
                    overriddenBy = MapOverride(wad, lastMapByName, i);
                    break;
                case LumpKind.MapData:
                    // Data lumps belong to their map; a stray one (no header before it) overrides nothing.
                    if (currentMap >= 0)
                    {
                        mapName = wad.Lumps[currentMap].Name;
                        overriddenBy = MapOverride(wad, lastMapByName, currentMap);
                    }
                    break;
                default:
                    WadLump? winner = lump.Namespace == LumpNamespace.Global
                        ? wad.Lumps[lastGlobalByName[lump.Name]]
                        : wad.Find(lump.Name, lump.Namespace);
                    if (winner is not null && !ReferenceEquals(winner, lump))
                        overriddenBy = winner;
                    break;
            }
            if (kind != LumpKind.MapMarker && kind != LumpKind.MapData)
                currentMap = -1;
            if (mapName is not null && kind == LumpKind.MapData)
                detail = $"of {mapName}";
            entries[i] = new LumpEntry(i, lump, kind, detail, overriddenBy, mapName);
        }
        return entries;
    }

    private static WadLump? MapOverride(WadArchive wad, Dictionary<string, int> lastMapByName, int header)
    {
        int last = lastMapByName[wad.Lumps[header].Name];
        return last == header ? null : wad.Lumps[last];
    }
}
