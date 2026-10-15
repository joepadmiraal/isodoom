# IsoDoom

An isometric top-down remake of Doom in Godot 4 + C#, heavily inspired by [Isowulf mod for Wolfenstein 3D](https://www.moddb.com/mods/isowulf). I really enjoyed Isowulf, and since I've been doing a lot of AI development at my day job, it got me thinking: Would Doom be just as fun to play like this? And how much work would it take to create something similar using AI coding tools?

[![IsoDoom demo video](https://img.youtube.com/vi/wHehnQ2Vzbo/maxresdefault.jpg?v=2)](https://www.youtube.com/watch?v=wHehnQ2Vzbo)

Builds for Windows, MacOS and Linux are provided. I'm actively testing it on a SteamDeck with the thumbsticks as well as on a laptop with the keyboard and mouse. You need to provide a shareware or registered WAD file, both Doom1 and Doom2 WADS are supported. Without one, the game offers to download the shareware episode (id Software's own release, from the idgames archive).

## Playing

Download the zip for your system from the [releases](https://github.com/joepadmiraal/isodoom/releases) and unpack it. Each zip holds a `README.txt` ([docs/player-readme.txt](docs/player-readme.txt)) with the details; in short:

- **The WAD.** Put `DOOM1.WAD`, `DOOM.WAD`, `DOOM2.WAD`, `TNT.WAD` or `PLUTONIA.WAD` next to the game (or in a `wads` folder beside it), or keep it in a Steam or GOG install of Doom, which the game finds itself. With several, a menu lets you pick one; with none, the same menu lets you browse for the file or download the shareware episode. From a command line: `IsoDoom.x86_64 -- -iwad /path/to/DOOM2.WAD` (`-file my.wad` adds a PWAD).
- **Keyboard and mouse.** W/A/S/D walk relative to the screen, Shift runs, the mouse aims, the left button fires, E or Space uses, 1–8 or the wheel pick a weapon, Tab the automap, Escape the menu, F6/F9 quicksave/quickload.
- **Gamepad.** The left stick walks (click toggles run), the right stick aims, the right trigger fires, A uses, LB/RB change weapon, Y the automap, Start the menu, Back pauses.
- Every action can be rebound in Options → More Options → Controls; F1 lists the other keys.
- **macOS.** The app is not notarized: open it the first time with right-click → Open (on macOS 15, System Settings → Privacy & Security → Open Anyway), or run `xattr -dr com.apple.quarantine IsoDoom.app`.

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
