# IsoDoom

An isometric top-down remake of Doom in Godot 4 + C#, heavily inspired by [Isowulf mod for Wolfenstein 3D](https://www.moddb.com/mods/isowulf). I really enjoyed Isowulf, and since I've been doing a lot of AI development at my day job, it got me thinking: Would Doom be just as fun to play like this? And how much work would it take to create something similar using AI coding tools?

[![IsoDoom demo video](https://img.youtube.com/vi/wHehnQ2Vzbo/maxresdefault.jpg?v=2)](https://www.youtube.com/watch?v=wHehnQ2Vzbo)

Builds for Windows, MacOS and Linux (x86_64, and arm64 for the Raspberry Pi 5) are provided. I'm actively testing it on a SteamDeck with the thumbsticks as well as on a laptop with the keyboard and mouse. You need to provide a shareware or registered WAD file, both Doom1 and Doom2 WADS are supported. Without one, the game offers to download the shareware episode (id Software's own release, from the idgames archive).

## Playing

Download the zip for your system from the [releases](https://github.com/joepadmiraal/isodoom/releases) and unpack it. Each zip holds a `README.txt` ([docs/player-readme.txt](docs/player-readme.txt)) with the details; in short:

- **The WAD.** Put `DOOM1.WAD`, `DOOM.WAD`, `DOOM2.WAD`, `TNT.WAD` or `PLUTONIA.WAD` next to the game (or in a `wads` folder beside it), or keep it in a Steam or GOG install of Doom, which the game finds itself. With several, a menu lets you pick one; with none, the same menu lets you browse for the file or download the shareware episode. From a command line: `IsoDoom.x86_64 -- -iwad /path/to/DOOM2.WAD` (`-file my.wad` adds a PWAD).
- **Keyboard and mouse.** W/A/S/D walk relative to the screen, Shift runs, the mouse aims, the left button fires, E or Space uses, 1–8 or the wheel pick a weapon, Tab the automap, Escape the menu, F6/F9 quicksave/quickload.
- **Gamepad.** The left stick walks (click toggles run), the right stick aims, the right trigger fires, A uses, LB/RB change weapon, Y the automap, Start the menu, Back pauses.
- Every action can be rebound in Options → More Options → Controls; F1 lists the other keys.
- **macOS.** The app is not notarized: open it the first time with right-click → Open (on macOS 15, System Settings → Privacy & Security → Open Anyway), or run `xattr -dr com.apple.quarantine IsoDoom.app`.

### Arcade cabinet

For a twin-stick cabinet, where both hands are on the sticks and the buttons are out of reach, start the game with `--arcade` (after `--`):

```bash
./IsoDoom.arm64 -- --arcade                      # e.g. on a Raspberry Pi 5
./IsoDoom.arm64 -- --arcade -iwad DOOM2.WAD
```

Arcade mode changes four things:

- **The right stick fires while it aims.** Push it in a direction to turn and shoot; let go to stop. The mouse never fires by itself.
- **Ammo never runs out.** Firing uses none, so you never have to change weapon for lack of it.
- **Walking into a door opens it**, as the use button would. A locked door without its key refuses once per push. Switches and other usable lines still need the use button.
- **The aim assist reaches 25° either side** (5° normally). An 8-way stick only faces in 45° steps, so this catches a target that lies between two directions. `--level-aim-assist=DEG` after `--arcade` still overrides it.

Changing weapon and coming back after dying still need a button (LB/RB and A by default; every action can be rebound under Options → More Options → Controls). In a network game the host's choice of infinite ammo, door opening and aim assist holds for every player, while the right stick's firing is each player's own.

### Demos

`-playdemo` plays a demo recorded with the original Doom, with the original game's rules, and quits at its end:

```bash
./IsoDoom.x86_64 -- -iwad DOOM2.WAD -playdemo run.lmp    # a .lmp file
./IsoDoom.x86_64 -- -iwad DOOM2.WAD -playdemo run        # run.lmp, else the WAD's lump RUN
./IsoDoom.x86_64 -- -iwad DOOM1.WAD -playdemo demo1      # a demo built into the WAD
```

- **`.lmp` files.** A name ending in `.lmp` is that file; otherwise the game tries `NAME.lmp`, then a demo lump in the WAD, then the file `NAME` itself. Relative paths are taken from the current directory (on Windows, pass an absolute path). A demo recorded with a PWAD needs the same PWAD: `-file mymod.wad -playdemo run.lmp`.
- **Built-in demos.** Every IWAD carries the demos id Software recorded: `demo1` to `demo3` (The Ultimate Doom also has `demo4`). The title screen cycles only its pages and does not play them, so start them with `-playdemo demo1` and so on.
- The demo picks the map, skill, game modes and players itself; your display options (camera, HUD, fog of war, …) still apply. Pause works while it plays, and starting a new game or loading a save from the menu ends it.
- A demo only stays in step with the release of Doom (and IWAD version) it was recorded on. Any demo from v1.666 to v1.9 plays, with a warning in the log when its version differs from the IWAD's own demos; with `-gameversion` (e.g. `-gameversion 1.666`) only that version's demos play. A demo that isn't found, is cut short or is for a map the WAD lacks is refused with a message.

## Licence

IsoDoom is free software under the GNU General Public License, version 2 or (at your option) any later version: see [COPYING](COPYING). The third-party code it includes and its licences are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Doom is a trademark of id Software; IsoDoom is not affiliated with id Software.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the toolchain, the checks a pull request must pass and how to run them locally, the code style and the rules for the simulation and game data.

## Running outside the dev container

To run on the host, export a Linux build in the container and run it on the host (any x86_64 Linux). The export bundles the .NET runtime, so the host needs only a Vulkan GPU driver.

1. In the container, export (or re-export after a code change):

   ```bash
   godot --headless --export-release "Linux" export/linux/IsoDoom.x86_64
   ```

   This needs the export templates. Build the dev container with `INSTALL_EXPORT_TEMPLATES=true`.

2. On the host, from the repo root (the workspace is a bind mount, so the build is already there):

   ```bash
   export/linux/IsoDoom.x86_64 -- --level E1M1 -iwad wads/DOOM1.WAD   # level scene
   export/linux/IsoDoom.x86_64 -- --level-check -iwad wads/DOOM1.WAD  # level check
   export/linux/IsoDoom.x86_64 -- --viewer-check -iwad wads/DOOM1.WAD # viewer check
   ```

   On a hybrid-GPU laptop, put `__NV_PRIME_RENDER_OFFLOAD=1` before the command to use the NVIDIA GPU, or pass `--gpu-index N` before `--`. Godot's startup log names the device it picked.

Bring your own WAD: `wads/` is gitignored and no game data is in the repo.
