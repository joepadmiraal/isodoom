using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace IsoDoom.Game;

// T11.9: the level script and the lobby page. `lobby [N [SECONDS]]` prints the
// lobby as the page shows it (with N, after waiting until N players are in).
// T11.9d: `lan [N [SECONDS]] [join]` lists the LAN's games as Join Game does.
public partial class LevelScript
{
    /// <summary>
    /// <c>lobby [N [SECONDS]]</c>: waits (frames) until the session's lobby
    /// has <paramref name="players"/> players in or more (0: at once), then
    /// prints it: host or join, state, the players (slot, colour, ping), the
    /// game, the start refusal and the failure; fails without a session, when
    /// it ended or after <paramref name="seconds"/>.
    /// </summary>
    private async Task<int> Lobby(int players, int seconds)
    {
        INetMenuHost host = _scene;
        ulong deadline = Time.GetTicksMsec() + (ulong)seconds * 1000;
        while (host.Lobby is { Failure: null } l && l.Players.Count < players && Time.GetTicksMsec() < deadline)
            await Frames(1);
        if (host.Lobby is not { } lobby)
        {
            GD.PrintErr("Level script: lobby: no netgame session");
            return 1;
        }
        string list = string.Join(", ", lobby.Players.Select(p =>
            $"{p.Slot + 1} {p.Name} {MMenu.PlayerColours[p.Slot].ToLowerInvariant()}{(p.Local ? " (local)" : "")} {(p.RttUs < 0 ? "-" : (p.RttUs / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " ms")}{(p.Ready ? "" : " not ready")}"));
        GD.Print($"Level script: lobby: {(lobby.Host ? "host" : "join")} {lobby.Address}:{lobby.Port}{(lobby.FromMenus ? " (menus)" : "")}, {lobby.State}, "
            + $"{lobby.Players.Count} of {lobby.MaxPlayers} [{list}], {lobby.MapName ?? "-"} {lobby.Wad}{(lobby.Starting ? ", starting" : "")}{(lobby.InGame ? ", in game" : "")}"
            + $"{(lobby.StartRefusal is { } r ? $", cannot start: {r}" : "")}{(lobby.Failure is { } f ? $", ended: {f}" : "")}");
        bool ok = lobby.Failure is null && lobby.Players.Count >= players;
        if (!ok)
            GD.PrintErr($"Level script: lobby: {(lobby.Failure is null ? $"fewer than {players} player(s)" : "the session ended")}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// T11.9d: <c>lan [N [SECONDS]] [join]</c>: runs a LAN browser (Join
    /// Game's while the page shows, else one of its own) until it lists
    /// <paramref name="count"/> games (frames go on), prints each (name,
    /// address, IWAD, map, mode, players, phase, compatible or why not) and
    /// fails after <paramref name="seconds"/>; with <paramref name="join"/>,
    /// joins the first compatible one as the page's row would (the menus'
    /// session, its lobby page), failing without one.
    /// </summary>
    private async Task<int> Lan(int count, int seconds, bool join)
    {
        IsoDoom.Net.NetDiscoveryBrowser? own = null;
        INetMenuHost host = _scene;
        try
        {
            ulong deadline = Time.GetTicksMsec() + (ulong)seconds * 1000;
            IsoDoom.Net.NetDiscoveryBrowser? browser = _scene.Flow.Menu.LanBrowser;
            if (browser is null)
            {
                own = host.OpenBrowser();
                browser = own;
            }
            if (browser is null)
            {
                GD.PrintErr("Level script: lan: no LAN browser (no IWAD, or no socket)");
                return 1;
            }
            while (true)
            {
                if (own is not null)
                    own.Update(_scene.Flow.Menu.NetClockUs()); // Join Game's runs with the menus
                if (browser.Entries.Count >= count && (!join || browser.Entries.Any(e => e.Compatible)) || Time.GetTicksMsec() >= deadline)
                    break;
                await Frames(1);
                if (own is null && _scene.Flow.Menu.LanBrowser != browser)
                {
                    GD.PrintErr("Level script: lan: the Join Game page closed");
                    return 1;
                }
            }
            GD.Print($"Level script: lan: {browser.Entries.Count} game(s), {browser.QueriesSent} queries sent");
            foreach (IsoDoom.Net.NetDiscoveryEntry e in browser.Entries)
            {
                IsoDoom.Net.NetDiscoveryReply r = e.Reply;
                string mode = r.Deathmatch switch { 1 => "deathmatch", 2 => "altdeath", _ => "co-op" };
                GD.Print($"Level script: lan:   {r.HostName} {e.JoinAddress} {r.IwadName}{(r.PwadCount > 0 ? $" + {r.PwadCount} pwad(s)" : "")} {r.MapName} {mode} skill {r.Skill}, "
                    + $"{r.Players}/{r.MaxPlayers} players, {(r.Started ? "started" : "lobby")}, {(e.Compatible ? "compatible" : $"not compatible: {e.Reason}")}");
            }
            if (browser.Entries.Count < count)
            {
                GD.PrintErr($"Level script: lan: fewer than {count} game(s) after {seconds} s");
                return 1;
            }
            if (!join)
                return 0;
            if (browser.Entries.FirstOrDefault(e => e.Compatible) is not { } game)
            {
                GD.PrintErr("Level script: lan: no game to join");
                return 1;
            }
            GD.Print($"Level script: lan: joining {game.Reply.HostName} at {game.JoinAddress}");
            if (host.JoinGame(game.JoinAddress) is { } error)
            {
                GD.PrintErr($"Level script: lan: can't join: {error}");
                return 1;
            }
            _scene.Flow.Menu.ShowLobby();
            return 0;
        }
        finally
        {
            own?.Dispose();
        }
    }
}
