using System.Collections.Generic;
using IsoDoom.Map;

namespace IsoDoom.Sim;

// p_enemy.c's Icon of Sin (T10.4): the boss brain (MT_BOSSBRAIN), its
// shooter (MT_BOSSSPIT), the spawn cubes (MT_SPAWNSHOT) and their targets
// (MT_BOSSTARGET), as vanilla (doomgeneric's p_enemy.c).
public sealed partial class World
{
    /// <summary>
    /// p_enemy.c <c>braintargets</c>: the brain targets <see cref="A_BrainAwake"/>
    /// found, in thinker order. Vanilla's array holds 32 and overflows past
    /// them; this list grows (SPEC §12 T10.4). Saved (<see cref="P_ArchiveGame"/>).
    /// </summary>
    public readonly List<mobj_t> braintargets = [];

    /// <summary>p_enemy.c <c>numbraintargets</c>.</summary>
    public int numbraintargets => braintargets.Count;

    /// <summary>p_enemy.c <c>braintargeton</c>: the target of the next spawn cube.</summary>
    public int braintargeton;

    /// <summary>
    /// p_enemy.c <c>A_BrainSpit</c>'s <c>static int easy</c>: flips every
    /// spit; on skill 1 and 2 only every other one shoots. Vanilla's lasts
    /// for the whole run; this one for the world (a game, SPEC §12 T10.4).
    /// </summary>
    public int braineasy;

    /// <summary>
    /// p_enemy.c <c>A_BrainAwake</c>: the shooter wakes: every
    /// <see cref="mobjtype_t.MT_BOSSTARGET"/> in thinker order becomes a
    /// target, the first one next; the sight sound, everywhere.
    /// </summary>
    public void A_BrainAwake(mobj_t mo)
    {
        // find all the target spots
        braintargets.Clear();
        braintargeton = 0;

        foreach (mobj_t m in Mobjs())
        {
            if (m.type == mobjtype_t.MT_BOSSTARGET)
                braintargets.Add(m);
        }

        S_StartSound((mobj_t?)null, sfxenum_t.sfx_bossit);
    }

    /// <summary>p_enemy.c <c>A_BrainPain</c>: the brain's pain sound, everywhere.</summary>
    public void A_BrainPain(mobj_t mo) => S_StartSound((mobj_t?)null, sfxenum_t.sfx_bospn);

    /// <summary>
    /// p_enemy.c <c>A_BrainScream</c>: the brain dies: a row of explosions
    /// (rockets in <see cref="statenum_t.S_BRAINEXPLODE1"/>) every 8 units
    /// from 196 west to 320 east of it, 320 units south, rising; the death
    /// sound, everywhere. Vanilla's <c>z = 128 + P_Random()*2*FRACUNIT</c>
    /// (128 not in units) is kept.
    /// </summary>
    public void A_BrainScream(mobj_t mo)
    {
        for (int x = mo.x - 196 * Fixed.FRACUNIT; x < mo.x + 320 * Fixed.FRACUNIT; x += Fixed.FRACUNIT * 8)
        {
            int y = mo.y - 320 * Fixed.FRACUNIT;
            int z = 128 + P_Random() * 2 * Fixed.FRACUNIT;
            mobj_t th = P_SpawnMobj(x, y, z, mobjtype_t.MT_ROCKET);
            th.momz = P_Random() * 512;

            P_SetMobjState(th, statenum_t.S_BRAINEXPLODE1);

            th.tics -= P_Random() & 7;
            if (th.tics < 1)
                th.tics = 1;
        }

        S_StartSound((mobj_t?)null, sfxenum_t.sfx_bosdth);
    }

    /// <summary>
    /// p_enemy.c <c>A_BrainExplode</c>: each explosion's last frame spawns
    /// another, up to 2 × 255 × 2048 units east or west of it, rising.
    /// </summary>
    public void A_BrainExplode(mobj_t mo)
    {
        int x = mo.x + ((P_Random() - P_Random()) * 2048);
        int y = mo.y;
        int z = 128 + P_Random() * 2 * Fixed.FRACUNIT;
        mobj_t th = P_SpawnMobj(x, y, z, mobjtype_t.MT_ROCKET);
        th.momz = P_Random() * 512;

        P_SetMobjState(th, statenum_t.S_BRAINEXPLODE1);

        th.tics -= P_Random() & 7;
        if (th.tics < 1)
            th.tics = 1;
    }

    /// <summary>p_enemy.c <c>A_BrainDie</c>: the brain's last frame ends the level (<see cref="G_ExitLevel"/>).</summary>
    public void A_BrainDie(mobj_t mo) => G_ExitLevel();

    /// <summary>
    /// p_enemy.c <c>A_BrainSpit</c>: the shooter fires a spawn cube
    /// (<see cref="mobjtype_t.MT_SPAWNSHOT"/>) at the next brain target in
    /// turn (only every other time on skill 1 and 2), its
    /// <see cref="mobj_t.reactiontime"/> the flight's length in its
    /// frames, by the north-south distance; the spit sound, everywhere.
    /// Without targets (vanilla reads past the array and crashes) it does
    /// nothing (SPEC §12 T10.4). The divisions are vanilla's, unguarded:
    /// neither divisor can be 0. <c>finesine</c> has no zero entry (it is
    /// sampled at half fine angles, ±25 at least), so a cube's <c>momy</c>
    /// is at least ±250 even due east or west; the cube is
    /// <c>MF_NOCLIP</c>, so <c>P_CheckMissileSpawn</c> never explodes it
    /// (zeroing its momentum); and <c>S_SPAWN1</c> lasts 3 tics. A target
    /// level with the shooter gets a flight of 0, which
    /// <see cref="A_SpawnFly"/> counts past: the cube flies on, as vanilla's
    /// (T10.7 review).
    /// </summary>
    public void A_BrainSpit(mobj_t mo)
    {
        braineasy ^= 1;
        if (gameskill <= skill_t.sk_easy && braineasy == 0)
            return;

        if (numbraintargets == 0)
            return; // vanilla: a crash

        // shoot a cube at current target
        mobj_t targ = braintargets[braintargeton];
        braintargeton = (braintargeton + 1) % numbraintargets;

        // spawn brain missile
        mobj_t newmobj = P_SpawnMissile(mo, targ, mobjtype_t.MT_SPAWNSHOT);
        newmobj.target = targ;
        newmobj.reactiontime = (targ.y - mo.y) / newmobj.momy / StateTics(newmobj.state);

        S_StartSound((mobj_t?)null, sfxenum_t.sfx_bospit);
    }

    /// <summary>p_enemy.c <c>A_SpawnSound</c>: travelling cube sound, then <see cref="A_SpawnFly"/>.</summary>
    public void A_SpawnSound(mobj_t mo)
    {
        S_StartSound(mo, sfxenum_t.sfx_boscub);
        A_SpawnFly(mo);
    }

    /// <summary>
    /// p_enemy.c <c>A_SpawnFly</c>: a spawn cube counts its flight down;
    /// at its end, on its target (<see cref="mobj_t.target"/>,
    /// <see cref="P_SubstNullMobj"/>), a fire with the teleport sound and a
    /// monster picked by <c>P_Random</c> (imps most likely), awake if it
    /// sees a player, telefragging what stands there; the cube goes.
    /// </summary>
    public void A_SpawnFly(mobj_t mo)
    {
        if (--mo.reactiontime != 0)
            return; // still flying

        mobj_t targ = P_SubstNullMobj(mo.target);

        // First spawn teleport fog.
        mobj_t fog = P_SpawnMobj(targ.x, targ.y, targ.z, mobjtype_t.MT_SPAWNFIRE);
        S_StartSound(fog, sfxenum_t.sfx_telept);

        // Randomly select monster to spawn.
        int r = P_Random();

        // Probability distribution (kind of :),
        // decreasing likelihood.
        mobjtype_t type;
        if (r < 50)
            type = mobjtype_t.MT_TROOP;
        else if (r < 90)
            type = mobjtype_t.MT_SERGEANT;
        else if (r < 120)
            type = mobjtype_t.MT_SHADOWS;
        else if (r < 130)
            type = mobjtype_t.MT_PAIN;
        else if (r < 160)
            type = mobjtype_t.MT_HEAD;
        else if (r < 162)
            type = mobjtype_t.MT_VILE;
        else if (r < 172)
            type = mobjtype_t.MT_UNDEAD;
        else if (r < 192)
            type = mobjtype_t.MT_BABY;
        else if (r < 222)
            type = mobjtype_t.MT_FATSO;
        else if (r < 246)
            type = mobjtype_t.MT_KNIGHT;
        else
            type = mobjtype_t.MT_BRUISER;

        mobj_t newmobj = P_SpawnMobj(targ.x, targ.y, targ.z, type);
        if (P_LookForPlayers(newmobj, true))
            P_SetMobjState(newmobj, newmobj.info.seestate);

        // telefrag anything in this spot
        P_TeleportMove(newmobj, newmobj.x, newmobj.y);

        // remove self (i.e., cube).
        P_RemoveMobj(mo);
    }
}
