# IsoDoom

An isometric top-down remake of Doom in Godot 4 + C#, heavily inspired by [Isowulf mod for Wolfenstein 3D](https://www.moddb.com/mods/isowulf). I really enjoyed Isowulf, and since I've been doing a lot of AI development at my day job, it got me thinking: Would Doom be just as fun to play like this? And how much work would it take to create something similar using AI coding tools?

[![IsoDoom demo video](https://img.youtube.com/vi/wHehnQ2Vzbo/maxresdefault.jpg?v=2)](https://www.youtube.com/watch?v=wHehnQ2Vzbo)

Builds for Windows, MacOS and Linux are provided. I'm actively testing it on a SteamDeck with the thumbsticks as well as on a laptop with the keyboard and mouse.

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
