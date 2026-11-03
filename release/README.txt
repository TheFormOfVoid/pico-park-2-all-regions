All Regions for PICO PARK 2
===========================

The public lobby list shows the lobbies of every region at once, each with its region and ping,
instead of one region at a time.

- Lobbies from all regions appear together, with the region and ping (green, yellow, red) after the
  host's name. "SEARCHING 4/11" in the corner shows the other regions being searched.
- Click any lobby to join it. If it is in another region, the game switches to that region and joins it
  for you. Your region setting stays on the new region afterwards, as if you had changed it yourself.
- The list keeps itself up to date while it is open, and the Update button refreshes every region.

The mod only changes the lobby list. It does not touch gameplay.


Install
-------
1. Unzip this folder anywhere.
2. Close PICO PARK 2.
3. Run Setup.bat and choose 1 (Install or update).
   It finds the game through Steam and installs BepInEx 6 (the mod loader) if it isn't there yet.
4. Start the game. The first start after installing BepInEx takes a few minutes while it prepares the
   game's code; later starts are normal.

Requires the Steam version of PICO PARK 2 on Windows.


Turn mods off, or uninstall
---------------------------
Run Setup.bat again:
  2  Turn mods off: the game starts without any mods; nothing is deleted.
  3  Turn mods back on.
  4  Uninstall: removes the mod, and BepInEx too if Setup.bat installed it, leaving the game folder
     as Steam installed it.


Settings
--------
After the first start with the mod, the settings are in
  <game folder>\BepInEx\config\modwright.picopark2.allregions.cfg
  Enabled          turn the mod off without uninstalling (true / false)
  RescanSeconds    how often the other regions are searched again while the list is open (default 10)
  ParallelRegions  how many regions are searched at the same time (default 3)
  DetailedLog      write every step to the log (default true)


Reporting a problem
-------------------
Please send these two files from <game folder>\BepInEx\ with a short description of what happened:
  AllRegions.log     what the mod did, step by step (AllRegions.prev.log is the session before)
  LogOutput.log      BepInEx's log
