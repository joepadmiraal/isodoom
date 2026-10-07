using System;
using System.Buffers.Binary;

namespace IsoDoom.Wad;

/// <summary>
/// A digitized sound effect (<c>DS*</c> lumps, the DMX sound library's
/// format, T7.7): a 16-bit format (3), a 16-bit sample rate, a 32-bit
/// sample count, then 8-bit unsigned mono samples (128 is silence). As
/// Chocolate Doom's i_sdlsound.c <c>CacheSFX</c>: the count includes 16 pad
/// bytes at either end, which DMX skips, and a lump whose count is beyond
/// the lump or at most 48 is not a sound (DMX's behaviour).
/// </summary>
public sealed class DmxSound
{
    /// <summary>The header's format number for digitized sounds.</summary>
    public const int Format = 3;

    /// <summary>The header's size in bytes.</summary>
    public const int HeaderSize = 8;

    /// <summary>The pad bytes DMX skips at each end of the samples.</summary>
    public const int Padding = 16;

    /// <summary>The sample rate in Hz, from the header (11025 for most of Doom's, 22050 for a few).</summary>
    public int SampleRate { get; }

    /// <summary>The samples played (the pads left out): 8-bit unsigned, 128 is silence.</summary>
    public byte[] Samples { get; }

    /// <summary>The sound's length in seconds at <see cref="SampleRate"/>.</summary>
    public double Seconds => SampleRate > 0 ? (double)Samples.Length / SampleRate : 0;

    private DmxSound(int sampleRate, byte[] samples)
    {
        SampleRate = sampleRate;
        Samples = samples;
    }

    /// <summary>Whether <paramref name="lump"/> starts with a digitized sound's header (format 3).</summary>
    public static bool HasHeader(ReadOnlySpan<byte> lump) =>
        lump.Length >= HeaderSize && BinaryPrimitives.ReadUInt16LittleEndian(lump) == Format;

    /// <summary>
    /// Decodes a <c>DS*</c> lump as i_sdlsound.c <c>CacheSFX</c> does, or
    /// returns null with the reason in <paramref name="error"/> when DMX would
    /// not play it (no format 3 header, a count beyond the lump, or 48
    /// samples or fewer).
    /// </summary>
    public static DmxSound? TryDecode(ReadOnlySpan<byte> lump, out string? error)
    {
        error = null;
        if (!HasHeader(lump))
        {
            error = lump.Length < HeaderSize ? $"{lump.Length} bytes, shorter than the header" : "not a digitized sound (format is not 3)";
            return null;
        }
        int rate = BinaryPrimitives.ReadUInt16LittleEndian(lump[2..]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(lump[4..]);
        if (length > (uint)(lump.Length - HeaderSize))
        {
            error = $"{length} samples, beyond the lump's {lump.Length - HeaderSize}";
            return null;
        }
        if (length <= 48)
        {
            error = $"{length} samples, too short for DMX";
            return null;
        }
        if (rate == 0)
        {
            error = "sample rate 0";
            return null;
        }
        // "The DMX sound library seems to skip the first 16 and last 16 bytes of the lump"
        byte[] samples = lump.Slice(HeaderSize + Padding, (int)length - 2 * Padding).ToArray();
        return new DmxSound(rate, samples);
    }

    /// <summary><see cref="TryDecode"/>, throwing <see cref="WadFormatException"/> when it fails.</summary>
    public static DmxSound Decode(ReadOnlySpan<byte> lump, string name = "sound") =>
        TryDecode(lump, out string? error) ?? throw new WadFormatException($"{name}: {error}");

    /// <summary>The samples as signed 8-bit (unsigned − 128), the form Godot's 8-bit <c>AudioStreamWAV</c> takes.</summary>
    public byte[] ToSigned8()
    {
        byte[] signed = new byte[Samples.Length];
        for (int i = 0; i < signed.Length; i++)
            signed[i] = (byte)(Samples[i] ^ 0x80);
        return signed;
    }
}
