using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

// T9.4: the registered and Ultimate Doom's episodes (not the shareware game's,
// whose other episodes show vanilla's message, nor Doom II's). Each episode is
// started from the menus through the scene's input glue (New Game, the episode,
// Hurt Me Plenty) on its first map; each episode's end past the first (E1's is
// CheckFinale's): its text and flat, mus_victor, then E2's VICTORY2, E4's ENDPIC
// as their patches, and E3's bunny scroll (F_BunnyScroll): PFUB1 at its start,
// mus_bunny, PFUB2 once scrolled (a wide one's middle 320 columns), the "THE END" letters with a pistol
// shot each, END6 drawn. With a real renderer the bunny scroll on screen too.
public partial class LevelCheck
{
    /// <summary>Whether the WAD's game has episodes to pick past the first (registered, retail).</summary>
    private bool HasEpisodes => _scene.GameMode is GameMode.registered or GameMode.retail;

    private void CheckEpisodes()
    {
        if (!HasEpisodes)
            return;
        GameFlow flow = _scene.Flow;
        MMenu menu = flow.Menu;
        int episodes = menu.EpiDef.numitems;
        int expected = _scene.GameMode == GameMode.retail ? 4 : 3;
        if (episodes != expected)
            Fail($"the episodes (T9.4): the episode menu has {episodes} items, expected {expected} for {_scene.GameMode}");
        for (int e = 1; e <= episodes; e++)
        {
            flow.D_StartTitle(null);
            _scene.MenuEvent(KeyEvent(Key.A));
            while (menu.itemOn != 0)
                _scene.MenuEvent(KeyEvent(Key.Up));
            _scene.MenuEvent(KeyEvent(Key.Enter));
            if (menu.StateName != "episode")
            {
                Fail($"the episodes (T9.4): New Game did not show the episodes: {menu.StateText()}");
                return;
            }
            while (menu.itemOn != e - 1)
                _scene.MenuEvent(KeyEvent(Key.Down));
            _scene.MenuEvent(KeyEvent(Key.Enter));
            if (menu.StateName != "skill")
            {
                Fail($"the episodes (T9.4): episode {e} did not go to the skills: {menu.StateText()}");
                continue;
            }
            while (menu.itemOn != 2)
                _scene.MenuEvent(KeyEvent(menu.itemOn < 2 ? Key.Down : Key.Up));
            _scene.MenuEvent(KeyEvent(Key.Enter));
            flow.G_DoGameActions();
            string first = $"E{e}M1";
            if (flow.gamestate != gamestate_t.GS_LEVEL || _scene.Mesh?.Level.Name != first || _scene.World is not { } world
                || world.gameepisode != e || world.gameskill != skill_t.sk_medium)
            {
                Fail($"the episodes (T9.4): episode {e} from the menu started {_scene.Mesh?.Level.Name} ({flow.StateText()}), expected {first} on sk_medium");
                continue;
            }
            if (e > 1)
                CheckEpisodeEnd(world, e);
        }
        flow.D_StartTitle(null);
        GD.Print($"Level check: episodes (T9.4): {episodes} started from the menu on their first maps; E2-E{episodes}'s ends checked");
    }

    /// <summary>T9.4: episode <paramref name="e"/>'s last map left (the world on its first map, as the menu started it): its finale.</summary>
    private void CheckEpisodeEnd(World world, int e)
    {
        GameFlow flow = _scene.Flow;
        world.gamemap = 8;
        world.G_ExitLevel();
        _scene.Tic(new ticcmd_t());
        FFinale fi = flow.Finale;
        FFinale.textscreen_t vanilla = FFinale.textscreens.FirstOrDefault(t => t.mission == GameMission.doom && t.episode == e && t.level == 8);
        if (flow.gamestate != gamestate_t.GS_FINALE || fi.finaletext != vanilla.text || fi.finaleflat != vanilla.background)
        {
            Fail($"the episodes (T9.4): E{e}M8's exit: {flow.StateText()}, text \"{fi.finaletext[..System.Math.Min(20, fi.finaletext.Length)]}…\" on {fi.finaleflat}, expected E{e}TEXT on {vanilla.background}");
            return;
        }
        if (_scene.Sound is { } sound && sound.mus_playing != musicenum_t.mus_victor)
            Fail($"the episodes (T9.4): E{e}'s text plays {sound.mus_playing}, not mus_victor");
        int textTics = fi.finaletext.Length * FFinale.TEXTSPEED + FFinale.TEXTWAIT;
        while (fi.finalestage == FFinale.F_STAGE_TEXT && fi.finalecount <= textTics)
            _scene.Tic(new ticcmd_t());
        if (fi.finalestage != FFinale.F_STAGE_ARTSCREEN || fi.finalecount != 0)
        {
            Fail($"the episodes (T9.4): E{e}'s text did not give way to the end picture after {textTics} tics: {flow.StateText()}");
            return;
        }
        var graphics = new ScreenGraphics(_scene.Wad, _scene.MessageLine);
        var screen = new HudScreen(0, HudScreen.SCREENHEIGHT);
        if (e != 3)
        {
            string page = e == 2 ? "VICTORY2" : "ENDPIC";
            for (int t = 0; t < 50; t++)
                _scene.Tic(new ticcmd_t { buttons = t % 2 == 0 ? buttoncode_t.BT_USE : (byte)0 });
            fi.F_Drawer(graphics, screen);
            if (flow.gamestate != gamestate_t.GS_FINALE || fi.ArtScreen() != page)
                Fail($"the episodes (T9.4): E{e}'s end picture did not stay ({flow.StateText()}, {fi.ArtScreen()}, expected {page})");
            else if (graphics.Patch(page) is { } patch && !PageDrawn(screen, patch))
                Fail($"the episodes (T9.4): E{e}'s end picture {page} is not drawn as its patch");
            else
                GD.Print($"Level check: episodes (T9.4): E{e}M8's text on {fi.finaleflat} for {textTics} tics, mus_victor, then {page}, which stays");
            return;
        }

        // E3: the bunny scroll
        if (_scene.Sound is { } bunny && bunny.mus_playing != musicenum_t.mus_bunny)
            Fail($"the episodes (T9.4): E3's end picture plays {bunny.mus_playing}, not mus_bunny");
        IndexedImage? pfub1 = graphics.Patch("PFUB1"), pfub2 = graphics.Patch("PFUB2");
        fi.F_Drawer(graphics, screen);
        if (pfub1 is not null && !ColumnsDrawn(screen, pfub1, 0))
            Fail("the episodes (T9.4): the bunny scroll does not start on PFUB1");
        int shots = 0;
        while (fi.finalecount < 1250 && flow.gamestate == gamestate_t.GS_FINALE)
        {
            _scene.Tic(new ticcmd_t());
            shots += fi.sounds.Count(s => s == sfxenum_t.sfx_pistol);
            if (fi.finalecount == 900)
            {
                fi.F_Drawer(graphics, screen);
                if (pfub2 is not null && !ColumnsDrawn(screen, pfub2, HudScreen.WideShift(pfub2)))
                    Fail("the episodes (T9.4): the bunny scroll does not end on PFUB2 (a wide one's middle 320 columns)");
            }
        }
        fi.F_Drawer(graphics, screen);
        IndexedImage? end6 = graphics.Patch("END6");
        if (flow.gamestate != gamestate_t.GS_FINALE || shots != 6)
            Fail($"the episodes (T9.4): the bunny scroll: {flow.StateText()}, {shots} pistol shots (expected 6)");
        else if (end6 is not null && !PatchDrawn(screen, end6, (HudScreen.SCREENWIDTH - 13 * 8) / 2, (HudScreen.SCREENHEIGHT - 8 * 8) / 2))
            Fail("the episodes (T9.4): the bunny scroll's END6 is not drawn");
        else
            GD.Print($"Level check: episodes (T9.4): E3M8's text on {fi.finaleflat} for {textTics} tics, mus_victor, then the bunny scroll (mus_bunny, PFUB1 to PFUB2, END0-END6 with {shots} pistol shots)");
    }

    /// <summary>Whether the screen's every column x shows column x + <paramref name="shift"/> of <paramref name="patch"/> from the top (F_DrawPatchCol's).</summary>
    private static bool ColumnsDrawn(HudScreen screen, IndexedImage patch, int shift)
    {
        for (int x = 0; x < HudScreen.SCREENWIDTH && x + shift < patch.Width; x++)
        {
            for (int y = 0; y < patch.Height && y < screen.Height; y++)
            {
                int i = y * screen.Width + x;
                if (patch.IsOpaque(x + shift, y) && (screen.Opaque[i] == 0 || screen.Pixels[i] != patch[x + shift, y]))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Whether every opaque pixel of <paramref name="patch"/> drawn at <paramref name="x0"/>, <paramref name="y0"/> (less its offsets) is on the screen.</summary>
    private static bool PatchDrawn(HudScreen screen, IndexedImage patch, int x0, int y0)
    {
        for (int y = 0; y < patch.Height; y++)
        {
            for (int x = 0; x < patch.Width; x++)
            {
                int sx = x0 + x - patch.LeftOffset, sy = y0 + y - patch.TopOffset;
                if (!patch.IsOpaque(x, y) || sx < 0 || sy < 0 || sx >= screen.Width || sy >= screen.Height)
                    continue;
                int i = sy * screen.Width + sx;
                if (screen.Opaque[i] == 0 || screen.Pixels[i] != patch[x, y])
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// T9.4, with a real renderer: E3's end, the bunny scroll, on the screens
    /// node mid-scroll and with its last letters, each pixel against its
    /// 320×200 screen (<see cref="CompareScreen"/>).
    /// </summary>
    private async Task CheckBunnyDrawn()
    {
        if (!HasEpisodes)
            return;
        GameFlow flow = _scene.Flow;
        bool overlay = _scene.Overlay.Visible;
        _scene.Overlay.Visible = false;
        _scene.StartNewGame(skill_t.sk_medium, 3, 1);
        flow.G_DoGameActions();
        if (_scene.World is not { } world)
        {
            Fail("the bunny scroll (T9.4): no new game on episode 3");
            return;
        }
        world.gamemap = 8;
        world.G_ExitLevel();
        _scene.Tic(new ticcmd_t());
        FFinale fi = flow.Finale;
        fi.finalecount = fi.finaletext.Length * FFinale.TEXTSPEED + FFinale.TEXTWAIT; // the text's last tic
        _scene.Tic(new ticcmd_t());
        int before = _screenPixels;
        foreach (int count in new[] { 500, 1215 })
        {
            while (fi.finalecount < count && flow.gamestate == gamestate_t.GS_FINALE)
                _scene.Tic(new ticcmd_t());
            _scene.UpdateScreens();
            await CompareScreen($"the bunny scroll at {fi.finalecount} ({flow.StateText()})", false);
        }
        _scene.Overlay.Visible = overlay;
        GD.Print($"Level check: the bunny scroll (T9.4): drawn mid-scroll and with END6, {_screenPixels - before} pixels compared");
        flow.D_StartTitle(null);
    }
}
