using System;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

// T6.13x: the ready weapon's icon and its ARMS digit. On every map (no
// renderer needed): each weapon the WAD has the sprite of, made the
// player's ready one, gets its icon (the pickup sprite as the WAD has it, the
// fist's and pistol's first-person frame within the icon box) above the
// bar's ammo and above the fullscreen HUD's, a change shows the new one
// dimmed, none while dead, none with the option off; the bar's own pixels
// stay vanilla's and its ARMS digit is brightened in a copy only. With a real
// renderer, each icon drawn over the view in both modes and the bar with its
// digit, every pixel against the palette.
public partial class LevelCheck
{
    private int _weaponIconMaps, _weaponIconsChecked;

    /// <summary>The weapons in <see cref="weapontype_t"/> order.</summary>
    private static readonly weapontype_t[] _allWeapons =
    [
        weapontype_t.wp_fist, weapontype_t.wp_pistol, weapontype_t.wp_shotgun, weapontype_t.wp_chaingun, weapontype_t.wp_missile,
        weapontype_t.wp_plasma, weapontype_t.wp_bfg, weapontype_t.wp_chainsaw, weapontype_t.wp_supershotgun,
    ];

    /// <summary>The pickup sprite each weapon's icon is (null: the fist's and pistol's first-person frames).</summary>
    private static string? PickupSprite(weapontype_t weapon) => weapon switch
    {
        weapontype_t.wp_shotgun => "SHOTA0",
        weapontype_t.wp_chaingun => "MGUNA0",
        weapontype_t.wp_missile => "LAUNA0",
        weapontype_t.wp_plasma => "PLASA0",
        weapontype_t.wp_bfg => "BFUGA0",
        weapontype_t.wp_chainsaw => "CSAWA0",
        weapontype_t.wp_supershotgun => "SGN2A0",
        _ => null,
    };

    /// <summary>
    /// Makes <paramref name="weapon"/> <paramref name="p"/>'s ready one (owned,
    /// up) and redraws the bar whole; returns what to restore.
    /// </summary>
    private static (weapontype_t Ready, weapontype_t Pending, statenum_t State, bool Owned) ReadyWeapon(StStuff st, player_t p, weapontype_t weapon)
    {
        var saved = (p.readyweapon, p.pendingweapon, p.psprites[World.ps_weapon].state, p.weaponowned[(int)weapon]);
        (p.readyweapon, p.pendingweapon, p.weaponowned[(int)weapon]) = (weapon, weapontype_t.wp_nochange, true);
        p.psprites[World.ps_weapon].state = Info.weaponinfo[(int)weapon].readystate;
        st.ST_Drawer(true);
        return saved;
    }

    private static void Restore(StStuff st, player_t p, weapontype_t weapon, (weapontype_t Ready, weapontype_t Pending, statenum_t State, bool Owned) saved)
    {
        (p.readyweapon, p.pendingweapon, p.psprites[World.ps_weapon].state, p.weaponowned[(int)weapon]) = saved;
        st.ST_Drawer(true);
    }

    /// <summary>T6.13x, on every map: each weapon's icon, its place and dimming, and the ARMS digit's copy.</summary>
    private void CheckWeaponIconState(string map)
    {
        if (_scene.World is not { } world || _scene.StatusBar is not { } st || _scene.WeaponIcon is not { } icon || _scene.Wad is not { } wad || _scene.PlayerMobj is null)
            return;
        _weaponIconMaps++;
        player_t p = world.players[world.consoleplayer];
        if (!icon.Enabled || !icon.Digit)
            Fail($"{map}: the weapon icon or digit is off by default");
        foreach (weapontype_t weapon in _allWeapons)
        {
            IndexedImage? image = icon.Icon(weapon);
            string? sprite = PickupSprite(weapon);
            if (sprite is not null)
            {
                IndexedImage? lump = wad.Find(sprite) is { } l ? Patch.Decode(l.Data.Span, l.Name) : null;
                if ((lump is null) != (image is null) || lump is not null && image is not null
                    && (lump.Width != image.Width || lump.Height != image.Height || !lump.Pixels.AsSpan().SequenceEqual(image.Pixels) || !lump.Opaque.AsSpan().SequenceEqual(image.Opaque)))
                {
                    Fail($"{map}: {weapon}'s icon is not the WAD's {sprite}");
                }
            }
            else if (image is not null && (image.Width > WeaponIcon.BoxWidth || image.Height > WeaponIcon.BoxHeight))
            {
                Fail($"{map}: {weapon}'s icon is {image.Width}x{image.Height}, over the {WeaponIcon.BoxWidth}x{WeaponIcon.BoxHeight} box");
            }
            if (image is null)
                continue;
            var saved = ReadyWeapon(st, p, weapon);
            uint bar = st.Screen.Hash();
            foreach (HudMode mode in new[] { HudMode.Bar, HudMode.Full })
            {
                icon.Draw(st, mode);
                (int x, int y, int w, int h) = icon.Place(weapon, mode)!.Value;
                int right = mode == HudMode.Bar ? WeaponIcon.BarX * WeaponIcon.Resolution + w : StStuff.FS_AMMOX * WeaponIcon.Resolution;
                int bottom = (mode == HudMode.Bar ? StStuff.ST_Y : StStuff.FS_Y) - WeaponIcon.Top;
                if (icon.Shown != weapon || icon.Dimmed || x + w != right || (y + h + WeaponIcon.Resolution - 1) / WeaponIcon.Resolution > bottom || x < 0 || y < 0)
                    Fail($"{map}: {weapon}'s icon ({mode}): shown {icon.Shown}{(icon.Dimmed ? " dimmed" : "")} at ({x}, {y}) {w}x{h}");
                int bad = 0;
                for (int py = 0; py < h; py++)
                {
                    for (int px = 0; px < w; px++)
                    {
                        int k = (y + py) * icon.Screen.Width + x + px;
                        if ((icon.Screen.Opaque[k] != 0) != image.IsOpaque(px, py) || image.IsOpaque(px, py) && icon.Screen.Pixels[k] != image[px, py])
                            bad++;
                    }
                }
                if (bad > 0)
                    Fail($"{map}: {weapon}'s icon ({mode}): {bad} pixels differ from the sprite");
                _weaponIconsChecked++;
            }
            if (st.Screen.Hash() != bar)
                Fail($"{map}: the weapon icon changed the status bar's own pixels");
            int slot = WeaponIcon.ArmsSlot(weapon);
            if (icon.Highlighted != slot || (slot >= 0 && st.G.arms[slot][1] is not null) != icon.Bar is not null)
                Fail($"{map}: {weapon}'s ARMS digit: slot {icon.Highlighted} highlighted (expected {slot}), bar copy {(icon.Bar is null ? "none" : "drawn")}");
            Restore(st, p, weapon, saved);
        }
        // a change: the new weapon dimmed while the old one lowers
        (weapontype_t ready, weapontype_t pending) = (p.readyweapon, p.pendingweapon);
        weapontype_t other = ready == weapontype_t.wp_pistol ? weapontype_t.wp_fist : weapontype_t.wp_pistol;
        p.pendingweapon = other;
        icon.Draw(st, HudMode.Bar);
        if (icon.Icon(other) is not null && (icon.Shown != other || !icon.Dimmed))
            Fail($"{map}: a change to {other} shows {icon.Shown}{(icon.Dimmed ? " dimmed" : "")}, expected {other} dimmed");
        p.pendingweapon = pending;
        // off: nothing
        _scene.WeaponIconShown = false;
        if (icon.Shown != weapontype_t.NUMWEAPONS || Array.Exists(icon.Screen.Opaque, o => o != 0))
            Fail($"{map}: the weapon icon shows with the option off");
        _scene.WeaponIconShown = true;
        _scene.WeaponDigit = false;
        if (icon.Bar is not null)
            Fail($"{map}: the ARMS digit is brightened with the option off");
        _scene.WeaponDigit = true;
        icon.Draw(st, _scene.Hud.Mode);
    }

    /// <summary>
    /// T6.13x, with a real renderer (from <see cref="CheckHudDrawn"/>, the level
    /// hidden on the background colour <paramref name="ur"/>, <paramref name="ug"/>,
    /// <paramref name="ub"/>): each weapon's icon over the bar and the
    /// fullscreen HUD, and the bar with its ARMS digit brightened, every
    /// pixel's centre its index's colour in palette 0; the strip's see-through
    /// pixels above the bar show the background.
    /// </summary>
    private async Task CheckWeaponIconDrawn(StStuff st, HuStuff hu, HudView view, int ur, int ug, int ub)
    {
        if (_scene.World is not { } world || _scene.WeaponIcon is not { } icon)
            return;
        string map = _scene.Mesh?.Level.Name ?? "?";
        player_t p = world.players[world.consoleplayer];
        byte[] pal = Playpal.GetPalette(0).ToArray();
        int compared = 0, weapons = 0;
        foreach (weapontype_t weapon in _allWeapons)
        {
            if (icon.Icon(weapon) is null)
                continue;
            weapons++;
            var saved = ReadyWeapon(st, p, weapon);
            hu.message_on = false;
            foreach (HudMode mode in new[] { HudMode.Bar, HudMode.Full })
            {
                view.Mode = mode;
                icon.Draw(st, mode);
                view.Show(st, hu, icon, pal);
                string what = $"{map}: the weapon icon ({weapon}, {mode})";
                if (await Capture(what) is not byte[] frame)
                    continue;
                Vector2I size = ViewSize();
                int bad = 0;
                string first = "";
                bool Same(int sx, int sy, int r, int g, int b, string part, int x, int y)
                {
                    int i = (sy * size.X + sx) * 4;
                    compared++;
                    if (frame[i] == r && frame[i + 1] == g && frame[i + 2] == b)
                        return true;
                    if (bad++ == 0)
                        first = $"{part} pixel ({x}, {y}) at ({sx}, {sy}): drawn ({frame[i]}, {frame[i + 1]}, {frame[i + 2]}), expected ({r}, {g}, {b})";
                    return false;
                }
                HudScreen strip = icon.Screen;
                ScreenLayout at = ScreenLayout.FromRect(ScreenRects.RectOf(view.WeaponIconRect), strip.Width, strip.Height);
                int barTop = (StStuff.ST_Y - WeaponIcon.Top) * WeaponIcon.Resolution;
                for (int y = 0; y < strip.Height; y++)
                {
                    for (int x = 0; x < strip.Width; x++)
                    {
                        int k = y * strip.Width + x;
                        if (strip.Opaque[k] == 0 && y >= barTop)
                            continue; // the bar or the fullscreen HUD under it
                        (int sx, int sy) = BlockCentre(at, x, y);
                        if (sx < 0 || sy < 0 || sx >= size.X || sy >= size.Y)
                            continue;
                        int c = strip.Pixels[k] * 3;
                        _ = strip.Opaque[k] != 0 ? Same(sx, sy, pal[c], pal[c + 1], pal[c + 2], "icon", x, y) : Same(sx, sy, ur, ug, ub, "icon (see-through)", x, y);
                    }
                }
                if (mode == HudMode.Bar)
                {
                    HudScreen bar = icon.Bar ?? st.Screen;
                    ScreenLayout layout = view.Layout;
                    for (int y = 0; y < bar.Height; y++)
                    {
                        for (int x = 0; x < bar.Width; x++)
                        {
                            (int sx, int sy) = BlockCentre(layout, x, bar.Top + y);
                            int k = y * bar.Width + x, c = bar.Pixels[k] * 3;
                            if (sx >= 0 && sy >= 0 && sx < size.X && sy < size.Y && bar.Opaque[k] != 0)
                                Same(sx, sy, pal[c], pal[c + 1], pal[c + 2], "status bar", x, y);
                        }
                    }
                }
                if (bad > 0)
                    Fail($"{what}: {bad} pixels differ; first: {first}");
            }
            Restore(st, p, weapon, saved);
        }
        _pixels += compared;
        GD.Print($"Level check: {map}: weapon icon (T6.13x): {weapons} weapons' icons over the bar and the fullscreen HUD and the ARMS digit brightened, {compared} pixels compared");
    }
}
