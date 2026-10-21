using System;
using System.Collections.Generic;
using System.Diagnostics;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Render;

/// <summary>
/// T6.14c (SPEC §12 "T6.14 Player weapons"): every composite of a WAD, one per weapon slot
/// (<see cref="weapontype_t"/> order; the fist's empty hands, the pistol our own art) and
/// <c>PLAY</c> lump of frames A–G that has a pose. A weapon whose art the WAD lacks gets no
/// composites; a lump without a pose (a PWAD's) none either: both are drawn stock. Built at
/// once, timed (<see cref="BuildMilliseconds"/>) against SPEC §9's 1 s load budget. No
/// Godot types.
/// </summary>
public sealed class HeldWeaponSet
{
    private readonly Dictionary<string, HeldComposite>[] _composites;

    private HeldWeaponSet(Dictionary<string, HoldPose> poses, HeldWeapon?[] weapons, Dictionary<string, HeldComposite>[] composites, double milliseconds)
    {
        Poses = poses;
        Weapons = weapons;
        _composites = composites;
        BuildMilliseconds = milliseconds;
    }

    /// <summary>The poses, keyed by lump name.</summary>
    public IReadOnlyDictionary<string, HoldPose> Poses { get; }

    /// <summary>The held weapons, in <see cref="weapontype_t"/> order (null: the fist, or no art).</summary>
    public IReadOnlyList<HeldWeapon?> Weapons { get; }

    /// <summary>How long <see cref="Build"/> took, poses and weapons loaded included.</summary>
    public double BuildMilliseconds { get; }

    /// <summary>The number of composites built.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            foreach (Dictionary<string, HeldComposite> c in _composites)
                n += c.Count;
            return n;
        }
    }

    /// <summary>Whether slot <paramref name="weapon"/> has composites (the fist always, given poses; another weapon when the WAD has its art).</summary>
    public bool Has(weapontype_t weapon) => (uint)weapon < (uint)_composites.Length && _composites[(int)weapon].Count > 0;

    /// <summary>The composite of <paramref name="lump"/> holding <paramref name="weapon"/>, or null (drawn stock).</summary>
    public HeldComposite? Get(weapontype_t weapon, string lump) =>
        (uint)weapon < (uint)_composites.Length && _composites[(int)weapon].TryGetValue(lump, out HeldComposite? c) ? c : null;

    private static double Reach(HoldPose p)
    {
        double dx = p.Muzzle.X - p.Grip.X, dy = p.Muzzle.Y - p.Grip.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// The stretch along the weapon's axis for lump <paramref name="name"/>: the stock rifle's
    /// grip-to-muzzle distance there over its distance in the aiming side view
    /// (<c>PLAYE3E7</c>), against which the weapons' scales were picked; frame F's lumps take
    /// frame E's (the flash ends their rifle short). The art draws the walking frames' rifle
    /// across the chest with the hands twice as far apart as the side view's (<c>PLAYA1</c>: 38
    /// texels, <c>PLAYE3E7</c>: 20), so a weapon at its own length alone would stop short of the
    /// forward hand; end on (the side views of the walking frames, the views pointing at the
    /// camera) the distance shrinks, which is the foreshortening. Above 1 only the part ahead
    /// of the grip stretches (<see cref="HeldWeaponComposer"/>). Null (the pose's own
    /// foreshortening) when the WAD lacks either pose.
    /// </summary>
    public static double? Along(IReadOnlyDictionary<string, HoldPose> poses, string name)
    {
        ArgumentNullException.ThrowIfNull(poses);
        ArgumentNullException.ThrowIfNull(name);
        if (!poses.TryGetValue("PLAYE3E7", out HoldPose? reference))
            return null;
        string own = name[4] == 'F' ? "PLAYE" + name[5..].Replace('F', 'E') : name;
        if (!poses.TryGetValue(own, out HoldPose? pose))
            return null;
        return Reach(pose) / Reach(reference);
    }

    /// <summary>
    /// Builds every composite of <paramref name="wad"/>. <paramref name="log"/> gets
    /// <see cref="HoldPoses.Load"/>'s line about lumps drawn stock, if any.
    /// </summary>
    public static HeldWeaponSet Build(WadArchive wad, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(wad);
        long start = Stopwatch.GetTimestamp();
        Dictionary<string, HoldPose> poses = HoldPoses.Load(wad, log);
        HeldWeapon?[] weapons = HeldWeapons.Load(wad, Playpal.Load(wad).Data.ToArray());
        var images = new Dictionary<string, IndexedImage>(StringComparer.Ordinal);
        foreach (string name in poses.Keys)
        {
            WadLump lump = wad.Find(name, LumpNamespace.Sprites) ?? wad.Find(name)!;
            images[name] = Patch.Decode(lump.Data.Span, name);
        }
        var composites = new Dictionary<string, HeldComposite>[(int)weapontype_t.NUMWEAPONS];
        for (int slot = 0; slot < composites.Length; slot++)
        {
            composites[slot] = new Dictionary<string, HeldComposite>(StringComparer.Ordinal);
            HeldWeapon? weapon = weapons[slot];
            if (weapon is null && slot != (int)weapontype_t.wp_fist)
                continue;
            foreach (string name in HoldPoses.Lumps)
            {
                if (poses.TryGetValue(name, out HoldPose? pose))
                    composites[slot][name] = HeldWeaponComposer.ComposeDetailed(images[name], pose, weapon, Along(poses, name));
            }
        }
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new HeldWeaponSet(poses, weapons, composites, ms);
    }
}
