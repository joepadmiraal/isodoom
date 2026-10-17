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
     to IsoDoom.exe, IsoDoom.x86_64 or IsoDoom.app, or in a "wads" folder
     beside it);
  3. the folders in the DOOMWADDIR and DOOMWADPATH environment variables;
  4. Steam's and GOG's Doom installs (every Steam library, a Steam Deck's
     SD card too), and on Linux ~/.local/share/games/doom and
     /usr/share/games/doom.

If it finds several, a menu lets you pick one (keyboard, mouse or pad); it
remembers your choice. If it finds none, the same menu lets you browse for
the file, or download the shareware DOOM1.WAD from the idgames archive
(id Software's own release, checked before use).

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


Multiplayer
-----------

Up to four players can play co-op or deathmatch over a network or the
internet. Every player needs the same version of IsoDoom and the same IWAD
(and the same PWADs, in the same order); the game checks both when you
join.

Host a game: Main menu > Multiplayer > Host Game. Pick the mode (co-op,
deathmatch or altdeath), the map, the skill, monsters on or off, respawning
and fast monsters, a time limit (deathmatch), the number of players and the
port, then Start Hosting. The game remembers these for next time. The lobby
shows who has joined (with their colour and ping); pick Start Game once
everyone is in (two players at least). Escape leaves the lobby.

Join a game: Main menu > Multiplayer > Join Game. Pick Address, type the
host's address (an IP address or a host name, with ":port" after it if the
host did not use the default port, 5029), press Enter. The last five
addresses you joined are listed below it; with a pad, pick one with A. You
wait in the lobby until the host starts the game.

Over the internet, the host's router must forward the game's port (UDP
5029 by default, or the port picked on the Host Game page) to the host's
computer, and the host tells the others its public IP address. On a home
network (LAN) no forwarding is needed: use the host's local address (such
as 192.168.1.20). Your firewall may ask whether to let IsoDoom accept
connections the first time you host: allow it.

From a command line (no menus needed; the game starts on its own once
that many players are in):

  IsoDoom.x86_64 -- -host -players 2 --level E1M1   (host; -deathmatch,
                                                     -altdeath, -timer N,
                                                     -port N also work)
  IsoDoom.x86_64 -- -connect 192.168.1.20           (join; ADDRESS:PORT too)

Saving is off in a netgame, and the pause pauses everyone.


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
