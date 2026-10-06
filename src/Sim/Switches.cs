using System.Collections.Generic;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

/// <summary>p_spec.h <c>switchlist_t</c>: a switch texture pair (off, on) and the first episode set that has it.</summary>
public readonly record struct switchlist_t(string name1, string name2, short episode);

/// <summary>
/// p_switch.c's switch texture table (T5.1: the presentation puts both
/// textures of every pair a level uses in its atlas up front, SPEC §12 T5.1).
/// <c>P_InitSwitchList</c>, <c>P_ChangeSwitchTexture</c> and the button
/// timers are in <c>World.Switch.cs</c> (T5.4).
/// </summary>
public static class Switches
{
    /// <summary>p_spec.h <c>MAXSWITCHES</c>: max # of wall switches in a level.</summary>
    public const int MAXSWITCHES = 50;

    /// <summary>
    /// p_switch.c <c>alphSwitchList</c>: CHANGE THE TEXTURE OF A WALL SWITCH TO
    /// ITS OPPOSITE. Episode 1 is shareware Doom's, 2 registered Doom's
    /// episodes 2 and 3, 3 Doom II's (vanilla's terminating empty entry left out).
    /// </summary>
    public static readonly switchlist_t[] alphSwitchList =
    {
        // Doom shareware episode 1 switches
        new("SW1BRCOM", "SW2BRCOM", 1),
        new("SW1BRN1", "SW2BRN1", 1),
        new("SW1BRN2", "SW2BRN2", 1),
        new("SW1BRNGN", "SW2BRNGN", 1),
        new("SW1BROWN", "SW2BROWN", 1),
        new("SW1COMM", "SW2COMM", 1),
        new("SW1COMP", "SW2COMP", 1),
        new("SW1DIRT", "SW2DIRT", 1),
        new("SW1EXIT", "SW2EXIT", 1),
        new("SW1GRAY", "SW2GRAY", 1),
        new("SW1GRAY1", "SW2GRAY1", 1),
        new("SW1METAL", "SW2METAL", 1),
        new("SW1PIPE", "SW2PIPE", 1),
        new("SW1SLAD", "SW2SLAD", 1),
        new("SW1STARG", "SW2STARG", 1),
        new("SW1STON1", "SW2STON1", 1),
        new("SW1STON2", "SW2STON2", 1),
        new("SW1STONE", "SW2STONE", 1),
        new("SW1STRTN", "SW2STRTN", 1),

        // Doom registered episodes 2&3 switches
        new("SW1BLUE", "SW2BLUE", 2),
        new("SW1CMT", "SW2CMT", 2),
        new("SW1GARG", "SW2GARG", 2),
        new("SW1GSTON", "SW2GSTON", 2),
        new("SW1HOT", "SW2HOT", 2),
        new("SW1LION", "SW2LION", 2),
        new("SW1SATYR", "SW2SATYR", 2),
        new("SW1SKIN", "SW2SKIN", 2),
        new("SW1VINE", "SW2VINE", 2),
        new("SW1WOOD", "SW2WOOD", 2),

        // Doom II switches
        new("SW1PANEL", "SW2PANEL", 3),
        new("SW1ROCK", "SW2ROCK", 3),
        new("SW1MET2", "SW2MET2", 3),
        new("SW1WDMET", "SW2WDMET", 3),
        new("SW1BRIK", "SW2BRIK", 3),
        new("SW1MOD1", "SW2MOD1", 3),
        new("SW1ZIM", "SW2ZIM", 3),
        new("SW1STON6", "SW2STON6", 3),
        new("SW1TEK", "SW2TEK", 3),
        new("SW1MARB", "SW2MARB", 3),
        new("SW1SKULL", "SW2SKULL", 3),
    };

    /// <summary>
    /// <c>P_InitSwitchList</c>'s episode set for <paramref name="mode"/>: 1
    /// shareware, 2 registered and retail (Chocolate Doom and the DOS
    /// executables; linuxdoom-1.10 leaves retail at 1), 3 commercial.
    /// </summary>
    public static int Episode(GameMode mode) => mode switch
    {
        GameMode.registered or GameMode.retail => 2,
        GameMode.commercial => 3,
        _ => 1,
    };

    /// <summary>The switch pairs <c>P_InitSwitchList</c> takes for <paramref name="mode"/>, in table order.</summary>
    public static IEnumerable<switchlist_t> For(GameMode mode)
    {
        int episode = Episode(mode);
        foreach (switchlist_t s in alphSwitchList)
        {
            if (s.episode <= episode)
                yield return s;
        }
    }
}
