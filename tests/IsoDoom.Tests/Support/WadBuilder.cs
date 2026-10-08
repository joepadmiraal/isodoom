using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using IsoDoom.Wad;

namespace IsoDoom.Tests.Support;

/// <summary>Builds small synthetic WADs in memory for tests.</summary>
public sealed class WadBuilder
{
    private readonly string _magic;
    private readonly List<(string Name, byte[] Data)> _lumps = [];

    public WadBuilder(WadType type = WadType.Pwad) => _magic = type == WadType.Iwad ? "IWAD" : "PWAD";

    /// <summary>Adds a lump; a null or empty payload makes a zero-size (marker) lump.</summary>
    public WadBuilder Lump(string name, params byte[] data)
    {
        if (name.Length > 8)
            throw new ArgumentException("Lump names are at most 8 characters.", nameof(name));
        _lumps.Add((name, data));
        return this;
    }

    /// <summary>Adds several zero-size lumps.</summary>
    public WadBuilder Markers(params string[] names)
    {
        foreach (string name in names)
            Lump(name);
        return this;
    }

    /// <summary>Layout: header, lump data, directory at the end (as id's tools write it).</summary>
    public byte[] Build()
    {
        int dataSize = 0;
        foreach ((string Name, byte[] Data) lump in _lumps)
            dataSize += lump.Data.Length;

        byte[] wad = new byte[12 + dataSize + 16 * _lumps.Count];
        Encoding.ASCII.GetBytes(_magic, wad.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(wad.AsSpan(4), _lumps.Count);
        int dirOfs = 12 + dataSize;
        BinaryPrimitives.WriteInt32LittleEndian(wad.AsSpan(8), dirOfs);

        int pos = 12;
        for (int i = 0; i < _lumps.Count; i++)
        {
            (string name, byte[] data) = _lumps[i];
            data.CopyTo(wad, pos);
            Span<byte> entry = wad.AsSpan(dirOfs + 16 * i, 16);
            BinaryPrimitives.WriteInt32LittleEndian(entry, data.Length == 0 ? 0 : pos);
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], data.Length);
            Encoding.ASCII.GetBytes(name, entry[8..]);
            pos += data.Length;
        }
        return wad;
    }

    public WadFile ToWadFile(string name = "test.wad") => WadFile.FromBytes(Build(), name);
}
