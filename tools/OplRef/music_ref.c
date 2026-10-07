/* OPL music reference harness (T7.8d; a dev tool, never shipped): plays
 * songs of a WAD through Chocolate Doom's own i_oplmusic.c, mus2mid.c,
 * midifile.c, memio.c and opl/ (opl.c, opl_sdl.c, opl_queue.c, opl3.c), with
 * SDL and SDL_mixer stubbed (stub/): this program calls opl_sdl.c's mixing
 * callback (OPL_Mix_Callback) block by block as SDL_mixer's audio thread
 * would, so the music's timer runs on the sample clock exactly as in
 * Chocolate Doom. Every write to the chip (OPL3_WriteRegBuffered) is logged
 * with the output sample before which it happens, in opl_ref.c's log format
 * (rate, buffered, length, "SAMPLE REG VALUE"), and the samples
 * (OPL3_GenerateStream, mixed as SDL_MixAudioFormat would into silence) are
 * written as raw signed 16-bit little-endian stereo pairs (OUT.raw "-": not
 * written). Built by build.sh
 * (opl_sdl.c with OPL3_WriteRegBuffered and OPL3_GenerateStream renamed to
 * this file's ref_ functions).
 *
 * Usage: music_ref [OPTIONS] WAD OUT.log OUT.raw
 *   -rate N       snd_samplerate, the mixing rate (default 44100)
 *   -block N      samples per mixing callback (default 512)
 *   -length N     samples to render (default 10 s)
 *   -opl2         snd_dmxoption without "-opl3" (OPL2 mode; default OPL3)
 *   -reverse      snd_dmxoption's "-reverse" (opl_stereo_correct)
 *   -ver V        I_SetOPLDriverVer: 1.9 (default), doom1-1.666, doom2-1.666
 *   -nochip       the chip is not clocked (the log only; OUT.raw gets zeros)
 *   -volume N     I_SetMusicVolume after the init (default 64: Chocolate
 *                 Doom's musicVolume 8 * 8)
 *   -at SAMPLE CMD  before output sample SAMPLE (blocks are split there):
 *                 play:LUMP:loop or play:LUMP:once (the song playing stopped
 *                 and unregistered first, then LUMP registered and played; a
 *                 LUMP with a '/' or '.' is a file), stop (stop and
 *                 unregister), pause, resume, volume:N
 * The GENMIDI lump comes from WAD.
 *
 * Copyright (C) 2026 the IsoDoom authors. GPL-2.0-or-later.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
#include <stdarg.h>
#include <unistd.h>

#include "doomtype.h"
#include "i_sound.h"
#include "m_misc.h"
#include "w_wad.h"
#include "SDL_mixer.h"
#include "opl3.h"

/* opl_sdl.c's calls, renamed by build.sh (-D) to these. */
void ref_WriteRegBuffered(opl3_chip *chip, uint16_t reg, uint8_t v);
void ref_GenerateStream(opl3_chip *chip, int16_t *sndptr, uint32_t numsamples);

int snd_samplerate = 44100;
extern char *snd_dmxoption;

typedef struct { unsigned long sample; unsigned reg, val; } write_t;

static write_t *writes;
static unsigned long nwrites, capwrites;
static unsigned long samples_done;
static int detecting = 1;
static int nochip;

static unsigned char *wad;
static long wad_len;

static Mix_EffectFunc_t effect;
static int mix_open, mix_freq;

/* --- the chip calls -------------------------------------------------- */

void ref_WriteRegBuffered(opl3_chip *chip, uint16_t reg, uint8_t v)
{
    if (nwrites == capwrites)
    {
        capwrites = capwrites ? capwrites * 2 : 4096;
        writes = realloc(writes, capwrites * sizeof(*writes));
    }
    writes[nwrites].sample = samples_done;
    writes[nwrites].reg = reg;
    writes[nwrites].val = v;
    nwrites++;
    OPL3_WriteRegBuffered(chip, reg, v);
}

void ref_GenerateStream(opl3_chip *chip, int16_t *sndptr, uint32_t numsamples)
{
    /* The chip detection's wait (OPL_Delay) only lets time pass: the chip
     * is not clocked before the music starts, as OplMusic's isn't. */
    if (detecting || nochip)
    {
        if (!detecting)
            samples_done += numsamples;
        memset(sndptr, 0, numsamples * 4);
        return;
    }
    OPL3_GenerateStream(chip, sndptr, numsamples);
    samples_done += numsamples;
}

/* --- SDL and SDL_mixer stubs ----------------------------------------- */

void harness_condwait(void)
{
    int16_t buf[2] = { 0, 0 };
    effect(MIX_CHANNEL_POST, buf, 4, NULL);
}

int Mix_QuerySpec(int *frequency, Uint16 *format, int *channels)
{
    if (!mix_open)
        return 0;
    *frequency = mix_freq;
    *format = AUDIO_S16SYS;
    *channels = 2;
    return 1;
}

int Mix_OpenAudioDevice(int frequency, Uint16 format, int channels, int chunksize, const char *device, int allowed_changes)
{
    (void) format; (void) channels; (void) chunksize; (void) device; (void) allowed_changes;
    mix_open = 1;
    mix_freq = frequency;
    return 0;
}

void Mix_CloseAudio(void) { mix_open = 0; }
void Mix_HookMusic(void (*mix_func)(void *, Uint8 *, int), void *arg) { (void) mix_func; (void) arg; }
const char *Mix_GetError(void) { return "stub"; }

int Mix_RegisterEffect(int chan, Mix_EffectFunc_t f, Mix_EffectDone_t d, void *arg)
{
    (void) chan; (void) d; (void) arg;
    effect = f;
    return 1;
}

/* --- m_misc.c and w_wad.c stand-ins ---------------------------------- */

char *M_TempFile(const char *s)
{
    const char *dir = getenv("TMPDIR");
    char *result;
    if (dir == NULL)
        dir = "/tmp";
    result = malloc(strlen(dir) + strlen(s) + 32);
    sprintf(result, "%s/music_ref.%d.%s", dir, (int) getpid(), s);
    return result;
}

boolean M_WriteFile(const char *name, const void *source, int length)
{
    FILE *f = fopen(name, "wb");
    size_t n;
    if (f == NULL)
        return false;
    n = fwrite(source, 1, length, f);
    fclose(f);
    return n == (size_t) length;
}

int M_remove(const char *path) { return remove(path); }
FILE *M_fopen(const char *filename, const char *mode) { return fopen(filename, mode); }

int M_snprintf(char *buf, size_t buf_len, const char *s, ...)
{
    va_list args;
    int r;
    va_start(args, s);
    r = vsnprintf(buf, buf_len, s, args);
    va_end(args);
    return r;
}

boolean M_StringConcat(char *dest, const char *src, size_t dest_size)
{
    size_t n = strlen(dest);
    if (n >= dest_size)
        return false;
    snprintf(dest + n, dest_size - n, "%s", src);
    return true;
}

static long rd32(const unsigned char *p) { return p[0] | (p[1] << 8) | (p[2] << 16) | ((long) p[3] << 24); }

/* The last lump of that name (as W_CheckNumForName), or NULL. */
static unsigned char *FindLump(const char *name, int *len)
{
    long n = rd32(wad + 4), dir = rd32(wad + 8), i;
    unsigned char *found = NULL;
    for (i = 0; i < n; i++)
    {
        const unsigned char *e = wad + dir + 16 * i;
        char lname[9];
        int j;
        for (j = 0; j < 8; j++)
            lname[j] = (char) toupper(e[8 + j]);
        lname[8] = 0;
        if (strncmp(lname, name, 8) == 0)
        {
            found = wad + rd32(e);
            *len = (int) rd32(e + 4);
        }
    }
    return found;
}

void *W_CacheLumpName(const char *name, int tag)
{
    char upper[9];
    int i, len;
    void *p;
    (void) tag;
    for (i = 0; i < 8 && name[i]; i++)
        upper[i] = (char) toupper((unsigned char) name[i]);
    upper[i] = 0;
    p = FindLump(upper, &len);
    if (p == NULL)
    {
        fprintf(stderr, "W_CacheLumpName: %s not found\n", name);
        exit(1);
    }
    return p;
}

void W_ReleaseLumpName(const char *name) { (void) name; }

static unsigned char *ReadFile(const char *path, long *len)
{
    FILE *f = fopen(path, "rb");
    unsigned char *buf;
    if (f == NULL)
    {
        perror(path);
        exit(1);
    }
    fseek(f, 0, SEEK_END);
    *len = ftell(f);
    fseek(f, 0, SEEK_SET);
    buf = malloc(*len + 1);
    if (fread(buf, 1, *len, f) != (size_t) *len)
    {
        perror(path);
        exit(1);
    }
    fclose(f);
    return buf;
}

/* --- the run ---------------------------------------------------------- */

typedef struct { unsigned long sample; char *cmd; } event_t;

static void *handle;

static void RunCommand(char *cmd)
{
    if (strncmp(cmd, "play:", 5) == 0)
    {
        char name[256];
        char *colon;
        int looping = 1, len = 0;
        void *data;
        snprintf(name, sizeof(name), "%s", cmd + 5);
        colon = strrchr(name, ':');
        if (colon != NULL)
        {
            looping = strcmp(colon + 1, "once") != 0;
            *colon = 0;
        }
        if (handle != NULL)
        {
            music_opl_module.StopSong();
            music_opl_module.UnRegisterSong(handle);
            handle = NULL;
        }
        if (strchr(name, '/') != NULL || strchr(name, '.') != NULL)
        {
            long l;
            data = ReadFile(name, &l);
            len = (int) l;
        }
        else
        {
            data = FindLump(name, &len);
            if (data == NULL)
            {
                fprintf(stderr, "%s: no such lump\n", name);
                exit(1);
            }
        }
        handle = music_opl_module.RegisterSong(data, len);
        if (handle == NULL)
            fprintf(stderr, "%s: I_OPL_RegisterSong refused it\n", name);
        else
            music_opl_module.PlaySong(handle, looping);
    }
    else if (strcmp(cmd, "stop") == 0)
    {
        if (handle != NULL)
        {
            music_opl_module.StopSong();
            music_opl_module.UnRegisterSong(handle);
            handle = NULL;
        }
    }
    else if (strcmp(cmd, "pause") == 0)
        music_opl_module.PauseMusic();
    else if (strcmp(cmd, "resume") == 0)
        music_opl_module.ResumeMusic();
    else if (strncmp(cmd, "volume:", 7) == 0)
        music_opl_module.SetMusicVolume(atoi(cmd + 7));
    else
    {
        fprintf(stderr, "unknown command %s\n", cmd);
        exit(2);
    }
}

int main(int argc, char **argv)
{
    unsigned long length = 0, block = 512, i;
    int volume = 64, opl3 = 1, reverse = 0, a;
    event_t events[64];
    int nevents = 0, e = 0;
    char dmxoption[32];
    const char *wadpath, *logpath, *rawpath;
    FILE *out, *log;
    int16_t *buf;

    for (a = 1; a < argc && argv[a][0] == '-'; a++)
    {
        if (!strcmp(argv[a], "-rate") && a + 1 < argc)
            snd_samplerate = atoi(argv[++a]);
        else if (!strcmp(argv[a], "-block") && a + 1 < argc)
            block = strtoul(argv[++a], NULL, 10);
        else if (!strcmp(argv[a], "-length") && a + 1 < argc)
            length = strtoul(argv[++a], NULL, 10);
        else if (!strcmp(argv[a], "-opl2"))
            opl3 = 0;
        else if (!strcmp(argv[a], "-nochip"))
            nochip = 1;
        else if (!strcmp(argv[a], "-reverse"))
            reverse = 1;
        else if (!strcmp(argv[a], "-volume") && a + 1 < argc)
            volume = atoi(argv[++a]);
        else if (!strcmp(argv[a], "-ver") && a + 1 < argc)
        {
            a++;
            if (!strcmp(argv[a], "1.9"))
                I_SetOPLDriverVer(opl_doom_1_9);
            else if (!strcmp(argv[a], "doom1-1.666"))
                I_SetOPLDriverVer(opl_doom1_1_666);
            else if (!strcmp(argv[a], "doom2-1.666"))
                I_SetOPLDriverVer(opl_doom2_1_666);
            else
            {
                fprintf(stderr, "unknown -ver %s\n", argv[a]);
                return 2;
            }
        }
        else if (!strcmp(argv[a], "-at") && a + 2 < argc && nevents < 64)
        {
            events[nevents].sample = strtoul(argv[a + 1], NULL, 10);
            events[nevents].cmd = argv[a + 2];
            if (nevents > 0 && events[nevents].sample < events[nevents - 1].sample)
            {
                fprintf(stderr, "-at samples must not decrease\n");
                return 2;
            }
            nevents++;
            a += 2;
        }
        else
        {
            fprintf(stderr, "bad option %s\n", argv[a]);
            return 2;
        }
    }
    if (argc - a != 3 || block == 0)
    {
        fprintf(stderr, "usage: %s [OPTIONS] WAD OUT.log OUT.raw\n", argv[0]);
        return 2;
    }
    wadpath = argv[a];
    logpath = argv[a + 1];
    rawpath = argv[a + 2];
    if (length == 0)
        length = 10UL * snd_samplerate;
    wad = ReadFile(wadpath, &wad_len);

    snprintf(dmxoption, sizeof(dmxoption), "%s%s", opl3 ? "-opl3" : "", reverse ? " -reverse" : "");
    snd_dmxoption = dmxoption;
    unsetenv("DMXOPTION");
    unsetenv("OPL_DRIVER");

    /* I_InitMusic, then S_Init's S_SetMusicVolume. */
    if (!music_opl_module.Init())
    {
        fprintf(stderr, "I_OPL_InitMusic failed\n");
        return 1;
    }
    detecting = 0;
    music_opl_module.SetMusicVolume(volume);

    out = strcmp(rawpath, "-") == 0 ? NULL : fopen(rawpath, "wb");
    if (out == NULL && strcmp(rawpath, "-") != 0)
    {
        perror(rawpath);
        return 1;
    }
    buf = malloc(block * 4);
    while (samples_done < length || e < nevents)
    {
        unsigned long n = block;
        for (; e < nevents && events[e].sample <= samples_done; e++)
            RunCommand(events[e].cmd);
        if (samples_done >= length)
            break;
        if (e < nevents && events[e].sample - samples_done < n)
            n = events[e].sample - samples_done;
        if (length - samples_done < n)
            n = length - samples_done;
        memset(buf, 0, n * 4);
        effect(MIX_CHANNEL_POST, buf, (int) (n * 4), NULL);
        for (i = 0; i < 2 * n; i++)
        {
            unsigned char b[2];
            b[0] = (unsigned char) (buf[i] & 0xff);
            b[1] = (unsigned char) ((uint16_t) buf[i] >> 8);
            if (out != NULL)
                fwrite(b, 1, 2, out);
        }
    }
    if (out != NULL)
        fclose(out);

    log = fopen(logpath, "w");
    if (log == NULL)
    {
        perror(logpath);
        return 1;
    }
    fprintf(log, "# music_ref");
    for (a = 1; a < argc - 3; a++)
        fprintf(log, " %s", argv[a]);
    fprintf(log, "\n# %lu writes\nrate %d\nlength %lu\nbuffered\n", nwrites, mix_freq, length);
    for (i = 0; i < nwrites; i++)
        fprintf(log, "%lu %x %x\n", writes[i].sample, writes[i].reg, writes[i].val);
    fclose(log);
    return 0;
}
