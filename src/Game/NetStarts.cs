using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IsoDoom.Sim;
using IsoDoom.Wad;

namespace IsoDoom.Game;

/// <summary>
/// T11.1a, checked where the host starts a co-op game (T11.8): every player in
/// the game needs a co-op start (thing type <c>player + 1</c>) on every map
/// the game can reach, or <c>P_SpawnPlayer</c> never gives it a mobj and its
/// first <c>P_PlayerThink</c> dereferences a null <c>mo</c> (vanilla crashes
/// too). Such a game is refused before the Start with a message naming the
/// maps and the missing starts (the user's decision; SPEC §12 T11.8:
/// the start map and every map its exits lead to, secret maps included, as
/// far as the WAD has them). A deathmatch game spawns at the deathmatch starts
/// (T11.4) instead, and vanilla <c>I_Error</c>s on a map with fewer than 4 of
/// them: such maps are refused the same way. Plain C#: reads the maps' <c>THINGS</c> only.
/// </summary>
public static class NetStarts
{
    /// <summary>
    /// The maps a game started on episode <paramref name="episode"/>, map
    /// <paramref name="map"/> can reach by its exits (g_game.c
    /// <c>G_DoCompleted</c>'s next map, secret exits included), in play order,
    /// those the WAD lacks left out (a next map the WAD lacks ends the game).
    /// </summary>
    public static IReadOnlyList<string> ReachableMaps(WadArchive wad, GameMode mode, int episode, int map)
    {
        ArgumentNullException.ThrowIfNull(wad);
        var numbers = new List<int>();
        if (mode == GameMode.commercial)
        {
            // MAP01–MAP15 reach the secret maps 31 and 32 (MAP15's secret exit), both lead back to 16; MAP30 ends the game.
            if (map == 31)
                numbers.Add(31);
            if (map is 31 or 32)
                numbers.Add(32);
            int from = map is 31 or 32 ? 16 : map;
            for (int m = from; m <= 30 && map <= 32; m++)
                numbers.Add(m);
            if (map <= 15)
                numbers.AddRange([31, 32]);
            if (map > 32)
                numbers.Add(map); // a PWAD's map past MAP32: its own exits are not followed
            return [.. numbers.Distinct().Select(m => $"MAP{m:00}").Where(name => wad.W_CheckNumForName(name) >= 0)];
        }
        // ExM9 is reached by the secret exit of E1M3, E2M5, E3M6 and E4M2 and leads back to E1M4, E2M6, E3M7, E4M3.
        (int secret, int back) = episode switch { 1 => (3, 4), 2 => (5, 6), 3 => (6, 7), 4 => (2, 3), _ => (0, 0) };
        if (map == 9)
            numbers.Add(9);
        for (int m = map == 9 ? back : map; m <= 8 && map <= 9; m++)
            numbers.Add(m);
        if (map <= secret)
            numbers.Add(9);
        return [.. numbers.Distinct().Select(m => $"E{episode}M{m}").Where(name => wad.W_CheckNumForName(name) >= 0)];
    }

    /// <summary>
    /// The players of <paramref name="playerMask"/> (bit i: player i + 1)
    /// without a co-op start on map <paramref name="map"/>: those whose thing
    /// type <c>P_LoadThings</c> never reads (outside Doom II it stops at the
    /// first Doom II monster, as <see cref="MapThingSpawning"/>'s rule).
    /// </summary>
    public static IReadOnlyList<int> MissingStarts(WadArchive wad, GameMode mode, string map, int playerMask)
    {
        (int found, _) = Starts(wad, mode, map);
        var missing = new List<int>();
        for (int p = 0; p < World.MAXPLAYERS; p++)
        {
            if ((playerMask & (1 << p)) != 0 && (found & (1 << p)) == 0)
                missing.Add(p + 1);
        }
        return missing;
    }

    /// <summary>The deathmatch starts (thing type 11) <c>P_LoadThings</c> reads on map <paramref name="map"/>.</summary>
    public static int DeathmatchStarts(WadArchive wad, GameMode mode, string map) => Starts(wad, mode, map).Deathmatch;

    /// <summary>
    /// The co-op starts found (bit i: player i + 1's) and the deathmatch starts
    /// on map <paramref name="map"/>, as far as <c>P_LoadThings</c> reads
    /// (outside Doom II it stops at the first Doom II monster).
    /// </summary>
    private static (int Coop, int Deathmatch) Starts(WadArchive wad, GameMode mode, string map)
    {
        ArgumentNullException.ThrowIfNull(wad);
        int header = wad.W_CheckNumForName(map);
        if (header < 0 || header + 1 >= wad.NumLumps || wad.Lumps[header + 1].Name != "THINGS")
            throw new WadFormatException($"Map {map} has no THINGS lump after its header.");
        ReadOnlySpan<byte> things = wad.W_CacheLumpNum(header + 1).Span;
        int found = 0, deathmatch = 0;
        for (int i = 0; i + 10 <= things.Length; i += 10)
        {
            short type = BinaryPrimitives.ReadInt16LittleEndian(things.Slice(i + 6, 2));
            if (mode != GameMode.commercial && MapThingSpawning.IsDoom2Monster(type))
                break; // p_setup.c P_LoadThings: the rest is not loaded
            if (type is >= 1 and <= 4)
                found |= 1 << (type - 1);
            else if (type == 11)
                deathmatch++;
        }
        return (found, deathmatch);
    }

    /// <summary>
    /// Why a co-op game of <paramref name="playerMask"/> started on
    /// <paramref name="episode"/>, <paramref name="map"/> is refused (the maps
    /// it can reach lacking starts for some of its players), or null when every
    /// such map has them; in <paramref name="deathmatch"/>, the maps with
    /// fewer than 4 deathmatch starts.
    /// </summary>
    public static string? Refusal(WadArchive wad, GameMode mode, int episode, int map, int playerMask, bool deathmatch = false)
    {
        if (deathmatch)
        {
            // T11.4: G_DeathMatchSpawnPlayer's "Only %i deathmatch spots, 4 required"
            var few = ReachableMaps(wad, mode, episode, map).Select(name => (Name: name, Count: DeathmatchStarts(wad, mode, name))).Where(m => m.Count < 4).ToList();
            return few.Count == 0 ? null
                : "fewer than 4 deathmatch starts on " + string.Join(", ", few.Select(m => $"{m.Name} ({m.Count})"));
        }
        var lacking = new List<(string Map, IReadOnlyList<int> Players)>();
        foreach (string name in ReachableMaps(wad, mode, episode, map))
        {
            IReadOnlyList<int> missing = MissingStarts(wad, mode, name, playerMask);
            if (missing.Count > 0)
                lacking.Add((name, missing));
        }
        if (lacking.Count == 0)
            return null;
        var text = new StringBuilder("no co-op start for ");
        for (int i = 0; i < lacking.Count; i++)
        {
            (string name, IReadOnlyList<int> players) = lacking[i];
            if (i > 0)
                text.Append(i == lacking.Count - 1 ? " and " : ", ");
            text.Append(players.Count == 1 ? "player " : "players ").Append(PlayerList(players)).Append(" on ").Append(name);
        }
        return text.ToString();
    }

    private static string PlayerList(IReadOnlyList<int> players) =>
        players.Count == 1 ? players[0].ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Join(", ", players.Take(players.Count - 1)) + " and " + players[^1];
}
