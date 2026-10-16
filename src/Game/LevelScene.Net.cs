using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using IsoDoom.Net;
using IsoDoom.Sim;

namespace IsoDoom.Game;

/// <summary>
/// T11.6: the netgame's session from the command line (<see cref="NetOptions"/>:
/// <c>-host</c>/<c>-server</c>, <c>-connect</c>, <c>-players</c>,
/// <c>-netdelay</c>, …) over ENet (<see cref="NetEnetTransport"/>), polled
/// once a frame on the main thread at the start of <see cref="_Process"/>
/// (<see cref="PollNet"/>) and ended with the scene (<see cref="StopNet"/>). It gathers the
/// players and starts the game's protocol; the game loop does not run its
/// tics yet (T11.8): the level script's <c>netexchange TICS</c> exchanges
/// commands without running them (<see cref="NetExchange"/>), <c>net</c>
/// prints the session's state.
/// </summary>
public partial class LevelScene
{
    private IReadOnlyList<string> _pwadPaths = [];

    /// <summary>The netgame's session, or null in single player.</summary>
    public NetSession? Net { get; private set; }

    /// <summary>The exchange the level script runs, if any.</summary>
    public NetExchange? NetExchange { get; private set; }

    /// <summary>
    /// Starts the session the arguments ask for (none: nothing). On a bad
    /// argument or a port that cannot be listened on, fails the scene (and a
    /// headless or scripted run quits with 1) and returns false.
    /// </summary>
    private bool StartNet()
    {
        try
        {
            if (NetOptions.Parse(OS.GetCmdlineUserArgs()) is not { } options)
                return true;
            if (IsCheckRun || _iwadPath is null)
                return true; // the checks are single player
            string? map = WadLocator.GetUserArg("--level") is string m && !m.StartsWith('-') ? m : null;
            var spawn = new SpawnSettings(GameMode, Skill, nomonsters: NoMonsters, gameversion: GameVersion, gamemission: GameMission);
            NetGameSettings settings = NetSession.GameSettings(options, spawn, Tweaks, map ?? DefaultMap());
            NetIdentity identity = NetSession.Identity(Main.Version, _iwadPath, _pwadPaths);
            GD.Print($"Net: {options.Role}, build {identity.Build}, IWAD {identity.IwadMd5}" + (identity.PwadMd5s.Count > 0 ? $", PWADs {string.Join(' ', identity.PwadMd5s)}" : ""));
            Net = new NetSession(options, identity, settings,
                use => new NetEnetTransport(options.Players) { BindAddress = use == NetTransportUse.Server ? options.BindAddress : "*" },
                line => GD.Print($"Net: {line}"));
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            Fail($"net: {e.Message}");
            if (DisplayServer.GetName() == "headless" || WadLocator.HasUserArg("--level-script"))
                GetTree().Quit(1);
            return false;
        }
    }

    /// <summary>One frame of the netgame: the session polled at this machine's clock, and the script's exchange's frame.</summary>
    private void PollNet()
    {
        if (Net is not { } net)
            return;
        net.Update((long)Time.GetTicksUsec());
        NetExchange?.Frame();
    }

    /// <summary>The level script's <c>netexchange TICS</c>: starts an exchange of TICS tics (whole windows) through this machine's client.</summary>
    public NetExchange StartNetExchange(int tics)
    {
        if (Net is not { } net)
            throw new ArgumentException("netexchange: no netgame (-host or -connect)");
        return NetExchange = new NetExchange(net.Client, tics);
    }

    /// <summary>Ends the session: the client quits, the transports close (the host's disconnects reach the others).</summary>
    public void StopNet()
    {
        NetExchange = null;
        Net?.Dispose();
        Net = null;
    }

    /// <summary>The level script's <c>net</c>: the session's state in a line.</summary>
    public string NetText()
    {
        if (Net is not { } net)
            return "no netgame";
        NetClient c = net.Client;
        var s = new System.Text.StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"{net.Options.Role}, {c.State}, slot {c.Slot}");
        if (c.Start is { } start)
            s.Append(CultureInfo.InvariantCulture, $", players {start.PlayerMask:x}, delay {start.Delay}, start tic {start.StartTic}");
        s.Append(CultureInfo.InvariantCulture, $", ping {(c.RttUs < 0 ? "-" : (c.RttUs / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " ms")}");
        s.Append(CultureInfo.InvariantCulture, $", tics ready {c.TicsReady}, bundles to {c.BundleTic}, ran {c.LastRanTic}");
        if (net.Server is { } server)
            s.Append(CultureInfo.InvariantCulture, $"; server {server.Phase}, players {server.PlayerCount}, windows compared {server.WindowsCompared}");
        if ((net.Server?.Desync ?? c.Desync) is { } desync)
            s.Append(CultureInfo.InvariantCulture, $", DESYNC at window {desync.Window} (tics {desync.FirstTic}-{desync.LastTic})");
        if (NetExchange is { } x)
            s.Append(CultureInfo.InvariantCulture, $"; exchange {x.TicsTaken}/{x.Tics} tics, hash {x.Hash:x16}");
        if (net.Failure is { } failure)
            s.Append(CultureInfo.InvariantCulture, $"; {failure}");
        return s.ToString();
    }
}
