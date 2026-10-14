using System.Collections.Generic;
using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

// f_finale.c's cast call (T10.5; Chocolate Doom's, the vanilla reference's):
// Doom II's end after MAP30's text, "Casting by id Software, in order of
// appearance". No M_Random: the cast runs on its own counters and the states
// table, its sounds have no origin.
public sealed partial class FFinale
{
    /// <summary>f_finale.c <c>castinfo_t</c>: a cast member's name (d_englsh.h <c>CC_*</c>) and thing type.</summary>
    public readonly record struct castinfo_t(string name, mobjtype_t type);

    /// <summary>f_finale.c <c>castorder</c> (without its <c>{NULL, 0}</c> end: <see cref="F_CastTicker"/> wraps at the table's length).</summary>
    public static readonly castinfo_t[] castorder =
    [
        new("ZOMBIEMAN", mobjtype_t.MT_POSSESSED), // CC_ZOMBIE
        new("SHOTGUN GUY", mobjtype_t.MT_SHOTGUY), // CC_SHOTGUN
        new("HEAVY WEAPON DUDE", mobjtype_t.MT_CHAINGUY), // CC_HEAVY
        new("IMP", mobjtype_t.MT_TROOP), // CC_IMP
        new("DEMON", mobjtype_t.MT_SERGEANT), // CC_DEMON
        new("LOST SOUL", mobjtype_t.MT_SKULL), // CC_LOST
        new("CACODEMON", mobjtype_t.MT_HEAD), // CC_CACO
        new("HELL KNIGHT", mobjtype_t.MT_KNIGHT), // CC_HELL
        new("BARON OF HELL", mobjtype_t.MT_BRUISER), // CC_BARON
        new("ARACHNOTRON", mobjtype_t.MT_BABY), // CC_ARACH
        new("PAIN ELEMENTAL", mobjtype_t.MT_PAIN), // CC_PAIN
        new("REVENANT", mobjtype_t.MT_UNDEAD), // CC_REVEN
        new("MANCUBUS", mobjtype_t.MT_FATSO), // CC_MANCU
        new("ARCH-VILE", mobjtype_t.MT_VILE), // CC_ARCH
        new("THE SPIDER MASTERMIND", mobjtype_t.MT_SPIDER), // CC_SPIDER
        new("THE CYBERDEMON", mobjtype_t.MT_CYBORG), // CC_CYBER
        new("OUR HERO", mobjtype_t.MT_PLAYER), // CC_HERO
    ];

    /// <summary>f_finale.c <c>castnum</c>: the cast member shown (an index of <see cref="castorder"/>).</summary>
    public int castnum;

    /// <summary>f_finale.c <c>casttics</c>: the tics left in <see cref="caststate"/>.</summary>
    public int casttics;

    /// <summary>f_finale.c <c>caststate</c> (vanilla's pointer into <c>states</c>, an index here).</summary>
    public statenum_t caststate;

    /// <summary>f_finale.c <c>castdeath</c>: the member is dying (a key killed it).</summary>
    public bool castdeath;

    /// <summary>f_finale.c <c>castframes</c>: the frames since the member's see state (or its attack, or death).</summary>
    public int castframes;

    /// <summary>f_finale.c <c>castonmelee</c>: the next attack is the melee one (toggled each attack).</summary>
    public int castonmelee;

    /// <summary>f_finale.c <c>castattacking</c>: the member is in its attack frames.</summary>
    public bool castattacking;

    /// <summary>
    /// The sounds the finale started for the last tic (<c>S_StartSound(NULL, …)</c>:
    /// those of a key's <see cref="F_Responder"/> before it, then
    /// <see cref="F_CastTicker"/>'s), for the tests and the overlay.
    /// </summary>
    public readonly List<sfxenum_t> sounds = [];

    // the sounds of F_Responder since the last tic (they go first in the next tic's)
    private readonly List<sfxenum_t> _respondersounds = [];

    private mobjinfo_t CastInfo => Info.mobjinfo[(int)castorder[castnum].type];

    private void S_StartSound(sfxenum_t sfx, List<sfxenum_t> into)
    {
        into.Add(sfx);
        flow.S_StartSound(sfx);
    }

    /// <summary>
    /// f_finale.c <c>F_StartCast</c>: the cast call from its first member,
    /// walking (its see state), a wipe forced and <c>mus_evil</c>. Called by
    /// <see cref="F_Ticker"/> for any button after MAP30's text has shown 50
    /// tics, again by any later one (vanilla's: a mouse click or a pad
    /// button, which the cast's responder lets through to the
    /// <c>ticcmd</c>, starts the cast over).
    /// </summary>
    public void F_StartCast()
    {
        flow.wipegamestate = GameFlow.GS_FORCEWIPE; // force a screen wipe
        castnum = 0;
        caststate = CastInfo.seestate;
        casttics = Info.states[(int)caststate].tics;
        castdeath = false;
        finalestage = F_STAGE_CAST;
        castframes = 0;
        castonmelee = 0;
        castattacking = false;
        flow.S_ChangeMusic(musicenum_t.mus_evil, true);
    }

    /// <summary>
    /// f_finale.c <c>F_CastTicker</c>: the member's states run their tics
    /// (no actions); after a death's last state the next member walks in
    /// (with its see sound; after the hero, the zombieman again); the 12th
    /// frame starts an attack (missile and melee in turn), which ends at the
    /// 24th frame or back in the see state; the attacks' "sound hacks".
    /// </summary>
    private void F_CastTicker()
    {
        if (--casttics > 0)
            return; // not time to change state yet

        bool stopattack = false;
        state_t st = Info.states[(int)caststate];
        if (st.tics == -1 || st.nextstate == statenum_t.S_NULL)
        {
            // switch from deathstate to next monster
            castnum++;
            castdeath = false;
            if (castnum == castorder.Length)
                castnum = 0;
            if (CastInfo.seesound != sfxenum_t.sfx_None)
                S_StartSound(CastInfo.seesound, sounds);
            caststate = CastInfo.seestate;
            castframes = 0;
        }
        else
        {
            // just advance to next state in animation
            if (caststate == statenum_t.S_PLAY_ATK1)
            {
                stopattack = true; // Oh, gross hack!
            }
            else
            {
                statenum_t next = st.nextstate;
                caststate = next;
                castframes++;

                // sound hacks....
                sfxenum_t sfx = next switch
                {
                    statenum_t.S_PLAY_ATK1 => sfxenum_t.sfx_dshtgn,
                    statenum_t.S_POSS_ATK2 => sfxenum_t.sfx_pistol,
                    statenum_t.S_SPOS_ATK2 => sfxenum_t.sfx_shotgn,
                    statenum_t.S_VILE_ATK2 => sfxenum_t.sfx_vilatk,
                    statenum_t.S_SKEL_FIST2 => sfxenum_t.sfx_skeswg,
                    statenum_t.S_SKEL_FIST4 => sfxenum_t.sfx_skepch,
                    statenum_t.S_SKEL_MISS2 => sfxenum_t.sfx_skeatk,
                    statenum_t.S_FATT_ATK8 or statenum_t.S_FATT_ATK5 or statenum_t.S_FATT_ATK2 => sfxenum_t.sfx_firsht,
                    statenum_t.S_CPOS_ATK2 or statenum_t.S_CPOS_ATK3 or statenum_t.S_CPOS_ATK4 => sfxenum_t.sfx_shotgn,
                    statenum_t.S_TROO_ATK3 => sfxenum_t.sfx_claw,
                    statenum_t.S_SARG_ATK2 => sfxenum_t.sfx_sgtatk,
                    statenum_t.S_BOSS_ATK2 or statenum_t.S_BOS2_ATK2 or statenum_t.S_HEAD_ATK2 => sfxenum_t.sfx_firsht,
                    statenum_t.S_SKULL_ATK2 => sfxenum_t.sfx_sklatk,
                    statenum_t.S_SPID_ATK2 or statenum_t.S_SPID_ATK3 => sfxenum_t.sfx_shotgn,
                    statenum_t.S_BSPI_ATK2 => sfxenum_t.sfx_plasma,
                    statenum_t.S_CYBER_ATK2 or statenum_t.S_CYBER_ATK4 or statenum_t.S_CYBER_ATK6 => sfxenum_t.sfx_rlaunc,
                    statenum_t.S_PAIN_ATK3 => sfxenum_t.sfx_sklatk,
                    _ => sfxenum_t.sfx_None,
                };

                if (sfx != sfxenum_t.sfx_None)
                    S_StartSound(sfx, sounds);
            }
        }

        if (!stopattack && castframes == 12)
        {
            // go into attack frame
            castattacking = true;
            caststate = castonmelee != 0 ? CastInfo.meleestate : CastInfo.missilestate;
            castonmelee ^= 1;
            if (caststate == statenum_t.S_NULL)
                caststate = castonmelee != 0 ? CastInfo.meleestate : CastInfo.missilestate;
        }

        if (stopattack || castattacking && (castframes == 24 || caststate == CastInfo.seestate))
        {
            // stopattack:
            castattacking = false;
            castframes = 0;
            caststate = CastInfo.seestate;
        }

        casttics = Info.states[(int)caststate].tics;
        if (casttics == -1)
            casttics = 15;
    }

    /// <summary>
    /// f_finale.c <c>F_Responder</c>: in the cast call a key pressed
    /// (vanilla's <c>ev_keydown</c>: not a mouse button or a pad's, which go
    /// on to the <c>ticcmd</c>) is the cast's (<c>F_CastResponder</c>): the
    /// member dies (its death state and sound), or goes on dying. Returns
    /// whether the finale ate the key.
    /// </summary>
    public bool F_Responder(bool keydown)
    {
        if (finalestage == F_STAGE_CAST)
            return F_CastResponder(keydown);
        return false;
    }

    /// <summary>f_finale.c <c>F_CastResponder</c>.</summary>
    private bool F_CastResponder(bool keydown)
    {
        if (!keydown)
            return false;

        if (castdeath)
            return true; // already in dying frames

        // go into death frame
        castdeath = true;
        caststate = CastInfo.deathstate;
        casttics = Info.states[(int)caststate].tics;
        castframes = 0;
        castattacking = false;
        if (CastInfo.deathsound != sfxenum_t.sfx_None)
            S_StartSound(CastInfo.deathsound, _respondersounds);

        return true;
    }

    /// <summary>
    /// f_finale.c <c>F_CastPrint</c>: <paramref name="text"/> in the message
    /// font centred on x 160 (half its width rounded down), at y 180; a
    /// character the font lacks (a space) 4 wide.
    /// </summary>
    private static void F_CastPrint(ScreenGraphics g, HudScreen screen, string text)
    {
        // find width
        int width = g.TextWidth(text);

        // draw it
        int cx = 160 - width / 2;
        foreach (char ch in text)
        {
            if (!g.HasGlyph(ch))
            {
                cx += 4;
                continue;
            }
            g.DrawChar(screen, cx, 180, ch);
            cx += g.CharWidth(ch);
        }
    }

    /// <summary>
    /// f_finale.c <c>F_CastDrawer</c>: <c>BOSSBACK</c>, the member's name,
    /// and its current frame's first rotation (mirrored when it is the
    /// second of a pair) at 160, 170 less the patch's offsets.
    /// </summary>
    private void F_CastDrawer(ScreenGraphics g, HudScreen screen)
    {
        // erase the entire screen to a background
        g.DrawPage(screen, "BOSSBACK");

        F_CastPrint(g, screen, castorder[castnum].name);

        // draw the current frame in the middle of the screen
        state_t st = Info.states[(int)caststate];
        (IndexedImage? patch, bool flip) = g.SpriteFrame((int)st.sprite, st.frame & Info.FF_FRAMEMASK);
        if (flip)
            screen.V_DrawPatchFlipped(160, 170, patch);
        else
            screen.V_DrawPatch(160, 170, patch);
    }
}
