// IsoDoom vanilla reference renderer (T2.9; dev tool, never shipped): a
// doomgeneric platform layer with no window. After `-warp`, it puts player 1
// at each view of $VIEWS ("X Y ANGLE;X Y ANGLE;...", map units and degrees),
// standing still at eye level, with the sector lights as the map lump has
// them (no light specials) and, with $DUMP_STATIC set, no animated textures
// or scrolling walls, and writes $OUTDIR/vN.ppm: vanilla's full-screen
// 320x200 view in palette 0, with masked middles (T3.1) but without sprites,
// ceilings or sky; pixels that draw nothing (ceilings, sky) are cyan (0, 255, 255), a
// colour in no PLAYPAL; and $OUTDIR/vN.z, the eye height (viewz, map units:
// the player stands on the highest floor within its radius, so near a step
// that is not the floor at the point). Built by build.sh against doomgeneric
// (GPL-2.0).
#include "doomgeneric.h"
#include "doomstat.h"
#include "d_player.h"
#include "p_local.h"
#include "r_main.h"
#include "r_state.h"
#include "w_wad.h"
#include "z_zone.h"
#include "i_video.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

#define MAX_VIEWS 4096

extern byte *I_VideoBuffer;
static int views[MAX_VIEWS][3], nviews, cur = -1, wait;
static uint32_t fake_ms;
int dump_noceil; // read by the patched r_main.c / r_plane.c
static byte first[SCREENWIDTH * SCREENHEIGHT];

void DG_Init(void)
{
    char *v = getenv("VIEWS");
    for (char *tok = strtok(strdup(v ? v : ""), ";"); tok && nviews < MAX_VIEWS; tok = strtok(NULL, ";"))
        if (sscanf(tok, "%d %d %d", &views[nviews][0], &views[nviews][1], &views[nviews][2]) == 3)
            nviews++;
}

// The light levels of the map's SECTORS lump (undoes flickering, glowing, ...).
static void reset_lights(void)
{
    char name[9];
    if (gamemode == commercial)
        snprintf(name, sizeof name, "MAP%02d", gamemap);
    else
        snprintf(name, sizeof name, "E%dM%d", gameepisode, gamemap);
    byte *data = W_CacheLumpNum(W_GetNumForName(name) + ML_SECTORS, PU_CACHE);
    for (int i = 0; i < numsectors; i++)
        sectors[i].lightlevel = (short)(data[26 * i + 20] | data[26 * i + 21] << 8);
}

static void place(int i)
{
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
}

void DG_DrawFrame(void)
{
    if (gamestate != GS_LEVEL || !players[0].mo || nviews == 0)
        return;
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
    // Render twice over two clear colours: pixels that differ were not drawn.
    reset_lights();
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
    if (++cur >= nviews)
        exit(0);
    place(cur);
    wait = 4;
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
