using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IsoDoom.Wad;

namespace IsoDoom.Map;

/// <summary>
/// P7 (docs/PWAD-VALIDATION.md): what a map holds that vanilla doesn't know
/// and so ignores, as the level's log line names it. Vanilla's code has no
/// such list: its specials' <c>switch</c>es simply have no case for the
/// rest. The sim ignores the same (World.Spec.cs, World.Switch.cs,
/// MapThingSpawn.cs); this only reports it. Plain C#, no Godot types.
/// </summary>
public sealed class VanillaIgnores
{
    /// <summary>The options bits <c>P_SpawnMapThing</c> reads: the three skills, ambush and "not in single player" (doomdata.h <c>MTF_*</c>, p_mobj.c).</summary>
    public const int VanillaThingOptions = 31;

    /// <summary>
    /// Whether vanilla's p_spec.c and p_switch.c act on line special
    /// <paramref name="special"/> (crossed, used, shot or at spawn): every
    /// special from 1 to 141 but 78 and 85, which no <c>switch</c> names.
    /// </summary>
    public static bool IsVanillaLineSpecial(int special) => special is >= 0 and <= 141 and not 78 and not 85;

    /// <summary>
    /// Whether vanilla knows sector special <paramref name="special"/>
    /// (p_spec.c <c>P_SpawnSpecials</c> and <c>P_PlayerInSpecialSector</c>):
    /// 0–17 but 6 and 15. Another is skipped at spawn and is
    /// <c>P_PlayerInSpecialSector</c>'s <c>I_Error</c> when a player stands in it.
    /// </summary>
    public static bool IsVanillaSectorSpecial(int special) => special is >= 0 and <= 17 and not 6 and not 15;

    /// <summary>One kind of ignored content: its value, how many there are, and the first few indices (lines, sectors or things).</summary>
    public sealed record Item(int Value, int Count, IReadOnlyList<int> First);

    /// <summary>The line specials vanilla doesn't know (<see cref="IsVanillaLineSpecial"/>), by special, in order.</summary>
    public IReadOnlyList<Item> LineSpecials { get; }

    /// <summary>The sector specials vanilla doesn't know (<see cref="IsVanillaSectorSpecial"/>), as loaded, by special.</summary>
    public IReadOnlyList<Item> SectorSpecials { get; }

    /// <summary>The thing options bits vanilla doesn't read (above <see cref="VanillaThingOptions"/>), by bit.</summary>
    public IReadOnlyList<Item> ThingFlags { get; }

    /// <summary>Whether the map holds nothing vanilla ignores.</summary>
    public bool IsEmpty => LineSpecials.Count == 0 && SectorSpecials.Count == 0 && ThingFlags.Count == 0;

    private const int Shown = 4; // indices kept per item

    private VanillaIgnores(IReadOnlyList<Item> lines, IReadOnlyList<Item> sectors, IReadOnlyList<Item> things)
    {
        LineSpecials = lines;
        SectorSpecials = sectors;
        ThingFlags = things;
    }

    /// <summary>What <paramref name="level"/> holds that vanilla ignores (its lines' specials as they are now: read it at load, before the sim clears any).</summary>
    public static VanillaIgnores Of(Level level)
    {
        System.ArgumentNullException.ThrowIfNull(level);
        var lines = new SortedDictionary<int, List<int>>();
        foreach (Line line in level.Lines)
        {
            int special = (ushort)line.Special;
            if (!IsVanillaLineSpecial(special))
                Add(lines, special, line.Index);
        }
        var sectors = new SortedDictionary<int, List<int>>();
        foreach (Sector sector in level.Sectors)
        {
            int special = (ushort)sector.LoadedSpecial;
            if (!IsVanillaSectorSpecial(special))
                Add(sectors, special, sector.Index);
        }
        var things = new SortedDictionary<int, List<int>>();
        for (int i = 0; i < level.Things.Length; i++)
        {
            int options = (ushort)level.Things[i].Options & ~VanillaThingOptions;
            for (int bit = 32; bit <= 0x8000; bit <<= 1)
            {
                if ((options & bit) != 0)
                    Add(things, bit, i);
            }
        }
        return new VanillaIgnores(Items(lines), Items(sectors), Items(things));
    }

    private static void Add(SortedDictionary<int, List<int>> by, int key, int index)
    {
        if (!by.TryGetValue(key, out List<int>? list))
            by[key] = list = [];
        list.Add(index);
    }

    private static List<Item> Items(SortedDictionary<int, List<int>> by) =>
        [.. by.Select(kv => new Item(kv.Key, kv.Value.Count, [.. kv.Value.Take(Shown)]))];

    /// <summary>The name of a line special vanilla doesn't know, where a later port gave it one (Boom's, MBF's), else null.</summary>
    public static string? LineSpecialName(int special) => special switch
    {
        78 => "Boom's change texture and type, SR",
        85 => "Boom's scroll right",
        242 => "Boom's deep water",
        260 => "Boom's translucency",
        271 => "MBF's sky transfer",
        272 => "MBF's sky transfer, flipped",
        >= 0x2F80 => "a Boom generalized special",
        _ => null,
    };

    /// <summary>The name of a thing options bit vanilla doesn't read, where a later port gave it one, else null.</summary>
    public static string? ThingFlagName(int bit) => bit switch
    {
        32 => "Boom's not in deathmatch",
        64 => "Boom's not in co-op",
        128 => "MBF's friendly",
        _ => null,
    };

    /// <summary>
    /// The level log's text: e.g. <c>line special 272 (MBF's sky transfer,
    /// flipped) on 2 lines: 1530, 1531</c>, each kind after the other, or
    /// null when <see cref="IsEmpty"/>.
    /// </summary>
    public string? Describe()
    {
        if (IsEmpty)
            return null;
        var parts = new List<string>();
        foreach (Item item in LineSpecials)
            parts.Add(Part("line special", item, LineSpecialName(item.Value), "line"));
        foreach (Item item in SectorSpecials)
            parts.Add(Part("sector special", item, "vanilla's I_Error when a player stands in it", "sector"));
        foreach (Item item in ThingFlags)
            parts.Add(Part("thing flag", item, ThingFlagName(item.Value), "thing"));
        return string.Join("; ", parts);
    }

    private static string Part(string what, Item item, string? name, string unit)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{what} {item.Value}");
        if (name is not null)
            sb.Append(CultureInfo.InvariantCulture, $" ({name})");
        sb.Append(CultureInfo.InvariantCulture, $" on {item.Count} {unit}{(item.Count == 1 ? "" : "s")}: ");
        sb.Append(string.Join(", ", item.First));
        if (item.Count > item.First.Count)
            sb.Append(", ...");
        return sb.ToString();
    }

    /// <summary>
    /// The lumps of the PWADs (the archive's files after its first, the IWAD)
    /// that vanilla never reads, by name, each with its file: those the lump
    /// directory classifies as <see cref="LumpKind.Other"/> (<c>DEHACKED</c>,
    /// <c>MAPINFO</c>, <c>ZMAPINFO</c>, tool lumps, …; P1 pins each set's).
    /// P12d: only the PWADs' lumps are classified (<see cref="LumpDirectory.Classify"/>
    /// reads the lump, its namespace and the next lump of its own file, never
    /// the IWAD's), and nothing without a PWAD.
    /// </summary>
    public static IReadOnlyList<(string Name, string File)> IgnoredLumps(WadArchive wad)
    {
        System.ArgumentNullException.ThrowIfNull(wad);
        var ignored = new List<(string Name, string File)>();
        for (int f = 1; f < wad.Files.Count; f++)
        {
            WadFile file = wad.Files[f];
            for (int i = 0; i < file.Lumps.Count; i++)
            {
                WadLump lump = file.Lumps[i];
                if (LumpDirectory.Classify(lump, i + 1 < file.Lumps.Count ? file.Lumps[i + 1].Name : null).Kind == LumpKind.Other)
                    ignored.Add((lump.Name, file.Name));
            }
        }
        return ignored;
    }
}
