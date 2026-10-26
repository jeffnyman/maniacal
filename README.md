# Maniacal

Maniacal is a C# implementation of the SCUMM interpreter for Maniac Mansion. It is a port of the
parts of ScummVM's `engines/scumm` that Maniac Mansion uses (SCUMM versions 0, 1 and 2),
restructured as a .NET library on a small host contract of its own, so the same engine runs
headless (as the tests run it) and on the desktop (SDL).

Maniacal is not a ScummVM fork and does not aim to run any other SCUMM game. Zak McKracken uses
the same engine generations and would come nearly for free, but it is not a goal.

## Screenshots

Taken by the headless player from the games' own data, at their native 320×200, from saves the
walkthrough tests make along their routes: the same three scenes in each release.

<table>
<tr><th colspan="3">Commodore 64 (V0)</th></tr>
<tr>
<td align="center" valign="top"><img src="https://raw.githubusercontent.com/jeffnyman/maniacal/main/assets/screenshots/c64-front-porch.png" width="280" alt="The front of the mansion"><br>The front of the mansion</td>
<td align="center" valign="top"><img src="https://raw.githubusercontent.com/jeffnyman/maniacal/main/assets/screenshots/c64-green-tentacle.png" width="280" alt="Green Tentacle's room"><br>Green Tentacle's room</td>
<td align="center" valign="top"><img src="https://raw.githubusercontent.com/jeffnyman/maniacal/main/assets/screenshots/c64-weird-ed.png" width="280" alt="Weird Ed's room"><br>Weird Ed's room</td>
</tr>
<tr><th colspan="3">DOS, 1987 (V1)</th></tr>
<tr>
<td align="center" valign="top"><img src="https://raw.githubusercontent.com/jeffnyman/maniacal/main/assets/screenshots/v1-front-porch.png" width="280" alt="The front of the mansion"><br>The front of the mansion</td>
<td align="center" valign="top"><img src="https://raw.githubusercontent.com/jeffnyman/maniacal/main/assets/screenshots/v1-green-tentacle.png" width="280" alt="Green Tentacle's room"><br>Green Tentacle's room</td>
<td align="center" valign="top"><img src="https://raw.githubusercontent.com/jeffnyman/maniacal/main/assets/screenshots/v1-weird-ed.png" width="280" alt="Weird Ed's room"><br>Weird Ed's room</td>
</tr>
<tr><th colspan="3">DOS, 1988, the enhanced release (V2)</th></tr>
<tr>
<td align="center" valign="top"><img src="https://raw.githubusercontent.com/jeffnyman/maniacal/main/assets/screenshots/v2-front-porch.png" width="280" alt="The front of the mansion"><br>The front of the mansion</td>
<td align="center" valign="top"><img src="https://raw.githubusercontent.com/jeffnyman/maniacal/main/assets/screenshots/v2-green-tentacle.png" width="280" alt="Green Tentacle's room"><br>Green Tentacle's room</td>
<td align="center" valign="top"><img src="https://raw.githubusercontent.com/jeffnyman/maniacal/main/assets/screenshots/v2-weird-ed.png" width="280" alt="Weird Ed's room"><br>Weird Ed's room</td>
</tr>
</table>

## Status

Early, but the DOS releases can be played. Releases are identified by ScummVM's MD5 table. For
both DOS releases the index, rooms (objects, walkboxes), room pictures, object images and
costumes are read, and every script decodes. `maniacal <folder>` opens the game in a window: the
scripts run, change rooms, set the camera and object states, and put, walk and animate actors; the
message line shows what is said, and the verbs, the sentence line and the inventory work with the
mouse and the keys. Kids are picked by clicking, the intro plays, and the kids walk from room to
room and pick things up; dark rooms are black but for the flashlight. Inside the house the game
plays as in ScummVM as far as it has been checked: the kitchen, Edna catching a kid and the
dungeon, the gargoyle's door, and the basement with its lights. The music and sound effects play
as the PCjr made them, or as the PC speaker did with `--speaker`. F5 opens the game's own save
screen; saves are ScummVM's own files (`maniac.s01` for Game A, and so on), so they can be moved
between Maniacal and ScummVM 2.7 or later. A save loaded with `--load` goes through the game's save
room on the way in, as one loaded from ScummVM's launcher does.
As in ScummVM, the copy protection is bypassed: the steel security door stays open, so its code
from the manual is never needed; `--copy-protection` keeps it.

A verb is clicked, then an object; clicking the object again, or clicking the sentence line,
carries the sentence out, as in the original. The right button, Escape or F4 skip a cutscene, and
the period key skips a line of speech.

The player also has tools for looking inside the game: `--room N` writes a room as a PNG with its
objects as the game starts and lists them, `--view N` the 320x200 screen with the camera and
standing actors, `--costume N` a sheet of a costume's figures, `--font` or `--text "words"` the
built-in font, `--script N` or `--room-scripts N` a disassembly, and `--run [steps] --screenshot
file.png` the game headless, with clicks and keys given at chosen steps (`--click step:x,y`,
`--key step:key`), and saves loaded or written (`--load file`, `--save file`, `--saves folder`).
[ROADMAP.md](ROADMAP.md) gives the order of work.

| Release | SCUMM version | Data | State |
|---|---|---|---|
| DOS V1 (1987) | 1 | `00.LFL` to `52.LFL`, XOR-encoded | Playable, with sound and saves |
| DOS V2 "enhanced" (1988) | 2 | `00.LFL` to `53.LFL`, XOR-encoded | Playable, with sound and saves |
| Commodore 64 (1987) | 0 | `maniac1.d64`, `maniac2.d64` disk images | Plays from the kid selection into the house, with the SID's sound and saves |
| NES (1990) | 1 (NES dialect) | `.prg` extracted from the ROM | Later, if at all |

## Layout

| Path | What it is |
|---|---|
| `src/Maniacal.Scumm` | The interpreter: detection, resources, script VM, rooms, actors, walkboxes, costumes, verbs, text, sound, saves. |
| `src/Maniacal.Player` | The `maniacal` command line player on the SDL host. |
| `tests/Maniacal.Scumm.Tests` | Unit tests, and tests that run against the real game data when it is present. |
| `tools` | Generators that port tables from the ScummVM source (`port_md5_table.py`, `port_fonts.py`, `port_opcodes.py`, `port_sound_tables.py`, `port_disk_tables.py`, `port_palettes.py`, `port_v0_verbs.py`, `port_v0_actors.py`, `port_sid_tables.py`, `port_transitions.py`). |
| `docs` | How Maniac Mansion works and how an interpreter for it is built: the commented C64 engine, the game's scripts, the boot loader, the NES data, and the game itself. Start at [docs/README.md](docs/README.md). |
| `assets/screenshots` | The screenshots above. |

The host contract is part of `Maniacal.Scumm`: the engine base class, the graphics, input and
audio drivers it is given and a headless host (`Hosting`, `Input`), screen surfaces and PNG
writing (`Graphics`), game folders and save stores (`Storage`), and the audio sources (`Sound`).
The SDL host is part of `Maniacal.Player` (`Sdl`). These come from
[Daventry](https://github.com/jeffnyman/daventry), the AGI and SCI interpreter, whose 0.1.0-alpha
host layer they were taken from and adapted. Beyond the .NET SDK, the only dependency is SDL3
(`ppy.SDL3-CS`), for the player.

## Building and running

Needs the .NET 10 SDK.

```
dotnet build Maniacal.slnx
dotnet test Maniacal.slnx
dotnet run --project src/Maniacal.Player -- <game folder> [--list] [--scale N] [--fullscreen] [--speaker] [--copy-protection] [--frames N]
dotnet run --project src/Maniacal.Player -- <game folder> --room N [--objects none|initial|all] [--mask] [--zoom N] [--output file.png]
dotnet run --project src/Maniacal.Player -- <game folder> --view N [--camera X] [--actor costume,x,y[,direction]]... [--zoom N]
dotnet run --project src/Maniacal.Player -- <game folder> --costume N [--zoom N] [--output file.png]
dotnet run --project src/Maniacal.Player -- <game folder> --font | --text "words" [--zoom N] [--output file.png]
dotnet run --project src/Maniacal.Player -- <game folder> --script N | --room-scripts N
dotnet run --project src/Maniacal.Player -- <game folder> --run [steps] [--click step:x,y]... [--key step:key]... [--set var=value]... [--skip step]... [--load file] [--save file [--save-version N]] [--saves folder [--target name]] [--screenshot file.png] [--wav file.wav [--speaker]] [--zoom N]
```

## Game data

Maniacal ships no game data. Point it at a folder holding a release. The tests look for the folders
`maniac_mansion_original_DOS.com` (V1), `maniac_mansion_enhanced_DOS.com` (V2) and `maniac_mansion_c64` (V0), in the folder
named by the `MANIACAL_GAMES` environment variable or in any folder above the test binary. The
`.gitignore` keeps game files out of the repository.

The walkthrough tests play the game's winning route, a section at a time, in every release, to
"You won the game!" with Syd, and again by each other kid's own way: Bernard's call to the Meteor
Police, Wendy's memoirs published, Michael's prints for Ed. With `MANIACAL_CHECKPOINTS` naming a
folder, they save there where each section ends (and in the telescope's and Meteor Mess's
close-ups), as ScummVM slot files for the release's target, from slot 31 on (`maniac64.s31`,
`maniac1.s32`, ...; Bernard's own sections from slot 49, Wendy's from 52, Michael's from 55), to
load in ScummVM and compare.

The C64 release's disk images are found by ScummVM's names, `maniac1.d64` and `maniac2.d64`, or by
the bytes each disk starts with.

## Documentation

[docs/](docs/README.md) is a self-contained guide to the game and its engine. It has two reading
paths: [how Maniac Mansion works](docs/how-maniac-mansion-works.md), and
[building an interpreter](docs/building-an-interpreter.md), which maps each part of the original
engine to ScummVM's code and to Maniacal's. ScummVM's `engines/scumm` is the reference the code is
ported from. The ScummVM checkout lives next to this repository and is not part of it.

## Licence

GPL-3.0-or-later, as ScummVM and Daventry are. See [LICENSE](LICENSE). The host layer is derived
from Daventry's, copyright the Daventry contributors. The command line tools in `docs/nes/tools`
carry their own licences next to them.
