// IsoDoom vanilla reference renderer (T2.9; dev tool, never shipped): a
// doomgeneric platform layer with no window. After `-warp`, it puts player 1
// at each view of $VIEWS ("X Y ANGLE;X Y ANGLE;...", map units and degrees),
// standing still at eye level, with the sector lights as the map lump has
// them (no light specials) and, with $DUMP_STATIC set, no animated textures
// or scrolling walls; or (T5.7) with $DUMP_TIC=N, all of them at the end of
// tic N (leveltime N: the lights, animations and scrolling walls as they run
// with the player standing at its start, no reset), and writes
// $OUTDIR/vN.ppm: vanilla's full-screen
// 320x200 view in palette 0, with masked middles (T3.1) but without sprites,
// ceilings or sky; pixels that draw nothing (ceilings, sky) are cyan (0, 255, 255), a
// colour in no PLAYPAL; and $OUTDIR/vN.z, the eye height (viewz, map units:
// the player stands on the highest floor within its radius, so near a step
// that is not the floor at the point). Built by build.sh against doomgeneric
// (GPL-2.0).
//
// The movement reference (T4.8): with $DUMP_TICS set (and no $VIEWS), it
// plays a demo (-playdemo) with every map special (dump_nospecials in
// ref.patch only lets the synthetic IWAD load as -file: line triggers,
// doors, switches and P_UpdateSpecials run since T5.2-T5.4, the sector
// specials and animations since T5.7, the player's special sectors since
// T5.8) and
// appends one line per tic to the file $DUMP_TICS, after the
// tic: leveltime, the ticcmd read (forwardmove, sidemove, angleturn,
// buttons), player 1's mobj x, y, z, momx, momy, momz, angle (unsigned),
// viewz, the P_Random index, and the mobj's state (statenum_t) and tics
// (fixed_t and BAM as integers); then (T5.3) the sectors whose floor or
// ceiling height differs from the map's SECTORS lump, as
// SECTOR:FLOOR:CEILING (fixed_t) joined by commas in sector order, or - for
// none; then (T5.4) the sidedef textures that differ from the map's SIDEDEFS
// lump (switches), as SIDE:PART:NAME (PART t, m or b; NAME upper case)
// joined by commas in sidedef and part order, or - for none; then (T5.6)
// the teleport fogs (MT_TFOG mobjs) in thinker order as X:Y:Z:STATE
// (fixed_t, statenum_t) joined by commas, or - for none; then (T5.7) the
// sectors whose light level differs from the map's SECTORS lump, as
// SECTOR:LIGHT joined by commas in sector order, or - for none; then (T5.8)
// the player's health, its mobj's health, armorpoints, armortype, cards (bit
// i set for card_t i) and secretcount; then (T6.1) the inventory as
// ITEMCOUNT:CLIP:SHELL:CELL:MISL:OWNED:BACKPACK (OWNED: bit i set for
// weapontype_t i) and the mobjs (every
// P_MobjThinker thinker, in thinker order) as COUNT:HASH, HASH the 32-bit
// FNV-1a (word-wise, 8 hex digits) of each one's type, state, tics, x, y, z,
// angle, flags and health; then (T6.6) the weapon as
// READY:PENDING:EXTRALIGHT:REFIRE:ATTACKDOWN:WSTATE:WTICS:WSX:WSY:FSTATE:FTICS:FSX:FSY
// (readyweapon, pendingweapon, the weapon and flash psprites' statenum_t,
// tics, sx and sy; state -1 for none); then (T6.8) the powers as
// INVULN:STRENGTH:INVIS:IRONFEET:ALLMAP:INFRARED:DAMAGECOUNT:BONUSCOUNT:FIXEDCOLORMAP:PALETTE
// (powers[] in powertype_t order, damagecount, bonuscount, fixedcolormap, and
// the palette st_stuff.c's ST_doPaletteStuff sets, called here after the
// tic; ref.patch makes its st_palette visible); then (T6.10) the sounds:
// every S_StartSound call of the tic (ref.patch calls dump_sound first thing
// in s_sound.c's S_StartSound, before its audibility checks) and P_RemoveMobj's
// S_StopSound, in order, as NAME@ORIGIN (NAME the S_sfx name, e.g. pistol,
// or stop for a stop) joined by commas, or - for none; ORIGIN is - for
// none, sN for sector N's soundorg, TYPE:X:Y for a mobj (mobjtype_t, its
// x and y when called; any P_MobjThinker thinker), ? for anything else
// (p_spec.c's button release passes the address of the button's soundorg
// field); then (T6.11, written after ST_Ticker and HU_Ticker: dump_posttic) the HUD as
// FACE:FACECOUNT:KEY0:KEY1:KEY2:READY:MSGON:MSGCOUNTER:TEXT:PIXELS (st_faceindex,
// st_facecount, keyboxes[], *w_ready.num (1994 for none), message_on,
// message_counter, the 32-bit FNV-1a of the message line's text bytes, and
// of the status bar's pixels (rows 168-199, drawn as in play: ST_Drawer(false,
// false) each tic) then the message line's (rows 0-15, 256 where HU_Drawer draws
// nothing), word-wise; 8 hex digits each); then (T5.9) exit: 0, or 1 (2) when the
// tic left the level by its exit (secret exit): gameaction is ga_completed.
// With $DUMP_START
// ("X Y ANGLE", map units and degrees; T5.6), player 1 starts there instead
// of at its map start: before the first tic it is moved with P_TeleportMove
// onto the floor, facing ANGLE (no fog, nothing else changed); again at the
// start of each reload of the level after a reborn (T6.12: whenever
// leveltime is 0). With
// $DUMP_EVENTS ("TIC damage X Y AMOUNT;TIC alert;...", TIC 0-based, in
// order; T6.4), at the start of the route's tic TIC (counted from the first
// tic: leveltime TIC until a reborn reloads the level), before the players
// think: P_DamageMobj(thing, mo, mo, AMOUNT) on the first mobj in thinker
// order spawned at map point X, Y (spawnpoint) that is shootable and alive
// (none: exit 1), or P_NoiseAlert(mo, mo), or (T6.5, "TIC rocket")
// P_SpawnPlayerMissile(mo, MT_ROCKET), mo player 1's mobj: the route's
// stand-ins for the player's shots.
#include "doomgeneric.h"
#include "doomstat.h"
#include "d_player.h"
#include "d_event.h"
#include "p_local.h"
#include "r_main.h"
#include "r_state.h"
#include "w_wad.h"
#include "z_zone.h"
#include "i_video.h"
#include "i_sound.h"
#include "st_stuff.h"
#include "st_lib.h"
#include "hu_stuff.h"
#include "hu_lib.h"
#include "wi_stuff.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <ctype.h>

#define MAX_VIEWS 4096

extern byte *I_VideoBuffer;
static int views[MAX_VIEWS][3], nviews, cur = -1, wait;
static int dump_tic_target; // $DUMP_TIC (T5.7): render the views at the end of this tic
static uint32_t fake_ms;
int dump_noceil; // read by the patched r_main.c / r_plane.c
int dump_nospecials; // read by the patched p_spec.c
static FILE *ticfile;
extern int prndindex;
extern boolean nodrawers;
static byte first[SCREENWIDTH * SCREENHEIGHT];

static void parse_events(void);

void DG_Init(void)
{
    char *v = getenv("VIEWS");
    for (char *tok = strtok(strdup(v ? v : ""), ";"); tok && nviews < MAX_VIEWS; tok = strtok(NULL, ";"))
        if (sscanf(tok, "%d %d %d", &views[nviews][0], &views[nviews][1], &views[nviews][2]) == 3)
            nviews++;
    char *dt = getenv("DUMP_TIC");
    if (dt && *dt)
        dump_tic_target = atoi(dt);
    char *t = getenv("DUMP_TICS");
    if (t && *t)
    {
        if (!(ticfile = fopen(t, "w")))
        {
            perror(t);
            exit(1);
        }
        dump_nospecials = 1;
        nodrawers = true; // no rendering: the synthetic map lacks the player's sprites
        parse_events();
    }
}

static byte *map_sectors(void);
static byte *map_lump(int lump);
static void capture_tic(void);
// r_data.c's textures: each starts with its name (char[8], not terminated when 8 long).
extern char **textures;

// $DUMP_EVENTS (T6.4): "TIC damage X Y AMOUNT;TIC alert;TIC rocket;..." in tic order.
// kind: 0 damage, 1 alert, 2 rocket (T6.5).
#define MAX_EVENTS 4096
static struct { int tic, kind, x, y, amount; } events[MAX_EVENTS];
static int nevents, nextevent;
static int routetic; // the route's tics so far (T6.12: leveltime starts again after a reborn)

static void parse_events(void)
{
    char *e = getenv("DUMP_EVENTS");
    for (char *tok = strtok(strdup(e ? e : ""), ";"); tok && nevents < MAX_EVENTS; tok = strtok(NULL, ";"))
    {
        int tic, x, y, amount;
        if (sscanf(tok, "%d damage %d %d %d", &tic, &x, &y, &amount) == 4)
            events[nevents++] = (typeof(events[0])){ tic, 0, x, y, amount };
        else if (sscanf(tok, "%d alert", &tic) == 1 && strstr(tok, "alert"))
            events[nevents++] = (typeof(events[0])){ tic, 1, 0, 0, 0 };
        else if (sscanf(tok, "%d rocket", &tic) == 1 && strstr(tok, "rocket"))
            events[nevents++] = (typeof(events[0])){ tic, 2, 0, 0, 0 };
        else
        {
            fprintf(stderr, "DUMP_EVENTS: bad event \"%s\"\n", tok);
            exit(1);
        }
    }
}

// The sounds of the tic so far (T6.10), written and reset by dump_tic.
static char sounds[1 << 16];
static size_t soundslen;

// Called by the patched s_sound.c (S_StartSound, sfx_id >= 1) and p_mobj.c
// (P_RemoveMobj's S_StopSound, sfx_id 0) (T6.10).
void dump_sound(void *origin, int sfx_id)
{
    if (!ticfile)
        return;
    char where[64] = "?";
    if (!origin)
        strcpy(where, "-");
    for (int i = 0; i < numsectors && where[0] == '?'; i++)
        if (origin == (void *)&sectors[i].soundorg)
            snprintf(where, sizeof where, "s%d", i);
    for (thinker_t *th = thinkercap.next; th != &thinkercap && where[0] == '?'; th = th->next)
        if (th == origin && th->function.acp1 == (actionf_p1)P_MobjThinker)
        {
            mobj_t *m = (mobj_t *)th;
            snprintf(where, sizeof where, "%d:%d:%d", m->type, m->x, m->y);
        }
    extern sfxinfo_t S_sfx[];
    int n = snprintf(sounds + soundslen, sizeof sounds - soundslen, "%s%s@%s", soundslen ? "," : "",
                     sfx_id ? S_sfx[sfx_id].name : "stop", where);
    if (n < 0 || soundslen + n >= sizeof sounds)
    {
        fprintf(stderr, "dump_sound: too many sounds in tic %d\n", leveltime + 1);
        exit(1);
    }
    soundslen += n;
}

// Called by the patched p_tick.c before the players think in every P_Ticker (T5.6).
void dump_pretic(void)
{
    if (!ticfile)
        return;
    mobj_t *mo = players[consoleplayer].mo;
    char *start = getenv("DUMP_START");
    int x, y, angle;
    if (leveltime == 0 && start && sscanf(start, "%d %d %d", &x, &y, &angle) == 3)
    {
        P_TeleportMove(mo, x << FRACBITS, y << FRACBITS);
        mo->z = mo->floorz;
        mo->angle = (angle_t)((long long)angle * 0x100000000LL / 360);
    }
    // The route's events of this tic (T6.4): stand-ins for the player's shots.
    for (; nextevent < nevents && events[nextevent].tic <= routetic; nextevent++)
    {
        if (events[nextevent].tic < routetic)
            continue;
        if (events[nextevent].kind == 1)
        {
            P_NoiseAlert(mo, mo);
            continue;
        }
        if (events[nextevent].kind == 2)
        {
            P_SpawnPlayerMissile(mo, MT_ROCKET);
            continue;
        }
        mobj_t *target = NULL;
        for (thinker_t *th = thinkercap.next; th != &thinkercap && !target; th = th->next)
        {
            if (th->function.acp1 != (actionf_p1)P_MobjThinker)
                continue;
            mobj_t *m = (mobj_t *)th;
            if (m->spawnpoint.x == events[nextevent].x && m->spawnpoint.y == events[nextevent].y
                && (m->flags & MF_SHOOTABLE) && m->health > 0)
                target = m;
        }
        if (!target)
        {
            fprintf(stderr, "DUMP_EVENTS: tic %d: nothing shootable spawned at (%d, %d) to damage\n",
                    routetic + 1, events[nextevent].x, events[nextevent].y);
            exit(1);
        }
        P_DamageMobj(target, mo, mo, events[nextevent].amount);
    }
    routetic++;
}

// Called by the patched p_tick.c at the end of every P_Ticker.
void dump_tic(void)
{
    if (dump_tic_target && nviews > 0 && leveltime == dump_tic_target)
        capture_tic();
    if (!ticfile)
        return;
    player_t *p = &players[consoleplayer];
    mobj_t *mo = p->mo;
    fprintf(ticfile, "%d %d %d %d %d %d %d %d %d %d %d %u %d %d %d %d ", leveltime,
            p->cmd.forwardmove, p->cmd.sidemove, p->cmd.angleturn, p->cmd.buttons,
            mo->x, mo->y, mo->z, mo->momx, mo->momy, mo->momz, mo->angle, p->viewz, prndindex,
            (int)(mo->state - states), mo->tics);
    // The moved sectors (T5.3).
    byte *data = map_sectors();
    int moved = 0;
    for (int i = 0; i < numsectors; i++)
    {
        fixed_t floor = (short)(data[26 * i] | data[26 * i + 1] << 8) * FRACUNIT;
        fixed_t ceiling = (short)(data[26 * i + 2] | data[26 * i + 3] << 8) * FRACUNIT;
        if (sectors[i].floorheight != floor || sectors[i].ceilingheight != ceiling)
            fprintf(ticfile, "%s%d:%d:%d", moved++ ? "," : "", i, sectors[i].floorheight, sectors[i].ceilingheight);
    }
    fprintf(ticfile, "%s ", moved ? "" : "-");
    // The switched textures (T5.4).
    byte *sidedata = map_lump(ML_SIDEDEFS);
    int changed = 0;
    for (int i = 0; i < numsides; i++)
    {
        const char *names[3] = { (char *)sidedata + 30 * i + 4, (char *)sidedata + 30 * i + 20, (char *)sidedata + 30 * i + 12 };
        int now[3] = { sides[i].toptexture, sides[i].midtexture, sides[i].bottomtexture };
        for (int k = 0; k < 3; k++)
        {
            char name[9] = { 0 };
            memcpy(name, names[k], 8);
            if (now[k] == R_TextureNumForName(name))
                continue;
            fprintf(ticfile, "%s%d:%c:", changed++ ? "," : "", i, "tmb"[k]);
            for (int c = 0; c < 8 && textures[now[k]][c]; c++)
                fputc(toupper((unsigned char)textures[now[k]][c]), ticfile);
        }
    }
    fprintf(ticfile, "%s ", changed ? "" : "-");
    // The teleport fogs (T5.6).
    int fogs = 0;
    for (thinker_t *th = thinkercap.next; th != &thinkercap; th = th->next)
    {
        if (th->function.acp1 != (actionf_p1)P_MobjThinker)
            continue;
        mobj_t *m = (mobj_t *)th;
        if (m->type == MT_TFOG)
            fprintf(ticfile, "%s%d:%d:%d:%d", fogs++ ? "," : "", m->x, m->y, m->z, (int)(m->state - states));
    }
    fprintf(ticfile, "%s ", fogs ? "" : "-");
    // The light levels (T5.7).
    int lit = 0;
    for (int i = 0; i < numsectors; i++)
    {
        short light = (short)(data[26 * i + 20] | data[26 * i + 21] << 8);
        if (sectors[i].lightlevel != light)
            fprintf(ticfile, "%s%d:%d", lit++ ? "," : "", i, sectors[i].lightlevel);
    }
    fprintf(ticfile, "%s ", lit ? "" : "-");
    // The player's health, armor, keys and secrets (T5.8).
    int cards = 0;
    for (int i = 0; i < NUMCARDS; i++)
        cards |= p->cards[i] ? 1 << i : 0;
    fprintf(ticfile, "%d %d %d %d %d %d ", p->health, mo->health, p->armorpoints, p->armortype, cards, p->secretcount);
    // The inventory and the mobjs' states (T6.1).
    int owned = 0;
    for (int i = 0; i < NUMWEAPONS; i++)
        owned |= p->weaponowned[i] ? 1 << i : 0;
    fprintf(ticfile, "%d:%d:%d:%d:%d:%d:%d ", p->itemcount, p->ammo[am_clip], p->ammo[am_shell], p->ammo[am_cell], p->ammo[am_misl], owned, p->backpack ? 1 : 0);
    uint32_t hash = 2166136261u;
    int count = 0;
    for (thinker_t *th = thinkercap.next; th != &thinkercap; th = th->next)
    {
        if (th->function.acp1 != (actionf_p1)P_MobjThinker)
            continue;
        mobj_t *m = (mobj_t *)th;
        int32_t v[9] = { m->type, (int)(m->state - states), m->tics, m->x, m->y, m->z, (int32_t)m->angle, m->flags, m->health };
        for (int k = 0; k < 9; k++)
            hash = (hash ^ (uint32_t)v[k]) * 16777619u;
        count++;
    }
    fprintf(ticfile, "%d:%08x ", count, hash);
    // The weapon and its psprites (T6.6).
    fprintf(ticfile, "%d:%d:%d:%d:%d", p->readyweapon, p->pendingweapon, p->extralight, p->refire, p->attackdown ? 1 : 0);
    for (int i = 0; i < NUMPSPRITES; i++)
        fprintf(ticfile, ":%d:%d:%d:%d", p->psprites[i].state ? (int)(p->psprites[i].state - states) : -1,
                p->psprites[i].tics, p->psprites[i].sx, p->psprites[i].sy);
    fputc(' ', ticfile);
    // The powers and the palette (T6.8).
    extern void ST_doPaletteStuff(void);
    extern int st_palette;
    ST_doPaletteStuff();
    for (int i = 0; i < NUMPOWERS; i++)
        fprintf(ticfile, "%d:", p->powers[i]);
    fprintf(ticfile, "%d:%d:%d:%d ", p->damagecount, p->bonuscount, p->fixedcolormap, st_palette);
    // The sounds (T6.10).
    fprintf(ticfile, "%s ", soundslen ? sounds : "-");
    soundslen = 0;
    // (T6.11: the line goes on in dump_posttic, after ST_Ticker and HU_Ticker.)
}

// Called by the patched g_game.c after HU_Ticker in every G_Ticker of a level
// (T6.11): the HUD column, then the exit, ending the tic's line.
void dump_posttic(void)
{
    if (!ticfile)
        return;
    // The status bar and the message line (T6.11): drawn now as in play
    // (ST_Drawer(false, false): the whole bar after ST_Start, then what
    // changed, onto the bar left on screen, which nothing else draws over
    // with nodrawers), HU_Drawer over two clear colours (pixels that differ
    // were not drawn).
    extern int st_faceindex, st_facecount, keyboxes[3], message_counter, st_palette;
    extern st_number_t w_ready;
    extern boolean message_on;
    extern hu_stext_t w_message;
    static byte hu[2][SCREENWIDTH * 16];
    ST_Drawer(false, false);
    for (int k = 0; k < 2; k++)
    {
        memset(I_VideoBuffer, k ? 4 : 0, sizeof hu[k]);
        HU_Drawer();
        memcpy(hu[k], I_VideoBuffer, sizeof hu[k]);
    }
    uint32_t pixels = 2166136261u, text = 2166136261u;
    for (int i = ST_Y * SCREENWIDTH; i < SCREENWIDTH * SCREENHEIGHT; i++)
        pixels = (pixels ^ I_VideoBuffer[i]) * 16777619u;
    for (int i = 0; i < SCREENWIDTH * 16; i++)
        pixels = (pixels ^ (hu[0][i] == hu[1][i] ? hu[0][i] : 256u)) * 16777619u;
    hu_textline_t *l = &w_message.l[w_message.cl];
    for (int i = 0; i < l->len; i++)
        text = (text ^ (byte)l->l[i]) * 16777619u;
    fprintf(ticfile, "%d:%d:%d:%d:%d:%d:%d:%d:%08x:%08x ", st_faceindex, st_facecount, keyboxes[0], keyboxes[1], keyboxes[2],
            *w_ready.num, message_on ? 1 : 0, message_counter, text, pixels);
    // $DUMP_HUD_DIR and $DUMP_HUD_TICS ("N,M,..." or "all"; T6.11): the HUD of those tics as
    // DIR/hudN.ppm, 320x48 in the palette of the tic: the message line (rows 0-15,
    // black where nothing is drawn) over the status bar (rows 168-199).
    char *dir = getenv("DUMP_HUD_DIR"), *tics = getenv("DUMP_HUD_TICS");
    if (dir && tics)
    {
        char want[16];
        snprintf(want, sizeof want, "%d", leveltime);
        int hit = !strcmp(tics, "all");
        for (char *t = strtok(strdup(tics), ","); t && !hit; t = strtok(NULL, ","))
            hit = !strcmp(t, want);
        if (hit)
        {
            char path[1024];
            snprintf(path, sizeof path, "%s/hud%d.ppm", dir, leveltime);
            FILE *f = fopen(path, "wb");
            if (!f)
            {
                perror(path);
                exit(1);
            }
            byte *pal = (byte *)W_CacheLumpName("PLAYPAL", PU_CACHE) + 768 * st_palette;
            static const byte black[3] = { 0, 0, 0 };
            fprintf(f, "P6\n%d %d\n255\n", SCREENWIDTH, 16 + ST_HEIGHT);
            for (int i = 0; i < SCREENWIDTH * 16; i++)
                fwrite(hu[0][i] == hu[1][i] ? pal + 3 * hu[0][i] : black, 1, 3, f);
            for (int i = ST_Y * SCREENWIDTH; i < SCREENWIDTH * SCREENHEIGHT; i++)
                fwrite(pal + 3 * I_VideoBuffer[i], 1, 3, f);
            fclose(f);
        }
    }
    // The exit (T5.9): G_ExitLevel/G_SecretExitLevel this tic.
    extern boolean secretexit;
    extern gameaction_t gameaction;
    fprintf(ticfile, "%d\n", gameaction == ga_completed ? secretexit ? 2 : 1 : 0);
    fflush(ticfile);
}

// The intermission (T7.4): with $DUMP_WI set (and $DUMP_TICS), called by the
// patched g_game.c after WI_Ticker in every G_Ticker of the intermission;
// appends a line per tic to the file $DUMP_WI (its first line, at the
// intermission's first tic: "presses $DUMP_WI_PRESSES" (the route tool's
// schedule, echoed), then "wminfo EPSD DIDSECRET LAST NEXT MAXKILLS MAXITEMS
// MAXSECRET PARTIME SKILLS SITEMS SSECRET STIME" as WI_Start left them): BCNT
// STATE HASH RNDINDEX SOUNDS, STATE wi_stuff.c's
// state:sp_state:cnt_kills:cnt_items:cnt_secret:cnt_time:cnt_par:cnt_pause:cnt:acceleratestage:snl_pointeron:ANIMS
// (ANIMS the animations' ctr joined by dots, - for none; ref.patch's
// WI_dumpState, which writes BCNT too), HASH the 32-bit FNV-1a of WI_Drawer's 320x200 screen drawn
// now (twice, over two clear colours: 256 where nothing is drawn; word-wise,
// 8 hex digits), RNDINDEX M_Random's index, SOUNDS the tic's S_StartSound calls
// as for the route dump. With $DUMP_WI_DIR and $DUMP_WI_TICS ("N,M,..." or
// "all", intermission tics: bcnt), the screen of those tics as DIR/wiN.ppm
// (320x200, palette 0, as D_Display sets it off the level). It exits once the
// intermission ends (G_WorldDone: ga_worlddone).
void dump_witic(void)
{
    static FILE *wifile;
    char *path = getenv("DUMP_WI");
    if (!path || !*path)
        return;
    extern int rndindex;
    extern wbstartstruct_t wminfo;
    extern gameaction_t gameaction;
    extern void WI_dumpState(char *, int);
    if (!wifile)
    {
        if (!(wifile = fopen(path, "w")))
        {
            perror(path);
            exit(1);
        }
        char *presses = getenv("DUMP_WI_PRESSES");
        wbplayerstruct_t *p = &wminfo.plyr[consoleplayer];
        fprintf(wifile, "presses %s\nwminfo %d %d %d %d %d %d %d %d %d %d %d %d\n", presses && *presses ? presses : "-",
                wminfo.epsd, wminfo.didsecret, wminfo.last, wminfo.next, wminfo.maxkills, wminfo.maxitems, wminfo.maxsecret,
                wminfo.partime, p->skills, p->sitems, p->ssecret, p->stime);
    }
    static byte wi[2][SCREENWIDTH * SCREENHEIGHT];
    for (int k = 0; k < 2; k++)
    {
        memset(I_VideoBuffer, k ? 4 : 0, sizeof wi[k]);
        WI_Drawer();
        memcpy(wi[k], I_VideoBuffer, sizeof wi[k]);
    }
    uint32_t hash = 2166136261u;
    for (int i = 0; i < SCREENWIDTH * SCREENHEIGHT; i++)
        hash = (hash ^ (wi[0][i] == wi[1][i] ? wi[0][i] : 256u)) * 16777619u;
    char state[512];
    WI_dumpState(state, sizeof state);
    fprintf(wifile, "%s %08x %d %s\n", state, hash, rndindex, soundslen ? sounds : "-");
    soundslen = 0;
    char *dir = getenv("DUMP_WI_DIR"), *tics = getenv("DUMP_WI_TICS");
    if (dir && tics)
    {
        char want[16];
        snprintf(want, sizeof want, "%d", atoi(state)); // bcnt
        int hit = !strcmp(tics, "all");
        for (char *t = strtok(strdup(tics), ","); t && !hit; t = strtok(NULL, ","))
            hit = !strcmp(t, want);
        if (hit)
        {
            char out[1024];
            snprintf(out, sizeof out, "%s/wi%s.ppm", dir, want);
            FILE *f = fopen(out, "wb");
            if (!f)
            {
                perror(out);
                exit(1);
            }
            byte *pal = (byte *)W_CacheLumpName("PLAYPAL", PU_CACHE);
            static const byte none[3] = { 0, 255, 255 };
            fprintf(f, "P6\n%d %d\n255\n", SCREENWIDTH, SCREENHEIGHT);
            for (int i = 0; i < SCREENWIDTH * SCREENHEIGHT; i++)
                fwrite(wi[0][i] == wi[1][i] ? pal + 3 * wi[0][i] : none, 1, 3, f);
            fclose(f);
        }
    }
    fflush(wifile);
    if (gameaction == ga_worlddone)
    {
        fclose(wifile);
        exit(0);
    }
}

// A lump of the map (ML_SECTORS, ...).
static byte *map_lump(int lump)
{
    char name[9];
    if (gamemode == commercial)
        snprintf(name, sizeof name, "MAP%02d", gamemap);
    else
        snprintf(name, sizeof name, "E%dM%d", gameepisode, gamemap);
    return W_CacheLumpNum(W_GetNumForName(name) + lump, PU_CACHE);
}

// The map's SECTORS lump.
static byte *map_sectors(void)
{
    return map_lump(ML_SECTORS);
}

// The light levels of the map's SECTORS lump (undoes flickering, glowing, ...).
static void reset_lights(void)
{
    byte *data = map_sectors();
    for (int i = 0; i < numsectors; i++)
        sectors[i].lightlevel = (short)(data[26 * i + 20] | data[26 * i + 21] << 8);
}

static void place(int i)
{
    if (!dump_tic_target)
        reset_lights();
    mobj_t *mo = players[0].mo;
    P_TeleportMove(mo, views[i][0] << FRACBITS, views[i][1] << FRACBITS);
    mo->z = mo->floorz;
    mo->momx = mo->momy = mo->momz = 0;
    mo->angle = (angle_t)((long long)views[i][2] * 0x100000000LL / 360);
    players[0].viewheight = VIEWHEIGHT;
    players[0].deltaviewheight = 0;
    players[0].psprites[0].state = NULL;
    players[0].psprites[1].state = NULL;
    players[0].message = NULL;
    if (dump_tic_target) // rendered now, not after the player thinks (T5.7)
        players[0].viewz = mo->z + VIEWHEIGHT;
}

static void render_view(void);

// $DUMP_TIC (T5.7): every view at the end of tic N, then exit.
static void capture_tic(void)
{
    extern boolean setsizeneeded;
    if (setsizeneeded)
        R_ExecuteSetViewSize();
    for (cur = 0; cur < nviews; cur++)
    {
        place(cur);
        render_view();
    }
    exit(0);
}

void DG_DrawFrame(void)
{
    if (gamestate != GS_LEVEL || !players[0].mo || nviews == 0)
        return;
    if (dump_tic_target) // T5.7: the views render at the end of tic N (capture_tic)
    {
        if (cur < 0)
            R_SetViewSize(11, 0);
        cur = 0;
        return;
    }
    if (cur < 0) // full-screen view, and let the screen wipe finish
    {
        R_SetViewSize(11, 0);
        cur = 0;
        place(0);
        wait = 80;
        return;
    }
    if (--wait > 0)
    {
        place(cur);
        return;
    }
    reset_lights();
    render_view();
    if (++cur >= nviews)
        exit(0);
    place(cur);
    wait = 4;
}

// Renders view cur to $OUTDIR/vN.ppm and its eye height to vN.z.
static void render_view(void)
{
    // Render twice over two clear colours: pixels that differ were not drawn.
    dump_noceil = 1;
    memset(I_VideoBuffer, 0, sizeof first);
    R_RenderPlayerView(&players[0]);
    memcpy(first, I_VideoBuffer, sizeof first);
    memset(I_VideoBuffer, 4, sizeof first);
    R_RenderPlayerView(&players[0]);
    dump_noceil = 0;

    char path[1024];
    snprintf(path, sizeof path, "%s/v%d.ppm", getenv("OUTDIR") ? getenv("OUTDIR") : ".", cur);
    byte *pal = W_CacheLumpName("PLAYPAL", PU_CACHE);
    static const byte none[3] = {0, 255, 255};
    FILE *f = fopen(path, "wb");
    if (!f)
    {
        perror(path);
        exit(1);
    }
    fprintf(f, "P6\n%d %d\n255\n", SCREENWIDTH, SCREENHEIGHT);
    for (int p = 0; p < SCREENWIDTH * SCREENHEIGHT; p++)
        fwrite(first[p] != I_VideoBuffer[p] ? none : pal + 3 * first[p], 1, 3, f);
    fclose(f);
    // The eye height vanilla used: the player stands on the highest floor within its radius (floorz).
    snprintf(path, sizeof path, "%s/v%d.z", getenv("OUTDIR") ? getenv("OUTDIR") : ".", cur);
    if ((f = fopen(path, "w")))
    {
        fprintf(f, "%d\n", players[0].viewz >> FRACBITS);
        fclose(f);
    }
}

void DG_SleepMs(uint32_t ms) { fake_ms += ms; }
uint32_t DG_GetTicksMs(void) { return fake_ms += 5; }
int DG_GetKey(int *pressed, unsigned char *key) { return 0; }
void DG_SetWindowTitle(const char *title) {}

int main(int argc, char **argv)
{
    doomgeneric_Create(argc, argv);
    for (;;)
        doomgeneric_Tick();
}
