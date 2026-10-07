using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Godot;
using SectorFloor = IsoDoom.Map.SectorFloor;
using IsoDoom.Render;

namespace IsoDoom.Game;

/// <summary>
/// Scripted input for the level scene (T2.7, debugging): the user argument
/// <c>--level-script="COMMAND; COMMAND; …"</c> feeds input events through
/// <see cref="Input.ParseInputEvent"/>, so they take the same path as real
/// keys and mouse, one command after another, then quits. Lets the free-fly
/// camera and the map switching be checked without a person at the keyboard
/// (with <c>--fixed-fps 60</c> before <c>--</c>, movement per frame is fixed).
/// <para>
/// Commands (keys by Godot name, e.g. <c>W</c>, <c>Shift</c>, <c>Tab</c>,
/// <c>PageDown</c>): <c>down KEY</c> / <c>up KEY</c> (press or release);
/// <c>tap KEY</c> (press, a frame, release); <c>hold KEY FRAMES</c>;
/// <c>wait FRAMES</c>; <c>click</c> (left button: captures the mouse);
/// <c>look DX DY</c> (mouse motion in pixels); <c>wheel up|down [STEPS]</c>
/// (Ctrl as currently held); <c>print</c> (the overlay text to stdout);
/// <c>shot FILE.png</c> (capture, needs a real renderer; WAD-derived, keep it
/// out of the repo); <c>view X Y ANGLE [Z]</c> (T2.9: put the free-fly camera
/// at map point X, Y facing vanilla angle ANGLE in degrees, no pitch, at
/// height Z or by default at eye level, floor + <c>VIEWHEIGHT</c>, as a
/// vanilla player standing still); <c>fov DEGREES|vanilla</c> (the free-fly
/// camera's vertical field of view; <c>vanilla</c> is
/// <see cref="FreeFlyCamera.VanillaFov"/>, so a 16:10 window shows what
/// vanilla's full-screen 320×200 view shows); <c>mouse X Y</c> (T3.3: move
/// the cursor to viewport pixel X, Y; the game camera's cursor ground point
/// follows); <c>place X Y [ANGLE]</c> (T3.3: put the player mobj at map
/// point X, Y on the floor, optionally facing ANGLE degrees, with no momentum
/// and no collision check, and centre the game camera on it); <c>visible [MIN]</c>
/// (T3.4: print how much of the player the game camera shows, in % of its
/// pixels, and fail the script below MIN %, default 25; needs a real renderer;
/// the world holds still meanwhile); <c>walkto X Y [STEP] [MIN]</c> (T3.4:
/// move the player in a straight line, through walls, to map point X, Y,
/// STEP units at a time (default 32), centring the camera and running
/// <c>visible MIN</c> at every step, and print the least visible step);
/// <c>tour [STEP] [MIN]</c> (T3.4: <c>walkto</c> a point inside every
/// sector's floor of the map, nearest unvisited first from where the
/// player stands); <c>frametime [FRAMES]</c> (T3.8: measure the wall-clock
/// time of the next FRAMES frames, default 300, and print the mean, median, 95th
/// percentile and worst in ms; run with <c>--disable-vsync</c> before <c>--</c>);
/// <c>things on|off</c> (T3.8: show or hide the thing billboards, e.g. to
/// measure what they cost); <c>ticcmd [vanilla]</c> (T4.6: print the
/// <c>ticcmd</c> the builder makes from the input since the last
/// <c>ticcmd</c> for the player, with the twin-stick tweaks or vanilla's; it
/// takes that input from the game loop's next tic);
/// <c>quit</c> (also implied at the end).
/// </para>
/// <para>
/// The game loop (T4.7): <c>cmd FORWARD SIDE ANGLETURN [BUTTONS] [TICS]</c>
/// queues TICS tics (default 1) of that <c>ticcmd</c> (raw fields: with the
/// twin-stick tweaks ANGLETURN is the absolute angle's upper 16 bits, e.g.
/// 16384 = north, FORWARD thrusts north and SIDE east; with
/// <c>--level-tweaks=vanilla</c> they are relative, as a demo's) and
/// <c>step TICS</c> queues TICS tics built from the input at each tic (keys
/// held by <c>down</c>, the cursor ground point) and waits until they ran.
/// Either switches the game loop to scripted tics (<see cref="LevelScene.ScriptedTics"/>,
/// also from the start when the script has a <c>cmd</c>, <c>step</c> or <c>sim scripted</c>):
/// tics run only from the queue, at 35 Hz, and the world holds still while
/// it is empty, so the result does not depend on the frame rate
/// (<c>--fixed-fps 30</c>, <c>60</c>, <c>144</c> give the same checksum;
/// a cursor aim does depend on it, as the camera follows the player with
/// smoothing). <c>sim live|scripted</c> switches the mode (live: tics from the
/// input every 1/35 s, as the game). <c>tics N</c> waits until N more tics
/// ran (or the queue is empty). <c>checksum</c> waits until the queue is empty and
/// prints <c>leveltime</c>, <c>World.Checksum()</c> and the player mobj.
/// <c>tictime TICS</c> (T4.9) times <c>World.G_Ticker</c> (T6.13: naming the worst tic and the
/// garbage collections of the whole process meanwhile) over the next TICS
/// tics (or until the scripted queue is empty) and prints the mean, median,
/// 95th percentile and worst in ms (SPEC §9's budget is 2 ms).
/// <c>map NAME [EXITS]</c> (T5.8) waits until the queue is empty, prints the map
/// shown, the maps completed through exits and the player's status, and
/// fails the script unless the map is NAME (and, T5.10, EXITS maps were
/// completed through exits since the scene started; e.g. after an exit and
/// the intermission (T7.1: the map shown stays the one left until it is over):
/// <c>place 2940 -4768 180; cmd 0 0 -32768 0 2; cmd 0 0 -32768 2; skip; map E1M2</c>
/// on DOOM1.WAD's E1M1).
/// <c>route FILE</c> (T5.10) queues a <c>.route</c> file's tics
/// (<see cref="RouteFile"/>, e.g. <c>tests/IsoDoom.Tests/Sim/Routes/e1m1-exit.route</c>)
/// and does not wait for them, so <c>tics N; shot FILE.png</c> can follow
/// along; a <c>start</c> header places the player first, as the route tests
/// do; its <c>damage</c>, <c>alert</c> and <c>rocket</c> events (T6.4, T6.5) run before their tics. Routes are vanilla demos: the scene needs <c>--level-tweaks=vanilla</c>,
/// <c>--level-monsters=off</c> (<c>on</c> for a route with the <c>monsters</c> header), the route's skill and map, or the script
/// fails. <c>skip; map NAME</c> after it checks that the route left by its exit.
/// A route goes on through a reborn (T6.12: a dead player's use reloads the
/// level; the <c>start</c> header places the player again), and
/// <c>reborns N</c> waits for the queued tics and fails unless the scene
/// reloaded the level for a reborn N times since it started (e.g.
/// <c>route tests/IsoDoom.Tests/Sim/Routes/e1m8-death.route; reborns 1</c>).
/// <c>route FILE TICS</c> (T6.11) queues only the route's first TICS tics, so
/// <c>tics TICS; shot FILE.png</c> captures the scene (and its HUD) after
/// exactly that tic, as <c>tools/VanillaRef/routes.sh</c>'s <c>DUMP_HUD_TICS</c> does vanilla's.
/// <c>joy AXIS VALUE</c> (T4.9) moves a gamepad axis by Godot <c>JoyAxis</c>
/// name (<c>LeftX</c>, <c>LeftY</c>: the move stick, <c>RightX</c>,
/// <c>RightY</c>: the aim stick, <c>TriggerRight</c>: fire; Y down is
/// positive) to VALUE (−1 to 1); <c>joybutton BUTTON down|up</c> presses or
/// releases a gamepad button by <c>JoyButton</c> name (e.g. <c>A</c> use,
/// <c>LeftStick</c> run toggle). Both go through <see cref="Input.ParseInputEvent"/>
/// as device 0, as a real pad.
/// <c>smooth FRAMES</c> records where the player is drawn on each of the next
/// FRAMES frames and prints the steps between frames (interpolation: even
/// steps while moving at a steady speed); <c>smooth FRAMES SECTOR</c> (T5.1)
/// does the same for sector SECTOR's drawn floor and ceiling.
/// <c>plane SECTOR floor|ceiling HEIGHT [SPEED]</c> (T5.1, a debug move) moves
/// the sector's floor or ceiling towards HEIGHT by SPEED units (default 2, a
/// door's speed) at the end of each tic, as a mover thinker would, with
/// <c>P_ChangeSector</c>; it runs with the tics (queue some, or <c>sim live</c>).
/// <c>missile [TYPE]</c> (T6.5, a debug shot from before the weapons, T6.6) makes the player
/// fire a missile of <c>mobjtype_t</c> <c>MT_TYPE</c> (default <c>rocket</c>; e.g.
/// <c>troopshot</c>, <c>bruisershot</c>) through <c>P_SpawnPlayerMissile</c> (along its
/// facing, aimed as vanilla) before the next tic queued after it (scripted tics) or the next
/// tic (<c>sim live</c>); e.g. <c>place X Y ANGLE; missile; cmd 0 0 0 0 10; shot FILE.png</c>
/// with <c>--level-tweaks=vanilla</c> (a twin-stick <c>cmd</c>'s ANGLETURN sets the facing).
/// <c>power NAME [TICS]</c> (T6.8, a debug power-up until the cheats, T7.9) gives the
/// player power-up <c>pw_NAME</c> (<c>invulnerability</c>, <c>strength</c>,
/// <c>invisibility</c>, <c>ironfeet</c>, <c>allmap</c>, <c>infrared</c>) as its
/// pickup does (<c>P_GivePower</c>), optionally with TICS left, and <c>flash damage|bonus COUNT</c>
/// sets its <c>damagecount</c> or <c>bonuscount</c> (the red or gold palette flash), both
/// before the next tic as <c>missile</c>; e.g. <c>power infrared; cmd 0 0 0 0 1; shot FILE.png</c>.
/// <c>wake</c> (T6.13, debugging) makes every live monster hunt the player
/// before the next tic (target and see state, ambush or not), e.g.
/// <c>power invulnerability; wake; sim live; wait 120; tictime 700</c> for the tic budget.
/// <c>demo LUMP [TICS]</c> (T6.13) queues the WAD's demo lump (e.g. <c>DEMO1</c>) as a route
/// (<see cref="RouteFile.FromDemo"/>: the vanilla-input adapter); like <c>route</c> it needs
/// <c>--level-tweaks=vanilla</c>, the demo's map and skill and monsters on.
/// <c>spawn TYPE X Y [ANGLE]</c> (T6.9, debugging) spawns a mobj of <c>mobjtype_t</c>
/// <c>MT_TYPE</c> (e.g. <c>shadows</c>, the spectre, which DOOM1's maps lack) on the floor at
/// map point X, Y facing ANGLE (degrees, default 0) through <c>P_SpawnMobj</c>, before the next
/// tic as <c>missile</c>; e.g. <c>power invisibility; spawn shadows 1100 -3600 180; cmd 0 0 0 0 1; shot FILE.png</c>.
/// </para>
/// <para>
/// The game states (T7.1, <see cref="GameFlow"/>): tics (queued or live) run
/// in every state, the title loop, the intermission and the finale too, and
/// a queued tic is the console player's <c>ticcmd</c> there (fire or use skip
/// the intermission; <c>cmd 0 0 0 129</c>, <c>BT_SPECIAL | BTS_PAUSE</c>,
/// toggles the pause). <c>newgame [SKILL [EPISODE [MAP]]]</c> starts a new
/// game (g_game.c <c>G_DeferedInitNew</c>, as the menus' New Game; default
/// <c>--level-skill</c>'s skill, episode 1, map 1) at once; <c>title</c>
/// goes back to the title loop (<c>D_StartTitle</c>); <c>gamestate NAME</c>
/// waits for the queued tics and fails unless the game state is NAME
/// (<c>level</c>, <c>intermission</c>, <c>finale</c> or <c>demoscreen</c>,
/// the title loop); <c>skip [TICS]</c> waits for the queued tics, then
/// queues tics pressing and releasing use, one at a time, until the
/// intermission or the finale is over (the level or the title loop shows),
/// and fails after TICS tics (default 3000); on the finale it stops at the
/// end picture, which stays until the menus start or end a game (T7.2). E.g. after an exit:
/// <c>route tests/IsoDoom.Tests/Sim/Routes/e1m1-exit.route; gamestate intermission; skip; map E1M2 1</c>.
/// While a screen wipe melts (T7.1a) no tic runs and queued tics wait;
/// <c>wipe STEP [N]</c> holds the melt under way (or the next; with N, the
/// scene's wipe number N) at melt step STEP and waits for it, so <c>shot</c>
/// captures that frame, and <c>wipe</c> lets it go on and waits until it is over;
/// <c>wipehold STEP [N]</c> sets the hold without waiting (before the tics
/// that start the wipe are queued: queueing takes frames, in which the melt runs on).
/// </para>
/// <para>
/// The menus (T7.2, <see cref="MMenu"/>) take the keys, the pad and the
/// mouse as a person's: <c>tap Escape</c> opens them, <c>tap Down</c>,
/// <c>tap Return</c>, <c>tap BackSpace</c>, <c>tap Y</c>, <c>joybutton A down</c>,
/// <c>joybutton DpadDown down</c>, <c>mouse X Y; click</c> (the click at the
/// last <c>mouse</c> point) and <c>rclick</c> (the right button) work them;
/// <c>menu [NAME [ITEM]]</c> prints what they show after a frame and fails
/// unless it is NAME (<c>closed</c>, <c>message</c>, <c>main</c>,
/// <c>episode</c>, <c>skill</c>, <c>options</c>, <c>sound</c>, <c>load</c>,
/// <c>save</c>, <c>readthis1</c>, <c>readthis2</c>; T7.3: <c>setup</c>, <c>video</c>,
/// <c>gameplay</c>, <c>sprites</c>, <c>hud</c>, T7.8g: <c>soundsetup</c>, <c>controls</c>, <c>movement</c>,
/// <c>actions</c>, <c>menukeys</c>, <c>binding</c> while one waits for an input to
/// bind) with the skull (or the cursor) on item ITEM.
/// </para>
/// <para>
/// The settings (T7.3, <see cref="Settings"/>): <c>setting KEY</c> prints one
/// (e.g. <c>gameplay/cutaway</c>, <c>controls/attack</c>), <c>setting KEY VALUE</c>
/// changes it as the options menu does (applied at once, saved when the run
/// has a settings file: <c>--settings=FILE</c>), <c>checksetting KEY VALUE</c>
/// fails the script unless it is VALUE, <c>settings</c> prints them all.
/// A script runs on the defaults unless <c>--settings=FILE</c> names a file.
/// </para>
/// <para>
/// Saving and loading (T7.6): <c>save SLOT [DESCRIPTION]</c> waits for the
/// queued tics and saves the game to slot SLOT (0–5) at once
/// (<see cref="GameFlow.G_DoSaveGame"/>; the menus' save goes with the next
/// tic, <c>BTS_SAVEGAME</c>), failing unless it saved; <c>load SLOT</c>
/// waits for the queued tics and loads it (<see cref="GameFlow.G_LoadGame"/>,
/// run between the frames' tics as the menus' load; the queued tics are
/// dropped, as by a new game), failing when it is refused, and
/// <c>load SLOT refused</c> fails unless it is. A script saves to a
/// directory of its own (removed at the end) unless <c>--savedir=DIR</c>
/// names one: <c>route …; tics 600; save 0; checksum; load 0; checksum</c>.
/// </para>
/// </summary>
public partial class LevelScript : Node
{
    private readonly LevelScene _scene;
    private readonly string[] _commands;

    public LevelScript(LevelScene scene, string script)
    {
        _scene = scene;
        _commands = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public override void _Ready()
    {
        // Scripted tics from the first frame, so no input tic runs before the first command.
        foreach (string command in _commands)
        {
            if (command.StartsWith("cmd ", StringComparison.Ordinal) || command.StartsWith("step ", StringComparison.Ordinal)
                || command.StartsWith("route ", StringComparison.Ordinal) || command.StartsWith("demo ", StringComparison.Ordinal)
                || command == "sim scripted")
            {
                _scene.ScriptedTics = true;
                break;
            }
        }
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        int exit = 0;
        await Frames(2);
        foreach (string command in _commands)
        {
            string[] w = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            GD.Print($"Level script: {command}");
            try
            {
                switch (w[0])
                {
                    case "down": Key(w[1], true); break;
                    case "up": Key(w[1], false); break;
                    case "tap": Key(w[1], true); await Frames(1); Key(w[1], false); break;
                    case "hold": Key(w[1], true); await Frames(Int(w[2])); Key(w[1], false); break;
                    case "wait": await Frames(Int(w[1])); break;
                    case "click": Click(MouseButton.Left); break;
                    case "rclick": Click(MouseButton.Right); break;
                    case "menu":
                        {
                            // T7.2: prints the menus' state after a frame; fails unless it is NAME (and item ITEM)
                            await Frames(1);
                            MMenu menu = _scene.Menu;
                            GD.Print($"Level script: menu: {menu.StateText()}");
                            if (w.Length > 1 && (!string.Equals(menu.StateName, w[1], StringComparison.OrdinalIgnoreCase)
                                || w.Length > 2 && menu.itemOn != Int(w[2])))
                            {
                                GD.PrintErr($"Level script: menu: expected {w[1]}{(w.Length > 2 ? $" item {w[2]}" : "")}, the menus show {menu.StateText()}");
                                exit = 1;
                            }
                            break;
                        }
                    case "look":
                        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(Int(w[1]), Int(w[2])) });
                        break;
                    case "wheel":
                        for (int i = 0, n = w.Length > 2 ? Int(w[2]) : 1; i < n; i++)
                            Wheel(w[1] == "up" ? MouseButton.WheelUp : MouseButton.WheelDown);
                        break;
                    case "print": GD.Print($"Level script: overlay:\n{_scene.OverlayText()}"); break;
                    case "shot": exit |= await Shot(w[1]); break;
                    case "view":
                        _scene.PlaceFreeFly(Int(w[1]), Int(w[2]), float.Parse(w[3], CultureInfo.InvariantCulture), w.Length > 4 ? Int(w[4]) : null);
                        break;
                    case "fov":
                        if (_scene.FreeFly is { } fly)
                            fly.Fov = w[1] == "vanilla" ? FreeFlyCamera.VanillaFov : float.Parse(w[1], CultureInfo.InvariantCulture);
                        break;
                    case "mouse":
                        _mouse = new Vector2(Int(w[1]), Int(w[2]));
                        Input.ParseInputEvent(new InputEventMouseMotion { Position = _mouse, GlobalPosition = _mouse });
                        break;
                    case "place":
                        _scene.PlacePlayer(Int(w[1]), Int(w[2]), w.Length > 3 ? float.Parse(w[3], CultureInfo.InvariantCulture) : null);
                        break;
                    case "visible":
                        exit |= await Visible(w.Length > 1 ? Int(w[1]) : DefaultMinVisible, true) is null ? 1 : 0;
                        break;
                    case "walkto":
                        exit |= await WalkTo(Int(w[1]), Int(w[2]), w.Length > 3 ? Int(w[3]) : 32, w.Length > 4 ? Int(w[4]) : DefaultMinVisible);
                        break;
                    case "tour":
                        exit |= await Tour(w.Length > 1 ? Int(w[1]) : 32, w.Length > 2 ? Int(w[2]) : DefaultMinVisible);
                        break;
                    case "frametime": await FrameTime(w.Length > 1 ? Int(w[1]) : 300); break;
                    case "things":
                        if (_scene.Things is { } things)
                            things.Visible = things.Shadows.Visible = w[1] == "on";
                        break;
                    case "ticcmd":
                        {
                            var cmd = _scene.BuildTiccmd(w.Length > 1 && w[1] == "vanilla" ? IsoDoom.Sim.Tweaks.Vanilla : IsoDoom.Sim.Tweaks.TopDown);
                            GD.Print($"Level script: ticcmd forwardmove {cmd.forwardmove} sidemove {cmd.sidemove} angleturn {cmd.angleturn} ({(ushort)cmd.angleturn * 360.0 / 65536:0.##} deg) buttons {cmd.buttons}");
                            break;
                        }
                    case "cmd":
                        {
                            var cmd = new IsoDoom.Sim.ticcmd_t
                            {
                                forwardmove = checked((sbyte)Int(w[1])),
                                sidemove = checked((sbyte)Int(w[2])),
                                angleturn = unchecked((short)Int(w[3])),
                                buttons = w.Length > 4 ? checked((byte)Int(w[4])) : (byte)0,
                            };
                            _scene.ScriptedTics = true;
                            for (int i = 0, n = w.Length > 5 ? Int(w[5]) : 1; i < n; i++)
                                _scene.QueueTic(cmd);
                            break;
                        }
                    case "step":
                        _scene.ScriptedTics = true;
                        for (int i = 0, n = Int(w[1]); i < n; i++)
                            _scene.QueueTic(null);
                        await Drain();
                        break;
                    case "sim":
                        _scene.ScriptedTics = w[1] switch
                        {
                            "scripted" => true,
                            "live" => false,
                            _ => throw new ArgumentException($"sim: \"{w[1]}\" (live or scripted)"),
                        };
                        if (!_scene.ScriptedTics)
                            _scene.ClearQueuedTics();
                        break;
                    case "tics":
                        {
                            long until = _scene.TicsRun + Int(w[1]);
                            while (_scene.TicsRun < until && !(_scene.ScriptedTics && _scene.QueuedTics == 0) && _scene.TicsCanRun)
                                await Frames(1);
                            break;
                        }
                    case "checksum":
                        await Drain();
                        PrintChecksum();
                        break;
                    case "smooth":
                        if (w.Length > 2)
                            await SmoothSector(Int(w[1]), Int(w[2]));
                        else
                            await Smooth(Int(w[1]));
                        break;
                    case "plane":
                        _scene.MovePlane(Int(w[1]), w[2] switch
                        {
                            "floor" => false,
                            "ceiling" => true,
                            _ => throw new ArgumentException($"plane: \"{w[2]}\" (floor or ceiling)"),
                        }, Int(w[3]), w.Length > 4 ? Int(w[4]) : 2);
                        break;
                    case "tictime": await TicTime(Int(w[1])); break;
                    case "missile":
                        {
                            var type = w.Length > 1
                                ? Enum.Parse<IsoDoom.Sim.mobjtype_t>("MT_" + w[1].ToUpperInvariant())
                                : IsoDoom.Sim.mobjtype_t.MT_ROCKET;
                            _scene.BeforeNextTic(world =>
                            {
                                if (world.players[world.consoleplayer].mo is { } mo)
                                    world.P_SpawnPlayerMissile(mo, type);
                            });
                            break;
                        }
                    case "spawn":
                        {
                            // T6.9: a debug spawn (P_SpawnMobj on the floor), e.g. a spectre.
                            var type = Enum.Parse<IsoDoom.Sim.mobjtype_t>("MT_" + w[1].ToUpperInvariant());
                            int x = Int(w[2]), y = Int(w[3]);
                            uint angle = w.Length > 4 ? ThingSprites.BamOfDegrees(double.Parse(w[4], CultureInfo.InvariantCulture)) : 0;
                            _scene.BeforeNextTic(world =>
                            {
                                IsoDoom.Sim.mobj_t mo = world.P_SpawnMobj(x << 16, y << 16, IsoDoom.Sim.World.ONFLOORZ, type);
                                mo.angle = angle;
                            });
                            break;
                        }
                    case "wake":
                        // T6.13 (debugging): every live monster wakes and hunts the player, as A_Look
                        // would on seeing it (target, see state), ambush or not: the tic budget's worst case.
                        _scene.BeforeNextTic(world =>
                        {
                            IsoDoom.Sim.mobj_t? player = world.players[world.consoleplayer].mo;
                            if (player is null)
                                return;
                            int woken = 0;
                            foreach (IsoDoom.Sim.mobj_t m in System.Linq.Enumerable.ToList(world.Mobjs()))
                            {
                                if ((m.flags & IsoDoom.Sim.mobjflag_t.MF_COUNTKILL) == 0 || m.health <= 0)
                                    continue;
                                m.flags &= ~IsoDoom.Sim.mobjflag_t.MF_AMBUSH;
                                m.target = player;
                                if (m.state == m.info.spawnstate)
                                    world.P_SetMobjState(m, m.info.seestate);
                                woken++;
                            }
                            GD.Print($"Level script: wake: {woken} monsters hunt the player");
                        });
                        break;
                    case "power":
                        {
                            // T6.8: a debug power-up, as its pickup gives it (P_GivePower), optionally with TICS left.
                            var power = Enum.Parse<IsoDoom.Sim.powertype_t>("pw_" + w[1].ToLowerInvariant());
                            int? left = w.Length > 2 ? Int(w[2]) : null;
                            _scene.BeforeNextTic(world =>
                            {
                                IsoDoom.Sim.player_t p = world.players[world.consoleplayer];
                                if (p.mo is null)
                                    return;
                                IsoDoom.Sim.World.P_GivePower(p, power);
                                if (left is int tics)
                                    p.powers[(int)power] = tics;
                            });
                            break;
                        }
                    case "flash":
                        {
                            // T6.8: a debug palette flash: the player's damagecount or bonuscount.
                            int count = Int(w[2]);
                            bool damage = w[1] switch
                            {
                                "damage" => true,
                                "bonus" => false,
                                _ => throw new ArgumentException($"flash: \"{w[1]}\" (damage or bonus)"),
                            };
                            _scene.BeforeNextTic(world =>
                            {
                                IsoDoom.Sim.player_t p = world.players[world.consoleplayer];
                                if (damage)
                                    p.damagecount = count;
                                else
                                    p.bonuscount = count;
                            });
                            break;
                        }
                    case "route":
                        if (!(w.Length > 2 && int.TryParse(w[^1], out int routeTics) && routeTics >= 0
                                ? QueueRoute(string.Join(' ', w[1..^1]), routeTics)
                                : QueueRoute(string.Join(' ', w[1..]))))
                        {
                            GetTree().Quit(1);
                            return;
                        }
                        break;
                    case "demo":
                        if (w.Length is < 2 or > 3 || !(w.Length == 2 ? QueueDemo(w[1].ToUpperInvariant())
                                : int.TryParse(w[2], out int demoTics) && demoTics >= 0 && QueueDemo(w[1].ToUpperInvariant(), demoTics)))
                        {
                            if (w.Length is < 2 or > 3)
                                GD.PrintErr("Level script: expected demo LUMP [TICS]");
                            GetTree().Quit(1);
                            return;
                        }
                        break;
                    case "map":
                        {
                            await Drain();
                            string now = _scene.Mesh?.Level.Name ?? "no map";
                            string status = _scene.World is { } world && world.players[world.consoleplayer] is { mo: not null } p
                                ? LevelScene.StatusText(world, p) : "no player";
                            GD.Print($"Level script: map {now} ({_scene.LevelsCompleted} completed by exits, game: {_scene.Flow.StateText()}): {status}");
                            if (!string.Equals(now, w[1], StringComparison.OrdinalIgnoreCase))
                            {
                                GD.PrintErr($"Level script: map: expected {w[1].ToUpperInvariant()}, the scene shows {now}");
                                exit = 1;
                            }
                            if (w.Length > 2 && _scene.LevelsCompleted != Int(w[2]))
                            {
                                GD.PrintErr($"Level script: map: expected {Int(w[2])} map(s) completed by exits, there were {_scene.LevelsCompleted}");
                                exit = 1;
                            }
                            break;
                        }
                    case "reborns":
                        {
                            // T6.12: waits for the queued tics; fails unless the scene reloaded the level for a reborn N times
                            await Drain();
                            string status = _scene.World is { } world && world.players[world.consoleplayer] is { mo: not null } p
                                ? LevelScene.StatusText(world, p) : "no player";
                            GD.Print($"Level script: reborns {_scene.Reborns} on {_scene.Mesh?.Level.Name ?? "no map"}: {status}");
                            if (_scene.Reborns != Int(w[1]))
                            {
                                GD.PrintErr($"Level script: reborns: expected {Int(w[1])}, there were {_scene.Reborns}");
                                exit = 1;
                            }
                            break;
                        }
                    case "newgame":
                        // T7.1: G_DeferedInitNew (the menus' New Game, T7.2): [SKILL 1-5 [EPISODE [MAP]]], started before the next tic
                        _scene.StartNewGame(w.Length > 1 ? (IsoDoom.Sim.skill_t)(Math.Clamp(Int(w[1]), 1, 5) - 1) : null,
                            w.Length > 2 ? Int(w[2]) : 1, w.Length > 3 ? Int(w[3]) : 1);
                        while (_scene.Flow.gameaction == IsoDoom.Sim.gameaction_t.ga_newgame)
                            await Frames(1); // the scene's _Process runs the game action
                        break;
                    case "title":
                        _scene.Flow.D_StartTitle(null); // T7.1: back to the title loop (d_main.c D_StartTitle)
                        break;
                    case "gamestate":
                        {
                            // T7.1: waits for the queued tics (and a title loop step due); fails unless the game state is NAME
                            await Drain();
                            while (_scene.Flow.advancedemo && !(_scene.ScriptedTics && _scene.QueuedTics == 0))
                                await Frames(1);
                            string state = GameFlow.StateName(_scene.GameState);
                            GD.Print($"Level script: gamestate {state}: {_scene.Flow.StateText()}{(_scene.LevelEnded is { } ended ? $" ({ended})" : "")}");
                            if (!string.Equals(state, w[1], StringComparison.OrdinalIgnoreCase))
                            {
                                GD.PrintErr($"Level script: gamestate: expected {w[1]}, the game is at {state}");
                                exit = 1;
                            }
                            break;
                        }
                    case "wipe":
                        exit |= await Wipe(w.Length > 1 ? Int(w[1]) : null, w.Length > 2 ? Int(w[2]) : null);
                        break;
                    case "wipehold":
                        // T7.1a: as wipe STEP [N], without waiting (set before the tics that start the wipe are queued)
                        _scene.WipeHold = Int(w[1]);
                        _scene.WipeHoldCount = w.Length > 2 ? Int(w[2]) : null;
                        break;
                    case "skip":
                        {
                            int skipped = await Skip(w.Length > 1 ? Int(w[1]) : 3000);
                            GD.Print($"Level script: skip: {(skipped < 0 ? "still" : $"{skipped} tic(s), now")} {_scene.Flow.StateText()}");
                            if (skipped < 0)
                            {
                                GD.PrintErr("Level script: skip: the intermission or the finale did not end");
                                exit = 1;
                            }
                            break;
                        }
                    case "joy":
                        Input.ParseInputEvent(new InputEventJoypadMotion { Device = 0, Axis = Enum.Parse<JoyAxis>(w[1], true), AxisValue = float.Parse(w[2], CultureInfo.InvariantCulture) });
                        break;
                    case "joybutton":
                        Input.ParseInputEvent(new InputEventJoypadButton { Device = 0, ButtonIndex = Enum.Parse<JoyButton>(w[1], true), Pressed = w[2] == "down", Pressure = w[2] == "down" ? 1 : 0 });
                        break;
                    case "setting":
                        // T7.3: prints setting KEY, or sets it to VALUE as the options menu does (applied at once, saved to --settings's file)
                        if (w.Length > 2)
                            _scene.SetSetting(w[1], string.Join(' ', w[2..]));
                        GD.Print($"Level script: setting {w[1]} = {_scene.GetSetting(w[1])}");
                        break;
                    case "checksetting":
                        {
                            // T7.3: fails unless setting KEY is VALUE now
                            string value = string.Join(' ', w[2..]), now = _scene.GetSetting(w[1]);
                            GD.Print($"Level script: checksetting {w[1]} = {now}");
                            if (now != value)
                            {
                                GD.PrintErr($"Level script: checksetting: {w[1]} is \"{now}\", expected \"{value}\"");
                                exit = 1;
                            }
                            break;
                        }
                    case "settings":
                        // T7.3: every setting's value, and the file
                        GD.Print($"Level script: settings ({_scene.SettingsPath ?? "no file"}):");
                        foreach (string key in LevelScene.SettingKeys())
                            GD.Print($"  {key} = {_scene.GetSetting(key)}{(_scene.SettingsValues.IsPinned(key) ? " (command line)" : "")}");
                        GD.Print($"  (the window: {DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y} {DisplayServer.WindowGetMode()}, vsync {DisplayServer.WindowGetVsyncMode()}, Engine.MaxFps {Engine.MaxFps})");
                        break;
                    case "save":
                        {
                            // T7.6: waits for the queued tics, then saves the game at once to slot SLOT (G_DoSaveGame); fails unless it saved
                            await Drain();
                            GameFlow flow = _scene.Flow;
                            flow.savegameslot = Math.Clamp(Int(w[1]), 0, 5);
                            flow.savedescription = w.Length > 2 ? string.Join(' ', w[2..]).ToUpperInvariant() : "SCRIPT";
                            if (!flow.G_DoSaveGame())
                            {
                                GD.PrintErr($"Level script: save: nothing saved to slot {flow.savegameslot}");
                                exit = 1;
                            }
                            break;
                        }
                    case "load":
                        {
                            // T7.6: waits for the queued tics, then loads slot SLOT (G_LoadGame, before the next frame's tics); fails when it is refused, unless "refused" follows
                            await Drain();
                            GameFlow flow = _scene.Flow;
                            flow.G_LoadGame(Math.Clamp(Int(w[1]), 0, 5));
                            while (flow.gameaction == IsoDoom.Sim.gameaction_t.ga_loadgame)
                                await Frames(1); // the scene's _Process runs the game action
                            bool expectRefused = w.Length > 2 && w[2] == "refused";
                            GD.Print($"Level script: load {w[1]}: {(flow.LoadRefused is { } why ? "refused: " + why.Replace('\n', ' ') : $"{_scene.Mesh?.Level.Name} at tic {_scene.World?.leveltime}")}");
                            if ((flow.LoadRefused is not null) != expectRefused)
                            {
                                GD.PrintErr($"Level script: load: {(expectRefused ? "loaded, expected a refusal" : "refused")}");
                                exit = 1;
                            }
                            break;
                        }
                    case "channels":
                        // T7.7: waits for the queued tics; prints the channels playing and the last sounds started
                        await Drain();
                        GD.Print($"Level script: channels (sfx volume {_scene.Sound?.snd_SfxVolume}, stereo {_scene.Stereo.ToString().ToLowerInvariant()}):\n{_scene.ChannelsText()}");
                        if (_scene.Sound is { } snd)
                            GD.Print("Level script: last starts: " + string.Join(", ", snd.Log.TakeLast(8).Select(st => _scene.StartText(st))));
                        break;
                    case "playing":
                    case "heard":
                        exit |= await CheckSound(w[0] == "playing", w);
                        break;
                    case "music":
                        exit |= await CheckMusic(w);
                        break;
                    case "sleep":
                        {
                            // T7.8e: waits SECONDS of wall-clock time (frames go on; --fixed-fps makes them fast)
                            ulong until = Time.GetTicksMsec() + (ulong)(double.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) * 1000);
                            while (Time.GetTicksMsec() < until)
                                await Frames(1);
                            break;
                        }
                    case "close":
                        // T7.8e: the window's close request, as the window manager sends it (the scene fades the music out and quits)
                        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
                        await Frames(1000000); // the quit ends the script
                        break;
                    case "underruns":
                        exit |= CheckUnderruns(w);
                        break;
                    case "quit": await _scene.QuitQuietly(exit); return;
                    default: throw new ArgumentException($"unknown command \"{w[0]}\"");
                }
            }
            catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException or FormatException or OverflowException)
            {
                GD.PrintErr($"Level script: bad command \"{command}\": {e.Message}");
                GetTree().Quit(1);
                return;
            }
            await Frames(1); // let the event reach the nodes
        }
        await _scene.QuitQuietly(exit);
    }

    /// <summary>
    /// T7.7: <c>playing NAME [ORIGIN]</c> fails unless a channel plays sound
    /// NAME (<c>sfx_</c> left out) now, from ORIGIN if given (<c>player</c>,
    /// <c>-</c> for none, <c>sector N</c> as <c>sector:N</c>, or a mobj type
    /// such as <c>troop</c>); <c>heard NAME [MIN]</c> fails unless NAME took a
    /// channel at least MIN times (default 1) since the scene started. Both
    /// wait for the queued tics first.
    /// </summary>
    private async Task<int> CheckSound(bool playing, string[] w)
    {
        await Drain();
        if (_scene.Sound is not { } snd)
        {
            GD.PrintErr("Level script: no sound");
            return 1;
        }
        var sfx = Enum.Parse<IsoDoom.Sim.sfxenum_t>("sfx_" + w[1].ToLowerInvariant());
        if (!playing)
        {
            int min = w.Length > 2 ? Int(w[2]) : 1, times = snd.ChannelStarts[(int)sfx];
            GD.Print($"Level script: heard {w[1]}: {times} time(s)");
            if (times >= min)
                return 0;
            GD.PrintErr($"Level script: heard: {w[1]} took a channel {times} time(s), expected at least {min}");
            return 1;
        }
        string? origin = w.Length > 2 ? string.Join(' ', w[2..]).Replace(':', ' ') : null;
        IsoDoom.Sim.mobj_t? player = _scene.PlayerMobj;
        bool found = snd.channels.Any(c => c.sfxinfo is not null && c.sfx == sfx
            && (origin is null || IsoDoom.Audio.SSound.OriginText(c.origin, player) == origin));
        GD.Print($"Level script: playing {w[1]}{(origin is null ? "" : $"@{origin}")}: {(found ? "yes" : "no")}\n{_scene.ChannelsText()}");
        if (found)
            return 0;
        GD.PrintErr($"Level script: playing: no channel plays {w[1]}{(origin is null ? "" : $" from {origin}")}");
        return 1;
    }

    /// <summary>
    /// T7.8e: <c>underruns [MAX]</c> prints the music player's state (the
    /// driver, the ring buffer's level, the underruns, the frames rendered and
    /// their cost) and fails unless its thread runs and the underruns since
    /// the buffer was first full are at most MAX (default 0).
    /// </summary>
    private int CheckUnderruns(string[] w)
    {
        long max = w.Length > 1 ? long.Parse(w[1], System.Globalization.CultureInfo.InvariantCulture) : 0;
        if (_scene.MusicDevice is not { } player)
        {
            GD.PrintErr("Level script: underruns: no music player");
            return 1;
        }
        GD.Print($"Level script: underruns: {player}");
        if (player.Running && player.Underruns <= max)
            return 0;
        GD.PrintErr($"Level script: underruns: {player.Underruns} (at most {max}){(player.Running ? "" : ", the thread does not run")}");
        return 1;
    }

    /// <summary>
    /// T7.8c: <c>music [NAME]</c> waits for the queued tics and prints the
    /// song playing (and the music device's state); with NAME it fails unless
    /// the song is NAME (<c>e1m1</c>, <c>D_E1M1</c>, <c>intro</c> for the
    /// <c>introa</c> it plays, or <c>none</c>).
    /// </summary>
    private async Task<int> CheckMusic(string[] w)
    {
        await Drain();
        if (_scene.Sound is not { } snd)
        {
            GD.PrintErr("Level script: no sound");
            return 1;
        }
        GD.Print($"Level script: music: {_scene.MusicText()}");
        if (w.Length < 2)
            return 0;
        string want = w[1].ToLowerInvariant();
        if (want.StartsWith("d_", StringComparison.Ordinal))
            want = want[2..];
        if (want.StartsWith("mus_", StringComparison.Ordinal))
            want = want[4..];
        IsoDoom.Sim.musicenum_t playing = snd.mus_playing;
        if (want == IsoDoom.Audio.SSound.MusicName(playing) || playing != IsoDoom.Sim.musicenum_t.mus_None && want == IsoDoom.Audio.SSound.MusicName(snd.mus_requested))
            return 0;
        GD.PrintErr($"Level script: music: {IsoDoom.Audio.SSound.MusicName(playing)} plays, expected {w[1]}");
        return 1;
    }

    /// <summary>Waits until the scripted tics queued so far ran (a map without a player runs none).</summary>
    private async Task Drain()
    {
        while (_scene.QueuedTics > 0 && _scene.TicsCanRun)
            await Frames(1);
    }

    /// <summary>
    /// T7.1: <c>skip [TICS]</c>: waits for the queued tics, then queues one
    /// tic at a time, use pressed and released in turn (as a player skipping
    /// the screens), until the game state is the level or the title loop, or
    /// the finale shows its end picture (which stays, as vanilla's, T7.2);
    /// fails after TICS tics (default 3000). Returns the tics it ran, or −1.
    /// </summary>
    private async Task<int> Skip(int max)
    {
        await Drain();
        _scene.ScriptedTics = true;
        int tics = 0;
        // T7.2: the finale's end picture stays until the menu starts or ends a game, so skip stops there
        while (_scene.GameState is gamestate_t.GS_INTERMISSION
            || _scene.GameState == gamestate_t.GS_FINALE && !(_scene.Flow.Finale.finalestage == 1 && _scene.Flow.gamemode != IsoDoom.Wad.GameMode.commercial))
        {
            if (tics >= max)
                return -1;
            _scene.QueueTic(new IsoDoom.Sim.ticcmd_t { buttons = tics % 2 == 0 ? IsoDoom.Sim.buttoncode_t.BT_USE : (byte)0 });
            tics++;
            await Drain();
        }
        return tics;
    }

    private void PrintChecksum()
    {
        if (_scene.World is not { } world)
        {
            GD.PrintErr("Level script: checksum: no world");
            return;
        }
        string player = _scene.PlayerMobj is { } mo
            ? $"; player x {mo.x} y {mo.y} z {mo.z} ({mo.x / 65536.0:F2}, {mo.y / 65536.0:F2}, {mo.z / 65536.0:F2}) angle {mo.angle} momx {mo.momx} momy {mo.momy} {mo.state}"
            : "; no player";
        GD.Print($"Level script: checksum leveltime {world.leveltime} checksum {world.Checksum():x16}{player}");
    }

    /// <summary>Records the drawn player position over <paramref name="n"/> frames and prints the steps between frames (map units).</summary>
    private async Task Smooth(int n)
    {
        if (_scene.Player is not { } p)
            throw new ArgumentException("no player");
        var steps = new List<float>();
        Vector2 last = p.MapPosition;
        long tics = _scene.TicsRun;
        for (int i = 0; i < n; i++)
        {
            await Frames(1);
            Vector2 at = p.MapPosition;
            steps.Add(at.DistanceTo(last));
            last = at;
        }
        float jerk = 0; // the largest change between consecutive steps: small while the speed changes smoothly
        for (int i = 1; i < steps.Count; i++)
            jerk = Math.Max(jerk, Math.Abs(steps[i] - steps[i - 1]));
        steps.Sort();
        float sum = 0;
        foreach (float s in steps)
            sum += s;
        string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        GD.Print($"Level script: smooth: {n} frames, {_scene.TicsRun - tics} tics; player step per frame min {F(steps[0])} median {F(steps[steps.Count / 2])} max {F(steps[^1])} mean {F(sum / steps.Count)} units, largest change between consecutive steps {F(jerk)}");
    }

    /// <summary>
    /// T5.1: records sector <paramref name="sector"/>'s drawn floor and ceiling
    /// (its data texel) over <paramref name="n"/> frames and prints the steps
    /// between frames (interpolation: even steps while a plane moves at a steady speed).
    /// </summary>
    private async Task SmoothSector(int n, int sector)
    {
        if (_scene.Mesh is not { } mesh || sector < 0 || sector >= mesh.Level.Sectors.Length)
            throw new ArgumentException($"no sector {sector}");
        var steps = new List<float>();
        Color last = mesh.SectorData(sector);
        long tics = _scene.TicsRun;
        for (int i = 0; i < n; i++)
        {
            await Frames(1);
            Color at = mesh.SectorData(sector);
            steps.Add(Math.Abs(at.R - last.R) + Math.Abs(at.G - last.G));
            last = at;
        }
        float jerk = 0;
        for (int i = 1; i < steps.Count; i++)
            jerk = Math.Max(jerk, Math.Abs(steps[i] - steps[i - 1]));
        steps.Sort();
        string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
        GD.Print($"Level script: smooth: sector {sector}, {n} frames, {_scene.TicsRun - tics} tics; drawn floor + ceiling step per frame min {F(steps[0])} "
            + $"median {F(steps[steps.Count / 2])} max {F(steps[^1])} units, largest change between consecutive steps {F(jerk)}; now floor {F(last.R)}, ceiling {F(last.G)}");
    }

    /// <summary>
    /// Times <see cref="World.G_Ticker(in ticcmd_t)"/> over the next
    /// <paramref name="n"/> tics (T4.9, SPEC §9: under 2 ms a tic) and prints
    /// the mean, median, 95th percentile and worst in ms.
    /// </summary>
    private async Task TicTime(int n)
    {
        if (_scene.World is not { } world)
            throw new ArgumentException("no world");
        var times = new List<double>(n);
        int gcs = GC.CollectionCount(0), start = world.leveltime;
        _scene.TicTimes = times;
        while (times.Count < n && !(_scene.ScriptedTics && _scene.QueuedTics == 0) && _scene.PlayerMobj is not null)
            await Frames(1);
        _scene.TicTimes = null;
        gcs = GC.CollectionCount(0) - gcs;
        int worstTic = times.Count == 0 ? 0 : start + 1 + times.IndexOf(System.Linq.Enumerable.Max(times));
        if (times.Count == 0)
        {
            GD.PrintErr("Level script: tictime: no tic ran");
            return;
        }
        double mean = 0;
        foreach (double t in times)
            mean += t;
        mean /= times.Count;
        times.Sort();
        string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
        GD.Print($"Level script: tic time over {times.Count} tics, {System.Linq.Enumerable.Count(world.Mobjs())} mobjs: mean {F(mean)} ms, median {F(times[times.Count / 2])}, p95 {F(times[(int)(times.Count * 0.95)])}, worst {F(times[^1])} (tic {worstTic}); {gcs} garbage collections meanwhile");
    }

    /// <summary>
    /// T5.10: queues the tics of the route file at <paramref name="path"/>
    /// (and places the player at its <c>start</c>); false, with an error,
    /// when the scene cannot play it as vanilla does.
    /// </summary>
    private bool QueueRoute(string path, int? tics = null)
    {
        RouteFile route;
        try
        {
            route = RouteFile.Parse(path);
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or FormatException)
        {
            GD.PrintErr($"Level script: route: {e.Message}");
            return false;
        }
        return QueueRoute(route, $"route {path}", tics);
    }

    /// <summary>
    /// T6.13: queues the tics of the WAD's demo lump <paramref name="lump"/>
    /// (e.g. <c>DEMO1</c>) read by the vanilla-input adapter
    /// (<see cref="RouteFile.FromDemo"/>) as a route; false, with an error,
    /// when the WAD lacks it, the sim cannot play it or the scene does not
    /// match its map, skill and monsters.
    /// </summary>
    private bool QueueDemo(string lump, int? tics = null)
    {
        RouteFile route;
        try
        {
            IsoDoom.Wad.WadArchive wad = _scene.Wad ?? throw new FormatException("no WAD open");
            if (wad.W_CheckNumForName(lump) < 0)
                throw new FormatException($"the WAD has no lump {lump}");
            route = RouteFile.FromDemo(wad.W_CacheLumpName(lump).Span, lump, _scene.Mesh?.Level.Name.StartsWith("MAP", StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (Exception e) when (e is FormatException or NotSupportedException)
        {
            GD.PrintErr($"Level script: demo: {e.Message}");
            return false;
        }
        return QueueRoute(route, $"demo {lump}", tics);
    }

    /// <summary>Queues a parsed route's (or demo's) tics; <paramref name="what"/> names it in messages.</summary>
    private bool QueueRoute(RouteFile route, string what, int? tics)
    {
        string map = (route.Map ?? "E1M1").ToUpperInvariant();
        string? shown = _scene.Mesh?.Level.Name;
        var wrong = new List<string>();
        if (_scene.Tweaks != IsoDoom.Sim.Tweaks.Vanilla)
            wrong.Add("needs --level-tweaks=vanilla (a route is a vanilla demo)");
        if (_scene.NoMonsters == route.Monsters)
            wrong.Add(route.Monsters ? "needs --level-monsters=on (the route has monsters)" : "needs --level-monsters=off (the route is -nomonsters)");
        if ((int)_scene.Skill + 1 != route.Skill)
            wrong.Add($"needs --level-skill={route.Skill}");
        if (!string.Equals(shown, map, StringComparison.OrdinalIgnoreCase))
            wrong.Add($"is for {map}, the scene shows {shown ?? "no map"}");
        if (_scene.PlayerMobj is null)
            wrong.Add("no player");
        if (wrong.Count > 0)
        {
            GD.PrintErr($"Level script: {what}: {string.Join("; ", wrong)}");
            return false;
        }
        if (route.Start is { } s && !_scene.PlaceRouteStart(s.X, s.Y, s.Angle))
        {
            GD.PrintErr($"Level script: {what}: start {s.X} {s.Y}: something stands there");
            return false;
        }
        _scene.ScriptedTics = true;
        _scene.RouteStartPoint = route.Start; // T6.12: placed again after a reborn
        int count = Math.Min(tics ?? route.Cmds.Count, route.Cmds.Count);
        for (int tic = 0; tic < count; tic++)
        {
            int t = tic;
            _scene.QueueTic(route.Cmds[tic], System.Linq.Enumerable.Any(route.Events, e => e.Tic == t) ? world => route.RunEvents(world, t) : null);
        }
        GD.Print($"Level script: {what}: {count} tics queued on {map}{(count < route.Cmds.Count ? $" (of {route.Cmds.Count})" : route.Exit switch { 1 => ", ending at the exit", 2 => ", ending at the secret exit", _ => "" })}");
        return true;
    }

    /// <summary>The least share of the player's pixels (%) <c>visible</c> and <c>walkto</c> accept by default.</summary>
    public const int DefaultMinVisible = 25;

    /// <summary>Measures the player's visibility (%); null (and an error) when it is below <paramref name="min"/> or can't be measured.</summary>
    private async Task<double?> Visible(int min, bool print)
    {
        if (await _scene.PlayerVisibilityAsync() is not (int visible, int total))
        {
            GD.PrintErr("Level script: visible needs a real renderer and the player (run without --headless)");
            return null;
        }
        double share = total == 0 ? 0 : 100.0 * visible / total;
        Vector2 at = _scene.Player!.MapPosition;
        string where = $"player at ({at.X:F0}, {at.Y:F0}), z {_scene.Player.Z:F0}";
        if (total == 0 || share < min)
        {
            GD.PrintErr($"Level script: {where}: {visible} of {total} pixels visible ({share:F0}%), below {min}%");
            return null;
        }
        if (print)
            GD.Print($"Level script: {where}: {visible} of {total} pixels visible ({share:F0}%)");
        return share;
    }

    private async Task<int> WalkTo(int x, int y, int step, int min)
    {
        if (_scene.Player is not { } p || _scene.PlayerMobj is null)
            throw new ArgumentException("no player");
        Vector2 from = p.MapPosition, to = new(x, y);
        int steps = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / Math.Max(1, step)));
        int failed = 0;
        double least = double.MaxValue;
        Vector2 leastAt = from;
        for (int i = 1; i <= steps; i++)
        {
            Vector2 at = from.Lerp(to, (float)i / steps);
            _scene.PlacePlayer(at.X, at.Y);
            if (await Visible(min, false) is double share)
            {
                if (share < least)
                    (least, leastAt) = (share, at);
            }
            else
                failed++;
        }
        GD.Print(failed == 0
            ? $"Level script: walked to ({x}, {y}) in {steps} steps; least visible {least:F0}% at ({leastAt.X:F0}, {leastAt.Y:F0})"
            : $"Level script: walked to ({x}, {y}) in {steps} steps; {failed} step(s) below {min}%");
        return failed == 0 ? 0 : 1;
    }

    private async Task<int> Tour(int step, int min)
    {
        if (_scene.Mesh is not { } m || _scene.Player is not { } p || _scene.PlayerMobj is null)
            throw new ArgumentException("no map or player");
        var points = new List<Vector2>();
        foreach (SectorFloor floor in m.Floors.BySector)
        {
            if (floor.TriangleCount > 0)
            {
                (int x, int y) = floor.InteriorPoint();
                points.Add(new Vector2(MathF.Round(x / 65536f), MathF.Round(y / 65536f)));
            }
        }
        int exit = 0, legs = 0;
        while (points.Count > 0)
        {
            Vector2 at = p.MapPosition;
            int next = 0;
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].DistanceSquaredTo(at) < points[next].DistanceSquaredTo(at))
                    next = i;
            }
            Vector2 to = points[next];
            points.RemoveAt(next);
            exit |= await WalkTo((int)to.X, (int)to.Y, step, min);
            legs++;
        }
        GD.Print($"Level script: toured {legs} sector floors of {m.Level.Name}{(exit == 0 ? "" : " (some steps below the minimum)")}");
        return exit;
    }

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static void Key(string name, bool pressed)
    {
        Key key = OS.FindKeycodeFromString(name);
        if (key == Godot.Key.None)
            throw new ArgumentException($"unknown key \"{name}\"");
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });
    }

    // Where the last `mouse` put the cursor (a click happens there; T7.2's menus read it).
    private Vector2 _mouse;

    private void Click(MouseButton button)
    {
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Position = _mouse, GlobalPosition = _mouse });
    }

    private static void Wheel(MouseButton button)
    {
        bool ctrl = Input.IsPhysicalKeyPressed(Godot.Key.Ctrl);
        foreach (bool pressed in new[] { true, false })
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, CtrlPressed = ctrl, Factor = 1 });
    }

    /// <summary>Prints wall-clock frame times over <paramref name="n"/> frames (T3.8; after a few frames to settle).</summary>
    private async Task FrameTime(int n)
    {
        await Frames(10);
        var ms = new double[Math.Max(1, n)];
        ulong last = Time.GetTicksUsec();
        for (int i = 0; i < ms.Length; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ulong now = Time.GetTicksUsec();
            ms[i] = (now - last) / 1000.0;
            last = now;
        }
        double mean = 0;
        foreach (double t in ms)
            mean += t;
        mean /= ms.Length;
        Array.Sort(ms);
        string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        GD.Print($"Level script: frame time over {ms.Length} frames at {GetViewport().GetVisibleRect().Size}: mean {F(mean)} ms ({F(1000 / mean)} fps), median {F(ms[ms.Length / 2])}, p95 {F(ms[(int)(ms.Length * 0.95)])}, worst {F(ms[^1])}");
    }

    /// <summary>
    /// T7.1a: <c>wipe STEP [N]</c> holds the melt under way (or the next one:
    /// the queued tics run until it starts; with N, the scene's wipe number N
    /// since it started, the earlier ones running through) at step STEP and
    /// waits until it gets there, so <c>shot</c> captures that frame;
    /// <c>wipe</c> lets it go on and waits until it is over. Fails (1) when
    /// no melt gets there.
    /// </summary>
    private async Task<int> Wipe(int? step, int? number)
    {
        GameFlow flow = _scene.Flow;
        if (step is not int at)
        {
            _scene.WipeHold = null;
            _scene.WipeHoldCount = null;
            while (flow.Wipe.go)
                await Frames(1);
            GD.Print($"Level script: wipe: over after {flow.Wipe.steps} steps (wipes so far: {flow.Wipe.count})");
            return 0;
        }
        _scene.WipeHold = at;
        _scene.WipeHoldCount = number;
        int frames = 0;
        while (!_scene.WipeHeld)
        {
            bool waiting = flow.Wipe.go || _scene.TicsCanRun && !(_scene.ScriptedTics && _scene.QueuedTics == 0);
            if (!waiting || number is int n && flow.Wipe.count > n || ++frames > 35 * 120)
            {
                GD.PrintErr($"Level script: wipe {at}{(number is int k ? $" {k}" : "")}: no melt got there ({flow.StateText()}, wipes so far: {flow.Wipe.count}, the last {flow.Wipe.steps} steps)");
                _scene.WipeHold = null;
                _scene.WipeHoldCount = null;
                return 1;
            }
            await Frames(1);
        }
        await Frames(1); // its frame drawn (WipeView, every frame)
        GD.Print($"Level script: wipe: wipe {flow.Wipe.count} held at step {flow.Wipe.steps} ({flow.StateText()})");
        return 0;
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<int> Shot(string path)
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("Level script: shot needs a real renderer (run without --headless)");
            return 1;
        }
        for (int i = 0; i < 3; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(err == Error.Ok ? $"Level script: saved {path}" : $"Level script: could not save {path}: {err}");
        return err == Error.Ok ? 0 : 1;
    }
}
