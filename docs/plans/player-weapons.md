# Plan: the player holds its ready weapon (T6.14)

From a user question (2026-10-20): is there a WAD that gives the player sprites holding each weapon,
so you can see which weapon a player holds? There is none for vanilla. Vanilla has one player
sprite set, `PLAY`, and its frames hold the same generic rifle whatever the ready weapon. The mods
that do this need ZDoom's or Zandronum's player states (`WeaponSkin`, inventory-token jumps), which
this game ignores (`src/Map/VanillaIgnores.cs`). So we draw it ourselves: **the ready weapon's
pickup sprite (`SHOTA0`, `LAUNA0`, …) in the hands of every player's sprite**, the stock rifle
removed, the weapon in front of the body or behind it as the pose needs, and the hands over its
grip.

A mock-up was made by pasting pickup sprites onto game screenshots by hand. It is in
`screenshots/mockup-player-weapon/` (`sheet-side.png`, `sheet-front.png`, `full/`; gitignored,
WAD-derived). It showed:

- **The side view reads well.** The pickup sprites are side views pointing the way the player
  faces, and at half scale the shotgun is about as long as the stock rifle.
- **The stock rifle must go.** Thin weapons don't cover it.
- **The weapons need their own scales.** At one scale, the BFG covers the torso.
- **Facing away needs the weapon behind the body**, and facing the camera needs it foreshortened.
  A pasted overlay can do neither.
- **The firing frame's muzzle flash** sits at the stock muzzle.

Presentation only. Nothing in `src/Sim/` changes: the checksums, saves, routes, demo sync and
netgames must not move. If a sim test changes, something went wrong. Teammates and deathmatch
opponents show their weapons too, which is half the point in a netgame. Every machine draws them
from its own world, so nothing goes on the wire.

---

## Approach: composite on the CPU, draw as one sprite

**Each (weapon, `PLAY` lump) pair becomes one new sprite image, built on the CPU at WAD load.**
For each one:

1. Remove the stock rifle from the `PLAY` lump and fill the hole.
2. Draw the weapon's pickup sprite, scaled, rotated and foreshortened to the rifle's axis.
3. Layer the weapon behind or in front of the body by the pose.
4. Draw the hand texels back over the grip.

The renderer then draws the composite as it draws `PLAYA1` now. **"Masks or depth tricks" are
answered on the CPU by layering with masks: no second billboard, no depth bias.**

Why not a second billboard for the weapon, sorted by a depth bias:

- The sprite shader turns billboards towards the camera, drops tilted vertices back onto the
  upright plane, pulls them off walls by the thing's radius, and hides them with the upright test
  (`shaders/sprite.gdshaderinc`). Two quads would have to agree on all of it at every frame, or
  the weapon swims or z-fights against the body.
- A one-texel outline drawn around each quad separately would draw a black line across the body
  where the weapon crosses it.
- "Behind the body but the muzzle sticks out" and "the hands over the grip" are per texel. A
  depth bias is per quad.

Compositing gets all of it right at once. The outline goes around the whole silhouette, and the
same holds for the cutaway, tilt, wall pull, upright hiding, fuzz (blur sphere), the player's
minimum light, the player-only palette flash, the sight dim (players are never dimmed), blob
shadows and the level check's pixel comparison. They all already work on any atlas slot.

**Cost:**

- **Atlas space:** 9 weapons × 35 `PLAY` lumps (frames A–G, 5 lumps each; rotations 6–8 are 4–2
  mirrored) = 315 images of about 80×70 texels, about 1.8 M texels (3.5 MB as RG8).
- **Load time:** the composites are built once per WAD, not per map, inside SPEC §9's 1 s budget.
  Measure it.

**Not composited:**

- **Death and gib frames** (`PLAYH0`–`PLAYW0`) stay stock. In vanilla they already drop the rifle.
- **Monsters** stay stock: zombies holding their dropped weapons is another feature (Box Art
  DOOM).

### Known facts the plan rests on (checked 2026-10-20)

- **The `PLAY` lumps:** DOOM1, The Ultimate Doom (2024) and Doom II share identical `PLAY` A–G
  lumps. TNT and Plutonia share a second set, which differs in 24 of the 35 lumps (Final Doom
  retouched them: compare them before assuming offsets only). The pose data must hold for both
  sets.
- **The pickup sprites** all point their muzzle to the right (+x): `SHOTA0` 63×12, `SGN2A0`
  54×14, `MGUNA0` 54×16, `LAUNA0` 62×16, `PLASA0` 54×16, `BFUGA0` 61×36, `CSAWA0` 62×24. The fist
  and pistol have none (`WeaponIcon` uses `PUNGA0`/`PISGA0`, which are first-person front views,
  wrong for this).
- **The renderer** (`src/Render/ThingSprites.cs`, `SpriteAtlas.cs`, `PlayerSprite.cs`):
  - Every thing is one MultiMesh instance.
  - `Entry.Sprite`/`Frame` pick a sprite frame, and `SpriteFrame.Select` picks the rotation and
    mirror.
  - `SpriteAtlas.Build` packs every lump of every `sprnames` frame. It has no way to add images
    that are not lumps yet.
  - `LevelScene.ThingEntry` (about `LevelScene.cs:3297`) builds the entry from
    `mo.sprite`/`mo.frame`. The player's own sprite goes through `PlayerSprite.Set`, and the
    others through `Things`.

---

## Sessions

| Session | Task | Depends on | Can run in parallel with |
|---|---|---|---|
| 1 | T6.14a Hold poses: the stock rifle in each `PLAY` lump | — | 2 |
| 2 | T6.14b Weapon art: the held sprites, their anchors and a pixel-art resampler | — | 1 |
| 3 | T6.14c The compositor, and the user's look review | 1, 2 | — |
| 4 | T6.14d In the game: atlas, every player's sprite, option, checks | 3 and the user's go | — |
| 5 | T6.14e Polish after hand-play (optional; the user picks from its list) | 4 | — |

**Branches:** create `t6.14-player-weapons` from `main` first, and commit this plan on it.
Sessions 1 and 2 branch from it (`t6.14a-hold-poses`, `t6.14b-weapon-art`) and run in separate
worktrees, as the memory note on parallel agent worktrees says: outside the repo, WADs copied in,
their own `ISODOOM_VANILLA_REF`. Merge both back into `t6.14-player-weapons`; they touch disjoint
files, but expect `docs/TASKS.md` and SPEC §12 to need a hand merge. Sessions 3–5 branch from
`t6.14-player-weapons` in turn, and each merges back. **Nothing goes to `main`**: the user decides
after seeing it in the game.

**Tasks:** session 1 adds all five tasks (T6.14a–e) to `docs/TASKS.md` under M6, after T6.13z, in
the style of the user requests (T6.13r, T6.13t): a `- [ ]` line with the problem, a **Do:**
paragraph and a *Done when:* line, taken from this file. Session 2 adds them too if it runs before
session 1 has merged (the merge then keeps one copy). Each session ticks its own task with a
*Done (date):* paragraph and logs its decisions in SPEC §12 under one entry, "T6.14 Player
weapons", which each session extends.

### The contract between sessions 1, 2 and 3

Sessions 1 and 2 run at the same time, so the types session 3 joins are fixed here. They live in
`src/Render/`, have no Godot types (so `IsoDoom.Tests` links them), and are tested in
`tests/IsoDoom.Tests/Render/`. Coordinates are in the lump's texel space: x right, y down, (0, 0)
the top-left texel's corner, so a texel's centre is (x + 0.5, y + 0.5). Use `double` there: this is
presentation code. Integer rounding happens only in the resampler, deterministically.

```csharp
// Session 1: src/Render/HoldPose.cs
public enum HoldLayer { Front, Behind }
public sealed record HoldPose(
    string Lump,            // "PLAYE2E8"
    bool[] Rifle,           // w*h: the stock rifle's texels (removed)
    bool[] Flash,           // w*h: the muzzle flash's texels (frame F only; removed, redrawn at the new muzzle)
    bool[] Over,            // w*h: body texels drawn over a Front weapon (the hands on the grip)
    HoldLayer Layer,        // the weapon in front of the body or behind it
    (double X, double Y) Grip,    // where the rear hand holds the weapon
    (double X, double Y) Muzzle,  // the rifle's muzzle end
    double Foreshortening); // 0.3–1: the rifle's drawn length over its side-view length
public static class HoldPoses { public static HoldPose? For(string lump, IndexedImage image); } // null: unknown lump

// Session 2: src/Render/HeldWeapon.cs
public sealed record HeldWeapon(
    weapontype_t Weapon,
    IndexedImage Image,           // pointing right, ramp-safe (no 0x70–0x7F texels)
    (double X, double Y) Grip,    // where the hand holds it
    (double X, double Y) Muzzle,  // its muzzle end
    double Scale);                // texels of the player per texel of the image (about 0.5)
public static class HeldWeapons { public static HeldWeapon?[] Load(WadArchive wad, byte[] playpal); } // null: none (the fist, or a lump the WAD lacks)
public static class PixelArt
{
    // Map src so srcFrom→dstFrom and srcTo→dstTo (rotation, uniform scale), then scale along that
    // axis by `along`; draw into a new image of the given size. Nearest-colour pixel art, no blending.
    public static IndexedImage Transform(IndexedImage src, (double X, double Y) srcFrom, (double X, double Y) srcTo,
        (double X, double Y) dstFrom, (double X, double Y) dstTo, double along, int width, int height);
}
```

A session may add members, but must not change these. If one has to change, it says so at the top
of its *Done* paragraph and in its hand-back, so the other session adapts before the merge.

---

## Session 1 — T6.14a Hold poses

```
Task T6.14a of docs/plans/player-weapons.md (read the whole plan first; this session is "Session 1").
Branch `t6.14a-hold-poses` from `t6.14-player-weapons` (create that from `main` if it doesn't exist yet,
and commit the plan on it). Session 2 may be running at the same time in another worktree: only touch
the files this session owns (below), docs/TASKS.md and SPEC §12.

First add tasks T6.14a–e to docs/TASKS.md under M6 after T6.13z, as the plan's *Tasks* paragraph says.

Goal: for each of the 35 PLAY lumps of frames A–G (PLAYA1, PLAYA2A8, …, PLAYG5), a `HoldPose` as the
plan's contract defines it, for both PLAY sets: the one DOOM1, doom.wad and doom2.wad share, and TNT's
and Plutonia's (wads/TNT.WAD, wads/PLUTONIA.WAD; 24 lumps differ, so look at what changed first).

Own files: src/Render/HoldPose.cs, src/Render/HoldPoses.cs (and a data file beside it if the table
is big), tests/IsoDoom.Tests/Render/HoldPoseTests.cs, a tool under tools/HoldPoses/ if you write one.

How:
- Committed data is ours, never id's pixels: per lump a few numbers, i.e. one or more seed texels
  on the rifle, the layer, the grip point, rectangles of hand texels for `Over`, and a seed on the flash
  for frame F. Everything else is derived at load from the WAD's own lump:
  - `Rifle` is a flood fill from the seeds over the rifle's colours (find its palette indices: the
    dark greys/blacks; the visor and boots share some, so bound the fill by the seeds' component and
    a box).
  - `Muzzle` is the far end of the mask's principal axis from `Grip`.
  - `Foreshortening` is the rifle's length along that axis over the side views' (rotations 3/7)
    length for the same frame, clamped to 0.3–1.
- Key the data by lump name. Where TNT's lump differs, either the same seeds still work (assert it)
  or give a second entry selected by the lump's content hash. A lump whose content matches neither
  known set (a PWAD's PLAY) gets null: the game then draws stock sprites for it. Log that once per
  WAD, not per lump.
- Layer: Behind where the rifle is on the far side of the body (the back views, rotation 5 and probably
  4/6; look at each lump), else Front. Use `Over` for the hand texels that hold the grip in Front poses,
  and in Behind poses for nothing. Decide each by eye from enlarged lumps.
- Write a review sheet tool (a test with an env var such as `ISODOOM_HOLDPOSE_SHEET=DIR`, as
  `ISODOOM_HUD_DIR`, or a tools/ program): every lump enlarged 6×, with the Rifle mask in magenta, Flash
  in cyan, Over in yellow, Grip and Muzzle as dots and the layer written beside it, for both sets, into
  screenshots/t6.14-player-weapon/poses-{doom,final}.png (gitignored: WAD-derived). Iterate until every
  lump's masks are right, including frame F's flash and frame G (pain).

Tests (skip without the WAD, as the other WAD tests): every lump of both sets gets a pose; each Rifle
mask is one component of a plausible size (say 40–400 texels; measure); Muzzle is farther than Grip from
the body's centre column in the side views; Foreshortening is 1 in rotations 3/7, < 1 in 1 and 5;
mirrored lumps are not special-cased (the renderer mirrors the whole composite); an unknown lump gives
null; the poses come out the same for DOOM1, doom.wad and doom2.wad.

Done when: the tests pass, the review sheets show every mask right (look at them yourself and list any
doubtful lump in the hand-back), build, `dotnet test` and `dotnet format IsoDoom.sln --verify-no-changes
--severity warn` pass, and the SPEC §12 "T6.14 Player weapons" entry holds the pose rules (seeds +
derived masks, why no pixels are committed, the two PLAY sets, unknown lumps stock). Tick T6.14a and
commit. Tell the user where the sheets are.
```

## Session 2 — T6.14b Weapon art and the resampler

```
Task T6.14b of docs/plans/player-weapons.md (read the whole plan first; this session is "Session 2").
Branch `t6.14b-weapon-art` from `t6.14-player-weapons` (create that from `main` if it doesn't exist yet,
and commit the plan on it). Session 1 may be running at the same time in another worktree: only touch
the files this session owns (below), docs/TASKS.md (add T6.14a–e if they aren't there; a merge will
keep one copy) and SPEC §12.

Goal: `HeldWeapon`, `HeldWeapons.Load` and `PixelArt.Transform` as the plan's contract defines them.

Own files: src/Render/HeldWeapon.cs, src/Render/HeldWeapons.cs, src/Render/PixelArt.cs,
tests/IsoDoom.Tests/Render/{HeldWeaponTests,PixelArtTests}.cs.

How:
- Sources: the pickup sprites as WeaponIcon (src/Game/WeaponIcon.cs `_pickupSprites`): SHOTA0, SGN2A0,
  MGUNA0, LAUNA0, PLASA0, BFUGA0, CSAWA0, looked up in the loaded WAD (so a PWAD's replacements are used).
  Share the lump table with WeaponIcon rather than copying it.
- Per weapon, committed numbers (ours): Grip and Muzzle as fractions of the image's width/height (so a
  PWAD's sprite of another size still gets sensible anchors), and Scale. Start at 0.5 and tune per weapon
  so the drawn length suits a 56-unit marine: the mock-up found the BFG too big at 0.5. Pick the numbers
  by eye from enlarged sprites, and note them in the hand-back.
- The pistol has no side-view sprite in any IWAD. Draw our own: a small side-view pistol (about 14×7) as
  palette indices in a C# array, in Doom's palette's greys, our own work under the GPL (say so in a
  comment). The fist has none: `Load` returns null for wp_fist (session 3 then draws empty hands; the user
  judges that look).
- Ramp safety: every texel in the player translation's green ramp (0x70–0x7F,
  src/Render/PlayerTranslation.cs) is remapped to the nearest palette colour outside the ramp (by RGB
  distance in PLAYPAL), so a teammate's colour never tints its weapon.
- `PixelArt.Transform`: a RotSprite-style resampler for palette art, with no blending (the composite
  stays palette indices):
  1. Upscale 8× with Scale2x (EPX) three times.
  2. Map every destination texel's centre back through the inverse affine (rotation, uniform scale,
     then `along` on the axis srcFrom→srcTo) into the 8× image and take that texel (nearest).
  3. Clip to the destination size, transparent outside.
  Deterministic: the same inputs give the same pixels on every platform (no Math.Sin differences can
  flip a texel: compute the affine once in double, then step in fixed point, or document why it is
  stable). Keep it fast enough for 315 composites at load (measure: a target of under 100 ms for all).

Tests: Transform's identity case is the image itself; a 90° rotation of a test pattern is exact; a 0.5
scale of a 2×2-checker image keeps palette indices only; the muzzle lands on dstTo within half a texel
for random angles; no texel of any loaded weapon is in 0x70–0x7F; the pistol art's indices are all
outside the ramp and opaque where intended; Load with DOOM1 gives null for the plasma rifle and BFG (not
in shareware) and the super shotgun, and with doom2.wad all eight; a sheet (env var, as session 1's) of
every weapon at its Scale and at 0°, 25°, 60° and a 0.4 foreshortening into
screenshots/t6.14-player-weapon/weapons.png.

Done when: the tests pass, the sheet looks clean (no broken lines at 25°: that is what Scale2x is for),
build, `dotnet test` and `dotnet format … --verify-no-changes --severity warn` pass, and SPEC §12's "T6.14
Player weapons" entry holds the weapon table's rules, the pistol art and the resampler choice. Tick
T6.14b and commit.
```

## Session 3 — T6.14c The compositor, and the look review

```
Task T6.14c of docs/plans/player-weapons.md (read the whole plan first; this session is "Session 3").
Merge `t6.14a-hold-poses` and `t6.14b-weapon-art` into `t6.14-player-weapons` if not done yet (resolve
TASKS.md/SPEC §12 by hand), then branch `t6.14c-compositor` from it.

Goal: `HeldWeaponComposer` (src/Render/HeldWeaponComposer.cs, no Godot types), which takes a PLAY lump's
`IndexedImage` (with its left/top offsets), its `HoldPose` and a `HeldWeapon` (or null: empty hands) and
returns the composite `IndexedImage` with new offsets.

Steps, per composite:
1. Remove: the pose's Rifle and Flash texels become holes. Fill each hole texel inside the body's
   silhouette from its nearest non-hole body texels (iterate outward, taking the most common neighbouring
   palette index, ties broken by a fixed order so it is deterministic). Hole texels outside the silhouette
   (the rifle sticking out past the body) become transparent. Decide "inside" by whether the texel has body
   texels on both sides along the row or along the column. Check frame G and the side views by eye: a
   badly filled torso is what the empty-hands case (fist) shows.
2. Place: `PixelArt.Transform` the weapon so its Grip lands on the pose's Grip and its Muzzle along the
   pose's Grip→Muzzle direction. Use the weapon's own length × Scale, not the stock rifle's (a pistol is
   short, a BFG long), times the pose's Foreshortening. Mirror it when the pose's muzzle points left of its
   grip.
3. Layer: Front: weapon over body, then the pose's Over texels (the hands) over the weapon. Behind:
   weapon first, body over it, so only what sticks out shows.
4. Flash (frame F only): redraw the pose's Flash texels moved so their base sits at the new muzzle (the
   flash's centre offset from the stock muzzle kept). Frame F is full-bright as a whole, as vanilla's.
5. Grow the canvas to fit everything (the weapon can stick out past the stock rifle), and adjust
   leftoffset/topoffset so the body stays exactly where the stock lump draws it: the feet must not move
   between weapons.

Also: a `HeldWeaponSet` that builds all composites for a WAD (9 weapon slots incl. empty hands for the
fist × 35 lumps, skipping lumps without a pose and weapons without art, the pistol with its own art) and
reports the time taken. It must be cheap enough for the 1 s load budget (SPEC §9): measure it on doom2.wad
and put the number in SPEC §12. If it's too slow, build lazily per weapon on first use.

Review sheets (the user's checkpoint):
- screenshots/t6.14-player-weapon/composites-{doom,final}.png: every weapon × every lump, at 4×, the
  stock lump first in each row.
- screenshots/t6.14-player-weapon/compare.png: the same layout as the mock-up's sheet-side.png (side
  view idle and firing, every weapon) from the composites, for comparison with
  screenshots/mockup-player-weapon/sheet-side.png.
Use the game's palette and a mid-grey background. Look at them yourself and fix what is clearly wrong
(holes, a weapon floating off the hands, a feet jump) before showing the user.

Tests: the body's texels outside Rifle/Flash are unchanged in every composite (Front: except where the
weapon covers them); the feet row and offsets put the body where the stock lump draws it; Behind poses
never draw a weapon texel over a body texel; no weapon texel in the ramp; deterministic (two builds
byte-equal); empty hands = the inpainted body alone.

Done when: the tests pass, build/test/format pass, the sheets exist, and you STOP and ask the user to
review them: the scales, the empty hands, the pistol art, the back views. Record their decisions in the
SPEC §12 entry and apply them in this session. Tick T6.14c (with the user's picks) and commit. Do not start
session 4 without the user's go.
```

## Session 4 — T6.14d In the game

```
Task T6.14d of docs/plans/player-weapons.md (read the whole plan first; this session is "Session 4").
Branch `t6.14d-in-game` from `t6.14-player-weapons` (with T6.14c merged and the user's go recorded in
TASKS.md).

Goal: every player's sprite in the game holds its ready weapon, as a presentation option.

How:
- Atlas: let `SpriteAtlas.Build` take extra images. The cleanest shape: synthetic sprites, one per weapon
  slot (named e.g. `#PW0`–`#PW8`; never a valid lump name so nothing collides), each with frames A–G whose
  rotation slots mirror PLAY's (the same lump pattern: rotation 1, 2/8, 3/7, 4/6, 5, flips included).
  `SpriteFrame.Select` and the shader's mirroring then work untouched. Add them after the WAD's sprites so
  existing sprite indices don't move; `CheckSpriteAtlas` must cover the new slots against the
  `HeldWeaponSet` images.
- Which sprite: in `LevelScene.ThingEntry` (and wherever a player mobj becomes an entry: the own sprite
  through `PlayerSprite.Set`, teammates through `Things`), when the option is on, the mobj has a player,
  its sprite is SPR_PLAY and its frame is A–G, use the synthetic sprite of `player.readyweapon`, same
  frame. Death frames, a mobj without a player (a voodoo doll is a player mobj too: check what vanilla's
  `mo.player` is for one and treat it the same as the player it belongs to), or a lump without a pose:
  stock. Use readyweapon only: during a change the old weapon shows until it is down, then the new one,
  which is vanilla's own order. T6.14e may animate it.
- Everything else comes through unchanged: light, own flash, fuzz, outline, translation (the art is
  ramp-safe), tilt, wall pull, upright hiding, cutaway, shadows, and the sight dim (players are never
  dimmed). Prove each with the checks below, not by assumption.
- Option: `sprites/player_weapon` on the Sprites page (`PLAYER WEAPON`, `on`/`off`, default `on`; check
  the page still fits, as T6.13p/T6.13r had to), `--level-player-weapon=on|off`. Look for a free debug
  key; if none is free, go without one and say so. F3 overlay: the player line gets ` holding SHOTA0`
  (or `stock`). Level script: `playerweapon` prints each player's drawn sprite/frame/slot and fails if a
  live player in A–G is drawn stock while the option is on and its lump has a pose.
- PWADs: a PWAD whose PLAY lumps match neither known set gets stock sprites, logged once
  (`Level: player weapons: PLAYA1 is a PWAD's, drawn stock`). A PWAD's pickup sprites are used. Run one
  set of docs/PWAD-VALIDATION.md that replaces PLAY, if any does (grep the sets' lumps first; say if none
  does), and one that doesn't.

Checks:
- Level check (a `LevelCheck.PlayerWeapon.cs` partial): on every map, headless:
  - the player's entry is the synthetic sprite of each weapon the WAD has art for, made ready in turn
    (as `LevelCheck.WeaponIcon.cs` does);
  - stock with the option off, while dead, and in frames H–W;
  - teammates' entries in the co-op section (`LevelCheck.Players.cs`).
  With a real renderer, on the check's map:
  - `CompareSprite` on the player holding the shotgun, the rocket launcher and the BFG, every pixel
    against the composite through the CPU light, as the other sprites;
  - one fuzzed (blur sphere) against the CPU fuzz;
  - one in a teammate's translation.
- The sim untouched: `dotnet test` with the route checksums, routes and demos unchanged.
- Load time: `--level-check` prints the slowest map. It must stay under 1 s with the composites built
  (SPEC §9).
- The usual: build, `dotnet test`, format, `--level-check` headless on DOOM1, doom2.wad, TNT and the
  synthetic IWAD (no PLAY art there: it must just draw stock, or its own PLAY if it has one), and under
  lavapipe on DOOM1 E1M1 and Doom II MAP01.

Screenshots for the user (screenshots/t6.14-player-weapon/game/, with an index.html side by side as
t6.13s did; WAD-derived, never commit):
- Doom II MAP01 at the default zoom and at `--level-zoom=320`, every weapon idle and firing (use `idkfa`
  and BT_CHANGE ticcmds as the mock-up did: `cmd 0 0 0 BUTTONS` with BUTTONS = 4 | slot << 3);
- the player turned through all 8 rotations with the rocket launcher;
- a 2-player co-op shot (`--level-players=2`) showing the teammate's weapon;
- the blur sphere;
- a dark room;
- the option off.

Done when: the checks pass, the screenshots are in place, the SPEC §12 entry and SPEC §6.3 #4 / §7.6
(the weapon now also shows on the player) are updated, CLAUDE.md's level scene arguments and
level-check paragraphs mention the option and the check (edit AGENTS.md; CLAUDE.md is a symlink to it,
so never `sed -i` it), and a hand-play item is added under T6.13a ("Player weapon: at the default zoom,
can you tell the weapons apart, including a teammate's? do the back views read?"). Tick T6.14d, commit,
and tell the user where the screenshots are. Do not merge to main.
```

## Session 5 — T6.14e Polish (optional)

```
Task T6.14e of docs/plans/player-weapons.md (read the whole plan first; this session is "Session 5").
Only after the user has played T6.14d and picked from the list below; do only what they picked. Branch
`t6.14e-polish` from `t6.14-player-weapons`.

Candidates (each behind the same option, presentation only, each with checks as T6.14d's):
1. Weapon change: while the ready weapon lowers or the new one rises (`psprites[ps_weapon]` in the
   weapon's lower or raise states, its `sy` going 32→128 and back), tip the held weapon down by up to
   about 45° in proportion. That needs composites at a few tip angles (say 4 steps) or a per-frame
   composite cache.
2. Muzzle flash colour per weapon in frame F: the plasma rifle's blue, the BFG's green, the rocket
   launcher's larger flash, using colour ramps of the palette; the chainsaw with no flash.
3. Firing stance per weapon: the chainsaw held lower and forward, the BFG two-handed. Pose offsets per
   weapon class on top of the pose.
4. Fist: the empty hands in fists (a small fist overlay of our own art) if the user found the empty hands
   odd.
5. Dropped weapon: in the death frames H–I, the held weapon falling (vanilla's corpse has the rifle).

Done when: the picked items work with their checks, the SPEC §12 entry is extended, the screenshots
are updated, and T6.14e is ticked with what was done and what was left.
```

---

## Out of scope

- **Monsters holding their weapons** (zombiemen, shotgun guys, chaingunners) differently: their
  sprites already show their own weapons.
- **ZDoom/Zandronum weapon skins and `S_SKIN`:** the vanilla-only rule stays (SPEC §12 "P7").
- **Third-person first-person-weapon sprites:** no first-person view exists (SPEC §6.3 #4).
