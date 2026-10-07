using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

// T5.1: the level's run-time data from the sim. On every map (no renderer
// needed): a sector moved in the sim between tics is drawn at its
// interpolated heights (the data texel at tic fractions 0, ½ and 1) and its
// things fit again (P_ChangeSector); a wall texture changed at run time
// (through its Side, as a switch does: to the other texture of its switch
// pair when it has one, else to another of the atlas's textures, of another
// height when there is one) re-points its side_textures texel, and the
// chunks' attributes evaluate to the new texture's span and anchor; a floor
// flat change re-points the sector's slot. Every change is put back. With a
// real renderer, the rendered map's changed wall is also compared side-on
// with the new texture.
// T5.7: on every map a wall's sidedef scrolled through its textureoffset
// moves its side_textures scroll (and back); after the tics the check runs,
// every animation frame's slot draws the frame the sim's translation names
// and every scrolling wall (special 48) is scrolled as far as the sim moved
// it. With a real renderer, a wall translated to another texture (as an
// animation re-points a slot) and a wall scrolled are compared side-on.
// T5.8: on every map with a use exit (special 11), the player uses it: the
// scene goes on to the next map in the same world, the player keeping its
// health and losing its keys, or stops with the reason when the game ends or
// the WAD lacks the next map; a message the sim leaves shows in the overlay.
public partial class LevelCheck
{
    private int _exitMaps, _exitsToNext, _exitsEnded;

    /// <summary>
    /// T5.8: the player uses the map's first use exit (special 11) from 24
    /// (16, 8) units in front of it; the level scene must then show the next
    /// map (<see cref="World.NextMapName"/>) in the same world, with the
    /// player's health kept, its keys taken and the overlay's status line,
    /// or stop with <see cref="LevelScene.LevelEnded"/>. Leaves the scene
    /// on whatever map it got to (call it last for a map).
    /// </summary>
    private void CheckExit(string map)
    {
        if (_scene.World is not { } world || _scene.PlayerMobj is not { } me)
            return;
        foreach (line_t line in world.lines)
        {
            if (line.special != 11 || line.frontsector is null)
                continue;
            double dx = line.dx / 65536.0, dy = line.dy / 65536.0, len = Math.Sqrt(dx * dx + dy * dy);
            double nx = dy / len, ny = -dx / len;
            int fx = 0, fy = 0;
            bool found = false;
            foreach (int d in new[] { 24, 16, 8 })
            {
                double sx = line.v1.X / 65536.0 + dx / 2 + nx * d, sy = line.v1.Y / 65536.0 + dy / 2 + ny * d;
                (fx, fy) = ((int)Math.Round(sx * 65536), (int)Math.Round(sy * 65536));
                if (found = world.R_PointInSubsector(fx, fy).sector == line.frontsector)
                    break;
            }
            if (!found)
                continue;
            uint facing = (uint)(long)Math.Round(Math.Atan2(-ny, -nx) / (2 * Math.PI) * 4294967296.0);
            player_t p = world.players[world.consoleplayer];
            p.health = me.health = 77;
            p.cards[(int)card_t.it_redskull] = true;
            p.message = World.GOTREDSKULL;
            world.S_StartSound((mobj_t?)null, sfxenum_t.sfx_getpow); // T6.10: queued before the tic, drained after it
            world.PlaceMobj(me, fx, fy, facing);
            var cmd = new ticcmd_t { angleturn = _scene.Tweaks.AbsoluteAiming ? Ticcmds.AbsoluteAngle(facing) : (short)0 };
            int completed = _scene.LevelsCompleted;
            _scene.Tic(cmd); // releases use (held since the spawn)
            if (_scene.HudMessage != World.GOTREDSKULL || !_scene.OverlayText().Contains("message: " + World.GOTREDSKULL, StringComparison.Ordinal)
                || !_scene.OverlayText().Contains("health 77", StringComparison.Ordinal)
                || !_scene.SoundLog.Any(l => l.Sound.sfx == sfxenum_t.sfx_getpow)
                || !_scene.OverlayText().Split("\n").Any(l => l.StartsWith("sounds: ", StringComparison.Ordinal) && l.Contains("getpow", StringComparison.Ordinal)))
                Fail($"{map}: the player's message, health and sound are not in the overlay:\n{_scene.OverlayText()}");
            cmd.buttons = buttoncode_t.BT_USE;
            _scene.Tic(cmd);
            if (_scene.LevelsCompleted == completed)
                continue; // the use hit something else: try another exit
            _exitMaps++;
            if (_scene.LevelEnded is not null)
            {
                _exitsEnded++;
                if (_scene.Mesh?.Level.Name != map || _scene.World != world)
                    Fail($"{map}: the game ended ({_scene.LevelEnded}) but the scene left the map");
                return;
            }
            _exitsToNext++;
            string next = world.NextMapName();
            if (_scene.Mesh?.Level.Name != next || _scene.World != world || world.level != _scene.Mesh.Level
                || world.gameaction != gameaction_t.ga_nothing || world.leveltime != 0 || _scene.PlayerMobj is not { } mo2
                || p.health != 77 || mo2.health != 77 || p.cards.Any(c => c) || _scene.HudMessage is not null
                || _scene.StatusBar is { } st && (st.plyr != p || !st.st_firsttime || st.keyboxes.Any(k => k != -1))) // T6.11: ST_Start for the new map
                Fail($"{map}: line {line.Index}'s exit: the scene shows {_scene.Mesh?.Level.Name} (expected {next}), same world {_scene.World == world}, "
                    + $"gameaction {world.gameaction}, leveltime {world.leveltime}, health {p.health}, keys {string.Join(",", p.cards)}");
            return;
        }
    }

    private int _switchPairs, _textureChanges, _flatChanges, _interpolatedSectors;
    private int _animSequences, _animatedMaps, _translatedSlots, _scrollingWalls, _scrollChecks;

    /// <summary>The <c>side_textures</c> upload read back from the GPU against the CPU copy.</summary>
    private void CheckSideTexturesUpload(LevelMesh m, string map)
    {
        Image? gpu = m.SideTexturesTexture.GetImage();
        if (gpu is null || gpu.GetFormat() != Image.Format.Rgf)
        {
            Fail($"{map}: side_textures can't be read back as RGF");
            return;
        }
        for (int side = 0; side < m.Level.Sides.Length; side++)
        {
            for (int part = 0; part < 3; part++)
            {
                int id = 3 * side + part;
                Color texel = gpu.GetPixel(id % LevelMesh.DataWidth, id / LevelMesh.DataWidth);
                if ((int)MathF.Round(texel.R) != m.SideTextureSlot(side, part) || texel.G != m.SideScroll(side, part))
                {
                    Fail($"{map}: side {side} part {part}: the GPU side_textures texel differs");
                    return;
                }
            }
        }
    }

    /// <summary>
    /// A drawn wall section (side-on checkable when <paramref name="sideOn"/>)
    /// and a texture to change it to: its switch pair's other texture when it
    /// has one, else another wall texture of the atlas, preferring one of
    /// another height and a section with a pegged texture bottom (so the anchor moves).
    /// </summary>
    private (WallSection Section, int Texture)? PickTextureChange(LevelMesh m, bool sideOn)
    {
        var partner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IReadOnlyList<string> group in LevelScene.SwitchGroups(_scene.GameMode))
        {
            partner[group[0]] = group[1];
            partner[group[1]] = group[0];
        }
        var wallTextures = new List<int>();
        for (int slot = 0; slot < m.SlotNames.Count; slot++)
        {
            int t = Textures.R_CheckTextureNumForName(m.SlotNames[slot]);
            if (t > 0 && m.TextureSlot(t) == slot)
                wallTextures.Add(t);
        }
        (WallSection, int)? best = null;
        int bestScore = -1;
        foreach (WallSection s in m.Walls.Sections)
        {
            if (!LevelMesh.IsDrawn(s) || (sideOn && (!IsSideOnCheckable(s) || LevelMesh.IsMasked(s) || s.Span().Top - s.Span().Bottom < 8 << Fixed.FRACBITS)))
                continue;
            string name = Textures.TextureDefs[s.Texture].Name;
            if (partner.TryGetValue(name, out string? other) && Textures.R_CheckTextureNumForName(other) is > 0 and var p && m.TextureSlot(p) >= 0)
                return (s, p);
            foreach (int t in wallTextures)
            {
                if (t == s.Texture)
                    continue;
                int score = (Textures.TextureDefs[t].Height != Textures.TextureDefs[s.Texture].Height ? 2 : 0) + (s.BottomPegged ? 1 : 0);
                if (score > bestScore)
                    (best, bestScore) = ((s, t), score);
            }
        }
        return best;
    }

    /// <summary>Changes <paramref name="s"/>'s sidedef texture to <paramref name="texture"/> (as the sim does, by name) and returns the old name.</summary>
    private static string SetPartTexture(WallSection s, string texture)
    {
        IsoDoom.Map.Side side = s.SideDef;
        string old = LevelMesh.PartTexture(side, LevelMesh.Part(s.Kind));
        switch (LevelMesh.Part(s.Kind))
        {
            case LevelMesh.PartTop:
                side.TopTexture = texture;
                break;
            case LevelMesh.PartBottom:
                side.BottomTexture = texture;
                break;
            default:
                side.MidTexture = texture;
                break;
        }
        return old;
    }

    /// <summary>
    /// The CPU side of a run-time texture and flat change on <paramref name="map"/>
    /// (and the <c>side_textures</c> upload with a renderer): the texel follows
    /// the change and comes back, and the chunk attributes evaluate to the new
    /// texture's span and anchor.
    /// </summary>
    private void CheckTextureChangeData(LevelMesh m, string map)
    {
        if (PickTextureChange(m, sideOn: false) is not var (s, texture))
        {
            if (m.SlotNames.Count > 1 && m.WallQuads > 0)
                Fail($"{map}: no wall section and texture for a run-time texture change");
            return;
        }
        string name = Textures.TextureDefs[texture].Name;
        int part = LevelMesh.Part(s.Kind);
        int before = m.SideTextureSlot(s.SideDef.Index, part), misses = m.RuntimeMisses;
        string what = $"{map}: side {s.SideDef.Index} ({s.Kind} of line {s.Line.Index}) changed from {Textures.TextureDefs[s.Texture].Name} to {name} at run time";
        string old = SetPartTexture(s, name);
        m.UpdateSectors();
        if (m.SideTextureSlot(s.SideDef.Index, part) != m.TextureSlot(texture))
            Fail($"{what}: side_textures texel {m.SideTextureSlot(s.SideDef.Index, part)}, expected slot {m.TextureSlot(texture)}");
        if (CanCapture)
            CheckSideTexturesUpload(m, what);
        CheckChunks(m, what);

        // A flat change (EV_DoFloor's texture changes, T5.5): the sector data's slot follows.
        Sector sector = s.FrontSector;
        string oldFlat = sector.FloorPic;
        string? otherFlat = null;
        foreach (Sector o in m.Level.Sectors)
        {
            if (!string.Equals(o.FloorPic, oldFlat, StringComparison.OrdinalIgnoreCase))
            {
                otherFlat = o.FloorPic;
                break;
            }
        }
        if (otherFlat is not null)
        {
            sector.FloorPic = otherFlat;
            m.UpdateSectors();
            if ((int)m.SectorData(sector.Index).A != m.FlatSlot(otherFlat))
                Fail($"{map}: sector {sector.Index}'s floor changed to {otherFlat} at run time: data slot {m.SectorData(sector.Index).A}, expected {m.FlatSlot(otherFlat)}");
            sector.FloorPic = oldFlat;
            _flatChanges++;
        }

        SetPartTexture(s, old);
        m.UpdateSectors();
        if (m.SideTextureSlot(s.SideDef.Index, part) != before)
            Fail($"{what}: the texel did not come back ({m.SideTextureSlot(s.SideDef.Index, part)}, was {before})");
        if (m.RuntimeMisses != misses)
            Fail($"{what}: {m.RuntimeMisses - misses} miss(es) of the atlas");
        CheckSectorData(m, $"{map} (texture and flat back)");
        _textureChanges++;
    }

    /// <summary>
    /// T5.1: a sector moved in the sim during a tic is drawn between its
    /// heights before and after the tic (the data texel at tic fractions 0, ½
    /// and 1), and <see cref="World.P_ChangeSector"/> keeps the things on it on
    /// its floor. A mover is emulated: one tic runs (storing the heights), then
    /// the floor drops 8 units and the ceiling rises 8, as a thinker of that
    /// tic would leave them. Then everything goes back.
    /// </summary>
    private void CheckSectorInterpolation(LevelMesh m, string map)
    {
        if (_scene.World is not { } world || _scene.PlayerMobj is not { } me)
            return;
        sector_t sec = me.subsector.sector;
        _scene.Tic(new ticcmd_t());
        int floor = sec.floorheight, ceiling = sec.ceilingheight;
        bool onFloor = me.z == me.floorz;
        // Things near the sector that don't fit already (a monster stuck at its spawn) set nofit either way.
        bool stuck = world.P_ChangeSector(sec, false);
        const int step = 8 << Fixed.FRACBITS;
        sec.floorheight = floor - step;
        sec.ceilingheight = ceiling + step;
        if (world.P_ChangeSector(sec, false) && !stuck)
            Fail($"{map}: sector {sec.Index}: something no longer fits after its floor dropped and its ceiling rose");
        if (onFloor && (me.z != me.floorz || me.floorz > floor - step && SoleSector(world, me)))
            Fail($"{map}: the player on sector {sec.Index}'s floor is at {me.z >> Fixed.FRACBITS} (floorz {me.floorz >> Fixed.FRACBITS}) after it dropped to {sec.floorheight >> Fixed.FRACBITS} (P_ChangeSector)");
        foreach (double f in new[] { 0.0, 0.5, 1.0 })
        {
            _scene.SetTicFraction(f);
            _scene.PresentWorld();
            Color d = m.SectorData(sec.Index);
            float ef = (float)((floor - step * f) / 65536.0), ec = (float)((ceiling + step * f) / 65536.0);
            if (Math.Abs(d.R - ef) > 1e-3 || Math.Abs(d.G - ec) > 1e-3 || d.B != sec.lightlevel)
                Fail($"{map}: sector {sec.Index} at tic fraction {f}: data texel {d}, expected floor {ef}, ceiling {ec}, light {sec.lightlevel}");
        }
        sec.floorheight = floor;
        sec.ceilingheight = ceiling;
        sec.StoreInterpolation();
        world.P_ChangeSector(sec, false);
        _scene.SetTicFraction(0.5);
        _scene.PresentWorld();
        _scene.SetTicFraction(1);
        CheckSectorData(m, $"{map} (sector {sec.Index} back)");
        _interpolatedSectors++;
    }

    /// <summary>Whether <paramref name="mo"/>'s box touches no line (so its floor is its sector's alone).</summary>
    private static bool SoleSector(World world, mobj_t mo)
    {
        world.P_CheckPosition(mo, mo.x, mo.y);
        return world.tmfloorz == mo.subsector.sector.floorheight && world.tmceilingz == mo.subsector.sector.ceilingheight;
    }

    /// <summary>
    /// With a renderer: the rendered map's wall texture changed at run time
    /// (<see cref="PickTextureChange"/>, side-on checkable) is compared
    /// side-on with the new texture's texels, then changed back.
    /// </summary>
    private async Task CheckTextureChangeDrawn(LevelMesh m)
    {
        string map = m.Level.Name;
        if (PickTextureChange(m, sideOn: true) is not var (s, texture))
        {
            Fail($"{map}: no side-on wall section to change the texture of at run time");
            return;
        }
        string old = SetPartTexture(s, Textures.TextureDefs[texture].Name);
        m.UpdateSectors();
        int pixels = (await CheckWall(m, s, WallTextureTiling.Vanilla, map, texture: texture)).Pixels;
        SetPartTexture(s, old);
        m.UpdateSectors();
        if (pixels == 0)
            Fail($"{map}: line {s.Line.Index}'s {s.Kind} changed to {Textures.TextureDefs[texture].Name} at run time: no pixel compared");
        GD.Print($"Level check: {map}: line {s.Line.Index} side {s.Side} {s.Kind} changed from {Textures.TextureDefs[s.Texture].Name} to "
            + $"{Textures.TextureDefs[texture].Name} at run time{(s.BottomPegged ? " (texture bottom pegged)" : "")}: {pixels} drawn pixels compared");
    }

    /// <summary>
    /// T5.7: a drawn wall's sidedef scrolled through its <c>textureoffset</c>
    /// (as linedef special 48 does) moves its three <c>side_textures</c>
    /// texels' scroll by as much, and back.
    /// </summary>
    private void CheckScrollData(LevelMesh m, string map)
    {
        WallSection? s = null;
        foreach (WallSection o in m.Walls.Sections)
        {
            if (LevelMesh.IsDrawn(o))
            {
                s = o;
                break;
            }
        }
        if (s is null)
            return;
        IsoDoom.Map.Side side = s.SideDef;
        int before = side.TextureOffset;
        float scroll = m.SideScroll(side.Index);
        side.TextureOffset += 5 << Fixed.FRACBITS;
        m.UpdateSectors();
        for (int part = 0; part < 3; part++)
        {
            if (m.SideScroll(side.Index, part) != scroll + 5)
                Fail($"{map}: side {side.Index} scrolled 5 units: side_textures part {part} scroll {m.SideScroll(side.Index, part)}, expected {scroll + 5}");
        }
        if (CanCapture)
            CheckSideTexturesUpload(m, $"{map} (side {side.Index} scrolled)");
        side.TextureOffset = before;
        m.UpdateSectors();
        if (m.SideScroll(side.Index) != scroll)
            Fail($"{map}: side {side.Index}'s scroll did not come back ({m.SideScroll(side.Index)}, was {scroll})");
        _scrollChecks++;
    }

    /// <summary>
    /// T5.7, after the tics the check ran on the map: every frame of the
    /// world's animations the atlas holds draws (through <c>texture_info</c>)
    /// the frame the sim's <c>texturetranslation</c>/<c>flattranslation</c>
    /// names, and every scrolling wall's front side is scrolled by the sim's
    /// tics (a unit a tic since the level began).
    /// </summary>
    private void CheckAnimations(LevelMesh m, string map)
    {
        if (_scene.World is not { } world)
            return;
        _scene.PresentWorld();
        int translated = 0;
        for (int a = 0; a < world.lastanim; a++)
        {
            anim_t anim = world.anims[a]!;
            for (int i = anim.basepic; i < anim.basepic + anim.numpics; i++)
            {
                (int slot, int shows, string name, string to) = anim.istexture
                    ? (m.TextureSlot(i), m.TextureSlot(world.texturetranslation[i]), Textures.TextureDefs[i].Name, Textures.TextureDefs[world.texturetranslation[i]].Name)
                    : (m.FlatSlot(world.flatnames[i]), m.FlatSlot(world.flatnames[world.flattranslation[i]]), world.flatnames[i], world.flatnames[world.flattranslation[i]]);
                if (slot < 0)
                    continue;
                translated++;
                AtlasRect r = shows >= 0 ? m.Atlas.Rects[shows] : default;
                if (shows < 0 || m.SlotShows(slot) != shows || m.TextureInfo(slot) != new Color(r.X, r.Y, r.Width, r.Height))
                    Fail($"{map}: at tic {world.leveltime} {name} (slot {slot}) should draw {to} (slot {shows}): it draws slot {m.SlotShows(slot)}, texture_info {m.TextureInfo(slot)}");
            }
        }
        if (translated > 0)
        {
            _animatedMaps++;
            _translatedSlots += translated;
            if (CanCapture)
            {
                Image? info = m.TextureInfoTexture.GetImage();
                for (int i = 0; info is not null && i < m.Atlas.Rects.Count; i++)
                {
                    if (info.GetPixel(i % LevelMesh.DataWidth, i / LevelMesh.DataWidth) != m.TextureInfo(i))
                    {
                        Fail($"{map}: slot {i}: the GPU texture_info texel differs after the animations");
                        break;
                    }
                }
            }
        }
        for (int i = 0; i < world.numlinespecials; i++)
        {
            int side = world.linespeciallist[i]!.sidenum[0];
            if (m.SideScroll(side) != world.leveltime)
                Fail($"{map}: scrolling wall line {world.linespeciallist[i]!.Index}: side {side} drawn scrolled {m.SideScroll(side)} units after {world.leveltime} tics");
            _scrollingWalls++;
        }
        if (CanCapture)
            CheckSideTexturesUpload(m, $"{map} (after {world.leveltime} tics)");
    }

    /// <summary>
    /// T5.7, with a renderer: the rendered map's side-on wall
    /// (<see cref="PickTextureChange"/>) is drawn with its slot translated to
    /// another texture (as an animation re-points it: an animation's next
    /// frame when the wall draws one) and compared side-on with that texture,
    /// then scrolled 7 units through its sidedef and compared again. Both are put back.
    /// </summary>
    private async Task CheckAnimationDrawn(LevelMesh m)
    {
        string map = m.Level.Name;
        if (PickTextureChange(m, sideOn: true) is not var (s, texture))
        {
            Fail($"{map}: no side-on wall section to translate and scroll");
            return;
        }
        (List<string[]> animTextures, _) = _scene.AnimGroups(_scene.Wad!);
        string name = Textures.TextureDefs[s.Texture].Name;
        foreach (string[] seq in animTextures)
        {
            int k = Array.FindIndex(seq, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (k >= 0)
                texture = Textures.R_CheckTextureNumForName(seq[(k + 1) % seq.Length]);
        }
        m.TranslateTexture(s.Texture, texture);
        m.UpdateSectors();
        int translated = (await CheckWall(m, s, WallTextureTiling.Vanilla, map, texture: texture)).Pixels;
        m.TranslateTexture(s.Texture, s.Texture);
        _scene.PresentWorld(); // the world's own translation back
        if (translated == 0)
            Fail($"{map}: line {s.Line.Index}'s {s.Kind} translated to {Textures.TextureDefs[texture].Name}: no pixel compared");

        const int scroll = 7 << Fixed.FRACBITS;
        IsoDoom.Map.Side side = s.SideDef;
        int before = side.TextureOffset;
        int already = (int)MathF.Round(m.SideScroll(side.Index)) << Fixed.FRACBITS; // a scrolling wall moved by the sim
        side.TextureOffset += scroll;
        m.UpdateSectors();
        int scrolled = (await CheckWall(m, s, WallTextureTiling.Vanilla, map, scroll: already + scroll)).Pixels;
        side.TextureOffset = before;
        m.UpdateSectors();
        if (scrolled == 0)
            Fail($"{map}: line {s.Line.Index}'s {s.Kind} scrolled: no pixel compared");
        GD.Print($"Level check: {map}: line {s.Line.Index} side {s.Side} {s.Kind} ({name}) drawn translated to {Textures.TextureDefs[texture].Name}: "
            + $"{translated} drawn pixels compared; scrolled 7 units: {scrolled} drawn pixels compared");
    }
}
