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

/// <summary>
/// The level check's thing sprites part (T3.5).
/// <list type="bullet">
/// <item><b>Sprite atlas</b> (once per WAD): every lump a frame slot of
/// <see cref="Sprites"/> uses has a slot whose atlas rectangle holds exactly the
/// decoded patch and whose <c>sprite_info</c> texels give its rectangle and
/// offsets (read back from the GPU too with a real renderer).</item>
/// <item><b>Every map:</b> the billboards are the map's spawn list on the
/// scene's skill (<see cref="SpawnedThings"/>, recomputed here), each instance
/// at the thing's position, with its sector and full-bright flag, and showing
/// the lump and mirror r_things.c <c>R_ProjectSprite</c> picks
/// (<see cref="Sprites.R_ProjectSpriteRotation"/>) for an orthographic view
/// along the game camera's direction and for a perspective camera; the sprite
/// material shares the level material's parameters.</item>
/// <item><b>Drawn pixels</b> (rendered map, real renderer): each thing of a
/// distinct sprite frame, rotation and mirror (all of them on the synthetic
/// map) alone, with the level hidden, seen horizontally along the game
/// camera's direction at 1 unit per pixel: every pixel of its rectangle must
/// be the patch texel (mirrored when the rotation flips) through the sprite
/// light (<see cref="LightTables.WallColormap"/> with no contrast at the
/// player distance, colormap 0 when full bright) or the background where
/// the patch is transparent, and nothing else may be drawn. One thing again
/// with no diminishing and <c>extralight</c> 1, one with a fixed colormap
/// (which beats full bright, as vanilla). Then one thing on open floor from
/// the game camera's angle with the level shown: the rows below its origin
/// must draw over the floor (SPEC §12 T3.5).</item>
/// </list>
/// </summary>
public partial class LevelCheck
{
    private bool _spriteAtlasChecked;
    private int _thingsChecked;

    /// <summary>The game camera's view direction (Godot space) and a horizontal one with the same yaw.</summary>
    private static Basis GameBasis(float pitch) => Basis.FromEuler(new Vector3(-Mathf.DegToRad(pitch), Mathf.DegToRad(IsoCamera.Yaw), 0));

    /// <summary>The sprite atlas against the decoded patches and its uploads (once per WAD).</summary>
    private void CheckSpriteAtlas()
    {
        if (_spriteAtlasChecked)
            return;
        _spriteAtlasChecked = true;
        SpriteAtlas? atlas = _scene.SpriteAtlas;
        if (atlas is null)
        {
            Fail("no sprite atlas (the WAD's sprites could not be indexed)");
            return;
        }
        int lumps = 0;
        foreach (SpriteDef def in atlas.Sprites.SpriteDefs)
        {
            foreach (SpriteFrame frame in def.Frames)
            {
                foreach (int lump in frame.Lump)
                {
                    if (lump >= 0 && atlas.SlotOf(lump) < 0)
                        Fail($"sprite {def.Name}: lump {_scene.Wad!.Lumps[lump].Name} has no atlas slot");
                }
            }
        }
        for (int slot = 0; slot < atlas.Lumps.Count; slot++)
        {
            if (atlas.Lumps[slot] < 0)
                continue;
            lumps++;
            var l = _scene.Wad!.Lumps[atlas.Lumps[slot]];
            IndexedImage expected = Patch.Decode(l.Data.Span, l.Name);
            AtlasRect r = atlas.Atlas.Rects[slot];
            if (atlas.InfoTexel(2 * slot) != new Color(r.X, r.Y, r.Width, r.Height)
                || atlas.InfoTexel(2 * slot + 1) != new Color(expected.LeftOffset, expected.TopOffset, 0, 0))
                Fail($"sprite lump {l.Name}: sprite_info texels {atlas.InfoTexel(2 * slot)}, {atlas.InfoTexel(2 * slot + 1)}; rectangle {r}, offsets ({expected.LeftOffset}, {expected.TopOffset})");
            if (r.Width != expected.Width || r.Height != expected.Height)
            {
                Fail($"sprite lump {l.Name}: atlas rectangle {r.Width}x{r.Height}, the patch is {expected.Width}x{expected.Height}");
                continue;
            }
            for (int y = 0; y < r.Height; y++)
            {
                for (int x = 0; x < r.Width; x++)
                {
                    if (atlas.Atlas.Image.IsOpaque(r.X + x, r.Y + y) != expected.IsOpaque(x, y)
                        || (expected.IsOpaque(x, y) && atlas.Atlas.Image[r.X + x, r.Y + y] != expected[x, y]))
                    {
                        Fail($"sprite lump {l.Name}: atlas texel ({x},{y}) differs from the patch");
                        y = r.Height;
                        break;
                    }
                }
            }
        }
        if (CanCapture)
        {
            Image? gpu = atlas.AtlasTexture.GetImage();
            if (gpu is null || gpu.GetFormat() != Image.Format.Rg8 || !gpu.GetData().AsSpan().SequenceEqual(IndexedTextures.ToRg8(atlas.Atlas.Image)))
                Fail("the sprite atlas upload differs from the atlas");
            Image? info = atlas.InfoTexture.GetImage();
            for (int i = 0; info is not null && i < 2 * atlas.Lumps.Count; i++)
            {
                if (info.GetPixel(i % LevelMesh.DataWidth, i / LevelMesh.DataWidth) != atlas.InfoTexel(i))
                {
                    Fail($"sprite_info texel {i} differs on the GPU");
                    break;
                }
            }
            if (info is null)
                Fail("sprite_info can't be read back");
        }
        GD.Print($"Level check: sprite atlas: {lumps} sprite lumps of {atlas.Sprites.NumSprites} sprites in {atlas.Atlas.Image.Width}x{atlas.Atlas.Image.Height}, "
            + $"each against its patch and offsets{(CanCapture ? ", uploads read back" : "")}");
    }

    /// <summary>The map's billboards against its spawn list, positions, sectors and the rotations two cameras see.</summary>
    private void CheckThings(LevelMesh m, string map)
    {
        CheckSpriteAtlas();
        ThingSprites? things = _scene.Things;
        SpriteAtlas? atlas = _scene.SpriteAtlas;
        if (things is null || atlas is null)
        {
            Fail($"{map}: no thing billboards");
            return;
        }
        SpawnedThing[] expected = SpawnedThings.Build(m.Level, MapThingSpawning.SpawnList(m.Level.Things, new SpawnSettings(_scene.GameMode, _scene.Skill)));
        if (things.Entries.Count != expected.Length || things.Multimesh.InstanceCount != expected.Length)
        {
            Fail($"{map}: {things.Entries.Count} billboards ({things.Multimesh.InstanceCount} instances), the spawn list has {expected.Length} things");
            return;
        }
        if (things.MissingFrames > 0)
            Fail($"{map}: {things.MissingFrames} thing(s) whose spawn frame the WAD lacks");
        if (things.MaterialOverride != m.SpriteMaterial
            || m.SpriteMaterial.GetShaderParameter("sprite_atlas").As<Texture2D>() != atlas.AtlasTexture
            || m.SpriteMaterial.GetShaderParameter("sprite_info").As<Texture2D>() != atlas.InfoTexture)
            Fail($"{map}: the billboards don't draw with the level's sprite material and the sprite atlas");
        foreach (string name in new[] { "sector_data", "playpal", "colormap", "palette_index", "colormap_override", "light_tables", "light_mode",
                     "light_origin", "light_reference", "light_near", "light_centerx", "extralight" })
        {
            Variant a = m.Material.GetShaderParameter(name), b = m.SpriteMaterial.GetShaderParameter(name);
            if (a.VariantType != b.VariantType || a.ToString() != b.ToString()
                || (a.VariantType == Variant.Type.Object && a.AsGodotObject() != b.AsGodotObject()))
                Fail($"{map}: the sprite material's {name} ({b}) differs from the level material's ({a})");
        }

        // An orthographic view along the game camera's direction, then a perspective camera south-east of the map.
        Vector3 forward = -GameBasis(IsoCamera.DefaultPitch).Z;
        Vector3 eye = m.Bounds.GetCenter() + new Vector3(m.Bounds.Size.X, m.Bounds.Size.Y + 10, m.Bounds.Size.Z);
        foreach (bool ortho in new[] { true, false })
        {
            things.UpdateRotations(ortho, forward, eye);
            for (int i = 0; i < expected.Length; i++)
            {
                SpawnedThing t = expected[i];
                ThingSprites.Entry e = things.Entries[i];
                string what = $"{map}: thing {i} ({t.Spawn.Type} at {t.x >> Fixed.FRACBITS}, {t.y >> Fixed.FRACBITS})";
                if (e != LevelScene.ThingEntry(t))
                {
                    Fail($"{what}: entry {e}, expected {LevelScene.ThingEntry(t)}");
                    continue;
                }
                Vector3 position = LevelMesh.ToGodot(t.x, t.y, (float)(t.z / 65536.0));
                // The instance data as written (the GPU's copy too with a real renderer; the headless one keeps none).
                if (!Near(things.InstancePosition(i), position)
                    || (CanCapture && (!Near(things.Multimesh.GetInstanceTransform(i).Origin, position) || things.Multimesh.GetInstanceTransform(i).Basis != Basis.Identity)))
                    Fail($"{what}: instance at {things.InstancePosition(i)} ({things.Multimesh.GetInstanceTransform(i).Origin} on the GPU), expected {position}");
                SpriteFrame frame = atlas.Sprites.SpriteDefs[(int)t.sprite].Frames[t.frame];
                Vector3 d = ortho ? forward : position - eye;
                int rot = frame.Rotate ? Sprites.R_ProjectSpriteRotation(ThingSprites.BamOfMap(d.X, -d.Z), t.angle) : 0;
                int slot = atlas.SlotOf(frame.Lump[rot]);
                var custom = new Color(slot, (frame.Flip[rot] ? ThingSprites.FlagFlip : 0) | (t.fullbright ? ThingSprites.FlagFullBright : 0)
                    | ((Info.mobjinfo[(int)t.Spawn.Type].flags & mobjflag_t.MF_SHOOTABLE) != 0 ? ThingSprites.FlagActor : 0), t.Sector.Index, 0);
                if (things.CustomData(i) != custom || things.ShownFrames[i].Rot != rot || (CanCapture && things.Multimesh.GetInstanceCustomData(i) != custom))
                    Fail($"{what}, {(ortho ? "orthographic" : "perspective")}: custom data {things.CustomData(i)} ({things.Multimesh.GetInstanceCustomData(i)} on the GPU, "
                        + $"rotation slot {things.ShownFrames[i].Rot}), expected {custom} (slot {rot})");
            }
        }
        _thingsChecked += expected.Length;
    }

    // ---- Drawn pixels ----

    private async Task SpriteChecks(LevelMesh m)
    {
        string map = m.Level.Name;
        ThingSprites? things = _scene.Things;
        SpriteAtlas? atlas = _scene.SpriteAtlas;
        if (things is null || atlas is null || things.Entries.Count == 0)
        {
            if (m.Level.Things.Length > 1)
                Fail($"{map}: no things to compare");
            return;
        }
        things.Visible = true;
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = false;
        }

        // Horizontal, along the game camera's yaw: its rotations, the sprites seen straight on.
        Basis basis = GameBasis(0);
        Vector3 forward = -basis.Z;
        things.UpdateRotations(true, forward, Vector3.Zero);
        var picked = new List<int>();
        var keys = new HashSet<(int, int, int, bool, bool)>();
        var rotations = new SortedSet<int>();
        var allRotations = new SortedSet<int>();
        bool anyFlip = false, flipCompared = false;
        for (int i = 0; i < things.Entries.Count; i++)
        {
            ThingSprites.Entry e = things.Entries[i];
            ThingSprites.Shown s = things.ShownFrames[i];
            if (s.Slot < 0)
                continue;
            bool rotates = things.FrameOf(e)!.Rotate;
            if (rotates)
                allRotations.Add(s.Rot);
            anyFlip |= s.Flip;
            if (keys.Add((e.Sprite, e.Frame, s.Rot, s.Flip, e.FullBright)) && picked.Count < 48)
                picked.Add(i);
        }
        int compared = 0, fullbright = 0;
        foreach (int i in picked)
        {
            ThingSprites.Shown s = things.ShownFrames[i];
            if (things.FrameOf(things.Entries[i])!.Rotate)
                rotations.Add(s.Rot);
            flipCompared |= s.Flip;
            if (things.Entries[i].FullBright)
            {
                // Full bright ignores the sector light: darken the sector for the view, so a lit sprite would differ.
                fullbright++;
                Sector sector = m.Level.Sectors[things.Entries[i].Sector];
                short light = sector.LightLevel;
                sector.LightLevel = 0;
                m.UpdateSectors();
                compared += await CompareSprite(m, things, atlas, i, basis, $"{map}: thing {i} (full bright, sector light 0)");
                sector.LightLevel = light;
                m.UpdateSectors();
            }
            else
                compared += await CompareSprite(m, things, atlas, i, basis, $"{map}: thing {i}");
        }
        if (allRotations.Count > 0 && rotations.Count != allRotations.Count)
            Fail($"{map}: sprite rotations compared {string.Join(" ", rotations)}, the map shows {string.Join(" ", allRotations)}");
        if (anyFlip && !flipCompared)
            Fail($"{map}: no mirrored rotation compared");

        // Light options: no diminishing with extralight 1 on a lit thing, a fixed colormap on a full-bright one (it wins).
        int lit = picked.Find(i => !things.Entries[i].FullBright);
        if (picked.Exists(i => !things.Entries[i].FullBright))
        {
            m.SetLightDiminishing(LightDiminishing.None);
            m.SetExtraLight(1);
            compared += await CompareSprite(m, things, atlas, lit, basis, $"{map}: thing {lit}, no diminishing, extralight 1");
            m.SetExtraLight(0);
            m.SetLightDiminishing(LightDiminishing.Player);
        }
        int bright = picked.Exists(i => things.Entries[i].FullBright) ? picked.Find(i => things.Entries[i].FullBright) : picked[0];
        m.SetColormapOverride(Colormap.INVERSECOLORMAP);
        compared += await CompareSprite(m, things, atlas, bright, basis, $"{map}: thing {bright}, fixed colormap {Colormap.INVERSECOLORMAP}");
        m.SetColormapOverride(-1);

        // T3.6: vanilla's look (no outline) side-on, and the full tilt from the game camera's pitch, where the
        // billboard faces the camera: the patch again at 1 unit per pixel (an upright one would be squashed).
        SpriteSettings settings = m.Sprites;
        m.SetSprites(settings with { Outline = -1, Tilt = 0 });
        compared += await CompareSprite(m, things, atlas, lit, basis, $"{map}: thing {lit}, no outline, no tilt");
        m.SetSprites(settings with { Tilt = 1 });
        Basis pitched = GameBasis(IsoCamera.DefaultPitch);
        foreach (int i in new[] { lit, bright }.Distinct())
            compared += await CompareSprite(m, things, atlas, i, pitched, $"{map}: thing {i}, full tilt from the game camera's pitch");
        m.SetSprites(settings);
        GD.Print($"Level check: {map}: {picked.Count} things of distinct frames, rotations and mirrors seen side-on ({fullbright} full bright; rotation slots {string.Join(" ", rotations)}"
            + $"{(flipCompared ? ", mirrored ones among them" : "")}; outline {settings.Outline}), plus two light options, no outline, and full tilt from {IsoCamera.DefaultPitch}°: {compared} pixels compared");

        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = true;
        }
        await CheckRowsBelowOrigin(m, things, atlas);
        await CheckTiltDepth(m, things, atlas);
        await CheckThingCutaway(m, things, atlas);
        things.Isolate(null);
        things.Visible = false;
    }

    /// <summary>
    /// Thing <paramref name="i"/> alone, seen along <paramref name="basis"/>
    /// (horizontal) with its origin on a pixel corner: its rectangle against
    /// the patch and the sprite light, the rest of the frame the background.
    /// </summary>
    private async Task<int> CompareSprite(LevelMesh m, ThingSprites things, SpriteAtlas atlas, int i, Basis basis, string what)
    {
        const float back = 1024;
        ThingSprites.Entry e = things.Entries[i];
        ThingSprites.Shown shown = things.ShownFrames[i];
        IndexedImage patch = atlas.Images[shown.Slot];
        things.Isolate(i);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        Ortho(basis, e.MapPosition + toCamera * back, 1, 2 * back);
        Vector2 foot = _scene.Camera.UnprojectPosition(LevelMesh.ToGodot((int)(e.MapPosition.X * 65536), (int)(e.MapPosition.Y * 65536), e.MapPosition.Z));
        int fx = (int)MathF.Round(foot.X), fy = (int)MathF.Round(foot.Y);
        if (MathF.Abs(foot.X - fx) > 0.01f || MathF.Abs(foot.Y - fy) > 0.01f)
        {
            Fail($"{what}: the origin projects to ({foot.X}, {foot.Y}), not a pixel corner");
            return 0;
        }
        byte[]? frame = await Capture(what);
        if (frame is null)
            return 0;

        int colormap = m.ColormapOverride >= 0 ? m.ColormapOverride
            : e.FullBright ? 0
            : ExpectedColormap(m, true, m.Level.Sectors[e.Sector].LightLevel, 0, e.MapPosition.X, e.MapPosition.Y, back, sprite: true);
        Vector2I size = ViewSize();
        int left = fx - patch.LeftOffset, top = fy - patch.TopOffset;
        // T3.6: the outline (in the sprite light) on transparent texels next to an opaque one, one texel around the rectangle too.
        int outline = m.Sprites.Outline, grow = outline >= 0 ? 1 : 0;
        bool Opaque(int c, int r) => c >= 0 && c < patch.Width && r >= 0 && r < patch.Height && patch.IsOpaque(shown.Flip ? patch.Width - 1 - c : c, r);
        int compared = 0, bad = 0, stray = 0, outlined = 0;
        string first = "";
        for (int py = 0; py < size.Y; py++)
        {
            for (int px = 0; px < size.X; px++)
            {
                int p = (py * size.X + px) * 4;
                (int R, int G, int B) got = (frame[p], frame[p + 1], frame[p + 2]);
                int c = px - left, r = py - top;
                if (c < -grow || c >= patch.Width + grow || r < -grow || r >= patch.Height + grow)
                {
                    if (!NearBackground(got))
                        stray++;
                    continue;
                }
                compared++;
                bool ok;
                string expected;
                if (Opaque(c, r))
                {
                    byte texel = patch[shown.Flip ? patch.Width - 1 - c : c, r];
                    ok = colormap < 0 ? !NearBackground(got) : got == Shade(texel, colormap);
                    expected = colormap < 0 ? "a texel" : $"{Shade(texel, colormap)} (colormap {colormap})";
                }
                else if (grow > 0 && (Opaque(c - 1, r) || Opaque(c + 1, r) || Opaque(c, r - 1) || Opaque(c, r + 1)))
                {
                    outlined++;
                    ok = colormap < 0 ? !NearBackground(got) : got == Shade((byte)outline, colormap);
                    expected = colormap < 0 ? "the outline" : $"the outline {Shade((byte)outline, colormap)}";
                }
                else
                {
                    ok = NearBackground(got);
                    expected = "the background";
                }
                if (!ok && bad++ == 0)
                    first = $"texel ({c}, {r}) at pixel ({px}, {py}): drew {got}, expected {expected}";
            }
        }
        _pixels += compared;
        if (bad > 0)
            Fail($"{what} ({(SpriteName(e))}, rotation slot {shown.Rot}{(shown.Flip ? " mirrored" : "")}{(grow > 0 ? $", outline {outline}" : "")}): {bad} of {compared} pixels differ, first {first}");
        if (stray > 0)
            Fail($"{what} ({SpriteName(e)}): {stray} pixel(s) drawn outside the sprite's rectangle{(grow > 0 ? " and outline" : "")}");
        if (grow > 0 && outlined == 0)
            Fail($"{what} ({SpriteName(e)}): no outline pixels to compare");
        return compared;
    }

    private string SpriteName(ThingSprites.Entry e) =>
        $"{_scene.SpriteAtlas!.Sprites.SpriteDefs[e.Sprite].Name} {(char)('A' + e.Frame)}";

    /// <summary>
    /// From the game camera's angle with the level shown: a thing on open floor
    /// (nothing but its own sector within 64 units towards the camera) whose
    /// patch has rows below its origin. Those rows must draw over the floor:
    /// the pixels below the origin's screen row must equal the thing drawn alone.
    /// </summary>
    private async Task CheckRowsBelowOrigin(LevelMesh m, ThingSprites things, SpriteAtlas atlas)
    {
        string map = m.Level.Name;
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        var ground = new Vector2(toCamera.X, toCamera.Y).Normalized();
        var right = new Vector2(ground.Y, -ground.X);
        things.UpdateRotations(true, -basis.Z, Vector3.Zero);
        int chosen = -1;
        for (int i = 0; i < things.Entries.Count && chosen < 0; i++)
        {
            ThingSprites.Entry e = things.Entries[i];
            ThingSprites.Shown s = things.ShownFrames[i];
            Sector sector = m.Level.Sectors[e.Sector];
            if (s.Slot < 0 || e.MapPosition.Z != sector.FloorHeight / 65536f)
                continue;
            IndexedImage patch = atlas.Images[s.Slot];
            int opaqueRowsBelow = 0;
            for (int r = Math.Max(0, patch.TopOffset); r < patch.Height; r++)
            {
                for (int c = 0; c < patch.Width; c++)
                {
                    if (patch.IsOpaque(c, r))
                    {
                        opaqueRowsBelow++;
                        break;
                    }
                }
            }
            if (opaqueRowsBelow < 3)
                continue;
            var at = new Vector2(e.MapPosition.X, e.MapPosition.Y);
            bool open = true;
            foreach (float side in new[] { -patch.Width / 2f - 2, 0, patch.Width / 2f + 2 })
            {
                Vector2 start = at + right * side, end = start + ground * 64;
                for (int k = 0; k <= 64 && open; k += 4)
                {
                    Vector2 p = start + ground * k;
                    open = m.Level.R_PointInSubsector((int)(p.X * 65536), (int)(p.Y * 65536)).Sector == sector;
                }
                foreach (Line line in sector.Lines)
                {
                    if (open && SegmentDistance(start.X, start.Y, end.X, end.Y, line.V1.X / 65536.0, line.V1.Y / 65536.0, line.V2.X / 65536.0, line.V2.Y / 65536.0) < 2)
                        open = false;
                }
            }
            if (open)
                chosen = i;
        }
        if (chosen < 0)
        {
            Fail($"{map}: no thing on open floor with rows below its origin to look at from the game camera");
            return;
        }
        const float back = 4096;
        ThingSprites.Entry t = things.Entries[chosen];
        things.Isolate(chosen);
        Ortho(basis, t.MapPosition + toCamera * back, 1, 2 * back);
        string what = $"{map}: thing {chosen} ({SpriteName(t)}) over the floor from the game camera";
        byte[]? withLevel = await Capture(what);
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = false;
        }
        byte[]? alone = await Capture($"{what}, alone");
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = true;
        }
        if (withLevel is null || alone is null)
            return;
        Vector2 foot = _scene.Camera.UnprojectPosition(LevelMesh.ToGodot((int)(t.MapPosition.X * 65536), (int)(t.MapPosition.Y * 65536), t.MapPosition.Z));
        Vector2I size = ViewSize();
        int below = 0, bad = 0, above = 0;
        for (int py = 0; py < size.Y; py++)
        {
            for (int px = 0; px < size.X; px++)
            {
                int p = (py * size.X + px) * 4;
                if (NearBackground((alone[p], alone[p + 1], alone[p + 2])))
                    continue;
                if (py + 0.5f < foot.Y + 0.1f)
                {
                    above++;
                    continue;
                }
                below++;
                if (withLevel[p] != alone[p] || withLevel[p + 1] != alone[p + 1] || withLevel[p + 2] != alone[p + 2])
                    bad++;
            }
        }
        _pixels += below;
        if (below == 0 || above == 0)
            Fail($"{what}: {below} sprite pixels below the origin's row, {above} above (expected both)");
        if (bad > 0)
            Fail($"{what}: {bad} of {below} pixels of the rows below the origin are hidden (the floor drew over them)");
        GD.Print($"Level check: {what}: {below} pixels of the rows below the origin drawn over the floor");
    }

    /// <summary>
    /// T3.6: a tilted billboard keeps an upright one's depth
    /// (<see cref="SpriteTiltDepth.Upright"/>). The tallest thing is moved in
    /// front of a long one-sided wall facing the game camera, on open floor,
    /// near enough that a billboard really leaning back (full tilt) would go
    /// into the wall: from the game camera with the level shown, every pixel
    /// of the sprite must equal the sprite drawn alone; with
    /// <see cref="SpriteTiltDepth.Tilted"/> some must be hidden (so the view
    /// does test the depth).
    /// </summary>
    private async Task CheckTiltDepth(LevelMesh m, ThingSprites things, SpriteAtlas atlas)
    {
        string map = m.Level.Name;
        double pitch = Mathf.DegToRad(IsoCamera.DefaultPitch);
        Basis basis = GameBasis(IsoCamera.DefaultPitch);
        Vector3 toCamera = Cutaway.ToMapAxes(basis.Z).Normalized();
        var ground = new Vector2(toCamera.X, toCamera.Y).Normalized();
        var right = new Vector2(ground.Y, -ground.X);
        things.UpdateRotations(true, -basis.Z, Vector3.Zero);

        // The tallest thing standing on its floor.
        int thing = -1, tallest = 0;
        for (int i = 0; i < things.Entries.Count; i++)
        {
            ThingSprites.Entry e = things.Entries[i];
            ThingSprites.Shown s = things.ShownFrames[i];
            if (s.Slot >= 0 && e.MapPosition.Z == m.Level.Sectors[e.Sector].FloorHeight / 65536f && atlas.Images[s.Slot].TopOffset > tallest)
            {
                tallest = atlas.Images[s.Slot].TopOffset;
                thing = i;
            }
        }
        if (thing < 0)
        {
            Fail($"{map}: no thing on a floor for the tilt depth view");
            return;
        }
        ThingSprites.Entry original = things.Entries[thing];
        IndexedImage patch = atlas.Images[things.ShownFrames[thing].Slot];
        float halfSpan = Math.Max(patch.LeftOffset, patch.Width - patch.LeftOffset) + 2;

        // A one-sided wall facing the camera, long and high enough, with open floor in front of the spot.
        Vector2? spot = null;
        Sector? front = null;
        float along = 0;
        foreach (Line line in m.Level.Lines)
        {
            if (line.BackSector is not null || line.FrontSector is not Sector sector)
                continue;
            var a = new Vector2(line.V1.X / 65536f, line.V1.Y / 65536f);
            var b = new Vector2(line.V2.X / 65536f, line.V2.Y / 65536f);
            float length = a.DistanceTo(b);
            Vector2 normal = new Vector2(b.Y - a.Y, a.X - b.X) / length; // the front (right) side
            float facing = normal.Dot(ground);
            if (facing < 0.5f || length < 2 * halfSpan + 128 || (sector.CeilingHeight - sector.FloorHeight) >> Fixed.FRACBITS < 96)
                continue;
            // Far enough that no column of the upright billboard reaches the wall.
            float distance = 8 + halfSpan * MathF.Abs(right.Dot(normal));
            Vector2 at = (a + b) / 2 + normal * distance;
            if (m.Level.R_PointInSubsector((int)(at.X * 65536), (int)(at.Y * 65536)).Sector != sector)
                continue;
            bool open = true;
            foreach (float side in new[] { -halfSpan, 0, halfSpan })
            {
                Vector2 start = at + right * side, end = start + ground * 96;
                for (int k = 0; k <= 96 && open; k += 4)
                {
                    Vector2 p = start + ground * k;
                    open = m.Level.R_PointInSubsector((int)(p.X * 65536), (int)(p.Y * 65536)).Sector == sector;
                }
                foreach (Line other in sector.Lines)
                {
                    if (open && other != line && SegmentDistance(start.X, start.Y, end.X, end.Y, other.V1.X / 65536.0, other.V1.Y / 65536.0, other.V2.X / 65536.0, other.V2.Y / 65536.0) < 2)
                        open = false;
                }
            }
            if (!open)
                continue;
            spot = at;
            front = sector;
            along = distance / facing;
            break;
        }
        if (spot is not Vector2 where || front is null)
        {
            Fail($"{map}: no one-sided wall facing the game camera with open floor in front for the tilt depth view");
            return;
        }
        // A billboard leaning back hides behind the wall above this screen height (map units).
        double hiddenAbove = along / Math.Sin(pitch);
        if (patch.TopOffset < hiddenAbove + 4)
        {
            Fail($"{map}: the tallest thing ({SpriteName(original)}, {patch.TopOffset} rows above its origin) is too short for the tilt depth view (hidden above {hiddenAbove:F0})");
            return;
        }

        ThingSprites.Entry moved = original with { MapPosition = new Vector3(where.X, where.Y, front.FloorHeight / 65536f), Sector = front.Index };
        SpriteSettings settings = m.Sprites;
        things.SetEntry(thing, moved);
        things.Isolate(thing);
        const float back = 4096;
        Ortho(basis, moved.MapPosition + toCamera * back, 1, 2 * back);
        string what = $"{map}: thing {thing} ({SpriteName(original)}) at ({where.X:F0}, {where.Y:F0}), {along:F0} units in front of a wall, full tilt";
        m.SetSprites(settings with { Tilt = 1, TiltDepth = SpriteTiltDepth.Upright });
        byte[]? upright = await Capture(what);
        m.SetSprites(settings with { Tilt = 1, TiltDepth = SpriteTiltDepth.Tilted });
        byte[]? tilted = await Capture($"{what}, tilted depth");
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = false;
        }
        m.SetSprites(settings with { Tilt = 1, TiltDepth = SpriteTiltDepth.Upright });
        byte[]? alone = await Capture($"{what}, alone");
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = true;
        }
        m.SetSprites(settings);
        things.SetEntry(thing, original);
        if (upright is null || tilted is null || alone is null)
            return;
        int drawn = 0, hidden = 0, hiddenTilted = 0;
        for (int p = 0; p < alone.Length; p += 4)
        {
            if (NearBackground((alone[p], alone[p + 1], alone[p + 2])))
                continue;
            drawn++;
            if (upright[p] != alone[p] || upright[p + 1] != alone[p + 1] || upright[p + 2] != alone[p + 2])
                hidden++;
            if (tilted[p] != alone[p] || tilted[p + 1] != alone[p + 1] || tilted[p + 2] != alone[p + 2])
                hiddenTilted++;
        }
        _pixels += drawn;
        if (drawn == 0)
            Fail($"{what}: nothing drawn");
        if (hidden > 0)
            Fail($"{what}: {hidden} of {drawn} sprite pixels hidden (the wall or floor behind drew over an upright-depth billboard)");
        if (hiddenTilted == 0)
            Fail($"{what}: with tilted depth no sprite pixel is hidden by the wall behind (the view does not test the depth)");
        GD.Print($"Level check: {what}: all {drawn} sprite pixels drawn with upright depth ({hiddenTilted} hidden by the wall with tilted depth)");
    }
}
