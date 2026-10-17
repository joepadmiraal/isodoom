using System.Collections.Generic;

namespace IsoDoom.Sim;

// The per-tic state checksum (SPEC §6.1, §12 T4.2).
public sealed partial class World
{
    /// <summary>
    /// A cheap checksum of the play state, for tests now and desync detection
    /// in co-op later (SPEC §6.1): 64-bit FNV-1a over, in order,
    /// <see cref="leveltime"/>, <c>prndindex</c>, every mobj in thinker order
    /// (type, x, y, z, momx, momy, momz, angle, health, state, tics, flags;
    /// since T6.4 also movedir, movecount, reactiontime, threshold and its
    /// target as a mobj index), every sector (floor height, ceiling height,
    /// light level; since T6.4 its sound target as a mobj index) and every
    /// player in the game (player state, health, armor points, view z,
    /// view height and its delta; since T6.6 also the ready and pending
    /// weapons, the ammo, refire, attackdown, extralight and each psprite's
    /// state, tics and position; since T10.4, once the Icon of Sin woke, its
    /// brain targets as mobj indices, the next one and <c>A_BrainSpit</c>'s
    /// <c>easy</c>; since T11.1, for the netgames' desync detection, the item
    /// respawn queue's head and tail and, for each player in the game, the
    /// armor type, the powers, the cards, the backpack, the frags, the
    /// weapons owned, the maximum ammo, the cheats and the kill, item and
    /// secret counts). Only sim state goes in, the same on every machine of
    /// a game: <c>M_Random</c>'s index and <see cref="consoleplayer"/> do not. A mobj index is the mobj's
    /// place in thinker order, -1 for none (or a removed mobj).
    /// </summary>
    public ulong Checksum()
    {
        ulong h = FnvOffset;
        Add(ref h, leveltime);
        Add(ref h, random.prndindex);

        // the mobjs' places in thinker order (lookups only: no iteration over the dictionary)
        _checksumIndex.Clear();
        foreach (mobj_t mo in Mobjs())
            _checksumIndex[mo] = _checksumIndex.Count;

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
            Add(ref h, mo.movedir);
            Add(ref h, mo.movecount);
            Add(ref h, mo.reactiontime);
            Add(ref h, mo.threshold);
            Add(ref h, MobjIndex(mo.target));
        }

        foreach (sector_t sec in sectors)
        {
            Add(ref h, sec.floorheight);
            Add(ref h, sec.ceilingheight);
            Add(ref h, sec.lightlevel);
            Add(ref h, MobjIndex(sec.soundtarget));
        }

        // the brain's targets (T10.4), only once it has any: other maps' checksums stay as they were
        if (braintargets.Count > 0)
        {
            foreach (mobj_t m in braintargets)
                Add(ref h, MobjIndex(m));
            Add(ref h, braintargeton);
            Add(ref h, braineasy);
        }
        _checksumIndex.Clear();

        // T11.1: the item respawn queue (deathmatch 2's)
        Add(ref h, iquehead);
        Add(ref h, iquetail);

        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (!playeringame[i])
                continue;
            player_t p = players[i];
            Add(ref h, i);
            Add(ref h, (int)p.playerstate);
            Add(ref h, p.health);
            Add(ref h, p.armorpoints);
            Add(ref h, p.viewz);
            Add(ref h, p.viewheight);
            Add(ref h, p.deltaviewheight);
            Add(ref h, (int)p.readyweapon);
            Add(ref h, (int)p.pendingweapon);
            foreach (int a in p.ammo)
                Add(ref h, a);
            Add(ref h, p.refire);
            Add(ref h, p.attackdown ? 1 : 0);
            Add(ref h, p.extralight);
            foreach (pspdef_t psp in p.psprites)
            {
                Add(ref h, (int)psp.state);
                Add(ref h, psp.tics);
                Add(ref h, psp.sx);
                Add(ref h, psp.sy);
            }

            // T11.1: the rest of the inventory, the frags, the cheats and the counts
            Add(ref h, p.armortype);
            foreach (int power in p.powers)
                Add(ref h, power);
            foreach (bool card in p.cards)
                Add(ref h, card ? 1 : 0);
            Add(ref h, p.backpack ? 1 : 0);
            foreach (int frag in p.frags)
                Add(ref h, frag);
            foreach (bool owned in p.weaponowned)
                Add(ref h, owned ? 1 : 0);
            foreach (int max in p.maxammo)
                Add(ref h, max);
            Add(ref h, p.cheats);
            Add(ref h, p.killcount);
            Add(ref h, p.itemcount);
            Add(ref h, p.secretcount);
        }
        return h;
    }

    /// <summary>
    /// Not vanilla (T11.11's follow-up, the desync replay): every field
    /// <see cref="Checksum"/> hashes, as text, a labelled line per part
    /// (<c>world</c>, <c>mobj I</c> in thinker order, <c>sector I</c>,
    /// <c>brain</c>, <c>player N</c>), each <c>name=value</c>, appended to
    /// <paramref name="sb"/>. Two machines' dumps of the same tic differ in
    /// a line exactly where their checksums' inputs differ, which names the
    /// field (<c>tools/NetDesync/compare.py</c>).
    /// </summary>
    public void DumpState(System.Text.StringBuilder sb)
    {
        System.ArgumentNullException.ThrowIfNull(sb);
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        sb.Append(inv, $"world leveltime={leveltime} prndindex={random.prndindex} iquehead={iquehead} iquetail={iquetail}\n");

        _checksumIndex.Clear();
        foreach (mobj_t mo in Mobjs())
            _checksumIndex[mo] = _checksumIndex.Count;
        int n = 0;
        foreach (mobj_t mo in Mobjs())
        {
            sb.Append(inv, $"mobj {n++} type={mo.type} x={mo.x} y={mo.y} z={mo.z} momx={mo.momx} momy={mo.momy} momz={mo.momz}");
            sb.Append(inv, $" angle={mo.angle} health={mo.health} state={mo.state} tics={mo.tics} flags={(uint)mo.flags:x}");
            sb.Append(inv, $" movedir={mo.movedir} movecount={mo.movecount} reactiontime={mo.reactiontime} threshold={mo.threshold} target={MobjIndex(mo.target)}\n");
        }
        for (int i = 0; i < sectors.Length; i++)
        {
            sector_t sec = sectors[i];
            sb.Append(inv, $"sector {i} floorheight={sec.floorheight} ceilingheight={sec.ceilingheight} lightlevel={sec.lightlevel} soundtarget={MobjIndex(sec.soundtarget)}\n");
        }
        if (braintargets.Count > 0)
        {
            sb.Append("brain targets=");
            for (int i = 0; i < braintargets.Count; i++)
                sb.Append(inv, $"{(i > 0 ? "," : "")}{MobjIndex(braintargets[i])}");
            sb.Append(inv, $" braintargeton={braintargeton} braineasy={braineasy}\n");
        }
        _checksumIndex.Clear();

        for (int i = 0; i < MAXPLAYERS; i++)
        {
            if (!playeringame[i])
                continue;
            player_t p = players[i];
            sb.Append(inv, $"player {i + 1} playerstate={p.playerstate} health={p.health} armorpoints={p.armorpoints} armortype={p.armortype}");
            sb.Append(inv, $" viewz={p.viewz} viewheight={p.viewheight} deltaviewheight={p.deltaviewheight}");
            sb.Append(inv, $" readyweapon={p.readyweapon} pendingweapon={p.pendingweapon} refire={p.refire} attackdown={p.attackdown} extralight={p.extralight}");
            sb.Append(inv, $" ammo={string.Join(',', p.ammo)} maxammo={string.Join(',', p.maxammo)} weaponowned={Bits(p.weaponowned)}");
            sb.Append(inv, $" powers={string.Join(',', p.powers)} cards={Bits(p.cards)} backpack={p.backpack} frags={string.Join(',', p.frags)}");
            sb.Append(inv, $" cheats={p.cheats} killcount={p.killcount} itemcount={p.itemcount} secretcount={p.secretcount}");
            for (int k = 0; k < p.psprites.Length; k++)
            {
                pspdef_t psp = p.psprites[k];
                sb.Append(inv, $" psp{k}={psp.state}/{psp.tics}/{psp.sx}/{psp.sy}");
            }
            sb.Append('\n');
        }
    }

    private static string Bits(bool[] bits)
    {
        var chars = new char[bits.Length];
        for (int i = 0; i < bits.Length; i++)
            chars[i] = bits[i] ? '1' : '0';
        return new string(chars);
    }

    private readonly System.Collections.Generic.Dictionary<mobj_t, int> _checksumIndex = new(ReferenceEqualityComparer.Instance);

    private int MobjIndex(mobj_t? mo) => mo != null && _checksumIndex.TryGetValue(mo, out int i) ? i : -1;

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
