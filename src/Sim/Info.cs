// Converted mechanically from linuxdoom-1.10 info.h and info.c (id Software,
// GPL-2.0 re-release; Doom v1.9 data, the same values as Chocolate Doom's)
// in T3.2 (SPEC §12). Names, order and values are vanilla's: fix conversion
// errors by hand, never change the data.

using IsoDoom.Wad.Graphics;
using static IsoDoom.Map.Fixed;
using static IsoDoom.Sim.actionf_t;
using static IsoDoom.Sim.mobjflag_t;
using static IsoDoom.Sim.sfxenum_t;
using static IsoDoom.Sim.spritenum_t;
using static IsoDoom.Sim.statenum_t;

namespace IsoDoom.Sim;

/// <summary>info.h <c>spritenum_t</c>: indices into <see cref="SpriteNames.sprnames"/> (and the <c>sprites[]</c> of <c>Sprites.R_InitSprites</c>).</summary>
public enum spritenum_t
{
    SPR_TROO,
    SPR_SHTG,
    SPR_PUNG,
    SPR_PISG,
    SPR_PISF,
    SPR_SHTF,
    SPR_SHT2,
    SPR_CHGG,
    SPR_CHGF,
    SPR_MISG,
    SPR_MISF,
    SPR_SAWG,
    SPR_PLSG,
    SPR_PLSF,
    SPR_BFGG,
    SPR_BFGF,
    SPR_BLUD,
    SPR_PUFF,
    SPR_BAL1,
    SPR_BAL2,
    SPR_PLSS,
    SPR_PLSE,
    SPR_MISL,
    SPR_BFS1,
    SPR_BFE1,
    SPR_BFE2,
    SPR_TFOG,
    SPR_IFOG,
    SPR_PLAY,
    SPR_POSS,
    SPR_SPOS,
    SPR_VILE,
    SPR_FIRE,
    SPR_FATB,
    SPR_FBXP,
    SPR_SKEL,
    SPR_MANF,
    SPR_FATT,
    SPR_CPOS,
    SPR_SARG,
    SPR_HEAD,
    SPR_BAL7,
    SPR_BOSS,
    SPR_BOS2,
    SPR_SKUL,
    SPR_SPID,
    SPR_BSPI,
    SPR_APLS,
    SPR_APBX,
    SPR_CYBR,
    SPR_PAIN,
    SPR_SSWV,
    SPR_KEEN,
    SPR_BBRN,
    SPR_BOSF,
    SPR_ARM1,
    SPR_ARM2,
    SPR_BAR1,
    SPR_BEXP,
    SPR_FCAN,
    SPR_BON1,
    SPR_BON2,
    SPR_BKEY,
    SPR_RKEY,
    SPR_YKEY,
    SPR_BSKU,
    SPR_RSKU,
    SPR_YSKU,
    SPR_STIM,
    SPR_MEDI,
    SPR_SOUL,
    SPR_PINV,
    SPR_PSTR,
    SPR_PINS,
    SPR_MEGA,
    SPR_SUIT,
    SPR_PMAP,
    SPR_PVIS,
    SPR_CLIP,
    SPR_AMMO,
    SPR_ROCK,
    SPR_BROK,
    SPR_CELL,
    SPR_CELP,
    SPR_SHEL,
    SPR_SBOX,
    SPR_BPAK,
    SPR_BFUG,
    SPR_MGUN,
    SPR_CSAW,
    SPR_LAUN,
    SPR_PLAS,
    SPR_SHOT,
    SPR_SGN2,
    SPR_COLU,
    SPR_SMT2,
    SPR_GOR1,
    SPR_POL2,
    SPR_POL5,
    SPR_POL4,
    SPR_POL3,
    SPR_POL1,
    SPR_POL6,
    SPR_GOR2,
    SPR_GOR3,
    SPR_GOR4,
    SPR_GOR5,
    SPR_SMIT,
    SPR_COL1,
    SPR_COL2,
    SPR_COL3,
    SPR_COL4,
    SPR_CAND,
    SPR_CBRA,
    SPR_COL6,
    SPR_TRE1,
    SPR_TRE2,
    SPR_ELEC,
    SPR_CEYE,
    SPR_FSKU,
    SPR_COL5,
    SPR_TBLU,
    SPR_TGRN,
    SPR_TRED,
    SPR_SMBT,
    SPR_SMGT,
    SPR_SMRT,
    SPR_HDB1,
    SPR_HDB2,
    SPR_HDB3,
    SPR_HDB4,
    SPR_HDB5,
    SPR_HDB6,
    SPR_POB1,
    SPR_POB2,
    SPR_BRS1,
    SPR_TLMP,
    SPR_TLP2,
    NUMSPRITES,
}

/// <summary>info.h <c>statenum_t</c>: indices into <see cref="Info.states"/>.</summary>
public enum statenum_t
{
    S_NULL,
    S_LIGHTDONE,
    S_PUNCH,
    S_PUNCHDOWN,
    S_PUNCHUP,
    S_PUNCH1,
    S_PUNCH2,
    S_PUNCH3,
    S_PUNCH4,
    S_PUNCH5,
    S_PISTOL,
    S_PISTOLDOWN,
    S_PISTOLUP,
    S_PISTOL1,
    S_PISTOL2,
    S_PISTOL3,
    S_PISTOL4,
    S_PISTOLFLASH,
    S_SGUN,
    S_SGUNDOWN,
    S_SGUNUP,
    S_SGUN1,
    S_SGUN2,
    S_SGUN3,
    S_SGUN4,
    S_SGUN5,
    S_SGUN6,
    S_SGUN7,
    S_SGUN8,
    S_SGUN9,
    S_SGUNFLASH1,
    S_SGUNFLASH2,
    S_DSGUN,
    S_DSGUNDOWN,
    S_DSGUNUP,
    S_DSGUN1,
    S_DSGUN2,
    S_DSGUN3,
    S_DSGUN4,
    S_DSGUN5,
    S_DSGUN6,
    S_DSGUN7,
    S_DSGUN8,
    S_DSGUN9,
    S_DSGUN10,
    S_DSNR1,
    S_DSNR2,
    S_DSGUNFLASH1,
    S_DSGUNFLASH2,
    S_CHAIN,
    S_CHAINDOWN,
    S_CHAINUP,
    S_CHAIN1,
    S_CHAIN2,
    S_CHAIN3,
    S_CHAINFLASH1,
    S_CHAINFLASH2,
    S_MISSILE,
    S_MISSILEDOWN,
    S_MISSILEUP,
    S_MISSILE1,
    S_MISSILE2,
    S_MISSILE3,
    S_MISSILEFLASH1,
    S_MISSILEFLASH2,
    S_MISSILEFLASH3,
    S_MISSILEFLASH4,
    S_SAW,
    S_SAWB,
    S_SAWDOWN,
    S_SAWUP,
    S_SAW1,
    S_SAW2,
    S_SAW3,
    S_PLASMA,
    S_PLASMADOWN,
    S_PLASMAUP,
    S_PLASMA1,
    S_PLASMA2,
    S_PLASMAFLASH1,
    S_PLASMAFLASH2,
    S_BFG,
    S_BFGDOWN,
    S_BFGUP,
    S_BFG1,
    S_BFG2,
    S_BFG3,
    S_BFG4,
    S_BFGFLASH1,
    S_BFGFLASH2,
    S_BLOOD1,
    S_BLOOD2,
    S_BLOOD3,
    S_PUFF1,
    S_PUFF2,
    S_PUFF3,
    S_PUFF4,
    S_TBALL1,
    S_TBALL2,
    S_TBALLX1,
    S_TBALLX2,
    S_TBALLX3,
    S_RBALL1,
    S_RBALL2,
    S_RBALLX1,
    S_RBALLX2,
    S_RBALLX3,
    S_PLASBALL,
    S_PLASBALL2,
    S_PLASEXP,
    S_PLASEXP2,
    S_PLASEXP3,
    S_PLASEXP4,
    S_PLASEXP5,
    S_ROCKET,
    S_BFGSHOT,
    S_BFGSHOT2,
    S_BFGLAND,
    S_BFGLAND2,
    S_BFGLAND3,
    S_BFGLAND4,
    S_BFGLAND5,
    S_BFGLAND6,
    S_BFGEXP,
    S_BFGEXP2,
    S_BFGEXP3,
    S_BFGEXP4,
    S_EXPLODE1,
    S_EXPLODE2,
    S_EXPLODE3,
    S_TFOG,
    S_TFOG01,
    S_TFOG02,
    S_TFOG2,
    S_TFOG3,
    S_TFOG4,
    S_TFOG5,
    S_TFOG6,
    S_TFOG7,
    S_TFOG8,
    S_TFOG9,
    S_TFOG10,
    S_IFOG,
    S_IFOG01,
    S_IFOG02,
    S_IFOG2,
    S_IFOG3,
    S_IFOG4,
    S_IFOG5,
    S_PLAY,
    S_PLAY_RUN1,
    S_PLAY_RUN2,
    S_PLAY_RUN3,
    S_PLAY_RUN4,
    S_PLAY_ATK1,
    S_PLAY_ATK2,
    S_PLAY_PAIN,
    S_PLAY_PAIN2,
    S_PLAY_DIE1,
    S_PLAY_DIE2,
    S_PLAY_DIE3,
    S_PLAY_DIE4,
    S_PLAY_DIE5,
    S_PLAY_DIE6,
    S_PLAY_DIE7,
    S_PLAY_XDIE1,
    S_PLAY_XDIE2,
    S_PLAY_XDIE3,
    S_PLAY_XDIE4,
    S_PLAY_XDIE5,
    S_PLAY_XDIE6,
    S_PLAY_XDIE7,
    S_PLAY_XDIE8,
    S_PLAY_XDIE9,
    S_POSS_STND,
    S_POSS_STND2,
    S_POSS_RUN1,
    S_POSS_RUN2,
    S_POSS_RUN3,
    S_POSS_RUN4,
    S_POSS_RUN5,
    S_POSS_RUN6,
    S_POSS_RUN7,
    S_POSS_RUN8,
    S_POSS_ATK1,
    S_POSS_ATK2,
    S_POSS_ATK3,
    S_POSS_PAIN,
    S_POSS_PAIN2,
    S_POSS_DIE1,
    S_POSS_DIE2,
    S_POSS_DIE3,
    S_POSS_DIE4,
    S_POSS_DIE5,
    S_POSS_XDIE1,
    S_POSS_XDIE2,
    S_POSS_XDIE3,
    S_POSS_XDIE4,
    S_POSS_XDIE5,
    S_POSS_XDIE6,
    S_POSS_XDIE7,
    S_POSS_XDIE8,
    S_POSS_XDIE9,
    S_POSS_RAISE1,
    S_POSS_RAISE2,
    S_POSS_RAISE3,
    S_POSS_RAISE4,
    S_SPOS_STND,
    S_SPOS_STND2,
    S_SPOS_RUN1,
    S_SPOS_RUN2,
    S_SPOS_RUN3,
    S_SPOS_RUN4,
    S_SPOS_RUN5,
    S_SPOS_RUN6,
    S_SPOS_RUN7,
    S_SPOS_RUN8,
    S_SPOS_ATK1,
    S_SPOS_ATK2,
    S_SPOS_ATK3,
    S_SPOS_PAIN,
    S_SPOS_PAIN2,
    S_SPOS_DIE1,
    S_SPOS_DIE2,
    S_SPOS_DIE3,
    S_SPOS_DIE4,
    S_SPOS_DIE5,
    S_SPOS_XDIE1,
    S_SPOS_XDIE2,
    S_SPOS_XDIE3,
    S_SPOS_XDIE4,
    S_SPOS_XDIE5,
    S_SPOS_XDIE6,
    S_SPOS_XDIE7,
    S_SPOS_XDIE8,
    S_SPOS_XDIE9,
    S_SPOS_RAISE1,
    S_SPOS_RAISE2,
    S_SPOS_RAISE3,
    S_SPOS_RAISE4,
    S_SPOS_RAISE5,
    S_VILE_STND,
    S_VILE_STND2,
    S_VILE_RUN1,
    S_VILE_RUN2,
    S_VILE_RUN3,
    S_VILE_RUN4,
    S_VILE_RUN5,
    S_VILE_RUN6,
    S_VILE_RUN7,
    S_VILE_RUN8,
    S_VILE_RUN9,
    S_VILE_RUN10,
    S_VILE_RUN11,
    S_VILE_RUN12,
    S_VILE_ATK1,
    S_VILE_ATK2,
    S_VILE_ATK3,
    S_VILE_ATK4,
    S_VILE_ATK5,
    S_VILE_ATK6,
    S_VILE_ATK7,
    S_VILE_ATK8,
    S_VILE_ATK9,
    S_VILE_ATK10,
    S_VILE_ATK11,
    S_VILE_HEAL1,
    S_VILE_HEAL2,
    S_VILE_HEAL3,
    S_VILE_PAIN,
    S_VILE_PAIN2,
    S_VILE_DIE1,
    S_VILE_DIE2,
    S_VILE_DIE3,
    S_VILE_DIE4,
    S_VILE_DIE5,
    S_VILE_DIE6,
    S_VILE_DIE7,
    S_VILE_DIE8,
    S_VILE_DIE9,
    S_VILE_DIE10,
    S_FIRE1,
    S_FIRE2,
    S_FIRE3,
    S_FIRE4,
    S_FIRE5,
    S_FIRE6,
    S_FIRE7,
    S_FIRE8,
    S_FIRE9,
    S_FIRE10,
    S_FIRE11,
    S_FIRE12,
    S_FIRE13,
    S_FIRE14,
    S_FIRE15,
    S_FIRE16,
    S_FIRE17,
    S_FIRE18,
    S_FIRE19,
    S_FIRE20,
    S_FIRE21,
    S_FIRE22,
    S_FIRE23,
    S_FIRE24,
    S_FIRE25,
    S_FIRE26,
    S_FIRE27,
    S_FIRE28,
    S_FIRE29,
    S_FIRE30,
    S_SMOKE1,
    S_SMOKE2,
    S_SMOKE3,
    S_SMOKE4,
    S_SMOKE5,
    S_TRACER,
    S_TRACER2,
    S_TRACEEXP1,
    S_TRACEEXP2,
    S_TRACEEXP3,
    S_SKEL_STND,
    S_SKEL_STND2,
    S_SKEL_RUN1,
    S_SKEL_RUN2,
    S_SKEL_RUN3,
    S_SKEL_RUN4,
    S_SKEL_RUN5,
    S_SKEL_RUN6,
    S_SKEL_RUN7,
    S_SKEL_RUN8,
    S_SKEL_RUN9,
    S_SKEL_RUN10,
    S_SKEL_RUN11,
    S_SKEL_RUN12,
    S_SKEL_FIST1,
    S_SKEL_FIST2,
    S_SKEL_FIST3,
    S_SKEL_FIST4,
    S_SKEL_MISS1,
    S_SKEL_MISS2,
    S_SKEL_MISS3,
    S_SKEL_MISS4,
    S_SKEL_PAIN,
    S_SKEL_PAIN2,
    S_SKEL_DIE1,
    S_SKEL_DIE2,
    S_SKEL_DIE3,
    S_SKEL_DIE4,
    S_SKEL_DIE5,
    S_SKEL_DIE6,
    S_SKEL_RAISE1,
    S_SKEL_RAISE2,
    S_SKEL_RAISE3,
    S_SKEL_RAISE4,
    S_SKEL_RAISE5,
    S_SKEL_RAISE6,
    S_FATSHOT1,
    S_FATSHOT2,
    S_FATSHOTX1,
    S_FATSHOTX2,
    S_FATSHOTX3,
    S_FATT_STND,
    S_FATT_STND2,
    S_FATT_RUN1,
    S_FATT_RUN2,
    S_FATT_RUN3,
    S_FATT_RUN4,
    S_FATT_RUN5,
    S_FATT_RUN6,
    S_FATT_RUN7,
    S_FATT_RUN8,
    S_FATT_RUN9,
    S_FATT_RUN10,
    S_FATT_RUN11,
    S_FATT_RUN12,
    S_FATT_ATK1,
    S_FATT_ATK2,
    S_FATT_ATK3,
    S_FATT_ATK4,
    S_FATT_ATK5,
    S_FATT_ATK6,
    S_FATT_ATK7,
    S_FATT_ATK8,
    S_FATT_ATK9,
    S_FATT_ATK10,
    S_FATT_PAIN,
    S_FATT_PAIN2,
    S_FATT_DIE1,
    S_FATT_DIE2,
    S_FATT_DIE3,
    S_FATT_DIE4,
    S_FATT_DIE5,
    S_FATT_DIE6,
    S_FATT_DIE7,
    S_FATT_DIE8,
    S_FATT_DIE9,
    S_FATT_DIE10,
    S_FATT_RAISE1,
    S_FATT_RAISE2,
    S_FATT_RAISE3,
    S_FATT_RAISE4,
    S_FATT_RAISE5,
    S_FATT_RAISE6,
    S_FATT_RAISE7,
    S_FATT_RAISE8,
    S_CPOS_STND,
    S_CPOS_STND2,
    S_CPOS_RUN1,
    S_CPOS_RUN2,
    S_CPOS_RUN3,
    S_CPOS_RUN4,
    S_CPOS_RUN5,
    S_CPOS_RUN6,
    S_CPOS_RUN7,
    S_CPOS_RUN8,
    S_CPOS_ATK1,
    S_CPOS_ATK2,
    S_CPOS_ATK3,
    S_CPOS_ATK4,
    S_CPOS_PAIN,
    S_CPOS_PAIN2,
    S_CPOS_DIE1,
    S_CPOS_DIE2,
    S_CPOS_DIE3,
    S_CPOS_DIE4,
    S_CPOS_DIE5,
    S_CPOS_DIE6,
    S_CPOS_DIE7,
    S_CPOS_XDIE1,
    S_CPOS_XDIE2,
    S_CPOS_XDIE3,
    S_CPOS_XDIE4,
    S_CPOS_XDIE5,
    S_CPOS_XDIE6,
    S_CPOS_RAISE1,
    S_CPOS_RAISE2,
    S_CPOS_RAISE3,
    S_CPOS_RAISE4,
    S_CPOS_RAISE5,
    S_CPOS_RAISE6,
    S_CPOS_RAISE7,
    S_TROO_STND,
    S_TROO_STND2,
    S_TROO_RUN1,
    S_TROO_RUN2,
    S_TROO_RUN3,
    S_TROO_RUN4,
    S_TROO_RUN5,
    S_TROO_RUN6,
    S_TROO_RUN7,
    S_TROO_RUN8,
    S_TROO_ATK1,
    S_TROO_ATK2,
    S_TROO_ATK3,
    S_TROO_PAIN,
    S_TROO_PAIN2,
    S_TROO_DIE1,
    S_TROO_DIE2,
    S_TROO_DIE3,
    S_TROO_DIE4,
    S_TROO_DIE5,
    S_TROO_XDIE1,
    S_TROO_XDIE2,
    S_TROO_XDIE3,
    S_TROO_XDIE4,
    S_TROO_XDIE5,
    S_TROO_XDIE6,
    S_TROO_XDIE7,
    S_TROO_XDIE8,
    S_TROO_RAISE1,
    S_TROO_RAISE2,
    S_TROO_RAISE3,
    S_TROO_RAISE4,
    S_TROO_RAISE5,
    S_SARG_STND,
    S_SARG_STND2,
    S_SARG_RUN1,
    S_SARG_RUN2,
    S_SARG_RUN3,
    S_SARG_RUN4,
    S_SARG_RUN5,
    S_SARG_RUN6,
    S_SARG_RUN7,
    S_SARG_RUN8,
    S_SARG_ATK1,
    S_SARG_ATK2,
    S_SARG_ATK3,
    S_SARG_PAIN,
    S_SARG_PAIN2,
    S_SARG_DIE1,
    S_SARG_DIE2,
    S_SARG_DIE3,
    S_SARG_DIE4,
    S_SARG_DIE5,
    S_SARG_DIE6,
    S_SARG_RAISE1,
    S_SARG_RAISE2,
    S_SARG_RAISE3,
    S_SARG_RAISE4,
    S_SARG_RAISE5,
    S_SARG_RAISE6,
    S_HEAD_STND,
    S_HEAD_RUN1,
    S_HEAD_ATK1,
    S_HEAD_ATK2,
    S_HEAD_ATK3,
    S_HEAD_PAIN,
    S_HEAD_PAIN2,
    S_HEAD_PAIN3,
    S_HEAD_DIE1,
    S_HEAD_DIE2,
    S_HEAD_DIE3,
    S_HEAD_DIE4,
    S_HEAD_DIE5,
    S_HEAD_DIE6,
    S_HEAD_RAISE1,
    S_HEAD_RAISE2,
    S_HEAD_RAISE3,
    S_HEAD_RAISE4,
    S_HEAD_RAISE5,
    S_HEAD_RAISE6,
    S_BRBALL1,
    S_BRBALL2,
    S_BRBALLX1,
    S_BRBALLX2,
    S_BRBALLX3,
    S_BOSS_STND,
    S_BOSS_STND2,
    S_BOSS_RUN1,
    S_BOSS_RUN2,
    S_BOSS_RUN3,
    S_BOSS_RUN4,
    S_BOSS_RUN5,
    S_BOSS_RUN6,
    S_BOSS_RUN7,
    S_BOSS_RUN8,
    S_BOSS_ATK1,
    S_BOSS_ATK2,
    S_BOSS_ATK3,
    S_BOSS_PAIN,
    S_BOSS_PAIN2,
    S_BOSS_DIE1,
    S_BOSS_DIE2,
    S_BOSS_DIE3,
    S_BOSS_DIE4,
    S_BOSS_DIE5,
    S_BOSS_DIE6,
    S_BOSS_DIE7,
    S_BOSS_RAISE1,
    S_BOSS_RAISE2,
    S_BOSS_RAISE3,
    S_BOSS_RAISE4,
    S_BOSS_RAISE5,
    S_BOSS_RAISE6,
    S_BOSS_RAISE7,
    S_BOS2_STND,
    S_BOS2_STND2,
    S_BOS2_RUN1,
    S_BOS2_RUN2,
    S_BOS2_RUN3,
    S_BOS2_RUN4,
    S_BOS2_RUN5,
    S_BOS2_RUN6,
    S_BOS2_RUN7,
    S_BOS2_RUN8,
    S_BOS2_ATK1,
    S_BOS2_ATK2,
    S_BOS2_ATK3,
    S_BOS2_PAIN,
    S_BOS2_PAIN2,
    S_BOS2_DIE1,
    S_BOS2_DIE2,
    S_BOS2_DIE3,
    S_BOS2_DIE4,
    S_BOS2_DIE5,
    S_BOS2_DIE6,
    S_BOS2_DIE7,
    S_BOS2_RAISE1,
    S_BOS2_RAISE2,
    S_BOS2_RAISE3,
    S_BOS2_RAISE4,
    S_BOS2_RAISE5,
    S_BOS2_RAISE6,
    S_BOS2_RAISE7,
    S_SKULL_STND,
    S_SKULL_STND2,
    S_SKULL_RUN1,
    S_SKULL_RUN2,
    S_SKULL_ATK1,
    S_SKULL_ATK2,
    S_SKULL_ATK3,
    S_SKULL_ATK4,
    S_SKULL_PAIN,
    S_SKULL_PAIN2,
    S_SKULL_DIE1,
    S_SKULL_DIE2,
    S_SKULL_DIE3,
    S_SKULL_DIE4,
    S_SKULL_DIE5,
    S_SKULL_DIE6,
    S_SPID_STND,
    S_SPID_STND2,
    S_SPID_RUN1,
    S_SPID_RUN2,
    S_SPID_RUN3,
    S_SPID_RUN4,
    S_SPID_RUN5,
    S_SPID_RUN6,
    S_SPID_RUN7,
    S_SPID_RUN8,
    S_SPID_RUN9,
    S_SPID_RUN10,
    S_SPID_RUN11,
    S_SPID_RUN12,
    S_SPID_ATK1,
    S_SPID_ATK2,
    S_SPID_ATK3,
    S_SPID_ATK4,
    S_SPID_PAIN,
    S_SPID_PAIN2,
    S_SPID_DIE1,
    S_SPID_DIE2,
    S_SPID_DIE3,
    S_SPID_DIE4,
    S_SPID_DIE5,
    S_SPID_DIE6,
    S_SPID_DIE7,
    S_SPID_DIE8,
    S_SPID_DIE9,
    S_SPID_DIE10,
    S_SPID_DIE11,
    S_BSPI_STND,
    S_BSPI_STND2,
    S_BSPI_SIGHT,
    S_BSPI_RUN1,
    S_BSPI_RUN2,
    S_BSPI_RUN3,
    S_BSPI_RUN4,
    S_BSPI_RUN5,
    S_BSPI_RUN6,
    S_BSPI_RUN7,
    S_BSPI_RUN8,
    S_BSPI_RUN9,
    S_BSPI_RUN10,
    S_BSPI_RUN11,
    S_BSPI_RUN12,
    S_BSPI_ATK1,
    S_BSPI_ATK2,
    S_BSPI_ATK3,
    S_BSPI_ATK4,
    S_BSPI_PAIN,
    S_BSPI_PAIN2,
    S_BSPI_DIE1,
    S_BSPI_DIE2,
    S_BSPI_DIE3,
    S_BSPI_DIE4,
    S_BSPI_DIE5,
    S_BSPI_DIE6,
    S_BSPI_DIE7,
    S_BSPI_RAISE1,
    S_BSPI_RAISE2,
    S_BSPI_RAISE3,
    S_BSPI_RAISE4,
    S_BSPI_RAISE5,
    S_BSPI_RAISE6,
    S_BSPI_RAISE7,
    S_ARACH_PLAZ,
    S_ARACH_PLAZ2,
    S_ARACH_PLEX,
    S_ARACH_PLEX2,
    S_ARACH_PLEX3,
    S_ARACH_PLEX4,
    S_ARACH_PLEX5,
    S_CYBER_STND,
    S_CYBER_STND2,
    S_CYBER_RUN1,
    S_CYBER_RUN2,
    S_CYBER_RUN3,
    S_CYBER_RUN4,
    S_CYBER_RUN5,
    S_CYBER_RUN6,
    S_CYBER_RUN7,
    S_CYBER_RUN8,
    S_CYBER_ATK1,
    S_CYBER_ATK2,
    S_CYBER_ATK3,
    S_CYBER_ATK4,
    S_CYBER_ATK5,
    S_CYBER_ATK6,
    S_CYBER_PAIN,
    S_CYBER_DIE1,
    S_CYBER_DIE2,
    S_CYBER_DIE3,
    S_CYBER_DIE4,
    S_CYBER_DIE5,
    S_CYBER_DIE6,
    S_CYBER_DIE7,
    S_CYBER_DIE8,
    S_CYBER_DIE9,
    S_CYBER_DIE10,
    S_PAIN_STND,
    S_PAIN_RUN1,
    S_PAIN_RUN2,
    S_PAIN_RUN3,
    S_PAIN_RUN4,
    S_PAIN_RUN5,
    S_PAIN_RUN6,
    S_PAIN_ATK1,
    S_PAIN_ATK2,
    S_PAIN_ATK3,
    S_PAIN_ATK4,
    S_PAIN_PAIN,
    S_PAIN_PAIN2,
    S_PAIN_DIE1,
    S_PAIN_DIE2,
    S_PAIN_DIE3,
    S_PAIN_DIE4,
    S_PAIN_DIE5,
    S_PAIN_DIE6,
    S_PAIN_RAISE1,
    S_PAIN_RAISE2,
    S_PAIN_RAISE3,
    S_PAIN_RAISE4,
    S_PAIN_RAISE5,
    S_PAIN_RAISE6,
    S_SSWV_STND,
    S_SSWV_STND2,
    S_SSWV_RUN1,
    S_SSWV_RUN2,
    S_SSWV_RUN3,
    S_SSWV_RUN4,
    S_SSWV_RUN5,
    S_SSWV_RUN6,
    S_SSWV_RUN7,
    S_SSWV_RUN8,
    S_SSWV_ATK1,
    S_SSWV_ATK2,
    S_SSWV_ATK3,
    S_SSWV_ATK4,
    S_SSWV_ATK5,
    S_SSWV_ATK6,
    S_SSWV_PAIN,
    S_SSWV_PAIN2,
    S_SSWV_DIE1,
    S_SSWV_DIE2,
    S_SSWV_DIE3,
    S_SSWV_DIE4,
    S_SSWV_DIE5,
    S_SSWV_XDIE1,
    S_SSWV_XDIE2,
    S_SSWV_XDIE3,
    S_SSWV_XDIE4,
    S_SSWV_XDIE5,
    S_SSWV_XDIE6,
    S_SSWV_XDIE7,
    S_SSWV_XDIE8,
    S_SSWV_XDIE9,
    S_SSWV_RAISE1,
    S_SSWV_RAISE2,
    S_SSWV_RAISE3,
    S_SSWV_RAISE4,
    S_SSWV_RAISE5,
    S_KEENSTND,
    S_COMMKEEN,
    S_COMMKEEN2,
    S_COMMKEEN3,
    S_COMMKEEN4,
    S_COMMKEEN5,
    S_COMMKEEN6,
    S_COMMKEEN7,
    S_COMMKEEN8,
    S_COMMKEEN9,
    S_COMMKEEN10,
    S_COMMKEEN11,
    S_COMMKEEN12,
    S_KEENPAIN,
    S_KEENPAIN2,
    S_BRAIN,
    S_BRAIN_PAIN,
    S_BRAIN_DIE1,
    S_BRAIN_DIE2,
    S_BRAIN_DIE3,
    S_BRAIN_DIE4,
    S_BRAINEYE,
    S_BRAINEYESEE,
    S_BRAINEYE1,
    S_SPAWN1,
    S_SPAWN2,
    S_SPAWN3,
    S_SPAWN4,
    S_SPAWNFIRE1,
    S_SPAWNFIRE2,
    S_SPAWNFIRE3,
    S_SPAWNFIRE4,
    S_SPAWNFIRE5,
    S_SPAWNFIRE6,
    S_SPAWNFIRE7,
    S_SPAWNFIRE8,
    S_BRAINEXPLODE1,
    S_BRAINEXPLODE2,
    S_BRAINEXPLODE3,
    S_ARM1,
    S_ARM1A,
    S_ARM2,
    S_ARM2A,
    S_BAR1,
    S_BAR2,
    S_BEXP,
    S_BEXP2,
    S_BEXP3,
    S_BEXP4,
    S_BEXP5,
    S_BBAR1,
    S_BBAR2,
    S_BBAR3,
    S_BON1,
    S_BON1A,
    S_BON1B,
    S_BON1C,
    S_BON1D,
    S_BON1E,
    S_BON2,
    S_BON2A,
    S_BON2B,
    S_BON2C,
    S_BON2D,
    S_BON2E,
    S_BKEY,
    S_BKEY2,
    S_RKEY,
    S_RKEY2,
    S_YKEY,
    S_YKEY2,
    S_BSKULL,
    S_BSKULL2,
    S_RSKULL,
    S_RSKULL2,
    S_YSKULL,
    S_YSKULL2,
    S_STIM,
    S_MEDI,
    S_SOUL,
    S_SOUL2,
    S_SOUL3,
    S_SOUL4,
    S_SOUL5,
    S_SOUL6,
    S_PINV,
    S_PINV2,
    S_PINV3,
    S_PINV4,
    S_PSTR,
    S_PINS,
    S_PINS2,
    S_PINS3,
    S_PINS4,
    S_MEGA,
    S_MEGA2,
    S_MEGA3,
    S_MEGA4,
    S_SUIT,
    S_PMAP,
    S_PMAP2,
    S_PMAP3,
    S_PMAP4,
    S_PMAP5,
    S_PMAP6,
    S_PVIS,
    S_PVIS2,
    S_CLIP,
    S_AMMO,
    S_ROCK,
    S_BROK,
    S_CELL,
    S_CELP,
    S_SHEL,
    S_SBOX,
    S_BPAK,
    S_BFUG,
    S_MGUN,
    S_CSAW,
    S_LAUN,
    S_PLAS,
    S_SHOT,
    S_SHOT2,
    S_COLU,
    S_STALAG,
    S_BLOODYTWITCH,
    S_BLOODYTWITCH2,
    S_BLOODYTWITCH3,
    S_BLOODYTWITCH4,
    S_DEADTORSO,
    S_DEADBOTTOM,
    S_HEADSONSTICK,
    S_GIBS,
    S_HEADONASTICK,
    S_HEADCANDLES,
    S_HEADCANDLES2,
    S_DEADSTICK,
    S_LIVESTICK,
    S_LIVESTICK2,
    S_MEAT2,
    S_MEAT3,
    S_MEAT4,
    S_MEAT5,
    S_STALAGTITE,
    S_TALLGRNCOL,
    S_SHRTGRNCOL,
    S_TALLREDCOL,
    S_SHRTREDCOL,
    S_CANDLESTIK,
    S_CANDELABRA,
    S_SKULLCOL,
    S_TORCHTREE,
    S_BIGTREE,
    S_TECHPILLAR,
    S_EVILEYE,
    S_EVILEYE2,
    S_EVILEYE3,
    S_EVILEYE4,
    S_FLOATSKULL,
    S_FLOATSKULL2,
    S_FLOATSKULL3,
    S_HEARTCOL,
    S_HEARTCOL2,
    S_BLUETORCH,
    S_BLUETORCH2,
    S_BLUETORCH3,
    S_BLUETORCH4,
    S_GREENTORCH,
    S_GREENTORCH2,
    S_GREENTORCH3,
    S_GREENTORCH4,
    S_REDTORCH,
    S_REDTORCH2,
    S_REDTORCH3,
    S_REDTORCH4,
    S_BTORCHSHRT,
    S_BTORCHSHRT2,
    S_BTORCHSHRT3,
    S_BTORCHSHRT4,
    S_GTORCHSHRT,
    S_GTORCHSHRT2,
    S_GTORCHSHRT3,
    S_GTORCHSHRT4,
    S_RTORCHSHRT,
    S_RTORCHSHRT2,
    S_RTORCHSHRT3,
    S_RTORCHSHRT4,
    S_HANGNOGUTS,
    S_HANGBNOBRAIN,
    S_HANGTLOOKDN,
    S_HANGTSKULL,
    S_HANGTLOOKUP,
    S_HANGTNOBRAIN,
    S_COLONGIBS,
    S_SMALLPOOL,
    S_BRAINSTEM,
    S_TECHLAMP,
    S_TECHLAMP2,
    S_TECHLAMP3,
    S_TECHLAMP4,
    S_TECH2LAMP,
    S_TECH2LAMP2,
    S_TECH2LAMP3,
    S_TECH2LAMP4,
    NUMSTATES,
}

/// <summary>info.h <c>mobjtype_t</c>: indices into <see cref="Info.mobjinfo"/>.</summary>
public enum mobjtype_t
{
    MT_PLAYER,
    MT_POSSESSED,
    MT_SHOTGUY,
    MT_VILE,
    MT_FIRE,
    MT_UNDEAD,
    MT_TRACER,
    MT_SMOKE,
    MT_FATSO,
    MT_FATSHOT,
    MT_CHAINGUY,
    MT_TROOP,
    MT_SERGEANT,
    MT_SHADOWS,
    MT_HEAD,
    MT_BRUISER,
    MT_BRUISERSHOT,
    MT_KNIGHT,
    MT_SKULL,
    MT_SPIDER,
    MT_BABY,
    MT_CYBORG,
    MT_PAIN,
    MT_WOLFSS,
    MT_KEEN,
    MT_BOSSBRAIN,
    MT_BOSSSPIT,
    MT_BOSSTARGET,
    MT_SPAWNSHOT,
    MT_SPAWNFIRE,
    MT_BARREL,
    MT_TROOPSHOT,
    MT_HEADSHOT,
    MT_ROCKET,
    MT_PLASMA,
    MT_BFG,
    MT_ARACHPLAZ,
    MT_PUFF,
    MT_BLOOD,
    MT_TFOG,
    MT_IFOG,
    MT_TELEPORTMAN,
    MT_EXTRABFG,
    MT_MISC0,
    MT_MISC1,
    MT_MISC2,
    MT_MISC3,
    MT_MISC4,
    MT_MISC5,
    MT_MISC6,
    MT_MISC7,
    MT_MISC8,
    MT_MISC9,
    MT_MISC10,
    MT_MISC11,
    MT_MISC12,
    MT_INV,
    MT_MISC13,
    MT_INS,
    MT_MISC14,
    MT_MISC15,
    MT_MISC16,
    MT_MEGA,
    MT_CLIP,
    MT_MISC17,
    MT_MISC18,
    MT_MISC19,
    MT_MISC20,
    MT_MISC21,
    MT_MISC22,
    MT_MISC23,
    MT_MISC24,
    MT_MISC25,
    MT_CHAINGUN,
    MT_MISC26,
    MT_MISC27,
    MT_MISC28,
    MT_SHOTGUN,
    MT_SUPERSHOTGUN,
    MT_MISC29,
    MT_MISC30,
    MT_MISC31,
    MT_MISC32,
    MT_MISC33,
    MT_MISC34,
    MT_MISC35,
    MT_MISC36,
    MT_MISC37,
    MT_MISC38,
    MT_MISC39,
    MT_MISC40,
    MT_MISC41,
    MT_MISC42,
    MT_MISC43,
    MT_MISC44,
    MT_MISC45,
    MT_MISC46,
    MT_MISC47,
    MT_MISC48,
    MT_MISC49,
    MT_MISC50,
    MT_MISC51,
    MT_MISC52,
    MT_MISC53,
    MT_MISC54,
    MT_MISC55,
    MT_MISC56,
    MT_MISC57,
    MT_MISC58,
    MT_MISC59,
    MT_MISC60,
    MT_MISC61,
    MT_MISC62,
    MT_MISC63,
    MT_MISC64,
    MT_MISC65,
    MT_MISC66,
    MT_MISC67,
    MT_MISC68,
    MT_MISC69,
    MT_MISC70,
    MT_MISC71,
    MT_MISC72,
    MT_MISC73,
    MT_MISC74,
    MT_MISC75,
    MT_MISC76,
    MT_MISC77,
    MT_MISC78,
    MT_MISC79,
    MT_MISC80,
    MT_MISC81,
    MT_MISC82,
    MT_MISC83,
    MT_MISC84,
    MT_MISC85,
    MT_MISC86,
    NUMMOBJTYPES,
}

/// <summary>
/// The action functions info.c declares, in declaration order, as a named
/// enum (<c>NULL</c> = no action; vanilla's <c>actionf_t</c> is a function
/// pointer). The bodies come in M4–M6 (p_enemy.c, p_pspr.c).
/// </summary>
public enum actionf_t
{
    NULL,
    A_Light0,
    A_WeaponReady,
    A_Lower,
    A_Raise,
    A_Punch,
    A_ReFire,
    A_FirePistol,
    A_Light1,
    A_FireShotgun,
    A_Light2,
    A_FireShotgun2,
    A_CheckReload,
    A_OpenShotgun2,
    A_LoadShotgun2,
    A_CloseShotgun2,
    A_FireCGun,
    A_GunFlash,
    A_FireMissile,
    A_Saw,
    A_FirePlasma,
    A_BFGsound,
    A_FireBFG,
    A_BFGSpray,
    A_Explode,
    A_Pain,
    A_PlayerScream,
    A_Fall,
    A_XScream,
    A_Look,
    A_Chase,
    A_FaceTarget,
    A_PosAttack,
    A_Scream,
    A_SPosAttack,
    A_VileChase,
    A_VileStart,
    A_VileTarget,
    A_VileAttack,
    A_StartFire,
    A_Fire,
    A_FireCrackle,
    A_Tracer,
    A_SkelWhoosh,
    A_SkelFist,
    A_SkelMissile,
    A_FatRaise,
    A_FatAttack1,
    A_FatAttack2,
    A_FatAttack3,
    A_BossDeath,
    A_CPosAttack,
    A_CPosRefire,
    A_TroopAttack,
    A_SargAttack,
    A_HeadAttack,
    A_BruisAttack,
    A_SkullAttack,
    A_Metal,
    A_SpidRefire,
    A_BabyMetal,
    A_BspiAttack,
    A_Hoof,
    A_CyberAttack,
    A_PainAttack,
    A_PainDie,
    A_KeenDie,
    A_BrainPain,
    A_BrainScream,
    A_BrainDie,
    A_BrainAwake,
    A_BrainSpit,
    A_SpawnSound,
    A_SpawnFly,
    A_BrainExplode,
}

/// <summary>info.c's tables.</summary>
public static partial class Info
{
    /// <summary>info.c <c>states[NUMSTATES]</c>.</summary>
    public static readonly state_t[] states =
    [
        new(SPR_TROO, 0, -1, NULL, S_NULL, 0, 0), // S_NULL
        new(SPR_SHTG, 4, 0, A_Light0, S_NULL, 0, 0), // S_LIGHTDONE
        new(SPR_PUNG, 0, 1, A_WeaponReady, S_PUNCH, 0, 0), // S_PUNCH
        new(SPR_PUNG, 0, 1, A_Lower, S_PUNCHDOWN, 0, 0), // S_PUNCHDOWN
        new(SPR_PUNG, 0, 1, A_Raise, S_PUNCHUP, 0, 0), // S_PUNCHUP
        new(SPR_PUNG, 1, 4, NULL, S_PUNCH2, 0, 0), // S_PUNCH1
        new(SPR_PUNG, 2, 4, A_Punch, S_PUNCH3, 0, 0), // S_PUNCH2
        new(SPR_PUNG, 3, 5, NULL, S_PUNCH4, 0, 0), // S_PUNCH3
        new(SPR_PUNG, 2, 4, NULL, S_PUNCH5, 0, 0), // S_PUNCH4
        new(SPR_PUNG, 1, 5, A_ReFire, S_PUNCH, 0, 0), // S_PUNCH5
        new(SPR_PISG, 0, 1, A_WeaponReady, S_PISTOL, 0, 0), // S_PISTOL
        new(SPR_PISG, 0, 1, A_Lower, S_PISTOLDOWN, 0, 0), // S_PISTOLDOWN
        new(SPR_PISG, 0, 1, A_Raise, S_PISTOLUP, 0, 0), // S_PISTOLUP
        new(SPR_PISG, 0, 4, NULL, S_PISTOL2, 0, 0), // S_PISTOL1
        new(SPR_PISG, 1, 6, A_FirePistol, S_PISTOL3, 0, 0), // S_PISTOL2
        new(SPR_PISG, 2, 4, NULL, S_PISTOL4, 0, 0), // S_PISTOL3
        new(SPR_PISG, 1, 5, A_ReFire, S_PISTOL, 0, 0), // S_PISTOL4
        new(SPR_PISF, 32768, 7, A_Light1, S_LIGHTDONE, 0, 0), // S_PISTOLFLASH
        new(SPR_SHTG, 0, 1, A_WeaponReady, S_SGUN, 0, 0), // S_SGUN
        new(SPR_SHTG, 0, 1, A_Lower, S_SGUNDOWN, 0, 0), // S_SGUNDOWN
        new(SPR_SHTG, 0, 1, A_Raise, S_SGUNUP, 0, 0), // S_SGUNUP
        new(SPR_SHTG, 0, 3, NULL, S_SGUN2, 0, 0), // S_SGUN1
        new(SPR_SHTG, 0, 7, A_FireShotgun, S_SGUN3, 0, 0), // S_SGUN2
        new(SPR_SHTG, 1, 5, NULL, S_SGUN4, 0, 0), // S_SGUN3
        new(SPR_SHTG, 2, 5, NULL, S_SGUN5, 0, 0), // S_SGUN4
        new(SPR_SHTG, 3, 4, NULL, S_SGUN6, 0, 0), // S_SGUN5
        new(SPR_SHTG, 2, 5, NULL, S_SGUN7, 0, 0), // S_SGUN6
        new(SPR_SHTG, 1, 5, NULL, S_SGUN8, 0, 0), // S_SGUN7
        new(SPR_SHTG, 0, 3, NULL, S_SGUN9, 0, 0), // S_SGUN8
        new(SPR_SHTG, 0, 7, A_ReFire, S_SGUN, 0, 0), // S_SGUN9
        new(SPR_SHTF, 32768, 4, A_Light1, S_SGUNFLASH2, 0, 0), // S_SGUNFLASH1
        new(SPR_SHTF, 32769, 3, A_Light2, S_LIGHTDONE, 0, 0), // S_SGUNFLASH2
        new(SPR_SHT2, 0, 1, A_WeaponReady, S_DSGUN, 0, 0), // S_DSGUN
        new(SPR_SHT2, 0, 1, A_Lower, S_DSGUNDOWN, 0, 0), // S_DSGUNDOWN
        new(SPR_SHT2, 0, 1, A_Raise, S_DSGUNUP, 0, 0), // S_DSGUNUP
        new(SPR_SHT2, 0, 3, NULL, S_DSGUN2, 0, 0), // S_DSGUN1
        new(SPR_SHT2, 0, 7, A_FireShotgun2, S_DSGUN3, 0, 0), // S_DSGUN2
        new(SPR_SHT2, 1, 7, NULL, S_DSGUN4, 0, 0), // S_DSGUN3
        new(SPR_SHT2, 2, 7, A_CheckReload, S_DSGUN5, 0, 0), // S_DSGUN4
        new(SPR_SHT2, 3, 7, A_OpenShotgun2, S_DSGUN6, 0, 0), // S_DSGUN5
        new(SPR_SHT2, 4, 7, NULL, S_DSGUN7, 0, 0), // S_DSGUN6
        new(SPR_SHT2, 5, 7, A_LoadShotgun2, S_DSGUN8, 0, 0), // S_DSGUN7
        new(SPR_SHT2, 6, 6, NULL, S_DSGUN9, 0, 0), // S_DSGUN8
        new(SPR_SHT2, 7, 6, A_CloseShotgun2, S_DSGUN10, 0, 0), // S_DSGUN9
        new(SPR_SHT2, 0, 5, A_ReFire, S_DSGUN, 0, 0), // S_DSGUN10
        new(SPR_SHT2, 1, 7, NULL, S_DSNR2, 0, 0), // S_DSNR1
        new(SPR_SHT2, 0, 3, NULL, S_DSGUNDOWN, 0, 0), // S_DSNR2
        new(SPR_SHT2, 32776, 5, A_Light1, S_DSGUNFLASH2, 0, 0), // S_DSGUNFLASH1
        new(SPR_SHT2, 32777, 4, A_Light2, S_LIGHTDONE, 0, 0), // S_DSGUNFLASH2
        new(SPR_CHGG, 0, 1, A_WeaponReady, S_CHAIN, 0, 0), // S_CHAIN
        new(SPR_CHGG, 0, 1, A_Lower, S_CHAINDOWN, 0, 0), // S_CHAINDOWN
        new(SPR_CHGG, 0, 1, A_Raise, S_CHAINUP, 0, 0), // S_CHAINUP
        new(SPR_CHGG, 0, 4, A_FireCGun, S_CHAIN2, 0, 0), // S_CHAIN1
        new(SPR_CHGG, 1, 4, A_FireCGun, S_CHAIN3, 0, 0), // S_CHAIN2
        new(SPR_CHGG, 1, 0, A_ReFire, S_CHAIN, 0, 0), // S_CHAIN3
        new(SPR_CHGF, 32768, 5, A_Light1, S_LIGHTDONE, 0, 0), // S_CHAINFLASH1
        new(SPR_CHGF, 32769, 5, A_Light2, S_LIGHTDONE, 0, 0), // S_CHAINFLASH2
        new(SPR_MISG, 0, 1, A_WeaponReady, S_MISSILE, 0, 0), // S_MISSILE
        new(SPR_MISG, 0, 1, A_Lower, S_MISSILEDOWN, 0, 0), // S_MISSILEDOWN
        new(SPR_MISG, 0, 1, A_Raise, S_MISSILEUP, 0, 0), // S_MISSILEUP
        new(SPR_MISG, 1, 8, A_GunFlash, S_MISSILE2, 0, 0), // S_MISSILE1
        new(SPR_MISG, 1, 12, A_FireMissile, S_MISSILE3, 0, 0), // S_MISSILE2
        new(SPR_MISG, 1, 0, A_ReFire, S_MISSILE, 0, 0), // S_MISSILE3
        new(SPR_MISF, 32768, 3, A_Light1, S_MISSILEFLASH2, 0, 0), // S_MISSILEFLASH1
        new(SPR_MISF, 32769, 4, NULL, S_MISSILEFLASH3, 0, 0), // S_MISSILEFLASH2
        new(SPR_MISF, 32770, 4, A_Light2, S_MISSILEFLASH4, 0, 0), // S_MISSILEFLASH3
        new(SPR_MISF, 32771, 4, A_Light2, S_LIGHTDONE, 0, 0), // S_MISSILEFLASH4
        new(SPR_SAWG, 2, 4, A_WeaponReady, S_SAWB, 0, 0), // S_SAW
        new(SPR_SAWG, 3, 4, A_WeaponReady, S_SAW, 0, 0), // S_SAWB
        new(SPR_SAWG, 2, 1, A_Lower, S_SAWDOWN, 0, 0), // S_SAWDOWN
        new(SPR_SAWG, 2, 1, A_Raise, S_SAWUP, 0, 0), // S_SAWUP
        new(SPR_SAWG, 0, 4, A_Saw, S_SAW2, 0, 0), // S_SAW1
        new(SPR_SAWG, 1, 4, A_Saw, S_SAW3, 0, 0), // S_SAW2
        new(SPR_SAWG, 1, 0, A_ReFire, S_SAW, 0, 0), // S_SAW3
        new(SPR_PLSG, 0, 1, A_WeaponReady, S_PLASMA, 0, 0), // S_PLASMA
        new(SPR_PLSG, 0, 1, A_Lower, S_PLASMADOWN, 0, 0), // S_PLASMADOWN
        new(SPR_PLSG, 0, 1, A_Raise, S_PLASMAUP, 0, 0), // S_PLASMAUP
        new(SPR_PLSG, 0, 3, A_FirePlasma, S_PLASMA2, 0, 0), // S_PLASMA1
        new(SPR_PLSG, 1, 20, A_ReFire, S_PLASMA, 0, 0), // S_PLASMA2
        new(SPR_PLSF, 32768, 4, A_Light1, S_LIGHTDONE, 0, 0), // S_PLASMAFLASH1
        new(SPR_PLSF, 32769, 4, A_Light1, S_LIGHTDONE, 0, 0), // S_PLASMAFLASH2
        new(SPR_BFGG, 0, 1, A_WeaponReady, S_BFG, 0, 0), // S_BFG
        new(SPR_BFGG, 0, 1, A_Lower, S_BFGDOWN, 0, 0), // S_BFGDOWN
        new(SPR_BFGG, 0, 1, A_Raise, S_BFGUP, 0, 0), // S_BFGUP
        new(SPR_BFGG, 0, 20, A_BFGsound, S_BFG2, 0, 0), // S_BFG1
        new(SPR_BFGG, 1, 10, A_GunFlash, S_BFG3, 0, 0), // S_BFG2
        new(SPR_BFGG, 1, 10, A_FireBFG, S_BFG4, 0, 0), // S_BFG3
        new(SPR_BFGG, 1, 20, A_ReFire, S_BFG, 0, 0), // S_BFG4
        new(SPR_BFGF, 32768, 11, A_Light1, S_BFGFLASH2, 0, 0), // S_BFGFLASH1
        new(SPR_BFGF, 32769, 6, A_Light2, S_LIGHTDONE, 0, 0), // S_BFGFLASH2
        new(SPR_BLUD, 2, 8, NULL, S_BLOOD2, 0, 0), // S_BLOOD1
        new(SPR_BLUD, 1, 8, NULL, S_BLOOD3, 0, 0), // S_BLOOD2
        new(SPR_BLUD, 0, 8, NULL, S_NULL, 0, 0), // S_BLOOD3
        new(SPR_PUFF, 32768, 4, NULL, S_PUFF2, 0, 0), // S_PUFF1
        new(SPR_PUFF, 1, 4, NULL, S_PUFF3, 0, 0), // S_PUFF2
        new(SPR_PUFF, 2, 4, NULL, S_PUFF4, 0, 0), // S_PUFF3
        new(SPR_PUFF, 3, 4, NULL, S_NULL, 0, 0), // S_PUFF4
        new(SPR_BAL1, 32768, 4, NULL, S_TBALL2, 0, 0), // S_TBALL1
        new(SPR_BAL1, 32769, 4, NULL, S_TBALL1, 0, 0), // S_TBALL2
        new(SPR_BAL1, 32770, 6, NULL, S_TBALLX2, 0, 0), // S_TBALLX1
        new(SPR_BAL1, 32771, 6, NULL, S_TBALLX3, 0, 0), // S_TBALLX2
        new(SPR_BAL1, 32772, 6, NULL, S_NULL, 0, 0), // S_TBALLX3
        new(SPR_BAL2, 32768, 4, NULL, S_RBALL2, 0, 0), // S_RBALL1
        new(SPR_BAL2, 32769, 4, NULL, S_RBALL1, 0, 0), // S_RBALL2
        new(SPR_BAL2, 32770, 6, NULL, S_RBALLX2, 0, 0), // S_RBALLX1
        new(SPR_BAL2, 32771, 6, NULL, S_RBALLX3, 0, 0), // S_RBALLX2
        new(SPR_BAL2, 32772, 6, NULL, S_NULL, 0, 0), // S_RBALLX3
        new(SPR_PLSS, 32768, 6, NULL, S_PLASBALL2, 0, 0), // S_PLASBALL
        new(SPR_PLSS, 32769, 6, NULL, S_PLASBALL, 0, 0), // S_PLASBALL2
        new(SPR_PLSE, 32768, 4, NULL, S_PLASEXP2, 0, 0), // S_PLASEXP
        new(SPR_PLSE, 32769, 4, NULL, S_PLASEXP3, 0, 0), // S_PLASEXP2
        new(SPR_PLSE, 32770, 4, NULL, S_PLASEXP4, 0, 0), // S_PLASEXP3
        new(SPR_PLSE, 32771, 4, NULL, S_PLASEXP5, 0, 0), // S_PLASEXP4
        new(SPR_PLSE, 32772, 4, NULL, S_NULL, 0, 0), // S_PLASEXP5
        new(SPR_MISL, 32768, 1, NULL, S_ROCKET, 0, 0), // S_ROCKET
        new(SPR_BFS1, 32768, 4, NULL, S_BFGSHOT2, 0, 0), // S_BFGSHOT
        new(SPR_BFS1, 32769, 4, NULL, S_BFGSHOT, 0, 0), // S_BFGSHOT2
        new(SPR_BFE1, 32768, 8, NULL, S_BFGLAND2, 0, 0), // S_BFGLAND
        new(SPR_BFE1, 32769, 8, NULL, S_BFGLAND3, 0, 0), // S_BFGLAND2
        new(SPR_BFE1, 32770, 8, A_BFGSpray, S_BFGLAND4, 0, 0), // S_BFGLAND3
        new(SPR_BFE1, 32771, 8, NULL, S_BFGLAND5, 0, 0), // S_BFGLAND4
        new(SPR_BFE1, 32772, 8, NULL, S_BFGLAND6, 0, 0), // S_BFGLAND5
        new(SPR_BFE1, 32773, 8, NULL, S_NULL, 0, 0), // S_BFGLAND6
        new(SPR_BFE2, 32768, 8, NULL, S_BFGEXP2, 0, 0), // S_BFGEXP
        new(SPR_BFE2, 32769, 8, NULL, S_BFGEXP3, 0, 0), // S_BFGEXP2
        new(SPR_BFE2, 32770, 8, NULL, S_BFGEXP4, 0, 0), // S_BFGEXP3
        new(SPR_BFE2, 32771, 8, NULL, S_NULL, 0, 0), // S_BFGEXP4
        new(SPR_MISL, 32769, 8, A_Explode, S_EXPLODE2, 0, 0), // S_EXPLODE1
        new(SPR_MISL, 32770, 6, NULL, S_EXPLODE3, 0, 0), // S_EXPLODE2
        new(SPR_MISL, 32771, 4, NULL, S_NULL, 0, 0), // S_EXPLODE3
        new(SPR_TFOG, 32768, 6, NULL, S_TFOG01, 0, 0), // S_TFOG
        new(SPR_TFOG, 32769, 6, NULL, S_TFOG02, 0, 0), // S_TFOG01
        new(SPR_TFOG, 32768, 6, NULL, S_TFOG2, 0, 0), // S_TFOG02
        new(SPR_TFOG, 32769, 6, NULL, S_TFOG3, 0, 0), // S_TFOG2
        new(SPR_TFOG, 32770, 6, NULL, S_TFOG4, 0, 0), // S_TFOG3
        new(SPR_TFOG, 32771, 6, NULL, S_TFOG5, 0, 0), // S_TFOG4
        new(SPR_TFOG, 32772, 6, NULL, S_TFOG6, 0, 0), // S_TFOG5
        new(SPR_TFOG, 32773, 6, NULL, S_TFOG7, 0, 0), // S_TFOG6
        new(SPR_TFOG, 32774, 6, NULL, S_TFOG8, 0, 0), // S_TFOG7
        new(SPR_TFOG, 32775, 6, NULL, S_TFOG9, 0, 0), // S_TFOG8
        new(SPR_TFOG, 32776, 6, NULL, S_TFOG10, 0, 0), // S_TFOG9
        new(SPR_TFOG, 32777, 6, NULL, S_NULL, 0, 0), // S_TFOG10
        new(SPR_IFOG, 32768, 6, NULL, S_IFOG01, 0, 0), // S_IFOG
        new(SPR_IFOG, 32769, 6, NULL, S_IFOG02, 0, 0), // S_IFOG01
        new(SPR_IFOG, 32768, 6, NULL, S_IFOG2, 0, 0), // S_IFOG02
        new(SPR_IFOG, 32769, 6, NULL, S_IFOG3, 0, 0), // S_IFOG2
        new(SPR_IFOG, 32770, 6, NULL, S_IFOG4, 0, 0), // S_IFOG3
        new(SPR_IFOG, 32771, 6, NULL, S_IFOG5, 0, 0), // S_IFOG4
        new(SPR_IFOG, 32772, 6, NULL, S_NULL, 0, 0), // S_IFOG5
        new(SPR_PLAY, 0, -1, NULL, S_NULL, 0, 0), // S_PLAY
        new(SPR_PLAY, 0, 4, NULL, S_PLAY_RUN2, 0, 0), // S_PLAY_RUN1
        new(SPR_PLAY, 1, 4, NULL, S_PLAY_RUN3, 0, 0), // S_PLAY_RUN2
        new(SPR_PLAY, 2, 4, NULL, S_PLAY_RUN4, 0, 0), // S_PLAY_RUN3
        new(SPR_PLAY, 3, 4, NULL, S_PLAY_RUN1, 0, 0), // S_PLAY_RUN4
        new(SPR_PLAY, 4, 12, NULL, S_PLAY, 0, 0), // S_PLAY_ATK1
        new(SPR_PLAY, 32773, 6, NULL, S_PLAY_ATK1, 0, 0), // S_PLAY_ATK2
        new(SPR_PLAY, 6, 4, NULL, S_PLAY_PAIN2, 0, 0), // S_PLAY_PAIN
        new(SPR_PLAY, 6, 4, A_Pain, S_PLAY, 0, 0), // S_PLAY_PAIN2
        new(SPR_PLAY, 7, 10, NULL, S_PLAY_DIE2, 0, 0), // S_PLAY_DIE1
        new(SPR_PLAY, 8, 10, A_PlayerScream, S_PLAY_DIE3, 0, 0), // S_PLAY_DIE2
        new(SPR_PLAY, 9, 10, A_Fall, S_PLAY_DIE4, 0, 0), // S_PLAY_DIE3
        new(SPR_PLAY, 10, 10, NULL, S_PLAY_DIE5, 0, 0), // S_PLAY_DIE4
        new(SPR_PLAY, 11, 10, NULL, S_PLAY_DIE6, 0, 0), // S_PLAY_DIE5
        new(SPR_PLAY, 12, 10, NULL, S_PLAY_DIE7, 0, 0), // S_PLAY_DIE6
        new(SPR_PLAY, 13, -1, NULL, S_NULL, 0, 0), // S_PLAY_DIE7
        new(SPR_PLAY, 14, 5, NULL, S_PLAY_XDIE2, 0, 0), // S_PLAY_XDIE1
        new(SPR_PLAY, 15, 5, A_XScream, S_PLAY_XDIE3, 0, 0), // S_PLAY_XDIE2
        new(SPR_PLAY, 16, 5, A_Fall, S_PLAY_XDIE4, 0, 0), // S_PLAY_XDIE3
        new(SPR_PLAY, 17, 5, NULL, S_PLAY_XDIE5, 0, 0), // S_PLAY_XDIE4
        new(SPR_PLAY, 18, 5, NULL, S_PLAY_XDIE6, 0, 0), // S_PLAY_XDIE5
        new(SPR_PLAY, 19, 5, NULL, S_PLAY_XDIE7, 0, 0), // S_PLAY_XDIE6
        new(SPR_PLAY, 20, 5, NULL, S_PLAY_XDIE8, 0, 0), // S_PLAY_XDIE7
        new(SPR_PLAY, 21, 5, NULL, S_PLAY_XDIE9, 0, 0), // S_PLAY_XDIE8
        new(SPR_PLAY, 22, -1, NULL, S_NULL, 0, 0), // S_PLAY_XDIE9
        new(SPR_POSS, 0, 10, A_Look, S_POSS_STND2, 0, 0), // S_POSS_STND
        new(SPR_POSS, 1, 10, A_Look, S_POSS_STND, 0, 0), // S_POSS_STND2
        new(SPR_POSS, 0, 4, A_Chase, S_POSS_RUN2, 0, 0), // S_POSS_RUN1
        new(SPR_POSS, 0, 4, A_Chase, S_POSS_RUN3, 0, 0), // S_POSS_RUN2
        new(SPR_POSS, 1, 4, A_Chase, S_POSS_RUN4, 0, 0), // S_POSS_RUN3
        new(SPR_POSS, 1, 4, A_Chase, S_POSS_RUN5, 0, 0), // S_POSS_RUN4
        new(SPR_POSS, 2, 4, A_Chase, S_POSS_RUN6, 0, 0), // S_POSS_RUN5
        new(SPR_POSS, 2, 4, A_Chase, S_POSS_RUN7, 0, 0), // S_POSS_RUN6
        new(SPR_POSS, 3, 4, A_Chase, S_POSS_RUN8, 0, 0), // S_POSS_RUN7
        new(SPR_POSS, 3, 4, A_Chase, S_POSS_RUN1, 0, 0), // S_POSS_RUN8
        new(SPR_POSS, 4, 10, A_FaceTarget, S_POSS_ATK2, 0, 0), // S_POSS_ATK1
        new(SPR_POSS, 5, 8, A_PosAttack, S_POSS_ATK3, 0, 0), // S_POSS_ATK2
        new(SPR_POSS, 4, 8, NULL, S_POSS_RUN1, 0, 0), // S_POSS_ATK3
        new(SPR_POSS, 6, 3, NULL, S_POSS_PAIN2, 0, 0), // S_POSS_PAIN
        new(SPR_POSS, 6, 3, A_Pain, S_POSS_RUN1, 0, 0), // S_POSS_PAIN2
        new(SPR_POSS, 7, 5, NULL, S_POSS_DIE2, 0, 0), // S_POSS_DIE1
        new(SPR_POSS, 8, 5, A_Scream, S_POSS_DIE3, 0, 0), // S_POSS_DIE2
        new(SPR_POSS, 9, 5, A_Fall, S_POSS_DIE4, 0, 0), // S_POSS_DIE3
        new(SPR_POSS, 10, 5, NULL, S_POSS_DIE5, 0, 0), // S_POSS_DIE4
        new(SPR_POSS, 11, -1, NULL, S_NULL, 0, 0), // S_POSS_DIE5
        new(SPR_POSS, 12, 5, NULL, S_POSS_XDIE2, 0, 0), // S_POSS_XDIE1
        new(SPR_POSS, 13, 5, A_XScream, S_POSS_XDIE3, 0, 0), // S_POSS_XDIE2
        new(SPR_POSS, 14, 5, A_Fall, S_POSS_XDIE4, 0, 0), // S_POSS_XDIE3
        new(SPR_POSS, 15, 5, NULL, S_POSS_XDIE5, 0, 0), // S_POSS_XDIE4
        new(SPR_POSS, 16, 5, NULL, S_POSS_XDIE6, 0, 0), // S_POSS_XDIE5
        new(SPR_POSS, 17, 5, NULL, S_POSS_XDIE7, 0, 0), // S_POSS_XDIE6
        new(SPR_POSS, 18, 5, NULL, S_POSS_XDIE8, 0, 0), // S_POSS_XDIE7
        new(SPR_POSS, 19, 5, NULL, S_POSS_XDIE9, 0, 0), // S_POSS_XDIE8
        new(SPR_POSS, 20, -1, NULL, S_NULL, 0, 0), // S_POSS_XDIE9
        new(SPR_POSS, 10, 5, NULL, S_POSS_RAISE2, 0, 0), // S_POSS_RAISE1
        new(SPR_POSS, 9, 5, NULL, S_POSS_RAISE3, 0, 0), // S_POSS_RAISE2
        new(SPR_POSS, 8, 5, NULL, S_POSS_RAISE4, 0, 0), // S_POSS_RAISE3
        new(SPR_POSS, 7, 5, NULL, S_POSS_RUN1, 0, 0), // S_POSS_RAISE4
        new(SPR_SPOS, 0, 10, A_Look, S_SPOS_STND2, 0, 0), // S_SPOS_STND
        new(SPR_SPOS, 1, 10, A_Look, S_SPOS_STND, 0, 0), // S_SPOS_STND2
        new(SPR_SPOS, 0, 3, A_Chase, S_SPOS_RUN2, 0, 0), // S_SPOS_RUN1
        new(SPR_SPOS, 0, 3, A_Chase, S_SPOS_RUN3, 0, 0), // S_SPOS_RUN2
        new(SPR_SPOS, 1, 3, A_Chase, S_SPOS_RUN4, 0, 0), // S_SPOS_RUN3
        new(SPR_SPOS, 1, 3, A_Chase, S_SPOS_RUN5, 0, 0), // S_SPOS_RUN4
        new(SPR_SPOS, 2, 3, A_Chase, S_SPOS_RUN6, 0, 0), // S_SPOS_RUN5
        new(SPR_SPOS, 2, 3, A_Chase, S_SPOS_RUN7, 0, 0), // S_SPOS_RUN6
        new(SPR_SPOS, 3, 3, A_Chase, S_SPOS_RUN8, 0, 0), // S_SPOS_RUN7
        new(SPR_SPOS, 3, 3, A_Chase, S_SPOS_RUN1, 0, 0), // S_SPOS_RUN8
        new(SPR_SPOS, 4, 10, A_FaceTarget, S_SPOS_ATK2, 0, 0), // S_SPOS_ATK1
        new(SPR_SPOS, 32773, 10, A_SPosAttack, S_SPOS_ATK3, 0, 0), // S_SPOS_ATK2
        new(SPR_SPOS, 4, 10, NULL, S_SPOS_RUN1, 0, 0), // S_SPOS_ATK3
        new(SPR_SPOS, 6, 3, NULL, S_SPOS_PAIN2, 0, 0), // S_SPOS_PAIN
        new(SPR_SPOS, 6, 3, A_Pain, S_SPOS_RUN1, 0, 0), // S_SPOS_PAIN2
        new(SPR_SPOS, 7, 5, NULL, S_SPOS_DIE2, 0, 0), // S_SPOS_DIE1
        new(SPR_SPOS, 8, 5, A_Scream, S_SPOS_DIE3, 0, 0), // S_SPOS_DIE2
        new(SPR_SPOS, 9, 5, A_Fall, S_SPOS_DIE4, 0, 0), // S_SPOS_DIE3
        new(SPR_SPOS, 10, 5, NULL, S_SPOS_DIE5, 0, 0), // S_SPOS_DIE4
        new(SPR_SPOS, 11, -1, NULL, S_NULL, 0, 0), // S_SPOS_DIE5
        new(SPR_SPOS, 12, 5, NULL, S_SPOS_XDIE2, 0, 0), // S_SPOS_XDIE1
        new(SPR_SPOS, 13, 5, A_XScream, S_SPOS_XDIE3, 0, 0), // S_SPOS_XDIE2
        new(SPR_SPOS, 14, 5, A_Fall, S_SPOS_XDIE4, 0, 0), // S_SPOS_XDIE3
        new(SPR_SPOS, 15, 5, NULL, S_SPOS_XDIE5, 0, 0), // S_SPOS_XDIE4
        new(SPR_SPOS, 16, 5, NULL, S_SPOS_XDIE6, 0, 0), // S_SPOS_XDIE5
        new(SPR_SPOS, 17, 5, NULL, S_SPOS_XDIE7, 0, 0), // S_SPOS_XDIE6
        new(SPR_SPOS, 18, 5, NULL, S_SPOS_XDIE8, 0, 0), // S_SPOS_XDIE7
        new(SPR_SPOS, 19, 5, NULL, S_SPOS_XDIE9, 0, 0), // S_SPOS_XDIE8
        new(SPR_SPOS, 20, -1, NULL, S_NULL, 0, 0), // S_SPOS_XDIE9
        new(SPR_SPOS, 11, 5, NULL, S_SPOS_RAISE2, 0, 0), // S_SPOS_RAISE1
        new(SPR_SPOS, 10, 5, NULL, S_SPOS_RAISE3, 0, 0), // S_SPOS_RAISE2
        new(SPR_SPOS, 9, 5, NULL, S_SPOS_RAISE4, 0, 0), // S_SPOS_RAISE3
        new(SPR_SPOS, 8, 5, NULL, S_SPOS_RAISE5, 0, 0), // S_SPOS_RAISE4
        new(SPR_SPOS, 7, 5, NULL, S_SPOS_RUN1, 0, 0), // S_SPOS_RAISE5
        new(SPR_VILE, 0, 10, A_Look, S_VILE_STND2, 0, 0), // S_VILE_STND
        new(SPR_VILE, 1, 10, A_Look, S_VILE_STND, 0, 0), // S_VILE_STND2
        new(SPR_VILE, 0, 2, A_VileChase, S_VILE_RUN2, 0, 0), // S_VILE_RUN1
        new(SPR_VILE, 0, 2, A_VileChase, S_VILE_RUN3, 0, 0), // S_VILE_RUN2
        new(SPR_VILE, 1, 2, A_VileChase, S_VILE_RUN4, 0, 0), // S_VILE_RUN3
        new(SPR_VILE, 1, 2, A_VileChase, S_VILE_RUN5, 0, 0), // S_VILE_RUN4
        new(SPR_VILE, 2, 2, A_VileChase, S_VILE_RUN6, 0, 0), // S_VILE_RUN5
        new(SPR_VILE, 2, 2, A_VileChase, S_VILE_RUN7, 0, 0), // S_VILE_RUN6
        new(SPR_VILE, 3, 2, A_VileChase, S_VILE_RUN8, 0, 0), // S_VILE_RUN7
        new(SPR_VILE, 3, 2, A_VileChase, S_VILE_RUN9, 0, 0), // S_VILE_RUN8
        new(SPR_VILE, 4, 2, A_VileChase, S_VILE_RUN10, 0, 0), // S_VILE_RUN9
        new(SPR_VILE, 4, 2, A_VileChase, S_VILE_RUN11, 0, 0), // S_VILE_RUN10
        new(SPR_VILE, 5, 2, A_VileChase, S_VILE_RUN12, 0, 0), // S_VILE_RUN11
        new(SPR_VILE, 5, 2, A_VileChase, S_VILE_RUN1, 0, 0), // S_VILE_RUN12
        new(SPR_VILE, 32774, 0, A_VileStart, S_VILE_ATK2, 0, 0), // S_VILE_ATK1
        new(SPR_VILE, 32774, 10, A_FaceTarget, S_VILE_ATK3, 0, 0), // S_VILE_ATK2
        new(SPR_VILE, 32775, 8, A_VileTarget, S_VILE_ATK4, 0, 0), // S_VILE_ATK3
        new(SPR_VILE, 32776, 8, A_FaceTarget, S_VILE_ATK5, 0, 0), // S_VILE_ATK4
        new(SPR_VILE, 32777, 8, A_FaceTarget, S_VILE_ATK6, 0, 0), // S_VILE_ATK5
        new(SPR_VILE, 32778, 8, A_FaceTarget, S_VILE_ATK7, 0, 0), // S_VILE_ATK6
        new(SPR_VILE, 32779, 8, A_FaceTarget, S_VILE_ATK8, 0, 0), // S_VILE_ATK7
        new(SPR_VILE, 32780, 8, A_FaceTarget, S_VILE_ATK9, 0, 0), // S_VILE_ATK8
        new(SPR_VILE, 32781, 8, A_FaceTarget, S_VILE_ATK10, 0, 0), // S_VILE_ATK9
        new(SPR_VILE, 32782, 8, A_VileAttack, S_VILE_ATK11, 0, 0), // S_VILE_ATK10
        new(SPR_VILE, 32783, 20, NULL, S_VILE_RUN1, 0, 0), // S_VILE_ATK11
        new(SPR_VILE, 32794, 10, NULL, S_VILE_HEAL2, 0, 0), // S_VILE_HEAL1
        new(SPR_VILE, 32795, 10, NULL, S_VILE_HEAL3, 0, 0), // S_VILE_HEAL2
        new(SPR_VILE, 32796, 10, NULL, S_VILE_RUN1, 0, 0), // S_VILE_HEAL3
        new(SPR_VILE, 16, 5, NULL, S_VILE_PAIN2, 0, 0), // S_VILE_PAIN
        new(SPR_VILE, 16, 5, A_Pain, S_VILE_RUN1, 0, 0), // S_VILE_PAIN2
        new(SPR_VILE, 16, 7, NULL, S_VILE_DIE2, 0, 0), // S_VILE_DIE1
        new(SPR_VILE, 17, 7, A_Scream, S_VILE_DIE3, 0, 0), // S_VILE_DIE2
        new(SPR_VILE, 18, 7, A_Fall, S_VILE_DIE4, 0, 0), // S_VILE_DIE3
        new(SPR_VILE, 19, 7, NULL, S_VILE_DIE5, 0, 0), // S_VILE_DIE4
        new(SPR_VILE, 20, 7, NULL, S_VILE_DIE6, 0, 0), // S_VILE_DIE5
        new(SPR_VILE, 21, 7, NULL, S_VILE_DIE7, 0, 0), // S_VILE_DIE6
        new(SPR_VILE, 22, 7, NULL, S_VILE_DIE8, 0, 0), // S_VILE_DIE7
        new(SPR_VILE, 23, 5, NULL, S_VILE_DIE9, 0, 0), // S_VILE_DIE8
        new(SPR_VILE, 24, 5, NULL, S_VILE_DIE10, 0, 0), // S_VILE_DIE9
        new(SPR_VILE, 25, -1, NULL, S_NULL, 0, 0), // S_VILE_DIE10
        new(SPR_FIRE, 32768, 2, A_StartFire, S_FIRE2, 0, 0), // S_FIRE1
        new(SPR_FIRE, 32769, 2, A_Fire, S_FIRE3, 0, 0), // S_FIRE2
        new(SPR_FIRE, 32768, 2, A_Fire, S_FIRE4, 0, 0), // S_FIRE3
        new(SPR_FIRE, 32769, 2, A_Fire, S_FIRE5, 0, 0), // S_FIRE4
        new(SPR_FIRE, 32770, 2, A_FireCrackle, S_FIRE6, 0, 0), // S_FIRE5
        new(SPR_FIRE, 32769, 2, A_Fire, S_FIRE7, 0, 0), // S_FIRE6
        new(SPR_FIRE, 32770, 2, A_Fire, S_FIRE8, 0, 0), // S_FIRE7
        new(SPR_FIRE, 32769, 2, A_Fire, S_FIRE9, 0, 0), // S_FIRE8
        new(SPR_FIRE, 32770, 2, A_Fire, S_FIRE10, 0, 0), // S_FIRE9
        new(SPR_FIRE, 32771, 2, A_Fire, S_FIRE11, 0, 0), // S_FIRE10
        new(SPR_FIRE, 32770, 2, A_Fire, S_FIRE12, 0, 0), // S_FIRE11
        new(SPR_FIRE, 32771, 2, A_Fire, S_FIRE13, 0, 0), // S_FIRE12
        new(SPR_FIRE, 32770, 2, A_Fire, S_FIRE14, 0, 0), // S_FIRE13
        new(SPR_FIRE, 32771, 2, A_Fire, S_FIRE15, 0, 0), // S_FIRE14
        new(SPR_FIRE, 32772, 2, A_Fire, S_FIRE16, 0, 0), // S_FIRE15
        new(SPR_FIRE, 32771, 2, A_Fire, S_FIRE17, 0, 0), // S_FIRE16
        new(SPR_FIRE, 32772, 2, A_Fire, S_FIRE18, 0, 0), // S_FIRE17
        new(SPR_FIRE, 32771, 2, A_Fire, S_FIRE19, 0, 0), // S_FIRE18
        new(SPR_FIRE, 32772, 2, A_FireCrackle, S_FIRE20, 0, 0), // S_FIRE19
        new(SPR_FIRE, 32773, 2, A_Fire, S_FIRE21, 0, 0), // S_FIRE20
        new(SPR_FIRE, 32772, 2, A_Fire, S_FIRE22, 0, 0), // S_FIRE21
        new(SPR_FIRE, 32773, 2, A_Fire, S_FIRE23, 0, 0), // S_FIRE22
        new(SPR_FIRE, 32772, 2, A_Fire, S_FIRE24, 0, 0), // S_FIRE23
        new(SPR_FIRE, 32773, 2, A_Fire, S_FIRE25, 0, 0), // S_FIRE24
        new(SPR_FIRE, 32774, 2, A_Fire, S_FIRE26, 0, 0), // S_FIRE25
        new(SPR_FIRE, 32775, 2, A_Fire, S_FIRE27, 0, 0), // S_FIRE26
        new(SPR_FIRE, 32774, 2, A_Fire, S_FIRE28, 0, 0), // S_FIRE27
        new(SPR_FIRE, 32775, 2, A_Fire, S_FIRE29, 0, 0), // S_FIRE28
        new(SPR_FIRE, 32774, 2, A_Fire, S_FIRE30, 0, 0), // S_FIRE29
        new(SPR_FIRE, 32775, 2, A_Fire, S_NULL, 0, 0), // S_FIRE30
        new(SPR_PUFF, 1, 4, NULL, S_SMOKE2, 0, 0), // S_SMOKE1
        new(SPR_PUFF, 2, 4, NULL, S_SMOKE3, 0, 0), // S_SMOKE2
        new(SPR_PUFF, 1, 4, NULL, S_SMOKE4, 0, 0), // S_SMOKE3
        new(SPR_PUFF, 2, 4, NULL, S_SMOKE5, 0, 0), // S_SMOKE4
        new(SPR_PUFF, 3, 4, NULL, S_NULL, 0, 0), // S_SMOKE5
        new(SPR_FATB, 32768, 2, A_Tracer, S_TRACER2, 0, 0), // S_TRACER
        new(SPR_FATB, 32769, 2, A_Tracer, S_TRACER, 0, 0), // S_TRACER2
        new(SPR_FBXP, 32768, 8, NULL, S_TRACEEXP2, 0, 0), // S_TRACEEXP1
        new(SPR_FBXP, 32769, 6, NULL, S_TRACEEXP3, 0, 0), // S_TRACEEXP2
        new(SPR_FBXP, 32770, 4, NULL, S_NULL, 0, 0), // S_TRACEEXP3
        new(SPR_SKEL, 0, 10, A_Look, S_SKEL_STND2, 0, 0), // S_SKEL_STND
        new(SPR_SKEL, 1, 10, A_Look, S_SKEL_STND, 0, 0), // S_SKEL_STND2
        new(SPR_SKEL, 0, 2, A_Chase, S_SKEL_RUN2, 0, 0), // S_SKEL_RUN1
        new(SPR_SKEL, 0, 2, A_Chase, S_SKEL_RUN3, 0, 0), // S_SKEL_RUN2
        new(SPR_SKEL, 1, 2, A_Chase, S_SKEL_RUN4, 0, 0), // S_SKEL_RUN3
        new(SPR_SKEL, 1, 2, A_Chase, S_SKEL_RUN5, 0, 0), // S_SKEL_RUN4
        new(SPR_SKEL, 2, 2, A_Chase, S_SKEL_RUN6, 0, 0), // S_SKEL_RUN5
        new(SPR_SKEL, 2, 2, A_Chase, S_SKEL_RUN7, 0, 0), // S_SKEL_RUN6
        new(SPR_SKEL, 3, 2, A_Chase, S_SKEL_RUN8, 0, 0), // S_SKEL_RUN7
        new(SPR_SKEL, 3, 2, A_Chase, S_SKEL_RUN9, 0, 0), // S_SKEL_RUN8
        new(SPR_SKEL, 4, 2, A_Chase, S_SKEL_RUN10, 0, 0), // S_SKEL_RUN9
        new(SPR_SKEL, 4, 2, A_Chase, S_SKEL_RUN11, 0, 0), // S_SKEL_RUN10
        new(SPR_SKEL, 5, 2, A_Chase, S_SKEL_RUN12, 0, 0), // S_SKEL_RUN11
        new(SPR_SKEL, 5, 2, A_Chase, S_SKEL_RUN1, 0, 0), // S_SKEL_RUN12
        new(SPR_SKEL, 6, 0, A_FaceTarget, S_SKEL_FIST2, 0, 0), // S_SKEL_FIST1
        new(SPR_SKEL, 6, 6, A_SkelWhoosh, S_SKEL_FIST3, 0, 0), // S_SKEL_FIST2
        new(SPR_SKEL, 7, 6, A_FaceTarget, S_SKEL_FIST4, 0, 0), // S_SKEL_FIST3
        new(SPR_SKEL, 8, 6, A_SkelFist, S_SKEL_RUN1, 0, 0), // S_SKEL_FIST4
        new(SPR_SKEL, 32777, 0, A_FaceTarget, S_SKEL_MISS2, 0, 0), // S_SKEL_MISS1
        new(SPR_SKEL, 32777, 10, A_FaceTarget, S_SKEL_MISS3, 0, 0), // S_SKEL_MISS2
        new(SPR_SKEL, 10, 10, A_SkelMissile, S_SKEL_MISS4, 0, 0), // S_SKEL_MISS3
        new(SPR_SKEL, 10, 10, A_FaceTarget, S_SKEL_RUN1, 0, 0), // S_SKEL_MISS4
        new(SPR_SKEL, 11, 5, NULL, S_SKEL_PAIN2, 0, 0), // S_SKEL_PAIN
        new(SPR_SKEL, 11, 5, A_Pain, S_SKEL_RUN1, 0, 0), // S_SKEL_PAIN2
        new(SPR_SKEL, 11, 7, NULL, S_SKEL_DIE2, 0, 0), // S_SKEL_DIE1
        new(SPR_SKEL, 12, 7, NULL, S_SKEL_DIE3, 0, 0), // S_SKEL_DIE2
        new(SPR_SKEL, 13, 7, A_Scream, S_SKEL_DIE4, 0, 0), // S_SKEL_DIE3
        new(SPR_SKEL, 14, 7, A_Fall, S_SKEL_DIE5, 0, 0), // S_SKEL_DIE4
        new(SPR_SKEL, 15, 7, NULL, S_SKEL_DIE6, 0, 0), // S_SKEL_DIE5
        new(SPR_SKEL, 16, -1, NULL, S_NULL, 0, 0), // S_SKEL_DIE6
        new(SPR_SKEL, 16, 5, NULL, S_SKEL_RAISE2, 0, 0), // S_SKEL_RAISE1
        new(SPR_SKEL, 15, 5, NULL, S_SKEL_RAISE3, 0, 0), // S_SKEL_RAISE2
        new(SPR_SKEL, 14, 5, NULL, S_SKEL_RAISE4, 0, 0), // S_SKEL_RAISE3
        new(SPR_SKEL, 13, 5, NULL, S_SKEL_RAISE5, 0, 0), // S_SKEL_RAISE4
        new(SPR_SKEL, 12, 5, NULL, S_SKEL_RAISE6, 0, 0), // S_SKEL_RAISE5
        new(SPR_SKEL, 11, 5, NULL, S_SKEL_RUN1, 0, 0), // S_SKEL_RAISE6
        new(SPR_MANF, 32768, 4, NULL, S_FATSHOT2, 0, 0), // S_FATSHOT1
        new(SPR_MANF, 32769, 4, NULL, S_FATSHOT1, 0, 0), // S_FATSHOT2
        new(SPR_MISL, 32769, 8, NULL, S_FATSHOTX2, 0, 0), // S_FATSHOTX1
        new(SPR_MISL, 32770, 6, NULL, S_FATSHOTX3, 0, 0), // S_FATSHOTX2
        new(SPR_MISL, 32771, 4, NULL, S_NULL, 0, 0), // S_FATSHOTX3
        new(SPR_FATT, 0, 15, A_Look, S_FATT_STND2, 0, 0), // S_FATT_STND
        new(SPR_FATT, 1, 15, A_Look, S_FATT_STND, 0, 0), // S_FATT_STND2
        new(SPR_FATT, 0, 4, A_Chase, S_FATT_RUN2, 0, 0), // S_FATT_RUN1
        new(SPR_FATT, 0, 4, A_Chase, S_FATT_RUN3, 0, 0), // S_FATT_RUN2
        new(SPR_FATT, 1, 4, A_Chase, S_FATT_RUN4, 0, 0), // S_FATT_RUN3
        new(SPR_FATT, 1, 4, A_Chase, S_FATT_RUN5, 0, 0), // S_FATT_RUN4
        new(SPR_FATT, 2, 4, A_Chase, S_FATT_RUN6, 0, 0), // S_FATT_RUN5
        new(SPR_FATT, 2, 4, A_Chase, S_FATT_RUN7, 0, 0), // S_FATT_RUN6
        new(SPR_FATT, 3, 4, A_Chase, S_FATT_RUN8, 0, 0), // S_FATT_RUN7
        new(SPR_FATT, 3, 4, A_Chase, S_FATT_RUN9, 0, 0), // S_FATT_RUN8
        new(SPR_FATT, 4, 4, A_Chase, S_FATT_RUN10, 0, 0), // S_FATT_RUN9
        new(SPR_FATT, 4, 4, A_Chase, S_FATT_RUN11, 0, 0), // S_FATT_RUN10
        new(SPR_FATT, 5, 4, A_Chase, S_FATT_RUN12, 0, 0), // S_FATT_RUN11
        new(SPR_FATT, 5, 4, A_Chase, S_FATT_RUN1, 0, 0), // S_FATT_RUN12
        new(SPR_FATT, 6, 20, A_FatRaise, S_FATT_ATK2, 0, 0), // S_FATT_ATK1
        new(SPR_FATT, 32775, 10, A_FatAttack1, S_FATT_ATK3, 0, 0), // S_FATT_ATK2
        new(SPR_FATT, 8, 5, A_FaceTarget, S_FATT_ATK4, 0, 0), // S_FATT_ATK3
        new(SPR_FATT, 6, 5, A_FaceTarget, S_FATT_ATK5, 0, 0), // S_FATT_ATK4
        new(SPR_FATT, 32775, 10, A_FatAttack2, S_FATT_ATK6, 0, 0), // S_FATT_ATK5
        new(SPR_FATT, 8, 5, A_FaceTarget, S_FATT_ATK7, 0, 0), // S_FATT_ATK6
        new(SPR_FATT, 6, 5, A_FaceTarget, S_FATT_ATK8, 0, 0), // S_FATT_ATK7
        new(SPR_FATT, 32775, 10, A_FatAttack3, S_FATT_ATK9, 0, 0), // S_FATT_ATK8
        new(SPR_FATT, 8, 5, A_FaceTarget, S_FATT_ATK10, 0, 0), // S_FATT_ATK9
        new(SPR_FATT, 6, 5, A_FaceTarget, S_FATT_RUN1, 0, 0), // S_FATT_ATK10
        new(SPR_FATT, 9, 3, NULL, S_FATT_PAIN2, 0, 0), // S_FATT_PAIN
        new(SPR_FATT, 9, 3, A_Pain, S_FATT_RUN1, 0, 0), // S_FATT_PAIN2
        new(SPR_FATT, 10, 6, NULL, S_FATT_DIE2, 0, 0), // S_FATT_DIE1
        new(SPR_FATT, 11, 6, A_Scream, S_FATT_DIE3, 0, 0), // S_FATT_DIE2
        new(SPR_FATT, 12, 6, A_Fall, S_FATT_DIE4, 0, 0), // S_FATT_DIE3
        new(SPR_FATT, 13, 6, NULL, S_FATT_DIE5, 0, 0), // S_FATT_DIE4
        new(SPR_FATT, 14, 6, NULL, S_FATT_DIE6, 0, 0), // S_FATT_DIE5
        new(SPR_FATT, 15, 6, NULL, S_FATT_DIE7, 0, 0), // S_FATT_DIE6
        new(SPR_FATT, 16, 6, NULL, S_FATT_DIE8, 0, 0), // S_FATT_DIE7
        new(SPR_FATT, 17, 6, NULL, S_FATT_DIE9, 0, 0), // S_FATT_DIE8
        new(SPR_FATT, 18, 6, NULL, S_FATT_DIE10, 0, 0), // S_FATT_DIE9
        new(SPR_FATT, 19, -1, A_BossDeath, S_NULL, 0, 0), // S_FATT_DIE10
        new(SPR_FATT, 17, 5, NULL, S_FATT_RAISE2, 0, 0), // S_FATT_RAISE1
        new(SPR_FATT, 16, 5, NULL, S_FATT_RAISE3, 0, 0), // S_FATT_RAISE2
        new(SPR_FATT, 15, 5, NULL, S_FATT_RAISE4, 0, 0), // S_FATT_RAISE3
        new(SPR_FATT, 14, 5, NULL, S_FATT_RAISE5, 0, 0), // S_FATT_RAISE4
        new(SPR_FATT, 13, 5, NULL, S_FATT_RAISE6, 0, 0), // S_FATT_RAISE5
        new(SPR_FATT, 12, 5, NULL, S_FATT_RAISE7, 0, 0), // S_FATT_RAISE6
        new(SPR_FATT, 11, 5, NULL, S_FATT_RAISE8, 0, 0), // S_FATT_RAISE7
        new(SPR_FATT, 10, 5, NULL, S_FATT_RUN1, 0, 0), // S_FATT_RAISE8
        new(SPR_CPOS, 0, 10, A_Look, S_CPOS_STND2, 0, 0), // S_CPOS_STND
        new(SPR_CPOS, 1, 10, A_Look, S_CPOS_STND, 0, 0), // S_CPOS_STND2
        new(SPR_CPOS, 0, 3, A_Chase, S_CPOS_RUN2, 0, 0), // S_CPOS_RUN1
        new(SPR_CPOS, 0, 3, A_Chase, S_CPOS_RUN3, 0, 0), // S_CPOS_RUN2
        new(SPR_CPOS, 1, 3, A_Chase, S_CPOS_RUN4, 0, 0), // S_CPOS_RUN3
        new(SPR_CPOS, 1, 3, A_Chase, S_CPOS_RUN5, 0, 0), // S_CPOS_RUN4
        new(SPR_CPOS, 2, 3, A_Chase, S_CPOS_RUN6, 0, 0), // S_CPOS_RUN5
        new(SPR_CPOS, 2, 3, A_Chase, S_CPOS_RUN7, 0, 0), // S_CPOS_RUN6
        new(SPR_CPOS, 3, 3, A_Chase, S_CPOS_RUN8, 0, 0), // S_CPOS_RUN7
        new(SPR_CPOS, 3, 3, A_Chase, S_CPOS_RUN1, 0, 0), // S_CPOS_RUN8
        new(SPR_CPOS, 4, 10, A_FaceTarget, S_CPOS_ATK2, 0, 0), // S_CPOS_ATK1
        new(SPR_CPOS, 32773, 4, A_CPosAttack, S_CPOS_ATK3, 0, 0), // S_CPOS_ATK2
        new(SPR_CPOS, 32772, 4, A_CPosAttack, S_CPOS_ATK4, 0, 0), // S_CPOS_ATK3
        new(SPR_CPOS, 5, 1, A_CPosRefire, S_CPOS_ATK2, 0, 0), // S_CPOS_ATK4
        new(SPR_CPOS, 6, 3, NULL, S_CPOS_PAIN2, 0, 0), // S_CPOS_PAIN
        new(SPR_CPOS, 6, 3, A_Pain, S_CPOS_RUN1, 0, 0), // S_CPOS_PAIN2
        new(SPR_CPOS, 7, 5, NULL, S_CPOS_DIE2, 0, 0), // S_CPOS_DIE1
        new(SPR_CPOS, 8, 5, A_Scream, S_CPOS_DIE3, 0, 0), // S_CPOS_DIE2
        new(SPR_CPOS, 9, 5, A_Fall, S_CPOS_DIE4, 0, 0), // S_CPOS_DIE3
        new(SPR_CPOS, 10, 5, NULL, S_CPOS_DIE5, 0, 0), // S_CPOS_DIE4
        new(SPR_CPOS, 11, 5, NULL, S_CPOS_DIE6, 0, 0), // S_CPOS_DIE5
        new(SPR_CPOS, 12, 5, NULL, S_CPOS_DIE7, 0, 0), // S_CPOS_DIE6
        new(SPR_CPOS, 13, -1, NULL, S_NULL, 0, 0), // S_CPOS_DIE7
        new(SPR_CPOS, 14, 5, NULL, S_CPOS_XDIE2, 0, 0), // S_CPOS_XDIE1
        new(SPR_CPOS, 15, 5, A_XScream, S_CPOS_XDIE3, 0, 0), // S_CPOS_XDIE2
        new(SPR_CPOS, 16, 5, A_Fall, S_CPOS_XDIE4, 0, 0), // S_CPOS_XDIE3
        new(SPR_CPOS, 17, 5, NULL, S_CPOS_XDIE5, 0, 0), // S_CPOS_XDIE4
        new(SPR_CPOS, 18, 5, NULL, S_CPOS_XDIE6, 0, 0), // S_CPOS_XDIE5
        new(SPR_CPOS, 19, -1, NULL, S_NULL, 0, 0), // S_CPOS_XDIE6
        new(SPR_CPOS, 13, 5, NULL, S_CPOS_RAISE2, 0, 0), // S_CPOS_RAISE1
        new(SPR_CPOS, 12, 5, NULL, S_CPOS_RAISE3, 0, 0), // S_CPOS_RAISE2
        new(SPR_CPOS, 11, 5, NULL, S_CPOS_RAISE4, 0, 0), // S_CPOS_RAISE3
        new(SPR_CPOS, 10, 5, NULL, S_CPOS_RAISE5, 0, 0), // S_CPOS_RAISE4
        new(SPR_CPOS, 9, 5, NULL, S_CPOS_RAISE6, 0, 0), // S_CPOS_RAISE5
        new(SPR_CPOS, 8, 5, NULL, S_CPOS_RAISE7, 0, 0), // S_CPOS_RAISE6
        new(SPR_CPOS, 7, 5, NULL, S_CPOS_RUN1, 0, 0), // S_CPOS_RAISE7
        new(SPR_TROO, 0, 10, A_Look, S_TROO_STND2, 0, 0), // S_TROO_STND
        new(SPR_TROO, 1, 10, A_Look, S_TROO_STND, 0, 0), // S_TROO_STND2
        new(SPR_TROO, 0, 3, A_Chase, S_TROO_RUN2, 0, 0), // S_TROO_RUN1
        new(SPR_TROO, 0, 3, A_Chase, S_TROO_RUN3, 0, 0), // S_TROO_RUN2
        new(SPR_TROO, 1, 3, A_Chase, S_TROO_RUN4, 0, 0), // S_TROO_RUN3
        new(SPR_TROO, 1, 3, A_Chase, S_TROO_RUN5, 0, 0), // S_TROO_RUN4
        new(SPR_TROO, 2, 3, A_Chase, S_TROO_RUN6, 0, 0), // S_TROO_RUN5
        new(SPR_TROO, 2, 3, A_Chase, S_TROO_RUN7, 0, 0), // S_TROO_RUN6
        new(SPR_TROO, 3, 3, A_Chase, S_TROO_RUN8, 0, 0), // S_TROO_RUN7
        new(SPR_TROO, 3, 3, A_Chase, S_TROO_RUN1, 0, 0), // S_TROO_RUN8
        new(SPR_TROO, 4, 8, A_FaceTarget, S_TROO_ATK2, 0, 0), // S_TROO_ATK1
        new(SPR_TROO, 5, 8, A_FaceTarget, S_TROO_ATK3, 0, 0), // S_TROO_ATK2
        new(SPR_TROO, 6, 6, A_TroopAttack, S_TROO_RUN1, 0, 0), // S_TROO_ATK3
        new(SPR_TROO, 7, 2, NULL, S_TROO_PAIN2, 0, 0), // S_TROO_PAIN
        new(SPR_TROO, 7, 2, A_Pain, S_TROO_RUN1, 0, 0), // S_TROO_PAIN2
        new(SPR_TROO, 8, 8, NULL, S_TROO_DIE2, 0, 0), // S_TROO_DIE1
        new(SPR_TROO, 9, 8, A_Scream, S_TROO_DIE3, 0, 0), // S_TROO_DIE2
        new(SPR_TROO, 10, 6, NULL, S_TROO_DIE4, 0, 0), // S_TROO_DIE3
        new(SPR_TROO, 11, 6, A_Fall, S_TROO_DIE5, 0, 0), // S_TROO_DIE4
        new(SPR_TROO, 12, -1, NULL, S_NULL, 0, 0), // S_TROO_DIE5
        new(SPR_TROO, 13, 5, NULL, S_TROO_XDIE2, 0, 0), // S_TROO_XDIE1
        new(SPR_TROO, 14, 5, A_XScream, S_TROO_XDIE3, 0, 0), // S_TROO_XDIE2
        new(SPR_TROO, 15, 5, NULL, S_TROO_XDIE4, 0, 0), // S_TROO_XDIE3
        new(SPR_TROO, 16, 5, A_Fall, S_TROO_XDIE5, 0, 0), // S_TROO_XDIE4
        new(SPR_TROO, 17, 5, NULL, S_TROO_XDIE6, 0, 0), // S_TROO_XDIE5
        new(SPR_TROO, 18, 5, NULL, S_TROO_XDIE7, 0, 0), // S_TROO_XDIE6
        new(SPR_TROO, 19, 5, NULL, S_TROO_XDIE8, 0, 0), // S_TROO_XDIE7
        new(SPR_TROO, 20, -1, NULL, S_NULL, 0, 0), // S_TROO_XDIE8
        new(SPR_TROO, 12, 8, NULL, S_TROO_RAISE2, 0, 0), // S_TROO_RAISE1
        new(SPR_TROO, 11, 8, NULL, S_TROO_RAISE3, 0, 0), // S_TROO_RAISE2
        new(SPR_TROO, 10, 6, NULL, S_TROO_RAISE4, 0, 0), // S_TROO_RAISE3
        new(SPR_TROO, 9, 6, NULL, S_TROO_RAISE5, 0, 0), // S_TROO_RAISE4
        new(SPR_TROO, 8, 6, NULL, S_TROO_RUN1, 0, 0), // S_TROO_RAISE5
        new(SPR_SARG, 0, 10, A_Look, S_SARG_STND2, 0, 0), // S_SARG_STND
        new(SPR_SARG, 1, 10, A_Look, S_SARG_STND, 0, 0), // S_SARG_STND2
        new(SPR_SARG, 0, 2, A_Chase, S_SARG_RUN2, 0, 0), // S_SARG_RUN1
        new(SPR_SARG, 0, 2, A_Chase, S_SARG_RUN3, 0, 0), // S_SARG_RUN2
        new(SPR_SARG, 1, 2, A_Chase, S_SARG_RUN4, 0, 0), // S_SARG_RUN3
        new(SPR_SARG, 1, 2, A_Chase, S_SARG_RUN5, 0, 0), // S_SARG_RUN4
        new(SPR_SARG, 2, 2, A_Chase, S_SARG_RUN6, 0, 0), // S_SARG_RUN5
        new(SPR_SARG, 2, 2, A_Chase, S_SARG_RUN7, 0, 0), // S_SARG_RUN6
        new(SPR_SARG, 3, 2, A_Chase, S_SARG_RUN8, 0, 0), // S_SARG_RUN7
        new(SPR_SARG, 3, 2, A_Chase, S_SARG_RUN1, 0, 0), // S_SARG_RUN8
        new(SPR_SARG, 4, 8, A_FaceTarget, S_SARG_ATK2, 0, 0), // S_SARG_ATK1
        new(SPR_SARG, 5, 8, A_FaceTarget, S_SARG_ATK3, 0, 0), // S_SARG_ATK2
        new(SPR_SARG, 6, 8, A_SargAttack, S_SARG_RUN1, 0, 0), // S_SARG_ATK3
        new(SPR_SARG, 7, 2, NULL, S_SARG_PAIN2, 0, 0), // S_SARG_PAIN
        new(SPR_SARG, 7, 2, A_Pain, S_SARG_RUN1, 0, 0), // S_SARG_PAIN2
        new(SPR_SARG, 8, 8, NULL, S_SARG_DIE2, 0, 0), // S_SARG_DIE1
        new(SPR_SARG, 9, 8, A_Scream, S_SARG_DIE3, 0, 0), // S_SARG_DIE2
        new(SPR_SARG, 10, 4, NULL, S_SARG_DIE4, 0, 0), // S_SARG_DIE3
        new(SPR_SARG, 11, 4, A_Fall, S_SARG_DIE5, 0, 0), // S_SARG_DIE4
        new(SPR_SARG, 12, 4, NULL, S_SARG_DIE6, 0, 0), // S_SARG_DIE5
        new(SPR_SARG, 13, -1, NULL, S_NULL, 0, 0), // S_SARG_DIE6
        new(SPR_SARG, 13, 5, NULL, S_SARG_RAISE2, 0, 0), // S_SARG_RAISE1
        new(SPR_SARG, 12, 5, NULL, S_SARG_RAISE3, 0, 0), // S_SARG_RAISE2
        new(SPR_SARG, 11, 5, NULL, S_SARG_RAISE4, 0, 0), // S_SARG_RAISE3
        new(SPR_SARG, 10, 5, NULL, S_SARG_RAISE5, 0, 0), // S_SARG_RAISE4
        new(SPR_SARG, 9, 5, NULL, S_SARG_RAISE6, 0, 0), // S_SARG_RAISE5
        new(SPR_SARG, 8, 5, NULL, S_SARG_RUN1, 0, 0), // S_SARG_RAISE6
        new(SPR_HEAD, 0, 10, A_Look, S_HEAD_STND, 0, 0), // S_HEAD_STND
        new(SPR_HEAD, 0, 3, A_Chase, S_HEAD_RUN1, 0, 0), // S_HEAD_RUN1
        new(SPR_HEAD, 1, 5, A_FaceTarget, S_HEAD_ATK2, 0, 0), // S_HEAD_ATK1
        new(SPR_HEAD, 2, 5, A_FaceTarget, S_HEAD_ATK3, 0, 0), // S_HEAD_ATK2
        new(SPR_HEAD, 32771, 5, A_HeadAttack, S_HEAD_RUN1, 0, 0), // S_HEAD_ATK3
        new(SPR_HEAD, 4, 3, NULL, S_HEAD_PAIN2, 0, 0), // S_HEAD_PAIN
        new(SPR_HEAD, 4, 3, A_Pain, S_HEAD_PAIN3, 0, 0), // S_HEAD_PAIN2
        new(SPR_HEAD, 5, 6, NULL, S_HEAD_RUN1, 0, 0), // S_HEAD_PAIN3
        new(SPR_HEAD, 6, 8, NULL, S_HEAD_DIE2, 0, 0), // S_HEAD_DIE1
        new(SPR_HEAD, 7, 8, A_Scream, S_HEAD_DIE3, 0, 0), // S_HEAD_DIE2
        new(SPR_HEAD, 8, 8, NULL, S_HEAD_DIE4, 0, 0), // S_HEAD_DIE3
        new(SPR_HEAD, 9, 8, NULL, S_HEAD_DIE5, 0, 0), // S_HEAD_DIE4
        new(SPR_HEAD, 10, 8, A_Fall, S_HEAD_DIE6, 0, 0), // S_HEAD_DIE5
        new(SPR_HEAD, 11, -1, NULL, S_NULL, 0, 0), // S_HEAD_DIE6
        new(SPR_HEAD, 11, 8, NULL, S_HEAD_RAISE2, 0, 0), // S_HEAD_RAISE1
        new(SPR_HEAD, 10, 8, NULL, S_HEAD_RAISE3, 0, 0), // S_HEAD_RAISE2
        new(SPR_HEAD, 9, 8, NULL, S_HEAD_RAISE4, 0, 0), // S_HEAD_RAISE3
        new(SPR_HEAD, 8, 8, NULL, S_HEAD_RAISE5, 0, 0), // S_HEAD_RAISE4
        new(SPR_HEAD, 7, 8, NULL, S_HEAD_RAISE6, 0, 0), // S_HEAD_RAISE5
        new(SPR_HEAD, 6, 8, NULL, S_HEAD_RUN1, 0, 0), // S_HEAD_RAISE6
        new(SPR_BAL7, 32768, 4, NULL, S_BRBALL2, 0, 0), // S_BRBALL1
        new(SPR_BAL7, 32769, 4, NULL, S_BRBALL1, 0, 0), // S_BRBALL2
        new(SPR_BAL7, 32770, 6, NULL, S_BRBALLX2, 0, 0), // S_BRBALLX1
        new(SPR_BAL7, 32771, 6, NULL, S_BRBALLX3, 0, 0), // S_BRBALLX2
        new(SPR_BAL7, 32772, 6, NULL, S_NULL, 0, 0), // S_BRBALLX3
        new(SPR_BOSS, 0, 10, A_Look, S_BOSS_STND2, 0, 0), // S_BOSS_STND
        new(SPR_BOSS, 1, 10, A_Look, S_BOSS_STND, 0, 0), // S_BOSS_STND2
        new(SPR_BOSS, 0, 3, A_Chase, S_BOSS_RUN2, 0, 0), // S_BOSS_RUN1
        new(SPR_BOSS, 0, 3, A_Chase, S_BOSS_RUN3, 0, 0), // S_BOSS_RUN2
        new(SPR_BOSS, 1, 3, A_Chase, S_BOSS_RUN4, 0, 0), // S_BOSS_RUN3
        new(SPR_BOSS, 1, 3, A_Chase, S_BOSS_RUN5, 0, 0), // S_BOSS_RUN4
        new(SPR_BOSS, 2, 3, A_Chase, S_BOSS_RUN6, 0, 0), // S_BOSS_RUN5
        new(SPR_BOSS, 2, 3, A_Chase, S_BOSS_RUN7, 0, 0), // S_BOSS_RUN6
        new(SPR_BOSS, 3, 3, A_Chase, S_BOSS_RUN8, 0, 0), // S_BOSS_RUN7
        new(SPR_BOSS, 3, 3, A_Chase, S_BOSS_RUN1, 0, 0), // S_BOSS_RUN8
        new(SPR_BOSS, 4, 8, A_FaceTarget, S_BOSS_ATK2, 0, 0), // S_BOSS_ATK1
        new(SPR_BOSS, 5, 8, A_FaceTarget, S_BOSS_ATK3, 0, 0), // S_BOSS_ATK2
        new(SPR_BOSS, 6, 8, A_BruisAttack, S_BOSS_RUN1, 0, 0), // S_BOSS_ATK3
        new(SPR_BOSS, 7, 2, NULL, S_BOSS_PAIN2, 0, 0), // S_BOSS_PAIN
        new(SPR_BOSS, 7, 2, A_Pain, S_BOSS_RUN1, 0, 0), // S_BOSS_PAIN2
        new(SPR_BOSS, 8, 8, NULL, S_BOSS_DIE2, 0, 0), // S_BOSS_DIE1
        new(SPR_BOSS, 9, 8, A_Scream, S_BOSS_DIE3, 0, 0), // S_BOSS_DIE2
        new(SPR_BOSS, 10, 8, NULL, S_BOSS_DIE4, 0, 0), // S_BOSS_DIE3
        new(SPR_BOSS, 11, 8, A_Fall, S_BOSS_DIE5, 0, 0), // S_BOSS_DIE4
        new(SPR_BOSS, 12, 8, NULL, S_BOSS_DIE6, 0, 0), // S_BOSS_DIE5
        new(SPR_BOSS, 13, 8, NULL, S_BOSS_DIE7, 0, 0), // S_BOSS_DIE6
        new(SPR_BOSS, 14, -1, A_BossDeath, S_NULL, 0, 0), // S_BOSS_DIE7
        new(SPR_BOSS, 14, 8, NULL, S_BOSS_RAISE2, 0, 0), // S_BOSS_RAISE1
        new(SPR_BOSS, 13, 8, NULL, S_BOSS_RAISE3, 0, 0), // S_BOSS_RAISE2
        new(SPR_BOSS, 12, 8, NULL, S_BOSS_RAISE4, 0, 0), // S_BOSS_RAISE3
        new(SPR_BOSS, 11, 8, NULL, S_BOSS_RAISE5, 0, 0), // S_BOSS_RAISE4
        new(SPR_BOSS, 10, 8, NULL, S_BOSS_RAISE6, 0, 0), // S_BOSS_RAISE5
        new(SPR_BOSS, 9, 8, NULL, S_BOSS_RAISE7, 0, 0), // S_BOSS_RAISE6
        new(SPR_BOSS, 8, 8, NULL, S_BOSS_RUN1, 0, 0), // S_BOSS_RAISE7
        new(SPR_BOS2, 0, 10, A_Look, S_BOS2_STND2, 0, 0), // S_BOS2_STND
        new(SPR_BOS2, 1, 10, A_Look, S_BOS2_STND, 0, 0), // S_BOS2_STND2
        new(SPR_BOS2, 0, 3, A_Chase, S_BOS2_RUN2, 0, 0), // S_BOS2_RUN1
        new(SPR_BOS2, 0, 3, A_Chase, S_BOS2_RUN3, 0, 0), // S_BOS2_RUN2
        new(SPR_BOS2, 1, 3, A_Chase, S_BOS2_RUN4, 0, 0), // S_BOS2_RUN3
        new(SPR_BOS2, 1, 3, A_Chase, S_BOS2_RUN5, 0, 0), // S_BOS2_RUN4
        new(SPR_BOS2, 2, 3, A_Chase, S_BOS2_RUN6, 0, 0), // S_BOS2_RUN5
        new(SPR_BOS2, 2, 3, A_Chase, S_BOS2_RUN7, 0, 0), // S_BOS2_RUN6
        new(SPR_BOS2, 3, 3, A_Chase, S_BOS2_RUN8, 0, 0), // S_BOS2_RUN7
        new(SPR_BOS2, 3, 3, A_Chase, S_BOS2_RUN1, 0, 0), // S_BOS2_RUN8
        new(SPR_BOS2, 4, 8, A_FaceTarget, S_BOS2_ATK2, 0, 0), // S_BOS2_ATK1
        new(SPR_BOS2, 5, 8, A_FaceTarget, S_BOS2_ATK3, 0, 0), // S_BOS2_ATK2
        new(SPR_BOS2, 6, 8, A_BruisAttack, S_BOS2_RUN1, 0, 0), // S_BOS2_ATK3
        new(SPR_BOS2, 7, 2, NULL, S_BOS2_PAIN2, 0, 0), // S_BOS2_PAIN
        new(SPR_BOS2, 7, 2, A_Pain, S_BOS2_RUN1, 0, 0), // S_BOS2_PAIN2
        new(SPR_BOS2, 8, 8, NULL, S_BOS2_DIE2, 0, 0), // S_BOS2_DIE1
        new(SPR_BOS2, 9, 8, A_Scream, S_BOS2_DIE3, 0, 0), // S_BOS2_DIE2
        new(SPR_BOS2, 10, 8, NULL, S_BOS2_DIE4, 0, 0), // S_BOS2_DIE3
        new(SPR_BOS2, 11, 8, A_Fall, S_BOS2_DIE5, 0, 0), // S_BOS2_DIE4
        new(SPR_BOS2, 12, 8, NULL, S_BOS2_DIE6, 0, 0), // S_BOS2_DIE5
        new(SPR_BOS2, 13, 8, NULL, S_BOS2_DIE7, 0, 0), // S_BOS2_DIE6
        new(SPR_BOS2, 14, -1, NULL, S_NULL, 0, 0), // S_BOS2_DIE7
        new(SPR_BOS2, 14, 8, NULL, S_BOS2_RAISE2, 0, 0), // S_BOS2_RAISE1
        new(SPR_BOS2, 13, 8, NULL, S_BOS2_RAISE3, 0, 0), // S_BOS2_RAISE2
        new(SPR_BOS2, 12, 8, NULL, S_BOS2_RAISE4, 0, 0), // S_BOS2_RAISE3
        new(SPR_BOS2, 11, 8, NULL, S_BOS2_RAISE5, 0, 0), // S_BOS2_RAISE4
        new(SPR_BOS2, 10, 8, NULL, S_BOS2_RAISE6, 0, 0), // S_BOS2_RAISE5
        new(SPR_BOS2, 9, 8, NULL, S_BOS2_RAISE7, 0, 0), // S_BOS2_RAISE6
        new(SPR_BOS2, 8, 8, NULL, S_BOS2_RUN1, 0, 0), // S_BOS2_RAISE7
        new(SPR_SKUL, 32768, 10, A_Look, S_SKULL_STND2, 0, 0), // S_SKULL_STND
        new(SPR_SKUL, 32769, 10, A_Look, S_SKULL_STND, 0, 0), // S_SKULL_STND2
        new(SPR_SKUL, 32768, 6, A_Chase, S_SKULL_RUN2, 0, 0), // S_SKULL_RUN1
        new(SPR_SKUL, 32769, 6, A_Chase, S_SKULL_RUN1, 0, 0), // S_SKULL_RUN2
        new(SPR_SKUL, 32770, 10, A_FaceTarget, S_SKULL_ATK2, 0, 0), // S_SKULL_ATK1
        new(SPR_SKUL, 32771, 4, A_SkullAttack, S_SKULL_ATK3, 0, 0), // S_SKULL_ATK2
        new(SPR_SKUL, 32770, 4, NULL, S_SKULL_ATK4, 0, 0), // S_SKULL_ATK3
        new(SPR_SKUL, 32771, 4, NULL, S_SKULL_ATK3, 0, 0), // S_SKULL_ATK4
        new(SPR_SKUL, 32772, 3, NULL, S_SKULL_PAIN2, 0, 0), // S_SKULL_PAIN
        new(SPR_SKUL, 32772, 3, A_Pain, S_SKULL_RUN1, 0, 0), // S_SKULL_PAIN2
        new(SPR_SKUL, 32773, 6, NULL, S_SKULL_DIE2, 0, 0), // S_SKULL_DIE1
        new(SPR_SKUL, 32774, 6, A_Scream, S_SKULL_DIE3, 0, 0), // S_SKULL_DIE2
        new(SPR_SKUL, 32775, 6, NULL, S_SKULL_DIE4, 0, 0), // S_SKULL_DIE3
        new(SPR_SKUL, 32776, 6, A_Fall, S_SKULL_DIE5, 0, 0), // S_SKULL_DIE4
        new(SPR_SKUL, 9, 6, NULL, S_SKULL_DIE6, 0, 0), // S_SKULL_DIE5
        new(SPR_SKUL, 10, 6, NULL, S_NULL, 0, 0), // S_SKULL_DIE6
        new(SPR_SPID, 0, 10, A_Look, S_SPID_STND2, 0, 0), // S_SPID_STND
        new(SPR_SPID, 1, 10, A_Look, S_SPID_STND, 0, 0), // S_SPID_STND2
        new(SPR_SPID, 0, 3, A_Metal, S_SPID_RUN2, 0, 0), // S_SPID_RUN1
        new(SPR_SPID, 0, 3, A_Chase, S_SPID_RUN3, 0, 0), // S_SPID_RUN2
        new(SPR_SPID, 1, 3, A_Chase, S_SPID_RUN4, 0, 0), // S_SPID_RUN3
        new(SPR_SPID, 1, 3, A_Chase, S_SPID_RUN5, 0, 0), // S_SPID_RUN4
        new(SPR_SPID, 2, 3, A_Metal, S_SPID_RUN6, 0, 0), // S_SPID_RUN5
        new(SPR_SPID, 2, 3, A_Chase, S_SPID_RUN7, 0, 0), // S_SPID_RUN6
        new(SPR_SPID, 3, 3, A_Chase, S_SPID_RUN8, 0, 0), // S_SPID_RUN7
        new(SPR_SPID, 3, 3, A_Chase, S_SPID_RUN9, 0, 0), // S_SPID_RUN8
        new(SPR_SPID, 4, 3, A_Metal, S_SPID_RUN10, 0, 0), // S_SPID_RUN9
        new(SPR_SPID, 4, 3, A_Chase, S_SPID_RUN11, 0, 0), // S_SPID_RUN10
        new(SPR_SPID, 5, 3, A_Chase, S_SPID_RUN12, 0, 0), // S_SPID_RUN11
        new(SPR_SPID, 5, 3, A_Chase, S_SPID_RUN1, 0, 0), // S_SPID_RUN12
        new(SPR_SPID, 32768, 20, A_FaceTarget, S_SPID_ATK2, 0, 0), // S_SPID_ATK1
        new(SPR_SPID, 32774, 4, A_SPosAttack, S_SPID_ATK3, 0, 0), // S_SPID_ATK2
        new(SPR_SPID, 32775, 4, A_SPosAttack, S_SPID_ATK4, 0, 0), // S_SPID_ATK3
        new(SPR_SPID, 32775, 1, A_SpidRefire, S_SPID_ATK2, 0, 0), // S_SPID_ATK4
        new(SPR_SPID, 8, 3, NULL, S_SPID_PAIN2, 0, 0), // S_SPID_PAIN
        new(SPR_SPID, 8, 3, A_Pain, S_SPID_RUN1, 0, 0), // S_SPID_PAIN2
        new(SPR_SPID, 9, 20, A_Scream, S_SPID_DIE2, 0, 0), // S_SPID_DIE1
        new(SPR_SPID, 10, 10, A_Fall, S_SPID_DIE3, 0, 0), // S_SPID_DIE2
        new(SPR_SPID, 11, 10, NULL, S_SPID_DIE4, 0, 0), // S_SPID_DIE3
        new(SPR_SPID, 12, 10, NULL, S_SPID_DIE5, 0, 0), // S_SPID_DIE4
        new(SPR_SPID, 13, 10, NULL, S_SPID_DIE6, 0, 0), // S_SPID_DIE5
        new(SPR_SPID, 14, 10, NULL, S_SPID_DIE7, 0, 0), // S_SPID_DIE6
        new(SPR_SPID, 15, 10, NULL, S_SPID_DIE8, 0, 0), // S_SPID_DIE7
        new(SPR_SPID, 16, 10, NULL, S_SPID_DIE9, 0, 0), // S_SPID_DIE8
        new(SPR_SPID, 17, 10, NULL, S_SPID_DIE10, 0, 0), // S_SPID_DIE9
        new(SPR_SPID, 18, 30, NULL, S_SPID_DIE11, 0, 0), // S_SPID_DIE10
        new(SPR_SPID, 18, -1, A_BossDeath, S_NULL, 0, 0), // S_SPID_DIE11
        new(SPR_BSPI, 0, 10, A_Look, S_BSPI_STND2, 0, 0), // S_BSPI_STND
        new(SPR_BSPI, 1, 10, A_Look, S_BSPI_STND, 0, 0), // S_BSPI_STND2
        new(SPR_BSPI, 0, 20, NULL, S_BSPI_RUN1, 0, 0), // S_BSPI_SIGHT
        new(SPR_BSPI, 0, 3, A_BabyMetal, S_BSPI_RUN2, 0, 0), // S_BSPI_RUN1
        new(SPR_BSPI, 0, 3, A_Chase, S_BSPI_RUN3, 0, 0), // S_BSPI_RUN2
        new(SPR_BSPI, 1, 3, A_Chase, S_BSPI_RUN4, 0, 0), // S_BSPI_RUN3
        new(SPR_BSPI, 1, 3, A_Chase, S_BSPI_RUN5, 0, 0), // S_BSPI_RUN4
        new(SPR_BSPI, 2, 3, A_Chase, S_BSPI_RUN6, 0, 0), // S_BSPI_RUN5
        new(SPR_BSPI, 2, 3, A_Chase, S_BSPI_RUN7, 0, 0), // S_BSPI_RUN6
        new(SPR_BSPI, 3, 3, A_BabyMetal, S_BSPI_RUN8, 0, 0), // S_BSPI_RUN7
        new(SPR_BSPI, 3, 3, A_Chase, S_BSPI_RUN9, 0, 0), // S_BSPI_RUN8
        new(SPR_BSPI, 4, 3, A_Chase, S_BSPI_RUN10, 0, 0), // S_BSPI_RUN9
        new(SPR_BSPI, 4, 3, A_Chase, S_BSPI_RUN11, 0, 0), // S_BSPI_RUN10
        new(SPR_BSPI, 5, 3, A_Chase, S_BSPI_RUN12, 0, 0), // S_BSPI_RUN11
        new(SPR_BSPI, 5, 3, A_Chase, S_BSPI_RUN1, 0, 0), // S_BSPI_RUN12
        new(SPR_BSPI, 32768, 20, A_FaceTarget, S_BSPI_ATK2, 0, 0), // S_BSPI_ATK1
        new(SPR_BSPI, 32774, 4, A_BspiAttack, S_BSPI_ATK3, 0, 0), // S_BSPI_ATK2
        new(SPR_BSPI, 32775, 4, NULL, S_BSPI_ATK4, 0, 0), // S_BSPI_ATK3
        new(SPR_BSPI, 32775, 1, A_SpidRefire, S_BSPI_ATK2, 0, 0), // S_BSPI_ATK4
        new(SPR_BSPI, 8, 3, NULL, S_BSPI_PAIN2, 0, 0), // S_BSPI_PAIN
        new(SPR_BSPI, 8, 3, A_Pain, S_BSPI_RUN1, 0, 0), // S_BSPI_PAIN2
        new(SPR_BSPI, 9, 20, A_Scream, S_BSPI_DIE2, 0, 0), // S_BSPI_DIE1
        new(SPR_BSPI, 10, 7, A_Fall, S_BSPI_DIE3, 0, 0), // S_BSPI_DIE2
        new(SPR_BSPI, 11, 7, NULL, S_BSPI_DIE4, 0, 0), // S_BSPI_DIE3
        new(SPR_BSPI, 12, 7, NULL, S_BSPI_DIE5, 0, 0), // S_BSPI_DIE4
        new(SPR_BSPI, 13, 7, NULL, S_BSPI_DIE6, 0, 0), // S_BSPI_DIE5
        new(SPR_BSPI, 14, 7, NULL, S_BSPI_DIE7, 0, 0), // S_BSPI_DIE6
        new(SPR_BSPI, 15, -1, A_BossDeath, S_NULL, 0, 0), // S_BSPI_DIE7
        new(SPR_BSPI, 15, 5, NULL, S_BSPI_RAISE2, 0, 0), // S_BSPI_RAISE1
        new(SPR_BSPI, 14, 5, NULL, S_BSPI_RAISE3, 0, 0), // S_BSPI_RAISE2
        new(SPR_BSPI, 13, 5, NULL, S_BSPI_RAISE4, 0, 0), // S_BSPI_RAISE3
        new(SPR_BSPI, 12, 5, NULL, S_BSPI_RAISE5, 0, 0), // S_BSPI_RAISE4
        new(SPR_BSPI, 11, 5, NULL, S_BSPI_RAISE6, 0, 0), // S_BSPI_RAISE5
        new(SPR_BSPI, 10, 5, NULL, S_BSPI_RAISE7, 0, 0), // S_BSPI_RAISE6
        new(SPR_BSPI, 9, 5, NULL, S_BSPI_RUN1, 0, 0), // S_BSPI_RAISE7
        new(SPR_APLS, 32768, 5, NULL, S_ARACH_PLAZ2, 0, 0), // S_ARACH_PLAZ
        new(SPR_APLS, 32769, 5, NULL, S_ARACH_PLAZ, 0, 0), // S_ARACH_PLAZ2
        new(SPR_APBX, 32768, 5, NULL, S_ARACH_PLEX2, 0, 0), // S_ARACH_PLEX
        new(SPR_APBX, 32769, 5, NULL, S_ARACH_PLEX3, 0, 0), // S_ARACH_PLEX2
        new(SPR_APBX, 32770, 5, NULL, S_ARACH_PLEX4, 0, 0), // S_ARACH_PLEX3
        new(SPR_APBX, 32771, 5, NULL, S_ARACH_PLEX5, 0, 0), // S_ARACH_PLEX4
        new(SPR_APBX, 32772, 5, NULL, S_NULL, 0, 0), // S_ARACH_PLEX5
        new(SPR_CYBR, 0, 10, A_Look, S_CYBER_STND2, 0, 0), // S_CYBER_STND
        new(SPR_CYBR, 1, 10, A_Look, S_CYBER_STND, 0, 0), // S_CYBER_STND2
        new(SPR_CYBR, 0, 3, A_Hoof, S_CYBER_RUN2, 0, 0), // S_CYBER_RUN1
        new(SPR_CYBR, 0, 3, A_Chase, S_CYBER_RUN3, 0, 0), // S_CYBER_RUN2
        new(SPR_CYBR, 1, 3, A_Chase, S_CYBER_RUN4, 0, 0), // S_CYBER_RUN3
        new(SPR_CYBR, 1, 3, A_Chase, S_CYBER_RUN5, 0, 0), // S_CYBER_RUN4
        new(SPR_CYBR, 2, 3, A_Chase, S_CYBER_RUN6, 0, 0), // S_CYBER_RUN5
        new(SPR_CYBR, 2, 3, A_Chase, S_CYBER_RUN7, 0, 0), // S_CYBER_RUN6
        new(SPR_CYBR, 3, 3, A_Metal, S_CYBER_RUN8, 0, 0), // S_CYBER_RUN7
        new(SPR_CYBR, 3, 3, A_Chase, S_CYBER_RUN1, 0, 0), // S_CYBER_RUN8
        new(SPR_CYBR, 4, 6, A_FaceTarget, S_CYBER_ATK2, 0, 0), // S_CYBER_ATK1
        new(SPR_CYBR, 5, 12, A_CyberAttack, S_CYBER_ATK3, 0, 0), // S_CYBER_ATK2
        new(SPR_CYBR, 4, 12, A_FaceTarget, S_CYBER_ATK4, 0, 0), // S_CYBER_ATK3
        new(SPR_CYBR, 5, 12, A_CyberAttack, S_CYBER_ATK5, 0, 0), // S_CYBER_ATK4
        new(SPR_CYBR, 4, 12, A_FaceTarget, S_CYBER_ATK6, 0, 0), // S_CYBER_ATK5
        new(SPR_CYBR, 5, 12, A_CyberAttack, S_CYBER_RUN1, 0, 0), // S_CYBER_ATK6
        new(SPR_CYBR, 6, 10, A_Pain, S_CYBER_RUN1, 0, 0), // S_CYBER_PAIN
        new(SPR_CYBR, 7, 10, NULL, S_CYBER_DIE2, 0, 0), // S_CYBER_DIE1
        new(SPR_CYBR, 8, 10, A_Scream, S_CYBER_DIE3, 0, 0), // S_CYBER_DIE2
        new(SPR_CYBR, 9, 10, NULL, S_CYBER_DIE4, 0, 0), // S_CYBER_DIE3
        new(SPR_CYBR, 10, 10, NULL, S_CYBER_DIE5, 0, 0), // S_CYBER_DIE4
        new(SPR_CYBR, 11, 10, NULL, S_CYBER_DIE6, 0, 0), // S_CYBER_DIE5
        new(SPR_CYBR, 12, 10, A_Fall, S_CYBER_DIE7, 0, 0), // S_CYBER_DIE6
        new(SPR_CYBR, 13, 10, NULL, S_CYBER_DIE8, 0, 0), // S_CYBER_DIE7
        new(SPR_CYBR, 14, 10, NULL, S_CYBER_DIE9, 0, 0), // S_CYBER_DIE8
        new(SPR_CYBR, 15, 30, NULL, S_CYBER_DIE10, 0, 0), // S_CYBER_DIE9
        new(SPR_CYBR, 15, -1, A_BossDeath, S_NULL, 0, 0), // S_CYBER_DIE10
        new(SPR_PAIN, 0, 10, A_Look, S_PAIN_STND, 0, 0), // S_PAIN_STND
        new(SPR_PAIN, 0, 3, A_Chase, S_PAIN_RUN2, 0, 0), // S_PAIN_RUN1
        new(SPR_PAIN, 0, 3, A_Chase, S_PAIN_RUN3, 0, 0), // S_PAIN_RUN2
        new(SPR_PAIN, 1, 3, A_Chase, S_PAIN_RUN4, 0, 0), // S_PAIN_RUN3
        new(SPR_PAIN, 1, 3, A_Chase, S_PAIN_RUN5, 0, 0), // S_PAIN_RUN4
        new(SPR_PAIN, 2, 3, A_Chase, S_PAIN_RUN6, 0, 0), // S_PAIN_RUN5
        new(SPR_PAIN, 2, 3, A_Chase, S_PAIN_RUN1, 0, 0), // S_PAIN_RUN6
        new(SPR_PAIN, 3, 5, A_FaceTarget, S_PAIN_ATK2, 0, 0), // S_PAIN_ATK1
        new(SPR_PAIN, 4, 5, A_FaceTarget, S_PAIN_ATK3, 0, 0), // S_PAIN_ATK2
        new(SPR_PAIN, 32773, 5, A_FaceTarget, S_PAIN_ATK4, 0, 0), // S_PAIN_ATK3
        new(SPR_PAIN, 32773, 0, A_PainAttack, S_PAIN_RUN1, 0, 0), // S_PAIN_ATK4
        new(SPR_PAIN, 6, 6, NULL, S_PAIN_PAIN2, 0, 0), // S_PAIN_PAIN
        new(SPR_PAIN, 6, 6, A_Pain, S_PAIN_RUN1, 0, 0), // S_PAIN_PAIN2
        new(SPR_PAIN, 32775, 8, NULL, S_PAIN_DIE2, 0, 0), // S_PAIN_DIE1
        new(SPR_PAIN, 32776, 8, A_Scream, S_PAIN_DIE3, 0, 0), // S_PAIN_DIE2
        new(SPR_PAIN, 32777, 8, NULL, S_PAIN_DIE4, 0, 0), // S_PAIN_DIE3
        new(SPR_PAIN, 32778, 8, NULL, S_PAIN_DIE5, 0, 0), // S_PAIN_DIE4
        new(SPR_PAIN, 32779, 8, A_PainDie, S_PAIN_DIE6, 0, 0), // S_PAIN_DIE5
        new(SPR_PAIN, 32780, 8, NULL, S_NULL, 0, 0), // S_PAIN_DIE6
        new(SPR_PAIN, 12, 8, NULL, S_PAIN_RAISE2, 0, 0), // S_PAIN_RAISE1
        new(SPR_PAIN, 11, 8, NULL, S_PAIN_RAISE3, 0, 0), // S_PAIN_RAISE2
        new(SPR_PAIN, 10, 8, NULL, S_PAIN_RAISE4, 0, 0), // S_PAIN_RAISE3
        new(SPR_PAIN, 9, 8, NULL, S_PAIN_RAISE5, 0, 0), // S_PAIN_RAISE4
        new(SPR_PAIN, 8, 8, NULL, S_PAIN_RAISE6, 0, 0), // S_PAIN_RAISE5
        new(SPR_PAIN, 7, 8, NULL, S_PAIN_RUN1, 0, 0), // S_PAIN_RAISE6
        new(SPR_SSWV, 0, 10, A_Look, S_SSWV_STND2, 0, 0), // S_SSWV_STND
        new(SPR_SSWV, 1, 10, A_Look, S_SSWV_STND, 0, 0), // S_SSWV_STND2
        new(SPR_SSWV, 0, 3, A_Chase, S_SSWV_RUN2, 0, 0), // S_SSWV_RUN1
        new(SPR_SSWV, 0, 3, A_Chase, S_SSWV_RUN3, 0, 0), // S_SSWV_RUN2
        new(SPR_SSWV, 1, 3, A_Chase, S_SSWV_RUN4, 0, 0), // S_SSWV_RUN3
        new(SPR_SSWV, 1, 3, A_Chase, S_SSWV_RUN5, 0, 0), // S_SSWV_RUN4
        new(SPR_SSWV, 2, 3, A_Chase, S_SSWV_RUN6, 0, 0), // S_SSWV_RUN5
        new(SPR_SSWV, 2, 3, A_Chase, S_SSWV_RUN7, 0, 0), // S_SSWV_RUN6
        new(SPR_SSWV, 3, 3, A_Chase, S_SSWV_RUN8, 0, 0), // S_SSWV_RUN7
        new(SPR_SSWV, 3, 3, A_Chase, S_SSWV_RUN1, 0, 0), // S_SSWV_RUN8
        new(SPR_SSWV, 4, 10, A_FaceTarget, S_SSWV_ATK2, 0, 0), // S_SSWV_ATK1
        new(SPR_SSWV, 5, 10, A_FaceTarget, S_SSWV_ATK3, 0, 0), // S_SSWV_ATK2
        new(SPR_SSWV, 32774, 4, A_CPosAttack, S_SSWV_ATK4, 0, 0), // S_SSWV_ATK3
        new(SPR_SSWV, 5, 6, A_FaceTarget, S_SSWV_ATK5, 0, 0), // S_SSWV_ATK4
        new(SPR_SSWV, 32774, 4, A_CPosAttack, S_SSWV_ATK6, 0, 0), // S_SSWV_ATK5
        new(SPR_SSWV, 5, 1, A_CPosRefire, S_SSWV_ATK2, 0, 0), // S_SSWV_ATK6
        new(SPR_SSWV, 7, 3, NULL, S_SSWV_PAIN2, 0, 0), // S_SSWV_PAIN
        new(SPR_SSWV, 7, 3, A_Pain, S_SSWV_RUN1, 0, 0), // S_SSWV_PAIN2
        new(SPR_SSWV, 8, 5, NULL, S_SSWV_DIE2, 0, 0), // S_SSWV_DIE1
        new(SPR_SSWV, 9, 5, A_Scream, S_SSWV_DIE3, 0, 0), // S_SSWV_DIE2
        new(SPR_SSWV, 10, 5, A_Fall, S_SSWV_DIE4, 0, 0), // S_SSWV_DIE3
        new(SPR_SSWV, 11, 5, NULL, S_SSWV_DIE5, 0, 0), // S_SSWV_DIE4
        new(SPR_SSWV, 12, -1, NULL, S_NULL, 0, 0), // S_SSWV_DIE5
        new(SPR_SSWV, 13, 5, NULL, S_SSWV_XDIE2, 0, 0), // S_SSWV_XDIE1
        new(SPR_SSWV, 14, 5, A_XScream, S_SSWV_XDIE3, 0, 0), // S_SSWV_XDIE2
        new(SPR_SSWV, 15, 5, A_Fall, S_SSWV_XDIE4, 0, 0), // S_SSWV_XDIE3
        new(SPR_SSWV, 16, 5, NULL, S_SSWV_XDIE5, 0, 0), // S_SSWV_XDIE4
        new(SPR_SSWV, 17, 5, NULL, S_SSWV_XDIE6, 0, 0), // S_SSWV_XDIE5
        new(SPR_SSWV, 18, 5, NULL, S_SSWV_XDIE7, 0, 0), // S_SSWV_XDIE6
        new(SPR_SSWV, 19, 5, NULL, S_SSWV_XDIE8, 0, 0), // S_SSWV_XDIE7
        new(SPR_SSWV, 20, 5, NULL, S_SSWV_XDIE9, 0, 0), // S_SSWV_XDIE8
        new(SPR_SSWV, 21, -1, NULL, S_NULL, 0, 0), // S_SSWV_XDIE9
        new(SPR_SSWV, 12, 5, NULL, S_SSWV_RAISE2, 0, 0), // S_SSWV_RAISE1
        new(SPR_SSWV, 11, 5, NULL, S_SSWV_RAISE3, 0, 0), // S_SSWV_RAISE2
        new(SPR_SSWV, 10, 5, NULL, S_SSWV_RAISE4, 0, 0), // S_SSWV_RAISE3
        new(SPR_SSWV, 9, 5, NULL, S_SSWV_RAISE5, 0, 0), // S_SSWV_RAISE4
        new(SPR_SSWV, 8, 5, NULL, S_SSWV_RUN1, 0, 0), // S_SSWV_RAISE5
        new(SPR_KEEN, 0, -1, NULL, S_KEENSTND, 0, 0), // S_KEENSTND
        new(SPR_KEEN, 0, 6, NULL, S_COMMKEEN2, 0, 0), // S_COMMKEEN
        new(SPR_KEEN, 1, 6, NULL, S_COMMKEEN3, 0, 0), // S_COMMKEEN2
        new(SPR_KEEN, 2, 6, A_Scream, S_COMMKEEN4, 0, 0), // S_COMMKEEN3
        new(SPR_KEEN, 3, 6, NULL, S_COMMKEEN5, 0, 0), // S_COMMKEEN4
        new(SPR_KEEN, 4, 6, NULL, S_COMMKEEN6, 0, 0), // S_COMMKEEN5
        new(SPR_KEEN, 5, 6, NULL, S_COMMKEEN7, 0, 0), // S_COMMKEEN6
        new(SPR_KEEN, 6, 6, NULL, S_COMMKEEN8, 0, 0), // S_COMMKEEN7
        new(SPR_KEEN, 7, 6, NULL, S_COMMKEEN9, 0, 0), // S_COMMKEEN8
        new(SPR_KEEN, 8, 6, NULL, S_COMMKEEN10, 0, 0), // S_COMMKEEN9
        new(SPR_KEEN, 9, 6, NULL, S_COMMKEEN11, 0, 0), // S_COMMKEEN10
        new(SPR_KEEN, 10, 6, A_KeenDie, S_COMMKEEN12, 0, 0), // S_COMMKEEN11
        new(SPR_KEEN, 11, -1, NULL, S_NULL, 0, 0), // S_COMMKEEN12
        new(SPR_KEEN, 12, 4, NULL, S_KEENPAIN2, 0, 0), // S_KEENPAIN
        new(SPR_KEEN, 12, 8, A_Pain, S_KEENSTND, 0, 0), // S_KEENPAIN2
        new(SPR_BBRN, 0, -1, NULL, S_NULL, 0, 0), // S_BRAIN
        new(SPR_BBRN, 1, 36, A_BrainPain, S_BRAIN, 0, 0), // S_BRAIN_PAIN
        new(SPR_BBRN, 0, 100, A_BrainScream, S_BRAIN_DIE2, 0, 0), // S_BRAIN_DIE1
        new(SPR_BBRN, 0, 10, NULL, S_BRAIN_DIE3, 0, 0), // S_BRAIN_DIE2
        new(SPR_BBRN, 0, 10, NULL, S_BRAIN_DIE4, 0, 0), // S_BRAIN_DIE3
        new(SPR_BBRN, 0, -1, A_BrainDie, S_NULL, 0, 0), // S_BRAIN_DIE4
        new(SPR_SSWV, 0, 10, A_Look, S_BRAINEYE, 0, 0), // S_BRAINEYE
        new(SPR_SSWV, 0, 181, A_BrainAwake, S_BRAINEYE1, 0, 0), // S_BRAINEYESEE
        new(SPR_SSWV, 0, 150, A_BrainSpit, S_BRAINEYE1, 0, 0), // S_BRAINEYE1
        new(SPR_BOSF, 32768, 3, A_SpawnSound, S_SPAWN2, 0, 0), // S_SPAWN1
        new(SPR_BOSF, 32769, 3, A_SpawnFly, S_SPAWN3, 0, 0), // S_SPAWN2
        new(SPR_BOSF, 32770, 3, A_SpawnFly, S_SPAWN4, 0, 0), // S_SPAWN3
        new(SPR_BOSF, 32771, 3, A_SpawnFly, S_SPAWN1, 0, 0), // S_SPAWN4
        new(SPR_FIRE, 32768, 4, A_Fire, S_SPAWNFIRE2, 0, 0), // S_SPAWNFIRE1
        new(SPR_FIRE, 32769, 4, A_Fire, S_SPAWNFIRE3, 0, 0), // S_SPAWNFIRE2
        new(SPR_FIRE, 32770, 4, A_Fire, S_SPAWNFIRE4, 0, 0), // S_SPAWNFIRE3
        new(SPR_FIRE, 32771, 4, A_Fire, S_SPAWNFIRE5, 0, 0), // S_SPAWNFIRE4
        new(SPR_FIRE, 32772, 4, A_Fire, S_SPAWNFIRE6, 0, 0), // S_SPAWNFIRE5
        new(SPR_FIRE, 32773, 4, A_Fire, S_SPAWNFIRE7, 0, 0), // S_SPAWNFIRE6
        new(SPR_FIRE, 32774, 4, A_Fire, S_SPAWNFIRE8, 0, 0), // S_SPAWNFIRE7
        new(SPR_FIRE, 32775, 4, A_Fire, S_NULL, 0, 0), // S_SPAWNFIRE8
        new(SPR_MISL, 32769, 10, NULL, S_BRAINEXPLODE2, 0, 0), // S_BRAINEXPLODE1
        new(SPR_MISL, 32770, 10, NULL, S_BRAINEXPLODE3, 0, 0), // S_BRAINEXPLODE2
        new(SPR_MISL, 32771, 10, A_BrainExplode, S_NULL, 0, 0), // S_BRAINEXPLODE3
        new(SPR_ARM1, 0, 6, NULL, S_ARM1A, 0, 0), // S_ARM1
        new(SPR_ARM1, 32769, 7, NULL, S_ARM1, 0, 0), // S_ARM1A
        new(SPR_ARM2, 0, 6, NULL, S_ARM2A, 0, 0), // S_ARM2
        new(SPR_ARM2, 32769, 6, NULL, S_ARM2, 0, 0), // S_ARM2A
        new(SPR_BAR1, 0, 6, NULL, S_BAR2, 0, 0), // S_BAR1
        new(SPR_BAR1, 1, 6, NULL, S_BAR1, 0, 0), // S_BAR2
        new(SPR_BEXP, 32768, 5, NULL, S_BEXP2, 0, 0), // S_BEXP
        new(SPR_BEXP, 32769, 5, A_Scream, S_BEXP3, 0, 0), // S_BEXP2
        new(SPR_BEXP, 32770, 5, NULL, S_BEXP4, 0, 0), // S_BEXP3
        new(SPR_BEXP, 32771, 10, A_Explode, S_BEXP5, 0, 0), // S_BEXP4
        new(SPR_BEXP, 32772, 10, NULL, S_NULL, 0, 0), // S_BEXP5
        new(SPR_FCAN, 32768, 4, NULL, S_BBAR2, 0, 0), // S_BBAR1
        new(SPR_FCAN, 32769, 4, NULL, S_BBAR3, 0, 0), // S_BBAR2
        new(SPR_FCAN, 32770, 4, NULL, S_BBAR1, 0, 0), // S_BBAR3
        new(SPR_BON1, 0, 6, NULL, S_BON1A, 0, 0), // S_BON1
        new(SPR_BON1, 1, 6, NULL, S_BON1B, 0, 0), // S_BON1A
        new(SPR_BON1, 2, 6, NULL, S_BON1C, 0, 0), // S_BON1B
        new(SPR_BON1, 3, 6, NULL, S_BON1D, 0, 0), // S_BON1C
        new(SPR_BON1, 2, 6, NULL, S_BON1E, 0, 0), // S_BON1D
        new(SPR_BON1, 1, 6, NULL, S_BON1, 0, 0), // S_BON1E
        new(SPR_BON2, 0, 6, NULL, S_BON2A, 0, 0), // S_BON2
        new(SPR_BON2, 1, 6, NULL, S_BON2B, 0, 0), // S_BON2A
        new(SPR_BON2, 2, 6, NULL, S_BON2C, 0, 0), // S_BON2B
        new(SPR_BON2, 3, 6, NULL, S_BON2D, 0, 0), // S_BON2C
        new(SPR_BON2, 2, 6, NULL, S_BON2E, 0, 0), // S_BON2D
        new(SPR_BON2, 1, 6, NULL, S_BON2, 0, 0), // S_BON2E
        new(SPR_BKEY, 0, 10, NULL, S_BKEY2, 0, 0), // S_BKEY
        new(SPR_BKEY, 32769, 10, NULL, S_BKEY, 0, 0), // S_BKEY2
        new(SPR_RKEY, 0, 10, NULL, S_RKEY2, 0, 0), // S_RKEY
        new(SPR_RKEY, 32769, 10, NULL, S_RKEY, 0, 0), // S_RKEY2
        new(SPR_YKEY, 0, 10, NULL, S_YKEY2, 0, 0), // S_YKEY
        new(SPR_YKEY, 32769, 10, NULL, S_YKEY, 0, 0), // S_YKEY2
        new(SPR_BSKU, 0, 10, NULL, S_BSKULL2, 0, 0), // S_BSKULL
        new(SPR_BSKU, 32769, 10, NULL, S_BSKULL, 0, 0), // S_BSKULL2
        new(SPR_RSKU, 0, 10, NULL, S_RSKULL2, 0, 0), // S_RSKULL
        new(SPR_RSKU, 32769, 10, NULL, S_RSKULL, 0, 0), // S_RSKULL2
        new(SPR_YSKU, 0, 10, NULL, S_YSKULL2, 0, 0), // S_YSKULL
        new(SPR_YSKU, 32769, 10, NULL, S_YSKULL, 0, 0), // S_YSKULL2
        new(SPR_STIM, 0, -1, NULL, S_NULL, 0, 0), // S_STIM
        new(SPR_MEDI, 0, -1, NULL, S_NULL, 0, 0), // S_MEDI
        new(SPR_SOUL, 32768, 6, NULL, S_SOUL2, 0, 0), // S_SOUL
        new(SPR_SOUL, 32769, 6, NULL, S_SOUL3, 0, 0), // S_SOUL2
        new(SPR_SOUL, 32770, 6, NULL, S_SOUL4, 0, 0), // S_SOUL3
        new(SPR_SOUL, 32771, 6, NULL, S_SOUL5, 0, 0), // S_SOUL4
        new(SPR_SOUL, 32770, 6, NULL, S_SOUL6, 0, 0), // S_SOUL5
        new(SPR_SOUL, 32769, 6, NULL, S_SOUL, 0, 0), // S_SOUL6
        new(SPR_PINV, 32768, 6, NULL, S_PINV2, 0, 0), // S_PINV
        new(SPR_PINV, 32769, 6, NULL, S_PINV3, 0, 0), // S_PINV2
        new(SPR_PINV, 32770, 6, NULL, S_PINV4, 0, 0), // S_PINV3
        new(SPR_PINV, 32771, 6, NULL, S_PINV, 0, 0), // S_PINV4
        new(SPR_PSTR, 32768, -1, NULL, S_NULL, 0, 0), // S_PSTR
        new(SPR_PINS, 32768, 6, NULL, S_PINS2, 0, 0), // S_PINS
        new(SPR_PINS, 32769, 6, NULL, S_PINS3, 0, 0), // S_PINS2
        new(SPR_PINS, 32770, 6, NULL, S_PINS4, 0, 0), // S_PINS3
        new(SPR_PINS, 32771, 6, NULL, S_PINS, 0, 0), // S_PINS4
        new(SPR_MEGA, 32768, 6, NULL, S_MEGA2, 0, 0), // S_MEGA
        new(SPR_MEGA, 32769, 6, NULL, S_MEGA3, 0, 0), // S_MEGA2
        new(SPR_MEGA, 32770, 6, NULL, S_MEGA4, 0, 0), // S_MEGA3
        new(SPR_MEGA, 32771, 6, NULL, S_MEGA, 0, 0), // S_MEGA4
        new(SPR_SUIT, 32768, -1, NULL, S_NULL, 0, 0), // S_SUIT
        new(SPR_PMAP, 32768, 6, NULL, S_PMAP2, 0, 0), // S_PMAP
        new(SPR_PMAP, 32769, 6, NULL, S_PMAP3, 0, 0), // S_PMAP2
        new(SPR_PMAP, 32770, 6, NULL, S_PMAP4, 0, 0), // S_PMAP3
        new(SPR_PMAP, 32771, 6, NULL, S_PMAP5, 0, 0), // S_PMAP4
        new(SPR_PMAP, 32770, 6, NULL, S_PMAP6, 0, 0), // S_PMAP5
        new(SPR_PMAP, 32769, 6, NULL, S_PMAP, 0, 0), // S_PMAP6
        new(SPR_PVIS, 32768, 6, NULL, S_PVIS2, 0, 0), // S_PVIS
        new(SPR_PVIS, 1, 6, NULL, S_PVIS, 0, 0), // S_PVIS2
        new(SPR_CLIP, 0, -1, NULL, S_NULL, 0, 0), // S_CLIP
        new(SPR_AMMO, 0, -1, NULL, S_NULL, 0, 0), // S_AMMO
        new(SPR_ROCK, 0, -1, NULL, S_NULL, 0, 0), // S_ROCK
        new(SPR_BROK, 0, -1, NULL, S_NULL, 0, 0), // S_BROK
        new(SPR_CELL, 0, -1, NULL, S_NULL, 0, 0), // S_CELL
        new(SPR_CELP, 0, -1, NULL, S_NULL, 0, 0), // S_CELP
        new(SPR_SHEL, 0, -1, NULL, S_NULL, 0, 0), // S_SHEL
        new(SPR_SBOX, 0, -1, NULL, S_NULL, 0, 0), // S_SBOX
        new(SPR_BPAK, 0, -1, NULL, S_NULL, 0, 0), // S_BPAK
        new(SPR_BFUG, 0, -1, NULL, S_NULL, 0, 0), // S_BFUG
        new(SPR_MGUN, 0, -1, NULL, S_NULL, 0, 0), // S_MGUN
        new(SPR_CSAW, 0, -1, NULL, S_NULL, 0, 0), // S_CSAW
        new(SPR_LAUN, 0, -1, NULL, S_NULL, 0, 0), // S_LAUN
        new(SPR_PLAS, 0, -1, NULL, S_NULL, 0, 0), // S_PLAS
        new(SPR_SHOT, 0, -1, NULL, S_NULL, 0, 0), // S_SHOT
        new(SPR_SGN2, 0, -1, NULL, S_NULL, 0, 0), // S_SHOT2
        new(SPR_COLU, 32768, -1, NULL, S_NULL, 0, 0), // S_COLU
        new(SPR_SMT2, 0, -1, NULL, S_NULL, 0, 0), // S_STALAG
        new(SPR_GOR1, 0, 10, NULL, S_BLOODYTWITCH2, 0, 0), // S_BLOODYTWITCH
        new(SPR_GOR1, 1, 15, NULL, S_BLOODYTWITCH3, 0, 0), // S_BLOODYTWITCH2
        new(SPR_GOR1, 2, 8, NULL, S_BLOODYTWITCH4, 0, 0), // S_BLOODYTWITCH3
        new(SPR_GOR1, 1, 6, NULL, S_BLOODYTWITCH, 0, 0), // S_BLOODYTWITCH4
        new(SPR_PLAY, 13, -1, NULL, S_NULL, 0, 0), // S_DEADTORSO
        new(SPR_PLAY, 18, -1, NULL, S_NULL, 0, 0), // S_DEADBOTTOM
        new(SPR_POL2, 0, -1, NULL, S_NULL, 0, 0), // S_HEADSONSTICK
        new(SPR_POL5, 0, -1, NULL, S_NULL, 0, 0), // S_GIBS
        new(SPR_POL4, 0, -1, NULL, S_NULL, 0, 0), // S_HEADONASTICK
        new(SPR_POL3, 32768, 6, NULL, S_HEADCANDLES2, 0, 0), // S_HEADCANDLES
        new(SPR_POL3, 32769, 6, NULL, S_HEADCANDLES, 0, 0), // S_HEADCANDLES2
        new(SPR_POL1, 0, -1, NULL, S_NULL, 0, 0), // S_DEADSTICK
        new(SPR_POL6, 0, 6, NULL, S_LIVESTICK2, 0, 0), // S_LIVESTICK
        new(SPR_POL6, 1, 8, NULL, S_LIVESTICK, 0, 0), // S_LIVESTICK2
        new(SPR_GOR2, 0, -1, NULL, S_NULL, 0, 0), // S_MEAT2
        new(SPR_GOR3, 0, -1, NULL, S_NULL, 0, 0), // S_MEAT3
        new(SPR_GOR4, 0, -1, NULL, S_NULL, 0, 0), // S_MEAT4
        new(SPR_GOR5, 0, -1, NULL, S_NULL, 0, 0), // S_MEAT5
        new(SPR_SMIT, 0, -1, NULL, S_NULL, 0, 0), // S_STALAGTITE
        new(SPR_COL1, 0, -1, NULL, S_NULL, 0, 0), // S_TALLGRNCOL
        new(SPR_COL2, 0, -1, NULL, S_NULL, 0, 0), // S_SHRTGRNCOL
        new(SPR_COL3, 0, -1, NULL, S_NULL, 0, 0), // S_TALLREDCOL
        new(SPR_COL4, 0, -1, NULL, S_NULL, 0, 0), // S_SHRTREDCOL
        new(SPR_CAND, 32768, -1, NULL, S_NULL, 0, 0), // S_CANDLESTIK
        new(SPR_CBRA, 32768, -1, NULL, S_NULL, 0, 0), // S_CANDELABRA
        new(SPR_COL6, 0, -1, NULL, S_NULL, 0, 0), // S_SKULLCOL
        new(SPR_TRE1, 0, -1, NULL, S_NULL, 0, 0), // S_TORCHTREE
        new(SPR_TRE2, 0, -1, NULL, S_NULL, 0, 0), // S_BIGTREE
        new(SPR_ELEC, 0, -1, NULL, S_NULL, 0, 0), // S_TECHPILLAR
        new(SPR_CEYE, 32768, 6, NULL, S_EVILEYE2, 0, 0), // S_EVILEYE
        new(SPR_CEYE, 32769, 6, NULL, S_EVILEYE3, 0, 0), // S_EVILEYE2
        new(SPR_CEYE, 32770, 6, NULL, S_EVILEYE4, 0, 0), // S_EVILEYE3
        new(SPR_CEYE, 32769, 6, NULL, S_EVILEYE, 0, 0), // S_EVILEYE4
        new(SPR_FSKU, 32768, 6, NULL, S_FLOATSKULL2, 0, 0), // S_FLOATSKULL
        new(SPR_FSKU, 32769, 6, NULL, S_FLOATSKULL3, 0, 0), // S_FLOATSKULL2
        new(SPR_FSKU, 32770, 6, NULL, S_FLOATSKULL, 0, 0), // S_FLOATSKULL3
        new(SPR_COL5, 0, 14, NULL, S_HEARTCOL2, 0, 0), // S_HEARTCOL
        new(SPR_COL5, 1, 14, NULL, S_HEARTCOL, 0, 0), // S_HEARTCOL2
        new(SPR_TBLU, 32768, 4, NULL, S_BLUETORCH2, 0, 0), // S_BLUETORCH
        new(SPR_TBLU, 32769, 4, NULL, S_BLUETORCH3, 0, 0), // S_BLUETORCH2
        new(SPR_TBLU, 32770, 4, NULL, S_BLUETORCH4, 0, 0), // S_BLUETORCH3
        new(SPR_TBLU, 32771, 4, NULL, S_BLUETORCH, 0, 0), // S_BLUETORCH4
        new(SPR_TGRN, 32768, 4, NULL, S_GREENTORCH2, 0, 0), // S_GREENTORCH
        new(SPR_TGRN, 32769, 4, NULL, S_GREENTORCH3, 0, 0), // S_GREENTORCH2
        new(SPR_TGRN, 32770, 4, NULL, S_GREENTORCH4, 0, 0), // S_GREENTORCH3
        new(SPR_TGRN, 32771, 4, NULL, S_GREENTORCH, 0, 0), // S_GREENTORCH4
        new(SPR_TRED, 32768, 4, NULL, S_REDTORCH2, 0, 0), // S_REDTORCH
        new(SPR_TRED, 32769, 4, NULL, S_REDTORCH3, 0, 0), // S_REDTORCH2
        new(SPR_TRED, 32770, 4, NULL, S_REDTORCH4, 0, 0), // S_REDTORCH3
        new(SPR_TRED, 32771, 4, NULL, S_REDTORCH, 0, 0), // S_REDTORCH4
        new(SPR_SMBT, 32768, 4, NULL, S_BTORCHSHRT2, 0, 0), // S_BTORCHSHRT
        new(SPR_SMBT, 32769, 4, NULL, S_BTORCHSHRT3, 0, 0), // S_BTORCHSHRT2
        new(SPR_SMBT, 32770, 4, NULL, S_BTORCHSHRT4, 0, 0), // S_BTORCHSHRT3
        new(SPR_SMBT, 32771, 4, NULL, S_BTORCHSHRT, 0, 0), // S_BTORCHSHRT4
        new(SPR_SMGT, 32768, 4, NULL, S_GTORCHSHRT2, 0, 0), // S_GTORCHSHRT
        new(SPR_SMGT, 32769, 4, NULL, S_GTORCHSHRT3, 0, 0), // S_GTORCHSHRT2
        new(SPR_SMGT, 32770, 4, NULL, S_GTORCHSHRT4, 0, 0), // S_GTORCHSHRT3
        new(SPR_SMGT, 32771, 4, NULL, S_GTORCHSHRT, 0, 0), // S_GTORCHSHRT4
        new(SPR_SMRT, 32768, 4, NULL, S_RTORCHSHRT2, 0, 0), // S_RTORCHSHRT
        new(SPR_SMRT, 32769, 4, NULL, S_RTORCHSHRT3, 0, 0), // S_RTORCHSHRT2
        new(SPR_SMRT, 32770, 4, NULL, S_RTORCHSHRT4, 0, 0), // S_RTORCHSHRT3
        new(SPR_SMRT, 32771, 4, NULL, S_RTORCHSHRT, 0, 0), // S_RTORCHSHRT4
        new(SPR_HDB1, 0, -1, NULL, S_NULL, 0, 0), // S_HANGNOGUTS
        new(SPR_HDB2, 0, -1, NULL, S_NULL, 0, 0), // S_HANGBNOBRAIN
        new(SPR_HDB3, 0, -1, NULL, S_NULL, 0, 0), // S_HANGTLOOKDN
        new(SPR_HDB4, 0, -1, NULL, S_NULL, 0, 0), // S_HANGTSKULL
        new(SPR_HDB5, 0, -1, NULL, S_NULL, 0, 0), // S_HANGTLOOKUP
        new(SPR_HDB6, 0, -1, NULL, S_NULL, 0, 0), // S_HANGTNOBRAIN
        new(SPR_POB1, 0, -1, NULL, S_NULL, 0, 0), // S_COLONGIBS
        new(SPR_POB2, 0, -1, NULL, S_NULL, 0, 0), // S_SMALLPOOL
        new(SPR_BRS1, 0, -1, NULL, S_NULL, 0, 0), // S_BRAINSTEM
        new(SPR_TLMP, 32768, 4, NULL, S_TECHLAMP2, 0, 0), // S_TECHLAMP
        new(SPR_TLMP, 32769, 4, NULL, S_TECHLAMP3, 0, 0), // S_TECHLAMP2
        new(SPR_TLMP, 32770, 4, NULL, S_TECHLAMP4, 0, 0), // S_TECHLAMP3
        new(SPR_TLMP, 32771, 4, NULL, S_TECHLAMP, 0, 0), // S_TECHLAMP4
        new(SPR_TLP2, 32768, 4, NULL, S_TECH2LAMP2, 0, 0), // S_TECH2LAMP
        new(SPR_TLP2, 32769, 4, NULL, S_TECH2LAMP3, 0, 0), // S_TECH2LAMP2
        new(SPR_TLP2, 32770, 4, NULL, S_TECH2LAMP4, 0, 0), // S_TECH2LAMP3
        new(SPR_TLP2, 32771, 4, NULL, S_TECH2LAMP, 0, 0), // S_TECH2LAMP4
    ];

    /// <summary>info.c <c>mobjinfo[NUMMOBJTYPES]</c>.</summary>
    public static readonly mobjinfo_t[] mobjinfo =
    [
        new() // MT_PLAYER
        {
            doomednum = -1,
            spawnstate = S_PLAY,
            spawnhealth = 100,
            seestate = S_PLAY_RUN1,
            seesound = sfx_None,
            reactiontime = 0,
            attacksound = sfx_None,
            painstate = S_PLAY_PAIN,
            painchance = 255,
            painsound = sfx_plpain,
            meleestate = S_NULL,
            missilestate = S_PLAY_ATK1,
            deathstate = S_PLAY_DIE1,
            xdeathstate = S_PLAY_XDIE1,
            deathsound = sfx_pldeth,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SHOOTABLE | MF_DROPOFF | MF_PICKUP | MF_NOTDMATCH,
            raisestate = S_NULL,
        },
        new() // MT_POSSESSED
        {
            doomednum = 3004,
            spawnstate = S_POSS_STND,
            spawnhealth = 20,
            seestate = S_POSS_RUN1,
            seesound = sfx_posit1,
            reactiontime = 8,
            attacksound = sfx_pistol,
            painstate = S_POSS_PAIN,
            painchance = 200,
            painsound = sfx_popain,
            meleestate = 0,
            missilestate = S_POSS_ATK1,
            deathstate = S_POSS_DIE1,
            xdeathstate = S_POSS_XDIE1,
            deathsound = sfx_podth1,
            speed = 8,
            radius = 20 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_posact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_POSS_RAISE1,
        },
        new() // MT_SHOTGUY
        {
            doomednum = 9,
            spawnstate = S_SPOS_STND,
            spawnhealth = 30,
            seestate = S_SPOS_RUN1,
            seesound = sfx_posit2,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_SPOS_PAIN,
            painchance = 170,
            painsound = sfx_popain,
            meleestate = 0,
            missilestate = S_SPOS_ATK1,
            deathstate = S_SPOS_DIE1,
            xdeathstate = S_SPOS_XDIE1,
            deathsound = sfx_podth2,
            speed = 8,
            radius = 20 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_posact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_SPOS_RAISE1,
        },
        new() // MT_VILE
        {
            doomednum = 64,
            spawnstate = S_VILE_STND,
            spawnhealth = 700,
            seestate = S_VILE_RUN1,
            seesound = sfx_vilsit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_VILE_PAIN,
            painchance = 10,
            painsound = sfx_vipain,
            meleestate = 0,
            missilestate = S_VILE_ATK1,
            deathstate = S_VILE_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_vildth,
            speed = 15,
            radius = 20 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 500,
            damage = 0,
            activesound = sfx_vilact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_NULL,
        },
        new() // MT_FIRE
        {
            doomednum = -1,
            spawnstate = S_FIRE1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_UNDEAD
        {
            doomednum = 66,
            spawnstate = S_SKEL_STND,
            spawnhealth = 300,
            seestate = S_SKEL_RUN1,
            seesound = sfx_skesit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_SKEL_PAIN,
            painchance = 100,
            painsound = sfx_popain,
            meleestate = S_SKEL_FIST1,
            missilestate = S_SKEL_MISS1,
            deathstate = S_SKEL_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_skedth,
            speed = 10,
            radius = 20 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 500,
            damage = 0,
            activesound = sfx_skeact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_SKEL_RAISE1,
        },
        new() // MT_TRACER
        {
            doomednum = -1,
            spawnstate = S_TRACER,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_skeatk,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_TRACEEXP1,
            xdeathstate = S_NULL,
            deathsound = sfx_barexp,
            speed = 10 * FRACUNIT,
            radius = 11 * FRACUNIT,
            height = 8 * FRACUNIT,
            mass = 100,
            damage = 10,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_SMOKE
        {
            doomednum = -1,
            spawnstate = S_SMOKE1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_FATSO
        {
            doomednum = 67,
            spawnstate = S_FATT_STND,
            spawnhealth = 600,
            seestate = S_FATT_RUN1,
            seesound = sfx_mansit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_FATT_PAIN,
            painchance = 80,
            painsound = sfx_mnpain,
            meleestate = 0,
            missilestate = S_FATT_ATK1,
            deathstate = S_FATT_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_mandth,
            speed = 8,
            radius = 48 * FRACUNIT,
            height = 64 * FRACUNIT,
            mass = 1000,
            damage = 0,
            activesound = sfx_posact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_FATT_RAISE1,
        },
        new() // MT_FATSHOT
        {
            doomednum = -1,
            spawnstate = S_FATSHOT1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_firsht,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_FATSHOTX1,
            xdeathstate = S_NULL,
            deathsound = sfx_firxpl,
            speed = 20 * FRACUNIT,
            radius = 6 * FRACUNIT,
            height = 8 * FRACUNIT,
            mass = 100,
            damage = 8,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_CHAINGUY
        {
            doomednum = 65,
            spawnstate = S_CPOS_STND,
            spawnhealth = 70,
            seestate = S_CPOS_RUN1,
            seesound = sfx_posit2,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_CPOS_PAIN,
            painchance = 170,
            painsound = sfx_popain,
            meleestate = 0,
            missilestate = S_CPOS_ATK1,
            deathstate = S_CPOS_DIE1,
            xdeathstate = S_CPOS_XDIE1,
            deathsound = sfx_podth2,
            speed = 8,
            radius = 20 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_posact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_CPOS_RAISE1,
        },
        new() // MT_TROOP
        {
            doomednum = 3001,
            spawnstate = S_TROO_STND,
            spawnhealth = 60,
            seestate = S_TROO_RUN1,
            seesound = sfx_bgsit1,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_TROO_PAIN,
            painchance = 200,
            painsound = sfx_popain,
            meleestate = S_TROO_ATK1,
            missilestate = S_TROO_ATK1,
            deathstate = S_TROO_DIE1,
            xdeathstate = S_TROO_XDIE1,
            deathsound = sfx_bgdth1,
            speed = 8,
            radius = 20 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_bgact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_TROO_RAISE1,
        },
        new() // MT_SERGEANT
        {
            doomednum = 3002,
            spawnstate = S_SARG_STND,
            spawnhealth = 150,
            seestate = S_SARG_RUN1,
            seesound = sfx_sgtsit,
            reactiontime = 8,
            attacksound = sfx_sgtatk,
            painstate = S_SARG_PAIN,
            painchance = 180,
            painsound = sfx_dmpain,
            meleestate = S_SARG_ATK1,
            missilestate = 0,
            deathstate = S_SARG_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_sgtdth,
            speed = 10,
            radius = 30 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 400,
            damage = 0,
            activesound = sfx_dmact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_SARG_RAISE1,
        },
        new() // MT_SHADOWS
        {
            doomednum = 58,
            spawnstate = S_SARG_STND,
            spawnhealth = 150,
            seestate = S_SARG_RUN1,
            seesound = sfx_sgtsit,
            reactiontime = 8,
            attacksound = sfx_sgtatk,
            painstate = S_SARG_PAIN,
            painchance = 180,
            painsound = sfx_dmpain,
            meleestate = S_SARG_ATK1,
            missilestate = 0,
            deathstate = S_SARG_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_sgtdth,
            speed = 10,
            radius = 30 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 400,
            damage = 0,
            activesound = sfx_dmact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_SHADOW | MF_COUNTKILL,
            raisestate = S_SARG_RAISE1,
        },
        new() // MT_HEAD
        {
            doomednum = 3005,
            spawnstate = S_HEAD_STND,
            spawnhealth = 400,
            seestate = S_HEAD_RUN1,
            seesound = sfx_cacsit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_HEAD_PAIN,
            painchance = 128,
            painsound = sfx_dmpain,
            meleestate = 0,
            missilestate = S_HEAD_ATK1,
            deathstate = S_HEAD_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_cacdth,
            speed = 8,
            radius = 31 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 400,
            damage = 0,
            activesound = sfx_dmact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_FLOAT | MF_NOGRAVITY | MF_COUNTKILL,
            raisestate = S_HEAD_RAISE1,
        },
        new() // MT_BRUISER
        {
            doomednum = 3003,
            spawnstate = S_BOSS_STND,
            spawnhealth = 1000,
            seestate = S_BOSS_RUN1,
            seesound = sfx_brssit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_BOSS_PAIN,
            painchance = 50,
            painsound = sfx_dmpain,
            meleestate = S_BOSS_ATK1,
            missilestate = S_BOSS_ATK1,
            deathstate = S_BOSS_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_brsdth,
            speed = 8,
            radius = 24 * FRACUNIT,
            height = 64 * FRACUNIT,
            mass = 1000,
            damage = 0,
            activesound = sfx_dmact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_BOSS_RAISE1,
        },
        new() // MT_BRUISERSHOT
        {
            doomednum = -1,
            spawnstate = S_BRBALL1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_firsht,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_BRBALLX1,
            xdeathstate = S_NULL,
            deathsound = sfx_firxpl,
            speed = 15 * FRACUNIT,
            radius = 6 * FRACUNIT,
            height = 8 * FRACUNIT,
            mass = 100,
            damage = 8,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_KNIGHT
        {
            doomednum = 69,
            spawnstate = S_BOS2_STND,
            spawnhealth = 500,
            seestate = S_BOS2_RUN1,
            seesound = sfx_kntsit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_BOS2_PAIN,
            painchance = 50,
            painsound = sfx_dmpain,
            meleestate = S_BOS2_ATK1,
            missilestate = S_BOS2_ATK1,
            deathstate = S_BOS2_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_kntdth,
            speed = 8,
            radius = 24 * FRACUNIT,
            height = 64 * FRACUNIT,
            mass = 1000,
            damage = 0,
            activesound = sfx_dmact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_BOS2_RAISE1,
        },
        new() // MT_SKULL
        {
            doomednum = 3006,
            spawnstate = S_SKULL_STND,
            spawnhealth = 100,
            seestate = S_SKULL_RUN1,
            seesound = 0,
            reactiontime = 8,
            attacksound = sfx_sklatk,
            painstate = S_SKULL_PAIN,
            painchance = 256,
            painsound = sfx_dmpain,
            meleestate = 0,
            missilestate = S_SKULL_ATK1,
            deathstate = S_SKULL_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_firxpl,
            speed = 8,
            radius = 16 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 50,
            damage = 3,
            activesound = sfx_dmact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_FLOAT | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_SPIDER
        {
            doomednum = 7,
            spawnstate = S_SPID_STND,
            spawnhealth = 3000,
            seestate = S_SPID_RUN1,
            seesound = sfx_spisit,
            reactiontime = 8,
            attacksound = sfx_shotgn,
            painstate = S_SPID_PAIN,
            painchance = 40,
            painsound = sfx_dmpain,
            meleestate = 0,
            missilestate = S_SPID_ATK1,
            deathstate = S_SPID_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_spidth,
            speed = 12,
            radius = 128 * FRACUNIT,
            height = 100 * FRACUNIT,
            mass = 1000,
            damage = 0,
            activesound = sfx_dmact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_NULL,
        },
        new() // MT_BABY
        {
            doomednum = 68,
            spawnstate = S_BSPI_STND,
            spawnhealth = 500,
            seestate = S_BSPI_SIGHT,
            seesound = sfx_bspsit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_BSPI_PAIN,
            painchance = 128,
            painsound = sfx_dmpain,
            meleestate = 0,
            missilestate = S_BSPI_ATK1,
            deathstate = S_BSPI_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_bspdth,
            speed = 12,
            radius = 64 * FRACUNIT,
            height = 64 * FRACUNIT,
            mass = 600,
            damage = 0,
            activesound = sfx_bspact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_BSPI_RAISE1,
        },
        new() // MT_CYBORG
        {
            doomednum = 16,
            spawnstate = S_CYBER_STND,
            spawnhealth = 4000,
            seestate = S_CYBER_RUN1,
            seesound = sfx_cybsit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_CYBER_PAIN,
            painchance = 20,
            painsound = sfx_dmpain,
            meleestate = 0,
            missilestate = S_CYBER_ATK1,
            deathstate = S_CYBER_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_cybdth,
            speed = 16,
            radius = 40 * FRACUNIT,
            height = 110 * FRACUNIT,
            mass = 1000,
            damage = 0,
            activesound = sfx_dmact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_NULL,
        },
        new() // MT_PAIN
        {
            doomednum = 71,
            spawnstate = S_PAIN_STND,
            spawnhealth = 400,
            seestate = S_PAIN_RUN1,
            seesound = sfx_pesit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_PAIN_PAIN,
            painchance = 128,
            painsound = sfx_pepain,
            meleestate = 0,
            missilestate = S_PAIN_ATK1,
            deathstate = S_PAIN_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_pedth,
            speed = 8,
            radius = 31 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 400,
            damage = 0,
            activesound = sfx_dmact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_FLOAT | MF_NOGRAVITY | MF_COUNTKILL,
            raisestate = S_PAIN_RAISE1,
        },
        new() // MT_WOLFSS
        {
            doomednum = 84,
            spawnstate = S_SSWV_STND,
            spawnhealth = 50,
            seestate = S_SSWV_RUN1,
            seesound = sfx_sssit,
            reactiontime = 8,
            attacksound = 0,
            painstate = S_SSWV_PAIN,
            painchance = 170,
            painsound = sfx_popain,
            meleestate = 0,
            missilestate = S_SSWV_ATK1,
            deathstate = S_SSWV_DIE1,
            xdeathstate = S_SSWV_XDIE1,
            deathsound = sfx_ssdth,
            speed = 8,
            radius = 20 * FRACUNIT,
            height = 56 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_posact,
            flags = MF_SOLID | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_SSWV_RAISE1,
        },
        new() // MT_KEEN
        {
            doomednum = 72,
            spawnstate = S_KEENSTND,
            spawnhealth = 100,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_KEENPAIN,
            painchance = 256,
            painsound = sfx_keenpn,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_COMMKEEN,
            xdeathstate = S_NULL,
            deathsound = sfx_keendt,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 72 * FRACUNIT,
            mass = 10000000,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY | MF_SHOOTABLE | MF_COUNTKILL,
            raisestate = S_NULL,
        },
        new() // MT_BOSSBRAIN
        {
            doomednum = 88,
            spawnstate = S_BRAIN,
            spawnhealth = 250,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_BRAIN_PAIN,
            painchance = 255,
            painsound = sfx_bospn,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_BRAIN_DIE1,
            xdeathstate = S_NULL,
            deathsound = sfx_bosdth,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 10000000,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SHOOTABLE,
            raisestate = S_NULL,
        },
        new() // MT_BOSSSPIT
        {
            doomednum = 89,
            spawnstate = S_BRAINEYE,
            spawnhealth = 1000,
            seestate = S_BRAINEYESEE,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 32 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOSECTOR,
            raisestate = S_NULL,
        },
        new() // MT_BOSSTARGET
        {
            doomednum = 87,
            spawnstate = S_NULL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 32 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOSECTOR,
            raisestate = S_NULL,
        },
        new() // MT_SPAWNSHOT
        {
            doomednum = -1,
            spawnstate = S_SPAWN1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_bospit,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_firxpl,
            speed = 10 * FRACUNIT,
            radius = 6 * FRACUNIT,
            height = 32 * FRACUNIT,
            mass = 100,
            damage = 3,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY | MF_NOCLIP,
            raisestate = S_NULL,
        },
        new() // MT_SPAWNFIRE
        {
            doomednum = -1,
            spawnstate = S_SPAWNFIRE1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_BARREL
        {
            doomednum = 2035,
            spawnstate = S_BAR1,
            spawnhealth = 20,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_BEXP,
            xdeathstate = S_NULL,
            deathsound = sfx_barexp,
            speed = 0,
            radius = 10 * FRACUNIT,
            height = 42 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SHOOTABLE | MF_NOBLOOD,
            raisestate = S_NULL,
        },
        new() // MT_TROOPSHOT
        {
            doomednum = -1,
            spawnstate = S_TBALL1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_firsht,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_TBALLX1,
            xdeathstate = S_NULL,
            deathsound = sfx_firxpl,
            speed = 10 * FRACUNIT,
            radius = 6 * FRACUNIT,
            height = 8 * FRACUNIT,
            mass = 100,
            damage = 3,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_HEADSHOT
        {
            doomednum = -1,
            spawnstate = S_RBALL1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_firsht,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_RBALLX1,
            xdeathstate = S_NULL,
            deathsound = sfx_firxpl,
            speed = 10 * FRACUNIT,
            radius = 6 * FRACUNIT,
            height = 8 * FRACUNIT,
            mass = 100,
            damage = 5,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_ROCKET
        {
            doomednum = -1,
            spawnstate = S_ROCKET,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_rlaunc,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_EXPLODE1,
            xdeathstate = S_NULL,
            deathsound = sfx_barexp,
            speed = 20 * FRACUNIT,
            radius = 11 * FRACUNIT,
            height = 8 * FRACUNIT,
            mass = 100,
            damage = 20,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_PLASMA
        {
            doomednum = -1,
            spawnstate = S_PLASBALL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_plasma,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_PLASEXP,
            xdeathstate = S_NULL,
            deathsound = sfx_firxpl,
            speed = 25 * FRACUNIT,
            radius = 13 * FRACUNIT,
            height = 8 * FRACUNIT,
            mass = 100,
            damage = 5,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_BFG
        {
            doomednum = -1,
            spawnstate = S_BFGSHOT,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = 0,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_BFGLAND,
            xdeathstate = S_NULL,
            deathsound = sfx_rxplod,
            speed = 25 * FRACUNIT,
            radius = 13 * FRACUNIT,
            height = 8 * FRACUNIT,
            mass = 100,
            damage = 100,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_ARACHPLAZ
        {
            doomednum = -1,
            spawnstate = S_ARACH_PLAZ,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_plasma,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_ARACH_PLEX,
            xdeathstate = S_NULL,
            deathsound = sfx_firxpl,
            speed = 25 * FRACUNIT,
            radius = 13 * FRACUNIT,
            height = 8 * FRACUNIT,
            mass = 100,
            damage = 5,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_MISSILE | MF_DROPOFF | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_PUFF
        {
            doomednum = -1,
            spawnstate = S_PUFF1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_BLOOD
        {
            doomednum = -1,
            spawnstate = S_BLOOD1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP,
            raisestate = S_NULL,
        },
        new() // MT_TFOG
        {
            doomednum = -1,
            spawnstate = S_TFOG,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_IFOG
        {
            doomednum = -1,
            spawnstate = S_IFOG,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_TELEPORTMAN
        {
            doomednum = 14,
            spawnstate = S_NULL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOSECTOR,
            raisestate = S_NULL,
        },
        new() // MT_EXTRABFG
        {
            doomednum = -1,
            spawnstate = S_BFGEXP,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC0
        {
            doomednum = 2018,
            spawnstate = S_ARM1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC1
        {
            doomednum = 2019,
            spawnstate = S_ARM2,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC2
        {
            doomednum = 2014,
            spawnstate = S_BON1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_COUNTITEM,
            raisestate = S_NULL,
        },
        new() // MT_MISC3
        {
            doomednum = 2015,
            spawnstate = S_BON2,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_COUNTITEM,
            raisestate = S_NULL,
        },
        new() // MT_MISC4
        {
            doomednum = 5,
            spawnstate = S_BKEY,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_NOTDMATCH,
            raisestate = S_NULL,
        },
        new() // MT_MISC5
        {
            doomednum = 13,
            spawnstate = S_RKEY,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_NOTDMATCH,
            raisestate = S_NULL,
        },
        new() // MT_MISC6
        {
            doomednum = 6,
            spawnstate = S_YKEY,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_NOTDMATCH,
            raisestate = S_NULL,
        },
        new() // MT_MISC7
        {
            doomednum = 39,
            spawnstate = S_YSKULL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_NOTDMATCH,
            raisestate = S_NULL,
        },
        new() // MT_MISC8
        {
            doomednum = 38,
            spawnstate = S_RSKULL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_NOTDMATCH,
            raisestate = S_NULL,
        },
        new() // MT_MISC9
        {
            doomednum = 40,
            spawnstate = S_BSKULL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_NOTDMATCH,
            raisestate = S_NULL,
        },
        new() // MT_MISC10
        {
            doomednum = 2011,
            spawnstate = S_STIM,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC11
        {
            doomednum = 2012,
            spawnstate = S_MEDI,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC12
        {
            doomednum = 2013,
            spawnstate = S_SOUL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_COUNTITEM,
            raisestate = S_NULL,
        },
        new() // MT_INV
        {
            doomednum = 2022,
            spawnstate = S_PINV,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_COUNTITEM,
            raisestate = S_NULL,
        },
        new() // MT_MISC13
        {
            doomednum = 2023,
            spawnstate = S_PSTR,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_COUNTITEM,
            raisestate = S_NULL,
        },
        new() // MT_INS
        {
            doomednum = 2024,
            spawnstate = S_PINS,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_COUNTITEM,
            raisestate = S_NULL,
        },
        new() // MT_MISC14
        {
            doomednum = 2025,
            spawnstate = S_SUIT,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC15
        {
            doomednum = 2026,
            spawnstate = S_PMAP,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_COUNTITEM,
            raisestate = S_NULL,
        },
        new() // MT_MISC16
        {
            doomednum = 2045,
            spawnstate = S_PVIS,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_COUNTITEM,
            raisestate = S_NULL,
        },
        new() // MT_MEGA
        {
            doomednum = 83,
            spawnstate = S_MEGA,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL | MF_COUNTITEM,
            raisestate = S_NULL,
        },
        new() // MT_CLIP
        {
            doomednum = 2007,
            spawnstate = S_CLIP,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC17
        {
            doomednum = 2048,
            spawnstate = S_AMMO,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC18
        {
            doomednum = 2010,
            spawnstate = S_ROCK,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC19
        {
            doomednum = 2046,
            spawnstate = S_BROK,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC20
        {
            doomednum = 2047,
            spawnstate = S_CELL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC21
        {
            doomednum = 17,
            spawnstate = S_CELP,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC22
        {
            doomednum = 2008,
            spawnstate = S_SHEL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC23
        {
            doomednum = 2049,
            spawnstate = S_SBOX,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC24
        {
            doomednum = 8,
            spawnstate = S_BPAK,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC25
        {
            doomednum = 2006,
            spawnstate = S_BFUG,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_CHAINGUN
        {
            doomednum = 2002,
            spawnstate = S_MGUN,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC26
        {
            doomednum = 2005,
            spawnstate = S_CSAW,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC27
        {
            doomednum = 2003,
            spawnstate = S_LAUN,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC28
        {
            doomednum = 2004,
            spawnstate = S_PLAS,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_SHOTGUN
        {
            doomednum = 2001,
            spawnstate = S_SHOT,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_SUPERSHOTGUN
        {
            doomednum = 82,
            spawnstate = S_SHOT2,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPECIAL,
            raisestate = S_NULL,
        },
        new() // MT_MISC29
        {
            doomednum = 85,
            spawnstate = S_TECHLAMP,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC30
        {
            doomednum = 86,
            spawnstate = S_TECH2LAMP,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC31
        {
            doomednum = 2028,
            spawnstate = S_COLU,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC32
        {
            doomednum = 30,
            spawnstate = S_TALLGRNCOL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC33
        {
            doomednum = 31,
            spawnstate = S_SHRTGRNCOL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC34
        {
            doomednum = 32,
            spawnstate = S_TALLREDCOL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC35
        {
            doomednum = 33,
            spawnstate = S_SHRTREDCOL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC36
        {
            doomednum = 37,
            spawnstate = S_SKULLCOL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC37
        {
            doomednum = 36,
            spawnstate = S_HEARTCOL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC38
        {
            doomednum = 41,
            spawnstate = S_EVILEYE,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC39
        {
            doomednum = 42,
            spawnstate = S_FLOATSKULL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC40
        {
            doomednum = 43,
            spawnstate = S_TORCHTREE,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC41
        {
            doomednum = 44,
            spawnstate = S_BLUETORCH,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC42
        {
            doomednum = 45,
            spawnstate = S_GREENTORCH,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC43
        {
            doomednum = 46,
            spawnstate = S_REDTORCH,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC44
        {
            doomednum = 55,
            spawnstate = S_BTORCHSHRT,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC45
        {
            doomednum = 56,
            spawnstate = S_GTORCHSHRT,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC46
        {
            doomednum = 57,
            spawnstate = S_RTORCHSHRT,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC47
        {
            doomednum = 47,
            spawnstate = S_STALAGTITE,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC48
        {
            doomednum = 48,
            spawnstate = S_TECHPILLAR,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC49
        {
            doomednum = 34,
            spawnstate = S_CANDLESTIK,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC50
        {
            doomednum = 35,
            spawnstate = S_CANDELABRA,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC51
        {
            doomednum = 49,
            spawnstate = S_BLOODYTWITCH,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 68 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC52
        {
            doomednum = 50,
            spawnstate = S_MEAT2,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 84 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC53
        {
            doomednum = 51,
            spawnstate = S_MEAT3,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 84 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC54
        {
            doomednum = 52,
            spawnstate = S_MEAT4,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 68 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC55
        {
            doomednum = 53,
            spawnstate = S_MEAT5,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 52 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC56
        {
            doomednum = 59,
            spawnstate = S_MEAT2,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 84 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC57
        {
            doomednum = 60,
            spawnstate = S_MEAT4,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 68 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC58
        {
            doomednum = 61,
            spawnstate = S_MEAT3,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 52 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC59
        {
            doomednum = 62,
            spawnstate = S_MEAT5,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 52 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC60
        {
            doomednum = 63,
            spawnstate = S_BLOODYTWITCH,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 68 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC61
        {
            doomednum = 22,
            spawnstate = S_HEAD_DIE6,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC62
        {
            doomednum = 15,
            spawnstate = S_PLAY_DIE7,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC63
        {
            doomednum = 18,
            spawnstate = S_POSS_DIE5,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC64
        {
            doomednum = 21,
            spawnstate = S_SARG_DIE6,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC65
        {
            doomednum = 23,
            spawnstate = S_SKULL_DIE6,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC66
        {
            doomednum = 20,
            spawnstate = S_TROO_DIE5,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC67
        {
            doomednum = 19,
            spawnstate = S_SPOS_DIE5,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC68
        {
            doomednum = 10,
            spawnstate = S_PLAY_XDIE9,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC69
        {
            doomednum = 12,
            spawnstate = S_PLAY_XDIE9,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC70
        {
            doomednum = 28,
            spawnstate = S_HEADSONSTICK,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC71
        {
            doomednum = 24,
            spawnstate = S_GIBS,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = 0,
            raisestate = S_NULL,
        },
        new() // MT_MISC72
        {
            doomednum = 27,
            spawnstate = S_HEADONASTICK,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC73
        {
            doomednum = 29,
            spawnstate = S_HEADCANDLES,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC74
        {
            doomednum = 25,
            spawnstate = S_DEADSTICK,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC75
        {
            doomednum = 26,
            spawnstate = S_LIVESTICK,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC76
        {
            doomednum = 54,
            spawnstate = S_BIGTREE,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 32 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC77
        {
            doomednum = 70,
            spawnstate = S_BBAR1,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID,
            raisestate = S_NULL,
        },
        new() // MT_MISC78
        {
            doomednum = 73,
            spawnstate = S_HANGNOGUTS,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 88 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC79
        {
            doomednum = 74,
            spawnstate = S_HANGBNOBRAIN,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 88 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC80
        {
            doomednum = 75,
            spawnstate = S_HANGTLOOKDN,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 64 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC81
        {
            doomednum = 76,
            spawnstate = S_HANGTSKULL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 64 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC82
        {
            doomednum = 77,
            spawnstate = S_HANGTLOOKUP,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 64 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC83
        {
            doomednum = 78,
            spawnstate = S_HANGTNOBRAIN,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 16 * FRACUNIT,
            height = 64 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_SOLID | MF_SPAWNCEILING | MF_NOGRAVITY,
            raisestate = S_NULL,
        },
        new() // MT_MISC84
        {
            doomednum = 79,
            spawnstate = S_COLONGIBS,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP,
            raisestate = S_NULL,
        },
        new() // MT_MISC85
        {
            doomednum = 80,
            spawnstate = S_SMALLPOOL,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP,
            raisestate = S_NULL,
        },
        new() // MT_MISC86
        {
            doomednum = 81,
            spawnstate = S_BRAINSTEM,
            spawnhealth = 1000,
            seestate = S_NULL,
            seesound = sfx_None,
            reactiontime = 8,
            attacksound = sfx_None,
            painstate = S_NULL,
            painchance = 0,
            painsound = sfx_None,
            meleestate = S_NULL,
            missilestate = S_NULL,
            deathstate = S_NULL,
            xdeathstate = S_NULL,
            deathsound = sfx_None,
            speed = 0,
            radius = 20 * FRACUNIT,
            height = 16 * FRACUNIT,
            mass = 100,
            damage = 0,
            activesound = sfx_None,
            flags = MF_NOBLOCKMAP,
            raisestate = S_NULL,
        },
    ];
}
