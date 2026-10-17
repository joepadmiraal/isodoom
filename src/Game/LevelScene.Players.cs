using System;
using System.Globalization;
using System.Linq;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T11.10: the presentation for several players without a network (until
// T11.8's lockstep loop): `--level-players=N` puts players 1–N in the game
// (a netgame; the others' commands idle), `--level-local=K` (or F12, or the
// level script's `local K`) picks the local player, d_net.c's consoleplayer,
// whose view, HUD, sounds and input everything follows (SPEC §12 T11.10).
public partial class LevelScene
{
    /// <summary>
    /// The players in a new game (<c>--level-players=N</c>, 1–4; default 1):
    /// players 1–N whose co-op start the first map has (player 1 always), a
    /// netgame when more than one. A debug harness (SPEC §12 T11.10): the
    /// others get empty commands each tic.
    /// </summary>
    public int PlayersInGame { get; set; } = 1;

    /// <summary>
    /// The local player for a new game (<c>--level-local=K</c>, 1–4, here 0–3;
    /// default player 1): the world's <see cref="World.consoleplayer"/> when it
    /// is in the game, else player 1. <see cref="SetLocalPlayer"/> changes it in a game.
    /// </summary>
    public int LocalPlayer { get; set; }

    /// <summary>
    /// T11.4: a new game's modes from the command line (<c>-deathmatch</c>,
    /// <c>-altdeath</c>, <c>-timer N</c>, <c>-avg</c>, <c>-respawn</c>,
    /// <c>-fast</c>, <c>-nomonsters</c>, vanilla's), for a game alone as for a
    /// netgame's host (<see cref="NetOptions.Modes"/>): with one player a
    /// deathmatch game runs, as vanilla's <c>-deathmatch</c> alone does.
    /// </summary>
    public NetOptions GameModes { get; set; } = new();

    private void ParsePlayerArgs()
    {
        if (WadLocator.GetUserArg("--level-players") is string players)
            PlayersInGame = int.TryParse(players, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= World.MAXPLAYERS
                ? n
                : throw new ArgumentException($"--level-players: \"{players}\" (1-{World.MAXPLAYERS})");
        if (WadLocator.GetUserArg("--level-local") is string local)
            LocalPlayer = int.TryParse(local, NumberStyles.None, CultureInfo.InvariantCulture, out int k) && k is >= 1 and <= World.MAXPLAYERS
                ? k - 1
                : throw new ArgumentException($"--level-local: \"{local}\" (1-{World.MAXPLAYERS})");
    }

    /// <summary>
    /// The players in a new game on <paramref name="level"/> (bit i: player
    /// i + 1): the first <see cref="PlayersInGame"/> whose co-op start the map
    /// has, player 1 always (T11.1a refuses a map without them in a real
    /// netgame; this debug harness leaves them out); in
    /// <paramref name="deathmatch"/> all of them (T11.4: they spawn at the
    /// deathmatch starts).
    /// </summary>
    public int PlayersMask(Level level, bool deathmatch = false)
    {
        int mask = 1;
        for (int i = 1; i < Math.Min(PlayersInGame, World.MAXPLAYERS); i++)
        {
            if (deathmatch || level.PlayerStart(i) is not null)
                mask |= 1 << i;
        }
        return mask;
    }

    /// <summary>
    /// Makes player <paramref name="player"/> (0–3, in the game) the local
    /// one: the world's <see cref="World.consoleplayer"/> (no sim state), and
    /// with it the camera (snapped to it), the player's own sprite, the
    /// cutaway, the light origin, the palette flashes, the HUD (the status bar
    /// started for it, its face background), the message filter, the sound
    /// listener, the aim marker, the tracers, the sight dim and monster
    /// markers (afresh) and the automap (started again when it shows). False
    /// when there is no world or the player is not in the game.
    /// </summary>
    public bool SetLocalPlayer(int player)
    {
        if (World is not { } world || player is < 0 or >= World.MAXPLAYERS || !world.playeringame[player])
            return false;
        LocalPlayer = player;
        if (world.consoleplayer == player)
            return true;
        world.consoleplayer = player;
        SightDims.Clear(); // T6.13t, T6.13u: the new player's sight, afresh
        _inSight.Clear();
        SeeFog();
        _hudMobj = null; // ST_Start, HU_Start for the new player
        StartHud();
        _redrawStatusBar = true;
        if (Automap is { automapactive: true } am)
            am.AM_Start(); // AM_initVariables: the new player's arrow to follow
        TiccmdBuilder.Reset();
        PresentWorld();
        SnapPending = true;
        return true;
    }

    /// <summary>F12: the next player in the game becomes the local one (<see cref="SetLocalPlayer"/>).</summary>
    public void CycleLocalPlayer()
    {
        if (World is not { } world)
            return;
        for (int k = 1; k <= World.MAXPLAYERS; k++)
        {
            int i = (world.consoleplayer + k) % World.MAXPLAYERS;
            if (world.playeringame[i])
            {
                SetLocalPlayer(i);
                return;
            }
        }
    }

    /// <summary>
    /// T11.10a (the user's decision, SPEC §12): in co-op, a ring in each
    /// teammate's colour around its feet (<see cref="Render.TeammateRings"/>,
    /// <c>--level-teammate-rings</c>, <c>sprites/teammate_rings</c>; default
    /// on). Never in single player or deathmatch, never under the local player
    /// (it has the aim marker), nor under a dead teammate or one the fog hides.
    /// </summary>
    public bool TeammateRinging { get; set; } = true;

    /// <summary>The teammate rings' node (T11.10a).</summary>
    public TeammateRings TeammateRings { get; } = new() { Name = StringNames.Get("TeammateRings") };

    /// <summary>The teammate rings under any camera (the level check's, which has no game camera).</summary>
    public bool RingsEverywhere { get; set; }

    private readonly TeammateRings.Ring[] _ringList = new TeammateRings.Ring[World.MAXPLAYERS];

    /// <summary>
    /// T11.10a: the teammate rings (<see cref="TeammateRinging"/>) at the drawn
    /// teammates under the game camera (or <see cref="RingsEverywhere"/>) in a
    /// co-op game: every live player in the game but the local one whose mobj
    /// is drawn and the fog shows, in its colour (<see cref="TeammateRings.ColourOf"/>).
    /// </summary>
    public void UpdateTeammateRings()
    {
        int n = 0;
        if (TeammateRinging && _showThings && (IsoActive || RingsEverywhere) && Mesh is not null && Playpal is { } palette
            && World is { netgame: true } world && world.settings.deathmatch == 0)
        {
            for (int i = 0; i < World.MAXPLAYERS; i++)
            {
                if (i == world.consoleplayer || !world.playeringame[i] || world.players[i] is not { playerstate: playerstate_t.PST_LIVE, mo: { } mo }
                    || !IsDrawn(mo) || !FogShows(mo))
                    continue;
                (Godot.Vector3 feet, _) = Interpolated(mo);
                _ringList[n++] = new TeammateRings.Ring(i, feet, TeammateRings.ColourOf(palette, i));
            }
        }
        TeammateRings.Set(_ringList.AsSpan(0, n));
    }

    /// <summary>The overlay's and the script's <c>players</c> line (T11.10): the players in the game, the local one and the fog's viewpoints.</summary>
    public string PlayersText()
    {
        if (World is not { } world)
            return "players: no world";
        var inGame = Enumerable.Range(0, World.MAXPLAYERS).Where(i => world.playeringame[i])
            .Select(i => $"{i + 1}{(world.players[i].playerstate == playerstate_t.PST_LIVE ? "" : " (dead)")}");
        string mode = !world.netgame ? "single player" : world.settings.deathmatch != 0 ? "deathmatch" : "co-op";
        return string.Create(CultureInfo.InvariantCulture,
            $"players: {string.Join(", ", inGame)} ({mode}), local {world.consoleplayer + 1}{(world.netgame && Net is null ? " (F12)" : "")}, fog from {FogViewpoints(world, _fogPoints)}, teammate rings {(TeammateRinging ? TeammateRings.Rings.Count.ToString(CultureInfo.InvariantCulture) : "off")}");
    }
}
