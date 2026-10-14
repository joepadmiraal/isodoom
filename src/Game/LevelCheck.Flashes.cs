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
// checks: CheckFloorsTopDown, CompareSprite and CompareFuzz.) T6.13w: the
// pickup ripple at its full size: a line circle, the pixels it changes lie
// within a pixel or two of its line (FlashGlow.RippleWidth inside the aim
// marker's ring) and fill most of it, the line whiter than without it, the
// centre unchanged.
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
        _scene.Glow.SetRipple(FlashGlow.RippleAlpha, FlashGlow.RippleRadius);
        byte[]? ripple = await Capture(where);
        _scene.Glow.SetGlow(0, 0, 0, 0); // (back to the glow's shape)
        _scene.Glow.Visible = false;
        _scene.Things?.Visible = things;
        _scene.Player?.Visible = player;
        if (without is null || full is null || half is null || ripple is null)
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

        // T6.13w: the ripple, a line circle.
        float outerR = FlashGlow.RippleRadius + 2, innerR = FlashGlow.RippleRadius - FlashGlow.RippleWidth - 2;
        int rippled = 0, astray = 0;
        for (int py = 0; py < size.Y; py++)
        {
            for (int px = 0; px < size.X; px++)
            {
                int i = (py * size.X + px) * 4;
                if (ripple[i] == without[i] && ripple[i + 1] == without[i + 1] && ripple[i + 2] == without[i + 2])
                    continue;
                rippled++;
                float d2 = (px + 0.5f - cx) * (px + 0.5f - cx) + (py + 0.5f - cy) * (py + 0.5f - cy);
                if (d2 > outerR * outerR || d2 < innerR * innerR)
                    astray++;
            }
        }
        int Brightness(byte[] frame, int px, int py) => (frame[(py * size.X + px) * 4] + frame[(py * size.X + px) * 4 + 1] + frame[(py * size.X + px) * 4 + 2]) / 3;
        int lx = cx + (int)(FlashGlow.RippleRadius - FlashGlow.RippleWidth / 2);
        int b0 = Brightness(without, lx, cy), b1 = Brightness(ripple, lx, cy);
        int c0 = Brightness(without, cx, cy), c1 = Brightness(ripple, cx, cy);
        float r0Line = FlashGlow.RippleRadius - FlashGlow.RippleWidth;
        float area = Mathf.Pi * (FlashGlow.RippleRadius * FlashGlow.RippleRadius - r0Line * r0Line);
        if (astray > 0 || rippled < area * 0.8f || b1 - b0 < 40 || c1 != c0)
            Fail($"{where}: the pickup ripple (T6.13w) changed {rippled} pixels (its line is {area:F0}), {astray} of them off its line ({FlashGlow.RippleRadius - FlashGlow.RippleWidth}–{FlashGlow.RippleRadius}); the line's brightness {b0} → {b1}, expected at least 40 brighter; the centre's {c0} → {c1}, expected unchanged");
        GD.Print($"Level check: {map}: the pickup ripple drew {rippled} pixels on its line; the line's brightness {b0} → {b1}, the centre's {c0} → {c1}");
    }
}
