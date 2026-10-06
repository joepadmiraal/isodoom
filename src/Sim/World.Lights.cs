namespace IsoDoom.Sim;

// p_lights.c: handle sector-based lighting special effects (T5.7). The
// thinkers (fire flicker, broken flashing, strobe, glow) are spawned by
// P_SpawnSpecials (World.Spec.cs) for the sector specials 1, 2, 3, 4, 8,
// 12, 13 and 17, and by the line specials through EV_StartLightStrobing.
// The light level is the sector's (sector_t.lightlevel, the map's Sector):
// the presentation draws it every frame, stepped as vanilla (T5.1).
public sealed partial class World
{
    //
    // FIRELIGHT FLICKER
    //

    /// <summary>p_lights.c <c>T_FireFlicker</c>.</summary>
    public void T_FireFlicker(fireflicker_t flick)
    {
        if (--flick.count != 0)
            return;

        int amount = (P_Random() & 3) * 16;

        if (flick.sector.lightlevel - amount < flick.minlight)
            flick.sector.lightlevel = (short)flick.minlight;
        else
            flick.sector.lightlevel = (short)(flick.maxlight - amount);

        flick.count = 4;
    }

    /// <summary>p_lights.c <c>P_SpawnFireFlicker</c>.</summary>
    public void P_SpawnFireFlicker(sector_t sector)
    {
        // Note that we are resetting sector attributes.
        // Nothing special about it during gameplay.
        sector.special = 0;

        var flick = new fireflicker_t();

        P_AddThinker(flick);

        flick.function = think_t.T_FireFlicker;
        flick.sector = sector;
        flick.maxlight = sector.lightlevel;
        flick.minlight = P_FindMinSurroundingLight(sector, sector.lightlevel) + 16;
        flick.count = 4;
    }

    //
    // BROKEN LIGHT FLASHING
    //

    /// <summary>p_lights.c <c>T_LightFlash</c>: Do flashing lights.</summary>
    public void T_LightFlash(lightflash_t flash)
    {
        if (--flash.count != 0)
            return;

        if (flash.sector.lightlevel == flash.maxlight)
        {
            flash.sector.lightlevel = (short)flash.minlight;
            flash.count = (P_Random() & flash.mintime) + 1;
        }
        else
        {
            flash.sector.lightlevel = (short)flash.maxlight;
            flash.count = (P_Random() & flash.maxtime) + 1;
        }
    }

    /// <summary>
    /// p_lights.c <c>P_SpawnLightFlash</c>: After the map has been loaded,
    /// scan each sector for specials that spawn thinkers.
    /// </summary>
    public void P_SpawnLightFlash(sector_t sector)
    {
        // nothing special about it during gameplay
        sector.special = 0;

        var flash = new lightflash_t();

        P_AddThinker(flash);

        flash.function = think_t.T_LightFlash;
        flash.sector = sector;
        flash.maxlight = sector.lightlevel;

        flash.minlight = P_FindMinSurroundingLight(sector, sector.lightlevel);
        flash.maxtime = 64;
        flash.mintime = 7;
        flash.count = (P_Random() & flash.maxtime) + 1;
    }

    //
    // STROBE LIGHT FLASHING
    //

    /// <summary>p_lights.c <c>T_StrobeFlash</c>.</summary>
    public void T_StrobeFlash(strobe_t flash)
    {
        if (--flash.count != 0)
            return;

        if (flash.sector.lightlevel == flash.minlight)
        {
            flash.sector.lightlevel = (short)flash.maxlight;
            flash.count = flash.brighttime;
        }
        else
        {
            flash.sector.lightlevel = (short)flash.minlight;
            flash.count = flash.darktime;
        }
    }

    /// <summary>
    /// p_lights.c <c>P_SpawnStrobeFlash</c>: After the map has been loaded,
    /// scan each sector for specials that spawn thinkers. As vanilla, the
    /// strobe does not set the sector's <c>specialdata</c>.
    /// </summary>
    public void P_SpawnStrobeFlash(sector_t sector, int fastOrSlow, int inSync)
    {
        var flash = new strobe_t();

        P_AddThinker(flash);

        flash.sector = sector;
        flash.darktime = fastOrSlow;
        flash.brighttime = LightFlash.STROBEBRIGHT;
        flash.function = think_t.T_StrobeFlash;
        flash.maxlight = sector.lightlevel;
        flash.minlight = P_FindMinSurroundingLight(sector, sector.lightlevel);

        if (flash.minlight == flash.maxlight)
            flash.minlight = 0;

        // nothing special about it during gameplay
        sector.special = 0;

        if (inSync == 0)
            flash.count = (P_Random() & 7) + 1;
        else
            flash.count = 1;
    }

    /// <summary>
    /// p_lights.c <c>EV_StartLightStrobing</c>: Start strobing lights
    /// (usually from a trigger). Sectors with a mover (<c>specialdata</c>) are
    /// skipped; a strobe sets none, so triggering it again adds another.
    /// </summary>
    public void EV_StartLightStrobing(line_t line)
    {
        int secnum = -1;
        while ((secnum = P_FindSectorFromLineTag(line, secnum)) >= 0)
        {
            sector_t sec = sectors[secnum];
            if (sec.specialdata != null)
                continue;

            P_SpawnStrobeFlash(sec, LightFlash.SLOWDARK, 0);
        }
    }

    /// <summary>
    /// p_lights.c <c>EV_TurnTagLightsOff</c>: TURN LINE'S TAG LIGHTS OFF, to
    /// the darkest neighbour's light (or their own when darker).
    /// </summary>
    public void EV_TurnTagLightsOff(line_t line)
    {
        for (int j = 0; j < sectors.Length; j++)
        {
            sector_t sector = sectors[j];
            if (sector.tag == line.tag)
            {
                int min = sector.lightlevel;
                for (int i = 0; i < sector.linecount; i++)
                {
                    line_t templine = sector.lines[i];
                    sector_t? tsec = getNextSector(templine, sector);
                    if (tsec == null)
                        continue;
                    if (tsec.lightlevel < min)
                        min = tsec.lightlevel;
                }
                sector.lightlevel = (short)min;
            }
        }
    }

    /// <summary>
    /// p_lights.c <c>EV_LightTurnOn</c>: TURN LINE'S TAG LIGHTS ON, to
    /// <paramref name="bright"/>, or with 0 to the brightest neighbour's light.
    /// As vanilla, once a sector found a brightness it is kept for the next
    /// tagged sectors (<c>bright</c> is not reset).
    /// </summary>
    public void EV_LightTurnOn(line_t line, int bright)
    {
        for (int i = 0; i < sectors.Length; i++)
        {
            sector_t sector = sectors[i];
            if (sector.tag == line.tag)
            {
                // bright = 0 means to search
                // for highest light level
                // surrounding sector
                if (bright == 0)
                {
                    for (int j = 0; j < sector.linecount; j++)
                    {
                        line_t templine = sector.lines[j];
                        sector_t? temp = getNextSector(templine, sector);

                        if (temp == null)
                            continue;

                        if (temp.lightlevel > bright)
                            bright = temp.lightlevel;
                    }
                }
                sector.lightlevel = (short)bright;
            }
        }
    }

    //
    // Spawn glowing light
    //

    /// <summary>p_lights.c <c>T_Glow</c>: <see cref="LightFlash.GLOWSPEED"/> a tic between the darkest neighbour's light and the sector's own.</summary>
    public void T_Glow(glow_t g)
    {
        switch (g.direction)
        {
            case -1:
                // DOWN
                g.sector.lightlevel -= LightFlash.GLOWSPEED;
                if (g.sector.lightlevel <= g.minlight)
                {
                    g.sector.lightlevel += LightFlash.GLOWSPEED;
                    g.direction = 1;
                }
                break;

            case 1:
                // UP
                g.sector.lightlevel += LightFlash.GLOWSPEED;
                if (g.sector.lightlevel >= g.maxlight)
                {
                    g.sector.lightlevel -= LightFlash.GLOWSPEED;
                    g.direction = -1;
                }
                break;
        }
    }

    /// <summary>p_lights.c <c>P_SpawnGlowingLight</c>.</summary>
    public void P_SpawnGlowingLight(sector_t sector)
    {
        var g = new glow_t();

        P_AddThinker(g);

        g.sector = sector;
        g.minlight = P_FindMinSurroundingLight(sector, sector.lightlevel);
        g.maxlight = sector.lightlevel;
        g.function = think_t.T_Glow;
        g.direction = -1;

        sector.special = 0;
    }
}
