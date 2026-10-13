using System.Threading.Tasks;
using Godot;
using IsoDoom.Render;

namespace IsoDoom.Game;

// T6.13v: the player style's glow (FlashGlow) with a real renderer. Straight
// down at 1 unit per pixel onto the player's feet, things hidden: drawn with
// a damage flash's glow at full strength and without it, the pixels it
// changes all lie within its radius, the centre is redder than without it,
// and at half the strength less so. (The palettes the level and the player's
// own sprite are drawn in are compared pixel by pixel with the other palette
// checks: CheckFloorsTopDown, CompareSprite and CompareFuzz.)
public partial class LevelCheck
{
    private async Task FlashGlowCheck(LevelMesh m)
    {
        if (_scene.PlayerMobj is not { } me || _scene.World is null)
            return;
        string map = m.Level.Name;
        bool things = _scene.Things?.Visible ?? false, player = _scene.Player?.Visible ?? false;
        _scene.Things?.Visible = false;
        _scene.Player?.Visible = false;
        float x = (float)(me.x / 65536.0), y = (float)(me.y / 65536.0), z = (float)(me.z / 65536.0);
        var basis = new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));
        Ortho(basis, new Vector3(x, y, z + 256.5f), 1, 512);
        string where = $"{map}: the flash glow (T6.13v) at the player's feet ({x}, {y})";
        PaletteFlash.Glow glow = PaletteFlash.GlowOf(Playpal, StStuff.STARTREDPALS + 7);
        _scene.Glow.Place(new Vector3(x, y, z));
        _scene.Glow.Visible = false;
        byte[]? without = await Capture(where);
        _scene.Glow.Visible = true;
        _scene.Glow.SetGlow(glow.R, glow.G, glow.B, glow.Level * FlashGlow.MaxAlpha);
        byte[]? full = await Capture(where);
        _scene.Glow.SetGlow(glow.R, glow.G, glow.B, glow.Level * 0.5f * FlashGlow.MaxAlpha);
        byte[]? half = await Capture(where);
        _scene.Glow.Visible = false;
        _scene.Things?.Visible = things;
        _scene.Player?.Visible = player;
        if (without is null || full is null || half is null)
            return;
        Vector2I size = ViewSize();
        int cx = size.X / 2, cy = size.Y / 2, changed = 0, outside = 0;
        for (int py = 0; py < size.Y; py++)
        {
            for (int px = 0; px < size.X; px++)
            {
                int i = (py * size.X + px) * 4;
                if (full[i] == without[i] && full[i + 1] == without[i + 1] && full[i + 2] == without[i + 2])
                    continue;
                changed++;
                if ((px + 0.5f - cx) * (px + 0.5f - cx) + (py + 0.5f - cy) * (py + 0.5f - cy) > (FlashGlow.Radius + 2) * (FlashGlow.Radius + 2))
                    outside++;
            }
        }
        _pixels += size.X * size.Y;
        // How much redder than green the 5×5 pixels at the centre are.
        int Redness(byte[] frame)
        {
            int sum = 0;
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int i = ((cy + dy) * size.X + cx + dx) * 4;
                    sum += frame[i] - frame[i + 1];
                }
            }
            return sum / 25;
        }
        int r0 = Redness(without), r1 = Redness(full), rHalf = Redness(half);
        if (changed == 0 || outside > 0)
            Fail($"{where}: the glow changed {changed} pixels, {outside} of them beyond its radius {FlashGlow.Radius}");
        if (!(r1 > rHalf && rHalf > r0 && r1 - r0 >= 40))
            Fail($"{where}: the centre's red over green is {r0} without the glow, {rHalf} at half strength and {r1} at full (glow {glow}); expected it rising, by at least 40 at full");
        GD.Print($"Level check: {map}: the flash glow drew {changed} pixels within its radius; the centre's red over green {r0} → {rHalf} → {r1}");
    }
}
