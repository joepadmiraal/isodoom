using System;
using IsoDoom.Sim;

namespace IsoDoom.Render;

/// <summary>Which pickups get a beam while the level hides them (T6.13r, <c>ItemBeams</c>, SPEC §12; a presentation option).</summary>
public enum ItemBeamMode
{
    /// <summary>No beams: vanilla.</summary>
    Off,

    /// <summary>Keys, weapons and power-ups only: the pickups a player would go back for.</summary>
    Keys,

    /// <summary>Every pickup (the default).</summary>
    All,
}

/// <summary>What a pickup is, for its beam's colour (T6.13r).</summary>
public enum ItemKind
{
    /// <summary>Not a pickup: no beam.</summary>
    None,
    BlueKey,
    YellowKey,
    RedKey,
    Weapon,

    /// <summary>The power-ups and the big ones: spheres, invulnerability, the suit, the visor, the map, berserk, the backpack.</summary>
    PowerUp,
    Armor,
    Health,
    Ammo,
}

/// <summary>
/// The item beams' table (T6.13r, plain C# so it is unit tested): each pickup
/// type's kind (info.c's <c>MF_SPECIAL</c> types, as p_inter.c's
/// <c>P_TouchSpecialThing</c> sorts them by sprite), whether a mode beams it,
/// and its colour.
/// </summary>
public static class ItemKinds
{
    /// <summary>The kind of a <paramref name="type"/> (<see cref="ItemKind.None"/> for anything but a pickup).</summary>
    public static ItemKind KindOf(mobjtype_t type) => type switch
    {
        mobjtype_t.MT_MISC4 or mobjtype_t.MT_MISC9 => ItemKind.BlueKey, // BKEY, BSKU
        mobjtype_t.MT_MISC6 or mobjtype_t.MT_MISC7 => ItemKind.YellowKey, // YKEY, YSKU
        mobjtype_t.MT_MISC5 or mobjtype_t.MT_MISC8 => ItemKind.RedKey, // RKEY, RSKU
        mobjtype_t.MT_MISC25 // BFUG
            or mobjtype_t.MT_CHAINGUN // MGUN
            or mobjtype_t.MT_MISC26 // CSAW
            or mobjtype_t.MT_MISC27 // LAUN
            or mobjtype_t.MT_MISC28 // PLAS
            or mobjtype_t.MT_SHOTGUN // SHOT
            or mobjtype_t.MT_SUPERSHOTGUN => ItemKind.Weapon, // SGN2
        mobjtype_t.MT_MISC12 // SOUL
            or mobjtype_t.MT_MEGA // MEGA
            or mobjtype_t.MT_INV // PINV
            or mobjtype_t.MT_INS // PINS
            or mobjtype_t.MT_MISC13 // PSTR, berserk
            or mobjtype_t.MT_MISC14 // SUIT
            or mobjtype_t.MT_MISC15 // PMAP
            or mobjtype_t.MT_MISC16 // PVIS
            or mobjtype_t.MT_MISC24 => ItemKind.PowerUp, // BPAK
        mobjtype_t.MT_MISC0 or mobjtype_t.MT_MISC1 or mobjtype_t.MT_MISC3 => ItemKind.Armor, // ARM1, ARM2, BON2
        mobjtype_t.MT_MISC2 or mobjtype_t.MT_MISC10 or mobjtype_t.MT_MISC11 => ItemKind.Health, // BON1, STIM, MEDI
        mobjtype_t.MT_CLIP // CLIP
            or mobjtype_t.MT_MISC17 // AMMO
            or mobjtype_t.MT_MISC18 // ROCK
            or mobjtype_t.MT_MISC19 // BROK
            or mobjtype_t.MT_MISC20 // CELL
            or mobjtype_t.MT_MISC21 // CELP
            or mobjtype_t.MT_MISC22 // SHEL
            or mobjtype_t.MT_MISC23 => ItemKind.Ammo, // SBOX
        _ => ItemKind.None,
    };

    /// <summary>Whether <paramref name="mode"/> beams a pickup of <paramref name="kind"/>.</summary>
    public static bool Beamed(ItemKind kind, ItemBeamMode mode) => kind != ItemKind.None && mode switch
    {
        ItemBeamMode.All => true,
        ItemBeamMode.Keys => kind is ItemKind.BlueKey or ItemKind.YellowKey or ItemKind.RedKey or ItemKind.Weapon or ItemKind.PowerUp,
        _ => false,
    };

    /// <summary>
    /// A kind's beam colour (sRGB, 0–1): the key colours, orange weapons,
    /// magenta power-ups, green armor, pink health, tan ammo; bright enough
    /// for the dark floors, and none the shot tracers' pale yellow or red
    /// (<c>ShotTracers.PlayerColor</c>, <c>MonsterColor</c>).
    /// </summary>
    public static (float R, float G, float B) ColorOf(ItemKind kind) => kind switch
    {
        ItemKind.BlueKey => (0.25f, 0.5f, 1f),
        ItemKind.YellowKey => (1f, 0.9f, 0.1f),
        ItemKind.RedKey => (1f, 0.1f, 0.1f),
        ItemKind.Weapon => (1f, 0.55f, 0f),
        ItemKind.PowerUp => (0.9f, 0.35f, 1f),
        ItemKind.Armor => (0.25f, 1f, 0.3f),
        ItemKind.Health => (1f, 0.45f, 0.7f),
        ItemKind.Ammo => (0.75f, 0.6f, 0.35f),
        _ => (1f, 1f, 1f),
    };

    /// <summary>Reads a mode (<c>off</c>, <c>keys</c>, <c>all</c>); <paramref name="name"/> names the argument or setting in the error.</summary>
    public static ItemBeamMode Parse(string value, string name) => value switch
    {
        "off" or "vanilla" => ItemBeamMode.Off,
        "keys" => ItemBeamMode.Keys,
        "all" or "on" => ItemBeamMode.All,
        _ => throw new ArgumentException($"{name}: \"{value}\" (off, keys or all)"),
    };

    /// <summary>A mode's name, as <see cref="Parse"/> reads it.</summary>
    public static string Name(ItemBeamMode mode) => mode.ToString().ToLowerInvariant();
}
