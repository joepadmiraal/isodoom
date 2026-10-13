using System;
using System.Collections.Generic;
using Godot;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T7.9: the automap (am_map.c, AmMap) in the game scene: its keys and the
// pad's (g_game.c G_Responder's AM_Responder, after the menus and the cheats),
// its tic (G_Ticker's AM_Ticker, between ST_Ticker and HU_Ticker), its drawing
// (D_Display's AM_Drawer, over the level and under the status bar, which
// shows with it whatever the HUD's mode, and the map title), and vanilla's
// stops: the console player's death (p_inter.c), a level's end, a new game, a
// load and a level loaded (g_game.c). Under the game camera only.
public partial class LevelScene : ICheatAutomap
{
    /// <summary>The automap (am_map.c), or null without a WAD.</summary>
    public AmMap? Automap { get; private set; }

    /// <summary>The automap's frame on screen (under the HUD).</summary>
    public AutomapView AutomapScreen { get; } = new() { Name = "Automap" };

    /// <summary>T7.9's style (<c>--level-automap</c>, <c>hud/automap</c>): see-through but the lines (not vanilla), or vanilla's black.</summary>
    public bool AutomapOverlay { get; set; }

    /// <summary>T7.9's detail (<c>--level-automap-detail</c>, <c>hud/automap_detail</c>): one window pixel per frame pixel (not vanilla), or the HUD's scale.</summary>
    public bool AutomapHighDetail { get; set; }

    /// <summary>
    /// T7.9c's turn (<c>--level-automap-turn</c>, <c>hud/automap_turn</c>):
    /// the automap turned to the game camera's view, its up the screen's up
    /// (not vanilla), or vanilla's north up.
    /// </summary>
    public bool AutomapTurned
    {
        get => _automapTurned;
        set
        {
            _automapTurned = value;
            Automap?.Rotation = AutomapRotation;
            _automapDirty = true;
        }
    }

    private bool _automapTurned = true;

    /// <summary>
    /// The automap's turn (BAM): the game camera looks along map angle
    /// 90° + <see cref="Render.IsoCamera.Yaw"/> (Godot's yaw 0 looks north),
    /// so the map turns by −yaw to put that direction up; 0 north up.
    /// </summary>
    public uint AutomapRotation => AutomapTurned ? unchecked((uint)(long)Math.Round(-Render.IsoCamera.Yaw / 360.0 * 4294967296.0)) : 0;

    /// <summary>Whether the automap shows: open, on a level, under the game camera.</summary>
    public bool AutomapActive => Automap is { automapactive: true } && GameState == gamestate_t.GS_LEVEL && IsoActive && PlayerMobj is not null;

    // The automap actions held (pressed and not let go: the responder gets a key down and a key up per press, as vanilla's).
    private readonly HashSet<string> _automapHeld = [];

    // The world (and level) the automap was opened on: another one (a level loaded) closes it.
    private Sim.World? _automapWorld;

    // The frame changed since it was shown (a key between the tics).
    private bool _automapDirty;

    // Whether the console player was alive after the last tic (its death closes the automap, p_inter.c P_KillMobj).
    private bool _automapPlayerAlive;

    /// <summary>The automap actions and the key code each sends <see cref="AmMap.AM_Responder"/> (its <c>key_map_*</c>).</summary>
    private static (string Action, int Key)[] AutomapKeys(AmMap am) =>
    [
        (GameInput.MapToggle, am.key_map_toggle), (GameInput.MapNorth, am.key_map_north), (GameInput.MapSouth, am.key_map_south),
        (GameInput.MapEast, am.key_map_east), (GameInput.MapWest, am.key_map_west), (GameInput.MapZoomIn, am.key_map_zoomin),
        (GameInput.MapZoomOut, am.key_map_zoomout), (GameInput.MapMaxZoom, am.key_map_maxzoom), (GameInput.MapFollow, am.key_map_follow),
        (GameInput.MapGrid, am.key_map_grid), (GameInput.MapMark, am.key_map_mark), (GameInput.MapClearMark, am.key_map_clearmark),
    ];

    /// <summary>Makes the automap for the WAD (its marks' numbers, <c>AMMNUM*</c>).</summary>
    private void InitAutomap()
    {
        Automap = new AmMap(Wad)
        {
            Rotation = AutomapRotation,
            Message = text => _menuMessage = (text, false), // plr->message: the next HU_Ticker shows it
            Entered = () => StatusBar?.st_firsttime = true, // ST_Responder's AM_MSGENTERED: the bar redraws whole
        };
    }

    /// <summary>
    /// T7.9: g_game.c <c>G_Responder</c>'s <c>AM_Responder</c> for an input
    /// event the menus did not take: on a level with a player, under the
    /// game camera. A key or pad input bound to an automap action
    /// (<see cref="Settings.AutomapActions"/>) goes as its <c>key_map_*</c>
    /// code, down when pressed and up when let go; every other key goes down
    /// as no automap key, its letter typed for <c>iddt</c>. Returns whether
    /// the automap took it (vanilla's <c>rc</c>: the game and the debug keys
    /// don't see it), or it was a key typed into <c>iddt</c> past its "id".
    /// </summary>
    public bool AutomapEvent(InputEvent e)
    {
        if (Automap is not { } am || _flow is not { gamestate: gamestate_t.GS_LEVEL } || World is not { } world
            || PlayerMobj is null || !IsoActive && !IsCheckRun)
            return false;
        if (e is not (InputEventKey or InputEventJoypadButton or InputEventJoypadMotion))
            return false;
        SyncAutomapWorld(world);
        bool taken = false, keyDown = false;
        int typedBefore = am.cheat_amap.p, cheating = am.cheating;
        int ch = e is InputEventKey { Pressed: true, Echo: false } k ? LetterKeyOf(k) : 0;
        foreach ((string action, int code) in AutomapKeys(am))
        {
            if (!InputMap.HasAction(action) || !InputMap.EventIsAction(e, action))
                continue; // (not an exact match: a stick's other direction lets this one go)
            bool pressed = e.IsActionPressed(action, allowEcho: true);
            bool held = _automapHeld.Contains(action);
            if (pressed && !held && !e.IsEcho())
            {
                _automapHeld.Add(action);
                taken |= am.AM_Responder(AmMap.evtype_t.ev_keydown, keyDown ? 0 : code, keyDown ? 0 : ch);
                keyDown = true;
            }
            else if (pressed && held)
                taken |= e is InputEventKey && am.automapactive && TakesHeld(am, code); // a key's repeats stay the automap's
            else if (!pressed && held)
            {
                _automapHeld.Remove(action);
                am.AM_Responder(AmMap.evtype_t.ev_keyup, code);
            }
        }
        if (!keyDown && e is InputEventKey { Pressed: true, Echo: false })
            am.AM_Responder(AmMap.evtype_t.ev_keydown, 0, ch); // any other key: the cheat reads it
        if (am.cheating != cheating)
            GD.Print($"Level: automap: iddt typed, step {am.cheating}");
        _automapDirty = true;
        return taken || typedBefore >= 2 && (am.cheat_amap.p > 2 || am.cheating != cheating);
    }

    // Whether a key held keeps being the automap's (its repeats): the pan keys while not following, the zoom keys.
    private static bool TakesHeld(AmMap am, int code) =>
        code == am.key_map_zoomin || code == am.key_map_zoomout
        || am.followplayer == 0 && (code == am.key_map_north || code == am.key_map_south || code == am.key_map_east || code == am.key_map_west);

    // The automap goes when the world or level it was opened on did (g_game.c: a new game, a load, a level's end, a reborn's reload).
    private void SyncAutomapWorld(Sim.World? world)
    {
        if (Automap is not { } am)
            return;
        if (am.automapactive && (world is null || !ReferenceEquals(world, _automapWorld) || world.level != _automapLevel
            || GameState != gamestate_t.GS_LEVEL))
            CloseAutomap();
        am.World = world;
        _automapWorld = world;
        _automapLevel = world?.level;
    }

    private Map.Level? _automapLevel;

    /// <summary>Closes the automap as g_game.c's <c>automapactive = false</c> does, its keys let go.</summary>
    public void CloseAutomap()
    {
        Automap?.Deactivate();
        _automapHeld.Clear();
        if (Automap is { } am)
        {
            am.m_paninc = default;
            am.mtof_zoommul = am.ftom_zoommul = Map.Fixed.FRACUNIT;
        }
    }

    /// <summary>
    /// g_game.c <c>G_Ticker</c>'s <c>AM_Ticker</c> (after <c>ST_Ticker</c>,
    /// before <c>HU_Ticker</c>; paused too), after p_inter.c's
    /// <c>AM_Stop</c> for the console player killed in the tic ("don't die in auto map").
    /// </summary>
    private void AutomapTicker()
    {
        if (Automap is not { } am || World is not { } world)
            return;
        SyncAutomapWorld(world);
        bool alive = world.players[world.consoleplayer] is { playerstate: playerstate_t.PST_LIVE, mo: not null };
        if (am.automapactive && _automapPlayerAlive && !alive)
        {
            am.AM_Stop();
            _automapHeld.Clear();
            am.m_paninc = default;
            am.mtof_zoommul = am.ftom_zoommul = Map.Fixed.FRACUNIT;
        }
        _automapPlayerAlive = alive;
        am.AM_Ticker();
        _automapDirty = true;
    }

    /// <summary>The frame's size for the window: the HUD's scale (or one window pixel, high detail) above the status bar's rows.</summary>
    private (int Width, int Height, int Scale, int PixelScale) AutomapFrame()
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int hud = Hud.FixedScale > 0 ? Hud.FixedScale : HudView.ScaleFor(size);
        int above = Math.Max((int)size.Y - StStuff.ST_HEIGHT * hud, 1);
        return AutomapHighDetail
            ? ((int)size.X, above, 1, hud)
            : (Math.Max((int)size.X / hud, 1), Math.Max(above / hud, 1), hud, 1);
    }

    /// <summary>
    /// d_main.c <c>D_Display</c>'s <c>AM_Drawer</c> (after each tic, and
    /// after a key between them): the frame for the window's size, drawn and
    /// shown under the status bar, in the level's palette; hidden when the
    /// automap is closed or off the game camera.
    /// </summary>
    private void UpdateAutomap()
    {
        if (Automap is not { } am)
            return;
        SyncAutomapWorld(World);
        am.Overlay = AutomapOverlay;
        bool shown = AutomapActive;
        Hud.AutomapShown = shown;
        if (!shown || Playpal is null)
        {
            AutomapScreen.Visible = false;
            return;
        }
        (int width, int height, int scale, int pixelScale) = AutomapFrame();
        if (am.f_w != width || am.f_h != height || am.PixelScale != pixelScale)
        {
            am.SetFrame(width, height, pixelScale);
            _automapDirty = true;
        }
        bool drawn = _automapDirty || !AutomapScreen.Visible;
        if (drawn)
        {
            am.AM_Drawer();
            MessageLine?.HU_DrawTitle();
            _automapDirty = false;
        }
        Vector2 size = GetViewport().GetVisibleRect().Size;
        float barTop = size.Y - StStuff.ST_HEIGHT * Hud.PixelScale;
        var at = new Vector2(MathF.Floor((size.X - am.f_w * scale) / 2), Math.Max(barTop - am.f_h * scale, 0));
        (int Row, int Palette, int Strength) palette = LevelScreenPalette;
        var key = (palette, at, scale, AutomapOverlay);
        if (!drawn && _automapShown == key)
            return; // the frame and its palette as shown
        _automapShown = key;
        AutomapScreen.Show(am.fb, ScreenPalette(palette.Row), at, scale, backdrop: !AutomapOverlay);
    }

    // What the automap's frame was last shown with (its palette, place, scale and style): unchanged, no upload.
    private ((int, int, int) Palette, Vector2 At, int Scale, bool Overlay)? _automapShown;

    /// <summary>
    /// The input the automap took from the game (vanilla's <c>rc</c>): while
    /// it pans (not following) the pan keys and the stick are its, so the
    /// turning keys and the aim stick, which share them, don't turn the player.
    /// </summary>
    private TiccmdInput AutomapFilter(TiccmdInput input) =>
        Automap is { automapactive: true, followplayer: 0 } && AutomapActive
            ? input with { TurnLeft = false, TurnRight = false, AimX = 0, AimY = 0 }
            : input;

    private static bool ParseAutomapStyle(string value, string name) => value switch
    {
        "overlay" => true,
        "opaque" or "vanilla" => false,
        _ => throw new ArgumentException($"{name}: \"{value}\" (opaque or overlay)"),
    };

    private static bool ParseAutomapTurn(string value, string name) => value switch
    {
        "camera" => true,
        "north" or "vanilla" => false,
        _ => throw new ArgumentException($"{name}: \"{value}\" (camera or north)"),
    };

    private static bool ParseAutomapDetail(string value, string name) => value switch
    {
        "high" => true,
        "low" or "vanilla" => false,
        _ => throw new ArgumentException($"{name}: \"{value}\" (low or high)"),
    };

    bool ICheatAutomap.AutomapShown
    {
        get
        {
            SyncAutomapWorld(World);
            return Automap is { automapactive: true } && GameState == gamestate_t.GS_LEVEL && PlayerMobj is not null && (IsoActive || IsCheckRun); // as AutomapEvent reads keys
        }
    }

    int ICheatAutomap.AutomapCheating => Automap?.cheating ?? 0;

    // T7.9b: the cheats page's iddt, read by the automap's responder only while it shows (as typed keys are)
    bool ICheatAutomap.TypeAutomapCheat(string keys)
    {
        if (Automap is not { } am || !((ICheatAutomap)this).AutomapShown || !am.TypeCheat(keys))
            return false;
        GD.Print($"Level: automap: iddt from the cheats page, step {am.cheating}");
        _automapDirty = true;
        return true;
    }

    /// <summary>The overlay's and the script's <c>automap</c> line.</summary>
    public string AutomapText() =>
        Automap is not { } am ? "automap: none"
            : am.StateText() + (AutomapOverlay ? ", overlay" : "") + (AutomapHighDetail ? ", high detail" : "") + (AutomapTurned ? "" : ", north up")
                + (am.automapactive && !AutomapActive ? " (game camera only)" : "");
}
