using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IsoDoom.Wad;

/// <summary>The four-byte identification at the start of a WAD.</summary>
public enum WadType
{
    Iwad,
    Pwad,
}

/// <summary>
/// One entry of a WAD's lump directory (w_wad.c: <c>filelump_t</c>), plus the
/// namespace it sits in. Marker lumps (<c>S_START</c>, <c>P1_END</c>, …) are
/// <see cref="IsMarker"/> and carry the namespace they open or close.
/// </summary>
public sealed record WadLump(WadFile File, int Index, string Name, int Position, int Size, LumpNamespace Namespace, bool IsMarker)
{
    /// <summary>The lump's bytes, a view into the file's buffer.</summary>
    public ReadOnlyMemory<byte> Data => File.Data.Slice(Position, Size);

    public override string ToString() => $"{Name} ({File.Name} #{Index}, {Size} bytes, {Namespace})";
}

/// <summary>
/// A single IWAD or PWAD: header, lump directory and lump data
/// (ported from w_wad.c <c>W_AddFile</c>). The whole file is held in memory.
/// </summary>
public sealed class WadFile
{
    private const int HeaderSize = 12;   // w_wad.c: wadinfo_t
    private const int DirEntrySize = 16; // w_wad.c: filelump_t
    private const int NameLength = 8;

    private readonly byte[] _data;
    private readonly WadLump[] _lumps;

    private WadFile(string name, byte[] data)
    {
        Name = name;
        _data = data;

        if (data.Length < HeaderSize)
            throw new WadFormatException($"{name}: file is {data.Length} bytes, too short for a WAD header.");

        string magic = Encoding.ASCII.GetString(data, 0, 4);
        Type = magic switch
        {
            "IWAD" => WadType.Iwad,
            "PWAD" => WadType.Pwad,
            _ => throw new WadFormatException($"{name}: bad WAD identification \"{magic}\" (expected IWAD or PWAD)."),
        };

        int numLumps = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        int infoTableOfs = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));
        if (numLumps < 0 || infoTableOfs < 0 || (long)infoTableOfs + (long)numLumps * DirEntrySize > data.Length)
            throw new WadFormatException($"{name}: lump directory ({numLumps} lumps at offset {infoTableOfs}) runs past the end of the file ({data.Length} bytes).");

        _lumps = new WadLump[numLumps];
        var tracker = new NamespaceTracker();
        for (int i = 0; i < numLumps; i++)
        {
            ReadOnlySpan<byte> entry = data.AsSpan(infoTableOfs + i * DirEntrySize, DirEntrySize);
            int filepos = BinaryPrimitives.ReadInt32LittleEndian(entry);
            int size = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            string lumpName = ReadName(entry.Slice(8, NameLength));

            // Zero-size lumps (markers) may carry any position; only check lumps with data.
            if (size < 0 || (size > 0 && (filepos < 0 || (long)filepos + size > data.Length)))
                throw new WadFormatException($"{name}: lump {i} ({lumpName}, {size} bytes at {filepos}) runs past the end of the file.");
            if (size == 0)
                filepos = 0;

            (LumpNamespace ns, bool isMarker) = tracker.Classify(lumpName);
            _lumps[i] = new WadLump(this, i, lumpName, filepos, size, ns, isMarker);
        }
    }

    /// <summary>Display name (the file name for files on disk).</summary>
    public string Name { get; }

    public WadType Type { get; }

    /// <summary>The lump directory in file order, markers included.</summary>
    public IReadOnlyList<WadLump> Lumps => _lumps;

    /// <summary>The raw file contents.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>Reads and parses the WAD at <paramref name="path"/>.</summary>
    public static WadFile Open(string path) => new(Path.GetFileName(path), File.ReadAllBytes(path));

    /// <summary>Parses a WAD held in memory (used for synthetic test WADs).</summary>
    public static WadFile FromBytes(byte[] data, string name = "<memory>") => new(name, data);

    /// <summary>
    /// Normalises a lump name: up to 8 characters, cut at the first NUL,
    /// upper-cased (w_wad.c <c>W_CheckNumForName</c> upper-cases the name it looks up).
    /// </summary>
    public static string NormalizeName(string name)
    {
        int nul = name.IndexOf('\0');
        if (nul >= 0)
            name = name[..nul];
        if (name.Length > NameLength)
            name = name[..NameLength];
        return name.ToUpperInvariant();
    }

    private static string ReadName(ReadOnlySpan<byte> raw)
    {
        int len = raw.IndexOf((byte)0);
        if (len < 0)
            len = raw.Length;
        // Latin-1 keeps every byte distinct; names are ASCII in practice.
        return NormalizeName(Encoding.Latin1.GetString(raw[..len]));
    }

    /// <summary>
    /// Tracks namespace markers while walking one file's directory. Markers
    /// follow the conventions Chocolate Doom's w_merge.c accepts: <c>X_START</c>
    /// or <c>XX_START</c> opens, <c>X_END</c> or <c>XX_END</c> closes. Inner
    /// markers (<c>P1_START</c>, <c>F2_END</c>, …) are markers inside the
    /// namespace. An unclosed namespace runs to the end of the file; a stray
    /// end marker is a global marker lump.
    /// </summary>
    private struct NamespaceTracker
    {
        private LumpNamespace _current;

        public (LumpNamespace Namespace, bool IsMarker) Classify(string name)
        {
            if (TryParseMarker(name, out LumpNamespace ns, out bool isStart, out bool isInner))
            {
                if (isInner)
                {
                    if (_current == ns)
                        return (ns, true);
                }
                else if (isStart)
                {
                    _current = ns;
                    return (ns, true);
                }
                else if (_current == ns)
                {
                    _current = LumpNamespace.Global;
                    return (ns, true);
                }
                // An end marker for a namespace that isn't open, or an inner
                // marker outside its namespace: treat it as a plain lump.
            }
            return (_current, false);
        }

        private static bool TryParseMarker(string name, out LumpNamespace ns, out bool isStart, out bool isInner)
        {
            ns = LumpNamespace.Global;
            isStart = false;
            isInner = false;

            string prefix;
            if (name.EndsWith("_START", StringComparison.Ordinal))
            {
                prefix = name[..^6];
                isStart = true;
            }
            else if (name.EndsWith("_END", StringComparison.Ordinal))
            {
                prefix = name[..^4];
            }
            else
            {
                return false;
            }

            switch (prefix)
            {
                case "S": case "SS": ns = LumpNamespace.Sprites; return true;
                case "F": case "FF": ns = LumpNamespace.Flats; return true;
                case "P": case "PP": ns = LumpNamespace.Patches; return true;
                case "F1": case "F2": case "F3": ns = LumpNamespace.Flats; isInner = true; return true;
                case "P1": case "P2": case "P3": ns = LumpNamespace.Patches; isInner = true; return true;
                default: return false;
            }
        }
    }
}
