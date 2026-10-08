using System;
using System.Collections.Generic;
using System.IO;
using IsoDoom.Map;
using IsoDoom.Wad;

namespace IsoDoom.Sim;

/// <summary>
/// T7.6: a saved game that can't be loaded: the data is cut short or out of
/// range, or is for another map or WAD. The message says why, for the player.
/// </summary>
public sealed class SaveGameException : Exception
{
    public SaveGameException(string message, Exception? inner = null) : base(message, inner)
    {
    }

    /// <summary>The save does not fit this WAD's map (its sectors, lines, sides, blockmap or animations differ): another game's.</summary>
    public bool OtherGame { get; init; }
}

/// <summary>
/// T7.6: what a saved game starts with, g_game.c <c>G_DoLoadGame</c>'s
/// header: the game's settings (skill, game mode, <c>-nomonsters</c>…), its
/// tweaks (SPEC §6.3: the aim assist's cone can change during a game) and
/// the map, all a new world and its level need before
/// <see cref="World.P_UnArchiveGame"/> fills them in.
/// </summary>
public readonly record struct SaveGameStart(SpawnSettings settings, Tweaks tweaks, string map);

// p_saveg.c (T7.6): the play state in a custom binary format, not vanilla's
// (SPEC §8, §12 T7.6). Vanilla's archive keeps only part of the state (no
// P_Random index, the mobjs' links rebuilt in another order, no references
// between mobjs), so a loaded game can go another way; this one keeps every
// field the sim reads from one tic to the next, so a loaded game goes on
// exactly as the saved one would have (the same checksums).
//
// Layout (little-endian, BinaryWriter's): the start (SaveGameStart), the
// thinker table (each thinker's kind and whether it is in the thinker list:
// the list in order, then the thinkers only referenced, e.g. a removed mobj
// a missile's target still points at), then the globals, the players, the
// sectors, lines and sides, the block lists, the specials (active ceilings
// and lifts, buttons), each thinker's fields, and an end marker. References
// are indices: thinkers into the table, sectors, lines, subsectors and
// players into the level's and the world's arrays; -1 is null.
public sealed partial class World
{
    /// <summary>The thinker table's kinds (the thinker classes; a removed thinker's function no longer tells them).</summary>
    private enum SaveKind : byte
    {
        Mobj,
        Door,
        Floor,
        Plat,
        Ceiling,
        FireFlicker,
        LightFlash,
        Strobe,
        Glow,
    }

    /// <summary>Marks the end of the data (vanilla's <c>SAVEGAME_EOF</c>, 0x1d).</summary>
    private const int SaveEof = 0x1d1d1d1d;

    /// <summary>More thinkers than this in a save is corruption (vanilla's maps hold a few thousand).</summary>
    private const int MaxSavedThinkers = 1 << 20;

    // ---- saving ----

    /// <summary>
    /// p_saveg.c's <c>P_ArchivePlayers</c>, <c>P_ArchiveWorld</c>,
    /// <c>P_ArchiveThinkers</c> and <c>P_ArchiveSpecials</c> (g_game.c
    /// <c>G_DoSaveGame</c>'s), after the start (<see cref="SaveGameStart"/>):
    /// the whole play state between two tics to <paramref name="w"/>.
    /// The presentation's interpolation and the event queue are not saved.
    /// </summary>
    public void P_ArchiveGame(BinaryWriter w)
    {
        // the start: what G_InitNew and G_DoLoadLevel need
        w.Write((int)settings.gamemode);
        w.Write((int)settings.gameskill);
        w.Write(settings.netgame);
        w.Write(settings.deathmatch);
        w.Write(settings.nomonsters);
        w.Write(settings.respawnparm);
        w.Write(settings.fastparm);
        w.Write(tweaks.AbsoluteAiming);
        w.Write(tweaks.AbsoluteMovement);
        w.Write(tweaks.AimAssistCone);
        w.Write(tweaks.UseFallback);
        w.Write(level.Name);

        // The data first (finding the thinkers only referenced on the way), then the table, then the data.
        var refs = new SaveRefs(this);
        using var data = new MemoryStream();
        using (var d = new BinaryWriter(data, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            P_ArchiveGlobals(d);
            P_ArchivePlayers(d, refs);
            P_ArchiveWorld(d, refs);
            P_ArchiveSpecials(d, refs);
            P_ArchiveThinkers(d, refs); // last: the table grows while the thinkers are written
            d.Write(SaveEof);
        }
        w.Write(refs.table.Count);
        foreach ((thinker_t th, bool inList) in refs.table)
        {
            w.Write((byte)KindOf(th));
            w.Write(inList);
        }
        w.Write(data.GetBuffer(), 0, (int)data.Length);
    }

    /// <summary>The thinker table and its lookups (by reference: no iteration over the dictionary).</summary>
    private sealed class SaveRefs
    {
        public readonly List<(thinker_t Thinker, bool InList)> table = [];
        private readonly Dictionary<thinker_t, int> _index = new(ReferenceEqualityComparer.Instance);

        public SaveRefs(World world)
        {
            for (thinker_t th = world.thinkercap.next; th != world.thinkercap; th = th.next)
                Add(th, true);
        }

        private int Add(thinker_t th, bool inList)
        {
            int i = table.Count;
            _index[th] = i;
            table.Add((th, inList));
            return i;
        }

        /// <summary>The index of <paramref name="th"/> (-1 for null), added to the table when only referenced.</summary>
        public int Of(thinker_t? th) => th is null ? -1 : _index.TryGetValue(th, out int i) ? i : Add(th, false);
    }

    private static SaveKind KindOf(thinker_t th) => th switch
    {
        mobj_t => SaveKind.Mobj,
        vldoor_t => SaveKind.Door,
        floormove_t => SaveKind.Floor,
        plat_t => SaveKind.Plat,
        ceiling_t => SaveKind.Ceiling,
        fireflicker_t => SaveKind.FireFlicker,
        lightflash_t => SaveKind.LightFlash,
        strobe_t => SaveKind.Strobe,
        glow_t => SaveKind.Glow,
        _ => throw new InvalidOperationException($"A thinker of an unknown kind: {th.GetType().Name}"),
    };

    private static void WriteString(BinaryWriter w, string? s)
    {
        w.Write(s is not null);
        if (s is not null)
            w.Write(s);
    }

    private static void WriteMapThing(BinaryWriter w, MapThing t)
    {
        w.Write(t.X);
        w.Write(t.Y);
        w.Write(t.Angle);
        w.Write(t.Type);
        w.Write(t.Options);
    }

    private static void WriteInts(BinaryWriter w, int[] a)
    {
        w.Write(a.Length);
        foreach (int v in a)
            w.Write(v);
    }

    private static void WriteBools(BinaryWriter w, bool[] a)
    {
        w.Write(a.Length);
        foreach (bool v in a)
            w.Write(v);
    }

    /// <summary>The world's own globals that last from one tic to the next.</summary>
    private void P_ArchiveGlobals(BinaryWriter w)
    {
        w.Write(gametic);
        w.Write(leveltime);
        w.Write(random.prndindex);
        w.Write(random.rndindex);
        WriteBools(w, playeringame);
        w.Write(totalkills);
        w.Write(totalitems);
        w.Write(totalsecret);
        w.Write(onground);
        WriteBools(w, turbodetected);
        w.Write(bulletslope);
        w.Write(levelTimer);
        w.Write(levelTimeCount);

        // p_mobj.c's item respawn queue
        w.Write(iquehead);
        w.Write(iquetail);
        for (int i = 0; i < ITEMQUESIZE; i++)
        {
            WriteMapThing(w, itemrespawnque[i]);
            w.Write(itemrespawntime[i]);
        }

        // p_setup.c's starts
        for (int i = 0; i < MAXPLAYERS; i++)
        {
            w.Write(playerstarts[i].HasValue);
            if (playerstarts[i] is MapThing start)
                WriteMapThing(w, start);
        }
        w.Write(deathmatchstarts.Count);
        foreach (MapThing start in deathmatchstarts)
            WriteMapThing(w, start);

        // p_spec.c's scrolling walls and the animations' frames
        w.Write((int)numlinespecials);
        for (int i = 0; i < numlinespecials; i++)
            w.Write(linespeciallist[i]!.Index);
        WriteInts(w, texturetranslation);
        WriteInts(w, flattranslation);
    }

    /// <summary>p_saveg.c <c>P_ArchivePlayers</c>: every player, in the game or not.</summary>
    private void P_ArchivePlayers(BinaryWriter w, SaveRefs refs)
    {
        foreach (player_t p in players)
        {
            w.Write(refs.Of(p.mo));
            w.Write((int)p.playerstate);
            w.Write(p.cmd.forwardmove);
            w.Write(p.cmd.sidemove);
            w.Write(p.cmd.angleturn);
            w.Write(p.cmd.consistancy);
            w.Write(p.cmd.chatchar);
            w.Write(p.cmd.buttons);
            w.Write(p.viewz);
            w.Write(p.viewheight);
            w.Write(p.deltaviewheight);
            w.Write(p.bob);
            w.Write(p.health);
            w.Write(p.armorpoints);
            w.Write(p.armortype);
            WriteInts(w, p.powers);
            WriteBools(w, p.cards);
            w.Write(p.backpack);
            WriteInts(w, p.frags);
            w.Write((int)p.readyweapon);
            w.Write((int)p.pendingweapon);
            WriteBools(w, p.weaponowned);
            WriteInts(w, p.ammo);
            WriteInts(w, p.maxammo);
            w.Write(p.attackdown);
            w.Write(p.usedown);
            w.Write(p.cheats);
            w.Write(p.refire);
            w.Write(p.killcount);
            w.Write(p.itemcount);
            w.Write(p.secretcount);
            WriteString(w, p.message);
            w.Write(p.damagecount);
            w.Write(p.bonuscount);
            w.Write(refs.Of(p.attacker));
            w.Write(p.extralight);
            w.Write(p.fixedcolormap);
            w.Write(p.colormap);
            foreach (pspdef_t psp in p.psprites)
            {
                w.Write((int)psp.state);
                w.Write(psp.tics);
                w.Write(psp.sx);
                w.Write(psp.sy);
            }
            w.Write(p.didsecret);
        }
    }

    /// <summary>p_saveg.c <c>P_ArchiveWorld</c>: the sectors, lines and sides as the game changed them, and the block lists.</summary>
    private void P_ArchiveWorld(BinaryWriter w, SaveRefs refs)
    {
        w.Write(sectors.Length);
        foreach (sector_t sec in sectors)
        {
            w.Write(sec.floorheight);
            w.Write(sec.ceilingheight);
            w.Write(sec.floorpic);
            w.Write(sec.ceilingpic);
            w.Write(sec.lightlevel);
            w.Write(sec.special);
            w.Write(sec.tag);
            w.Write(sec.soundtraversed);
            w.Write(refs.Of(sec.soundtarget));
            w.Write(refs.Of(sec.thinglist));
            w.Write(refs.Of(sec.specialdata));
        }
        w.Write(lines.Length);
        foreach (line_t li in lines)
        {
            w.Write(li.flags);
            w.Write(li.special);
            w.Write(li.tag);
        }
        w.Write(sides.Length);
        foreach (side_t si in sides)
        {
            w.Write(si.textureoffset);
            w.Write(si.rowoffset);
            w.Write(si.toptexture);
            w.Write(si.bottomtexture);
            w.Write(si.midtexture);
        }

        // the block lists' heads (the mobjs link the rest), the used blocks only
        int used = 0;
        foreach (mobj_t? mo in blocklinks)
        {
            if (mo is not null)
                used++;
        }
        w.Write(blocklinks.Length);
        w.Write(used);
        for (int i = 0; i < blocklinks.Length; i++)
        {
            if (blocklinks[i] is { } mo)
            {
                w.Write(i);
                w.Write(refs.Of(mo));
            }
        }
    }

    /// <summary>p_saveg.c <c>P_ArchiveSpecials</c>'s lists: the active ceilings and lifts, and the buttons.</summary>
    private void P_ArchiveSpecials(BinaryWriter w, SaveRefs refs)
    {
        foreach (ceiling_t? c in activeceilings)
            w.Write(refs.Of(c));
        foreach (plat_t? p in activeplats)
            w.Write(refs.Of(p));
        foreach (button_t b in buttonlist)
        {
            w.Write(b.line?.Index ?? -1);
            w.Write((int)b.where);
            WriteString(w, b.btexture);
            w.Write(b.btimer);
            w.Write(b.soundorg?.Index ?? -1);
        }
    }

    /// <summary>p_saveg.c <c>P_ArchiveThinkers</c> (and the specials' thinkers): each thinker of the table's fields, in table order.</summary>
    private void P_ArchiveThinkers(BinaryWriter w, SaveRefs refs)
    {
        for (int i = 0; i < refs.table.Count; i++)
        {
            thinker_t th = refs.table[i].Thinker;
            w.Write((int)th.function);
            switch (th)
            {
                case mobj_t mo:
                    w.Write(mo.x);
                    w.Write(mo.y);
                    w.Write(mo.z);
                    w.Write(refs.Of(mo.snext));
                    w.Write(refs.Of(mo.sprev));
                    w.Write(mo.angle);
                    w.Write((int)mo.sprite);
                    w.Write(mo.frame);
                    w.Write(refs.Of(mo.bnext));
                    w.Write(refs.Of(mo.bprev));
                    w.Write(mo.subsector?.Index ?? -1);
                    w.Write(mo.floorz);
                    w.Write(mo.ceilingz);
                    w.Write(mo.radius);
                    w.Write(mo.height);
                    w.Write(mo.momx);
                    w.Write(mo.momy);
                    w.Write(mo.momz);
                    w.Write((int)mo.type);
                    w.Write(mo.tics);
                    w.Write((int)mo.state);
                    w.Write((int)mo.flags);
                    w.Write(mo.health);
                    w.Write(mo.movedir);
                    w.Write(mo.movecount);
                    w.Write(refs.Of(mo.target));
                    w.Write(mo.reactiontime);
                    w.Write(mo.threshold);
                    w.Write(mo.player is null ? -1 : Array.IndexOf(players, mo.player));
                    w.Write(mo.lastlook);
                    WriteMapThing(w, mo.spawnpoint);
                    w.Write(refs.Of(mo.tracer));
                    break;
                case vldoor_t door:
                    w.Write((int)door.type);
                    w.Write(door.sector.Index);
                    w.Write(door.topheight);
                    w.Write(door.speed);
                    w.Write(door.direction);
                    w.Write(door.topwait);
                    w.Write(door.topcountdown);
                    break;
                case floormove_t floor:
                    w.Write((int)floor.type);
                    w.Write(floor.crush);
                    w.Write(floor.sector.Index);
                    w.Write(floor.direction);
                    w.Write(floor.newspecial);
                    WriteString(w, floor.texture);
                    w.Write(floor.floordestheight);
                    w.Write(floor.speed);
                    w.Write(floor.doordirection);
                    break;
                case plat_t plat:
                    w.Write(plat.sector.Index);
                    w.Write(plat.speed);
                    w.Write(plat.low);
                    w.Write(plat.high);
                    w.Write(plat.wait);
                    w.Write(plat.count);
                    w.Write((int)plat.status);
                    w.Write((int)plat.oldstatus);
                    w.Write(plat.crush);
                    w.Write(plat.tag);
                    w.Write((int)plat.type);
                    break;
                case ceiling_t ceiling:
                    w.Write((int)ceiling.type);
                    w.Write(ceiling.sector.Index);
                    w.Write(ceiling.bottomheight);
                    w.Write(ceiling.topheight);
                    w.Write(ceiling.speed);
                    w.Write(ceiling.crush);
                    w.Write(ceiling.direction);
                    w.Write(ceiling.tag);
                    w.Write(ceiling.olddirection);
                    break;
                case fireflicker_t flicker:
                    w.Write(flicker.sector.Index);
                    w.Write(flicker.count);
                    w.Write(flicker.maxlight);
                    w.Write(flicker.minlight);
                    break;
                case lightflash_t flash:
                    w.Write(flash.sector.Index);
                    w.Write(flash.count);
                    w.Write(flash.maxlight);
                    w.Write(flash.minlight);
                    w.Write(flash.maxtime);
                    w.Write(flash.mintime);
                    break;
                case strobe_t strobe:
                    w.Write(strobe.sector.Index);
                    w.Write(strobe.count);
                    w.Write(strobe.minlight);
                    w.Write(strobe.maxlight);
                    w.Write(strobe.darktime);
                    w.Write(strobe.brighttime);
                    break;
                case glow_t glow:
                    w.Write(glow.sector.Index);
                    w.Write(glow.minlight);
                    w.Write(glow.maxlight);
                    w.Write(glow.direction);
                    break;
            }
        }
    }

    // ---- loading ----

    /// <summary>
    /// g_game.c <c>G_DoLoadGame</c>'s header: the start of a save written
    /// by <see cref="P_ArchiveGame"/>, for a new world
    /// (<c>new World(start.settings, start.tweaks)</c>) and the map's level.
    /// </summary>
    public static SaveGameStart P_ReadSaveStart(BinaryReader r)
    {
        try
        {
            var settings = new SpawnSettings(
                ReadEnum<GameMode>(r),
                ReadEnum<skill_t>(r),
                r.ReadBoolean(),
                r.ReadInt32(),
                r.ReadBoolean(),
                r.ReadBoolean(),
                r.ReadBoolean());
            var tweaks = new Tweaks
            {
                AbsoluteAiming = r.ReadBoolean(),
                AbsoluteMovement = r.ReadBoolean(),
                AimAssistCone = r.ReadUInt32(),
                UseFallback = r.ReadBoolean(),
            };
            string map = r.ReadString();
            if (map.Length is 0 or > 8)
                throw new SaveGameException($"bad map name \"{map}\"");
            return new SaveGameStart(settings, tweaks, map);
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or FormatException or ArgumentException)
        {
            throw new SaveGameException("the save is cut short or damaged", e);
        }
    }

    /// <summary>
    /// p_saveg.c's <c>P_UnArchivePlayers</c>, <c>P_UnArchiveWorld</c>,
    /// <c>P_UnArchiveThinkers</c> and <c>P_UnArchiveSpecials</c> (g_game.c
    /// <c>G_DoLoadGame</c>'s), after <see cref="P_ReadSaveStart"/>, into a
    /// new world built with the start's settings and tweaks (and its
    /// <see cref="P_InitPicAnims"/>), on <paramref name="level"/>: the start's
    /// map, freshly loaded (<see cref="Level.Load"/>), which this world then
    /// owns. Vanilla loads the level with its things first
    /// (<c>G_InitNew</c>) and then replaces them; here only the map part of
    /// <see cref="P_SetupLevel"/> runs (so loading draws no <c>P_Random</c>).
    /// Throws <see cref="SaveGameException"/> when the data does not fit the
    /// level or is damaged; the world is then unusable.
    /// </summary>
    public void P_UnArchiveGame(BinaryReader r, Level level)
    {
        try
        {
            P_InitThinkers();
            P_SetupLevelMap(level);
            unported.Clear();
            events.Clear();
            gameaction = gameaction_t.ga_nothing;
            secretexit = false;

            int count = r.ReadInt32();
            if (count < 0 || count > MaxSavedThinkers)
                throw new SaveGameException($"bad thinker count {count}");
            var table = new thinker_t[count];
            for (int i = 0; i < count; i++)
            {
                table[i] = ReadKind(r) switch
                {
                    SaveKind.Mobj => new mobj_t(),
                    SaveKind.Door => new vldoor_t(),
                    SaveKind.Floor => new floormove_t(),
                    SaveKind.Plat => new plat_t(),
                    SaveKind.Ceiling => new ceiling_t(),
                    SaveKind.FireFlicker => new fireflicker_t(),
                    SaveKind.LightFlash => new lightflash_t(),
                    SaveKind.Strobe => new strobe_t(),
                    _ => new glow_t(),
                };
                if (r.ReadBoolean())
                    P_AddThinker(table[i]);
            }
            var load = new LoadRefs(this, table);

            P_UnArchiveGlobals(r);
            P_UnArchivePlayers(r, load);
            P_UnArchiveWorld(r, load);
            P_UnArchiveSpecials(r, load);
            P_UnArchiveThinkers(r, load);
            if (r.ReadInt32() != SaveEof)
                throw new SaveGameException("bad end of the save");
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or FormatException or ArgumentException
                                      or InvalidCastException or IndexOutOfRangeException or NullReferenceException)
        {
            throw new SaveGameException("the save is cut short or damaged", e);
        }
    }

    /// <summary>The loading side of the references: indices checked against their arrays.</summary>
    private sealed class LoadRefs
    {
        private readonly World _world;
        private readonly thinker_t[] _table;

        public LoadRefs(World world, thinker_t[] table)
        {
            _world = world;
            _table = table;
        }

        public thinker_t? Thinker(BinaryReader r)
        {
            int i = r.ReadInt32();
            if (i == -1)
                return null;
            if (i < 0 || i >= _table.Length)
                throw new SaveGameException($"bad thinker reference {i}");
            return _table[i];
        }

        public T? Thinker<T>(BinaryReader r) where T : thinker_t =>
            Thinker(r) switch
            {
                null => null,
                T t => t,
                var other => throw new SaveGameException($"a {typeof(T).Name} reference to a {other.GetType().Name}"),
            };

        public int Count => _table.Length;

        public thinker_t At(int i) => _table[i];

        public sector_t Sector(BinaryReader r) => SectorOrNull(r) ?? throw new SaveGameException("a missing sector");

        public sector_t? SectorOrNull(BinaryReader r) => Index(r, _world.sectors, "sector");

        public line_t? Line(BinaryReader r) => Index(r, _world.lines, "line");

        public subsector_t? Subsector(BinaryReader r) => Index(r, _world.subsectors, "subsector");

        private static T? Index<T>(BinaryReader r, T[] array, string what) where T : class
        {
            int i = r.ReadInt32();
            if (i == -1)
                return null;
            if (i < 0 || i >= array.Length)
                throw new SaveGameException($"bad {what} reference {i}");
            return array[i];
        }
    }

    private static SaveKind ReadKind(BinaryReader r)
    {
        var kind = (SaveKind)r.ReadByte();
        if (!Enum.IsDefined(kind))
            throw new SaveGameException($"bad thinker kind {(int)kind}");
        return kind;
    }

    private static T ReadEnum<T>(BinaryReader r) where T : struct, Enum
    {
        int v = r.ReadInt32();
        var e = (T)(object)v;
        if (!Enum.IsDefined(e))
            throw new SaveGameException($"bad {typeof(T).Name} {v}");
        return e;
    }

    private static T ReadRange<T>(BinaryReader r, int count) where T : struct, Enum
    {
        int v = r.ReadInt32();
        if (v < 0 || v >= count)
            throw new SaveGameException($"bad {typeof(T).Name} {v}");
        return (T)(object)v;
    }

    private static string? ReadNullableString(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

    private static MapThing ReadMapThing(BinaryReader r) =>
        new(r.ReadInt16(), r.ReadInt16(), r.ReadInt16(), r.ReadInt16(), r.ReadInt16());

    private static void ReadInts(BinaryReader r, int[] into, string what)
    {
        int n = r.ReadInt32();
        if (n != into.Length)
            throw new SaveGameException($"{what}: {n} saved, {into.Length} here") { OtherGame = true };
        for (int i = 0; i < n; i++)
            into[i] = r.ReadInt32();
    }

    private static void ReadBools(BinaryReader r, bool[] into, string what)
    {
        int n = r.ReadInt32();
        if (n != into.Length)
            throw new SaveGameException($"{what}: {n} saved, {into.Length} here");
        for (int i = 0; i < n; i++)
            into[i] = r.ReadBoolean();
    }

    private void P_UnArchiveGlobals(BinaryReader r)
    {
        gametic = r.ReadInt32();
        leveltime = r.ReadInt32();
        random.prndindex = r.ReadInt32() & 0xff;
        random.rndindex = r.ReadInt32() & 0xff;
        ReadBools(r, playeringame, "players");
        totalkills = r.ReadInt32();
        totalitems = r.ReadInt32();
        totalsecret = r.ReadInt32();
        onground = r.ReadBoolean();
        ReadBools(r, turbodetected, "players");
        bulletslope = r.ReadInt32();
        levelTimer = r.ReadBoolean();
        levelTimeCount = r.ReadInt32();

        iquehead = r.ReadInt32();
        iquetail = r.ReadInt32();
        if (iquehead is < 0 or >= ITEMQUESIZE || iquetail is < 0 or >= ITEMQUESIZE)
            throw new SaveGameException("bad item respawn queue");
        for (int i = 0; i < ITEMQUESIZE; i++)
        {
            itemrespawnque[i] = ReadMapThing(r);
            itemrespawntime[i] = r.ReadInt32();
        }

        for (int i = 0; i < MAXPLAYERS; i++)
            playerstarts[i] = r.ReadBoolean() ? ReadMapThing(r) : null;
        int dm = r.ReadInt32();
        if (dm < 0 || dm > MapThingSpawning.MAX_DM_STARTS)
            throw new SaveGameException($"bad deathmatch start count {dm}");
        deathmatchstarts.Clear();
        for (int i = 0; i < dm; i++)
            deathmatchstarts.Add(ReadMapThing(r));

        int scrollers = r.ReadInt32();
        if (scrollers < 0 || scrollers > MAXLINEANIMS)
            throw new SaveGameException($"bad scrolling wall count {scrollers}");
        Array.Clear(linespeciallist);
        numlinespecials = (short)scrollers;
        for (int i = 0; i < scrollers; i++)
        {
            int line = r.ReadInt32();
            if (line < 0 || line >= lines.Length)
                throw new SaveGameException($"bad scrolling wall line {line}");
            linespeciallist[i] = lines[line];
        }
        ReadInts(r, texturetranslation, "texture animations");
        ReadInts(r, flattranslation, "flat animations");
    }

    private void P_UnArchivePlayers(BinaryReader r, LoadRefs refs)
    {
        foreach (player_t p in players)
        {
            p.mo = refs.Thinker<mobj_t>(r);
            p.playerstate = ReadEnum<playerstate_t>(r);
            p.cmd = new ticcmd_t
            {
                forwardmove = r.ReadSByte(),
                sidemove = r.ReadSByte(),
                angleturn = r.ReadInt16(),
                consistancy = r.ReadInt16(),
                chatchar = r.ReadByte(),
                buttons = r.ReadByte(),
            };
            p.viewz = r.ReadInt32();
            p.viewheight = r.ReadInt32();
            p.deltaviewheight = r.ReadInt32();
            p.bob = r.ReadInt32();
            p.health = r.ReadInt32();
            p.armorpoints = r.ReadInt32();
            p.armortype = r.ReadInt32();
            ReadInts(r, p.powers, "powers");
            ReadBools(r, p.cards, "keys");
            p.backpack = r.ReadBoolean();
            ReadInts(r, p.frags, "frags");
            p.readyweapon = ReadEnum<weapontype_t>(r);
            p.pendingweapon = ReadEnum<weapontype_t>(r);
            ReadBools(r, p.weaponowned, "weapons");
            ReadInts(r, p.ammo, "ammo");
            ReadInts(r, p.maxammo, "ammo");
            p.attackdown = r.ReadBoolean();
            p.usedown = r.ReadBoolean();
            p.cheats = r.ReadInt32();
            p.refire = r.ReadInt32();
            p.killcount = r.ReadInt32();
            p.itemcount = r.ReadInt32();
            p.secretcount = r.ReadInt32();
            p.message = ReadNullableString(r);
            p.damagecount = r.ReadInt32();
            p.bonuscount = r.ReadInt32();
            p.attacker = refs.Thinker<mobj_t>(r);
            p.extralight = r.ReadInt32();
            p.fixedcolormap = r.ReadInt32();
            p.colormap = r.ReadInt32();
            foreach (pspdef_t psp in p.psprites)
            {
                psp.state = ReadRange<statenum_t>(r, (int)statenum_t.NUMSTATES);
                psp.tics = r.ReadInt32();
                psp.sx = r.ReadInt32();
                psp.sy = r.ReadInt32();
            }
            p.didsecret = r.ReadBoolean();
        }
    }

    private void P_UnArchiveWorld(BinaryReader r, LoadRefs refs)
    {
        if (r.ReadInt32() != sectors.Length)
            throw new SaveGameException($"the map {level.Name} has another number of sectors") { OtherGame = true };
        foreach (sector_t sec in sectors)
        {
            sec.floorheight = r.ReadInt32();
            sec.ceilingheight = r.ReadInt32();
            sec.floorpic = r.ReadString();
            sec.ceilingpic = r.ReadString();
            sec.lightlevel = r.ReadInt16();
            sec.special = r.ReadInt16();
            sec.tag = r.ReadInt16();
            sec.soundtraversed = r.ReadInt32();
            sec.soundtarget = refs.Thinker<mobj_t>(r);
            sec.thinglist = refs.Thinker<mobj_t>(r);
            sec.specialdata = refs.Thinker(r);
            sec.StoreInterpolation(); // not vanilla: drawn where it is
        }
        if (r.ReadInt32() != lines.Length)
            throw new SaveGameException($"the map {level.Name} has another number of lines") { OtherGame = true };
        foreach (line_t li in lines)
        {
            li.flags = r.ReadInt16();
            li.special = r.ReadInt16();
            li.tag = r.ReadInt16();
        }
        if (r.ReadInt32() != sides.Length)
            throw new SaveGameException($"the map {level.Name} has another number of sides") { OtherGame = true };
        foreach (side_t si in sides)
        {
            si.textureoffset = r.ReadInt32();
            si.rowoffset = r.ReadInt32();
            si.toptexture = r.ReadString();
            si.bottomtexture = r.ReadString();
            si.midtexture = r.ReadString();
        }

        if (r.ReadInt32() != blocklinks.Length)
            throw new SaveGameException($"the map {level.Name} has another blockmap") { OtherGame = true };
        int used = r.ReadInt32();
        if (used < 0 || used > blocklinks.Length)
            throw new SaveGameException($"bad block count {used}");
        for (int n = 0; n < used; n++)
        {
            int i = r.ReadInt32();
            if (i < 0 || i >= blocklinks.Length)
                throw new SaveGameException($"bad block {i}");
            blocklinks[i] = refs.Thinker<mobj_t>(r);
        }
    }

    private void P_UnArchiveSpecials(BinaryReader r, LoadRefs refs)
    {
        for (int i = 0; i < activeceilings.Length; i++)
            activeceilings[i] = refs.Thinker<ceiling_t>(r);
        for (int i = 0; i < activeplats.Length; i++)
            activeplats[i] = refs.Thinker<plat_t>(r);
        foreach (button_t b in buttonlist)
        {
            b.line = refs.Line(r);
            b.where = ReadEnum<bwhere_e>(r);
            b.btexture = ReadNullableString(r);
            b.btimer = r.ReadInt32();
            b.soundorg = refs.SectorOrNull(r);
        }
    }

    private void P_UnArchiveThinkers(BinaryReader r, LoadRefs refs)
    {
        for (int i = 0; i < refs.Count; i++)
        {
            thinker_t th = refs.At(i);
            th.function = ReadEnum<think_t>(r);
            switch (th)
            {
                case mobj_t mo:
                    mo.x = r.ReadInt32();
                    mo.y = r.ReadInt32();
                    mo.z = r.ReadInt32();
                    mo.snext = refs.Thinker<mobj_t>(r);
                    mo.sprev = refs.Thinker<mobj_t>(r);
                    mo.angle = r.ReadUInt32();
                    mo.sprite = ReadRange<spritenum_t>(r, (int)spritenum_t.NUMSPRITES);
                    mo.frame = r.ReadInt32();
                    mo.bnext = refs.Thinker<mobj_t>(r);
                    mo.bprev = refs.Thinker<mobj_t>(r);
                    mo.subsector = refs.Subsector(r) ?? throw new SaveGameException("a mobj without a subsector");
                    mo.floorz = r.ReadInt32();
                    mo.ceilingz = r.ReadInt32();
                    mo.radius = r.ReadInt32();
                    mo.height = r.ReadInt32();
                    mo.momx = r.ReadInt32();
                    mo.momy = r.ReadInt32();
                    mo.momz = r.ReadInt32();
                    mo.type = ReadRange<mobjtype_t>(r, (int)mobjtype_t.NUMMOBJTYPES);
                    mo.info = Info.mobjinfo[(int)mo.type];
                    mo.tics = r.ReadInt32();
                    mo.state = ReadRange<statenum_t>(r, (int)statenum_t.NUMSTATES);
                    mo.flags = (mobjflag_t)r.ReadInt32();
                    mo.health = r.ReadInt32();
                    mo.movedir = r.ReadInt32();
                    mo.movecount = r.ReadInt32();
                    mo.target = refs.Thinker<mobj_t>(r);
                    mo.reactiontime = r.ReadInt32();
                    mo.threshold = r.ReadInt32();
                    int player = r.ReadInt32();
                    if (player < -1 || player >= MAXPLAYERS)
                        throw new SaveGameException($"bad player {player}");
                    mo.player = player < 0 ? null : players[player];
                    mo.lastlook = r.ReadInt32();
                    mo.spawnpoint = ReadMapThing(r);
                    mo.tracer = refs.Thinker<mobj_t>(r);
                    // not vanilla: drawn where it is (T4.7)
                    mo.oldx = mo.x;
                    mo.oldy = mo.y;
                    mo.oldz = mo.z;
                    mo.oldangle = mo.angle;
                    mo.interp = false;
                    break;
                case vldoor_t door:
                    door.type = ReadEnum<vldoor_e>(r);
                    door.sector = refs.Sector(r);
                    door.topheight = r.ReadInt32();
                    door.speed = r.ReadInt32();
                    door.direction = r.ReadInt32();
                    door.topwait = r.ReadInt32();
                    door.topcountdown = r.ReadInt32();
                    break;
                case floormove_t floor:
                    floor.type = ReadEnum<floor_e>(r);
                    floor.crush = r.ReadBoolean();
                    floor.sector = refs.Sector(r);
                    floor.direction = r.ReadInt32();
                    floor.newspecial = r.ReadInt32();
                    floor.texture = ReadNullableString(r);
                    floor.floordestheight = r.ReadInt32();
                    floor.speed = r.ReadInt32();
                    floor.doordirection = r.ReadInt32();
                    break;
                case plat_t plat:
                    plat.sector = refs.Sector(r);
                    plat.speed = r.ReadInt32();
                    plat.low = r.ReadInt32();
                    plat.high = r.ReadInt32();
                    plat.wait = r.ReadInt32();
                    plat.count = r.ReadInt32();
                    plat.status = ReadEnum<plat_e>(r);
                    plat.oldstatus = ReadEnum<plat_e>(r);
                    plat.crush = r.ReadBoolean();
                    plat.tag = r.ReadInt32();
                    plat.type = ReadEnum<plattype_e>(r);
                    break;
                case ceiling_t ceiling:
                    ceiling.type = ReadEnum<ceiling_e>(r);
                    ceiling.sector = refs.Sector(r);
                    ceiling.bottomheight = r.ReadInt32();
                    ceiling.topheight = r.ReadInt32();
                    ceiling.speed = r.ReadInt32();
                    ceiling.crush = r.ReadInt32();
                    ceiling.direction = r.ReadInt32();
                    ceiling.tag = r.ReadInt32();
                    ceiling.olddirection = r.ReadInt32();
                    break;
                case fireflicker_t flicker:
                    flicker.sector = refs.Sector(r);
                    flicker.count = r.ReadInt32();
                    flicker.maxlight = r.ReadInt32();
                    flicker.minlight = r.ReadInt32();
                    break;
                case lightflash_t flash:
                    flash.sector = refs.Sector(r);
                    flash.count = r.ReadInt32();
                    flash.maxlight = r.ReadInt32();
                    flash.minlight = r.ReadInt32();
                    flash.maxtime = r.ReadInt32();
                    flash.mintime = r.ReadInt32();
                    break;
                case strobe_t strobe:
                    strobe.sector = refs.Sector(r);
                    strobe.count = r.ReadInt32();
                    strobe.minlight = r.ReadInt32();
                    strobe.maxlight = r.ReadInt32();
                    strobe.darktime = r.ReadInt32();
                    strobe.brighttime = r.ReadInt32();
                    break;
                case glow_t glow:
                    glow.sector = refs.Sector(r);
                    glow.minlight = r.ReadInt32();
                    glow.maxlight = r.ReadInt32();
                    glow.direction = r.ReadInt32();
                    break;
            }
        }
    }
}
