# Plan: monsters out of sight drawn darker

From the height-readability discussion (`docs/height-readability-options.md`; a player's comment:
"it becomes difficult to tell what is above you, below you, or merely waiting for you with a
fireball"). In vanilla, the first-person view answers "can it hit me?" on its own. The view is
from the player's eyes, and a monster attacks only once `P_CheckSight` (p_sight.c) gives it a line
of sight, which is close to "the player can see it". The iso camera floats above the player. It
sees over walls, ledges and pillars that block sight in the sim, so monsters that cannot attack
look just like the ones that can.

This plan uses vanilla's own rule to separate them. **A live monster that has no line of sight to
the player (`P_CheckSight(monster, player)` false) is drawn darker.** It's a presentation option,
for the user to judge the look before anything goes to `main`.

**Follow CLAUDE.md's session workflow: one task.** Work on the branch `sight-dim` (already created
from `main`; do not merge it, the user decides after seeing it). First add the task below to
`docs/TASKS.md` under M6, after T6.13s, in the style of the other user requests (T6.13j, T6.13k,
T6.13p, T6.13r): a `- [ ]` line with the problem, a **Do:** paragraph and a *Done when:* line,
taken from this file. Then do it, tick it with a *Done (date):* paragraph, and log the decisions in
SPEC §12. Also commit `docs/height-readability-options.md` (untracked in the working tree) with it:
the task refers to it.

Presentation only. Nothing in `src/Sim/` changes: the sim's checksums, saves, routes and demo sync
must not move. If a sim test changes, something went wrong.

---

## T6.13t Monsters out of sight drawn darker

### Rule

- **Which things:** a mobj that is drawn (`IsDrawn`, `FogShows`), is not the console player's,
  is alive (`health > 0`), is `MF_SHOOTABLE`, and has a `info.seestate` other than `S_NULL`
  (i.e. it chases: monsters, lost souls; not barrels, which have no see state, and not corpses).
  Everything else is drawn as now: items, decorations, barrels, missiles, corpses, puffs.
- **The test:** `World.P_CheckSight(mo, me)` with the monster as `t1` and the player's mobj as
  `t2`, which is the direction that governs the monster's attacks (`A_Look`, `A_Chase`'s
  `P_CheckMissileRange`). Sight isn't symmetric (each end's eye height differs), so don't swap
  them. REJECT answers most of them at once.
- **When:** once per tic, right after `SeeFog()` in `LevelScene`'s tic (`src/Game/LevelScene.cs`,
  "the sight walk after the tic"), and wherever `SeeFog()` is also called (level start, load,
  script `place`). Never per frame. A dead player keeps the last result, as the fog does.
- **Calling the sim's `P_CheckSight` between tics** is safe, but write down why in the SPEC §12
  entry. It writes `validcount` (only ever compared for equality, so an extra increment changes no
  outcome), `sightcounts` (statistics), and `sightzstart`/`topslope`/`bottomslope`/`strace`/`t2x`/
  `t2y` (set afresh at the start of each check). None of these is in `World.Checksum` or the save.
  Prove it: the route, demo-sync and save tests must stay unchanged. Under lockstep co-op each
  peer's `validcount` would drift apart, which is harmless for the same reason. Note that too.
- **Only under the game camera** (`IsoActive`, as the fog), not in the overview or free-fly
  debug cameras.
- **Not touched:** sounds, the aim assist, the item beams (actors still count as actors there),
  tracers. With `--level-fog-things=sight`, monsters out of sector sight aren't drawn at all, as
  now. The rest are dimmed by this rule, which is per thing and 3D, so it also catches a monster
  behind a pillar in a visible sector, or on a ledge whose lip hides it. T6.13l's log lists
  "per-thing sight" as not done, so this fills that gap.

### Fade (no flicker)

Keep the state per mobj in a small plain-C# class in `src/Render/` (no Godot types, so
`IsoDoom.Tests` can test it; e.g. `SightDim`), keyed by the mobj's reference and cleared on level
load:

- **Coming into sight:** undimmed at once, the same tic. A threat must show immediately.
- **Leaving sight:** wait `SightGraceTics` = 4 tics (walking past a doorframe or pillar doesn't
  blink), then step the dim up 2 colormap rows a tic until it reaches the setting's amount.
- A monster that becomes drawn for the first time (spawned, or let through by the fog) starts at
  its target value with no fade.
- Mobjs gone from the world drop out of the table, so nothing grows over a long level.

### Look: darker

- **Shader:** in `shaders/sprite.gdshaderinc`, the colormap row the sprite is drawn through
  (`v_colormap`: the light from the sector, distance and `extralight`, or 0 for full-bright frames)
  is pushed down by the instance's dim rows: `min(row + dim, SIGHT_DIM_MAX_ROW)` with
  `SIGHT_DIM_MAX_ROW` = 29, so a dimmed monster in a dark room stays faintly visible instead of
  going black. Full-bright frames dim too: an imp out of sight throwing a fireball reads as out of
  sight. A fixed colormap (invulnerability, light amplification) still wins, as it does now. The
  outline goes through the same row (black stays black). Leave the fuzz pass (`#ifdef FUZZ`, the
  spectres') alone: it is already dark.
- **Per-instance data:** the custom data's 4 channels are all in use (slot, flags, sector,
  radius; see the `ThingSprites` summary). Put the dim rows in the flags above the existing bits:
  `flags | (dim << 5)` (0–31 needs 5 bits), decoded in the vertex shader next to `v_own`, and
  passed to the fragment shader. Add an `Entry` field (`int SightDim = 0`) and document it in the
  summary's flags list. Check that the fuzz shader variants and `sprite_hidden` still decode their
  flags correctly with the extra bits (they test with `&`, so they should).
- **Amount:** a setting in colormap rows, default **10**. Before settling, take screenshots of
  6, 10 and 14 in a bright room and a dark one and show them to the user (see *Screenshots*).

### Option

As the other presentation options (copy the shape of `gameplay/item_beams` / `ItemBeamMode`):

- `--level-sight-dim=ROWS|off` (`off` = vanilla: everything drawn as now), setting
  `sprites/sight_dim` on the Sprites page (`SIGHT DIM`, values `off`, `6`, `10`, `14`; default
  `10`). Check that the page still fits the screen. T6.13p and T6.13r moved items off full pages:
  if it doesn't fit, put it on Gameplay or Walls and say so.
- A debug key cycling 10 → 14 → off → 6 → 10. `Y` looks free in the level scene's key handling
  (`case Key.` in `LevelScene.cs`). Make sure it doesn't clash with the menus' y/n (they only
  take keys while a menu is open), and list it in F1's controls.
- Overlay: a `sight:` line when not at the default, with the dimmed and checked counts and the
  cost (as the fog's `walk … ms`).
- Level script: `sight` waits for the queued tics and prints each candidate monster (type,
  position, sector, in sight or not, current dim rows).

### Checks

- **Unit tests** (`tests/IsoDoom.Tests/Render/`, next to `FogOfWarTests`): the fade state machine.
  Undim at once, grace then steps, cap at the amount, first appearance without fade, removal,
  level reset, option off returns 0.
- **Level check** (`--level-check`, a `LevelCheck.SightDim.cs` partial, as `LevelCheck.Fog.cs`),
  on every map, headless:
  - Each drawn candidate's dim target equals `P_CheckSight(mo, me) ? 0 : amount`.
  - Non-candidates (barrels, items, corpses, the player) are never dimmed.
  - The custom data carries the rows. Use `ThingSprites.CustomData(i)`: the headless renderer
    keeps no instance data.
  - With the option off, nothing is dimmed.
  - On one map with a known case, spawn an imp in sight and one behind a wall (or use one the map
    has). Check the first undimmed and the second dimmed. Then `place` the player where it sees
    the second, and check that it's undimmed the same tic.
- **With a real renderer** (the same map), compare the dimmed imp's pixels with the CPU's
  colormap at the shifted row, as the player's minimum light check does (`CheckPlayerLight` in
  `LevelCheck.Sprites.cs`). If that's out of reach this session, add it as a follow-up task
  instead; the headless check is the minimum.
- **Cost:** print the per-tic cost in the overlay line. Measure it on the busiest maps you have
  (DOOM1 E1M9 at skill 4 with `wake`; Doom II MAP29/MAP30 if `wads/doom2.wad` exists) and put the
  numbers in the SPEC entry. Expect well under SPEC §9's 2 ms tic budget. If it isn't, check only
  monsters within the screen's map bounds.
- All the usual checks: `dotnet build`, `dotnet test` (the sim's tests unchanged),
  `dotnet format IsoDoom.sln --verify-no-changes --severity warn`, `--level-check` headless on
  DOOM1 and the synthetic IWAD, and under lavapipe on DOOM1 E1M1.

### Screenshots (what the user is waiting for)

The point of the branch is to see the look. Take before/after pairs under lavapipe at 1280×800
(the offscreen command in CLAUDE.md, `--level-screenshot` or a script's `shot`) into
`screenshots/t6.13t-sight-dim/` with an `index.html` side by side, as
`screenshots/t6.13s-height-cues/` did (WAD-derived: never commit them):

- DOOM1 E1M1: the start room with the zombies across the nukage, some in sight and some behind the
  pillars or walls.
- A ledge case: a monster on a raised floor whose edge blocks its sight to the player below (find
  one with the route generator's `--info`/`--monsters`, e.g. E1M2 or E1M3).
- A dark room (to judge the amount): 6, 10 and 14 rows side by side.
- The same view with `--level-sight-dim=off`.

Use `spawn` and `place` (script commands) to set up the scenes, and `cmd 0 0 ANGLE 0 1; tics 1`
so a tic has run (the sight test runs after a tic) before `shot`. Tell the user where the page is
and stop there. The default amount, and whether to merge, are the user's call.

### SPEC §12 entry (decisions to log)

- The rule (vanilla's `P_CheckSight`, monster to player, the candidates), why: the first-person
  view's implicit "if you see it, it sees you" lost under the iso camera.
- Why calling it between tics is safe (above), and the cost.
- The fade (instant in, 4-tic grace, 2 rows a tic out), the shader's row shift, max row 29,
  full-bright frames dimmed, fuzz not.
- Rejected: transparency (alpha-sorted billboards and off-palette colours; dither reads as the
  cutaway's or the fuzz's); hiding (that's `--level-fog-things=sight`, per sector); a dim keyed to
  "can the player hit it" (`P_AimLineAttack` changes `linetarget` and the shot's slopes, which the
  sim reads; that is option 1 of the options doc, still open: a monster in sight that the player's
  autoaim can't reach stays fully lit by this rule, which is right, since it can shoot).
