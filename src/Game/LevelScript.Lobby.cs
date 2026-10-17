using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace IsoDoom.Game;

// T11.9: the level script and the lobby page. `lobby [N [SECONDS]]` prints the
// lobby as the page shows it (with N, after waiting until N players are in).
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
            $"{p.Slot + 1} {MMenu.PlayerColours[p.Slot].ToLowerInvariant()}{(p.Local ? " (local)" : "")} {(p.RttUs < 0 ? "-" : (p.RttUs / 1000.0).ToString("F1", CultureInfo.InvariantCulture) + " ms")}{(p.Ready ? "" : " not ready")}"));
        GD.Print($"Level script: lobby: {(lobby.Host ? "host" : "join")} {lobby.Address}:{lobby.Port}{(lobby.FromMenus ? " (menus)" : "")}, {lobby.State}, "
            + $"{lobby.Players.Count} of {lobby.MaxPlayers} [{list}], {lobby.MapName ?? "-"} {lobby.Wad}{(lobby.Starting ? ", starting" : "")}{(lobby.InGame ? ", in game" : "")}"
            + $"{(lobby.StartRefusal is { } r ? $", cannot start: {r}" : "")}{(lobby.Failure is { } f ? $", ended: {f}" : "")}");
        bool ok = lobby.Failure is null && lobby.Players.Count >= players;
        if (!ok)
            GD.PrintErr($"Level script: lobby: {(lobby.Failure is null ? $"fewer than {players} player(s)" : "the session ended")}");
        return ok ? 0 : 1;
    }
}
