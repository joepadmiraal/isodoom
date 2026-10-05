using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace IsoDoom.Tests.Support;

/// <summary>
/// Minimal PNG encoder (8-bit RGBA, no filtering) for debug exports in tests.
/// Uses only the BCL: zlib via <see cref="ZLibStream"/>, CRC-32 computed here.
/// </summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void WriteRgba(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        File.WriteAllBytes(path, EncodeRgba(width, height, rgba));
    }

    /// <summary>Upscales by an integer factor (nearest neighbour) so small sprites are easy to inspect.</summary>
    public static byte[] Scale(int width, int height, ReadOnlySpan<byte> rgba, int factor)
    {
        byte[] scaled = new byte[width * factor * height * factor * 4];
        for (int y = 0; y < height * factor; y++)
            for (int x = 0; x < width * factor; x++)
                rgba.Slice(((y / factor) * width + x / factor) * 4, 4).CopyTo(scaled.AsSpan((y * width * factor + x) * 4, 4));
        return scaled;
    }

    public static byte[] EncodeRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("RGBA buffer must be width * height * 4 bytes.", nameof(rgba));

        using MemoryStream png = new();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // colour type: RGBA
        WriteChunk(png, "IHDR", ihdr);

        using MemoryStream raw = new();
        using (ZLibStream z = new(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0); // filter: none
                z.Write(rgba.Slice(y * width * 4, width * 4));
            }
        }
        WriteChunk(png, "IDAT", raw.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        s.Write(buf);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
        s.Write(buf);
    }

    private static uint Crc(uint crc, byte[] data)
    {
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
