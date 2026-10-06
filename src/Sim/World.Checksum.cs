namespace IsoDoom.Sim;

// The per-tic state checksum (SPEC §6.1, §12 T4.2).
public sealed partial class World
{
    /// <summary>
    /// A cheap checksum of the play state, for tests now and desync detection
    /// in co-op later (SPEC §6.1): 64-bit FNV-1a over, in order,
    /// <see cref="leveltime"/>, <c>prndindex</c>, every mobj in thinker order
    /// (type, x, y, z, momx, momy, momz, angle, health, state, tics, flags),
    /// every sector (floor height, ceiling height, light level) and every
    /// player in the game (player state, health, armor points). Only sim state
    /// goes in: <c>M_Random</c>'s index does not.
    /// </summary>
    public ulong Checksum()
    {
        ulong h = FnvOffset;
        Add(ref h, leveltime);
        Add(ref h, random.prndindex);

        foreach (mobj_t mo in Mobjs())
        {
            Add(ref h, (int)mo.type);
            Add(ref h, mo.x);
            Add(ref h, mo.y);
            Add(ref h, mo.z);
            Add(ref h, mo.momx);
            Add(ref h, mo.momy);
            Add(ref h, mo.momz);
            Add(ref h, unchecked((int)mo.angle));
            Add(ref h, mo.health);
            Add(ref h, (int)mo.state);
            Add(ref h, mo.tics);
            Add(ref h, (int)mo.flags);
        }

        foreach (sector_t sec in sectors)
        {
            Add(ref h, sec.floorheight);
            Add(ref h, sec.ceilingheight);
            Add(ref h, sec.lightlevel);
        }

        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (!playeringame[i])
                continue;
            player_t p = players[i];
            Add(ref h, i);
            Add(ref h, (int)p.playerstate);
            Add(ref h, p.health);
            Add(ref h, p.armorpoints);
        }
        return h;
    }

    private const ulong FnvOffset = 14695981039346656037;
    private const ulong FnvPrime = 1099511628211;

    /// <summary>FNV-1a over the four little-endian bytes of <paramref name="v"/>.</summary>
    private static void Add(ref ulong h, int v)
    {
        unchecked
        {
            for (int b = 0; b < 32; b += 8)
            {
                h ^= (byte)(v >> b);
                h *= FnvPrime;
            }
        }
    }
}
