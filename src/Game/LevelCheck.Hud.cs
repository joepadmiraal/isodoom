using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Sim;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

// T6.11: the HUD. On every map (no renderer needed): the status bar starts
// on the map's console player (ST_Start, P_SpawnPlayer's), each tic runs
// its ticker on the presentation's M_Random (never the world's), the face,
// key boxes and ready ammo follow the player, a message the sim leaves
// shows on the message line for HU_MSGTIMEOUT tics, and the bar draws the
// WAD's STBAR under its widgets. With a real renderer, the HUD node shows the
// bar (and the fullscreen HUD) and the message line scaled at the bottom
// and top of the window, every HUD pixel compared with the palette colour of
// its index, in a flash palette too.
public partial class LevelCheck
{
    private int _hudMaps;

    /// <summary>
    /// T6.11: the scene's status bar and message line along a tic: started
    /// for the console player, ticked on <see cref="LevelScene.MRandom"/>
    /// (the world's <c>rndindex</c> unused), the face turned by an attacker,
    /// a skull key in its box, the fist's ammo blank, the message on and
    /// off again. The player's state is restored.
    /// </summary>
    private void CheckHudState(string map)
    {
        if (_scene.World is not { } world || _scene.PlayerMobj is not { } me || _scene.StatusBar is not { } st || _scene.MessageLine is not { } hu)
            return;
        _hudMaps++;
        player_t p = world.players[world.consoleplayer];
        if (st.plyr != p)
            Fail($"{map}: the status bar was not started for the map's console player");
        int clock = st.st_clock, mrandom = _scene.MRandom.rndindex, rndindex = world.random.rndindex;
        (int damagecount, mobj_t? attacker, weapontype_t ready, bool card, uint angle) = (p.damagecount, p.attacker, p.readyweapon, p.cards[(int)card_t.it_yellowskull], me.angle);
        mobj_t foe = world.P_SpawnMobj(me.x, me.y + 64 * Fixed.FRACUNIT, me.z, mobjtype_t.MT_POSSESSED);
        // Facing west, the attacker to the north: the face turns right (STFTR), unless a higher priority (death, a new weapon) shows.
        world.PlaceMobj(me, me.x, me.y, Tables.ANG180);
        p.attacker = foe;
        p.damagecount = 10;
        p.cards[(int)card_t.it_yellowskull] = true;
        p.readyweapon = weapontype_t.wp_fist;
        p.message = World.GOTYELWSKUL;
        st.priority = 0;
        p.weaponowned.CopyTo(st.oldweaponsowned, 0); // no evil grin for a weapon the game loop picked up
        _scene.Tic(new ticcmd_t { angleturn = _scene.Tweaks.AbsoluteAiming ? Ticcmds.AbsoluteAngle(Tables.ANG180) : (short)0 });
        int pain = st.ST_calcPainOffset();
        if (st.st_clock != clock + 1 || _scene.MRandom.rndindex != ((mrandom + 1) & 255) || world.random.rndindex != rndindex)
            Fail($"{map}: the status bar's tic: st_clock {clock} -> {st.st_clock}, the scene's M_Random index {mrandom} -> {_scene.MRandom.rndindex}, "
                + $"the world's {rndindex} -> {world.random.rndindex} (must not move)");
        if (p.health > 0 && st.st_faceindex != pain + StStuff.ST_TURNOFFSET)
            Fail($"{map}: an attacker to the player's right: face {st.st_faceindex} ({StStuff.FaceName(st.st_faceindex)}), expected {pain + StStuff.ST_TURNOFFSET}");
        if (st.keyboxes[1] != (int)card_t.it_yellowskull || st.w_ready_num != StStuff.largeammo)
            Fail($"{map}: key boxes {string.Join(",", st.keyboxes)} (expected the yellow skull in the second), ready ammo {st.w_ready_num} (expected none for the fist)");
        if (_scene.HudMessage != World.GOTYELWSKUL || !hu.message_on || hu.message_counter != HuStuff.HU_MSGTIMEOUT)
            Fail($"{map}: the message line shows \"{_scene.HudMessage}\" ({hu.message_counter} tics), expected \"{World.GOTYELWSKUL}\" for {HuStuff.HU_MSGTIMEOUT}");
        if (st.G.sbar is { } bar && bar.Width >= StStuff.ST_WIDTH && bar.Height >= StStuff.ST_HEIGHT && st.Screen.Opaque.Any(o => o == 0))
            Fail($"{map}: the status bar has see-through pixels over a whole STBAR");
        if (hu.hu_font.Any(g => g is not null) && !hu.Screen.Opaque.Any(o => o != 0))
            Fail($"{map}: the message line draws nothing");
        world.P_RemoveMobj(foe);
        (p.damagecount, p.attacker, p.readyweapon, p.cards[(int)card_t.it_yellowskull]) = (damagecount, attacker, ready, card);
        me.angle = angle;
        hu.message_counter = 1; // ends with the next tic
        hu.HU_Ticker(null);
        if (_scene.HudMessage is not null)
            Fail($"{map}: the message line still shows \"{_scene.HudMessage}\" after its time");
    }

    /// <summary>
    /// T6.11, with a real renderer: the HUD node draws the status bar at the
    /// bottom (centred, at <see cref="HudView.ScaleFor"/>'s scale), the
    /// message line at the top left, and with <see cref="HudMode.Full"/>
    /// the fullscreen HUD over the view: every scaled HUD pixel's centre
    /// must be its index's colour in the palette shown (0, then 4: a damage
    /// flash), and the view must show where they draw nothing.
    /// </summary>
    private async Task CheckHudDrawn()
    {
        if (_scene.World is not { } world || _scene.StatusBar is not { } st || _scene.MessageLine is not { } hu || _scene.PlayerMobj is null)
            return;
        string map = _scene.Mesh?.Level.Name ?? "?";
        player_t p = world.players[world.consoleplayer];
        p.message = "HUD CHECK";
        _scene.Tic(new ticcmd_t { angleturn = _scene.Tweaks.AbsoluteAiming ? Ticcmds.AbsoluteAngle(_scene.PlayerMobj.angle) : (short)0 });
        HudView view = _scene.Hud;
        bool overlay = _scene.Overlay.Visible;
        _scene.Overlay.Visible = false;
        Color background = _scene.Environment.BackgroundColor;
        (int ur, int ug, int ub) = UnusedColor(Playpal);
        _scene.Environment.BackgroundColor = Color.Color8((byte)ur, (byte)ug, (byte)ub);
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = false;
        }
        if (_scene.Things is { } things)
            things.Visible = false;
        HudMode mode = view.Mode;
        view.Visible = true;
        int compared = 0;
        foreach ((HudMode m, int palette) in new[] { (HudMode.Bar, 0), (HudMode.Bar, 4), (HudMode.Full, 0) })
        {
            view.Mode = m;
            view.Show(st, hu, Playpal, palette);
            string what = $"{map}: the HUD ({m}, palette {palette})";
            if (await Capture(what) is not byte[] frame)
                continue;
            Vector2I size = ViewSize();
            int scale = view.PixelScale, left = (int)MathF.Floor((size.X - HudScreen.SCREENWIDTH * scale) / 2f);
            byte[] pal = Playpal.GetPalette(palette).ToArray();
            int bad = 0;
            string first = "";
            void Compare(HudScreen screen, int x0, int y0, string part)
            {
                for (int y = 0; y < screen.Height; y++)
                {
                    for (int x = 0; x < screen.Width; x++)
                    {
                        int sx = x0 + x * scale + scale / 2, sy = y0 + y * scale + scale / 2;
                        if (sx < 0 || sy < 0 || sx >= size.X || sy >= size.Y)
                            continue;
                        int i = (sy * size.X + sx) * 4, k = y * screen.Width + x;
                        (int r, int g, int b) = screen.Opaque[k] != 0 ? (pal[screen.Pixels[k] * 3], pal[screen.Pixels[k] * 3 + 1], pal[screen.Pixels[k] * 3 + 2]) : (ur, ug, ub);
                        compared++;
                        if (frame[i] == r && frame[i + 1] == g && frame[i + 2] == b)
                            continue;
                        if (bad++ == 0)
                            first = $"{part} pixel ({x}, {y}) at ({sx}, {sy}): drawn ({frame[i]}, {frame[i + 1]}, {frame[i + 2]}), expected ({r}, {g}, {b})";
                    }
                }
            }
            Compare(m == HudMode.Bar ? st.Screen : st.FullScreen, left, size.Y - StStuff.ST_HEIGHT * scale, m == HudMode.Bar ? "status bar" : "fullscreen HUD");
            Compare(hu.Screen, Math.Max(left, 0), 0, "message line");
            if (bad > 0)
                Fail($"{what}: {bad} pixels differ; first: {first}");
        }
        _pixels += compared;
        GD.Print($"Level check: {map}: HUD (T6.11): status bar, fullscreen HUD and message line at scale {view.PixelScale}, {compared} pixels compared");
        view.Mode = mode;
        view.Visible = false;
        _scene.Overlay.Visible = overlay;
        _scene.Environment.BackgroundColor = background;
        foreach (MeshInstance3D? chunk in _scene.Chunks)
        {
            if (chunk is not null)
                chunk.Visible = true;
        }
        if (_scene.Things is { } shown)
            shown.Visible = true;
    }
}
