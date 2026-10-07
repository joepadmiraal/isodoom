/* OPL reference harness (T7.8a; a dev tool, never shipped): plays a
 * register-write log (tools/OplRef/genlogs.py describes the format) through
 * Nuked-OPL3's opl3.c and writes the stereo samples as raw signed 16-bit
 * little-endian pairs (left, right). Built by build.sh against the pinned
 * Nuked-OPL3 (LGPL-2.1-or-later) and Chocolate Doom's copy of it.
 *
 * Usage: opl_ref LOG OUT.raw
 *
 * Copyright (C) 2026 the IsoDoom authors. GPL-2.0-or-later.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "opl3.h"

typedef struct { unsigned long sample; unsigned reg, val; } write_t;

static opl3_chip chip;

int main(int argc, char **argv)
{
    FILE *in, *out;
    char line[512];
    unsigned long rate = 0, length = 0, nwrites = 0, cap = 1024, i, w = 0;
    int buffered = 0;
    write_t *writes;

    if (argc != 3)
    {
        fprintf(stderr, "usage: %s LOG OUT.raw\n", argv[0]);
        return 2;
    }
    in = fopen(argv[1], "r");
    if (!in)
    {
        perror(argv[1]);
        return 1;
    }
    writes = malloc(cap * sizeof(*writes));
    while (fgets(line, sizeof(line), in))
    {
        unsigned long s;
        unsigned reg, val;
        if (line[0] == '#' || line[0] == '\n')
            continue;
        if (sscanf(line, "rate %lu", &rate) == 1)
            continue;
        if (sscanf(line, "length %lu", &length) == 1)
            continue;
        if (strncmp(line, "buffered", 8) == 0)
        {
            buffered = 1;
            continue;
        }
        if (sscanf(line, "%lu %x %x", &s, &reg, &val) != 3)
        {
            fprintf(stderr, "%s: bad line: %s", argv[1], line);
            return 1;
        }
        if (nwrites == cap)
        {
            cap *= 2;
            writes = realloc(writes, cap * sizeof(*writes));
        }
        writes[nwrites].sample = s;
        writes[nwrites].reg = reg;
        writes[nwrites].val = val;
        nwrites++;
    }
    fclose(in);

    out = fopen(argv[2], "wb");
    if (!out)
    {
        perror(argv[2]);
        return 1;
    }
    OPL3_Reset(&chip, rate ? (uint32_t)rate : 49716);
    for (i = 0; i < length; i++)
    {
        int16_t buf[2];
        unsigned char b[4];
        for (; w < nwrites && writes[w].sample <= i; w++)
        {
            if (buffered)
                OPL3_WriteRegBuffered(&chip, (uint16_t)writes[w].reg, (uint8_t)writes[w].val);
            else
                OPL3_WriteReg(&chip, (uint16_t)writes[w].reg, (uint8_t)writes[w].val);
        }
        if (rate)
            OPL3_GenerateStream(&chip, buf, 1);
        else
            OPL3_Generate(&chip, buf);
        b[0] = (unsigned char)(buf[0] & 0xff);
        b[1] = (unsigned char)((uint16_t)buf[0] >> 8);
        b[2] = (unsigned char)(buf[1] & 0xff);
        b[3] = (unsigned char)((uint16_t)buf[1] >> 8);
        fwrite(b, 1, 4, out);
    }
    fclose(out);
    free(writes);
    return 0;
}
