# IsoDoom

An isometric top-down remake of Doom in Godot 4 + C#, heavily inspired by [Isowulf mod for Wolfenstein 3D](https://www.moddb.com/mods/isowulf). I really enjoyed Isowulf, and since I've been doing a lot of AI development at my day job, it got me thinking: Would Doom be just as fun to play like this? And how much work would it take to create something similar using AI coding tools?

[![IsoDoom demo video](https://img.youtube.com/vi/wHehnQ2Vzbo/maxresdefault.jpg?v=2)](https://www.youtube.com/watch?v=wHehnQ2Vzbo)

Builds for Windows, MacOS and Linux (x86_64, and arm64 for the Raspberry Pi 5) are provided. I'm actively testing it on a SteamDeck with the thumbsticks as well as on a laptop with the keyboard and mouse. You need to provide a shareware or registered WAD file, both Doom1 and Doom2 WADS are supported. Without one, the game offers to download the shareware episode (id Software's own release, from the idgames archive).

## Playing

1. Download the zip for your system from the [releases](https://github.com/joepadmiraal/isodoom/releases) and unpack it.
2. Put `DOOM1.WAD`, `DOOM.WAD`, `DOOM2.WAD`, `TNT.WAD` or `PLUTONIA.WAD` next to the game (or in a `wads` folder beside it), or keep it in a Steam or GOG install of Doom, which the game finds itself. With none, the game offers to download the shareware episode.
3. Run `IsoDoom.exe`, `IsoDoom.x86_64`, `IsoDoom.arm64` or `IsoDoom.app`. On macOS the app is not notarized: open it the first time with right-click → Open.

W/A/S/D walk, the mouse aims and fires; on a gamepad the left stick walks and the right stick aims. F1 lists the other keys.

The **[player guide](docs/player-readme.txt)** (the `README.txt` in each zip) covers the rest: where the game looks for WADs, the full controls, arcade mode for a twin-stick cabinet, multiplayer (hosting, joining, firewalls), playing demos, running on a Raspberry Pi, and where settings and saves are kept.

## Licence

IsoDoom is free software under the GNU General Public License, version 2 or (at your option) any later version: see [COPYING](COPYING). The third-party code it includes and its licences are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Doom is a trademark of id Software; IsoDoom is not affiliated with id Software.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the toolchain (and running a build outside the dev container), the checks a pull request must pass and how to run them locally, the code style and the rules for the simulation and game data.
