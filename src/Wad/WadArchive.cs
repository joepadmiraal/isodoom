using System;
using System.Collections.Generic;
using System.Linq;

namespace IsoDoom.Wad;

/// <summary>
/// The merged lump directory of an IWAD plus any PWADs, in load order
/// (w_wad.c: <c>lumpinfo</c>, <c>W_InitMultipleFiles</c>).
/// <para>
/// Global lookups by name follow vanilla: the directory is searched from the
/// end, so a lump in a later file overrides one with the same name in an
/// earlier file. Lumps inside namespace markers are also merged per namespace,
/// as Chocolate Doom's w_merge.c does: a later lump with the same name in the
/// same namespace replaces the earlier one in place (keeping animation ranges
/// such as <c>NUKAGE1</c>..<c>NUKAGE3</c> intact), new names are appended.
/// </para>
/// </summary>
public sealed class WadArchive
{
    private readonly WadFile[] _files;
    private readonly WadLump[] _lumps;
    private readonly Dictionary<string, int> _lastByName = new(StringComparer.Ordinal);
    private readonly NamespaceList[] _namespaces;

    public WadArchive(IEnumerable<WadFile> files)
    {
        _files = [.. files];
        if (_files.Length == 0)
            throw new ArgumentException("At least one WAD is required.", nameof(files));

        _lumps = [.. _files.SelectMany(f => f.Lumps)];
        for (int i = 0; i < _lumps.Length; i++)
            _lastByName[_lumps[i].Name] = i;

        _namespaces = new NamespaceList[Enum.GetValues<LumpNamespace>().Length];
        for (int i = 0; i < _namespaces.Length; i++)
            _namespaces[i] = new NamespaceList();
        foreach (WadLump lump in _lumps)
        {
            if (lump.Namespace != LumpNamespace.Global && !lump.IsMarker)
                _namespaces[(int)lump.Namespace].Add(lump);
        }
    }

    /// <summary>Opens an IWAD and merges the PWADs over it, in order.</summary>
    public static WadArchive Open(string iwadPath, params string[] pwadPaths) =>
        new(new[] { iwadPath }.Concat(pwadPaths).Select(WadFile.Open));

    /// <summary>The files in load order; the first is normally the IWAD.</summary>
    public IReadOnlyList<WadFile> Files => _files;

    /// <summary>Every lump of every file in load order, markers and duplicates included.</summary>
    public IReadOnlyList<WadLump> Lumps => _lumps;

    /// <summary>w_wad.c: <c>numlumps</c>.</summary>
    public int NumLumps => _lumps.Length;

    /// <summary>
    /// Index of the last lump called <paramref name="name"/>, or -1 (w_wad.c).
    /// Case-insensitive; names longer than 8 characters are cut to 8, as vanilla does.
    /// </summary>
    public int W_CheckNumForName(string name) =>
        _lastByName.TryGetValue(WadFile.NormalizeName(name), out int index) ? index : -1;

    /// <summary>Like <see cref="W_CheckNumForName"/>, but a missing lump is an error (w_wad.c).</summary>
    public int W_GetNumForName(string name)
    {
        int index = W_CheckNumForName(name);
        if (index < 0)
            throw new KeyNotFoundException($"W_GetNumForName: {name} not found!");
        return index;
    }

    /// <summary>w_wad.c.</summary>
    public int W_LumpLength(int lump) => _lumps[lump].Size;

    /// <summary>The lump's bytes (w_wad.c <c>W_CacheLumpNum</c>; no copy, no caching needed).</summary>
    public ReadOnlyMemory<byte> W_CacheLumpNum(int lump) => _lumps[lump].Data;

    /// <summary>w_wad.c.</summary>
    public ReadOnlyMemory<byte> W_CacheLumpName(string name) => W_CacheLumpNum(W_GetNumForName(name));

    /// <summary>The last lump called <paramref name="name"/> in any namespace, or null.</summary>
    public WadLump? Find(string name)
    {
        int index = W_CheckNumForName(name);
        return index < 0 ? null : _lumps[index];
    }

    /// <summary>The lump called <paramref name="name"/> in the merged namespace, or null.</summary>
    public WadLump? Find(string name, LumpNamespace ns)
    {
        if (ns == LumpNamespace.Global)
        {
            // The last global lump of that name (vanilla order).
            string key = WadFile.NormalizeName(name);
            for (int i = _lumps.Length - 1; i >= 0; i--)
            {
                if (_lumps[i].Namespace == LumpNamespace.Global && _lumps[i].Name == key)
                    return _lumps[i];
            }
            return null;
        }
        return _namespaces[(int)ns].Find(WadFile.NormalizeName(name));
    }

    /// <summary>
    /// The merged contents of a marker namespace, in order, without marker lumps.
    /// Replaces vanilla's <c>firstflat</c>..<c>lastflat</c> and
    /// <c>firstspritelump</c>..<c>lastspritelump</c> ranges (r_data.c).
    /// </summary>
    public IReadOnlyList<WadLump> GetNamespace(LumpNamespace ns)
    {
        if (ns == LumpNamespace.Global)
            throw new ArgumentException("The global namespace is not a marker namespace; use Lumps.", nameof(ns));
        return _namespaces[(int)ns].Lumps;
    }

    private sealed class NamespaceList
    {
        private readonly List<WadLump> _lumps = [];
        private readonly Dictionary<string, int> _indexByName = new(StringComparer.Ordinal);

        public IReadOnlyList<WadLump> Lumps => _lumps;

        public void Add(WadLump lump)
        {
            if (_indexByName.TryGetValue(lump.Name, out int index))
            {
                _lumps[index] = lump;
            }
            else
            {
                _indexByName[lump.Name] = _lumps.Count;
                _lumps.Add(lump);
            }
        }

        public WadLump? Find(string name) =>
            _indexByName.TryGetValue(name, out int index) ? _lumps[index] : null;
    }
}
