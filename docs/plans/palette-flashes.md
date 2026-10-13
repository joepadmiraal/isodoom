# Plan: gentler palette flashes (strength, and a flash around the player)

From a player's comment:

> It would be cool to be able to turn down UI flashing from picking bonuses/receiving damage etc.
> Or even turn it off completely. In third person perspective it dazzles you way more than in FPS.
> You can reduce it to small flash around character or something.

**What exists (T6.8, T7.3):** Options → More Options → Gameplay → PALETTE FLASHES (`gameplay/palette_effects`,
`--level-palette-effects=on|off`) turns them off completely. It is all or nothing, and `off` also drops the
invulnerability and light amplification colormaps. The player cannot turn the flashes down, and cannot keep
the information ("I was hit", "I picked something up") without tinting the whole screen.

**Why it dazzles more here:** vanilla's view is first person with the status bar, and a flash tints a view that
is mostly near walls. The iso view shows a whole room of bright floor at once, and the HUD and the void tint
too (T6.8), so a whole 1280×800 window switches palette. Picking up a row of clips or bonuses gives gold
flashes back to back.

**Follow CLAUDE.md's session workflow: one task.** First add the task below to `docs/TASKS.md` under M6,
after T6.13u, in the style of the other user requests (T6.13j, T6.13k, T6.13r): a `- [x]`/`- [ ]` line with
the problem, a **Do:** paragraph and a *Done when:* line taken from this file. Then do it, tick it with a
*Done (date):* paragraph, log the decisions in SPEC §12 and update §7.4's palette flash line, and update
CLAUDE.md's (AGENTS.md's: CLAUDE.md is a symlink) `--level-palette-effects` entry and Options list.

Presentation only. Nothing in `src/Sim/` changes: `damagecount`, `bonuscount` and the powers stay vanilla's,
so the sim's checksums, saves, routes and demo sync must not move. `StStuff.ST_doPaletteStuff` stays the
vanilla port it is: the HUD and route tests compare it with vanilla's. If a sim or route test changes,
something went wrong.

---

## T6.13v Gentler palette flashes (user request)

### Settings

Two settings on the Gameplay page, next to each other:

| Key | Menu label | Values | Default | Argument |
|---|---|---|---|---|
| `gameplay/palette_effects` | PALETTE FLASHES | `screen`, `player`, `off` (`on` accepted as `screen`, as the monster markers' `on`) | `screen` | `--level-palette-effects` |
| `gameplay/flash_strength` | FLASH STRENGTH | `10`–`100`, steps of 10 (percent) | `100` | `--level-flash-strength` |

- `screen` at `100` is today's `on`: vanilla, pixel for pixel. The level check's T6.8 pixel comparisons keep
  running on it.
- `screen` below `100`: the whole view flashes as now, but weaker (below).
- `player`: the view never changes palette; the flash shows on and around the player only (below). The
  strength scales it too.
- `off`: as today (no flashes, no power-up colormaps). This stays the photosensitivity switch.
- The invulnerability and light amplification colormaps (`fixedcolormap`) are not flashes: they last the
  whole power and tell the player it is active. They stay with `screen` and `player`, unchanged by the
  strength (the user's decision).
- Settings files saved with `on` read as `screen` (the default, so it is not written back). Nothing to
  migrate: only non-default values are saved, and `off` keeps its meaning.
- A debug key is optional; if added, follow the other cycling keys and list it in F1 and CLAUDE.md. The
  overlay gets a `palette flashes: …` line when not `screen 100`, as the monster markers do.

### Strength: blend, not fewer palettes

Two ways to weaken a flash:

1. Scale the counts (`damagecount × strength`) before `ST_doPaletteStuff` picks one of the 8 red / 4 gold
   palettes. Stays exact PLAYPAL colours, but in coarse steps (gold has only 4), and the radiation suit's
   single green palette (13) cannot be scaled at all.
2. **Blend** (recommended): the shaders fetch the texel's colour from palette 0 and from the flash palette and
   mix them by `palette_mix` = strength / 100. Smooth at any strength, the suit and the berserk fade included,
   and exactly vanilla's at 100 (mix 1.0 returns the flash palette's colour; do it as
   `palette_mix >= 1.0 ? flash : mix(normal, flash, palette_mix)` or check the float result bit for bit in the
   level check).

Recommend 2. It departs from T6.8's "every texel an exact PLAYPAL colour" only below 100%, which is an opt-in
deviation; log that in §12.

Where it goes:

- `shaders/level.gdshaderinc` (`ALBEDO = … texelFetch(playpal, ivec2(mapped, palette_index), 0)`, ~line 668)
  and `shaders/sprite.gdshaderinc` (~line 548): a second fetch from row 0 and the mix, behind a
  `palette_mix` uniform (default 1.0). Keep the cost to one extra `texelFetch`, and skip it when
  `palette_index == 0`.
- `shaders/sprite.gdshaderinc`'s fuzz `nearest_index` searches the current palette (~line 406): with a mix
  below 1 it should search palette 0 and let the final fetch blend, or the fuzz picks indices from the
  flash palette. Check the fuzz pixels in the level check at 50%.
- `shaders/palette.gdshader` (the HUD, intermission and menus' palette pass, `palette_index`): same mix.
- `LevelMesh.SetPalette` (`src/Render/LevelMesh.cs` ~777) and `IndexedTextures` (~66) set `palette_index`
  on every material; add `SetPaletteMix` next to them, set only when it changes.
- The void: `LevelScene.UpdatePaletteEffects` sets `Environment.BackgroundColor` to the palette's colour 0;
  lerp it from the normal background by the same mix.
- Blob shadows' `shadow_colour` (T6.8 tints them): the same lerp.

### `player`: a flash around the player

The view stays on palette 0 (`mesh.SetPalette(0)`, the HUD and void too). The flash shows where the player
already looks: at Doomguy.

- **The player's own sprite** drawn through the flash palette, at the strength's mix. The sprite shader
  already has `palette_index`; make it a per-instance value (or a second uniform the player's billboard
  alone reads, as the player light does with `SpriteSettings.PlayerLight`) so only the player's billboard
  changes. Find how the player's billboard is told apart today (player light, `--level-player-light`) and do
  the same.
- **A glow on the floor around the feet**: a soft disc, radius about 40 units (a little wider than the aim
  marker's ring), additive or alpha-blended, its colour the flash's tint and its alpha the flash's level ×
  strength. Build it as `src/Render/AimMarker.cs` + `shaders/aim_marker.gdshader` build theirs (a quad on
  the floor under the player's interpolated foot, under the fog as its sector); a new `FlashGlow.cs` and
  `flash_glow.gdshader`, or a mode of the aim marker's mesh if that stays simpler.
- **The tint and level**: from the palette `ST_doPaletteStuff` picks (unchanged), mapped by a small
  presentation helper (in `LevelScene` or a new plain-C# `PaletteFlash` class the tests can link):
  red 1–8 → red at level n/8; gold 9–12 → gold at level (n−8)/4; 13 → green at a low constant level.
  Take the colours from PLAYPAL (e.g. the shift of palette n's white, index 4, from palette 0's), so a
  PWAD's PLAYPAL changes them as it would vanilla's flashes.
- Under the game camera only (as the aim marker); the overview and free-fly cameras fall back to `screen`
  at the strength, so debugging still shows the flashes.

### What does not change

- `StStuff.ST_doPaletteStuff` and the HUD's own dump comparison (`VanillaRouteTests`' `powers` column).
- The status bar's face (it shows pain and the evil grin by itself, a non-flashing cue in both styles).
- `fixedcolormap` handling (`mesh.SetColormapOverride`) with `screen` and `player`.
- `--level-tweaks=vanilla` does not touch these: they are presentation settings like the others.

### Checks

- **Unit tests** (plain C#): the `player` style's tint and level for each palette 0–13 and for a PLAYPAL
  with shifted colours; the setting's parse (`on` → `screen`, `10`–`100`, out-of-range rejected with the
  argument's message, as the others).
- **Level check** (`LevelCheck.Sprites.cs`' `CheckPaletteEffects`, which already flips `PaletteEffects`):
  - headless: for `screen`/`player`/`off` × strength 100/50, the mesh's palette, mix, colormap override,
    the void's colour and the glow's visibility and colour after a `damagecount` and a `bonuscount`
    (the script's `flash damage|bonus COUNT`);
  - with a renderer: `screen` at 100 as today (every pixel the flash palette's); `screen` at 50, every
    compared pixel the mix of the two palettes' colours (within the float tolerance the light comparisons
    use); `player` at 100, the level's pixels palette 0's, the player's billboard's the flash palette's,
    and glow pixels around its foot in the tint; the fuzz at 50%.
  - the settings part (T7.3's): both settings at their defaults, changed through the menu, saved and read
    back; `on` in a file reads as `screen`.
- **By hand:** E1M1, pick up the clips and the armor in the start room and take damage from the zombies
  with each style at 100, 50 and 30; screenshot (`flash damage 40; shot FILE.png` and `flash bonus 6`).

*Done when:* the flashes can be weakened from the Gameplay page and shown around the player only instead of
over the whole screen, `screen` at `100` still draws exactly as vanilla (the T6.8 pixel checks pass
unchanged), `off` still turns them off, the settings save and load, the sim's checksums and every route and
demo test are unchanged, and all tests pass.

### Decided by the user (2026-10-13)

1. **Default:** stays `screen` at `100` (vanilla). The gentler looks are opt-in.
2. **Invulnerability's colormap** stays as it is with `screen` and `player`; only `off` drops it.
