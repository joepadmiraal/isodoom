using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

// T6.14d: every player's sprite holds its ready weapon (SPEC §12 "T6.14 Player
// weapons"). Once per WAD: the synthetic sprites (#PW0–#PW8) against the
// HeldWeaponSet: one per weapon slot with composites, frames A–G laid out as
// PLAY's (each rotation slot the composite of PLAY's lump there, mirrored as
// it, or the stock lump without a pose), each composite's atlas texels and
// offsets the composite's. On every map, headless: the player's entry, made
// ready with each weapon in turn, is that weapon's synthetic sprite in every
// frame A–G and rotation, the composite of the lump PLAY shows there (through
// PlayerSprite, as the game draws it); stock with the option off, in frames
// H–W (dying, dead) and for a mobj without a player; the voodoo dolls hold
// their player's weapon; the level script's playerweapon passes. The
// teammates' entries in the co-op section (LevelCheck.Players.cs). With a
// real renderer, on the check's map: the player holding the shotgun, the
// rocket launcher and the BFG side-on, every pixel against the composite
// through the CPU light (CompareSprite), in front, side and back views and
// firing (full bright); one fuzzed (the blur sphere) against the CPU fuzz,
// and one in a teammate's translation.
public partial class LevelCheck
{
    private bool _heldAtlasChecked;
    private int _playerWeaponMaps, _playerWeaponEntries, _playerWeaponPixels;

    /// <summary>T6.14d, once per WAD: the synthetic sprites and their atlas slots against the <see cref="HeldWeaponSet"/>.</summary>
    private void CheckHeldAtlas()
    {
        if (_heldAtlasChecked || _scene.SpriteAtlas is not { } atlas || _scene.Wad is not { } wad)
            return;
        _heldAtlasChecked = true;
        HeldWeaponSet? held = atlas.Held;
        SpriteDef? play = atlas.Sprites.Find("PLAY");
        if (held is null)
        {
            Fail("player weapons: the sprite atlas has no composites (HeldWeaponSet)");
            return;
        }
        int sprites = 0, stockSlots = 0;
        var slots = new HashSet<int>();
        foreach (weapontype_t weapon in _allWeapons)
        {
            int sprite = atlas.HeldSprite(weapon);
            bool expected = held.Has(weapon) && play is not null && play.NumFrames >= SpriteAtlas.HeldFrames;
            if ((sprite >= 0) != expected)
            {
                Fail($"player weapons: {weapon}: synthetic sprite {sprite}, composites {held.Has(weapon)}");
                continue;
            }
            if (sprite < 0)
                continue;
            sprites++;
            SpriteDef def = atlas.Sprites.SpriteDefs[sprite];
            if (sprite < _scene.Sprites!.NumSprites || def.Name != $"#PW{(int)weapon}" || def.NumFrames != SpriteAtlas.HeldFrames || atlas.HeldWeaponOf(sprite) != weapon)
            {
                Fail($"player weapons: {weapon}: sprite {sprite} ({def.Name}, {def.NumFrames} frames) is not #PW{(int)weapon} after the WAD's {_scene.Sprites.NumSprites} sprites with frames A-G");
                continue;
            }
            for (int f = 0; f < SpriteAtlas.HeldFrames; f++)
            {
                SpriteFrame stock = play!.Frames[f], frame = def.Frames[f];
                if (frame.Rotate != stock.Rotate)
                    Fail($"player weapons: {def.Name} {(char)('A' + f)}: rotate {frame.Rotate}, PLAY's {stock.Rotate}");
                for (int r = 0; r < 8; r++)
                {
                    string name = wad.Lumps[stock.Lump[r]].Name;
                    int slot = atlas.SlotOf(frame.Lump[r]);
                    string what = $"player weapons: {def.Name} {(char)('A' + f)}{r + 1} ({name})";
                    if (frame.Flip[r] != stock.Flip[r])
                        Fail($"{what}: flip {frame.Flip[r]}, PLAY's {stock.Flip[r]}");
                    if (held.Get(weapon, name) is { } composite)
                    {
                        IndexedImage image = composite.Image;
                        if (slot < 0 || atlas.HeldSource(slot) != (weapon, name) || atlas.Lumps[slot] < wad.NumLumps)
                        {
                            Fail($"{what}: slot {slot} ({atlas.HeldSource(slot)}) is not the composite's");
                            continue;
                        }
                        slots.Add(slot);
                        IndexedImage drawn = atlas.Images[slot];
                        AtlasRect rect = atlas.Atlas.Rects[slot];
                        if (drawn != image || rect.Width != image.Width || rect.Height != image.Height
                            || atlas.InfoTexel(2 * slot) != new Color(rect.X, rect.Y, rect.Width, rect.Height)
                            || atlas.InfoTexel(2 * slot + 1) != new Color(image.LeftOffset, image.TopOffset, 0, 0))
                        {
                            Fail($"{what}: the atlas slot {slot} ({rect}, info {atlas.InfoTexel(2 * slot + 1)}) is not the composite ({image.Width}x{image.Height}, offsets {image.LeftOffset}, {image.TopOffset})");
                            continue;
                        }
                        for (int y = 0; y < image.Height; y++)
                        {
                            for (int x = 0; x < image.Width; x++)
                            {
                                if (atlas.Atlas.Image.IsOpaque(rect.X + x, rect.Y + y) != image.IsOpaque(x, y)
                                    || (image.IsOpaque(x, y) && atlas.Atlas.Image[rect.X + x, rect.Y + y] != image[x, y]))
                                {
                                    Fail($"{what}: atlas texel ({x},{y}) differs from the composite");
                                    y = image.Height;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        stockSlots++;
                        if (frame.Lump[r] != stock.Lump[r])
                            Fail($"{what}: no composite, but not the stock lump");
                    }
                }
            }
        }
        if (sprites > 0 && slots.Count != held.Count)
            Fail($"player weapons: {slots.Count} composite slots in the synthetic sprites, the set has {held.Count}");
        GD.Print($"Level check: player weapons (T6.14d): {sprites} synthetic sprites after the WAD's, {slots.Count} composites in the atlas against the HeldWeaponSet "
            + $"(built in {held.BuildMilliseconds:F0} ms), {stockSlots} rotation slots stock (no pose)");
    }

    /// <summary>T6.14d, on every map: the player's entry with each weapon, frame and rotation; stock cases; the voodoo dolls; the script's lines.</summary>
    private void CheckPlayerWeaponState(string map)
    {
        CheckHeldAtlas();
        if (_scene.World is not { } world || _scene.PlayerMobj is not { } me || _scene.SpriteAtlas is not { } atlas || _scene.Mesh is not { } m
            || atlas.Sprites.Find("PLAY") is not { } play || _scene.Wad is not { } wad)
            return;
        _playerWeaponMaps++;
        player_t p = world.players[world.consoleplayer];
        if (!_scene.PlayerWeapon)
            Fail($"{map}: the player weapon option is off by default");
        weapontype_t ready = p.readyweapon;
        var player = new PlayerSprite();
        player.Bind(atlas, m.SpriteMaterial);
        ThingSprites.Entry stock = LevelScene.ThingEntry(me);
        try
        {
            foreach (weapontype_t weapon in _allWeapons)
            {
                p.readyweapon = weapon;
                int sprite = atlas.HeldSprite(weapon);
                for (int f = 0; f < play.NumFrames; f++)
                {
                    ThingSprites.Entry e = _scene.WithHeldWeapon(me, stock with { Frame = f });
                    int expected = f < SpriteAtlas.HeldFrames && sprite >= 0 ? sprite : PlayerSprite.SprPlay;
                    if (e != (stock with { Frame = f, Sprite = expected }))
                    {
                        Fail($"{map}: {weapon}, frame {(char)('A' + f)}: entry sprite {e.Sprite}, expected {expected}");
                        continue;
                    }
                    if (sprite < 0 || f >= SpriteAtlas.HeldFrames)
                        continue;
                    // Through the player's billboard in each rotation: the composite of the lump PLAY shows there.
                    for (int r = 0; r < 8; r++)
                    {
                        uint ang = player.Sprite.ViewAngle(0);
                        player.Set(e with { Angle = unchecked(ang + (uint)(4 - r) * 0x20000000u) });
                        ThingSprites.Shown shown = player.Sprite.ShownFrames[0];
                        string name = wad.Lumps[play.Frames[f].Lump[r]].Name;
                        bool composite = atlas.Held!.Get(weapon, name) is not null;
                        if (shown.Rot != r || shown.Flip != play.Frames[f].Flip[r]
                            || (composite ? atlas.HeldSource(shown.Slot) != (weapon, name) : shown.Slot != atlas.SlotOf(play.Frames[f].Lump[r])))
                            Fail($"{map}: {weapon}, frame {(char)('A' + f)} rotation {r + 1}: the player's billboard shows slot {shown.Slot} ({atlas.HeldSource(shown.Slot)}) rotation {shown.Rot + 1}, expected {name}{(composite ? "'s composite" : " stock")}");
                        _playerWeaponEntries++;
                    }
                }
            }
            // Stock: the option off; a mobj without a player (a corpse left by a co-op reborn).
            p.readyweapon = weapontype_t.wp_shotgun;
            _scene.PlayerWeapon = false;
            if (_scene.WithHeldWeapon(me, stock with { Frame = 0 }) != (stock with { Frame = 0 }))
                Fail($"{map}: the player holds its weapon with the option off");
            _scene.PlayerWeapon = true;
            player_t? owner = me.player;
            me.player = null;
            if (_scene.WithHeldWeapon(me, stock with { Frame = 0 }) != (stock with { Frame = 0 }))
                Fail($"{map}: a mobj without a player holds a weapon");
            me.player = owner;
            // The voodoo dolls (MT_PLAYER mobjs of extra player 1 starts) are drawn holding their player's weapon.
            _scene.PresentWorld();
            if (_scene.Things is { } things)
            {
                for (int i = 0; i < _scene.DrawnMobjs.Count && i < things.Entries.Count; i++)
                {
                    mobj_t mo = _scene.DrawnMobjs[i];
                    if (mo.player is null)
                        continue;
                    ThingSprites.Entry e = things.Entries[i];
                    int expected = (mo.frame & Info.FF_FRAMEMASK) < SpriteAtlas.HeldFrames && mo.sprite == spritenum_t.SPR_PLAY && atlas.HeldSprite(mo.player.readyweapon) >= 0
                        ? atlas.HeldSprite(mo.player.readyweapon) : (int)mo.sprite;
                    if (e.Sprite != expected)
                        Fail($"{map}: voodoo doll {i}: drawn sprite {e.Sprite}, expected {expected} (its player's {mo.player.readyweapon})");
                }
            }
            // The level script's playerweapon: the player as the game would draw it (no billboard in the check: its entry).
            foreach ((string line, bool fails) in _scene.PlayerWeaponLines())
            {
                if (fails)
                    Fail($"{map}: playerweapon: {line}");
            }
        }
        finally
        {
            p.readyweapon = ready;
            _scene.PlayerWeapon = true;
            _scene.PresentWorld();
            player.Sprite.Free(); // not a child until _Ready
            player.Free();
        }
    }

    /// <summary>T11.10 with T6.14d: player <paramref name="i"/>'s billboard among the things holds its ready weapon (another than the local player's).</summary>
    private void CheckTeammateWeapon(World world, string what, int i, int k)
    {
        if (_scene.Things is not { } things || _scene.SpriteAtlas is not { } atlas || world.players[i] is not { mo: { } mo } q)
            return;
        weapontype_t ready = q.readyweapon;
        weapontype_t weapon = _allWeapons.FirstOrDefault(w => w != world.players[k].readyweapon && w != weapontype_t.wp_fist && atlas.HeldSprite(w) >= 0, weapontype_t.wp_fist);
        q.readyweapon = weapon;
        _scene.PresentWorld();
        int at = _scene.DrawnMobjs.ToList().IndexOf(mo);
        int frame = mo.frame & Info.FF_FRAMEMASK;
        int expected = mo.sprite == spritenum_t.SPR_PLAY && frame < SpriteAtlas.HeldFrames && atlas.HeldSprite(weapon) >= 0 ? atlas.HeldSprite(weapon) : (int)mo.sprite;
        if (at >= 0 && things.Entries[at].Sprite != expected)
            Fail($"{what}: player {i + 1}'s billboard: sprite {things.Entries[at].Sprite}, expected {expected} (holding {weapon})");
        q.readyweapon = ready;
        _scene.PresentWorld();
    }

    /// <summary>
    /// T6.14d, with a real renderer: thing <paramref name="i"/> shown as the player holding the
    /// shotgun, the rocket launcher and the BFG (those the WAD has), every pixel against the
    /// composite through the CPU light (<see cref="CompareSprite"/>): front, side and back
    /// views of frame A, and frame F side-on full bright; then one fuzzed in front of
    /// the widest other picked thing (<see cref="CompareFuzz"/>) and one in translation 2.
    /// </summary>
    private async Task<int> CheckPlayerWeaponPixels(LevelMesh m, ThingSprites things, SpriteAtlas atlas, int i, List<int> picked, Basis basis)
    {
        string map = m.Level.Name;
        if (atlas.Held is not { } held || _scene.Wad is not { } wad)
            return 0;
        ThingSprites.Entry original = things.Entries[i];
        uint ang = things.ViewAngle(i);
        int compared = 0, views = 0;
        ThingSprites.Entry? last = null;
        weapontype_t lastWeapon = weapontype_t.wp_fist;
        foreach (weapontype_t weapon in new[] { weapontype_t.wp_shotgun, weapontype_t.wp_missile, weapontype_t.wp_bfg })
        {
            int sprite = atlas.HeldSprite(weapon);
            if (sprite < 0)
                continue;
            foreach ((int frame, int rot, bool bright) in new[] { (0, 0, false), (0, 2, false), (0, 4, false), (5, 2, true) })
            {
                ThingSprites.Entry e = original with
                {
                    Sprite = sprite,
                    Frame = frame,
                    FullBright = bright,
                    Shadow = false,
                    SightDim = 0,
                    Own = false,
                    Translation = 0,
                    Angle = unchecked(ang + (uint)(4 - rot) * 0x20000000u),
                };
                things.SetEntry(i, e);
                ThingSprites.Shown shown = things.ShownFrames[i];
                SpriteFrame stock = _scene.Sprites!.Find("PLAY")!.Frames[frame];
                string name = wad.Lumps[stock.Lump[shown.Rot]].Name;
                string what = $"{map}: thing {i} as the player holding {weapon} ({name}{(shown.Flip ? " mirrored" : "")})";
                if (held.Get(weapon, name) is not { } composite || atlas.HeldSource(shown.Slot) != (weapon, name) || atlas.Images[shown.Slot] != composite.Image)
                {
                    Fail($"{what}: shows slot {shown.Slot} ({atlas.HeldSource(shown.Slot)}), not the composite");
                    continue;
                }
                compared += await CompareSprite(m, things, atlas, i, basis, what);
                views++;
                if (!bright)
                    (last, lastWeapon) = (e, weapon);
            }
        }
        if (last is { } held1)
        {
            // Translated as a teammate's (the art is ramp-safe: the body's green changes, the weapon not).
            things.SetEntry(i, held1 with { Translation = 2 });
            compared += await CompareSprite(m, things, atlas, i, basis, $"{map}: thing {i} as the player holding {lastWeapon}, translation 2");
            views++;
            // Fuzzed (the blur sphere) in front of the widest other compared thing.
            int j = -1, widest = 0;
            foreach (int k in picked)
            {
                int width = atlas.Images[things.ShownFrames[k].Slot].Width;
                if (k != i && width > widest)
                    (j, widest) = (k, width);
            }
            if (j >= 0)
            {
                ThingSprites.Entry behind = things.Entries[j];
                Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
                things.SetEntry(j, behind with { MapPosition = held1.MapPosition - toCamera * 64, Sector = held1.Sector });
                int phase = m.FuzzPhase;
                m.SetFuzzPhase(17);
                things.SetEntry(i, held1 with { Shadow = true });
                compared += await CompareFuzz(m, things, atlas, i, j, basis, $"{map}: thing {i} as the player holding {lastWeapon}, fuzzed in front of thing {j}");
                views++;
                m.SetFuzzPhase(phase);
                things.SetEntry(j, behind);
            }
        }
        things.SetEntry(i, original);
        things.Isolate(null);
        _playerWeaponPixels += compared;
        if (views == 0 && held.Count > 0 && (held.Has(weapontype_t.wp_shotgun) || held.Has(weapontype_t.wp_missile)))
            Fail($"{map}: no player weapon view compared");
        GD.Print($"Level check: {map}: player weapons (T6.14d): {views} views of the player holding its weapon (the shotgun, rocket launcher and BFG the WAD has; front, side, back, firing; translated; fuzzed) against the composites: {compared} pixels compared");
        return compared;
    }

    private void PrintPlayerWeapons() =>
        GD.Print($"Level check: player weapons (T6.14d): {_playerWeaponMaps} maps drew the player holding each weapon the WAD has art for in frames A-G "
            + $"({_playerWeaponEntries} frame and rotation entries through the player's billboard, each the composite of PLAY's lump there), stock with the option off, "
            + "in frames H-W and without a player; voodoo dolls holding their player's weapon");
}
