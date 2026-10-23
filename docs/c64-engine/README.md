# The Commodore 64 engine

This folder holds a commented reconstruction of the engine that runs the Commodore 64 version of
Maniac Mansion (1987), the first SCUMM interpreter, now called SCUMM version 0. Once the loaders
have finished, the game is one monolithic block of 6502 code in memory, with more than 500
routines. Here that block has been split into modules by subsystem, and every routine has been
named and commented. The source uses KickAssembler syntax.

It is a reading resource. The aim is to explain what the original code does, so that a
reimplementation (such as Maniacal's `src/Maniacal.Scumm`) can be checked against the real
behaviour of version 0, not only against another port.

## Does it reassemble?

No, not as it stands, and it has never been assembled into a working binary. The modules are
documentation first:

- There is no top-level build file. [system/main.asm](system/main.asm) is the closest thing to a
  root, but six of the `interpreter/ops_*.asm` files and [system/unused_code.asm](system/unused_code.asm)
  are not imported by any other file.
- The opcode dispatch table in [include/opcode_handlers.inc](include/opcode_handlers.inc) is a raw
  byte table of handler addresses, not a list of labels. The handlers have to sit at their original
  addresses for dispatch to work.
- Routines are placed with `* = $xxxx` directives at their original run-time addresses. Large data
  blocks that the loader copies into place (character sets, tile definitions, the music player,
  which lives inside a sound resource) are not part of the source.
- There are no output, segment or disk-image directives.

Treat the addresses, labels and comments as the reliable part, and use the original listing in
[listings/](listings/) to check an instruction sequence when it matters. No assembler has been
run over the modules.

## How to read it

The comments are written for readers who do not know the Commodore 64. The header of each file
and each routine says what it does in plain terms, and long routines are followed by pseudocode.
The code itself is easier to follow with a little background:

- **6502 assembly.** You need to recognise the instructions, not write them. The 6502 has three
  8-bit registers (A, X, Y), a 256-byte stack at `$0100`, and a "zero page" (`$0000`-`$00FF`)
  that is faster to address, so the engine keeps its hottest variables and pointers there.
  Pointers are 16-bit and little-endian, and there are no multiply or divide instructions, which
  is why so much of the code shifts and adds. The engine also uses self-modifying code a lot: it
  patches the operand of a `JSR`, `LDA` or `STA` and then runs it.
- **The chips.** The **VIC-II** makes the picture (text and character modes, eight hardware
  sprites, raster interrupts). The **SID** makes the sound (three oscillator voices and one
  filter). The two **CIA** chips handle timers, the keyboard and joystick (CIA1), and the serial
  bus to the disk drive (CIA2).
- **Memory-mapped registers.** The CPU talks to the chips by reading and writing addresses in
  `$D000`-`$DFFF`. The same range can be switched to RAM through the CPU port at `$0001`, and the
  engine does this all the time ("map I/O in / map I/O out") because it keeps code and data in the
  RAM underneath. The names of the registers are in [include/registers.inc](include/registers.inc).
- **Raster interrupts.** The VIC-II can raise an interrupt when the beam reaches a given scan
  line. The engine chains 17 of these per frame to reuse four hardware sprites further down the
  screen ("multiplexing") and to switch display modes between the room and the interface.
- **KickAssembler.** `#import "file"` includes another source file, and `#importonce` stops a file
  from being included twice. `.label` names an address, `.const` names a value, `.byte` emits
  data, and `* = $xxxx` sets the address where the code that follows is placed.

If you have no time to learn any of this, the comments and the pseudocode are enough to follow
the logic.

## Engine and interpreter

All of the game logic lives in the game's scripts: the title screen, the intro, every puzzle and
even the save/load screen. A script says things like "make Dave walk to the door" or "if the
microwave is running, play the microwave sound". The scripts are disassembled in
[../scripts/](../scripts/).

The code here is what turns those scripts, and the player's input, into pictures and sound. It
falls into two parts:

1. **The audiovisual engine**: disk access, memory and resource management, room and actor
   graphics, pathfinding, input, the user interface and sound. Everything that knows about the
   hardware.
2. **The interpreter**: the script virtual machine. [interpreter/script_engine.asm](interpreter/script_engine.asm)
   schedules and dispatches, and the `interpreter/ops_*.asm` files hold the opcode handlers, each
   of which reads its operands from the script and calls into the engine. The `ops_` prefix marks
   every file that is part of the interpreter.

### Resources and their representations

The engine calls the game's data **resources**:

| Resource | What it is |
|---|---|
| **Room** | A self-contained area of the game world: background graphics, objects, walkboxes, an entry and an exit script, and the sounds and scripts it carries. |
| **Costume** | The graphics and animations of a character. |
| **Object** | Anything in a room the player can interact with that is not a character. Objects live inside rooms and are copied out when picked up. |
| **Script** | Bytecode for the virtual machine: global scripts, room scripts, and verb handlers attached to objects. |
| **Sound** | A sound effect or a piece of music. |

The vocabulary borrows from film-making: locations, cast, props and direction. The hardware limits
how many of these can be active at once, so the engine works through a second layer of
**representations**, each with a fixed number of slots:

| Representation | Represents | Slots |
|---|---|---|
| **Actor** | A costume on screen | 4 |
| **Voice** | A sound being played | 7 logical voices on 3 SID oscillators and the filter |
| **Task** | A running script | 16 |

A costume exists whether or not it is in the current room. Only when it is in the room does it
get an actor, and with it a hardware sprite. In the same way a sound resource only plays once
voices have been allocated to it, and a script only runs once it has a task.

## Directory map

### [disk/](disk/)

| File | What it holds |
|---|---|
| [disk_low_level.asm](disk/disk_low_level.asm) | Serial bus (IEC) bit-banging through CIA2: send and receive a byte, sync, send a three-byte command. |
| [disk_high_level.asm](disk/disk_high_level.asm) | Sector reads and writes over the fast-loader protocol, the byte stream over a sector chain, bulk copy, disk side checks. |
| [drive_setup.asm](disk/drive_setup.asm) | Uploads the custom loader into the 1541 drive's RAM and switches both sides to the fast protocol. |

### [resources/](resources/)

| File | What it holds |
|---|---|
| [memory_mgmt.asm](resources/memory_mgmt.asm) | The heap: best-fit allocation, free-list sorting and coalescing, compaction, and eviction when memory runs out. |
| [rsrc_mgmt.asm](resources/rsrc_mgmt.asm) | Loads costumes, sounds, scripts and objects from disk, checks them, publishes their pointers, evicts by priority. |
| [room_loader.asm](resources/room_loader.asm) | Switches rooms: tears down the old one, loads and unpacks the new one, runs the exit and entry scripts, spawns actors. |
| [room_gfx_rsrc.asm](resources/room_gfx_rsrc.asm) | Builds the per-column decompressor snapshots that let the renderer start decoding at any column. |
| [decompressor.asm](resources/decompressor.asm) | The run-length plus four-symbol dictionary decoder used for all room and object graphics. |

### [graphics/](graphics/)

| File | What it holds |
|---|---|
| [render_room.asm](graphics/render_room.asm) | Draws the room view: full redraws, one-column scrolling, double-buffered frame buffers. |
| [render_object.asm](graphics/render_object.asm) | Draws objects over the room background, with the parent and overlay rules. |
| [render_actor.asm](graphics/render_actor.asm) | Composes an actor's sprite from its limbs' cels. |
| [blit_cel.asm](graphics/blit_cel.asm) | Blits one cel into a sprite buffer, with optional horizontal flipping. |
| [masking.asm](graphics/masking.asm) | Hides parts of an actor that are behind foreground scenery. |
| [camera.asm](graphics/camera.asm) | The horizontal camera: panning, following an actor, clamping to the room. |
| [flashlight.asm](graphics/flashlight.asm) | The flashlight beam when the lights are off. |
| [shutter.asm](graphics/shutter.asm) | The rectangular "shutter" transition that opens a new room. |
| [irq_handlers.asm](graphics/irq_handlers.asm) | The chain of raster interrupts: sprite multiplexing, mode splits, and the per-frame input, text and sound ticks. |

### [actors/](actors/)

| File | What it holds |
|---|---|
| [actor.asm](actors/actor.asm) | Per-frame actor visibility and sprite position updates. |
| [actor_costume.asm](actors/actor_costume.asm) | Binds costumes to actors and releases them. |
| [actor_animation.asm](actors/actor_animation.asm) | Limb animation: clips, cel sequences, loops, speaking and facing. |
| [actor_motion_core.asm](actors/actor_motion_core.asm) | The motion state machine: installing waypoints, turning, walking. |
| [actor_path_dda.asm](actors/actor_path_dda.asm) | Pixel stepping towards a waypoint with an integer DDA, checked against walkboxes. |
| [actor_sprites.asm](actors/actor_sprites.asm) | Sprite allocation, room-to-screen coordinates, front-to-back ordering. |
| [actor_targeting.asm](actors/actor_targeting.asm) | Turns "go to that actor or object" into a safe target, and handles instant placement. |

### [walkboxes/](walkboxes/)

| File | What it holds |
|---|---|
| [walkbox_helpers.asm](walkboxes/walkbox_helpers.asm) | Finds a room's walkbox table and a box's record. |
| [walkbox_snap.asm](walkboxes/walkbox_snap.asm) | Finds the nearest walkbox to a point and snaps the point into it, including diagonal edges. |
| [walkbox_dfs.asm](walkboxes/walkbox_dfs.asm) | Depth-first search over the walkbox adjacency lists. |
| [walkbox_waypoint_planner.asm](walkboxes/walkbox_waypoint_planner.asm) | Turns a box path into the next waypoint, going straight or around a corner. |
| [actor_walkbox_traversal.asm](walkboxes/actor_walkbox_traversal.asm) | Works out which box an actor is in after each step and runs box behaviours (ladders, slopes, foreground masking). |

### [interpreter/](interpreter/)

| File | What it holds |
|---|---|
| [script_engine.asm](interpreter/script_engine.asm) | The task scheduler, the opcode dispatch loop, program counters, relocation, starting and nesting scripts. |
| [script_primitives.asm](interpreter/script_primitives.asm) | Shared helpers: operand decoding, object attributes, distance, pause timers, sentence objects. |
| [ops_primitives.asm](interpreter/ops_primitives.asm) | Jumps, conditionals, variable arithmetic, bit variables, start/stop/pause. |
| [ops_loading.asm](interpreter/ops_loading.asm) | Loading rooms, costumes, scripts and sounds, including "load room with ego". |
| [ops_locking.asm](interpreter/ops_locking.asm) | Locking and unlocking resources against eviction. |
| [ops_messages.asm](interpreter/ops_messages.asm) | Making the current kid or another actor speak. |
| [ops_camera.asm](interpreter/ops_camera.asm) | Camera panning and following. |
| [ops_costume.asm](interpreter/ops_costume.asm) | Actor bit variables, distances, positions, rooms and animation clips. |
| [ops_object.asm](interpreter/ops_object.asm) | Object state bits, owners, picking up, renaming, comparisons with the active object. |
| [ops_walking.asm](interpreter/ops_walking.asm) | Walking and putting actors at points, actors and objects, and putting them in rooms. |
| [ops_cutscene.asm](interpreter/ops_cutscene.asm) | Entering and leaving cutscenes, and switching the control mode. |
| [ops_sound.asm](interpreter/ops_sound.asm) | Starting and stopping sounds and music. |
| [ops_misc.asm](interpreter/ops_misc.asm) | Random numbers, delays, lights, save/load, restart, sentences, override, status queries. |

### [ui/](ui/)

| File | What it holds |
|---|---|
| [input_scan.asm](ui/input_scan.asm) | Keyboard matrix and joystick scanning through CIA1. |
| [key_handler.asm](ui/key_handler.asm) | The game's keys: pause, restart, text speed, save/load, kid switching, skipping a cutscene. |
| [cursor.asm](ui/cursor.asm) | Cursor movement with acceleration and drag. |
| [ui_interaction.asm](ui/ui_interaction.asm) | Hotspots under the cursor: verbs, inventory, scroll arrows, highlighting and clicks. |
| [ui_messages.asm](ui/ui_messages.asm) | The message bar at the top: staging, timed printing, speaker colours, mouth sync. |
| [sentence_text.asm](ui/sentence_text.asm) | Builds the sentence bar text and resolves object names and prepositions. |
| [sentence_action.asm](ui/sentence_action.asm) | The sentence system: completing a sentence, the sentence stack, walking first, running verb handlers. |

### [sound/](sound/)

| File | What it holds |
|---|---|
| [sid_voice_controller.asm](sound/sid_voice_controller.asm) | Writes a voice's cached frequency, envelope and control state to the SID, and ticks durations and glides. |
| [voice_allocation.asm](sound/voice_allocation.asm) | Maps sounds onto logical and real voices, with priorities and reference counts. |
| [voice_scheduler.asm](sound/voice_scheduler.asm) | Decides whether a sound can start, evicts lower-priority sounds, binds voice streams, stops sounds. |
| [sound_engine.asm](sound/sound_engine.asm) | The per-frame sound driver and the voice instruction decoder, with voice multiplexing and filter routing. |
| [music.asm](sound/music.asm) | Points the driver at the music player code inside a music resource, and stops music. |

### [system/](system/)

| File | What it holds |
|---|---|
| [entry_point.asm](system/entry_point.asm) | Where the loader hands over: baseline hardware state, heap clearing, then the start of the game. |
| [init_engine.asm](system/init_engine.asm) | Hardware setup, memory clearing, relocation of code and data, initial engine state. |
| [start_game.asm](system/start_game.asm) | Loads boot-time tables, sets up the player and camera, starts the first global script. |
| [main.asm](system/main.asm) | The main loop, and the walking-sound logic. |
| [misc.asm](system/misc.asm) | Memory fill and copy helpers and other routines that belong to no single module. |
| [pause.asm](system/pause.asm) | Pausing and unpausing, with the "snail" cursor. |
| [random.asm](system/random.asm) | The 24-bit pseudo-random generator. |
| [save_game.asm](system/save_game.asm) | Save and load: memory snapshots streamed to fixed tracks of a save disk. |
| [unused_code.asm](system/unused_code.asm) | Routines that are present in memory but never called. |

### [include/](include/)

| File | What it holds |
|---|---|
| [constants.inc](include/constants.inc) | Shared constants: resource types and limits, verbs, control modes, screen layout, task states. |
| [globals.inc](include/globals.inc) | Addresses of global variables, grouped by subsystem. |
| [registers.inc](include/registers.inc) | Names for the VIC-II, SID, CIA and CPU port registers. |
| [opcode_handlers.inc](include/opcode_handlers.inc) | The 256-entry opcode dispatch table (low and high bytes). |
| [rsrc_metadata.inc](include/rsrc_metadata.inc) | Where each resource is on disk: room sides and start sectors, and per-resource offsets. |
| [hotspots_metadata.inc](include/hotspots_metadata.inc) | Rectangles and handlers for the interface hotspots (verbs, inventory, sentence bar). |
| [actor_motion_constants.inc](include/actor_motion_constants.inc) | Return codes and flags for the actor motion modules. |
| [cursor_physics.inc](include/cursor_physics.inc) | Joystick direction, acceleration and drag tables for the cursor. |
| [sound_constants.inc](include/sound_constants.inc) | Constants for the sound engine. |
| [text_data.inc](include/text_data.inc) | Built-in strings such as the pause and disk prompts. |

### Other files

| File | What it is |
|---|---|
| [listings/engine-listing.txt](listings/engine-listing.txt) | The raw, monolithic listing of the engine as it sits in memory, with early notes on the memory layout. The modules were cut from it. Use it to see code in its original order and to check an address. |
| [listings/music-player-listing.txt](listings/music-player-listing.txt) | A listing of the music player, which is code stored inside the music sound resource and called by the sound driver. |
| [tools/notepad++-6502-kickassembler.xml](tools/notepad++-6502-kickassembler.xml) | A Notepad++ user-defined language for 6502 KickAssembler syntax, to read the sources with highlighting. |
| [data-structures.txt](data-structures.txt) | The data formats in one place: resource liveness bytes, the room header, object records, walkbox records, object attributes, and the costume format with a worked example (costume 1, Syd). |
| [technical-aspects.pdf](technical-aspects.pdf) | A reprinted magazine article on the technical side of the C64 version: the room format, the compression, the sprite matrix, the disk layout and the scripts. A good overview to read before the code. |

The disk loader that runs before this engine, including the copy protection and the code
decryption, is documented separately in [../c64-loader/](../c64-loader/), starting with
[../c64-loader/overview.txt](../c64-loader/overview.txt).

## The modules, from the bottom up

The engine is not perfectly layered, but it reads well from the hardware upwards. Each step
below says what the module offers and who calls it.

1. **Disk.** [drive_setup.asm](disk/drive_setup.asm) runs once, uploading 512 bytes of loader into
   the drive with ordinary KERNAL commands and then switching to a custom serial protocol.
   [disk_low_level.asm](disk/disk_low_level.asm) moves single bytes over that protocol by toggling
   CIA2 lines with cycle-counted timing. [disk_high_level.asm](disk/disk_high_level.asm) builds the
   interface everything else uses: "start a chain at track/sector T/S, read into the buffer at
   `$0300`, then stream or copy N bytes". It also writes linear buffers out as whole sectors (for
   saves) and keeps asking for the right disk side until it is inserted.

2. **Resources.** [rsrc_mgmt.asm](resources/rsrc_mgmt.asm) knows where every resource is
   ([rsrc_metadata.inc](include/rsrc_metadata.inc)): each resource belongs to a room, and is found
   by the room's disk side and start sector plus a sector index and byte offset. Its "ensure
   resident" helpers check a pointer table and, on a miss, ask the disk layer for the bytes and
   [memory_mgmt.asm](resources/memory_mgmt.asm) for a block. When memory is short, the allocator
   compacts the heap and then calls back into the resource manager to evict something unlocked,
   rotating between rooms, costumes, sounds and scripts. Moved blocks are reported back so that
   pointer tables and script program counters can be fixed. [room_loader.asm](resources/room_loader.asm)
   handles rooms: it unpacks the tile set, reads the object records into per-field tables, makes
   sure the room's sounds and scripts are resident, and asks [room_gfx_rsrc.asm](resources/room_gfx_rsrc.asm)
   to snapshot the [decompressor](resources/decompressor.asm) at the start of every column.

3. **Rendering.** [render_room.asm](graphics/render_room.asm) uses those snapshots to decode only
   the columns it needs: the whole 40-column view on a full redraw, or one new column when the
   camera has moved by one. It draws into one of two frame buffers and calls
   [render_object.asm](graphics/render_object.asm) for objects whose state differs from the
   background. [camera.asm](graphics/camera.asm) decides each frame where the view should be.
   Actors are drawn into sprite memory by [render_actor.asm](graphics/render_actor.asm), cel by cel
   through [blit_cel.asm](graphics/blit_cel.asm), and then cut by the foreground mask in
   [masking.asm](graphics/masking.asm). [flashlight.asm](graphics/flashlight.asm) and
   [shutter.asm](graphics/shutter.asm) are effects drawn over the same frame buffers.

4. **Actors and walkboxes.** A walk request goes to [actor_targeting.asm](actors/actor_targeting.asm),
   which snaps the target into the room's walkboxes ([walkbox_snap.asm](walkboxes/walkbox_snap.asm)).
   [walkbox_dfs.asm](walkboxes/walkbox_dfs.asm) finds a chain of boxes, and
   [walkbox_waypoint_planner.asm](walkboxes/walkbox_waypoint_planner.asm) turns the next step of the
   chain into a waypoint. Each frame [actor_motion_core.asm](actors/actor_motion_core.asm) moves the
   actor towards it with [actor_path_dda.asm](actors/actor_path_dda.asm), and
   [actor_walkbox_traversal.asm](walkboxes/actor_walkbox_traversal.asm) checks which box the actor
   is now in. [actor_animation.asm](actors/actor_animation.asm) steps the limbs, flagging actors that
   need redrawing, and [actor_sprites.asm](actors/actor_sprites.asm) decides which hardware sprite
   each visible actor gets and in what order they overlap.

5. **Interpreter.** [script_engine.asm](interpreter/script_engine.asm) runs every task that is
   ready, opcode by opcode, until the script yields. Each opcode jumps through the table in
   [opcode_handlers.inc](include/opcode_handlers.inc) to a handler in an `ops_*.asm` file, which
   decodes its operands with [script_primitives.asm](interpreter/script_primitives.asm) and calls
   down into the layers above: the room loader, the actor targeting code, the message bar, the
   sound scheduler.

6. **User interface.** [input_scan.asm](ui/input_scan.asm) and [cursor.asm](ui/cursor.asm) turn
   the joystick into a cursor position. [ui_interaction.asm](ui/ui_interaction.asm) maps that to a
   hotspot and a click. A click on a verb, an inventory item or the room feeds
   [sentence_action.asm](ui/sentence_action.asm), which completes a sentence (verb, object,
   preposition, second object), walks the kid to the object if needed, and then starts the
   object's handler script for that verb, which brings the interpreter back in.
   [sentence_text.asm](ui/sentence_text.asm) and [ui_messages.asm](ui/ui_messages.asm) draw the
   two text bars, and [key_handler.asm](ui/key_handler.asm) handles the keyboard.

7. **Main loop and interrupts.** [main.asm](system/main.asm) runs one game step: keys, scripts,
   sentences, actor motion and animation, camera, room redraw, actor redraw, effects. It then
   waits until at least four frames have passed. [irq_handlers.asm](graphics/irq_handlers.asm)
   runs alongside it at the frame rate: it shows the frame the main loop prepared, multiplexes the
   actor sprites down the screen, and ticks the things that must run every frame (the cursor,
   keyboard scanning, script delays, the message bar and the sound driver).

8. **Sound.** Sound is a separate column hanging off the interrupt. Scripts call
   [voice_scheduler.asm](sound/voice_scheduler.asm) to start a sound; it uses
   [voice_allocation.asm](sound/voice_allocation.asm) to find voices by priority. Every frame
   [sound_engine.asm](sound/sound_engine.asm) runs each voice's instruction stream and
   [sid_voice_controller.asm](sound/sid_voice_controller.asm) writes the result to the SID. Music
   is a program of its own, stored inside the music resource and entered through
   [music.asm](sound/music.asm).

## Where to start reading

A codebase of 67,000 lines is a lot to take in. Three ways in work well, and you can switch
between them.

- **Script first.** Pick a line in a script from [../scripts/](../scripts/), find its opcode in
  the tables at the top of the `interpreter/ops_*.asm` files, and follow the handler down. For
  example, the kid selection script (script 1 in
  [../scripts/retail/global-scripts.c](../scripts/retail/global-scripts.c)) starts with
  `delay(60)`, which is opcode `$2A`. That is `op_set_script_pause_counters` in
  [ops_misc.asm](interpreter/ops_misc.asm): it stores a 24-bit counter and marks the task paused.
  `handle_paused_tasks` in [script_primitives.asm](interpreter/script_primitives.asm), called from
  the frame interrupt, counts it up and makes the task runnable again when it overflows. This is
  the most direct route if you are reimplementing the interpreter.
- **Bottom up.** Read disk, resources and the decompressor, then rendering, then actors and
  walkboxes, then the interpreter and the interface, in the order of the walk-through above. This
  is the best route for the data formats.
- **Top down.** Start at `main_loop` in [main.asm](system/main.asm) and `irq_handler1` in
  [irq_handlers.asm](graphics/irq_handlers.asm), and drill down into what they call. This is the
  best route for timing and the order of events within a frame.

## In ScummVM

ScummVM implements version 0 in `engines/scumm`. It is organised differently and is not a
line-by-line translation of this engine, but most modules here have a counterpart there. The tables give the files and the main classes or functions
to compare. The sound player is the closest match: the comments in `players/player_sid.cpp` give
the C64 addresses of the routines they mirror (for example `onTimer()` at `$481B` and
`stopMusic_intern()` at `$4CAA`), and those are the same addresses as `sound_irq_handler` in
[sound_engine.asm](sound/sound_engine.asm) and `stop_music` in [music.asm](sound/music.asm).

### Disk and resources

| Module | ScummVM |
|---|---|
| [disk/](disk/) | `file.cpp`: `ScummDiskImage` (reads `maniac1.d64`/`maniac2.d64`, `openDisk`, `generateIndex`, `generateResource`) |
| [rsrc_mgmt.asm](resources/rsrc_mgmt.asm), [rsrc_metadata.inc](include/rsrc_metadata.inc) | `file.cpp` (`ScummDiskImage` reads the same room disk, track and sector tables from the disk image), `resource.cpp` (`ResourceManager`, `expireResources`), `resource_v2.cpp` |
| [memory_mgmt.asm](resources/memory_mgmt.asm) | No equivalent: ScummVM allocates from the host heap and expires resources by age in `resource.cpp`. |
| [room_loader.asm](resources/room_loader.asm) | `room.cpp`: `startScene`; `object.cpp`: `ScummEngine_v0::resetRoomObject` |
| [decompressor.asm](resources/decompressor.asm), [room_gfx_rsrc.asm](resources/room_gfx_rsrc.asm) | `gfx.cpp`: `GdiV1::decodeV1Gfx`, `GdiV1::roomChanged` |

### Graphics

| Module | ScummVM |
|---|---|
| [render_room.asm](graphics/render_room.asm) | `gfx.cpp`: `GdiV1::drawStripV1Background`, `GdiV1::drawStripV1Mask` |
| [render_object.asm](graphics/render_object.asm) | `object.cpp`: `drawObject`; `gfx.cpp`: `GdiV1::drawStripV1Object` |
| [render_actor.asm](graphics/render_actor.asm), [blit_cel.asm](graphics/blit_cel.asm), [masking.asm](graphics/masking.asm) | `costume.cpp`: `V0CostumeRenderer`, `V0CostumeLoader` |
| [camera.asm](graphics/camera.asm) | `camera.cpp`: `setCameraFollows`, `moveCamera`, `panCameraTo` |
| [flashlight.asm](graphics/flashlight.asm) | `gfx.cpp`: `drawFlashlight` (inherited by version 0 from `ScummEngine_v5`) |
| [shutter.asm](graphics/shutter.asm) | `gfx.cpp`: `fadeIn`, `transitionEffect` |
| [irq_handlers.asm](graphics/irq_handlers.asm) | No equivalent: ScummVM composes the screen in software and has no sprite multiplexing. |

### Actors and walkboxes

| Module | ScummVM |
|---|---|
| [actors/](actors/) | `actor.cpp`: `Actor_v0` (`initActor`, `walkActor`, `actorWalkXCalculate`, `actorWalkYCalculate`, `animateActor`, `limbFrameCheck`, `speakCheck`, `setDirection`) |
| [actor_targeting.asm](actors/actor_targeting.asm) | `actor.cpp`: `Actor_v0::actorSetWalkTo`, `adjustXYToBeInBox`; `script.cpp`: `ScummEngine_v0::walkToActorOrObject` |
| [walkboxes/](walkboxes/) | `boxes.cpp`: `ScummEngine_v0::walkboxFindTarget`, `ScummEngine_v0::areBoxesNeighbors`; `actor.cpp`: `Actor_v0::walkBoxQueue*`, `calcWalkDistances`, `updateWalkbox` |

### Interpreter

| Module | ScummVM |
|---|---|
| [script_engine.asm](interpreter/script_engine.asm) | `script.cpp`: `runScript`, `runScriptNested`, `runAllScripts` |
| [opcode_handlers.inc](include/opcode_handlers.inc), `ops_*.asm` | `script_v0.cpp`: `ScummEngine_v0::setupOpcodes` and the `o_` handlers (some are inherited from `script_v2.cpp` and `script_v5.cpp`) |
| [script_primitives.asm](interpreter/script_primitives.asm) | `script_v0.cpp`: `getVarOrDirectWord`, `getActiveObject` |
| [ops_cutscene.asm](interpreter/ops_cutscene.asm) | `script_v0.cpp`: `o_cutscene`, `o_endCutscene`, `o_setMode`, `ScummEngine_v0::setMode` |

### User interface

| Module | ScummVM |
|---|---|
| [input_scan.asm](ui/input_scan.asm), [key_handler.asm](ui/key_handler.asm), [cursor.asm](ui/cursor.asm) | `input.cpp`: `ScummEngine_v0::processInput`, `processKeyboard` |
| [ui_interaction.asm](ui/ui_interaction.asm) | `verbs.cpp`: `ScummEngine_v0::handleMouseOver`, `checkExecVerbs`, `resetVerbs`, `switchActor` |
| [sentence_action.asm](ui/sentence_action.asm) | `script.cpp`: `ScummEngine_v0::checkAndRunSentenceScript`, `runSentenceScript`; `verbs.cpp`: `ScummEngine_v0::verbExec` |
| [sentence_text.asm](ui/sentence_text.asm) | `script_v0.cpp`: `drawSentenceLine`, `drawSentenceObject`, `resetSentence` |
| [ui_messages.asm](ui/ui_messages.asm) | `string.cpp` (message printing), `script_v0.cpp`: `decodeParseString`; `actor.cpp`: `Actor_v0::speakCheck` |

### Sound

| Module | ScummVM |
|---|---|
| [sound/](sound/) | `players/player_sid.cpp`: `Player_SID` (`onTimer`, `processSongData`, `readSongChunk`, `findLessPrioChannels`, `swapVars`, `initMusic`) |

### System

| Module | ScummVM |
|---|---|
| [main.asm](system/main.asm) | `scumm.cpp`: `ScummEngine_v0::scummLoop`, `ScummEngine::scummLoop` |
| [init_engine.asm](system/init_engine.asm), [start_game.asm](system/start_game.asm) | `scumm.cpp`: `ScummEngine_v0::resetScumm` |
| [save_game.asm](system/save_game.asm) | `saveload.cpp`: `ScummEngine_v0::saveLoadWithSerializer`, `Actor_v0::saveLoadWithSerializer`. ScummVM uses its own save format, not the C64 memory snapshot. |
| [random.asm](system/random.asm) | `script_v5.cpp`: `o5_getRandomNr` (opcode `$16` in version 0) uses ScummVM's own random source, not this generator. |
