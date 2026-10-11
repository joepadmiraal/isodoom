using System;
using System.Linq;
using IsoDoom.Render;
using IsoDoom.Sim;
using Xunit;

namespace IsoDoom.Tests.Render;

/// <summary>T6.13r: the item beams' table, each pickup type's kind, which modes beam it and its colour (<see cref="ItemKinds"/>).</summary>
public class ItemKindsTests
{
    private static mobjtype_t[] AllTypes => [.. Enumerable.Range(0, (int)mobjtype_t.NUMMOBJTYPES).Select(i => (mobjtype_t)i)];

    private static bool Special(mobjtype_t t) => (Info.mobjinfo[(int)t].flags & mobjflag_t.MF_SPECIAL) != 0;

    [Fact]
    public void EveryPickupHasAKindAndNothingElseDoes()
    {
        foreach (mobjtype_t t in AllTypes)
            Assert.True(Special(t) == (ItemKinds.KindOf(t) != ItemKind.None), $"{t}: MF_SPECIAL {Special(t)}, kind {ItemKinds.KindOf(t)}");
        Assert.Equal(36, AllTypes.Count(Special)); // info.c's pickups: 6 keys, 7 weapons, 9 power-ups, 3 armor, 3 health, 8 ammo
    }

    [Fact]
    public void KindsFollowTheSprites()
    {
        // p_inter.c P_TouchSpecialThing sorts the pickups by sprite.
        (spritenum_t Sprite, ItemKind Kind)[] expected =
        [
            (spritenum_t.SPR_BKEY, ItemKind.BlueKey), (spritenum_t.SPR_BSKU, ItemKind.BlueKey),
            (spritenum_t.SPR_YKEY, ItemKind.YellowKey), (spritenum_t.SPR_YSKU, ItemKind.YellowKey),
            (spritenum_t.SPR_RKEY, ItemKind.RedKey), (spritenum_t.SPR_RSKU, ItemKind.RedKey),
            (spritenum_t.SPR_SHOT, ItemKind.Weapon), (spritenum_t.SPR_SGN2, ItemKind.Weapon), (spritenum_t.SPR_MGUN, ItemKind.Weapon),
            (spritenum_t.SPR_LAUN, ItemKind.Weapon), (spritenum_t.SPR_PLAS, ItemKind.Weapon), (spritenum_t.SPR_BFUG, ItemKind.Weapon), (spritenum_t.SPR_CSAW, ItemKind.Weapon),
            (spritenum_t.SPR_SOUL, ItemKind.PowerUp), (spritenum_t.SPR_MEGA, ItemKind.PowerUp), (spritenum_t.SPR_PINV, ItemKind.PowerUp),
            (spritenum_t.SPR_PSTR, ItemKind.PowerUp), (spritenum_t.SPR_PINS, ItemKind.PowerUp), (spritenum_t.SPR_SUIT, ItemKind.PowerUp),
            (spritenum_t.SPR_PMAP, ItemKind.PowerUp), (spritenum_t.SPR_PVIS, ItemKind.PowerUp), (spritenum_t.SPR_BPAK, ItemKind.PowerUp),
            (spritenum_t.SPR_ARM1, ItemKind.Armor), (spritenum_t.SPR_ARM2, ItemKind.Armor), (spritenum_t.SPR_BON2, ItemKind.Armor),
            (spritenum_t.SPR_BON1, ItemKind.Health), (spritenum_t.SPR_STIM, ItemKind.Health), (spritenum_t.SPR_MEDI, ItemKind.Health),
            (spritenum_t.SPR_CLIP, ItemKind.Ammo), (spritenum_t.SPR_AMMO, ItemKind.Ammo), (spritenum_t.SPR_ROCK, ItemKind.Ammo), (spritenum_t.SPR_BROK, ItemKind.Ammo),
            (spritenum_t.SPR_CELL, ItemKind.Ammo), (spritenum_t.SPR_CELP, ItemKind.Ammo), (spritenum_t.SPR_SHEL, ItemKind.Ammo), (spritenum_t.SPR_SBOX, ItemKind.Ammo),
        ];
        foreach (mobjtype_t t in AllTypes.Where(Special))
        {
            spritenum_t sprite = Info.states[(int)Info.mobjinfo[(int)t].spawnstate].sprite;
            Assert.Contains((sprite, ItemKinds.KindOf(t)), expected);
        }
    }

    [Fact]
    public void KeysModeKeepsKeysWeaponsAndPowerUps()
    {
        foreach (ItemKind kind in Enum.GetValues<ItemKind>())
        {
            bool none = kind == ItemKind.None;
            bool kept = kind is ItemKind.BlueKey or ItemKind.YellowKey or ItemKind.RedKey or ItemKind.Weapon or ItemKind.PowerUp;
            Assert.Equal(!none, ItemKinds.Beamed(kind, ItemBeamMode.All));
            Assert.Equal(kept, ItemKinds.Beamed(kind, ItemBeamMode.Keys));
            Assert.False(ItemKinds.Beamed(kind, ItemBeamMode.Off));
        }
    }

    [Fact]
    public void KeysShowTheirColours()
    {
        static void Dominant((float R, float G, float B) c, int channel)
        {
            float[] v = [c.R, c.G, c.B];
            Assert.True(v[channel] >= 0.9f, $"{c}");
            Assert.True(v.Where((x, i) => i != channel).All(x => x < v[channel]), $"{c}");
        }
        Dominant(ItemKinds.ColorOf(ItemKind.BlueKey), 2);
        Dominant(ItemKinds.ColorOf(ItemKind.RedKey), 0);
        (float r, float g, float b) = ItemKinds.ColorOf(ItemKind.YellowKey);
        Assert.True(r > 0.9f && g > 0.8f && b < 0.3f, $"yellow {(r, g, b)}");
    }

    [Fact]
    public void ColoursDifferFromEachOtherAndTheTracers()
    {
        // ShotTracers.PlayerColor and MonsterColor (Godot types, not compiled here).
        (float, float, float)[] tracers = [(1f, 0.95f, 0.6f), (1f, 0.35f, 0.2f)];
        ItemKind[] kinds = [.. Enum.GetValues<ItemKind>().Where(k => k != ItemKind.None)];
        static float Distance((float R, float G, float B) a, (float R, float G, float B) b) =>
            MathF.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));
        foreach (ItemKind a in kinds)
        {
            foreach (ItemKind b in kinds.Where(b => b > a))
                Assert.True(Distance(ItemKinds.ColorOf(a), ItemKinds.ColorOf(b)) > 0.3f, $"{a} and {b}");
            foreach ((float, float, float) t in tracers)
                Assert.True(Distance(ItemKinds.ColorOf(a), t) > 0.2f, $"{a} and the tracer {t}");
        }
    }

    [Fact]
    public void ModesParseAndName()
    {
        foreach (ItemBeamMode mode in Enum.GetValues<ItemBeamMode>())
            Assert.Equal(mode, ItemKinds.Parse(ItemKinds.Name(mode), "test"));
        Assert.Equal(ItemBeamMode.Off, ItemKinds.Parse("vanilla", "test"));
        Assert.Throws<ArgumentException>(() => ItemKinds.Parse("some", "test"));
    }
}
