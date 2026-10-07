/* Stub for the OPL music reference harness (T7.8d): music_ref.c implements these. */
#include <stdio.h>
#include <stddef.h>
#include "doomtype.h"
char *M_TempFile(const char *s);
boolean M_WriteFile(const char *name, const void *source, int length);
int M_remove(const char *path);
FILE *M_fopen(const char *filename, const char *mode);
int M_snprintf(char *buf, size_t buf_len, const char *s, ...);
boolean M_StringConcat(char *dest, const char *src, size_t dest_size);
