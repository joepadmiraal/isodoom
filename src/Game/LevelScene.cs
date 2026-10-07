using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;
using IsoDoom.Wad;
using IsoDoom.Wad.Graphics;

namespace IsoDoom.Game;

/// <summary>
/// The level scene (T2.5, milestone M2), the game scene since T7.1: loads one map of the IWAD and shows
/// it as a textured mesh (<see cref="LevelMesh"/>) through the game camera
/// (<see cref="IsoCamera"/>, T3.3) following the player, or from a fixed overview camera
/// (<see cref="LevelCamera"/>) or the free-fly debug camera. Opened by <see cref="Main"/>:
/// at the title loop by default, or with the
/// user argument <c>--level MAP</c> (<c>godot -- --level E1M1</c>; without a
/// map name the first of <c>E1M1</c>/<c>MAP01</c>) straight into a new game on that map.
/// <para>
/// The game's flow (T7.1): <see cref="Flow"/> (<see cref="GameFlow"/>, g_game.c's game
/// states and actions and d_main.c's title loop) runs every tic, with this
/// scene as its <see cref="IGameHost"/>: the title loop's pages, a new game
/// (fire, use, Enter, Escape or the pad's Start on the title until the menus,
/// T7.2), each level, the intermission between levels (T7.4) and the
/// (placeholder) finale, drawn over the level by <see cref="Screens"/>; the
/// pause key (Pause, the pad's Back) pauses through the <c>ticcmd</c>
/// (vanilla's <c>BTS_PAUSE</c>), and the window's focus loss pauses too.
/// </para>
/// <para>
/// The game loop (T4.7): each map gets a <see cref="Sim.World"/> on the same
/// <see cref="Level"/> as the mesh; <see cref="_Process"/> runs it at
/// <see cref="SimInfo.TICRATE"/> from a time accumulator, one
/// <c>ticcmd</c> per tic (<see cref="BuildTiccmd(Tweaks)"/> from the game's
/// input while the game camera is current), whatever the frame rate, and
/// draws the mobjs between the last two tics (<see cref="TicFraction"/>,
/// <see cref="mobj_t.interp"/>): the player as <see cref="PlayerSprite"/>,
/// which the game camera follows, the others as <see cref="Things"/>. A level
/// script can play a <c>ticcmd</c> sequence instead (<see cref="QueueTic"/>).
/// </para>
/// <para>
/// More user arguments (debugging and checks):
/// <c>--level-view=iso|top</c> (camera angle, default iso);
/// <c>--level-focus=SECTOR</c> (frame one sector and its neighbours instead of the map);
/// <c>--level-tiling=vanilla|size</c> (<see cref="WallTextureTiling"/>);
/// <c>--level-sector-floor=SECTOR:HEIGHT[,SECTOR:HEIGHT…]</c> (set sector floors,
/// in map units, through the data texture after loading);
/// <c>--level-screenshot=FILE.png</c> (save a capture and quit; needs a real
/// renderer; the image is WAD data, keep it out of the repo);
/// <c>--level-camera=iso|overview|fly</c> (start with the game camera, the
/// overview or the free-fly camera, T2.7/T3.3; default iso);
/// <c>--level-projection=ortho|perspective</c> (the game camera's projection,
/// <see cref="IsoProjection"/>, default ortho); <c>--level-pitch=DEGREES</c>
/// (its pitch, 45–60, default 55); <c>--level-zoom=UNITS</c> (its view height
/// in map units, 320–1600, default 640);
/// <c>--level-script=COMMANDS</c> (feed scripted input: <see cref="LevelScript"/>);
/// <c>--level-cutaway=cut|dither|off</c> (the wall cutaway's style, T3.4,
/// <see cref="Cutaway"/>; default cut), <c>--level-cutaway-radius=UNITS</c>
/// (default 80), <c>--level-cutaway-height=UNITS</c> (the cutoff above the
/// player's floor, default 32), <c>--level-cutaway-cursor=on|off</c> (the
/// cursor ground point cuts too; default off),
/// <c>--level-cutaway-cap=dark|flat|off</c> (T3.4a, <see cref="CutawayCap"/>:
/// what a cut block shows inside; default dark);
/// <c>--level-cutaway-things=decor|all|off</c> (T3.4b, <see cref="CutawayThings"/>:
/// which thing billboards in front of the player the cutaway cuts as walls;
/// default decor, all but actors);
/// <c>--level-masked-back=mirror|off</c> (T3.1a, <see cref="MaskedBackFaces"/>:
/// a masked middle on one side of a line only is drawn from behind too,
/// mirrored, or as vanilla only from its own side; default mirror);
/// <c>--level-door-lids=on|off</c> (T6.13b, <see cref="DoorLidMode"/>: a lid
/// on the solid above doors, lintels and other thin low-ceilinged sectors, so
/// a closed door reads as a block; default on);
/// <c>--level-weapon-light=on|off</c> (T6.6, <see cref="WeaponLight"/>: the
/// player's weapon flash, <c>player_t.extralight</c>, lights the level as
/// vanilla's lights the view; default on);
/// <c>--level-hud=bar|full|off</c> (T6.11, <see cref="HudView"/>: under the
/// game camera, vanilla's status bar at the bottom, the minimal fullscreen
/// HUD, or neither; the message line shows with all; default bar) and
/// <c>--level-hud-scale=N|auto</c> (its scale; default auto, the largest
/// whole scale vanilla's 320×200 screen fits: 4 at 1280×800);
/// <c>--level-palette-effects=on|off</c> (T6.8, <see cref="PaletteEffects"/>:
/// the console player's palette flashes, st_stuff.c's red, gold and green,
/// and its power-ups' fixed colormaps, invulnerability's inverse map and the
/// light amplification visor's; default on);
/// <c>--level-skill=1-5</c> (the skill whose things are drawn, T3.5; default 3);
/// <c>--level-monsters=on|off</c> (T5.7: off spawns no monsters, vanilla's
/// <c>-nomonsters</c>, e.g. so the light specials' <c>P_Random</c> calls match
/// a vanilla reference run with it; default on);
/// <c>--level-things=on|off</c> (T3.8: off hides the billboards and the
/// player, as vanilla's reference renders draw no sprites; default on);
/// <c>--level-tweaks=topdown|vanilla</c> (T4.7: the sim's <see cref="Tweaks"/>
/// and the commands built for it: the twin-stick game, or vanilla's relative
/// movement and turning; default topdown);
/// <c>--level-aim-assist=DEG|off</c> (T6.7: the aim assist's half-cone,
/// <see cref="Tweaks.AimAssistCone"/>, 0–45°; default 5 with topdown, off with vanilla);
/// <c>--level-sprite-tilt=0-1|off|half|full</c> (billboards turn towards the
/// camera by this fraction of its elevation, T3.6, <see cref="SpriteSettings"/>;
/// default full), <c>--level-sprite-tilt-depth=upright|tilted</c> (default
/// upright), <c>--level-sprite-shadow=off|blend|dither</c> (blob shadows under
/// actors; default off), <c>--level-sprite-outline=off|INDEX</c> (a one-texel
/// outline in palette index INDEX; default 0, black),
/// <c>--level-sprite-wall-pull=UNITS|off</c> (T3.5a, <see cref="SpriteSettings.WallPull"/>:
/// billboards are pulled towards the camera by the thing's radius, at most
/// UNITS, so walls they touch behind them do not cut them; default 16);
/// <c>--level-sprite-hidden=depth|upright</c> (T3.6a, <see cref="SpriteSettings.Hidden"/>:
/// with upright, a tilted billboard is also dropped whole when the level would
/// hide its upright billboard whole; default depth);
/// <c>--level-player-light=LEVEL|off</c> (T4.7a, <see cref="SpriteSettings.PlayerLight"/>:
/// the player's own sprite is lit as if its sector's light were at least
/// LEVEL, 0–255, so it stays visible in dark sectors; default 128);
/// <c>--level-fuzz=on|off</c> (T6.9, <see cref="SpriteSettings.Fuzz"/>: things
/// with <c>MF_SHADOW</c>, spectres and the player with a blur sphere, drawn
/// with the fuzz, or as any sprite; default on);
/// <c>--level-light=player|none|camera</c> (<see cref="LightDiminishing"/>, T2.8;
/// default player); <c>--level-light-near=UNITS</c> (the shortest distance the
/// player mode uses, T3.7; default 80, 0 for the tables down to the player);
/// <c>--level-light-reference=UNITS</c> (the distance of the none mode; default 128);
/// <c>--level-light-origin=X,Y</c> (the player position light
/// diminishing uses, in map units; by default the free-fly camera's pivot, or
/// player 1's start under the overview camera);
/// <c>--level-background=RRGGBB</c> (the void's colour, default black; a
/// colour in no palette makes cracks easy to find, T2.9);
/// <c>--level-check</c> (load every map and check the meshes, data textures
/// and, with a real renderer, drawn pixels: <see cref="LevelCheck"/>).
/// </para>
/// <para>
/// Keys (T2.7, T3.3; not under <c>--level-check</c>): Tab cycles the game
/// camera, the overview and the free-fly camera (<see cref="FreeFlyCamera"/>,
/// which has its own keys); with the game camera the game's input actions
/// drive the player (T4.6: W/A/S/D relative to the screen, Shift runs, the
/// mouse aims), Ctrl + wheel zooms and O switches orthographic / perspective
/// (<see cref="IsoCamera"/>); Home puts the player (or, in free-fly, the
/// free-fly camera) at player 1's start, Page
/// Down / Page Up load the next / previous map of the WAD, L cycles the light
/// diminishing mode, X cycles the cutaway style (cut, dither, off), T the
/// sprite tilt (full, half, off), G the blob shadows (off, blend, dither), P
/// the sprite wall pull (on, off), H the upright hiding (depth, upright), B
/// the player's minimum light (on, off), M
/// the one-sided masked middles from behind (mirrored, off), K the cutaway
/// cap (dark, flat, off), V the things it cuts (decor, all, off), J the door
/// lids (on, off), = and -
/// the HUD (T6.11: as vanilla's screen size keys, = bar, fullscreen HUD,
/// none, - back), F1 shows the controls, F3 hides the overlay.
/// </para>
/// </summary>
public partial class LevelScene : Node3D, IGameHost
{
    private Camera3D _camera = null!;
    private Label _message = null!;
    private string _status = "";
    private Control _crosshair = null!;
    private bool _showHelp;
    private readonly List<MeshInstance3D> _chunks = new();
    private LightDiminishing _lightMode = LightDiminishing.Player;

    /// <summary>Whether one-sided masked middles are drawn from behind (T3.1a, <c>--level-masked-back</c>, key M); applied to every level.</summary>
    public MaskedBackFaces MaskedBacks { get; private set; } = MaskedBackFaces.Mirrored;

    /// <summary>Whether lids are drawn on doors and lintels (T6.13b, <c>--level-door-lids</c>, key J); applied to every level.</summary>
    public DoorLidMode DoorLids { get; private set; } = DoorLidMode.On;

    /// <summary>
    /// T6.6: whether the console player's weapon flash (<see cref="player_t.extralight"/>,
    /// 0–2, set by the flash states' <c>A_Light1</c>/<c>A_Light2</c>) goes to
    /// <see cref="LevelMesh.SetExtraLight"/> every frame, lighting walls,
    /// flats and sprites one or two light levels up as vanilla's r_main.c
    /// <c>extralight</c> does the whole view (<c>--level-weapon-light</c>; SPEC §12 T6.6).
    /// </summary>
    public bool WeaponLight { get; set; } = true;

    /// <summary>
    /// T6.8: whether the console player's palette flashes and power-up
    /// colormaps show (<see cref="UpdatePaletteEffects"/>, <c>--level-palette-effects</c>;
    /// SPEC §12 T6.8).
    /// </summary>
    public bool PaletteEffects { get; set; } = true;

    /// <summary>
    /// T6.11: m_random.c's <c>M_Random</c> index of the presentation (the
    /// status bar's face; later the wipes and the intermission): cleared at
    /// a new game as vanilla's <c>G_InitNew</c>, never read by the sim (the
    /// world's own <c>rndindex</c> stays unused), so the sim's state does not
    /// depend on it.
    /// </summary>
    public DoomRandom MRandom { get; } = new();

    private GameFlow? _flow;

    /// <summary>
    /// T7.1: the game's flow (g_game.c's game states and actions, d_main.c's
    /// title loop; <see cref="GameFlow"/>), with this scene as its host
    /// (<see cref="IGameHost"/>); null until a WAD is open.
    /// </summary>
    public GameFlow Flow => _flow ?? throw new InvalidOperationException("No WAD open.");

    /// <summary>T7.1: the game state (<see cref="GameFlow.gamestate"/>; the level before a WAD is open).</summary>
    public gamestate_t GameState => _flow?.gamestate ?? gamestate_t.GS_LEVEL;

    /// <summary>T7.1: the full screens of the game states (the title loop's pages, the intermission, the finale) and the pause graphic.</summary>
    public ScreenView Screens { get; } = new() { Name = "Screens" };

    private ScreenGraphics? _graphics;

    // What the screens showed last (redrawn when it changes).
    private (gamestate_t State, int Tic, bool Pause, Vector2 Size, int Palette)? _screenKey;

    /// <summary>
    /// T7.1: the window lost the focus: nothing runs until it is back (not
    /// vanilla's pause, which goes through the <c>ticcmd</c>; never under the
    /// level check or a level script). The pause graphic shows.
    /// </summary>
    public bool FocusPaused { get; private set; }

    /// <summary>T6.11: the console player's status bar (st_stuff.c), from the WAD's graphics; null until a WAD is open.</summary>
    public StStuff? StatusBar { get; private set; }

    /// <summary>T6.11: the message line (hu_stuff.c), in the WAD's <c>STCFN*</c> font; null until a WAD is open.</summary>
    public HuStuff? MessageLine { get; private set; }

    /// <summary>T6.11: shows <see cref="StatusBar"/> and <see cref="MessageLine"/> under the game camera (<c>--level-hud</c>); hidden under the other cameras and the level check.</summary>
    public HudView Hud { get; } = new() { Name = "Hud" };

    // The player mobj the status bar was started for (ST_Start: each level, and a reborn player's new mobj).
    private mobj_t? _hudMobj;

    /// <summary>The void's colour as set at start (<c>--level-background</c>, else black); a palette flash tints it unless it was set.</summary>
    private Color _background = Colors.Black;
    private bool _backgroundSet;
    private int _voidPalette;

    /// <summary>The cutaway's presentation options (T3.4); applied while the game camera is current.</summary>
    public CutawaySettings Cutaway { get; set; } = new();

    /// <summary>The thing sprites' readability options (T3.6: tilt, blob shadows, outline); applied to every level.</summary>
    public SpriteSettings SpriteOptions { get; set; } = new();

    /// <summary>The wall pull key P turns back on (T3.5a): <c>--level-sprite-wall-pull</c>'s, or the default.</summary>
    private float _wallPull = SpriteSettings.DefaultWallPull;

    /// <summary>The player's minimum light key B turns back on (T4.7a): <c>--level-player-light</c>'s, or the default.</summary>
    private int _playerLight = SpriteSettings.DefaultPlayerLight;

    /// <summary>The loaded level's mesh, or null when no map is loaded.</summary>
    public LevelMesh? Mesh { get; private set; }

    /// <summary>The loaded IWAD (plus PWADs), its texture table, palettes and colormaps; null until <see cref="OpenWad"/> succeeds.</summary>
    public WadArchive? Wad { get; private set; }
    public Textures? Textures { get; private set; }
    public Playpal? Playpal { get; private set; }
    public Colormap? Colormap { get; private set; }

    /// <summary>The WAD's sprite index and its sprite atlas (T3.5; null when the sprites can't be indexed).</summary>
    public Sprites? Sprites { get; private set; }
    public SpriteAtlas? SpriteAtlas { get; private set; }

    /// <summary>The IWAD's game mode (which things spawn, T3.2).</summary>
    public GameMode GameMode { get; private set; }

    /// <summary>T7.2: the IWAD's variant (Chocolate Doom's <c>gamevariant</c>: the BFG Edition's menu workarounds).</summary>
    public GameVariant GameVariant { get; private set; }

    /// <summary>The skill whose things are drawn (<c>--level-skill</c>, default 3: <see cref="skill_t.sk_medium"/>).</summary>
    public skill_t Skill { get; private set; } = skill_t.sk_medium;

    /// <summary>Vanilla's <c>-nomonsters</c> (<c>--level-monsters=off</c>, T5.7; default false).</summary>
    public bool NoMonsters { get; private set; }

    /// <summary>Whether the billboards and the player are drawn (<c>--level-things</c>, T3.8).</summary>
    private bool _showThings = true;

    /// <summary>The billboards of the loaded map's mobjs but the player (T3.5; from the world's mobjs since T4.7, in thinker order, without <c>MF_NOSECTOR</c> ones since T5.6: <see cref="DrawnMobjs"/>, <see cref="IsDrawn"/>).</summary>
    public ThingSprites? Things { get; private set; }

    /// <summary>The mobj of each entry of <see cref="Things"/>.</summary>
    public IReadOnlyList<mobj_t> DrawnMobjs => _drawn;

    private readonly List<mobj_t> _drawn = new();
    private readonly List<mobj_t> _scratch = new();

    /// <summary>
    /// The loaded map's game state (T4.7): a new game on <see cref="Skill"/>
    /// with <see cref="Tweaks"/>, on the mesh's own <see cref="Level"/>
    /// (the world changes its sectors in place); null when no map is loaded
    /// or its things can't be spawned.
    /// </summary>
    public World? World { get; private set; }

    /// <summary>The sim's tweaks (<c>--level-tweaks</c>, default <see cref="Tweaks.TopDown"/>); the commands are built for them.</summary>
    public Tweaks Tweaks { get; private set; } = Tweaks.TopDown;

    /// <summary>The player mobj (<c>players[consoleplayer].mo</c>), or null.</summary>
    public mobj_t? PlayerMobj => World is { } w ? w.players[w.consoleplayer].mo : null;

    /// <summary>A tic's length in seconds (1 / <see cref="SimInfo.TICRATE"/>).</summary>
    public const double TicSeconds = 1.0 / SimInfo.TICRATE;

    /// <summary>
    /// The most tics one frame runs (about 4 frames a second): a longer frame
    /// (a stall, a map load) drops the rest instead of catching up.
    /// </summary>
    public const int MaxTicsPerFrame = 8;

    private double _ticTime;
    private readonly Queue<(ticcmd_t? Cmd, Action<World>? Before)> _scriptTics = new();

    // Debug plane moves (T5.1, the level script's plane): run after each tic, as a mover thinker would.
    private readonly List<(sector_t Sector, bool Ceiling, int Target, int Speed)> _planeMoves = new();

    /// <summary>How far the presentation is between the last two tics (0–1): the time banked towards the next tic, in tics.</summary>
    public double TicFraction { get; private set; } = 1;

    /// <summary>The tics run since the scene started (all maps; the level script waits on it).</summary>
    public long TicsRun { get; private set; }

    /// <summary>The command of the last tic run.</summary>
    public ticcmd_t LastTiccmd { get; private set; }

    /// <summary>While true no tic runs (the visibility measure keeps the world still; not the game's pause, <see cref="GameFlow.paused"/>).</summary>
    public bool Paused { get; set; }

    /// <summary>
    /// Scripted tics (the level script's <c>cmd</c> and <c>step</c>): tics
    /// run only from <see cref="QueueTic"/>'s queue, still paced at 35 Hz, and
    /// the world holds still while it is empty, so a script's result does
    /// not depend on the frame rate.
    /// </summary>
    public bool ScriptedTics { get; set; }

    /// <summary>The scripted tics not run yet.</summary>
    public int QueuedTics => _scriptTics.Count;

    /// <summary>The maps of the WAD (<c>ExMy</c>/<c>MAPxx</c> headers followed by <c>THINGS</c>), in lump order, each once.</summary>
    public IReadOnlyList<string> MapNames { get; private set; } = Array.Empty<string>();

    /// <summary>The scene's camera (the overview camera; checks move it).</summary>
    public Camera3D Camera => _camera;

    /// <summary>The free-fly debug camera (T2.7); null under <c>--level-check</c>, which must keep the overview camera current.</summary>
    public FreeFlyCamera? FreeFly { get; private set; }

    /// <summary>The game camera (T3.3); null under <c>--level-check</c>.</summary>
    public IsoCamera? Iso { get; private set; }

    /// <summary>The player mobj's billboard, which the game camera follows (T3.3's placeholder, the player mobj since T4.7); null under <c>--level-check</c>.</summary>
    public PlayerSprite? Player { get; private set; }

    /// <summary>While true the game camera does not follow the player (scripted measurements keep the view still).</summary>
    public bool HoldCamera { get; set; }

    /// <summary>The cursor ground point (<see cref="CursorGround"/>) under the game camera, updated every frame while it is current.</summary>
    public CursorGround.Hit? Cursor { get; private set; }

    private MeshInstance3D? _cursorMarker;

    /// <summary>The game's input actions (T4.6): <see cref="BuildTiccmd"/> turns them into a <c>ticcmd</c> each tic.</summary>
    public GameInput GameInput { get; } = new();

    /// <summary>The <c>ticcmd</c> builder (T4.6), run once per tic for the sim (T4.7).</summary>
    public TiccmdBuilder TiccmdBuilder { get; } = new();

    /// <summary>p_local.h <c>VIEWHEIGHT</c>: eye height above the floor, map units (where Home puts the free-fly camera).</summary>
    public const int VIEWHEIGHT = 41;

    /// <summary>Controls shown by F1.</summary>
    public const string ControlsHelp =
        "Tab game camera/overview/free-fly   Home player 1 start   PgDn/PgUp next/previous map   L light mode   X cutaway   T sprite tilt   G shadows   P sprite wall pull   H sprite upright hiding   B player minimum light   M masked backs   K cutaway cap   V cutaway things   J door lids   =/- HUD (bar, fullscreen, none)   Pause pause   F1 controls   F3 overlay\n"
        + "Menus: Escape (pad Start) opens and closes, arrows (D-pad, left stick, wheel) move, Enter (A, left click) selects, Backspace (B, right click) goes back, Y/N (A/B, left/right click) answer; any key on the title opens them; Options > More options: settings and controls (Enter binds, Delete / pad X clears)   Intermission: fire or use go on\n"
        + "Game camera: W/A/S/D walk (Shift runs), the mouse aims, left button fires, E/Space use, 1-8 / wheel weapons, Ctrl+wheel zoom, O orthographic/perspective\n"
        + "Free-fly: click captures the mouse (Esc releases), mouse look, W/A/S/D move, E/Space up, Q/C down,\n"
        + "Shift x4, Alt x1/4, wheel speed, Ctrl+wheel FOV / ortho size, O perspective/orthographic";

    /// <summary>The scene's environment (black background; checks change it).</summary>
    public Godot.Environment Environment { get; private set; } = null!;

    /// <summary>The overlay with the status line (checks hide it so it doesn't cover pixels).</summary>
    public CanvasLayer Overlay { get; private set; } = null!;

    /// <summary>The node of each sector's chunk (null where <see cref="LevelMesh.SectorMeshes"/> has none).</summary>
    public MeshInstance3D?[] Chunks { get; private set; } = Array.Empty<MeshInstance3D?>();

    private static bool IsCheckRun => WadLocator.HasUserArg("--level-check");

    /// <summary>
    /// How long opening the WAD took (read, identify, texture table, palettes;
    /// once per WAD), and how long the last <see cref="LoadMap"/> took (map
    /// lumps, wall sections, floors, atlas, meshes, uploads and scene nodes),
    /// in milliseconds. SPEC §9: under 1 s per map (T2.9; <c>--level-check</c>
    /// checks every map).
    /// </summary>
    public double OpenWadMilliseconds { get; private set; }

    /// <inheritdoc cref="OpenWadMilliseconds"/>
    public double LastLoadMilliseconds { get; private set; }

    public override void _Ready()
    {
        _camera = new Camera3D { Current = true };
        AddChild(_camera);
        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Colors.Black, // SPEC §7.2: the void is black
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
        };
        if (WadLocator.GetUserArg("--level-background") is string bg && Color.HtmlIsValid(bg))
        {
            Environment.BackgroundColor = Color.FromHtml(bg); // T2.9: a colour in no palette shows cracks
            _backgroundSet = true;
        }
        _background = Environment.BackgroundColor;
        AddChild(new WorldEnvironment { Environment = Environment });
        AddChild(Screens); // T7.1: under the debug overlay and the HUD
        AddChild(MenuScreens); // T7.2: layer 2, over everything
        Overlay = new CanvasLayer();
        _message = new Label
        {
            Position = new Vector2(8, 8),
            LabelSettings = new LabelSettings { FontSize = 14, OutlineSize = 4, OutlineColor = Colors.Black },
        };
        Overlay.AddChild(_message);
        _crosshair = Crosshair();
        Overlay.AddChild(_crosshair);
        AddChild(Overlay);
        Hud.Visible = false;
        AddChild(Hud);

        try
        {
            if (WadLocator.GetUserArg("--level-skill") is string skill)
                Skill = int.TryParse(skill, out int n) && n >= 1 && n <= 5
                    ? (skill_t)(n - 1)
                    : throw new ArgumentException($"--level-skill: \"{skill}\" (1-5)");
            if (WadLocator.GetUserArg("--level-monsters") is string monsters)
                NoMonsters = monsters switch
                {
                    "on" => false,
                    "off" => true,
                    _ => throw new ArgumentException($"--level-monsters: \"{monsters}\" (on or off)"),
                };
            if (WadLocator.GetUserArg("--level-things") is string things)
                _showThings = things switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new ArgumentException($"--level-things: \"{things}\" (on or off)"),
                };
            if (WadLocator.GetUserArg("--level-tweaks") is string tweaks)
                Tweaks = tweaks switch
                {
                    "topdown" => Tweaks.TopDown,
                    "vanilla" => Tweaks.Vanilla,
                    _ => throw new ArgumentException($"--level-tweaks: \"{tweaks}\" (topdown or vanilla)"),
                };
            if (WadLocator.GetUserArg("--level-aim-assist") is string aimAssist)
                Tweaks = Tweaks with { AimAssistCone = ParseAimAssistCone(aimAssist) };
            OpenWad();
            _flow = new GameFlow(this, GameMode, MRandom, GameVariant); // T7.1: g_game.c's flow, d_main.c's title loop
            _graphics = new ScreenGraphics(Wad, MessageLine);
            InitMenus(); // T7.2
            if (IsCheckRun)
            {
                InitSettings(); // T7.3: the defaults (no file), for the check's settings round trip
                AddChild(new LevelCheck(this)); // loads every map itself; no free-fly camera, no keys
                return;
            }
            if (WadLocator.GetUserArg("--level-light") is string light)
                _lightMode = light switch
                {
                    "none" => LightDiminishing.None,
                    "camera" => LightDiminishing.Camera,
                    "player" => LightDiminishing.Player,
                    _ => throw new ArgumentException($"--level-light: unknown mode \"{light}\" (player, none or camera)"),
                };
            Cutaway = ParseCutaway();
            SpriteOptions = ParseSprites();
            if (SpriteOptions.WallPull > 0)
                _wallPull = SpriteOptions.WallPull;
            if (SpriteOptions.PlayerLight > 0)
                _playerLight = SpriteOptions.PlayerLight;
            if (WadLocator.GetUserArg("--level-masked-back") is string backs)
                MaskedBacks = ParseMaskedBacks(backs);
            if (WadLocator.GetUserArg("--level-door-lids") is string lids)
                DoorLids = lids switch
                {
                    "on" => DoorLidMode.On,
                    "off" or "vanilla" => DoorLidMode.Off,
                    _ => throw new ArgumentException($"--level-door-lids: \"{lids}\" (on or off)"),
                };
            if (WadLocator.GetUserArg("--level-weapon-light") is string weaponLight)
                WeaponLight = weaponLight switch
                {
                    "on" or "vanilla" => true,
                    "off" => false,
                    _ => throw new ArgumentException($"--level-weapon-light: \"{weaponLight}\" (on or off)"),
                };
            if (WadLocator.GetUserArg("--level-palette-effects") is string paletteEffects)
                PaletteEffects = paletteEffects switch
                {
                    "on" or "vanilla" => true,
                    "off" => false,
                    _ => throw new ArgumentException($"--level-palette-effects: \"{paletteEffects}\" (on or off)"),
                };
            if (WadLocator.GetUserArg("--level-hud") is string hud)
                Hud.Mode = hud switch
                {
                    "bar" or "vanilla" => HudMode.Bar,
                    "full" => HudMode.Full,
                    "off" => HudMode.Off,
                    _ => throw new ArgumentException($"--level-hud: \"{hud}\" (bar, full or off)"),
                };
            if (WadLocator.GetUserArg("--level-hud-scale") is string hudScale)
                Hud.FixedScale = hudScale == "auto" ? 0
                    : int.TryParse(hudScale, out int n) && n >= 1 && n <= 16 ? n
                    : throw new ArgumentException($"--level-hud-scale: \"{hudScale}\" (1-16 or auto)");
            FreeFly = new FreeFlyCamera { Name = "FreeFly" };
            AddChild(FreeFly);
            CreateGameCamera();
            InitSettings(); // T7.3: the saved settings, but those the arguments above give
            if (WadLocator.HasUserArg("--level"))
            {
                // A map from the command line (vanilla's -warp): a new game on it, no title loop.
                string? map = WadLocator.GetUserArg("--level");
                if (map is null || map.StartsWith('-'))
                    map = DefaultMap();
                LoadMap(map);
                if (WadLocator.GetUserArg("--level-sector-floor") is string moves)
                    MoveFloors(Mesh!.Level, moves);
            }
            else
                _flow.D_StartTitle(null); // T7.1: d_main.c D_DoomMain without a map: the title loop
        }
        catch (Exception e) when (e is WadFormatException or ModifiedGameException or IOException or UnauthorizedAccessException
            or KeyNotFoundException or ArgumentException)
        {
            Fail(e.Message);
            return;
        }
        GetViewport().SizeChanged += FrameCamera;
        switch (WadLocator.GetUserArg("--level-camera"))
        {
            case "fly": UseCamera(CameraMode.FreeFly); break;
            case "overview": UseCamera(CameraMode.Overview); break;
            case null or "iso": UseCamera(CameraMode.Iso); break;
            case string other: Fail($"--level-camera: unknown camera \"{other}\" (iso, overview or fly)"); return;
        }
        if (WadLocator.GetUserArg("--level-script") is string script)
            AddChild(new LevelScript(this, script));
        else if (WadLocator.GetUserArg("--level-screenshot") is string path)
            _ = ScreenshotAsync(path);
    }

    /// <summary>The level scene's cameras (Tab cycles them in this order).</summary>
    public enum CameraMode { Iso, Overview, FreeFly }

    /// <summary>Makes the free-fly camera (<paramref name="fly"/>) or the overview camera current.</summary>
    public void UseFreeFly(bool fly) => UseCamera(fly ? CameraMode.FreeFly : CameraMode.Overview);

    /// <summary>Makes the camera <paramref name="mode"/> current (the overview camera when that one does not exist).</summary>
    public void UseCamera(CameraMode mode)
    {
        if (mode == CameraMode.FreeFly && FreeFly is not null)
        {
            FreeFly.MakeCurrent();
            return;
        }
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (mode == CameraMode.Iso && Iso is not null)
        {
            Iso.MakeCurrent();
            if (Player is not null)
                Iso.Snap(Player.Foot);
        }
        else
            _camera.MakeCurrent();
    }

    /// <summary>The current camera.</summary>
    public CameraMode CurrentCamera =>
        FreeFly is { Current: true } ? CameraMode.FreeFly : Iso is { Current: true } ? CameraMode.Iso : CameraMode.Overview;

    /// <summary>Whether the free-fly camera is the current one.</summary>
    public bool FreeFlyActive => FreeFly is { } f && f.Current;

    /// <summary>Whether the game camera is the current one.</summary>
    public bool IsoActive => Iso is { } i && i.Current;

    /// <summary>Creates the game camera, the player's billboard and the cursor marker.</summary>
    private void CreateGameCamera()
    {
        Iso = new IsoCamera { Name = "Iso" };
        AddChild(Iso);
        if (WadLocator.GetUserArg("--level-projection") is string projection)
            Iso.SetProjectionMode(projection switch
            {
                "ortho" or "orthographic" => IsoProjection.Orthographic,
                "perspective" => IsoProjection.Perspective,
                _ => throw new ArgumentException($"--level-projection: unknown projection \"{projection}\" (ortho or perspective)"),
            });
        if (WadLocator.GetUserArg("--level-pitch") is string pitch)
            Iso.SetPitch(ParseFloat(pitch, "--level-pitch"));
        if (WadLocator.GetUserArg("--level-zoom") is string zoom)
            Iso.SetViewUnits(ParseFloat(zoom, "--level-zoom"));

        Player = new PlayerSprite { Name = "Player", Visible = _showThings };
        AddChild(Player);

        // The cursor ground point: a small ring on the floor, drawn over everything (debug, hidden with the overlay).
        var ring = new TorusMesh { InnerRadius = 5f / LevelMesh.MapUnitsPerMetre, OuterRadius = 8f / LevelMesh.MapUnitsPerMetre, Rings = 16, RingSegments = 4 };
        ring.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.85f, 0.1f),
            NoDepthTest = true,
            RenderPriority = 1,
        };
        _cursorMarker = new MeshInstance3D { Mesh = ring, Name = "CursorMarker", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_cursorMarker);
    }

    /// <summary>The cutaway options from <c>--level-cutaway</c>, <c>--level-cutaway-radius</c>, <c>--level-cutaway-height</c>, <c>--level-cutaway-cursor</c>, <c>--level-cutaway-cap</c> and <c>--level-cutaway-things</c>.</summary>
    private static CutawaySettings ParseCutaway()
    {
        var settings = new CutawaySettings();
        if (WadLocator.GetUserArg("--level-cutaway") is string style)
            settings = settings with { Style = Render.Cutaway.ParseStyle(style) };
        if (WadLocator.GetUserArg("--level-cutaway-radius") is string radius)
            settings = settings with { Radius = Math.Max(0, ParseFloat(radius, "--level-cutaway-radius")) };
        if (WadLocator.GetUserArg("--level-cutaway-height") is string height)
            settings = settings with { Height = ParseFloat(height, "--level-cutaway-height") };
        if (WadLocator.GetUserArg("--level-cutaway-cursor") is string cursor)
            settings = settings with
            {
                Cursor = cursor switch
                {
                    "on" => true,
                    "off" => false,
                    _ => throw new ArgumentException($"--level-cutaway-cursor: \"{cursor}\" (on or off)"),
                },
            };
        if (WadLocator.GetUserArg("--level-cutaway-cap") is string cap)
            settings = settings with { Cap = Render.Cutaway.ParseCap(cap) };
        if (WadLocator.GetUserArg("--level-cutaway-things") is string things)
            settings = settings with { Things = Render.Cutaway.ParseThings(things) };
        return settings;
    }

    /// <summary>The overlay's name for <see cref="CutawaySettings.Things"/> (as <c>--level-cutaway-things</c> takes it).</summary>
    private static string ThingsName(CutawayThings things) => things switch
    {
        CutawayThings.Decorations => "decor",
        CutawayThings.All => "all",
        _ => "off",
    };

    /// <summary>The <c>--level-masked-back</c> option (T3.1a).</summary>
    public static MaskedBackFaces ParseMaskedBacks(string value) => value switch
    {
        "mirror" or "mirrored" or "on" => MaskedBackFaces.Mirrored,
        "off" or "vanilla" => MaskedBackFaces.Off,
        _ => throw new ArgumentException($"--level-masked-back: \"{value}\" (mirror or off)"),
    };

    /// <summary>The sprite options from <c>--level-sprite-tilt</c>, <c>--level-sprite-tilt-depth</c>, <c>--level-sprite-shadow</c> and <c>--level-sprite-outline</c> (T3.6), <c>--level-sprite-wall-pull</c> (T3.5a), <c>--level-sprite-hidden</c> (T3.6a), <c>--level-player-light</c> (T4.7a) and <c>--level-fuzz</c> (T6.9).</summary>
    private static SpriteSettings ParseSprites()
    {
        var settings = new SpriteSettings();
        if (WadLocator.GetUserArg("--level-sprite-tilt") is string tilt)
            settings = settings with { Tilt = SpriteSettings.ParseTilt(tilt) };
        if (WadLocator.GetUserArg("--level-sprite-tilt-depth") is string depth)
            settings = settings with { TiltDepth = SpriteSettings.ParseTiltDepth(depth) };
        if (WadLocator.GetUserArg("--level-sprite-shadow") is string shadow)
            settings = settings with { Shadow = SpriteSettings.ParseShadow(shadow) };
        if (WadLocator.GetUserArg("--level-sprite-outline") is string outline)
            settings = settings with { Outline = SpriteSettings.ParseOutline(outline) };
        if (WadLocator.GetUserArg("--level-sprite-wall-pull") is string pull)
            settings = settings with { WallPull = SpriteSettings.ParseWallPull(pull) };
        if (WadLocator.GetUserArg("--level-sprite-hidden") is string hidden)
            settings = settings with { Hidden = SpriteSettings.ParseHidden(hidden) };
        if (WadLocator.GetUserArg("--level-player-light") is string playerLight)
            settings = settings with { PlayerLight = SpriteSettings.ParsePlayerLight(playerLight) };
        if (WadLocator.GetUserArg("--level-fuzz") is string fuzz)
            settings = settings with { Fuzz = SpriteSettings.ParseFuzz(fuzz) };
        return settings;
    }

    /// <summary>
    /// <c>--level-aim-assist=DEG|off</c> (T6.7): the aim assist's half-cone
    /// in degrees (0–45; 0 or off: vanilla aiming) as the sim's BAM
    /// <see cref="Tweaks.AimAssistCone"/>.
    /// </summary>
    private static uint ParseAimAssistCone(string s)
    {
        if (s == "off")
            return 0;
        float deg = ParseFloat(s, "--level-aim-assist");
        if (deg < 0 || deg > 45)
            throw new ArgumentException($"--level-aim-assist: \"{s}\" (0-45 degrees or off)");
        return (uint)Math.Round(deg / 360.0 * 4294967296.0);
    }

    private static float ParseFloat(string s, string what) =>
        float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)
            ? f
            : throw new ArgumentException($"{what}: not a number: \"{s}\"");

    /// <summary>
    /// Not vanilla, a debug move (T5.1, the level script's <c>plane</c>):
    /// moves sector <paramref name="sector"/>'s floor (or ceiling) towards
    /// <paramref name="height"/> (map units) by <paramref name="speed"/>
    /// units at the end of each tic, as a mover thinker of that tic would
    /// (with <see cref="World.P_ChangeSector"/>, no crushing), until it gets there;
    /// so the sector moves smoothly on screen, drawn between tics. The sim's
    /// own movers (doors since T5.3, <c>T_MovePlane</c>; lifts and floors
    /// with T5.5) move sectors as the game does: this stays for moving any
    /// sector by hand.
    /// </summary>
    public void MovePlane(int sector, bool ceiling, int height, int speed)
    {
        if (World is not { } world || sector < 0 || sector >= world.sectors.Length || speed <= 0)
            throw new ArgumentException($"plane: no sector {sector} (or speed {speed} not positive)");
        _planeMoves.RemoveAll(m => m.Sector == world.sectors[sector] && m.Ceiling == ceiling);
        _planeMoves.Add((world.sectors[sector], ceiling, height << Fixed.FRACBITS, speed << Fixed.FRACBITS));
    }

    /// <summary>Whether a <see cref="MovePlane"/> move is still running.</summary>
    public bool PlanesMoving => _planeMoves.Count > 0;

    private void MovePlanes()
    {
        for (int i = _planeMoves.Count - 1; i >= 0; i--)
        {
            (sector_t sec, bool ceiling, int target, int speed) = _planeMoves[i];
            int now = ceiling ? sec.ceilingheight : sec.floorheight;
            int next = now < target ? Math.Min(now + speed, target) : Math.Max(now - speed, target);
            if (ceiling)
                sec.ceilingheight = next;
            else
                sec.floorheight = next;
            World!.P_ChangeSector(sec, false);
            if (next == target)
                _planeMoves.RemoveAt(i);
        }
    }

    /// <summary>
    /// Puts the player mobj at map point (<paramref name="x"/>, <paramref name="y"/>)
    /// on the floor there, facing <paramref name="angle"/> (vanilla degrees)
    /// when given, with no momentum (<see cref="World.PlaceMobj"/>: a debug
    /// move, no collision check, not interpolated), and centres the game camera on it.
    /// </summary>
    public void PlacePlayer(float x, float y, float? angle = null)
    {
        if (World is not { } world || PlayerMobj is not { } mo)
            return;
        world.PlaceMobj(mo, ToFixed(x), ToFixed(y), angle is float a ? ThingSprites.BamOfDegrees(a) : null);
        TiccmdBuilder.Reset(); // keep the new facing until something aims
        PresentWorld();
        if (Player is not null)
            Iso?.Snap(Player.Foot);
    }

    /// <summary>
    /// T5.10: a route's <c>start X Y ANGLE</c> header (<see cref="RouteFile"/>),
    /// as the tests' <c>RouteStart.Place</c> and the reference's <c>dump_pretic</c>
    /// do it: <see cref="World.P_TeleportMove"/> (no fog), onto the floor,
    /// facing ANGLE degrees; the camera snaps. False when something stands there.
    /// A route starts standing still (a fresh map's player), so the momentum
    /// is zeroed too (T5.10: <c>e1m8-exit</c> after <c>e1m8-arena</c> slid past its switch).
    /// </summary>
    public bool PlaceRouteStart(int x, int y, int angle)
    {
        if (World is not { } world || PlayerMobj is not { } mo)
            return false;
        if (!world.P_TeleportMove(mo, x << Fixed.FRACBITS, y << Fixed.FRACBITS))
            return false;
        mo.z = mo.floorz;
        mo.momx = mo.momy = mo.momz = 0;
        mo.angle = (uint)((long)angle * 0x100000000L / 360);
        mo.interp = false;
        TiccmdBuilder.Reset();
        PresentWorld();
        if (Player is not null)
            Iso?.Snap(Player.Foot);
        return true;
    }

    /// <summary>Puts the player mobj back at player 1's start, facing its angle.</summary>
    public void PlayerToStart()
    {
        if (Mesh?.Level.PlayerStart(0) is MapThing start)
            PlacePlayer(start.X, start.Y, start.Angle);
    }

    /// <summary>
    /// Puts the free-fly camera at player <paramref name="player"/>'s start of
    /// the loaded map, at <see cref="VIEWHEIGHT"/> above its floor, facing
    /// its angle (or at the map's centre when the map has no such start).
    /// </summary>
    public void JumpToStart(int player = 0)
    {
        if (FreeFly is null || Mesh is null)
            return;
        Level level = Mesh.Level;
        if (level.PlayerStart(player) is MapThing start)
            PlaceFreeFly(start.X, start.Y, start.Angle);
        else
            FreeFly.Place(Mesh.Bounds.GetCenter(), 0, -30);
    }

    /// <summary>
    /// Puts the free-fly camera at map point (<paramref name="x"/>, <paramref name="y"/>)
    /// facing vanilla angle <paramref name="angle"/> (degrees) with no pitch, at height
    /// <paramref name="z"/> (map units), or by default at <see cref="VIEWHEIGHT"/>
    /// above the floor there, as vanilla's view of a player standing still.
    /// </summary>
    public void PlaceFreeFly(int x, int y, float angle, int? z = null)
    {
        if (FreeFly is null || Mesh is null)
            return;
        Sector sector = Mesh.Level.R_PointInSubsector(x << Fixed.FRACBITS, y << Fixed.FRACBITS).Sector;
        float height = z ?? (sector.FloorHeight >> Fixed.FRACBITS) + VIEWHEIGHT;
        FreeFly.Place(LevelMesh.ToGodot(x << Fixed.FRACBITS, y << Fixed.FRACBITS, height), FreeFlyCamera.YawForMapAngle(angle), 0);
    }

    /// <summary>Loads the map <paramref name="step"/> places after the current one in <see cref="MapNames"/> (wrapping).</summary>
    public void SwitchMap(int step)
    {
        if (MapNames.Count == 0)
            return;
        int i = Mesh is null ? -1 : IndexOfMap(Mesh.Level.Name);
        string next = MapNames[Mathf.PosMod(i + step, MapNames.Count)];
        try
        {
            LoadMap(next);
        }
        catch (Exception e) when (e is WadFormatException or KeyNotFoundException)
        {
            GD.PrintErr($"Level: {next}: {e.Message}");
            _status = $"{next}: {e.Message}";
        }
    }

    private int IndexOfMap(string map)
    {
        for (int i = 0; i < MapNames.Count; i++)
        {
            if (MapNames[i] == map)
                return i;
        }
        return -1;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // T6.6: the next/previous weapon (the wheel without Ctrl: the game camera zooms with it) for the game camera's next tic.
        if (!IsCheckRun && IsoActive && !(e is InputEventWithModifiers { CtrlPressed: true } || Input.IsPhysicalKeyPressed(Key.Ctrl))
            && GameInput.WeaponEvent(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (IsCheckRun || e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (IsGameInput(key))
            return; // T7.3: a key rebound to a game action is the game's, not a debug key
        switch (key.PhysicalKeycode)
        {
            case Key.Tab:
                CameraMode next = (CameraMode)(((int)CurrentCamera + 1) % 3);
                if (next == CameraMode.Iso && Iso is null)
                    next = CameraMode.Overview;
                UseCamera(next);
                break;
            case Key.Home:
                if (FreeFlyActive || Player is null)
                {
                    JumpToStart();
                    UseFreeFly(true);
                }
                else
                    PlayerToStart();
                break;
            case Key.Pagedown:
                SwitchMap(1);
                break;
            case Key.Pageup:
                SwitchMap(-1);
                break;
            case Key.L:
                _lightMode = (LightDiminishing)(((int)_lightMode + 1) % 3);
                Mesh?.SetLightDiminishing(_lightMode);
                break;
            case Key.X:
                Cutaway = Cutaway with { Style = Cutaway.Style switch { CutawayStyle.Cut => CutawayStyle.Dither, CutawayStyle.Dither => CutawayStyle.Off, _ => CutawayStyle.Cut } };
                break;
            case Key.K:
                Cutaway = Cutaway with { Cap = Cutaway.Cap switch { CutawayCap.Dark => CutawayCap.Flat, CutawayCap.Flat => CutawayCap.Off, _ => CutawayCap.Dark } };
                break;
            case Key.V:
                Cutaway = Cutaway with { Things = Cutaway.Things switch { CutawayThings.Decorations => CutawayThings.All, CutawayThings.All => CutawayThings.Off, _ => CutawayThings.Decorations } };
                break;
            case Key.T:
                SpriteOptions = SpriteOptions with { Tilt = SpriteOptions.Tilt >= 1f ? 0.5f : SpriteOptions.Tilt >= 0.5f ? 0f : 1f };
                break;
            case Key.G:
                SpriteOptions = SpriteOptions with { Shadow = (SpriteShadowStyle)(((int)SpriteOptions.Shadow + 1) % 3) };
                break;
            case Key.P:
                SpriteOptions = SpriteOptions with { WallPull = SpriteOptions.WallPull > 0 ? 0 : _wallPull };
                break;
            case Key.H:
                SpriteOptions = SpriteOptions with { Hidden = SpriteOptions.Hidden == SpriteHidden.Depth ? SpriteHidden.Upright : SpriteHidden.Depth };
                break;
            case Key.B:
                SpriteOptions = SpriteOptions with { PlayerLight = SpriteOptions.PlayerLight > 0 ? 0 : _playerLight };
                break;
            case Key.M:
                MaskedBacks = MaskedBacks == MaskedBackFaces.Mirrored ? MaskedBackFaces.Off : MaskedBackFaces.Mirrored;
                Mesh?.SetMaskedBackFaces(MaskedBacks);
                break;
            case Key.J:
                DoorLids = DoorLids == DoorLidMode.On ? DoorLidMode.Off : DoorLidMode.On;
                Mesh?.SetDoorLids(DoorLids);
                break;
            case Key.Equal:
                // T6.11: vanilla's screen size keys: = shows more of the view (bar, fullscreen HUD, none), - less.
                Hud.Mode = Hud.Mode == HudMode.Bar ? HudMode.Full : HudMode.Off;
                break;
            case Key.Minus:
                Hud.Mode = Hud.Mode == HudMode.Off ? HudMode.Full : HudMode.Bar;
                break;
            case Key.F1:
                _showHelp = !_showHelp;
                break;
            case Key.F3:
                Overlay.Visible = !Overlay.Visible;
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// T7.1: g_game.c <c>G_DeferedInitNew</c> as the menus' skill choice
    /// does (T7.2), for the level script: episode <paramref name="episode"/>, map <paramref name="map"/>
    /// on <paramref name="skill"/> (default <c>--level-skill</c>'s), started
    /// between the tics (<see cref="GameFlow.G_DoGameActions"/>, every frame).
    /// </summary>
    public void StartNewGame(skill_t? skill = null, int episode = 1, int map = 1)
    {
        Flow.G_DeferedInitNew(skill ?? Skill, episode, map);
        GD.Print($"Level: new game: episode {episode}, map {map}, skill {(int)(skill ?? Skill) + 1}");
    }

    public override void _Process(double delta)
    {
        if (!IsCheckRun && _flow is { } flow)
        {
            if (Mesh is not null)
                UpdateCursor();
            if (flow.gamestate != gamestate_t.GS_LEVEL)
                GameInput.Poll(); // T7.1: fire and use for the intermission and the finale
            PollMenuPad(delta); // T7.2: the pad's directions in the menus
            flow.G_DoGameActions(); // T7.1: the game actions set between the tics (a new game from the title)
            RunTics(delta);
            if (Mesh is not null)
            {
                PresentWorld();
                UpdateExtraLight();
                UpdatePaletteEffects();
                FollowPlayer(delta);
                if (GetViewport().GetCamera3D() is Camera3D current)
                    Things?.UpdateRotations(current);
                Mesh.SetLightOrigin(LightOrigin());
                UpdateCutaway();
                if (Mesh.Sprites != SpriteOptions)
                    Mesh.SetSprites(SpriteOptions);
            }
            UpdateHud();
            UpdateScreens();
            SyncSettings(); // T7.3: what the menus, the keys and the zoom changed is saved
        }
        _crosshair.Visible = FreeFlyActive;
        if (!IsCheckRun && Overlay.Visible)
            _message.Text = OverlayText();
    }

    /// <summary>
    /// The cursor ground point under the mouse (T3.3) while the game camera
    /// is current, at the player's height where no floor is hit, and the
    /// input latches (<see cref="GameInput.Poll"/>, every frame).
    /// </summary>
    private void UpdateCursor()
    {
        if (Iso is null || Mesh is null)
            return;
        if (Iso.Current)
        {
            Cursor = CursorGround.Pick(Mesh, Iso, Iso.CursorOrCentre, Player?.Z ?? 0);
            GameInput.Poll();
        }
        else
            Cursor = null;
        if (_cursorMarker is not null)
        {
            _cursorMarker.Visible = Cursor is not null && Overlay.Visible;
            if (Cursor is { } c)
                _cursorMarker.Position = c.Point + new Vector3(0, 0.5f / LevelMesh.MapUnitsPerMetre, 0);
        }
    }

    /// <summary>
    /// The game loop (T4.7, SPEC §6.1): banks <paramref name="delta"/> and runs
    /// one tic per <see cref="TicSeconds"/> banked (at most <see cref="MaxTicsPerFrame"/>),
    /// then sets <see cref="TicFraction"/> to what is left. With
    /// <see cref="ScriptedTics"/> an empty queue holds the world still (nothing
    /// banked, the last tic shown). Nothing runs while <see cref="Paused"/>
    /// or <see cref="FocusPaused"/>, or on a level without a player mobj
    /// (vanilla needs one; <see cref="GameFlow.CanTic"/>). Tics run in every
    /// game state (T7.1: the title loop, the intermission and the finale tic too).
    /// </summary>
    private void RunTics(double delta)
    {
        if (_flow is not { } flow || Paused || FocusPaused || !flow.CanTic)
            return;
        _ticTime += delta;
        int ran = 0;
        while (_ticTime >= TicSeconds)
        {
            if (ScriptedTics && _scriptTics.Count == 0)
                break;
            _ticTime -= TicSeconds;
            RunTic();
            if (++ran >= MaxTicsPerFrame)
            {
                _ticTime = Math.Min(_ticTime, TicSeconds * 0.999);
                break;
            }
        }
        if (ScriptedTics && _scriptTics.Count == 0)
        {
            _ticTime = 0;
            TicFraction = 1;
        }
        else if (flow.paused || flow.gamestate != gamestate_t.GS_LEVEL || World is { } w && flow.MenuHolds(w))
            TicFraction = 1; // T7.1: the world holds still (T7.2: also under the menu): show its last tic
        else
            TicFraction = Math.Clamp(_ticTime / TicSeconds, 0, 1);
    }

    /// <summary>
    /// One tic: the next scripted command (or, for a queued input tic or
    /// without <see cref="ScriptedTics"/>, <see cref="BuildTiccmd(Tweaks)"/>)
    /// through <see cref="World.G_Ticker(in ticcmd_t)"/>.
    /// </summary>
    private void RunTic()
    {
        if (!ScriptedTics)
        {
            if (_beforeNext is { } pending && World is { } world)
            {
                _beforeNext = null;
                pending(world);
            }
            Tic(BuildTiccmd(World?.tweaks ?? Tweaks));
            return;
        }
        (ticcmd_t? cmd, Action<World>? before) = _scriptTics.Dequeue();
        if (World is { } w)
            before?.Invoke(w); // T6.4: a route's events, before the tic
        Tic(cmd ?? BuildTiccmd(World?.tweaks ?? Tweaks));
    }

    private Action<World>? _beforeNext;

    /// <summary>
    /// T6.5: runs <paramref name="action"/> on the world before a tic (e.g.
    /// the level script's <c>missile</c>): with <see cref="ScriptedTics"/>
    /// before the next tic queued after this call (<see cref="QueueTic"/>), so
    /// it keeps its place among the script's tics, else before the next tic.
    /// </summary>
    public void BeforeNextTic(Action<World> action) => _beforeNext += action;

    /// <summary>
    /// Runs one game tic of <paramref name="cmd"/> (the game loop's, and the
    /// level check's): <see cref="GameFlow.G_Ticker"/>, whose level tic is
    /// <see cref="G_LevelTicker"/>, in every game state (T7.1).
    /// </summary>
    public void Tic(in ticcmd_t cmd)
    {
        Flow.G_Ticker(cmd);
        LastTiccmd = cmd;
        TicsRun++;
    }

    /// <summary>
    /// <see cref="IGameHost.G_LevelTicker"/>: the level's tic (g_game.c
    /// <c>G_Ticker</c>'s <see cref="gamestate_t.GS_LEVEL"/> part): the
    /// world's (<see cref="World.G_Ticker(in ticcmd_t)"/>, <c>P_Ticker</c>; not
    /// while <paramref name="paused"/>), then the status bar's and the
    /// message line's, which vanilla runs paused too.
    /// </summary>
    public void G_LevelTicker(in ticcmd_t cmd, bool paused)
    {
        if (!paused)
            WorldTic(cmd);
        // T6.11: g_game.c G_Ticker's order: P_Ticker, ST_Ticker, HU_Ticker (TakeEvents), then the HUD is drawn.
        StartHud();
        StatusBar?.ST_Ticker();
        TakeEvents();
        DrawHud();
    }

    private void WorldTic(in ticcmd_t cmd)
    {
        if (TicTimes is { } times)
        {
            long start = Stopwatch.GetTimestamp();
            World!.G_Ticker(cmd);
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        else
            World!.G_Ticker(cmd);
        // T5.6: a player mobj the tic did not let interpolate (a teleport) takes the camera with it.
        if (PlayerMobj is { interp: false })
            SnapPending = true;
        if (_planeMoves.Count > 0)
            MovePlanes();
        PrintUnported();
    }

    /// <summary>The player's last message (<see cref="player_t.message"/>: pickups, locked doors) while it shows, else null (T5.8; T6.11: the message line's, <see cref="HuStuff.Message"/>).</summary>
    public string? HudMessage => MessageLine?.Message;

    /// <summary>
    /// T6.11: st_stuff.c <c>ST_Start</c> and hu_stuff.c <c>HU_Start</c> when
    /// the console player has a new mobj (vanilla's <c>P_SpawnPlayer</c>
    /// calls them: each level start, and a reborn player).
    /// </summary>
    private void StartHud()
    {
        mobj_t? mo = PlayerMobj;
        if (mo == _hudMobj)
            return;
        _hudMobj = mo;
        if (mo is null || World is not { } world)
            return;
        StatusBar?.ST_Start(world.players[world.consoleplayer], world.settings.netgame, world.settings.deathmatch);
        if (StatusBar is not null)
            StatusBar.ConsolePlayer = world.consoleplayer;
        MessageLine?.HU_Start();
        _menuMessage = null; // G_PlayerReborn clears players[].message
    }

    /// <summary>T6.11: draws the status bar (as vanilla's <c>ST_Drawer</c>, what changed), the fullscreen HUD and the message line after a tic.</summary>
    private void DrawHud()
    {
        if (PlayerMobj is null)
            return;
        StatusBar?.ST_Drawer(_redrawStatusBar); // T7.2: whole after a read-this page (D_Display's redrawsbar)
        _redrawStatusBar = false;
        StatusBar?.ST_DrawFullscreen();
        MessageLine?.HU_Drawer();
    }

    /// <summary>
    /// T6.11: shows the HUD under the game camera (<see cref="Hud"/>, in
    /// the level's palette: the flashes tint it as vanilla's) and keeps the
    /// game camera's focus above the bar (<see cref="IsoCamera.BottomInset"/>).
    /// </summary>
    private void UpdateHud()
    {
        Hud.Visible = IsoActive && PlayerMobj is not null && StatusBar is not null && GameState == gamestate_t.GS_LEVEL;
        if (Hud.Visible && Playpal is not null)
            Hud.Show(StatusBar, MessageLine, Playpal, Mesh?.Palette ?? 0);
        if (Iso is not null)
            Iso.BottomInset = Hud.BottomInset;
        _message.Position = new Vector2(8, 8 + Hud.TopInset);
    }

    /// <summary>
    /// T7.1: draws the game state's screen when it changed
    /// (<see cref="Screens"/>): the title loop's page (d_main.c
    /// <c>D_PageDrawer</c>), the intermission (<see cref="WiStuff.WI_Drawer"/>)
    /// or the finale (<see cref="FFinale.F_Drawer"/>) in palette 0, over a
    /// black backdrop, and the pause graphic over them or over the level (in
    /// its palette, flashes included, as vanilla's <c>D_Display</c>).
    /// </summary>
    public void UpdateScreens()
    {
        if (_flow is not { } flow || _graphics is not { } g || Playpal is null)
            return;
        bool pause = flow.paused || FocusPaused;
        gamestate_t state = flow.gamestate;
        int palette = state == gamestate_t.GS_LEVEL ? Mesh?.Palette ?? 0 : 0;
        UpdateMenuScreens(flow, palette, pause); // T7.2: M_Drawer, last (over the pause graphic), on its own layer
        if (state == gamestate_t.GS_LEVEL && (!pause || Mesh is null))
        {
            Screens.Visible = false;
            _screenKey = null;
            return;
        }
        var key = (state, flow.gametic, pause, GetViewport().GetVisibleRect().Size, palette);
        if (_screenKey == key && Screens.Visible)
            return;
        _screenKey = key;
        HudScreen screen = Screens.Screen;
        switch (state)
        {
            case gamestate_t.GS_LEVEL:
                screen.Clear();
                break;
            case gamestate_t.GS_INTERMISSION:
                flow.Wi.WI_Drawer(g, screen);
                break;
            case gamestate_t.GS_FINALE:
                flow.Finale.F_Drawer(g, screen);
                break;
            default:
                screen.Clear();
                g.DrawPage(screen, flow.pagename);
                break;
        }
        if (pause)
            g.DrawPause(screen);
        Screens.Show(Playpal, palette, state != gamestate_t.GS_LEVEL, Hud.FixedScale);
    }

    /// <summary>
    /// T7.1: the window's focus: nothing runs while it is lost
    /// (<see cref="FocusPaused"/>), but under the level check and a level
    /// script, which run without a person.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut && !IsCheckRun && !WadLocator.HasUserArg("--level-script"))
            FocusPaused = true;
        else if (what == NotificationApplicationFocusIn)
            FocusPaused = false;
    }

    private readonly List<sim_event_t> _events = new();

    /// <summary>How many started sounds <see cref="SoundLog"/> keeps.</summary>
    public const int SoundLogLength = 6;

    /// <summary>
    /// The last <see cref="SoundLogLength"/> sounds the sim started (T6.10:
    /// silent until M7, so the overlay lists them), oldest first, with the
    /// tic they started in.
    /// </summary>
    public IReadOnlyList<(int Tic, sound_event_t Sound)> SoundLog => _soundLog;

    private readonly List<(int Tic, sound_event_t Sound)> _soundLog = new();

    /// <summary>
    /// The tic's events (T6.10, <see cref="World.DrainEvents"/>), drained
    /// after each tic: the console player's message (hu_stuff.c
    /// <c>HU_Ticker</c>, T5.8; the sim takes it from
    /// <see cref="player_t.message"/>, <see cref="World.HU_TakeMessages"/>)
    /// goes to the message line (<see cref="HuStuff.HU_Ticker"/>: it shows
    /// for <see cref="HuStuff.HU_MSGTIMEOUT"/> tics, the same message again
    /// too); started sounds go to <see cref="SoundLog"/>.
    /// </summary>
    private void TakeEvents()
    {
        string? message = null;
        _events.Clear();
        World!.DrainEvents(_events);
        foreach (sim_event_t e in _events)
        {
            switch (e.type)
            {
                case simevent_t.se_message when e.player == World.consoleplayer:
                    message = e.message;
                    GD.Print($"Level: tic {World.leveltime}: \"{e.message}\"");
                    break;
                case simevent_t.se_startsound:
                    if (_soundLog.Count == SoundLogLength)
                        _soundLog.RemoveAt(0);
                    _soundLog.Add((e.tic, e.sound));
                    break;
            }
        }
        if (MessageLine is { } hu)
        {
            // T7.2: the menus' message (players[consoleplayer].message) and the messages toggle
            hu.showMessages = _flow?.Menu.showMessages != 0;
            if (_menuMessage is { } menu)
            {
                message ??= menu.Text;
                hu.message_dontfuckwithme |= menu.DontFuckWithMe;
                _menuMessage = null;
            }
            hu.HU_Ticker(message);
        }
    }

    /// <summary>A started sound as the overlay lists it: the name without <c>sfx_</c> and its origin (none, a sector, the player or a mobj type).</summary>
    public string SoundText(sound_event_t s)
    {
        string name = s.sfx.ToString().Replace("sfx_", "", StringComparison.Ordinal);
        if (s.sector is { } sec)
            return $"{name}@sector {sec.Index}";
        if (s.origin is { } mo)
            return mo == PlayerMobj ? $"{name}@player" : $"{name}@{mo.type.ToString().Replace("MT_", "", StringComparison.Ordinal).ToLowerInvariant()}";
        return name;
    }

    /// <summary>
    /// Why the last game stopped (T5.8, T7.1: the end of the game, or a next
    /// map the WAD lacks: back to the title loop), or null; a new game clears it.
    /// </summary>
    public string? LevelEnded { get; private set; }

    /// <summary>The maps completed through an exit since the scene started (T5.8).</summary>
    public int LevelsCompleted { get; private set; }

    /// <summary>The reborns since the scene started (T6.12).</summary>
    public int Reborns { get; private set; }

    /// <summary>
    /// T6.12: a <c>route</c>'s <c>start</c> header while its tics are queued
    /// (the level script's), placed again after a reborn reloads the level,
    /// as the route tests and dump.c do; null otherwise.
    /// </summary>
    public (int X, int Y, int Angle)? RouteStartPoint { get; set; }

    /// <inheritdoc/>
    public bool HasLump(string name) => Wad is { } wad && wad.W_CheckNumForName(name) >= 0;

    // The exception of the last level load the flow asked for (LoadMap rethrows it).
    private Exception? _loadError;

    /// <summary>
    /// <see cref="IGameHost.G_InitNew"/>: a new game on <paramref name="skill"/>
    /// (<see cref="Skill"/> from now on) at map <paramref name="map"/>
    /// (<see cref="LoadMap(string, World?)"/> with a new world).
    /// </summary>
    bool IGameHost.G_InitNew(skill_t skill, string map)
    {
        Skill = skill;
        LevelEnded = null;
        return TryLoad(map, null);
    }

    /// <summary>
    /// <see cref="IGameHost.G_DoWorldDone"/> (T5.8): the next map in the
    /// same world (<see cref="World.G_DoWorldDone"/>: the players keep their
    /// health, armor and weapons).
    /// </summary>
    bool IGameHost.G_DoWorldDone(string map) => TryLoad(map, World);

    /// <summary>
    /// <see cref="IGameHost.G_DoLoadLevel"/> (T6.12, g_game.c's
    /// <see cref="gameaction_t.ga_loadlevel"/>: a dead player pressed use,
    /// <see cref="World.G_DoReborn"/>): the same map afresh in the same
    /// world (<see cref="LoadMap(string, World?)"/>, whose
    /// <see cref="World.G_DoLoadLevel"/> gives the player a fresh start), the
    /// status bar and message line restarted (<see cref="StartHud"/>), the
    /// queued script tics kept (a route goes on through a reborn, its
    /// <see cref="RouteStartPoint"/> placed again).
    /// </summary>
    bool IGameHost.G_DoLoadLevel()
    {
        World world = World!;
        string map = world.level.Name;
        var queued = _scriptTics.ToArray();
        if (!TryLoad(map, world))
            return false;
        foreach (var tic in queued)
            _scriptTics.Enqueue(tic);
        Reborns++;
        if (queued.Length > 0 && RouteStartPoint is { } s && !PlaceRouteStart(s.X, s.Y, s.Angle))
            GD.PrintErr($"Level: reborn: the route's start {s.X} {s.Y}: something stands there");
        GD.Print($"Level: {map} reloaded for a reborn player (G_DoReborn) at tic {TicsRun}");
        return true;
    }

    private bool TryLoad(string map, World? carry)
    {
        try
        {
            LoadMap(map, carry);
            return true;
        }
        catch (Exception e) when (e is WadFormatException or KeyNotFoundException)
        {
            GD.PrintErr($"Level: {map}: {e.Message}");
            _status = $"{map}: {e.Message}";
            _loadError = e;
            return false;
        }
    }

    /// <summary>
    /// <see cref="IGameHost.LevelCompleted"/> (T5.8, g_game.c <c>G_DoCompleted</c>):
    /// counts and prints it; the scripted tics left are dropped (T5.10: they
    /// were for the map just left, e.g. a route whose exit came early).
    /// </summary>
    void IGameHost.LevelCompleted()
    {
        World world = World!;
        LevelsCompleted++;
        if (_scriptTics.Count > 0)
        {
            GD.Print($"Level: {_scriptTics.Count} scripted tic(s) left at the exit dropped");
            _scriptTics.Clear();
        }
        wbplayerstruct_t p = world.wminfo.plyr[world.consoleplayer];
        GD.Print($"Level: {world.level.Name} completed{(world.secretexit ? " (secret exit)" : "")} at tic {world.leveltime}"
            + (world.gameaction == gameaction_t.ga_victory ? ": the episode's end, the finale"
                : $": kills {p.skills}/{world.wminfo.maxkills}, items {p.sitems}/{world.wminfo.maxitems}, "
                    + $"secrets {p.ssecret}/{world.wminfo.maxsecret}; the intermission, then {world.NextMapName()}"));
    }

    /// <summary>
    /// <see cref="IGameHost.EndGame"/> (T7.1, d_main.c <c>D_StartTitle</c>):
    /// the level goes (the title loop shows next); <paramref name="why"/>,
    /// when the game stopped short, shows in the overlay (<see cref="LevelEnded"/>).
    /// </summary>
    void IGameHost.EndGame(string? why)
    {
        if (why is not null)
        {
            LevelEnded = why;
            GD.Print($"Level: {why}; the title loop");
        }
        UnloadLevel();
    }

    // How many of the world's World.unported calls are printed (or were there at its start).
    private int _unportedPrinted;

    /// <summary>
    /// Prints the calls the last tic made to specials not ported yet
    /// (<see cref="World.unported"/>: a line triggered by walking or use, T5.2),
    /// so a trigger can be seen to fire until its effect exists (T5.3-T5.8).
    /// </summary>
    private void PrintUnported()
    {
        List<string> calls = World!.unported;
        for (; _unportedPrinted < calls.Count; _unportedPrinted++)
            GD.Print($"Level: tic {World.leveltime}: {calls[_unportedPrinted]} (not ported yet)");
    }

    /// <summary>While not null, each tic's <see cref="World.G_Ticker(in ticcmd_t)"/> wall-clock time in ms is added (T4.9: the level script's <c>tictime</c>, SPEC §9's budget).</summary>
    public List<double>? TicTimes { get; set; }

    /// <summary>Sets <see cref="TicFraction"/> (the level check draws the world between two tics; the game loop sets it every frame).</summary>
    public void SetTicFraction(double fraction) => TicFraction = Math.Clamp(fraction, 0, 1);

    /// <summary>
    /// Queues one scripted tic (<see cref="ScriptedTics"/>): <paramref name="cmd"/>,
    /// or null for a command built from the input at that tic.
    /// </summary>
    /// <remarks>T6.4: <paramref name="before"/> runs on the world just before that tic (a route's events).</remarks>
    public void QueueTic(ticcmd_t? cmd, Action<World>? before = null)
    {
        if (_beforeNext is { } pending)
        {
            _beforeNext = null;
            before = before is null ? pending : pending + before;
        }
        _scriptTics.Enqueue((cmd, before));
    }

    /// <summary>Drops the scripted tics not run yet.</summary>
    public void ClearQueuedTics() => _scriptTics.Clear();

    /// <summary>
    /// A mobj's position (map units) and facing at <see cref="TicFraction"/>
    /// between the last two tics: from <see cref="mobj_t.oldx"/>… to where it
    /// is, unless it may not be interpolated (<see cref="mobj_t.interp"/>:
    /// spawned during the tic, teleported). The angle turns the short way.
    /// </summary>
    public (Vector3 Position, uint Angle) Interpolated(mobj_t mo)
    {
        double f = TicFraction;
        if (!mo.interp || f >= 1)
            return (new Vector3((float)(mo.x / 65536.0), (float)(mo.y / 65536.0), (float)(mo.z / 65536.0)), mo.angle);
        static float Lerp(int a, int b, double f) => (float)((a + ((long)b - a) * f) / 65536.0);
        uint angle = unchecked(mo.oldangle + (uint)(int)Math.Round(unchecked((int)(mo.angle - mo.oldangle)) * f));
        return (new Vector3(Lerp(mo.oldx, mo.x, f), Lerp(mo.oldy, mo.y, f), Lerp(mo.oldz, mo.z, f)), angle);
    }

    /// <summary>
    /// A sector's floor and ceiling heights (map units) at <see cref="TicFraction"/>
    /// between the last two tics (T5.1): from <see cref="sector_t.oldfloorheight"/>…
    /// to where they are now. Light levels step, as in vanilla.
    /// </summary>
    public (float Floor, float Ceiling) InterpolatedHeights(Sector sector)
    {
        double f = TicFraction;
        if (World is not { } world || f >= 1 || sector.Index >= world.sectors.Length || world.sectors[sector.Index].map != sector)
            return ((float)(sector.FloorHeight / 65536.0), (float)(sector.CeilingHeight / 65536.0));
        sector_t s = world.sectors[sector.Index];
        static float Lerp(int a, int b, double f) => (float)((a + ((long)b - a) * f) / 65536.0);
        return (Lerp(s.oldfloorheight, s.floorheight, f), Lerp(s.oldceilingheight, s.ceilingheight, f));
    }

    private LevelMesh.SectorHeights? _interpolatedHeights;

    /// <summary>
    /// The world's animations (T5.7): every frame of every animation drawn as
    /// the sim's <c>texturetranslation</c>/<c>flattranslation</c> say
    /// (<see cref="LevelMesh.TranslateTexture"/>, <see cref="LevelMesh.TranslateFlat"/>;
    /// stepped on <c>leveltime</c> as vanilla, not interpolated).
    /// </summary>
    public static void Translate(World world, LevelMesh mesh)
    {
        for (int a = 0; a < world.lastanim; a++)
        {
            anim_t anim = world.anims[a]!;
            for (int i = anim.basepic; i < anim.basepic + anim.numpics; i++)
            {
                if (anim.istexture)
                    mesh.TranslateTexture(i, world.texturetranslation[i]);
                else
                    mesh.TranslateFlat(world.flatnames[i], world.flatnames[world.flattranslation[i]]);
            }
        }
    }

    /// <summary>
    /// Draws the world at <see cref="TicFraction"/>: the sectors at their
    /// interpolated heights with their current lights, flats and side
    /// textures (<see cref="LevelMesh.UpdateSectors"/>, T5.1), the player mobj as
    /// <see cref="Player"/>, every other mobj as an entry of <see cref="Things"/>
    /// (rebuilt when the mobjs change), each from its interpolated position,
    /// facing and state.
    /// </summary>
    public void PresentWorld()
    {
        if (World is not { } world)
            return;
        if (Mesh is { } mesh)
        {
            Translate(world, mesh);
            // T6.9: the fuzz shimmers per tic.
            int phase = Fuzz.Phase(world.leveltime);
            if (mesh.FuzzPhase != phase)
                mesh.SetFuzzPhase(phase);
        }
        Mesh?.UpdateSectors(_interpolatedHeights ??= InterpolatedHeights);
        mobj_t? me = PlayerMobj;
        if (Player is not null && me is not null)
            Player.Set(ThingEntry(me, Interpolated(me)));
        if (Things is null)
            return;
        _scratch.Clear();
        foreach (mobj_t mo in world.Mobjs())
        {
            if (mo != me && IsDrawn(mo))
                _scratch.Add(mo);
        }
        bool same = _scratch.Count == _drawn.Count;
        for (int i = 0; same && i < _scratch.Count; i++)
            same = _scratch[i] == _drawn[i];
        if (!same)
        {
            _drawn.Clear();
            _drawn.AddRange(_scratch);
            var entries = new ThingSprites.Entry[_drawn.Count];
            for (int i = 0; i < entries.Length; i++)
                entries[i] = ThingEntry(_drawn[i], Interpolated(_drawn[i]));
            Things.SetEntries(entries);
            return;
        }
        for (int i = 0; i < _drawn.Count; i++)
            Things.SetEntry(i, ThingEntry(_drawn[i], Interpolated(_drawn[i])));
    }

    /// <summary>
    /// T6.6: the level's <c>extralight</c> (<see cref="LevelMesh.SetExtraLight"/>)
    /// from the console player's weapon flash, stepped per tic as vanilla's
    /// (0 with <see cref="WeaponLight"/> off or without a world).
    /// </summary>
    public void UpdateExtraLight()
    {
        if (Mesh is not { } mesh)
            return;
        int extralight = WeaponLight && World is { } world ? world.players[world.consoleplayer].extralight : 0;
        if (mesh.ExtraLight != extralight)
            mesh.SetExtraLight(extralight);
    }

    /// <summary>
    /// T6.8 (SPEC §7.4): the console player's palette, st_stuff.c's
    /// <see cref="StStuff.ST_doPaletteStuff"/> (red for damage and the
    /// berserk, gold for pickups, green in the radiation suit; vanilla's
    /// <c>I_SetPalette</c>, here <see cref="LevelMesh.SetPalette"/> on the level,
    /// its sprites and the blob shadows, with the void tinted as palette index 0
    /// unless <c>--level-background</c> set it), and its <c>fixedcolormap</c>
    /// (r_main.c <c>R_SetupFrame</c>: 32 for invulnerability, 1 for the light
    /// amplification visor, 0 none) as <see cref="LevelMesh.SetColormapOverride"/>;
    /// stepped per tic as vanilla's. Palette 0 and no override with
    /// <see cref="PaletteEffects"/> off or without a world. Sets only what
    /// changed, so a debug override stays until the player's state changes.
    /// </summary>
    public void UpdatePaletteEffects()
    {
        if (Mesh is not { } mesh)
            return;
        int palette = 0, colormap = -1;
        if (PaletteEffects && World is { } world && world.players[world.consoleplayer] is { mo: not null } p)
        {
            palette = StStuff.ST_doPaletteStuff(p);
            if (p.fixedcolormap != 0)
                colormap = p.fixedcolormap;
        }
        if (mesh.Palette != palette)
            mesh.SetPalette(palette);
        // (tracked apart from the mesh's: a new map's mesh starts at palette 0)
        if (_voidPalette != palette && !_backgroundSet && Playpal is { } playpal && palette < playpal.Count)
        {
            _voidPalette = palette;
            (byte r, byte g, byte b) = playpal.GetColor(palette, 0);
            Environment.BackgroundColor = palette == 0 ? _background : Color.Color8(r, g, b);
        }
        if (mesh.ColormapOverride != colormap)
            mesh.SetColormapOverride(colormap);
    }

    /// <summary>
    /// T5.6: whether a tic moved the player mobj without interpolation (a
    /// teleport: <see cref="mobj_t.interp"/> false after the tic), so the game
    /// camera snaps to it (<see cref="SnapCameraIfPending"/>) instead of
    /// sliding across the map.
    /// </summary>
    public bool SnapPending { get; private set; }

    /// <summary>How many times the game camera snapped to a teleported player (T5.6; the level script's <c>print</c>).</summary>
    public int TeleportSnaps { get; private set; }

    /// <summary>T5.6: snaps the game camera to the drawn player when a tic teleported it (<see cref="SnapPending"/>); true when it did.</summary>
    public bool SnapCameraIfPending()
    {
        if (!SnapPending || Player is null || Iso is null)
            return false;
        SnapPending = false;
        Iso.Snap(Player.Foot);
        TeleportSnaps++;
        return true;
    }

    /// <summary>
    /// Whether a mobj is drawn (T5.6): not with <c>MF_NOSECTOR</c>, which keeps
    /// it out of its sector's thing list, the list vanilla's renderer draws
    /// from (r_things.c <c>R_AddSprites</c>): teleport destinations
    /// (<c>MT_TELEPORTMAN</c>, whose state has no sprite of its own).
    /// </summary>
    public static bool IsDrawn(mobj_t mo) => (mo.flags & mobjflag_t.MF_NOSECTOR) == 0;

    /// <summary>The game camera follows the player (T3.3) with its look-ahead towards the cursor ground point; the player's billboard turns to the current camera.</summary>
    private void FollowPlayer(double delta)
    {
        if (Player is null)
            return;
        if (!SnapCameraIfPending() && Iso is { Current: true } iso && !HoldCamera)
            iso.Follow(Player.Foot, Cursor?.Point, delta);
        if (GetViewport().GetCamera3D() is Camera3D current)
            Player.FaceCamera(current);
    }

    /// <summary>
    /// The cutaway (T3.4) follows the player (and the cursor ground point
    /// when <see cref="CutawaySettings.Cursor"/>) under the game camera; the
    /// overview and free-fly cameras show the walls whole.
    /// </summary>
    public void UpdateCutaway()
    {
        if (Mesh is null)
            return;
        bool on = IsoActive && Player is not null && PlayerMobj is not null;
        CutawaySettings settings = on ? Cutaway : Cutaway with { Style = CutawayStyle.Off };
        if (Mesh.Cutaway != settings)
            Mesh.SetCutaway(settings);
        Vector3? player = on ? new Vector3(Player!.MapPosition.X, Player.MapPosition.Y, Player.Z) : null;
        Vector3? cursor = on && Cutaway.Cursor && Cursor is { } hit ? hit.MapUnits : null;
        if (Mesh.CutPlayer != player || Mesh.CutCursor != cursor)
            Mesh.SetCutawayCentres(player, cursor);
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && !IsCheckRun)
            GameInput.CursorMoved(motion.Relative.X);
        // T7.2: the menus before anything else (d_main.c D_ProcessEvents: M_Responder first; on the title loop
        // any key opens them, g_game.c G_Responder), so the cameras' keys don't act under them
        if (!IsCheckRun && MenuEvent(e))
            GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// T4.6: the <c>ticcmd</c> <see cref="TiccmdBuilder"/> makes for the
    /// player mobj (its x, y and angle) from the input since the last call,
    /// with <paramref name="tweaks"/>; called once per tic (T4.7; the level
    /// script's <c>ticcmd</c> prints one). Only the game camera reads the
    /// input on a level (the free-fly camera shares W/A/S/D, E and Space):
    /// under the others the latches are dropped (but the pause key, T7.1)
    /// and the command only keeps the player's angle. The other game states
    /// (T7.1: the title loop, the intermission, the finale) read the input
    /// under every camera.
    /// </summary>
    public ticcmd_t BuildTiccmd(Tweaks tweaks)
    {
        TiccmdInput input = MenuFilter(GameInput.Take(Cursor is { } hit ? (hit.MapUnits.X, hit.MapUnits.Y) : null)); // T7.2: none under the menus
        if (!IsoActive && GameState == gamestate_t.GS_LEVEL)
            input = new TiccmdInput { Pause = input.Pause }; // T7.1: the pause key works under every camera
        mobj_t? mo = PlayerMobj;
        uint screenUp = Iso is not null ? GameInput.ScreenUp(Iso.GroundUp) : Tables.ANG90;
        player_t? player = World?.players[World.consoleplayer];
        GameMode mode = World?.gamemode ?? GameMode.shareware;
        return mo is null
            ? TiccmdBuilder.G_BuildTiccmd(input, tweaks, screenUp, 0, 0, Tables.ANG90, player, mode)
            : TiccmdBuilder.G_BuildTiccmd(input, tweaks, screenUp, mo.x, mo.y, mo.angle, player, mode);
    }

    /// <summary>A small cross at the screen centre (the free-fly camera's view direction; straight down it marks the floor the overlay names).</summary>
    private static Control Crosshair()
    {
        var root = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.Center);
        foreach (Rect2 r in new[] { new Rect2(-6, -1, 5, 2), new Rect2(1, -1, 5, 2), new Rect2(-1, -6, 2, 5), new Rect2(-1, 1, 2, 5) })
            root.AddChild(new ColorRect { Position = r.Position, Size = r.Size, Color = Colors.White, MouseFilter = Control.MouseFilterEnum.Ignore });
        return root;
    }

    /// <summary>
    /// The debug overlay (SPEC §10): map, FPS, camera; for the free-fly camera
    /// its position in map units, angle, and the subsector and sector under it
    /// (<see cref="Level.R_PointInSubsector"/>); then the build status.
    /// </summary>
    public string OverlayText()
    {
        var text = new System.Text.StringBuilder();
        string map = Mesh?.Level.Name ?? "no map";
        text.Append($"{map} ({IndexOfMap(map) + 1}/{MapNames.Count})   FPS {Engine.GetFramesPerSecond():F0}   ");
        if (FreeFly is { } fly && fly.Current)
        {
            text.Append(fly.IsOrthographic
                ? $"free-fly, orthographic {fly.Size * LevelMesh.MapUnitsPerMetre:F0} units"
                : $"free-fly, perspective {fly.Fov:F0}°");
            text.Append($"   speed {fly.Speed:F0}/s\n");
            (int x, int y, int z) = MapPosition(fly.Pivot);
            text.Append($"x {x}  y {y}  z {z}   angle {fly.MapAngle:F0}°  pitch {fly.Pitch:F0}°\n");
            if (Mesh is not null)
            {
                int fx = ToFixed(fly.Pivot.X * LevelMesh.MapUnitsPerMetre), fy = ToFixed(-fly.Pivot.Z * LevelMesh.MapUnitsPerMetre);
                Subsector ss = Mesh.Level.R_PointInSubsector(fx, fy);
                Sector s = ss.Sector;
                int drawn = Mesh.Floors.SectorAt(fx, fy);
                text.Append($"sector {s.Index} (floor {s.FloorHeight >> Fixed.FRACBITS} {s.FloorPic}, ceiling {s.CeilingHeight >> Fixed.FRACBITS}, light {s.LightLevel})   subsector {ss.Index}");
                text.Append(drawn == s.Index ? "\n" : drawn < 0 ? "   (outside the map: no floor here)\n" : $"   (floor drawn here: sector {drawn})\n");
            }
        }
        else if (Iso is { } iso && iso.Current)
        {
            text.Append(iso.Mode == IsoProjection.Orthographic ? "game camera, orthographic" : $"game camera, perspective {IsoCamera.PerspectiveFov:F0}°");
            text.Append($", view {iso.ViewUnits:F0} units, pitch {iso.Pitch:F0}°\n");
            (int fx, int fy, int fz) = MapPosition(iso.Focus);
            text.Append($"focus x {fx}  y {fy}  z {fz}\n");
        }
        else
            text.Append(FreeFly is null ? "overview\n" : "overview (Tab: free-fly)\n");
        if (Player is { } p && PlayerMobj is { } mo && Mesh is not null && !FreeFlyActive)
        {
            Vector2 at = p.MapPosition;
            text.Append($"player x {at.X:F0}  y {at.Y:F0}  z {p.Z:F0}   angle {p.Angle:F0}°  rotation {p.Rotation + 1}  {mo.state}");
            Sector ps = mo.subsector.sector.map;
            text.Append($"   sector {ps.Index} (floor {ps.FloorHeight >> Fixed.FRACBITS} {ps.FloorPic}, light {ps.LightLevel}"
                + $"{(SpriteOptions.PlayerLight > ps.LightLevel ? $", sprite lit at {SpriteOptions.PlayerLight}" : "")}{(ps.Special != 0 ? $", special {ps.Special}" : "")})\n");
        }
        if (World is { } w && w.players[w.consoleplayer] is { mo: not null } pl)
            text.Append(StatusText(w, pl) + "\n");
        if (StatusBar is { plyr: not null } st)
            text.Append($"hud: {Hud.Mode.ToString().ToLowerInvariant()} (=/-){(Hud.Visible ? $", scale {Hud.Scale}" : ", hidden (game camera only)")}, face {StStuff.FaceName(st.st_faceindex)}"
                + $" for {st.st_facecount + 1} tics, M_Random index {MRandom.rndindex}\n");
        if (HudMessage is not null)
            text.Append($"message: {HudMessage}\n");
        if (World is { } sw && _soundLog.Count > 0 && _soundLog[^1].Tic >= sw.leveltime - SimInfo.TICRATE)
            text.Append("sounds: " + string.Join(", ", _soundLog.Where(l => l.Tic >= sw.leveltime - SimInfo.TICRATE).Select(l => SoundText(l.Sound))) + "\n");
        if (_flow is { } flow)
            text.Append($"game: {flow.StateText()}{(FocusPaused ? ", focus lost (paused)" : "")}   gametic {flow.gametic}   menu: {flow.Menu.StateText()}\n");
        if (LevelEnded is not null)
            text.Append(LevelEnded + "\n");
        if (World is { } world)
            text.Append($"tic {world.leveltime}{(Paused ? " (paused)" : ScriptedTics ? $" (scripted, {QueuedTics} queued)" : "")}   checksum {world.Checksum():x16}   "
                + $"ticcmd {LastTiccmd.forwardmove} {LastTiccmd.sidemove} {LastTiccmd.angleturn}{(Tweaks == Tweaks.Vanilla ? "   tweaks: vanilla" : "")}{(Tweaks.AimAssist ? $"   aim assist ±{Tweaks.AimAssistCone * (360.0 / 4294967296.0):0.#}°" : "")}"
                + $"{(TeleportSnaps > 0 ? $"   teleports {TeleportSnaps}" : "")}\n");
        if (Cursor is { } hit)
        {
            Vector3 c = hit.MapUnits;
            text.Append($"cursor x {c.X:F0}  y {c.Y:F0}  z {c.Z:F0}   ");
            text.Append(hit.OnFloor ? $"floor of sector {hit.Sector}\n" : "no floor (plane at the player's height)\n");
        }
        if (Iso is { Current: true })
            text.Append(Cutaway.Style == CutawayStyle.Off
                ? "cutaway: off\n"
                : $"cutaway: {Cutaway.Style.ToString().ToLowerInvariant()}, radius {Cutaway.Radius:F0}, above {Cutaway.Height:F0}{(Cutaway.Cursor ? ", and around the cursor" : "")}, cap {Cutaway.Cap.ToString().ToLowerInvariant()}, things {ThingsName(Cutaway.Things)}\n");
        if (Mesh is not null)
        {
            Vector2 o = Mesh.LightOrigin;
            text.Append(Mesh.LightMode switch
            {
                LightDiminishing.None => $"light: none (fixed distance {Mesh.LightReference:F0})\n",
                LightDiminishing.Camera => "light: camera depth\n",
                _ => $"light: player distance from ({o.X:F0}, {o.Y:F0}), at least {Mesh.LightNear:F0}\n",
            });
        }
        if (Mesh is { MaskedBacks: MaskedBackFaces.Off })
            text.Append("masked middles: one side only, as vanilla (M)\n");
        if (Mesh is { LidMode: DoorLidMode.Off })
            text.Append("door lids: off (J)\n");
        text.Append(_status);
        text.Append(_showHelp ? "\n" + ControlsHelp : "\nF1: controls");
        return text.ToString();
    }

    /// <summary>
    /// The player's status for the overlay until the HUD (M6, T5.8): health,
    /// armor, keys, power-ups, and the level's kills, items and secrets.
    /// </summary>
    public static string StatusText(World world, player_t p)
    {
        string[] keyNames = { "blue card", "yellow card", "red card", "blue skull", "yellow skull", "red skull" };
        var keys = new List<string>();
        for (int i = 0; i < p.cards.Length; i++)
        {
            if (p.cards[i])
                keys.Add(keyNames[i]);
        }
        var powers = new List<string>();
        for (int i = 0; i < p.powers.Length; i++)
        {
            if (p.powers[i] == 0)
                continue;
            bool timed = (powertype_t)i is not (powertype_t.pw_strength or powertype_t.pw_allmap);
            powers.Add(((powertype_t)i).ToString()[3..] + (timed ? $" {(p.powers[i] + SimInfo.TICRATE - 1) / SimInfo.TICRATE}s" : ""));
        }
        // T6.8: the palette flash and the fixed colormap the scene shows (UpdatePaletteEffects).
        int palette = StStuff.ST_doPaletteStuff(p);
        string effects = (palette != 0 ? $"   palette {palette}" : "") + (p.fixedcolormap != 0 ? $"   colormap {p.fixedcolormap}" : "");
        return $"health {p.health}{(p.playerstate == playerstate_t.PST_DEAD ? " (dead)" : "")}   armor {p.armorpoints}{(p.armortype != 0 ? (p.armortype == 1 ? " green" : " blue") : "")}"
            + $"   keys {(keys.Count == 0 ? "none" : string.Join(", ", keys))}{(powers.Count == 0 ? "" : "   powers " + string.Join(", ", powers))}{effects}"
            + $"   kills {p.killcount}/{world.totalkills}  items {p.itemcount}/{world.totalitems}  secrets {p.secretcount}/{world.totalsecret}"
            + $"\n{WeaponText(p)}";
    }

    /// <summary>
    /// T6.6: the weapon line of the overlay (until the status bar, T6.11): the
    /// ready weapon (and the pending one), its ammo, the weapons owned and every ammo count.
    /// </summary>
    public static string WeaponText(player_t p)
    {
        string Name(weapontype_t w) => w.ToString()[3..];
        ammotype_t ammo = Info.weaponinfo[(int)p.readyweapon].ammo;
        var owned = new List<string>();
        for (int i = 0; i < p.weaponowned.Length; i++)
        {
            if (p.weaponowned[i])
                owned.Add(Name((weapontype_t)i));
        }
        return $"weapon {Name(p.readyweapon)}{(p.pendingweapon != weapontype_t.wp_nochange ? $" -> {Name(p.pendingweapon)}" : "")}"
            + $"{(ammo != ammotype_t.am_noammo ? $" ({p.ammo[(int)ammo]})" : "")}   owned {string.Join(", ", owned)}"
            + $"   ammo {p.ammo[0]}/{p.maxammo[0]} bullets, {p.ammo[1]}/{p.maxammo[1]} shells, {p.ammo[3]}/{p.maxammo[3]} rockets, {p.ammo[2]}/{p.maxammo[2]} cells";
    }

    /// <summary>
    /// The player position for light diminishing (map units x, y):
    /// <c>--level-light-origin</c>, else the free-fly camera's pivot when it
    /// is current, else the player mobj as drawn (T4.7), else player 1's
    /// start (the map's centre without one).
    /// </summary>
    public Vector2 LightOrigin()
    {
        if (WadLocator.GetUserArg("--level-light-origin") is string arg)
        {
            string[] xy = arg.Split(',');
            if (xy.Length == 2 && float.TryParse(xy[0], System.Globalization.CultureInfo.InvariantCulture, out float x)
                && float.TryParse(xy[1], System.Globalization.CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);
        }
        if (FreeFly is { } fly && fly.Current)
            return new Vector2(fly.Pivot.X, -fly.Pivot.Z) * LevelMesh.MapUnitsPerMetre;
        if (Player is { } p && PlayerMobj is not null)
            return p.MapPosition;
        if (Mesh is null)
            return Vector2.Zero;
        if (Mesh.Level.PlayerStart(0) is MapThing start)
            return new Vector2(start.X, start.Y);
        Vector3 c = Mesh.Bounds.GetCenter();
        return new Vector2(c.X, -c.Z) * LevelMesh.MapUnitsPerMetre;
    }

    /// <summary>A Godot-space point in whole map units (x, y, height), rounded.</summary>
    public static (int X, int Y, int Z) MapPosition(Vector3 p) =>
        ((int)MathF.Round(p.X * LevelMesh.MapUnitsPerMetre), (int)MathF.Round(-p.Z * LevelMesh.MapUnitsPerMetre), (int)MathF.Round(p.Y * LevelMesh.MapUnitsPerMetre));

    /// <summary>Map units to fixed_t, clamped to the fixed_t range.</summary>
    private static int ToFixed(float units) => (int)Math.Clamp(Math.Round(units * 65536.0), int.MinValue, int.MaxValue);

    /// <summary>Opens the IWAD <see cref="WadLocator"/> finds and reads its texture table, palettes and map list.</summary>
    private void OpenWad()
    {
        IwadSearchResult found = WadLocator.Find(out _);
        if (found.Warning is not null)
            GD.PushWarning($"Level: {found.Warning}");
        if (found.Path is null)
            throw new IOException(found.Error ?? "No IWAD found (pass -- -iwad PATH or set ISODOOM_IWAD).");

        var clock = Stopwatch.StartNew();
        var wad = WadArchive.Open(found.Path, [.. found.Pwads]);
        IwadInfo info = IwadIdentification.D_IdentifyVersion(wad);
        ModifiedGame.D_CheckModifiedGame(wad, info);
        Textures = Textures.R_InitTextures(wad);
        GameMode = info.GameMode;
        GameVariant = info.GameVariant;
        try
        {
            Sprites = Sprites.R_InitSprites(wad);
            SpriteAtlas = SpriteAtlas.Build(wad, Sprites);
        }
        catch (WadFormatException e)
        {
            GD.PushWarning($"Level: no sprites: {e.Message}");
            (Sprites, SpriteAtlas) = (null, null);
        }
        Playpal = Playpal.Load(wad);
        Colormap = Colormap.Load(wad);
        StatusBar = new StStuff(StStuff.Graphics.Load(wad), MRandom); // T6.11: st_stuff.c ST_Init
        MessageLine = new HuStuff(wad); // hu_stuff.c HU_Init
        MapNames = FindMaps(wad);
        Wad = wad;
        OpenWadMilliseconds = clock.Elapsed.TotalMilliseconds;
        GD.Print($"Level: {found.Path}: {info}, {MapNames.Count} maps; opened in {OpenWadMilliseconds:F0} ms"
            + (SpriteAtlas is { } sa ? $" (sprite atlas: {sa.Lumps.Count} lumps in {sa.Atlas.Image.Width}x{sa.Atlas.Image.Height}, {sa.BuildMilliseconds:F0} ms)" : ""));
    }

    /// <summary>The first of <c>E1M1</c>/<c>MAP01</c> the WAD has (else <c>MAP01</c>, which then fails to load).</summary>
    public string DefaultMap() => Wad!.W_CheckNumForName("E1M1") >= 0 ? "E1M1" : "MAP01";

    private static List<string> FindMaps(WadArchive wad)
    {
        var maps = new List<string>();
        for (int i = 0; i + 1 < wad.NumLumps; i++)
        {
            string name = wad.Lumps[i].Name;
            if (LumpDirectory.IsMapName(name) && wad.Lumps[i + 1].Name == "THINGS" && !maps.Contains(name))
                maps.Add(name);
        }
        return maps;
    }

    /// <summary>
    /// Shows map <paramref name="map"/> of the open WAD: frees the previous
    /// level's chunks, builds the new <see cref="LevelMesh"/> and frames the
    /// overview camera. Throws <see cref="WadFormatException"/> (or
    /// <see cref="KeyNotFoundException"/> for an unknown flat) when the map
    /// can't be built.
    /// </summary>
    /// <remarks>
    /// T7.1: a new game on the map (<see cref="GameFlow.G_InitNewMap"/>: as
    /// <c>G_InitNew</c>, on <see cref="Skill"/>; the debug arguments' way
    /// in: <c>--level MAP</c>, PgDn/PgUp, the level check, the level script).
    /// A map that can't be built leaves the game at the title loop.
    /// </remarks>
    public void LoadMap(string map)
    {
        _loadError = null;
        Flow.G_InitNewMap(Skill, map);
        if (_loadError is { } e)
        {
            _loadError = null;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e);
        }
    }

    /// <summary>
    /// Builds and shows map <paramref name="map"/>: a new game's world, or
    /// with <paramref name="carry"/> (T5.8, the level flow) the game going on
    /// in that world, its players as they left the last level
    /// (<see cref="World.G_DoWorldDone"/>), or a reborn's
    /// (<see cref="gameaction_t.ga_loadlevel"/>, T6.12).
    /// </summary>
    private void LoadMap(string map, World? carry)
    {
        WadArchive wad = Wad ?? throw new InvalidOperationException("No WAD open.");
        map = map.ToUpperInvariant();
        if (wad.W_CheckNumForName(map) < 0)
            throw new WadFormatException($"the WAD has no map {map}");

        UnloadLevel();
        SnapPending = false;
        TeleportSnaps = 0;
        TiccmdBuilder.Reset(); // the player keeps its angle until something aims (T4.6)
        ClearQueuedTics();
        _ticTime = 0;
        TicFraction = 1;

        var clock = Stopwatch.StartNew();
        Level level = Level.Load(wad, map);
        (List<string[]> animTextures, List<string[]> animFlats) = AnimGroups(wad);
        LevelMesh mesh = LevelMesh.Build(wad, level, Textures!, Playpal!, Colormap!,
            textureGroups: System.Linq.Enumerable.Concat(SwitchGroups(GameMode), animTextures), flatGroups: animFlats);
        if (WadLocator.GetUserArg("--level-tiling") is string tiling)
            mesh.SetWallTiling(tiling == "size" ? WallTextureTiling.TextureSize : WallTextureTiling.Vanilla);
        mesh.SetLightDiminishing(_lightMode);
        mesh.SetMaskedBackFaces(MaskedBacks);
        mesh.SetDoorLids(DoorLids);
        if (WadLocator.GetUserArg("--level-light-near") is string near)
            mesh.SetLightNear(Math.Max(0, ParseFloat(near, "--level-light-near")));
        if (WadLocator.GetUserArg("--level-light-reference") is string reference)
            mesh.SetLightReference(Math.Max(0, ParseFloat(reference, "--level-light-reference")));
        mesh.SetSprites(SpriteOptions);

        Chunks = new MeshInstance3D?[mesh.SectorMeshes.Length];
        for (int s = 0; s < mesh.SectorMeshes.Length; s++)
        {
            if (mesh.SectorMeshes[s] is not ArrayMesh chunkMesh)
                continue;
            var node = new MeshInstance3D { Mesh = chunkMesh, Name = $"Sector{s}", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(node);
            _chunks.Add(node);
            Chunks[s] = node;
        }
        StartWorld(level, carry); // (a new game's M_ClearRandom is GameFlow.G_InitNewMap's, T6.11)
        _hudMobj = null;
        StartHud(); // T6.11: P_SpawnPlayer's ST_Start and HU_Start (drawn after the first tic's ST_Ticker, as vanilla's)
        BuildThings(level, mesh);
        clock.Stop();
        LastLoadMilliseconds = clock.Elapsed.TotalMilliseconds;
        Mesh = mesh;
        if (SpriteAtlas is not null)
            Player?.Bind(SpriteAtlas, mesh.SpriteMaterial, mesh.ShadowMaterial);
        if (Player is not null)
            Player.Visible = _showThings && PlayerMobj is not null;

        FrameCamera();
        if (FreeFly is not null)
            JumpToStart();
        PresentWorld();
        if (Player is not null && PlayerMobj is not null)
            Iso?.Snap(Player.Foot);
        else
            Iso?.Snap(Mesh.Bounds.GetCenter());
        string text = $"{map}: {level.Sectors.Length} sectors, {mesh.FloorTriangleCount} floor triangles, {mesh.WallQuads} wall quads, {mesh.MaskedQuads} masked (+{mesh.MaskedBackQuads} back), {mesh.Lids.Sectors.Count} lids, {_drawn.Count} things, "
            + $"{mesh.SlotNames.Count} textures in a {mesh.Atlas.Image.Width}x{mesh.Atlas.Image.Height} atlas, {mesh.Walls.Missing.Count} missing; "
            + $"built in {clock.ElapsedMilliseconds} ms";
        GD.Print($"Level: {text}");
        _message.Text = _status = text;
    }

    /// <summary>
    /// Frees the level's chunks, billboards and world (before the next
    /// level, or for the title loop, T7.1: <see cref="IGameHost.EndGame"/>).
    /// </summary>
    private void UnloadLevel()
    {
        foreach (MeshInstance3D chunk in _chunks)
            chunk.QueueFree();
        _chunks.Clear();
        Chunks = Array.Empty<MeshInstance3D?>();
        Mesh = null;
        Things?.QueueFree();
        Things = null;
        _drawn.Clear();
        World = null;
        _hudMobj = null;
        if (Player is not null)
            Player.Visible = false;
        Cursor = null;
        if (_cursorMarker is not null)
            _cursorMarker.Visible = false;
        _soundLog.Clear();
        _planeMoves.Clear();
    }

    /// <summary>
    /// The texture groups a level's walls change between at run time (SPEC §12
    /// T5.1): the switch pairs of <paramref name="mode"/> (p_switch.c
    /// <c>alphSwitchList</c> as <c>P_InitSwitchList</c> picks them), which
    /// <see cref="LevelMesh.Build"/> puts in the atlas up front.
    /// </summary>
    public static IEnumerable<IReadOnlyList<string>> SwitchGroups(GameMode mode)
    {
        foreach (switchlist_t s in Switches.For(mode))
            yield return new[] { s.name1, s.name2 };
    }

    /// <summary>
    /// The animation sequences of the WAD (p_spec.c <c>animdefs</c> as
    /// <c>P_InitPicAnims</c> finds them, T5.7), wall textures and flats, which
    /// <see cref="LevelMesh.Build"/> puts in the atlas up front; none (with a
    /// warning) when the WAD's sequences are broken, as vanilla would error.
    /// </summary>
    public (List<string[]> Textures, List<string[]> Flats) AnimGroups(WadArchive wad)
    {
        try
        {
            return PicAnims.Sequences(Textures, FlatNames(wad));
        }
        catch (WadFormatException e)
        {
            GD.PushWarning($"Level: no animated textures: {e.Message}");
            return (new List<string[]>(), new List<string[]>());
        }
    }

    // The WAD's flat namespace by name (vanilla's flat numbers), per WAD.
    private (WadArchive Wad, List<string> Names)? _flatNames;

    private List<string> FlatNames(WadArchive wad)
    {
        if (_flatNames is not { } f || f.Wad != wad)
            _flatNames = f = (wad, PicAnims.FlatNames(wad));
        return f.Names;
    }

    /// <summary>
    /// A new game on <see cref="Skill"/> with <see cref="Tweaks"/> and its
    /// first level, <paramref name="level"/> (the mesh's own: the world
    /// changes its sectors in place, SPEC §12 T4.2): <c>G_InitNew</c> and
    /// <c>G_DoLoadLevel</c>; or, with <paramref name="carry"/>, that game's
    /// next level (<see cref="World.G_DoWorldDone"/>, T5.8). A thing type the game doesn't know (vanilla
    /// <c>I_Error</c>s) leaves the map without a world, so without things.
    /// </summary>
    private void StartWorld(Level level, World? carry = null)
    {
        try
        {
            World world;
            if (carry is not null)
            {
                world = carry;
                if (world.gameaction == gameaction_t.ga_loadlevel)
                    world.G_DoLoadLevel(level); // T6.12: a reborn, the same level afresh
                else
                    world.G_DoWorldDone(level); // T5.8: the next level of the same game
            }
            else
            {
                world = new World(new SpawnSettings(GameMode, Skill, nomonsters: NoMonsters), Tweaks)
                {
                    textures = Textures,
                    map31exists = Wad!.W_CheckNumForName("MAP31") >= 0, // G_SecretExitLevel's check (T5.8)
                };
                world.P_InitPicAnims(Textures, FlatNames(Wad!)); // P_Init's (T5.7)
                world.G_DoLoadLevel(level);
            }
            World = world;
            _unportedPrinted = world.unported.Count; // the level's start (P_SpawnSpecials) is not news
        }
        catch (WadFormatException e)
        {
            GD.PushWarning($"Level: {level.Name}: no things: {e.Message}");
            World = null;
        }
    }

    /// <summary>
    /// The world's mobjs but the player's (T3.5's billboards; from the mobjs
    /// since T4.7, <see cref="PresentWorld"/> keeps them in step). Voodoo
    /// dolls (more starts of player 1) are drawn as things, as vanilla.
    /// </summary>
    private void BuildThings(Level level, LevelMesh mesh)
    {
        if (SpriteAtlas is null)
            return;
        Things = new ThingSprites { Name = "Things" };
        Things.Bind(SpriteAtlas, mesh.SpriteMaterial, mesh.ShadowMaterial);
        _drawn.Clear();
        PresentWorld(); // fills the entries from the mobjs
        Things.Visible = Things.Shadows.Visible = _showThings;
        AddChild(Things);
        if (Things.MissingFrames > 0)
            GD.PushWarning($"Level: {level.Name}: {Things.MissingFrames} thing(s) whose spawn frame the WAD lacks are not drawn");
    }

    /// <summary>A spawned thing as a billboard entry (map units), with a blob shadow of its radius when it is an actor (<see cref="ShadowRadius"/>) on its sector's floor, marked as an actor (<see cref="IsActor"/>, T3.4b), and its radius (the wall pull, T3.5a): what a mobj spawned there shows before its first tic (<see cref="ThingEntry(mobj_t)"/>).</summary>
    public static ThingSprites.Entry ThingEntry(SpawnedThing t) =>
        new(new Vector3((float)(t.x / 65536.0), (float)(t.y / 65536.0), (float)(t.z / 65536.0)), t.angle, t.Sector.Index, (int)t.sprite, t.frame, t.fullbright,
            ShadowRadius(Info.mobjinfo[(int)t.Spawn.Type]), IsActor(Info.mobjinfo[(int)t.Spawn.Type]), Info.mobjinfo[(int)t.Spawn.Type].radius / 65536f,
            (float)(t.Sector.FloorHeight / 65536.0), Shadow: (Info.mobjinfo[(int)t.Spawn.Type].flags & mobjflag_t.MF_SHADOW) != 0);

    /// <summary>A mobj as a billboard entry where it is now (T4.7).</summary>
    public static ThingSprites.Entry ThingEntry(mobj_t mo) =>
        ThingEntry(mo, (new Vector3((float)(mo.x / 65536.0), (float)(mo.y / 65536.0), (float)(mo.z / 65536.0)), mo.angle));

    /// <summary>
    /// A mobj as a billboard entry at <paramref name="at"/> (map units and
    /// facing, <see cref="Interpolated"/>): its sector (<c>subsector->sector</c>,
    /// the light), its state's sprite and frame (<c>FF_FULLBRIGHT</c>), the
    /// blob shadow on its <c>floorz</c> (so a falling or flying thing's shadow
    /// stays on the floor), actor flag and radius as <see cref="ThingEntry(SpawnedThing)"/>
    /// (an exploding missile's: <see cref="PullRadius"/>), and its current
    /// <c>MF_SHADOW</c> (T6.9: the fuzz; a spectre's, or the player's while
    /// it has the blur sphere).
    /// </summary>
    public static ThingSprites.Entry ThingEntry(mobj_t mo, (Vector3 Position, uint Angle) at) =>
        new(at.Position, at.Angle, mo.subsector.sector.Index, (int)mo.sprite, mo.frame & Info.FF_FRAMEMASK, (mo.frame & Info.FF_FULLBRIGHT) != 0,
            ShadowRadius(mo.info), IsActor(mo.info), PullRadius(mo), (float)(mo.floorz / 65536.0), Shadow: (mo.flags & mobjflag_t.MF_SHADOW) != 0);

    /// <summary>
    /// The radius a mobj's billboard is pulled by towards the camera, up to
    /// <see cref="SpriteSettings.WallPull"/> (T3.5a): its <c>radius</c>, but
    /// for an exploding missile (a type with <c>MF_MISSILE</c> that
    /// <c>P_ExplodeMissile</c> took it from) the whole pull, since its
    /// explosion frames are much wider than its 6–11 unit radius and it
    /// stands against the wall it hit (T6.5, SPEC §12).
    /// </summary>
    public static float PullRadius(mobj_t mo) =>
        (mo.info.flags & mobjflag_t.MF_MISSILE) != 0 && (mo.flags & mobjflag_t.MF_MISSILE) == 0 ? ExplosionPullRadius : mo.radius / 65536f;

    /// <summary>T6.5: <see cref="PullRadius"/> of an exploding missile, more than any wall pull.</summary>
    public const float ExplosionPullRadius = 1024f;

    /// <summary>
    /// Whether a thing of <paramref name="info"/> is an actor the cutaway keeps
    /// whole by default (T3.4b, <see cref="CutawayThings.Decorations"/>):
    /// <c>MF_SHOOTABLE</c> (monsters, barrels, the player), what the player
    /// must see to shoot.
    /// </summary>
    public static bool IsActor(mobjinfo_t info) => (info.flags & mobjflag_t.MF_SHOOTABLE) != 0;

    /// <summary>
    /// The blob shadow's radius for a thing of <paramref name="info"/> (T3.6,
    /// SPEC §7.5): its radius (map units) for actors, things with
    /// <c>MF_SHOOTABLE</c> (monsters, the player, barrels) standing on the
    /// floor; 0 (none) for everything else.
    /// </summary>
    public static float ShadowRadius(mobjinfo_t info) =>
        (info.flags & mobjflag_t.MF_SHOOTABLE) != 0 && (info.flags & mobjflag_t.MF_SPAWNCEILING) == 0 ? info.radius / 65536f : 0f;

    private void MoveFloors(Level level, string moves)
    {
        foreach (string move in moves.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = move.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int s) || !int.TryParse(parts[1], out int height)
                || s < 0 || s >= level.Sectors.Length)
                throw new ArgumentException($"--level-sector-floor: bad entry \"{move}\" (SECTOR:HEIGHT)");
            level.Sectors[s].FloorHeight = height << Fixed.FRACBITS;
            if (World is { } world && world.level == level)
            {
                world.sectors[s].StoreInterpolation(); // a debug move: not interpolated
                world.P_ChangeSector(world.sectors[s], false);
            }
            GD.Print($"Level: sector {s} floor set to {height}");
        }
        Mesh!.UpdateSectors();
    }

    /// <summary>Frames the overview camera on the level (or the <c>--level-focus</c> sector).</summary>
    public void FrameCamera()
    {
        if (Mesh is null)
            return;
        Aabb bounds = Mesh.Bounds;
        if (WadLocator.GetUserArg("--level-focus") is string focus && int.TryParse(focus, out int s) && s >= 0 && s < Mesh.Level.Sectors.Length)
            bounds = SectorBounds(Mesh.Level.Sectors[s]);
        Vector2 size = GetViewport().GetVisibleRect().Size;
        LevelCamera.FrameOverview(_camera, bounds, size.Y > 0 ? size.X / size.Y : 1.6f, WadLocator.GetUserArg("--level-view") == "top");
    }

    /// <summary>A sector and the sectors across its lines, at their current heights, in Godot space.</summary>
    private static Aabb SectorBounds(Sector sector)
    {
        Aabb? box = null;
        foreach (Line line in sector.Lines)
        {
            foreach (Sector? s in new[] { line.FrontSector, line.BackSector })
            {
                if (s is null)
                    continue;
                foreach (Vertex v in new[] { line.V1, line.V2 })
                {
                    var b = new Aabb(LevelMesh.ToGodot(v.X, v.Y, s.FloorHeight / 65536f), new Vector3(0, (s.CeilingHeight - s.FloorHeight) / 65536f / LevelMesh.MapUnitsPerMetre, 0));
                    box = box is Aabb a ? a.Merge(b) : b;
                }
            }
        }
        return box ?? new Aabb();
    }

    /// <summary>
    /// How much of the player the game camera shows (T3.4; needs a real
    /// renderer, else null): the pixels that change when the player is
    /// hidden, against the pixels it covers with the level hidden, over a
    /// background in no palette colour (T3.8: over black, its black outline
    /// and dark texels were left out of the total). The camera and the world
    /// hold still and the overlay is hidden while the four frames render.
    /// </summary>
    public async Task<(int Visible, int Total)?> PlayerVisibilityAsync()
    {
        if (Player is null || PlayerMobj is null || DisplayServer.GetName() == "headless")
            return null;
        bool overlay = Overlay.Visible, hold = HoldCamera, paused = Paused;
        Overlay.Visible = false;
        HoldCamera = true;
        Paused = true;
        byte[] shown = await CaptureAsync();
        Player.Visible = false;
        byte[] hidden = await CaptureAsync();
        Color background = Environment.BackgroundColor;
        if (Playpal is not null)
        {
            (int r, int g, int b) = LevelCheck.UnusedColor(Playpal);
            Environment.BackgroundColor = Color.Color8((byte)r, (byte)g, (byte)b);
        }
        foreach (MeshInstance3D chunk in _chunks)
            chunk.Visible = false;
        byte[] empty = await CaptureAsync();
        Player.Visible = true;
        byte[] alone = await CaptureAsync();
        foreach (MeshInstance3D chunk in _chunks)
            chunk.Visible = true;
        Environment.BackgroundColor = background;
        Player.Visible = _showThings;
        Overlay.Visible = overlay;
        HoldCamera = hold;
        Paused = paused;
        int visible = 0, total = 0;
        for (int i = 0; i + 3 < shown.Length; i += 4)
        {
            if (shown[i] != hidden[i] || shown[i + 1] != hidden[i + 1] || shown[i + 2] != hidden[i + 2])
                visible++;
            if (alone[i] != empty[i] || alone[i + 1] != empty[i + 1] || alone[i + 2] != empty[i + 2])
                total++;
        }
        return (visible, total);
    }

    /// <summary>Renders a frame and reads it back as RGBA8 bytes.</summary>
    private async Task<byte[]> CaptureAsync()
    {
        for (int i = 0; i < 2; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image frame = GetViewport().GetTexture().GetImage();
        if (frame.GetFormat() != Image.Format.Rgba8)
            frame.Convert(Image.Format.Rgba8);
        return frame.GetData();
    }

    private async Task ScreenshotAsync(string path)
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("Level: --level-screenshot needs a real renderer (run without --headless)");
            GetTree().Quit(1);
            return;
        }
        for (int i = 0; i < 4; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(err == Error.Ok ? $"Level: saved {path}" : $"Level: could not save {path}: {err}");
        GetTree().Quit(err == Error.Ok ? 0 : 1);
    }

    private void Fail(string message)
    {
        GD.PrintErr($"Level: {message}");
        _message.Text = _status = message;
        if (IsCheckRun)
            GD.PrintErr("Level check: FAILED (no WAD loaded)");
        if (WadLocator.HasUserArg("--level-screenshot") || IsCheckRun)
            GetTree().Quit(1);
    }
}
