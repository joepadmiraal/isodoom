# Plan: vanilla's screens with tall pixels (4:3 aspect correction)

From outside feedback: *"Aspect ratio of the UI is messed up, like most poorly made doom ports.
Drives me crazy when people don't realize that pixels on DOS weren't square."*

**Follow CLAUDE.md's session workflow: one task per session.** In that session, add the task to
`docs/TASKS.md` under M7 as **T7.11** (before T7.10, the M7 acceptance, which then covers it),
with a hand check task **T7.11a** (manual, for the user), then do T7.11.

Nothing here touches `src/Sim/`. The sim, its checksums, saves, routes and demo sync must not
change. If a sim test changes, something went wrong.

---

## T7.11 Vanilla's screens at 4:3

### Problem

Vanilla draws a 320×200 screen (VGA mode 13h), and a 4:3 CRT showed it. 320×200 is 16:10, so
the monitor stretched every pixel to 1.2 times taller than it was wide (`(4/3) / (320/200)`
= `240/200`). id's artists drew for that: `TITLEPIC`, `STBAR`, the menu graphics, the fonts, the
intermission and the finale's pictures all look right only with pixels 5:6 (width:height).

We show that screen with **square pixels**: `HudView.ScaleFor` (`src/Game/HudView.cs`) picks the
largest whole scale at which 320×200 fits the window, with the same scale for x and y. Everything
built from vanilla's screen is then 1/1.2 too flat: the status bar, the message line, the menus,
the title loop's pages, the intermission, the finale and the automap's frame. Round things look
squashed (the face in the status bar, the skull, the `M_DOOM` logo, `TITLEPIC`'s lettering).

The fix is to show vanilla's 320×200 screen at 320×240 proportions: the same scale `k` across,
`1.2 k` down.

### What is affected (and what is not)

All four views that draw vanilla's screen get the same layout:

| View | File | Today |
|---|---|---|
| Status bar, fullscreen HUD, message line, automap title | `src/Game/HudView.cs` | integer `PixelScale`, square |
| Title loop, intermission, finale, pause, menus (`Screens`, `MenuScreens`) | `src/Game/ScreenView.cs` | the HUD's scale, square; `ToScreen` maps the mouse |
| Automap frame | `src/Game/AutomapView.cs`, `LevelScene.AutomapFrame` (`src/Game/LevelScene.Automap.cs`) | the HUD's scale (low detail) or window pixels (high detail) |
| Screen wipe | `src/Game/WipeView.cs` | already window-relative (a column is 1/160 of the window, a row 1/200); **no change** |

Not affected:

- **The level itself** (the iso camera, walls, flats, billboards). It is our own 3D view in map
  units, not vanilla's 320×200 view, and walls and sprites share one scale there. (Vanilla's 3D
  view was stretched 1.2 times too, walls and sprites together, so their proportions to each other
  already match ours.) Stretching sprites alone would make them taller than their walls and hit
  boxes. Out of scope.
- **The IWAD menu** (`src/Game/IwadMenu.cs`): Godot labels and buttons, not vanilla graphics.
- **The free-fly vanilla view** (`fov vanilla; view X Y ANGLE`) and `tools/VanillaRef/compare.sh`:
  these compare 3D renders pixel by pixel with vanilla's 320×200 frame, at square pixels on
  purpose.

### Design

**1. An option, defaulting to 4:3.** Add `hud/aspect` to `src/Game/Settings.cs` on the HUD page
(`"ASPECT"`, values `4:3` and `square`, default `4:3`, argument `--level-hud-aspect=4:3|square`),
applied in `LevelScene.Settings.cs` beside `hud/scale`. Corrected pixels are the right look, so
they are the default. `square` keeps today's layout exactly, for the pixel-exact vanilla
comparisons (below) and for players who prefer it. This is presentation, not a sim tweak, so it is
a setting like `hud/scale` and not a `--level-tweaks` flag.

**2. One layout for every view.** Add a small struct, for example `ScreenLayout` in
`src/Game/ScreenLayout.cs` (Godot types are fine here; keep any pure maths that the tests need in a
Godot-free helper, as `HudScreen` is):

- `ScaleX`, `ScaleY` (viewport pixels per vanilla pixel, floats) and `Origin` (the viewport point
  of vanilla's screen point 0, 0).
- Two layouts, because the two kinds of view need different things to fit:
  - **The HUD** (`ScreenLayout.ForHud`: the status bar, the fullscreen HUD, the message line, the
    automap's frame and title) only has to fit **across** the window. Keep today's scale across
    and make it 1.2 times taller: `ScaleX = k`, `ScaleY = 1.2 k`, with k = `ScaleFor(viewport)`
    as today (the whole scale at which 320×200 fits), or the fixed scale. At 1280×800, k = 4: the
    bar is **1280 wide, as today, and 154 tall** (was 128); at 1920×1080, k = 5: 1600×192. Since
    k fits 200 rows into the height, the bar never takes more than 38.4/200 = 19.2% of the window
    (vanilla's on a CRT: 16%). The other choice, the bar at the full screens' scale, would leave
    it 1067 wide at 1280×800, pillarboxed beside the level for no reason. The scale across stays
    whole, so columns stay exactly as sharp as today; only the rows need point 3's filter.
  - **The full screens** (`ScreenLayout.ForScreen`: the title loop, the intermission, the finale,
    the pause graphic, the menus) must fit **whole**, 320×240 in proportion:
    `k = min(W / 320, H / 240)`, **not rounded down**, at least 1 (at 1280×800, k = 3.33: the
    screen is 1067×800, filling the height and pillarboxed; at 1920×1080, k = 4.5: 1440×1080).
    Rounding down would waste a lot of the window (1280×800 would give k = 3, 960×720). Under the
    sharp-bilinear filter (point 3) a fractional scale stays crisp. A fixed scale N gives
    `ScaleX = N`, `ScaleY = 1.2 N`.

    The menus over the level are then a little smaller than the bar under them (3.33 against 4 at
    1280×800). That is fine: vanilla's menus never lined up with the bar's pixels either.
  - `square`: both as today, `ScaleX = ScaleY = fixedScale` or `ScaleFor(viewport)` (a whole
    number).
- `ToViewport(x, y)`, `ToScreen(Vector2)` (replacing `ScreenView.ToScreen`'s `PixelScale`
  maths), and `Size(w, h)`.

`HudView.ScaleFor` stays, as `square`'s scale. Replace the views' `int PixelScale` with the layout,
or keep `PixelScale` as `ScaleX` where a caller only needs that, but no caller may assume
`ScaleY == ScaleX`. Callers to update (grep `PixelScale`, `ScaleFor`, `ToScreen(`):

- `HudView.Show`: the bar at `size.Y - ST_HEIGHT * ScaleY`, its size `320·ScaleX × 32·ScaleY`;
  the message line and the automap title likewise. `BottomInset` (which keeps the game camera's
  focus above the bar) and `TopInset` use `ScaleY`.
- `ScreenView.Show`, `ComposeInto` and `ToScreen` (the menus' mouse hover and clicks,
  `LevelScene.Menu.cs:162`, `:170`).
- `LevelScene.AutomapFrame` and `UpdateAutomap`: in low detail, frame pixels `ScaleX × ScaleY`,
  so the frame is `W / ScaleX` by `(H - bar) / ScaleY` vanilla pixels. The map's lines then get
  vanilla's 1.2 vertical stretch, as on a CRT. That is the faithful look and matches the bar under
  it. High detail stays at one window pixel per frame pixel (geometry true to the map); its
  `PixelScale` passed to `AmMap.SetFrame` (pan step, line widths) becomes `ScaleX` rounded.
  `AutomapView.Show` takes a `Vector2` scale.
- The overlay's `hud:` line (`LevelScene.cs`, about line 2396): print the aspect and both scales.

**3. Sharp-bilinear filtering.** At `ScaleY = 1.2 k`, nearest filtering makes rows uneven (at
k = 4, rows 5, 5, 5, 5, 4 pixels tall: the fonts' strokes visibly vary). Chocolate Doom's fix is to
scale up by a whole number with nearest filtering, then filter linearly to the final size. Do the
same in one pass with a canvas-item shader (as `WipeView` keeps its shader inline as a string),
shared by the `TextureRect`s of the three views:

```glsl
shader_type canvas_item;
render_mode blend_premul_alpha;
uniform vec2 scale; // viewport pixels per texel (ScaleX, ScaleY)

void fragment() {
    vec2 size = 1.0 / TEXTURE_PIXEL_SIZE;
    vec2 texel = UV * size;
    vec2 range = 0.5 - 0.5 / scale;
    vec2 d = fract(texel) - 0.5;
    vec2 f = (d - clamp(d, -range, range)) * scale + 0.5;
    COLOR = texture(TEXTURE, (floor(texel) + f) / size);
}
```

- The `TextureRect`s then use `TextureFilterEnum.Linear` (the shader relies on the linear sample
  at texel edges).
- **Premultiplied alpha.** `HudScreen.ToRgba` writes see-through pixels as (0, 0, 0, 0), which is
  already premultiplied. Blending in that mode keeps a dark fringe from creeping around the bar's
  and the menus' edges where they meet the level.
- At a whole scale the shader gives nearest's result exactly (no blended pixels), so `square` mode
  and fixed scales in `4:3`'s x look as they do today.
- Mirror it on the CPU (a Godot-free `SharpBilinear` helper) for `ScreenView.ComposeInto` (the
  wipe's start frame and the checks) and the level check's comparisons.

**4. Fixed scales.** `hud/scale` keeps its values (`auto`, 1–8) and sets both layouts' scale
across. With `4:3`, N is the horizontal scale and 1.2 N the vertical. A fixed scale whose 240·N
does not fit the window is clipped at the top as today (the bar stays at the bottom).

### Checks and tools

The vanilla comparisons stay exact by running in `square`:

- `tools/VanillaRef/hud.sh`, `wi.sh`, `fi.sh`, `wipe.sh`: add `--level-hud-aspect=square` to the
  game arguments they pass (they compare 1280×800 shots with vanilla's 320×200 × 4). Check that
  `automap.sh` needs nothing (it compares the sim's frames, not shots).
- The level check (`--level-check`): run its existing HUD, screens, menu, automap and wipe pixel
  comparisons in `square` (set at the check's start, as it turns the wipes off for the rest of the
  check). `LevelCheck.Menu.cs:26`'s click point and `LevelCheck.Automap.cs:336`'s scale go through
  the layout. Then add a `4:3` section:
  - the layouts at 1280×800, 1920×1080, 800×600 and a 21:9 window: each view's rectangle (the
    bar at the bottom, as wide as with `square`, 320·k by 38.4·k, so 1280×154 at 1280×800; the
    full screens whole and centred at `min(W / 320, H / 240)`; the automap frame above the
    bar);
  - with a renderer: the status bar, a menu, the title page and the automap's frame drawn in `4:3`,
    with the pixel at the centre of every vanilla pixel's block against the palette exactly, and
    every other pixel against the CPU's sharp bilinear within ±2 per channel;
  - the menus' mouse: a click at the centre of a skill item's block picks that item (through
    `ToScreen`).
- Unit tests (`tests/IsoDoom.Tests`) for the Godot-free maths: the layout for those window sizes,
  `ToScreen` ∘ `ToViewport` round trips, and `SharpBilinear` at whole scales equal to nearest.
- Screenshots to compare by eye, never committed (WAD-derived):
  `--level E1M1 --level-screenshot=screenshots/aspect-43.png` and the same with
  `--level-hud-aspect=square`; and through the script, `tap Escape; shot …` for the main menu and
  `title; shot …` for `TITLEPIC`.

### Docs

- SPEC §7.6: one sentence saying that vanilla's screen shows at 4:3 (pixels 1.2 times taller than
  wide, as on the CRTs it was drawn for) by default.
- SPEC §12 decisions log: a T7.11 entry recording the default (`4:3`), the HUD at today's width
  and 1.2 times taller, the full screens' fractional auto scale,
  sharp-bilinear filtering, the automap's low detail stretched as vanilla's and high detail true,
  the level's 3D view left alone (and why), and the comparisons run in `square`.
- AGENTS.md (CLAUDE.md is a symlink to it; edit it with the Edit tool, `sed -i` breaks the link):
  the HUD page's list in *Options and settings*, `--level-hud-scale`'s description, a new
  `--level-hud-aspect=4:3|square` beside it, and the comparison tools' notes that they run with
  `square`.

### Done when

- By default the status bar is as wide as today and 1.2 times taller (1280×154 at 1280×800).
- By default, at 1280×800 and 1920×1080, the status bar, the message line, the menus, the title
  loop's pages, the intermission, the finale, the pause graphic and the automap show vanilla's
  pixels 1.2 times taller than wide, crisp (sharp bilinear), with no dark fringe at their edges.
- `--level-hud-aspect=square` (and the HUD page's ASPECT) gives today's look pixel for pixel.
- The mouse picks the menu item under it in both modes.
- The level check, the vanilla comparison tools and `dotnet test` pass, and the formatting and
  analyzers are clean.

## T7.11a 4:3 hand check (manual, for the user)

T7.11 ran headless and under Xvfb only. On a desktop, windowed and fullscreen, at a 16:10, 16:9 and
ultrawide size: compare the title page, the main menu, the status bar's face and a finale text
screen with `square` and `4:3`. Check that the 4:3 ones look like the original on a CRT (round
skull and face, `M_DOOM` in proportion) and that small text is sharp. Also judge the taller bar
(154 of 800 rows at 1280×800, against 128 today) for how much of the level it leaves, and the
menus' scale against the bar's under them.
