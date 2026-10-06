using System;
using System.Collections.Generic;
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
public partial class LevelCheck
{
    private int _switchPairs, _textureChanges, _flatChanges, _interpolatedSectors;

    /// <summary>The <c>side_textures</c> upload read back from the GPU against the CPU copy.</summary>
    private void CheckSideTexturesUpload(LevelMesh m, string map)
    {
        Image? gpu = m.SideTexturesTexture.GetImage();
        if (gpu is null || gpu.GetFormat() != Image.Format.Rf)
        {
            Fail($"{map}: side_textures can't be read back as RF");
            return;
        }
        for (int side = 0; side < m.Level.Sides.Length; side++)
        {
            for (int part = 0; part < 3; part++)
            {
                int id = 3 * side + part;
                if ((int)MathF.Round(gpu.GetPixel(id % LevelMesh.DataWidth, id / LevelMesh.DataWidth).R) != m.SideTextureSlot(side, part))
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
}
