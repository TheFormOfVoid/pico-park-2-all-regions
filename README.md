# All Regions for PICO PARK 2

A mod that shows the public lobbies of **every region at once** in PICO PARK 2, each with its region and
ping, so you no longer have to switch regions one by one to find a game.

- Lobbies from all regions appear together, with the region and ping (green, yellow, red) after the host's
  name. "SEARCHING 4/11" in the corner shows the other regions being searched.
- Click any lobby to join it. If it is in another region, the game switches to that region and joins it
  for you. Your region setting stays on the new region afterwards, as if you had changed it yourself.
- The list keeps itself up to date while it is open, and the Update button refreshes every region.

It only changes the lobby list. It does not touch gameplay.

## Install

1. Download `AllRegions-<version>.zip` from [Releases](../../releases) and unzip it anywhere.
2. Close PICO PARK 2.
3. Run `Setup.bat` and choose **1 (Install or update)**. It finds the game through Steam and installs
   [BepInEx 6](https://github.com/BepInEx/BepInEx) (the mod loader) if it isn't there yet. The download
   is checked against a pinned SHA-256.
4. Start the game. The first start after installing BepInEx takes a few minutes while it prepares the
   game's code; later starts are normal.

Requires the Steam version of PICO PARK 2 on Windows.

## Turn mods off, or uninstall

Run `Setup.bat` again:

| Choice | What it does |
| --- | --- |
| 2 Turn mods off | The game starts without any mods. Nothing is deleted. |
| 3 Turn mods back on | |
| 4 Uninstall | Removes the mod, and BepInEx too if `Setup.bat` installed it, leaving the game folder as Steam installed it. A BepInEx that was there before (for other mods) is left alone. |

## Settings

`<game folder>\BepInEx\config\modwright.picopark2.allregions.cfg`, created on the first start:

| Setting | Default | |
| --- | --- | --- |
| `Enabled` | `true` | Turn the mod off without uninstalling. |
| `RescanSeconds` | `10` | How long after a search ends the other regions are searched again while the list is open. |
| `ParallelRegions` | `3` | How many regions are searched at the same time. |
| `DetailedLog` | `true` | Write every step to `AllRegions.log`. |

## Reporting a problem

Open an issue with a short description of what happened and these files from `<game folder>\BepInEx\`:

- `AllRegions.log`: what the mod did, step by step, with times (`AllRegions.prev.log` is the session
  before);
- `LogOutput.log`: BepInEx's log.

## How it works

- While the list is open, the mod connects to the other regions a few at a time with the game's own
  connection code, reads their room lists, and turns them into list entries with the game's own code.
  Each connection is closed as soon as its list has arrived.
- Those entries are added to the list the game builds for your region, and each row gets a region and
  ping label.
- Picking a lobby from another region does what you would do by hand: it sets the region the region
  window sets, lets the game reload the list for that region, and presses the lobby's button once the
  new list is on screen, so the game joins it through its normal path.
- The search pauses while the list is busy, stops when you pick a lobby or leave the list, and every
  hook into the game catches its own errors so a problem in the mod can't break the game.

## Building

You need Windows, the [.NET SDK](https://dotnet.microsoft.com/download) (6 or later) and the game with
BepInEx 6 be.788 installed and started once (that creates `BepInEx\interop`, which the project builds
against; nothing from the game is copied into this repository or the output).

```
.\build.ps1
.\build.ps1 -GameDir "D:\Steam\steamapps\common\PICO PARK 2"
```

It builds `AllRegions.dll` and the player download `dist\AllRegions-<version>.zip` (the DLL, `Setup.bat`,
`AllRegions-Setup.ps1` and `README.txt` from `release\`).

## Licence

MIT, see [LICENSE](LICENSE). BepInEx (LGPL-2.1) is downloaded from its official builds at install time and
is not part of this repository. Not affiliated with TECOPARK; PICO PARK is their trademark.
