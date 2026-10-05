using System;

namespace IsoDoom.Wad;

/// <summary>Thrown when a file is not a valid IWAD/PWAD (bad magic, truncated header, directory or lump out of range).</summary>
public sealed class WadFormatException : FormatException
{
    public WadFormatException(string message) : base(message)
    {
    }
}
