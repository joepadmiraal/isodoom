using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T7.1a: the screen wipes (f_wipe.c's melt, GameFlow.D_Display). Once, with
// the wipes on for the check (off for the rest of it): a new game from the
// title melts after the level's first tic (M_Random drawn 320 times), no tic
// runs while it melts, it lasts as its columns say; the exit's intermission
// melts after its first tic; turning the option off ends a melt under way.
// With a real renderer, the melt from the title page into the level held at
// a step, every pixel against the frames it melts (the captured start frame
// slid down by each column's offset, the level above it).
public partial class LevelCheck
{
    private void CheckWipes()
    {
        GameFlow flow = _scene.Flow;
        var none = new ticcmd_t();
        _scene.CheckWipes = true;
        try
        {
            flow.D_StartTitle(null);
            _scene.Tic(none);
            while (!flow.WipeStep())
            {
            }
            _scene.StartNewGame();
            flow.G_DoGameActions();
            int before = _scene.MRandom.rndindex, count = flow.Wipe.count;
            _scene.Tic(none);
            int draws = 320 + (_scene.StatusBar is null ? 0 : 1); // the melt's columns, and the status bar's tic
            if (flow.gamestate != gamestate_t.GS_LEVEL || flow.Wipe.count != count + 1 || !flow.Wipe.go || flow.CanTic
                || ((_scene.MRandom.rndindex - before) & 0xff) != (draws & 0xff))
            {
                Fail($"the wipe into a new game: {flow.StateText()}, wipes {count} -> {flow.Wipe.count}, under way {flow.Wipe.go}, "
                    + $"tics {(flow.CanTic ? "run" : "held")}, M_Random index {before} -> {_scene.MRandom.rndindex} (expected {draws} draws)");
                return;
            }
            int wait = -flow.Wipe.y.Take(FWipe.COLUMNS).Min();
            long tics = _scene.TicsRun;
            int steps = 1;
            while (!flow.WipeStep())
                steps++;
            // the longest wait, then 27 steps for 200 rows (1, 2, 4, 8, 16, then 8 a tic), then the step that finds it done
            if (steps != wait + 28 || flow.Wipe.steps != steps || !flow.CanTic || _scene.TicsRun != tics)
                Fail($"the melt: {steps} steps (expected {wait + 28}: the columns wait up to {wait} tics), tics {_scene.TicsRun - tics}");

            World world = _scene.World!;
            world.G_ExitLevel(); // (between the tics: the next tic's G_DoCompleted)
            _scene.Tic(none); // the intermission's first tic, then D_Display melts
            if (flow.gamestate != gamestate_t.GS_INTERMISSION || flow.Wipe.count != count + 2 || !flow.Wipe.go || flow.Wi.bcnt != 1)
                Fail($"the wipe into the intermission: {flow.StateText()}, wipes {flow.Wipe.count - count}, under way {flow.Wipe.go}");
            _scene.CheckWipes = false;
            if (flow.Wipe.go || !flow.CanTic)
                Fail("the wipes turned off: the melt under way goes on");
            else
                GD.Print($"Level check: wipes (T7.1a): a new game melts after its first tic in {steps} steps (M_Random drawn {draws} times), "
                    + "no tic meanwhile; the intermission after its first; the option off ends one");
        }
        finally
        {
            _scene.CheckWipes = false;
        }
    }

    /// <summary>
    /// With a real renderer: the melt from the title page into a new game's
    /// level held at <paramref name="step"/>: each pixel of the frame against
    /// the start frame slid down by its column's offset (vanilla's 320×200
    /// over the window: a column 1/160 of its width, a row 1/200 of its
    /// height), the level's frame above it (pixels on an edge between the two
    /// left out).
    /// </summary>
    private async Task CheckWipeDrawn(int step = 12)
    {
        GameFlow flow = _scene.Flow;
        bool overlay = _scene.Overlay.Visible;
        _scene.Overlay.Visible = false;
        _scene.CheckWipes = true;
        try
        {
            flow.Menu.M_ClearMenus();
            flow.D_StartTitle(null);
            _scene.Tic(new ticcmd_t());
            while (!flow.WipeStep())
            {
            }
            _scene.WipeLayer.Show(flow.Wipe, false);
            _scene.UpdateScreens();
            await NextFrame();
            await NextFrame();
            _scene.StartNewGame();
            flow.G_DoGameActions();
            _scene.UpdateScreens(); // the title page goes
            _scene.Tic(new ticcmd_t()); // the level's first tic: the title page's frame melts
            if (!flow.Wipe.go || _scene.WipeLayer.StartFrame is not Image start)
            {
                Fail($"the melt drawn: no wipe under way ({flow.StateText()}) or no frame captured");
                return;
            }
            while (flow.Wipe.steps < step)
                flow.WipeStep();
            _scene.WipeLayer.Show(flow.Wipe, false);
            if (await Capture("the melt") is not byte[] frame)
                return;
            _scene.WipeLayer.Visible = false;
            byte[]? end = await Capture("the level under the melt");
            _scene.WipeLayer.Visible = true;
            if (end is null)
                return;
            if (start.GetFormat() != Image.Format.Rgba8)
                start.Convert(Image.Format.Rgba8);
            byte[] from = start.GetData();
            Vector2I size = ViewSize();
            if (start.GetWidth() != size.X || start.GetHeight() != size.Y)
            {
                Fail($"the melt: the start frame is {start.GetWidth()}x{start.GetHeight()}, the viewport {size.X}x{size.Y}");
                return;
            }
            int bad = 0, here = 0, fromStart = 0;
            string firstBad = "";
            for (int y = 0; y < size.Y; y++)
            {
                for (int x = 0; x < size.X; x++)
                {
                    int column = Math.Clamp((int)Math.Floor((x + 0.5) / size.X * FWipe.COLUMNS), 0, FWipe.COLUMNS - 1);
                    double shift = Math.Clamp(flow.Wipe.y[column], 0, HudScreen.SCREENHEIGHT) * size.Y / (double)HudScreen.SCREENHEIGHT;
                    double v = y + 0.5 - shift; // the start frame's row (pixel units) the shader samples
                    if (Math.Abs(v) < 0.02 || Math.Abs(v - Math.Round(v)) < 0.02)
                        continue; // on an edge
                    int i = (y * size.X + x) * 4;
                    int k = v < 0 ? i : ((int)Math.Floor(v) * size.X + x) * 4;
                    byte[] want = v < 0 ? end : from;
                    here++;
                    fromStart += v < 0 ? 0 : 1;
                    if (frame[i] == want[k] && frame[i + 1] == want[k + 1] && frame[i + 2] == want[k + 2])
                        continue;
                    if (bad++ == 0)
                        firstBad = $"({x}, {y}): drawn ({frame[i]}, {frame[i + 1]}, {frame[i + 2]}), expected ({want[k]}, {want[k + 1]}, {want[k + 2]}) from the {(v < 0 ? "level" : "start frame")}";
                }
            }
            if (fromStart == 0 || fromStart == here)
                Fail($"the melt at step {step}: {fromStart} of {here} pixels from the start frame (expected both frames)");
            if (bad > 0)
                Fail($"the melt at step {step}: {bad} of {here} pixels differ; first: {firstBad}");
            else
                GD.Print($"Level check: the melt drawn (T7.1a): the title into the level at step {step}, {here} pixels ({fromStart} from the start frame) as the columns say");
            _pixels += here;
            while (!flow.WipeStep())
            {
            }
            _scene.WipeLayer.Show(flow.Wipe, false);
        }
        finally
        {
            _scene.CheckWipes = false;
            _scene.Overlay.Visible = overlay;
        }
    }
}
