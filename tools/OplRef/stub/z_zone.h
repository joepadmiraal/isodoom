/* Stub for the OPL music reference harness (T7.8d): the zone is malloc. */
#include <stdlib.h>
#define PU_STATIC 1
#define Z_Malloc(size, tag, user) malloc(size)
#define Z_Free(p) free(p)
