IsoDoom
=======

An isometric top-down remake of Doom. It plays the original game's maps,
monsters and rules from your own copy of Doom's data file (the "IWAD");
none of id Software's game data comes with it.

Source code, issues and new releases: https://github.com/joepadmiraal/isodoom


Getting a WAD
-------------

IsoDoom plays any of these IWADs:

  DOOM1.WAD     Doom shareware (episode 1, free)
  DOOM.WAD      Doom (registered) or The Ultimate Doom
  DOOM2.WAD     Doom II
  TNT.WAD       Final Doom: TNT: Evilution
  PLUTONIA.WAD  Final Doom: The Plutonia Experiment

You don't need to put it anywhere in particular. At start the game looks,
in this order, in:

  1. the folder you started the game from;
  2. the game's own folder, and a "wads" folder inside it (put the WAD next
     to IsoDoom.exe or IsoDoom.x86_64, or in a "wads" folder beside it);
  3. the folders in the DOOMWADDIR and DOOMWADPATH environment variables;
  4. Steam's and GOG's Doom installs (every Steam library, a Steam Deck's
     SD card too), and on Linux ~/.local/share/games/doom and
     /usr/share/games/doom.

If it finds several, a menu lets you pick one (keyboard, mouse or pad); it
remembers your choice. If it finds none, the same menu lets you browse for
the file, or download the shareware DOOM1.WAD from the idgames archive
(id Software's own release, checked before use). On macOS, where the game's
folder is inside IsoDoom.app, use the menu or a Steam install.

From a command line, pass the file directly (anything after "--" goes to
the game):

  IsoDoom.x86_64 -- -iwad /path/to/DOOM2.WAD
  IsoDoom.console.exe -- -iwad C:\Games\Doom\DOOM2.WAD   (absolute paths on Windows)
  IsoDoom.x86_64 -- -iwad DOOM2.WAD -file mymod.wad      (add a PWAD)


Controls
--------

Every game action can be rebound in Options > More Options > Controls.

                     Keyboard and mouse        Gamepad
  Walk               W A S D                   left stick
  Run                hold Shift                left stick click (toggles)
  Aim                mouse pointer             right stick
  Fire               left mouse button         right trigger
  Use / open         E or Space                A
  Weapon             1-8, mouse wheel          LB / RB (previous / next)
  Automap            Tab                       Y
  Menu               Escape                    Start
  Pause              Pause                     Back
  Quicksave / load   F6 / F9

In the menus: arrows, Enter and Backspace (or the D-pad or left stick, A
and B, or the mouse: click to pick, right click to go back). Y and N answer
questions.

On the automap: = and - zoom (D-pad up/down), 0 shows the whole map (D-pad
right), F toggles follow (D-pad left), the arrows pan when not following
(right stick), G the grid (right stick click), M marks a spot (X), C clears
the marks (B).

The classic cheats work when typed during play (iddqd, idkfa, ...), and a
pad has them under Options > More Options > Cheats.

F1 lists the remaining keys (camera and display toggles); F3 shows a
status overlay.


Running it
----------

Windows: run IsoDoom.exe. Keep IsoDoom.pck and the data_IsoDoom_* folder
next to it. IsoDoom.console.exe is the same game with its log in a console
window.

Linux: run IsoDoom.x86_64 (x86_64, with a Vulkan driver). Keep IsoDoom.pck
and the data_IsoDoom_* folder next to it.

macOS: the app is not signed with an Apple developer identity, so macOS
blocks it the first time. Right-click IsoDoom.app and pick Open, then Open
again. On macOS 15 and later, if that offers no Open button, try to open it
once, then go to System Settings > Privacy & Security and click "Open
Anyway". Or, in Terminal:

  xattr -dr com.apple.quarantine IsoDoom.app

Settings and saved games are kept in Godot's per-user folder:
  Windows  %APPDATA%\Godot\app_userdata\IsoDoom
  Linux    ~/.local/share/godot/app_userdata/IsoDoom
  macOS    ~/Library/Application Support/Godot/app_userdata/IsoDoom


Licence
-------

IsoDoom is free software under the GNU General Public License, version 2
or (at your option) any later version: see COPYING. It is based on id
Software's Doom source code and includes code from Chocolate Doom and
Nuked-OPL3: see THIRD-PARTY-NOTICES.md. The source code is at the address
above.

Doom is a trademark of id Software; IsoDoom is not affiliated with or
endorsed by id Software, Bethesda or ZeniMax.
