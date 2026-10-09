using System;
using System.Diagnostics;
using System.Globalization;
using IsoDoom.Map;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T6.13l: the fog of war (SPEC §7, §12) in the game scene: the sight walk
// after each tic (FogOfWar, which maps vanilla's ML_MAPPED on the level's
// lines), the sectors' states uploaded to the level shader, and the things
// out of sight left out. Under the game camera only.
public partial class LevelScene
{
    /// <summary>
    /// How the sectors the player has not seen are drawn (<c>--level-fog</c>,
    /// <c>gameplay/fog</c>, key N; not vanilla, SPEC §12 T6.13l): hidden by
    /// default, or dimmed; with <see cref="FogStyle.Off"/> everything is
    /// drawn, and so are the things out of sight.
    /// </summary>
    public FogStyle FogStyle { get; set; } = FogStyle.Hide;

    /// <summary>The level's fog of war (its discovered sectors and mapped lines), or null without a world.</summary>
    public FogOfWar? Fog { get; private set; }

    /// <summary>The last sight walk's cost, in milliseconds.</summary>
    public double FogMilliseconds { get; private set; }

    /// <summary>Whether the fog applies now: a style other than off, under the game camera (or <see cref="FogEverywhere"/>), once the player has looked.</summary>
    public bool FogActive => FogStyle != FogStyle.Off && (IsoActive || FogEverywhere) && Fog is { HasSeen: true };

    /// <summary>The fog under any camera (the level check's, which has no game camera).</summary>
    public bool FogEverywhere { get; set; }

    // The level's fog: the discovered set from its mapped lines (a loaded game's), then the player's first look.
    private void StartFog(Level level)
    {
        Fog = World is null ? null : new FogOfWar(level);
        SeeFog();
    }

    // The sight walk from the console player, after each tic; a dead player keeps its last set.
    private void SeeFog()
    {
        if (Fog is not { } fog || World is not { } world || world.players[world.consoleplayer] is not { playerstate: playerstate_t.PST_LIVE, mo: { } mo })
            return;
        long start = Stopwatch.GetTimestamp();
        fog.See(mo.x, mo.y);
        FogMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>The fog's style and sectors' states in the level shader, as <see cref="FogActive"/> says (every frame).</summary>
    public void UpdateFog()
    {
        if (Mesh is not { } mesh)
            return;
        FogStyle style = FogActive ? FogStyle : FogStyle.Off;
        if (mesh.FogMode != style)
            mesh.SetFog(style);
        mesh.UpdateFog(Fog);
    }

    /// <summary>
    /// Whether the fog lets <paramref name="mo"/> be drawn: everything while
    /// it is not active; else a thing that acts or moves (<see cref="FogActs"/>)
    /// only while its sector is visible, any other once its sector is discovered.
    /// </summary>
    public bool FogShows(mobj_t mo)
    {
        if (!FogActive)
            return true;
        int sector = mo.subsector.sector.Index;
        return FogActs(mo) ? Fog!.IsVisible(sector) : Fog!.IsDiscovered(sector);
    }

    /// <summary>
    /// Whether the fog hides <paramref name="mo"/> as soon as it is out of
    /// sight: monsters, barrels, missiles (every <c>MF_SHOOTABLE</c>,
    /// <c>MF_COUNTKILL</c> or <c>MF_MISSILE</c> thing), corpses (which may be
    /// raised), and what marks an event (puffs, blood, teleport and item fog);
    /// not items, keys and decorations, which stay where they were seen.
    /// </summary>
    public static bool FogActs(mobj_t mo) =>
        (mo.flags & (mobjflag_t.MF_SHOOTABLE | mobjflag_t.MF_COUNTKILL | mobjflag_t.MF_MISSILE | mobjflag_t.MF_CORPSE)) != 0
        || mo.type is mobjtype_t.MT_PUFF or mobjtype_t.MT_BLOOD or mobjtype_t.MT_TFOG or mobjtype_t.MT_IFOG;

    /// <summary>The overlay's and the script's <c>fog</c> line: the style, the sector counts and the walk's cost.</summary>
    public string FogText()
    {
        string style = FogStyle.ToString().ToLowerInvariant();
        if (FogStyle == FogStyle.Dim && Mesh is { } mesh && mesh.FogDimLook != FogDim.Default)
            style += $" ({mesh.FogDimLook})";
        if (Fog is not { } fog)
            return $"fog: {style}, no world";
        return string.Create(CultureInfo.InvariantCulture,
            $"fog: {style}{(FogStyle != FogStyle.Off && !IsoActive ? " (game camera only)" : "")} (N), {fog.VisibleCount} visible, {fog.DiscoveredCount} discovered of {fog.Level.Sectors.Length} sectors, walk {FogMilliseconds:F3} ms");
    }

    private static FogStyle ParseFog(string value, string name) => value switch
    {
        "dim" => FogStyle.Dim,
        "hide" => FogStyle.Hide,
        "off" or "vanilla" => FogStyle.Off,
        _ => throw new ArgumentException($"{name}: \"{value}\" (hide, dim or off)"),
    };

    /// <summary>Parses a sector's state for the level script's <c>seen</c>.</summary>
    public static SectorSight ParseSectorSight(string value) => value switch
    {
        "unseen" => SectorSight.Unseen,
        "discovered" => SectorSight.Discovered,
        "visible" => SectorSight.Visible,
        _ => throw new ArgumentException($"seen: \"{value}\" (unseen, discovered or visible)"),
    };
}
