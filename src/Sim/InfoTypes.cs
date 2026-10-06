using System;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Sim;

// The types of info.h and the frame flags of p_pspr.h, filled by the
// generated tables in Info.cs (info.c).

/// <summary>info.h <c>state_t</c>: one frame of a thing's animation.</summary>
/// <param name="sprite">The sprite (<c>sprites[]</c> index).</param>
/// <param name="frame">The frame letter (0 = A), possibly ORed with <see cref="Info.FF_FULLBRIGHT"/>.</param>
/// <param name="tics">Duration in tics; −1 lasts forever.</param>
/// <param name="action">The action function called when the state is entered.</param>
/// <param name="nextstate">The state after <paramref name="tics"/>.</param>
/// <param name="misc1">Weapon states: the psprite's x offset (p_pspr.c).</param>
/// <param name="misc2">Weapon states: the psprite's y offset.</param>
public readonly record struct state_t(
    spritenum_t sprite,
    int frame,
    int tics,
    actionf_t action,
    statenum_t nextstate,
    int misc1,
    int misc2);

/// <summary>
/// info.h <c>mobjinfo_t</c>: a thing type's properties. Speeds of missiles,
/// radius and height are fixed_t; monster speeds are map units per step.
/// </summary>
public sealed class mobjinfo_t
{
    /// <summary>The editor number (<c>THINGS</c> type), −1 for types that maps cannot place.</summary>
    public int doomednum { get; init; }

    public statenum_t spawnstate { get; init; }
    public int spawnhealth { get; init; }
    public statenum_t seestate { get; init; }
    public sfxenum_t seesound { get; init; }
    public int reactiontime { get; init; }
    public sfxenum_t attacksound { get; init; }
    public statenum_t painstate { get; init; }
    public int painchance { get; init; }
    public sfxenum_t painsound { get; init; }
    public statenum_t meleestate { get; init; }
    public statenum_t missilestate { get; init; }
    public statenum_t deathstate { get; init; }
    public statenum_t xdeathstate { get; init; }
    public sfxenum_t deathsound { get; init; }
    public int speed { get; init; }

    /// <summary>fixed_t.</summary>
    public int radius { get; init; }

    /// <summary>fixed_t.</summary>
    public int height { get; init; }

    public int mass { get; init; }
    public int damage { get; init; }
    public sfxenum_t activesound { get; init; }
    public mobjflag_t flags { get; init; }
    public statenum_t raisestate { get; init; }
}

/// <summary>p_mobj.h <c>mobjflag_t</c>.</summary>
[Flags]
public enum mobjflag_t
{
    /// <summary>Call <c>P_SpecialThing</c> when touched.</summary>
    MF_SPECIAL = 1,
    /// <summary>Blocks.</summary>
    MF_SOLID = 2,
    /// <summary>Can be hit.</summary>
    MF_SHOOTABLE = 4,
    /// <summary>Don't use the sector links (invisible but touchable).</summary>
    MF_NOSECTOR = 8,
    /// <summary>Don't use the blocklinks (inert but displayable).</summary>
    MF_NOBLOCKMAP = 16,
    /// <summary>Not activated by sound: deaf monster.</summary>
    MF_AMBUSH = 32,
    /// <summary>Will try to attack right back.</summary>
    MF_JUSTHIT = 64,
    /// <summary>Will take at least one step before attacking.</summary>
    MF_JUSTATTACKED = 128,
    /// <summary>On level spawning, hang from the ceiling instead of standing on the floor.</summary>
    MF_SPAWNCEILING = 256,
    /// <summary>Don't apply gravity.</summary>
    MF_NOGRAVITY = 512,
    /// <summary>Allows jumps from high places.</summary>
    MF_DROPOFF = 0x400,
    /// <summary>Players: picks up items.</summary>
    MF_PICKUP = 0x800,
    /// <summary>Player cheat.</summary>
    MF_NOCLIP = 0x1000,
    /// <summary>Player: keep info about sliding along walls.</summary>
    MF_SLIDE = 0x2000,
    /// <summary>Active floaters (cacodemons, pain elementals).</summary>
    MF_FLOAT = 0x4000,
    /// <summary>Don't cross lines or look at heights on teleport.</summary>
    MF_TELEPORT = 0x8000,
    /// <summary>Missiles.</summary>
    MF_MISSILE = 0x10000,
    /// <summary>Dropped by a monster, not level spawned.</summary>
    MF_DROPPED = 0x20000,
    /// <summary>Fuzzy draw (spectres, partial invisibility).</summary>
    MF_SHADOW = 0x40000,
    /// <summary>Puffs instead of blood when shot.</summary>
    MF_NOBLOOD = 0x80000,
    /// <summary>Dead bodies slide down steps all the way.</summary>
    MF_CORPSE = 0x100000,
    /// <summary>Floating to a height for a move.</summary>
    MF_INFLOAT = 0x200000,
    /// <summary>Counts towards the intermission kill total.</summary>
    MF_COUNTKILL = 0x400000,
    /// <summary>Counts towards the intermission item total.</summary>
    MF_COUNTITEM = 0x800000,
    /// <summary>Skull in flight.</summary>
    MF_SKULLFLY = 0x1000000,
    /// <summary>Not spawned in deathmatch (e.g. key cards).</summary>
    MF_NOTDMATCH = 0x2000000,
    /// <summary>Player colour translation (0x4, 0x8 or 0xc000000; see <see cref="Info.MF_TRANSSHIFT"/>).</summary>
    MF_TRANSLATION = 0xc000000,
}

public static partial class Info
{
    /// <summary>p_pspr.h <c>FF_FULLBRIGHT</c>: flag in <see cref="state_t.frame"/> for full-bright frames.</summary>
    public const int FF_FULLBRIGHT = 0x8000;

    /// <summary>p_pspr.h <c>FF_FRAMEMASK</c>: the frame number in <see cref="state_t.frame"/>.</summary>
    public const int FF_FRAMEMASK = 0x7fff;

    /// <summary>p_mobj.h <c>MF_TRANSSHIFT</c>: shift of <see cref="mobjflag_t.MF_TRANSLATION"/>.</summary>
    public const int MF_TRANSSHIFT = 26;

    /// <summary>
    /// info.c <c>sprnames</c>, in <see cref="spritenum_t"/> order. One table,
    /// kept in <c>IsoDoom.Wad</c> since T1.5 (sprite indexing needs it too).
    /// </summary>
    public static string[] sprnames => SpriteNames.sprnames;
}
