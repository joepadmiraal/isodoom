using System;
using System.Collections.Generic;
using System.IO;
using IsoDoom.Game;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Tools.NetDesync;

/// <summary>
/// T11.11's follow-up: <c>dotnet run --project tools/NetDesync -- replay
/// WAD[,PWAD...] DIR [OUT]</c> replays a desync dump directory (one machine's
/// <c>TIME-playerN/</c>, <see cref="NetDesyncReplay"/>) on the game's WADs
/// (<c>synthetic</c> for the synthetic IWAD), writes <c>DIR/replay.txt</c> (or
/// OUT) and says whether the replay reproduces the machine's own checksums.
/// Exit code: 0 when it does, 1 when it differs (the tic is printed), 2 on
/// an error. <c>tools/NetDesync/compare.py</c> then compares two machines'
/// dumps (and their replays).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length is < 3 or > 4 || args[0] != "replay")
        {
            Console.Error.WriteLine("usage: replay WAD[,PWAD...] DIR [OUT]   (WAD 'synthetic': the synthetic IWAD)");
            return 2;
        }
        try
        {
            var files = new List<WadFile>();
            foreach (string path in args[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                files.Add(path == "synthetic"
                    ? WadFile.FromBytes(SyntheticIwad.SyntheticIwad.Build(), SyntheticIwad.SyntheticIwad.DefaultFileName)
                    : WadFile.Open(path));
            }
            string dir = args[2];
            string output = args.Length == 4 ? args[3] : Path.Combine(dir, NetDesyncReplay.FileName);
            NetDesyncReplay.Result r = NetDesyncReplay.Run(new WadArchive(files), dir, output);
            Console.WriteLine($"replayed tics {r.FirstTic}-{r.LastTic} into {output}");
            if (r.Stopped is { } why)
                Console.WriteLine($"stopped: {why}");
            if (r.FirstMismatch is int tic)
            {
                Console.WriteLine($"the replay differs from this machine's logged checksum from tic {tic}: its run did something the commands do not say there (or the build or WADs differ)");
                return 1;
            }
            Console.WriteLine("the replay reproduces this machine's logged checksums");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or UnauthorizedAccessException
            or SaveGameException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"replay failed: {e.Message}");
            return 2;
        }
    }
}
