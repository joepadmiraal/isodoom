/* Stub SDL for the OPL music reference harness (T7.8d; tools/OplRef): no
 * threads, no audio device. music_ref.c drives opl_sdl.c's mixing callback
 * itself; SDL_CondWait (OPL_Delay's wait during the chip detection) runs
 * that callback a sample at a time, as the audio thread would. */
#ifndef STUB_SDL_H
#define STUB_SDL_H
#include <stdint.h>
#include <string.h>
typedef uint8_t Uint8;
typedef uint16_t Uint16;
typedef uint32_t Uint32;
typedef struct SDL_mutex SDL_mutex;
typedef struct SDL_cond SDL_cond;
#define SDL_INIT_AUDIO 0x10
#define AUDIO_S16SYS 0x8010
#define SDL_MIX_MAXVOLUME 128
#define SDL_AUDIO_ALLOW_FREQUENCY_CHANGE 1
static inline SDL_mutex *SDL_CreateMutex(void) { return (SDL_mutex *) 1; }
static inline void SDL_DestroyMutex(SDL_mutex *m) { (void) m; }
static inline int SDL_LockMutex(SDL_mutex *m) { (void) m; return 0; }
static inline int SDL_UnlockMutex(SDL_mutex *m) { (void) m; return 0; }
static inline SDL_cond *SDL_CreateCond(void) { return (SDL_cond *) 1; }
static inline void SDL_DestroyCond(SDL_cond *c) { (void) c; }
static inline int SDL_CondSignal(SDL_cond *c) { (void) c; return 0; }
void harness_condwait(void);
static inline int SDL_CondWait(SDL_cond *c, SDL_mutex *m) { (void) c; (void) m; harness_condwait(); return 0; }
static inline int SDL_Init(Uint32 f) { (void) f; return 0; }
static inline void SDL_QuitSubSystem(Uint32 f) { (void) f; }
static inline void SDL_PauseAudio(int p) { (void) p; }
static inline Uint16 SDL_SwapBE16(Uint16 x) { return (Uint16) ((x >> 8) | (x << 8)); }
static inline Uint32 SDL_SwapBE32(Uint32 x) { return __builtin_bswap32(x); }
/* SDL_MixAudioFormat for AUDIO_S16SYS at full volume: adds with clipping. */
static inline void SDL_MixAudioFormat(Uint8 *dst, const Uint8 *src, Uint16 format, Uint32 len, int volume)
{
    Uint32 i;
    (void) format; (void) volume;
    for (i = 0; i + 1 < len; i += 2)
    {
        int16_t a, b;
        int s;
        memcpy(&a, dst + i, 2);
        memcpy(&b, src + i, 2);
        s = a + b;
        if (s > 32767) s = 32767;
        if (s < -32768) s = -32768;
        a = (int16_t) s;
        memcpy(dst + i, &a, 2);
    }
}
#endif
