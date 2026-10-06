using System;
using System.Collections.Generic;
using System.IO;
using IsoDoom.Wad;

namespace IsoDoom.Tools.SyntheticIwad;

/// <summary>
/// <c>dotnet run --project tools/SyntheticIwad -- [OUT.wad]</c>: writes the
/// synthetic IWAD (default <see cref="SyntheticIwad.DefaultFileName"/> in the
/// working directory), reloads it and checks it, and exits 1 if the check fails.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0].StartsWith('-')))
        {
            Console.Error.WriteLine("usage: IsoDoom.SyntheticIwad [OUT.wad]");
            return 2;
        }
        string path = Path.GetFullPath(args.Length == 1 ? args[0] : SyntheticIwad.DefaultFileName);
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, SyntheticIwad.Build());

        var wad = WadArchive.Open(path);
        List<string> problems = SyntheticIwad.Check(wad);
        foreach (string problem in problems)
            Console.Error.WriteLine($"synthetic IWAD: {problem}");
        if (problems.Count > 0)
            return 1;
        Console.WriteLine($"Wrote {path} ({new FileInfo(path).Length} bytes, {wad.NumLumps} lumps): {IwadIdentification.D_IdentifyVersion(wad)}");
        return 0;
    }
}
