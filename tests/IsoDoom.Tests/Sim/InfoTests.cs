using System;
using System.Linq;
using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;
using Xunit;
using static IsoDoom.Map.Fixed;

namespace IsoDoom.Tests.Sim;

/// <summary>T3.2: info.c's tables against vanilla v1.9.</summary>
public class InfoTests
{
    [Fact]
    public void TableSizesAreVanillas()
    {
        Assert.Equal(967, (int)statenum_t.NUMSTATES);
        Assert.Equal(967, Info.states.Length);
        Assert.Equal(137, (int)mobjtype_t.NUMMOBJTYPES);
        Assert.Equal(137, Info.mobjinfo.Length);
        Assert.Equal(138, (int)spritenum_t.NUMSPRITES);
        Assert.Equal(138, Info.sprnames.Length);
        Assert.Equal(109, (int)sfxenum_t.NUMSFX); // sfx_None and 108 sounds
    }

    [Fact]
    public void SpriteEnumFollowsSprnames()
    {
        for (int i = 0; i < (int)spritenum_t.NUMSPRITES; i++)
            Assert.Equal("SPR_" + SpriteNames.sprnames[i], ((spritenum_t)i).ToString());
    }

    [Fact]
    public void StatesSpotCheck()
    {
        // DeHackEd frame numbers: 149 player, 442 imp, 806 barrel.
        Assert.Equal(149, (int)statenum_t.S_PLAY);
        Assert.Equal(442, (int)statenum_t.S_TROO_STND);
        Assert.Equal(806, (int)statenum_t.S_BAR1);

        Assert.Equal(new state_t(spritenum_t.SPR_TROO, 0, -1, actionf_t.NULL, statenum_t.S_NULL, 0, 0), Info.states[(int)statenum_t.S_NULL]);
        Assert.Equal(new state_t(spritenum_t.SPR_PLAY, 0, -1, actionf_t.NULL, statenum_t.S_NULL, 0, 0), Info.states[(int)statenum_t.S_PLAY]);
        Assert.Equal(new state_t(spritenum_t.SPR_PLAY, 0, 4, actionf_t.NULL, statenum_t.S_PLAY_RUN2, 0, 0), Info.states[(int)statenum_t.S_PLAY_RUN1]);
        Assert.Equal(new state_t(spritenum_t.SPR_TROO, 0, 10, actionf_t.A_Look, statenum_t.S_TROO_STND2, 0, 0), Info.states[(int)statenum_t.S_TROO_STND]);
        Assert.Equal(new state_t(spritenum_t.SPR_TROO, 1, 10, actionf_t.A_Look, statenum_t.S_TROO_STND, 0, 0), Info.states[(int)statenum_t.S_TROO_STND2]);
        Assert.Equal(new state_t(spritenum_t.SPR_BAR1, 0, 6, actionf_t.NULL, statenum_t.S_BAR2, 0, 0), Info.states[(int)statenum_t.S_BAR1]);
        Assert.Equal(new state_t(spritenum_t.SPR_PUNG, 0, 1, actionf_t.A_WeaponReady, statenum_t.S_PUNCH, 0, 0), Info.states[(int)statenum_t.S_PUNCH]);

        // Full-bright frames: the barrel explosion and the last state.
        state_t bexp = Info.states[(int)statenum_t.S_BEXP];
        Assert.Equal((spritenum_t.SPR_BEXP, Info.FF_FULLBRIGHT | 0, 5), (bexp.sprite, bexp.frame, bexp.tics));
        state_t last = Info.states[(int)statenum_t.S_TECH2LAMP4];
        Assert.Equal(32771, last.frame);
        Assert.Equal(3, last.frame & Info.FF_FRAMEMASK);
        Assert.NotEqual(0, last.frame & Info.FF_FULLBRIGHT);
        Assert.Equal(statenum_t.S_TECH2LAMP, last.nextstate);
    }

    [Fact]
    public void MobjinfoSpotCheck()
    {
        mobjinfo_t barrel = Info.mobjinfo[(int)mobjtype_t.MT_BARREL];
        Assert.Equal(2035, barrel.doomednum);
        Assert.Equal(10 * FRACUNIT, barrel.radius);
        Assert.Equal(42 * FRACUNIT, barrel.height);
        Assert.Equal(statenum_t.S_BAR1, barrel.spawnstate);
        Assert.Equal(20, barrel.spawnhealth);
        Assert.Equal(statenum_t.S_BEXP, barrel.deathstate);
        Assert.Equal(sfxenum_t.sfx_barexp, barrel.deathsound);
        Assert.Equal(mobjflag_t.MF_SOLID | mobjflag_t.MF_SHOOTABLE | mobjflag_t.MF_NOBLOOD, barrel.flags);

        mobjinfo_t player = Info.mobjinfo[(int)mobjtype_t.MT_PLAYER];
        Assert.Equal((-1, statenum_t.S_PLAY, 100, 16 * FRACUNIT, 56 * FRACUNIT),
            (player.doomednum, player.spawnstate, player.spawnhealth, player.radius, player.height));
        Assert.Equal(mobjflag_t.MF_SOLID | mobjflag_t.MF_SHOOTABLE | mobjflag_t.MF_DROPOFF | mobjflag_t.MF_PICKUP | mobjflag_t.MF_NOTDMATCH, player.flags);

        mobjinfo_t imp = Info.mobjinfo[(int)mobjtype_t.MT_TROOP];
        Assert.Equal((3001, statenum_t.S_TROO_STND, 60, 8, 20 * FRACUNIT, 56 * FRACUNIT, 200),
            (imp.doomednum, imp.spawnstate, imp.spawnhealth, imp.speed, imp.radius, imp.height, imp.painchance));
        Assert.Equal(mobjflag_t.MF_SOLID | mobjflag_t.MF_SHOOTABLE | mobjflag_t.MF_COUNTKILL, imp.flags);

        // A missile's speed is fixed_t (monsters: map units per step).
        Assert.Equal(10 * FRACUNIT, Info.mobjinfo[(int)mobjtype_t.MT_TROOPSHOT].speed);
        Assert.Equal(3005, Info.mobjinfo[(int)mobjtype_t.MT_HEAD].doomednum);
        Assert.Equal(81, Info.mobjinfo[(int)mobjtype_t.MT_MISC86].doomednum);
    }

    [Fact]
    public void ReferencesStayInRange()
    {
        foreach (state_t s in Info.states)
        {
            Assert.InRange((int)s.sprite, 0, (int)spritenum_t.NUMSPRITES - 1);
            Assert.InRange((int)s.nextstate, 0, (int)statenum_t.NUMSTATES - 1);
            Assert.True(Enum.IsDefined(s.action));
        }
        foreach (mobjinfo_t m in Info.mobjinfo)
        {
            statenum_t[] st = [m.spawnstate, m.seestate, m.painstate, m.meleestate, m.missilestate, m.deathstate, m.xdeathstate, m.raisestate];
            Assert.All(st, x => Assert.InRange((int)x, 0, (int)statenum_t.NUMSTATES - 1));
            sfxenum_t[] sfx = [m.seesound, m.attacksound, m.painsound, m.deathsound, m.activesound];
            Assert.All(sfx, x => Assert.InRange((int)x, 0, (int)sfxenum_t.NUMSFX - 1));
        }

        // Editor numbers are unique (the doomednum lookup takes the first match).
        int[] numbers = [.. Info.mobjinfo.Select(m => m.doomednum).Where(n => n != -1)];
        Assert.Equal(numbers.Length, numbers.Distinct().Count());
    }
}
