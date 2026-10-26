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
  IsoDoom.x86_64 -- -iwad DOOM2.WAD -playdemo run.lmp    (watch a demo, then quit)

"-playdemo" plays a demo recorded with the original Doom (a .lmp file, or
a demo in the WAD such as "-playdemo demo1") with the original game's
rules, then quits. Pause works while it plays. A demo the game can't play
correctly (another version of Doom, or a damaged file) is refused with a
message.


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

Arcade mode, for a twin-stick cabinet: start the game with --arcade (on a
Raspberry Pi, for example: ./IsoDoom.arm64 -- --arcade). The right stick
then fires while it aims, ammo never runs out, walking into a door
opens it, and the aim assist reaches 25 degrees either side, so an 8-way
stick hits what lies between its directions. In a network game the host's choice counts for everyone, but the
right stick's firing is each player's own.

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

Your name: Main menu > Multiplayer > Player Name, press Enter, type it
(up to 15 letters, digits or signs), press Enter. Left empty, it is your
computer's user name. The lobby shows every player's name.

Host a game: Main menu > Multiplayer > Host Game. Pick the mode (co-op,
deathmatch or altdeath), the map, the skill, monsters on or off, respawning
and fast monsters, a time limit (deathmatch), the number of players and the
port, then Start Hosting. The game remembers these for next time. The lobby
shows the addresses the others can join you at (such as
"HOSTING ON 192.168.1.23:5029") and who has joined (with their colour and
ping); pick Start Game once everyone is in (two players at least). Escape
leaves the lobby.

Join a game: Main menu > Multiplayer > Join Game. The page lists the games
hosted on your network (under LOCAL GAMES) by itself: pick one to join it
(with a pad, A). A game you can't join is greyed, with the reason (another
version of IsoDoom, another IWAD or other PWADs, already started, or full).
If yours isn't listed (the host is on another subnet, a guest Wi-Fi, behind
a firewall, or on the internet), pick Address, type the address the host's
lobby shows (an IP address or a host name, with ":port" after it if the
host did not use the default port, 5029), press Enter. The last five
addresses you joined are listed at the bottom; with a pad, pick one with A.
You wait in the lobby until the host starts the game.

Over the internet, the host's router must forward the game's port (UDP
5029 by default, or the port picked on the Host Game page) to the host's
computer, and the host tells the others its public IP address. On a home
network (LAN) no forwarding is needed: Join Game finds the host, or use the
host's local address (such as 192.168.1.20).

Firewalls: the host's computer must let IsoDoom accept connections (UDP on
the game's port, and on 5028 to be found by Join Game). The first time you
host, your system may ask:

  Windows  "Windows Security Alert: Windows Defender Firewall has blocked
           some features of this app" for IsoDoom.exe. Tick "Private
           networks" (a home network; tick "Public networks" too only if
           Windows calls your network public) and pick Allow access (this
           needs an administrator). If you picked Cancel, IsoDoom is
           blocked: Control Panel > Windows Defender Firewall > Allow an app or
           feature through Windows Defender Firewall > Change settings, and
           tick IsoDoom (or "Allow another app..." and pick IsoDoom.exe).
           Joining usually asks nothing; if it does, allow it too.
  macOS    "Do you want the application "IsoDoom.app" to accept incoming
           network connections?" (only when the macOS firewall is on): pick
           Allow. IsoDoom is not signed by a registered developer, so macOS
           may ask again after you update it. Changed your mind: System
           Settings > Network > Firewall > Options, set IsoDoom to "Allow
           incoming connections". The first time Join Game looks for games
           (macOS 15 and later), macOS also asks whether IsoDoom may "find
           and connect to devices on your local network": pick Allow, or
           no local games are listed (System Settings > Privacy & Security
           > Local Network turns it back on).
  Linux    nothing asks. If you run a firewall (ufw, firewalld), open the
           ports yourself, e.g. "sudo ufw allow 5028:5029/udp".

Playing on one computer (127.0.0.1) never needs any of this. A third-party
firewall or antivirus has its own prompt: allow IsoDoom there the same way.

From a command line (no menus needed; the game starts on its own once
that many players are in):

  IsoDoom.x86_64 -- -host -players 2 --level E1M1   (host; -deathmatch,
                                                     -altdeath, -timer N,
                                                     -port N also work)
  IsoDoom.x86_64 -- -connect 192.168.1.20           (join; ADDRESS:PORT too)

Saving is off in a netgame, and the pause pauses everyone.

A few maps lack a start for some players (Doom II 1.666's MAP29 has
player 1's only; a deathmatch map needs 4 deathmatch starts). The host
can't start a game on such a map ("the game can't start: no co-op start
for player 2 on MAP29"). When a later map of the game lacks one, the lobby
warns of it ("MAP29 LACKS PLAYER 2'S START: THE GAME WILL END THERE") but
the game starts; it plays up to that map and ends there for everyone ("the
netgame ended: MAP29: no co-op start for player 2"), as the original game
could not go on there either.

For the smoothest game, turn wifi power saving off on every computer
(Windows: the wifi adapter's Power Saving Mode in Device Manager; Linux:
"iw dev wlan0 set power_save off"), or plug one or all of them into the
router with a cable. The host raises the input delay by itself when the
game freezes (and lowers it again once the link is calm), so a freeze
should pass within a second. To fix the delay yourself, start the host's
game from a command line with "-netdelay N" (N in tics of 1/35 s, say 8;
more is smoother but slower to respond), whether it hosts with "-host" or
from the menus' Host Game; the game then keeps it. "-nettimeout SECONDS"
(how long a silent player is waited for) works the same way, on joins too.


Running it
----------

Windows: run IsoDoom.exe. Keep IsoDoom.pck and the data_IsoDoom_* folder
next to it. IsoDoom.console.exe is the same game with its log in a console
window.

Linux: run IsoDoom.x86_64 (x86_64, with a Vulkan driver). Keep IsoDoom.pck
and the data_IsoDoom_* folder next to it.

Raspberry Pi (and other 64-bit ARM Linux): use the linux-arm64 zip and run
IsoDoom.arm64, on a Raspberry Pi 5 with the 64-bit Raspberry Pi OS and its
desktop (its Vulkan driver, Mesa's v3dv, comes with it; keep the system
updated). The game picks the renderer the Pi's graphics chip can run by
itself. A Pi 5 is not fast enough for 60 frames a second at 1920x1080, so
there the game draws the level at half the screen's resolution (Options >
More Options > Video > 3D Resolution: auto is 50 on a Pi; the HUD and menus
stay sharp). A smaller window (Fullscreen off) runs faster still; a higher
3D Resolution gives more detail. Exact Colours on that page is off on a Pi, for
speed: dark colours are then a shade off, which is hard to see.

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
