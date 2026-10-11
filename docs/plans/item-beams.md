# Plan: item beams (pickups hidden behind walls)

From play: under the game camera (fixed iso, looking north-west at 55°, SPEC §7.1, §12 T3.3), a
pickup that stands just behind a wall seen from the camera's side is hidden by that wall, while a
vanilla player standing in the room would see it. The cutaway (§7.3) only opens walls near the
player, so pickups farther away (across a room, behind a ledge, in an alcove behind a pillar) stay
hidden. Isowulf solves the same problem with a thin vertical line from the item into the sky. This
plan does the same, as a presentation option.

**Follow CLAUDE.md's session workflow: one task.** First add the task below to `docs/TASKS.md` under
M6, after T6.13q, in the style of the other user requests (T6.13j, T6.13k, T6.13p): a `- [ ]` line
with the problem, a **Do:** paragraph and a *Done when:* line, taken from this file. Then do it, tick
it with a *Done (date):* paragraph, and log the decisions in SPEC §12.

Presentation only. Nothing in `src/Sim/` changes: the sim's checksums, saves, routes and demo sync
must not move. If a sim test changes, something went wrong.

---

## T6.13r Item beams

### Problem

- Walls hide what is behind them on screen up to about 0.7 × the wall's height behind it (the
  floor a wall of height h hides at the 55° pitch is h / tan 55°): an item within ~90 units behind
  a 128-unit wall facing the camera is hidden whole.
- Vanilla's first-person view shows those items as soon as the player is in the room, so the player
  misses health, armor, ammo, keys and weapons that vanilla would show.
- Find a concrete case on DOOM1 before starting (the route generator's `--info MAP` and
  `--monsters` list the things; `place X Y ANGLE; shot FILE.png` shows them) and note it in the
  task, as T6.13p did with its switch lines. Use one in the level check below.

### Look

A thin vertical beam rising from the item, in a colour by the item's kind, drawn **only while the
item itself is hidden** by the level, and depth-tested against the level, so it seems to come up
from behind the wall that hides the item: the player sees "something is back there", its kind by
colour, and where.

- **Shape:** a ribbon facing the camera (as `ShotTracers` builds its ribbons), about 1.5 units wide,
  from the item's floor up to a fixed height above it (start with 160 units: enough to clear a
  128-unit wall right in front of the item), opaque at the bottom and fading out over its top
  third. Try a small mark at the beam's top (a diamond or a dot in the same colour) and keep it only
  if it reads better.
- **Colour by kind** (a plain C# table over `mobjtype_t`, first match wins):
  - keys and skull keys: their key colour (blue, yellow, red);
  - weapons: orange;
  - power-ups and the big ones (soul sphere, mega sphere, invulnerability, blur, radiation suit,
    light amp, computer map, berserk, backpack): white or magenta;
  - armor (green and blue armor, armor bonus): green;
  - health (stimpack, medikit, health bonus): red-pink;
  - ammo: yellow-brown.

  Pick colours that read on the dark Doom floors and differ from the shot tracers'
  (`ShotTracers.PlayerColor`, `MonsterColor`).
- **No pulse at first.** Static is calmer; try a slow pulse only if a static beam is hard to spot,
  and show the user both.

### Which things

- Pickups: mobjs with `MF_SPECIAL` (p_mobj.c / info.c), including dropped ones (`MF_DROPPED`: a
  zombie's clip, a sergeant's shotgun). Not decorations, monsters, barrels, missiles or corpses.
- Only things the fog of war lets be drawn (`LevelScene.FogShows`, T6.13l, T6.13n). A beam must
  never reveal an item in a sector the player has not discovered.
- Only under the game camera (`IsoActive`), as the fog. The overview and free-fly cameras draw none.
- The option has three settings: `off`, `keys` (keys, weapons and power-ups only: the ones a player
  would go back for) and `all` (every pickup). Start with `all` as the default and let the user's
  look at the screenshots decide (E1M1's armor and nukage rooms have many small pickups: check for
  clutter there, but beams show only for *hidden* items, so it should be small).

### "Hidden" (the core decision)

A beam shows only while the level hides its item. The renderer decides this, because the cutaway,
door lids, wall caps, upper walls and the fog all change what hides what (an occlusion test on the
CPU would have to repeat all of them).

- **On the GPU, per beam fragment:** the beam is drawn in the transparent pass and reads the level's
  depth texture, as the upright hiding does (`shaders/sprite_hidden.gdshader`, SPEC §12 T3.6a: the
  depth texture holds the walls, floors and masked middles, drawn in the opaque pass; check whether
  the lids, wall caps and switch markers are in it too, and say so in the decision). Each beam
  instance carries its item's billboard in custom data (foot position, width, height, and the
  sprite's tilt, which `SpriteSettings` sets). The fragment shader projects a handful of sample
  points of the item's **drawn** (tilted) billboard (for example the centre column at 20, 50 and
  80 % of its height, and the quarter columns at 50 %) with `PROJECTION_MATRIX * VIEW_MATRIX`,
  reads the depth texture at each, and discards the fragment unless every sample point lies behind
  the level's depth there (with a small bias). Then the beam shows exactly when the item is hidden
  whole, and appears or vanishes as the cutaway opens or closes a wall.
  - Mind the screen edges and the HUD: a sample point off screen or under the status bar counts as
    hidden or not? Decide (simplest: off screen counts as visible, so no beam for items off screen
    whose beams poke into view; under the bar: test what looks right).
  - The things' own sprites are not in the depth texture (they draw in the transparent pass), so a
    monster in front of an item does not trigger a beam. That is right: it is not the level hiding
    it.
  - The beam itself is depth-tested normally (no `NoDepthTest`, unlike the tracers), so the wall
    hides its lower part and it rises from behind the wall's top. Check this against the cutaway's
    `discard` in the level shader: a cut wall writes no depth, so a cut-open item is visible and
    gets no beam.
- **Partly hidden items get no beam** (they are visible). If playtesting shows that a sliver of an
  item peeking over a wall is easy to miss, add a threshold (beam when at least N of the sample
  points are hidden) and note it.
- **Cost:** a few depth samples per beam pixel, for a few dozen beams of about 2 × 100 pixels each.
  Measure the frame time under lavapipe on E1M1 with `all` and every item on screen (as T3.6a
  measured the upright hiding's 3 ms) and note it; `tictime` is unaffected (the sim is not involved).

Rejected (log these in SPEC §12 with the decision):

- **An x-ray silhouette** (the hidden item's outline drawn through the wall): it tells what the item
  is, but drawing through walls fights the readable "walls hide" look the fog of war and the
  cutaway work for, and in the iso view it is hard to tell which room a silhouette is in. The beam
  rises above the walls, so the room is clear.
- **Beams on every item, hidden or not:** clutter in rooms full of pickups.
- **Cutting more of the walls (a bigger cutaway radius):** it removes walls far from the player and
  still misses items across the map.

### Code

- `src/Render/ItemBeams.cs` (new, Godot side, like `ShotTracers`/`AimMarker`): a
  `MultiMeshInstance3D` of camera-facing quads, one instance per beamed item, custom data as above,
  drawn with `shaders/item_beam.gdshader` (new; it may include the sprite shader's include,
  `sprite.gdshaderinc`, for the tilt maths rather than copying it). The beam is unshaded and full
  bright, as the tracers, so it needs none of the level's light.
- The kind table (`mobjtype_t` → kind → colour, and whether `keys` mode keeps it) as a static,
  Godot-free helper (in `ItemBeams.cs` or its own file under `src/Render`) so that it can be unit
  tested.
- `LevelScene.PresentWorld` (`src/Game/LevelScene.cs`, around the `_scratch` loop that builds
  `Things`' entries): collect the beamed mobjs (pickup, `FogShows`, not the player) and their
  interpolated positions (`Interpolated(mo)`), and set the beams' instances the same way (rebuild on
  a change of the list, update positions otherwise; items rarely move, but they ride lifts and
  floors, T5.1). The sprite's height and width come from the frame `ThingSprites` shows
  (`ShownFrames` / the atlas), so the beam's hidden test covers the real billboard.
- Clear them on a level unload and on a reborn, as `_shotTracers.Clear()`.
- **Option** (the usual set, as T6.13p's switch markers): enum `ItemBeamMode { Off, Keys, All }`,
  `--level-item-beams=off|keys|all`, setting `gameplay/item_beams` (an `Add(...)` line in
  `LevelScene.Settings.cs`) on the **Sprites** page (`MMenu.Setup.cs`; the Gameplay page is full,
  see T6.13p; `SettingsTests` checks every page fits), a debug key (F looks free: neither the
  key switch in `LevelScene.cs` nor `project.godot`'s actions use it; check), the F1 controls line,
  and an overlay line when not the default. Update CLAUDE.md's `--level-*` list and key list
  (CLAUDE.md is a symlink to AGENTS.md: edit AGENTS.md, and do not use `sed -i` on it).
- **Level script:** `beams` prints the beamed items (type, kind, position, sector) for scripts and
  for debugging; whether each shows on screen is the GPU's decision, so the screenshot checks below
  test that.

### Checks

- **Unit tests** (`tests/IsoDoom.Tests`, e.g. `ItemBeamsTests`): every `MF_SPECIAL` type of
  `mobjinfo` has a kind; keys map to their key colours; `keys` mode keeps keys, weapons and
  power-ups and drops health, armor and ammo; no non-`MF_SPECIAL` type gets a beam.
- **`--level-check`, every map (headless):** the beams' instances are exactly the pickups the fog
  shows (none in an undiscovered sector at the start; after the fog check's door opens, the items
  seen through it).
- **`--level-check` with a real renderer** (`LevelCheck.Beams.cs`, new, in the style of
  `LevelCheck.Markers.cs`), from the game camera's angle:
  - an item behind a wall facing the camera (the case found above, or the synthetic E1M1's: add an
    item behind a wall to `tools/SyntheticIwad`'s E1M1 if it has none, since CI's render checks run
    there): beam pixels above the wall's top, none below it;
  - the same item with the wall cut away by the cutaway (centre just behind the wall): no beam
    pixel;
  - an item in the open: no beam pixel;
  - an item in an undiscovered sector under `hide`: no beam pixel;
  - option `off`: no beam pixel anywhere.
- **The sim:** `dotnet test` passes unchanged (route, demo, save and checksum tests).
- **Format and build:** `dotnet build IsoDoom.sln` (warnings are errors), `dotnet format IsoDoom.sln
  --verify-no-changes --severity warn`.
- **Cost:** the frame time with and without the beams, as above.

### Show the user

Take screenshots under lavapipe (offscreen, `--level-screenshot` or a script's `shot`; never
commit them) of the case found above with the option `all`, `keys` and `off`, and of E1M1's start
and its armor room with `all`, and show them to the user before ticking the task. The user decides
on looks: earlier presentation changes were rejected on looks alone (T6.13h/T6.13i), so ask before
merging. Ask in particular about the colours, the beam's height and width, and whether `all` or
`keys` should be the default.

### Docs

- `docs/SPEC.md` §7.5 (sprites): a bullet for the item beams, as §7.2 has for the switch markers.
- `docs/SPEC.md` §12: a decision row (date, T6.13r, not vanilla, presentation only): the problem,
  the rejected options, which things, the kinds and colours, the hidden test and its samples, the
  fog rule, the option and default, what was checked and the cost.
- `docs/TASKS.md`: the task ticked with its *Done* paragraph; a line in T6.13a's hand-play list
  ("item beams: hidden pickups show a beam; check they read, are not cluttered, and none reveals an
  unexplored room").
- CLAUDE.md (AGENTS.md): the `--level-item-beams` argument, the key, the `beams` script command,
  and the level check's new views.

### Done when

- A pickup hidden whole behind a wall under the game camera shows a beam rising above the wall, in
  its kind's colour; a visible one, a cut-open one and one in an undiscovered sector do not.
- The option works from the command line, the menu, the key and the settings file.
- The unit tests and `--level-check` (headless on DOOM1 and the synthetic IWAD, and under lavapipe
  on DOOM1 E1M1 and the synthetic E1M1) pass; `dotnet test` passes with the sim unchanged.
- The user has seen the screenshots and agreed on the look and the default.
- Committed on branch `t6.13r-item-beams`.
