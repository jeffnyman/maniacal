# Building an interpreter for Maniac Mansion

Maniac Mansion is two things: a game written in SCUMM scripts, and an engine that runs them. The
scripts, graphics, sounds and costumes are data; the engine (Lucasfilm called it SPUTM) is the
program that loads that data, executes the scripts and turns them into pictures, sound and
responses to the player. To play the game on new hardware you rewrite the engine and leave the
data alone. That is what ScummVM does, and what [Maniacal](../README.md) does in C#.

This guide describes how such an interpreter is put together, layer by layer, and where each layer
is documented in this resource: as the original C64 implementation in [`c64-engine/`](c64-engine/), as
ScummVM's implementation in `engines/scumm`, and as the planned Maniacal component. It assumes you
have read [how-maniac-mansion-works.md](how-maniac-mansion-works.md) or know the game's model of
rooms, objects, actors and scripts.

## The versions you have to handle

Maniac Mansion shipped on three engine generations. Each release must be identified (Maniacal
uses ScummVM's MD5 table, see `src/Maniacal.Scumm/Detection`) and then run with the right mix of
parts:

| | Version 0 (C64, Apple II) | Version 1 (DOS 1987) | Version 2 (DOS/Amiga/ST 1988) |
|---|---|---|---|
| Files | Two disk images (`maniac1.d64`, `maniac2.d64`); the index is in fixed sectors | `00.LFL` index, `01.LFL`…`52.LFL` rooms | `00.LFL` index, `01.LFL`…`53.LFL` rooms |
| Encoding | None | Every byte XOR `0xFF` | Every byte XOR `0xFF` |
| Index magic (first word of `00.LFL`, decoded) | `0x0132` (C64), `0x0032` (Apple II) | `0x0A31` | `0x0100` |
| Script opcodes | Version 0 table | Version 2 table | Version 2 table |
| Room graphics | Character tiles + color map + mask characters (C64 multicolor text mode) | The same C64-style tiles, drawn on a PC | 16-color strips, run-length encoded, plus a separate mask |
| Costumes | V0 costume format | Classic format, version 1 variant | Classic format |
| Verbs and sentences | V0 sentence system | V2 verb system | V2 verb system |
| Sound | SID chip music and effects | PC speaker / PCjr | PC speaker / PCjr |
| Copy protection | On the disk (loader); no door code | Door code | Door code |
| Script, variable and object numbers | As in [scripts/](scripts/README.md) | Renumbered (scripts mostly +5 or +6; variables and objects irregularly) | As version 1 |

The practical order of work follows from the table. Version 2 shares the most code with later
SCUMM versions and is the best exercised path in ScummVM, so start there. Version 1 then needs only
the C64-style graphics and costume variant, under the same script interpreter. Version 0 needs its
own opcode table, verb system and walking code, and reuses version 1's graphics. Maniacal's
[ROADMAP.md](../ROADMAP.md) follows this order.

## The layers

An interpreter for these versions has roughly ten parts. They are listed from the bottom up. Each
depends only on the ones above it in the list.

### 1. Game data access

Open the files, undo the encoding, and for version 0 read files out of the disk images.

- Version 1 and 2: each `.LFL` file is XOR-ed with `0xFF`. Decode on read.
- Version 0: the images are standard 1541 disk images (35 tracks, 17 to 21 sectors per track).
  The game does not use the disk's directory. It reads rooms by track and sector from tables in the
  index. See [scripts/c64-disk-layout.txt](scripts/c64-disk-layout.txt) and the disk modules in
  [c64-engine/disk/](c64-engine/disk/). The boot loader and copy protection
  ([c64-loader/](c64-loader/)) only matter on real hardware. An interpreter reads the sectors directly.
- ScummVM: `file.cpp` (`ScummFile`, `ScummDiskImage`); `file_nes.cpp` for the NES.

### 2. The resource manager

The index file (room 0) lists, for every global object, its owner and state, and for every room,
costume, script and sound, which room file holds it and at what offset. The resource manager loads
resources on demand, caches them, and locks the ones in use.

- Limits from the index (ScummVM `resource_v2.cpp`, `readClassicIndexFile`): version 0 has 256
  global objects, 55 rooms, 25 costumes, 160 scripts and 70 sounds (the demo: 55 scripts, 40 sounds).
  DOS version 1 has 800 objects, 55 rooms, 35 costumes, 200 scripts and 100 sounds. Version 2 stores
  its counts in the index (`readEnhancedIndexFile`). The English DOS release has 780 objects, 61 room
  slots, 40 costumes, 179 scripts and 120 sounds.
- C64: [c64-engine/resources/](c64-engine/resources/) (memory manager, resource loader, room
  loader, decompressor) and [c64-engine/include/rsrc_metadata.inc](c64-engine/include/rsrc_metadata.inc).
- ScummVM: `resource.cpp`, `resource_v2.cpp`, `room.cpp`.

### 3. Room and object graphics

A room is a background wider than the screen, drawn in vertical 8-pixel strips; objects are
patches drawn over it in some of their states; a mask says which pixels sit in front of actors.
The renderer keeps a virtual screen, marks strips dirty as things change and redraws only those.

- C64: the background is a multicolor character set plus a screen map, a color map and a mask
  character set. See [c64-engine/graphics/](c64-engine/graphics/) and
  [c64-engine/resources/room_gfx_rsrc.asm](c64-engine/resources/room_gfx_rsrc.asm).
- ScummVM: `gfx.cpp` (`GdiV1` for versions 0 and 1, `GdiV2` for version 2, the `VirtScreen`),
  `palette.cpp`, `camera.cpp` for scrolling.

### 4. Actors and costumes

Actors are the characters on screen. Each has a costume (its animation data), a position, a facing,
a walk target and the current animation of each limb. Costume decoding and drawing is its own
subsystem: frames are built from cels per limb, mirrored for facing left, and clipped by the mask.
These versions draw actors at a single size; there is no scaling.

- C64: [c64-engine/actors/](c64-engine/actors/); actors are drawn as multiplexed hardware sprites
  driven by the raster interrupt chain in [c64-engine/graphics/irq_handlers.asm](c64-engine/graphics/irq_handlers.asm).
  An interpreter draws them into the frame buffer instead.
- ScummVM: `actor.cpp` (`Actor`, `Actor_v3`, `Actor_v2`, `Actor_v0`), `costume.cpp`
  (`ClassicCostumeLoader`/`Renderer`, `V0CostumeLoader`/`Renderer`).

### 5. Walkboxes and path finding

The floor of each room is a set of convex boxes; a matrix says which box leads to which. Walking
to a point means finding the box sequence, then the points where boxes meet, then stepping along
the line. Getting this exactly right is what makes actors stop where the original stopped, and
scripts depend on that.

- C64: [c64-engine/walkboxes/](c64-engine/walkboxes/) (search, waypoint planner, snapping) and
  [c64-engine/actors/actor_path_dda.asm](c64-engine/actors/actor_path_dda.asm) (the stepping).
- ScummVM: `boxes.cpp`, and the walking code in `actor.cpp`.

### 6. The script interpreter

The heart of the engine. Scripts run in slots, a small fixed pool of cooperative threads. Each
frame the engine resumes every running slot until it yields (`breakHere`, a delay, waiting for an
actor or message). State lives in global variables, bit variables, object states and owners, and
a few local variables per slot. The interpreter also handles cutscenes (which freeze the player's
control and can be skipped with an override), the sentence queue, and the special scripts the
engine starts itself (room entry and exit, object verb scripts).

- The game's own scripts, disassembled and named: [scripts/](scripts/). Use them to know what a
  script is supposed to do before you debug what your interpreter does with it.
- C64: [c64-engine/interpreter/](c64-engine/interpreter/) (`script_engine.asm` for slots and
  dispatch, `ops_*.asm` for the opcodes) and [c64-engine/include/opcode_handlers.inc](c64-engine/include/opcode_handlers.inc).
- ScummVM: `script.cpp` (slots, `runScript`, `runObjectScript`, entry and exit scripts, cutscenes),
  `script_v2.cpp` (the opcode table for versions 1 and 2), `script_v0.cpp` (version 0), `vars.cpp`
  (which variable numbers the engine itself reads and writes).

### 7. Verbs, sentences, input and text

The bottom of the screen holds the sentence line, the verbs and the inventory. Clicking builds a
sentence (verb, object, preposition, second object). The engine queues it and runs the object's
script for that verb, or the default script when there is none. Text appears in the message line at
the top and stays until it times out or is skipped.

- C64: [c64-engine/ui/](c64-engine/ui/) (verbs and hotspots, sentence building and execution,
  messages, cursor, keyboard and joystick).
- ScummVM: `verbs.cpp` (`initV2MouseOver`, `checkV2MouseOver`, `checkExecVerbs`, and the
  `ScummEngine_v0` versions), `input.cpp`, `string.cpp`, `charset.cpp` (`CharsetRendererV2`) with
  the built-in fonts in `charset-fontdata.cpp`.

### 8. Sound

Version 0 plays music and effects on the SID chip through virtual voices; versions 1 and 2 play
on the PC speaker or the PCjr's three-voice chip.

- C64: [c64-engine/sound/](c64-engine/sound/) and the player listing in
  [c64-engine/listings/music-player-listing.txt](c64-engine/listings/music-player-listing.txt).
- ScummVM: `players/player_sid.cpp`, `players/player_v1.cpp`, `players/player_v2.cpp` and
  `player_v2base.cpp`, `sound.cpp`.

### 9. Saving and loading

A saved game is the whole interpreter state: variables, object tables, actors, script slots,
the current room and camera. The save/load screen itself is a room driven by game scripts, so the
interpreter only provides the operations those scripts call.

- C64: [c64-engine/system/save_game.asm](c64-engine/system/save_game.asm).
- ScummVM: `saveload.cpp`. Matching ScummVM's format lets saves move between the two.

### 10. The main loop and timing

Everything above is driven by one loop that runs at a fixed rate of 60 Hz ticks ("jiffies"). For
versions 0 to 2, ScummVM's `ScummEngine::scummLoop` does this, in order, each frame:

1. Advance the timers and count down the script delays (`decreaseScriptDelay`) and the talk delay.
2. Read input (`processInput`) and update the engine variables the scripts read
   (`scummLoop_updateScummVars`).
3. Run every script slot until it yields (`runAllScripts`), then check the verbs (`checkExecVerbs`)
   and run a queued sentence (`checkAndRunSentenceScript`).
4. Walk the actors (`walkActors`), move the camera (`moveCamera`), update object states.
5. Redraw what changed (`scummLoop_handleDrawing`), draw the actors (`scummLoop_handleActors`),
   apply effects, update the verb highlights (`handleMouseOver`), and present the dirty parts of the
   screen (`drawDirtyScreenParts`).
6. Feed the sound player.

The loop then waits the number of ticks the game asked for (`VAR_TIMER_NEXT`). The original C64
engine splits the same work between a main loop ([c64-engine/system/main.asm](c64-engine/system/main.asm))
and a chain of raster interrupts. An interpreter does it all in one place, and a host such as
Daventry's calls it once per tick.

Timing matters more than it looks. ScummVM notes two cases: the C64 could not always keep 60 ticks
per second, and some scenes (the intro's music against the "A Lucasfilm Games Production" title)
were tuned to that. In DOS V1, the timer counts in steps of three frames.

## Mapping table

| Subsystem | C64 engine | ScummVM | Maniacal (planned) |
|---|---|---|---|
| Detection | n/a | `detection*.cpp`, `scumm-md5.h` | `Maniacal.Scumm.Detection` (done) |
| Files and disk images | [`disk/`](c64-engine/disk/) | `file.cpp` | `Resources` |
| Index and resources | [`resources/`](c64-engine/resources/) | `resource.cpp`, `resource_v2.cpp`, `room.cpp` | `Resources` |
| Room drawing | [`graphics/`](c64-engine/graphics/) | `gfx.cpp`, `palette.cpp`, `camera.cpp` | `Graphics` |
| Actors and costumes | [`actors/`](c64-engine/actors/) | `actor.cpp`, `costume.cpp` | `Actors` |
| Walkboxes | [`walkboxes/`](c64-engine/walkboxes/) | `boxes.cpp` | `Actors` |
| Scripts | [`interpreter/`](c64-engine/interpreter/) | `script.cpp`, `script_v2.cpp`, `script_v0.cpp`, `vars.cpp` | `Script` |
| Objects | [`interpreter/ops_object.asm`](c64-engine/interpreter/ops_object.asm), [`graphics/render_object.asm`](c64-engine/graphics/render_object.asm) | `object.cpp` | `World` |
| Verbs, input, text | [`ui/`](c64-engine/ui/) | `verbs.cpp`, `input.cpp`, `string.cpp`, `charset.cpp` | `Ui` |
| Sound | [`sound/`](c64-engine/sound/) | `players/player_sid.cpp`, `player_v1.cpp`, `player_v2*.cpp`, `sound.cpp` | `Sound` |
| Save and load | [`system/save_game.asm`](c64-engine/system/save_game.asm) | `saveload.cpp` | `Saves` |
| Main loop | [`system/`](c64-engine/system/), [`graphics/irq_handlers.asm`](c64-engine/graphics/irq_handlers.asm) | `scumm.cpp` | `ScummEngine` |

## Porting from ScummVM without porting all of ScummVM

ScummVM builds Maniac Mansion's engine as a chain of subclasses:
`ScummEngine_v0 : v2 : v3old : v3 : v4 : v5 : ScummEngine`. Much of what Maniac Mansion runs
on lives in the version 5 and base classes, alongside code for much later games (iMUSE, HE, SMUSH,
AKOS costumes, 256 colors). When reading a ScummVM function, follow only the branches that
`_game.version <= 2` and `GID_MANIAC` reach, and keep the version as data in one flat engine,
not as a class chain. Where version 0 really behaves differently (scripts, verbs, walking), give it
its own class.

## Checking your work

- **Run headless and deterministically.** A fixed random seed, a fixed tick, scripted input. The
  same run then gives the same frames, so a screenshot or a variable dump can be a test.
- **Name things from the tables.** [scripts/retail/tables/](scripts/retail/tables/) names the
  variables, objects, rooms, scripts and sounds of the C64 release. Use those names in debug output
  and tests ("after script 1, variable 97 is 3: Dave is kid 1"). The DOS releases renumber scripts,
  variables and objects, so translate first. [scripts/README.md](scripts/README.md#the-dos-releases)
  explains how, and ScummVM's `MM_SCRIPT` and `MM_VALUE` macros in `script_v2.cpp` record the
  pairs ScummVM itself relies on.
- **Use the scripts as the specification.** When a scene misbehaves, read the script that drives it
  in [scripts/](scripts/), then the opcode's handler in [c64-engine/interpreter/](c64-engine/interpreter/),
  then ScummVM's.
- **Use the dead ends and walkthrough as play tests.** [game/dead-ends.txt](game/dead-ends.txt) and
  [game/walkthrough.pdf](game/walkthrough.pdf) describe states the game must reach.
- **Compare with ScummVM.** It is the reference: same input, same frames. Its save files are a good
  cross-check of the whole interpreter state.
