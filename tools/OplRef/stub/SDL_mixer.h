/* Stub SDL_mixer for the OPL music reference harness (T7.8d): music_ref.c
 * implements these and keeps the post-mix effect opl_sdl.c registers. */
#ifndef STUB_SDL_MIXER_H
#define STUB_SDL_MIXER_H
#include "SDL.h"
#define MIX_CHANNEL_POST (-2)
typedef void (*Mix_EffectFunc_t)(int chan, void *stream, int len, void *udata);
typedef void (*Mix_EffectDone_t)(int chan, void *udata);
int Mix_QuerySpec(int *frequency, Uint16 *format, int *channels);
int Mix_OpenAudioDevice(int frequency, Uint16 format, int channels, int chunksize, const char *device, int allowed_changes);
void Mix_CloseAudio(void);
void Mix_HookMusic(void (*mix_func)(void *, Uint8 *, int), void *arg);
int Mix_RegisterEffect(int chan, Mix_EffectFunc_t f, Mix_EffectDone_t d, void *arg);
const char *Mix_GetError(void);
#endif
