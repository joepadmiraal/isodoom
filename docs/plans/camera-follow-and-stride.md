# Plan: the player slides over the screen (camera follow) and over the floor (walk stride)

From hand play after T6.13e: running under the game camera, Doomguy "slides". Two separate causes,
two tasks. **Follow CLAUDE.md's session workflow: one task per session.** In the first session, add
both tasks to `docs/TASKS.md` (M6, after T6.13g, in the "from hand play" style of T6.13b–g), then do
**T6.13h**. Feed this file again to a new session for **T6.13i**.

Neither task touches `src/Sim/`: both are presentation only. The sim, its checksums, saves, routes
and demo sync must not change. If a sim test changes, something went wrong.

---

## T6.13h Camera follow without lag

### Problem (measured)

`IsoCamera.Follow` ([src/Render/IsoCamera.cs](../../src/Render/IsoCamera.cs), called from
`LevelScene.FollowPlayer`) smooths the whole focus exponentially towards
`target + LookAhead(target, cursorGround)` (`SmoothingRate` = 10/s, SPEC §12 T3.3). Two effects
combine:

1. **Lag.** Following a target at a steady speed v, an exponential follower settles v / rate behind
   it. Running is about 580 units/s, so the camera settles about 58 units behind.
2. **Look-ahead feedback.** The mouse cursor stays put on screen, so its ground point moves with the
   lagging camera. With the cursor near the centre, `cursorGround − target` points *backwards*, and
   the look-ahead pulls the goal behind the player as well. The steady lag becomes about
   v / (0.75 × rate) ≈ 78 units.

A probe of the focus-to-player distance per frame on DOOM1 E1M1 (`--fixed-fps 60`, running east
for 2 s, then letting go) gave:

| frame | gap between focus and player |
|---|---|
| 0.25 s | 27 units |
| 0.5 s | 45 units |
| 1 s | 66 units |
| 0.25 s after stopping | 17 units |
| 0.5 s after stopping | 2.5 units |

68 units is about 11% of the default 640-unit view, roughly 85 px at 1280×800. The player drifts off
centre in the direction they run, then glides back to centre after they stop.

### Change

- **Follow the drawn player exactly:** `Focus = target + _lookAhead`. The target is already
  interpolated every frame (`LevelScene.Interpolated`, T4.7), so there is no jitter to smooth away.
- **Smooth only the look-ahead,** at `SmoothingRate`, so a mouse flick still eases the view.
- **Work out the look-ahead from the cursor's offset from the screen centre, not from the player:**
  `goal = LookAheadFraction × (cursorGround − Focus)` (horizontal), clamped to
  `MaxLookAheadUnits`. `cursorGround` comes from the previous frame's camera, which is centred on
  `Focus`, so this is a screen-relative offset. Moving the camera moves the cursor's ground point by
  the same amount, which removes the feedback. With no cursor (`CursorOrCentre`, the centre above
  the HUD) the look-ahead is zero. Check against the HUD's `BottomInset`: the "centre" is the point
  above the bar, as `CursorOrCentre` defines it.
  - Watch for one subtlety. The cursor's ground point sits on the floor it picks (`CursorGround`),
    not at the player's height, so over a raised floor the offset also includes the height
    difference along the view. Measure the offset on the plane of the player's feet (intersect the
    cursor ray with `y = target.Y`) so that raised floors don't push the view. Keep `Cursor.Point`
    for aiming as it is.
- `Snap` (a new map, a teleport) and `HoldCamera` keep working. `Snap` also resets the look-ahead.
- Put the maths in a plain static function, Godot types only as `System.Numerics` or plain floats if
  needed, so `tests/IsoDoom.Tests` can unit-test it. The test project compiles a few Godot-free
  files from `src/Game` directly: see the comment and the `Compile Include` items in
  `tests/IsoDoom.Tests/IsoDoom.Tests.csproj`. Tests to write:
  - a constant-speed target gives zero lag;
  - a fixed cursor offset converges to its clamped look-ahead and stays put while the target moves;
  - `Snap` resets the look-ahead.

### Checks

- Add a level-script command, e.g. `follow FRAMES [MAX]`, beside `smooth` in
  `src/Game/LevelScript.cs`. It prints the largest distance per frame between the drawn player's
  foot and `Focus − lookAhead` (map units, horizontal) and fails above MAX (default 0.5). Document
  it in CLAUDE.md's scripted-input list. The probe used for the numbers above was:
  `godot --headless --fixed-fps 60 -- -iwad wads/DOOM1.WAD --level E1M1 --level-wipe=off --level-script="sim live; down Shift; down D; follow 70; up D; follow 40"`.
- The level check (`src/Game/LevelCheck*.cs`) should run the player across a map under the game
  camera and fail if the player leaves the screen-centre point, once the look-ahead is taken out.

### Docs

- SPEC §7.1 currently says "slight smoothing". Change it to "follows the player exactly; the
  look-ahead is smoothed".
- Add a §12 decisions-log row (T6.13h) with the measurement, the cause (lag plus feedback) and the
  new rule. Update the T3.3 row's camera sentence to point to it.
- Update CLAUDE.md for the new script command.

### Done when

The player stays at the same screen point while running at a steady speed (with the mouse still),
neither drifting nor gliding back after stopping. The look-ahead still eases towards the cursor.
The level check covers it, and build, test, format and `--level-check` pass.

---

## T6.13i Walk cycle matched to the distance walked (stride)

### Problem

Vanilla's `S_PLAY_RUN1`–`RUN4` (`PLAYA`–`PLAYD`, a full two-step cycle) last 4 tics each, so one
cycle takes 16 tics whatever the speed. At the top walking speed (about 8.3 units/tic) that covers
about 130 units per cycle, and running (about 16.7 units/tic) about 265 units. In first person you
never saw this. Seen from outside, the feet skate over the floor like ice skating.

### Change (presentation only)

- In the level scene, keep a **stride phase** per player mobj: the horizontal distance its *drawn*
  (interpolated) position covered, accumulated every frame in map units. While the mobj's state is
  `S_PLAY_RUN1`…`S_PLAY_RUN4`, draw frame `(int)(phase / unitsPerFrame) % 4` (A–D) instead of
  `mo.frame`. Every other state (standing `S_PLAY`, attack E/F, pain, death) draws the sim's frame
  as now.
  - The place to hook in is `LevelScene.PresentWorld` → `ThingEntry(me, Interpolated(me))`: override
    the entry's `Frame` there.
  - Use `mo.player != null`, so later co-op players get it too.
  - Reset the phase on `Snap`, teleport and map load.
  - Start a new walk at frame A: when the state goes from `S_PLAY` to `RUN1`, align the phase to a
    cycle boundary so the first drawn frame matches vanilla's.
- **Setting:** `sprites/stride` = `UNITS|off`, the map units per full cycle (`off` = vanilla's
  per-tic frames). Add `--level-sprite-stride=UNITS|off` and a SPRITES page entry, following how
  `sprites/wall_pull` is plumbed (`SpriteSettings`, `LevelScene.Settings.cs`, `MMenu.Setup.cs`,
  CLAUDE.md's argument list).
  - Choose the default by measuring, not by guessing. In the viewer
    (`godot -- --viewer`, the `PLAYA1`–`PLAYD1` side views, rotation 3 or 7), measure the distance
    between the feet in the widest-stride frames. A full cycle is two steps, so the default is
    about 2 × step. Expect roughly 40–70 units.
  - Check the result visually with `--level-script` screenshots under lavapipe (CLAUDE.md, *Level
    check*'s offscreen command): a planted foot should stay still on the floor between frames.
    Never commit the images.
  - With only 4 frames, a skate-free cycle may flicker at running speed (a frame almost every
    tic). If it does, say so in the task's notes and pick a compromise default; the setting lets
    the user choose.
- Only the player for now. Monsters move in `P_Move` steps per `A_Chase` call and skate much less;
  if they look wrong in hand play, add a follow-up task rather than widening this one.
- Put the phase-to-frame maths in a plain static function with unit tests (as in T6.13h):
  - frame boundaries at multiples of `unitsPerFrame`;
  - wrap-around after D;
  - `off` returns the sim's frame;
  - non-run states pass through unchanged.

### Checks

- Add a level-script command, e.g. `stride FRAMES`, that prints the frames drawn and the distance
  covered per frame change. Fail if the distance per change is not within one frame's step of
  `unitsPerFrame`.
- In the level check, run the player and check that the drawn frame advances with distance, not
  with tics: the same distance at walking and running speed gives the same frames. Also check that
  standing still shows `S_PLAY`'s frame.

### Docs

- SPEC §7.5: the player's walk cycle follows the distance walked (a presentation option).
- §12 row T6.13i: the measured stride, the default and why. Note that it is presentation only (no
  tweak flag: the sim's states are vanilla's).
- CLAUDE.md: the argument and the script command.
- T6.13a's hand-play list in TASKS.md: add a line to judge the stride by hand.

### Done when

Walking and running, the player's feet no longer skate (a planted foot stays put within about a
frame). `sprites/stride off` gives vanilla's per-tic frames. The sim's checksums, routes and demo
sync are unchanged. The level check covers it, and build, test, format and `--level-check` pass.
