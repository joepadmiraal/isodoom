using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// T7.6: a saved game's file (SPEC §8, §12 T7.6; not vanilla's
/// <c>doomsav*.dsg</c> format), in plain C# (the tests link it). A header the
/// menus read without the WAD (the description, map, game mode, skill and
/// level time), the sim's state (<see cref="World.P_ArchiveGame"/>), and a
/// checksum over all of it:
/// <list type="bullet">
/// <item><see cref="Magic"/> (12 bytes), <see cref="VERSION"/> (int32);</item>
/// <item>the description, the map (length-prefixed UTF-8), the game mode, the skill, <c>leveltime</c> (int32s);</item>
/// <item>the state's length (int32) and the state;</item>
/// <item>a 64-bit FNV-1a hash of everything before it.</item>
/// </list>
/// A file from another version is refused before its hash is checked, so
/// the player is told which it is (<see cref="Read"/>).
/// </summary>
public sealed class SaveGameFile
{
    /// <summary>The file's first bytes.</summary>
    public static ReadOnlySpan<byte> Magic => "ISODOOMSAVE\n"u8;

    /// <summary>
    /// The format's version: a save of another version is refused (vanilla's
    /// <c>VERSIONSIZE</c> check). Bump it whenever the sim's state or its
    /// layout changes (a field saved, a thinker added).
    /// </summary>
    public const int VERSION = 1;

    /// <summary>The slot files' name (Chocolate Doom's <c>doomsav%d.dsg</c>, in a directory per IWAD).</summary>
    public static string SlotFileName(int slot) => $"isodoomsav{slot}.dsg";

    // The player's messages for a save that can't be loaded (the menus' message box, d_englsh.h's style).
    public const string DAMAGED = "this savegame is damaged\nand can't be loaded.";
    public const string OLDER = "this savegame is from an older\nversion of isodoom and can't\nbe loaded.";
    public const string NEWER = "this savegame is from a newer\nversion of isodoom and can't\nbe loaded.";
    public const string OTHERGAME = "this savegame is for another\ngame and can't be loaded.";
    public const string NOTASAVE = "this is not an isodoom savegame.";

    public SaveGameFile(string description, string map, GameMode gamemode, skill_t skill, int leveltime, byte[] state)
    {
        Description = description;
        Map = map;
        GameMode = gamemode;
        Skill = skill;
        LevelTime = leveltime;
        State = state;
    }

    /// <summary>m_menu.c's <c>savegamestrings</c>: what the player called it.</summary>
    public string Description { get; }

    /// <summary>The level's map lump (<c>E1M3</c>, <c>MAP07</c>).</summary>
    public string Map { get; }

    public GameMode GameMode { get; }

    public skill_t Skill { get; }

    /// <summary>The level's <c>leveltime</c> when it was saved (tics).</summary>
    public int LevelTime { get; }

    /// <summary>The sim's state (<see cref="World.P_ArchiveGame"/>).</summary>
    public byte[] State { get; }

    /// <summary>g_game.c <c>G_DoSaveGame</c>'s writing: <paramref name="world"/>'s game as a file's bytes.</summary>
    public static byte[] Write(World world, string description)
    {
        using var state = new MemoryStream();
        using (var w = new BinaryWriter(state, Encoding.UTF8, leaveOpen: true))
            world.P_ArchiveGame(w);
        return new SaveGameFile(description, world.level.Name, world.gamemode, world.gameskill, world.leveltime, state.ToArray()).ToBytes();
    }

    /// <summary>The file's bytes.</summary>
    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(VERSION);
            w.Write(Description);
            w.Write(Map);
            w.Write((int)GameMode);
            w.Write((int)Skill);
            w.Write(LevelTime);
            w.Write(State.Length);
            w.Write(State);
        }
        ulong hash = Fnv(stream.GetBuffer().AsSpan(0, (int)stream.Length));
        stream.Write(BitConverter.GetBytes(hash));
        return stream.ToArray();
    }

    /// <summary>
    /// Reads a save file's bytes, refusing (with a <see cref="SaveGameException"/>
    /// whose message is for the player) one that is not a save
    /// (<see cref="NOTASAVE"/>), from another version (<see cref="OLDER"/>,
    /// <see cref="NEWER"/>) or damaged (<see cref="DAMAGED"/>: cut short, its
    /// hash not matching). The state itself is checked when it is loaded
    /// (<see cref="LoadWorld"/>).
    /// </summary>
    public static SaveGameFile Read(byte[] data)
    {
        if (data.Length < Magic.Length + 4 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new SaveGameException(NOTASAVE);
        int version = BitConverter.ToInt32(data, Magic.Length);
        if (version != VERSION)
            throw new SaveGameException(version < VERSION ? OLDER : NEWER);
        if (data.Length < Magic.Length + 4 + 8)
            throw new SaveGameException(DAMAGED);
        ulong hash = BitConverter.ToUInt64(data, data.Length - 8);
        if (hash != Fnv(data.AsSpan(0, data.Length - 8)))
            throw new SaveGameException(DAMAGED);
        try
        {
            using var r = new BinaryReader(new MemoryStream(data, 0, data.Length - 8), Encoding.UTF8);
            r.ReadBytes(Magic.Length + 4);
            string description = r.ReadString();
            string map = r.ReadString();
            var gamemode = (GameMode)r.ReadInt32();
            var skill = (skill_t)r.ReadInt32();
            int leveltime = r.ReadInt32();
            int length = r.ReadInt32();
            if (!Enum.IsDefined(gamemode) || !Enum.IsDefined(skill) || length < 0 || length != r.BaseStream.Length - r.BaseStream.Position)
                throw new SaveGameException(DAMAGED);
            return new SaveGameFile(description, map, gamemode, skill, leveltime, r.ReadBytes(length));
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or FormatException or ArgumentException)
        {
            throw new SaveGameException(DAMAGED, e);
        }
    }

    /// <summary>
    /// m_menu.c <c>M_ReadSaveStrings</c>'s read: the description of a save
    /// file's bytes, whatever its version (so a save the game can no longer
    /// load still shows, and loading it says why); null when it is not a save.
    /// </summary>
    public static string? ReadDescription(byte[] data)
    {
        if (data.Length < Magic.Length + 4 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            return null;
        try
        {
            using var r = new BinaryReader(new MemoryStream(data, Magic.Length + 4, data.Length - Magic.Length - 4), Encoding.UTF8);
            return r.ReadString();
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// g_game.c <c>G_DoLoadGame</c>'s loading: a new world with the save's
    /// settings and tweaks (<see cref="World.P_ReadSaveStart"/>; the
    /// <paramref name="textures"/> and <paramref name="flats"/> of
    /// <c>P_Init</c>, as a new game's) on <paramref name="level"/> (the save's
    /// <see cref="Map"/>, freshly loaded) with the saved state
    /// (<see cref="World.P_UnArchiveGame"/>). Throws a
    /// <see cref="SaveGameException"/> with the player's message
    /// (<see cref="DAMAGED"/>, <see cref="OTHERGAME"/>) and the reason inside.
    /// </summary>
    public World LoadWorld(Level level, Textures? textures, IReadOnlyList<string> flats)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(State), Encoding.UTF8);
            SaveGameStart start = World.P_ReadSaveStart(r);
            if (start.settings.gamemode != GameMode || !string.Equals(start.map, level.Name, StringComparison.OrdinalIgnoreCase))
                throw new SaveGameException(DAMAGED);
            var world = new World(start.settings, start.tweaks) { textures = textures };
            world.P_InitPicAnims(textures, flats);
            world.P_UnArchiveGame(r, level);
            if (r.BaseStream.Position != State.Length)
                throw new SaveGameException("data after the end of the save");
            return world;
        }
        catch (SaveGameException e) when (e.Message is not (DAMAGED or OTHERGAME))
        {
            // the sim's reason is for the log; another WAD's map (its sectors, lines, animations) is another game
            throw new SaveGameException(e.OtherGame ? OTHERGAME : DAMAGED, e);
        }
    }

    /// <summary>64-bit FNV-1a.</summary>
    private static ulong Fnv(ReadOnlySpan<byte> data)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in data)
        {
            h ^= b;
            h *= 1099511628211UL;
        }
        return h;
    }
}
