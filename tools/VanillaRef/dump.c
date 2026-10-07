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
// angle, flags and health; then (T5.9) exit: 0, or 1 (2) when the
// tic left the level by its exit (secret exit): gameaction is ga_completed.
// With $DUMP_START
// ("X Y ANGLE", map units and degrees; T5.6), player 1 starts there instead
// of at its map start: before the first tic it is moved with P_TeleportMove
// onto the floor, facing ANGLE (no fog, nothing else changed). With
// $DUMP_EVENTS ("TIC damage X Y AMOUNT;TIC alert;...", TIC 0-based, in
// order; T6.4), at the start of tic TIC (leveltime TIC), before the players
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
    for (; nextevent < nevents && events[nextevent].tic <= leveltime; nextevent++)
    {
        if (events[nextevent].tic < leveltime)
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
                    leveltime + 1, events[nextevent].x, events[nextevent].y);
            exit(1);
        }
        P_DamageMobj(target, mo, mo, events[nextevent].amount);
    }
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
    // The exit (T5.9): G_ExitLevel/G_SecretExitLevel this tic.
    extern boolean secretexit;
    extern gameaction_t gameaction;
    fprintf(ticfile, "%d\n", gameaction == ga_completed ? secretexit ? 2 : 1 : 0);
    fflush(ticfile);
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
