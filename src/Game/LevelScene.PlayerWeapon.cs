using System.Collections.Generic;
using System.Globalization;
using IsoDoom.Render;
using IsoDoom.Sim;

namespace IsoDoom.Game;

// T6.14d: every player's sprite holds its ready weapon (SPEC §12 "T6.14 Player
// weapons"; not vanilla, presentation only). A player's mobj in PLAY's frames
// A–G is drawn as the synthetic sprite of its player's readyweapon
// (SpriteAtlas.HeldSprite: the composites of T6.14c), same frame; death
// frames, a mobj without a player and the option off draw stock, as does a
// rotation whose lump has no pose (a PWAD's).
public partial class LevelScene
{
    /// <summary>
    /// T6.14d: whether the players' sprites hold their ready weapons
    /// (<c>--level-player-weapon</c>, <c>sprites/player_weapon</c>, the Q key;
    /// default on).
    /// </summary>
    public bool PlayerWeapon { get; set; } = true;

    /// <summary>Parses <c>--level-player-weapon</c>'s and <c>sprites/player_weapon</c>' value.</summary>
    public static bool ParsePlayerWeapon(string value, string what) => value switch
    {
        "on" => true,
        "off" or "vanilla" or "stock" => false,
        _ => throw new System.ArgumentException($"{what}: \"{value}\" (on or off)"),
    };

    /// <summary>
    /// T6.14d: <paramref name="e"/> (<paramref name="mo"/>'s entry, <see cref="ThingEntry(mobj_t)"/>)
    /// as the synthetic sprite of its player's ready weapon when <see cref="PlayerWeapon"/>
    /// is on, <paramref name="mo"/> has a player (a voodoo doll has its player's, as
    /// vanilla's <c>P_SpawnPlayer</c> gives it) and shows <c>PLAY</c>'s frames A–G, and the
    /// atlas has composites of that weapon; else <paramref name="e"/> as it is. The ready
    /// weapon only: during a change the old weapon shows until it is down, vanilla's order.
    /// </summary>
    public ThingSprites.Entry WithHeldWeapon(mobj_t mo, ThingSprites.Entry e)
    {
        if (!PlayerWeapon || mo.player is not { } p || e.Sprite != PlayerSprite.SprPlay || e.Frame >= SpriteAtlas.HeldFrames
            || SpriteAtlas is not { } atlas)
            return e;
        int sprite = atlas.HeldSprite(p.readyweapon);
        return sprite < 0 ? e : e with { Sprite = sprite };
    }

    /// <summary>T6.14d: what the overlay's player line ends with: <c>holding SHOTA0</c> (the held weapon's art), <c>holding fists</c>, or <c>stock</c>.</summary>
    public string HeldText(ThingSprites.Entry drawn)
    {
        if (SpriteAtlas?.HeldWeaponOf(drawn.Sprite) is not { } weapon)
            return "stock";
        return SpriteAtlas.Held?.Weapons[(int)weapon] is { } art ? $"holding {art.Source}" : "holding fists";
    }

    /// <summary>
    /// T6.14d: the level script's <c>playerweapon</c>: each player in the game, its drawn
    /// sprite, frame, rotation slot and atlas slot, and what it holds. A line fails when
    /// the option is on, the player is alive in frames A–G and is drawn stock in a rotation
    /// whose lump has a composite of its ready weapon.
    /// </summary>
    public IEnumerable<(string Line, bool Fails)> PlayerWeaponLines()
    {
        if (World is not { } world || SpriteAtlas is not { } atlas)
        {
            yield return ("player weapon: no world", true);
            yield break;
        }
        yield return ($"player weapon: {(PlayerWeapon ? "on" : "off")}, {atlas.Held?.Count ?? 0} composites", false);
        IReadOnlyList<mobj_t> drawn = DrawnMobjs;
        for (int i = 0; i < World.MAXPLAYERS; i++)
        {
            if (!world.playeringame[i] || world.players[i] is not { mo: { } mo } p)
                continue;
            ThingSprites.Entry? entry = null;
            ThingSprites.Shown shown = default;
            if (mo == PlayerMobj && Player is { } own)
                (entry, shown) = (own.Entry, own.Sprite.ShownFrames[0]);
            else if (Things is { } things)
            {
                for (int k = 0; k < drawn.Count && k < things.Entries.Count; k++)
                {
                    if (drawn[k] == mo)
                        (entry, shown) = (things.Entries[k], things.ShownFrames[k]);
                }
            }
            entry ??= WithHeldWeapon(mo, ThingEntry(mo)); // (not drawn: under the fog, or the check's run without the player's billboard)
            ThingSprites.Entry e = entry.Value;
            string sprite = e.Sprite >= 0 && e.Sprite < atlas.Sprites.NumSprites ? atlas.Sprites.SpriteDefs[e.Sprite].Name : "none";
            int frame = mo.frame & Info.FF_FRAMEMASK;
            bool live = p.playerstate == playerstate_t.PST_LIVE && mo.sprite == spritenum_t.SPR_PLAY && frame < SpriteAtlas.HeldFrames;
            // The lump PLAY shows in that rotation slot, and whether its weapon has a composite of it.
            string lump = "";
            bool composite = false;
            if (atlas.Sprites.Find("PLAY") is { } play && frame < play.NumFrames && Wad is { } wad)
            {
                int l = play.Frames[frame].Lump[shown.Rot];
                lump = l >= 0 ? wad.Lumps[l].Name : "";
                composite = atlas.Held?.Get(p.readyweapon, lump) is not null;
            }
            bool fails = PlayerWeapon && live && composite && atlas.HeldWeaponOf(e.Sprite) is null;
            yield return (string.Create(CultureInfo.InvariantCulture,
                $"player {i + 1}: {p.readyweapon} {(live ? "live" : p.playerstate == playerstate_t.PST_LIVE ? "live (not A-G)" : "dead")}, drawn {sprite} {(char)('A' + e.Frame)} rotation {shown.Rot + 1}{(shown.Flip ? " mirrored" : "")} slot {shown.Slot} ({lump}), {HeldText(e)}{(fails ? ": drawn stock, expected its weapon" : "")}"), fails);
        }
    }
}
