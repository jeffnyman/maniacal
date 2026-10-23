# How Maniac Mansion works

Maniac Mansion came out for the Commodore 64 in 1987. It had point-and-click verbs, three playable
kids with different skills, characters who moved around the house on their own, cutscenes, and
several endings, all running on a 1 MHz 6502 with 64 KB of memory and a floppy drive. This
document explains how, from the top down. It describes the C64 original (SCUMM version 0), which
the source in [c64-engine/](c64-engine/README.md) documents routine by routine. The later DOS,
Amiga, Atari ST and NES releases kept the same model and changed the details; see
[building-an-interpreter.md](building-an-interpreter.md#the-versions-you-have-to-handle).

Each section ends with where to read more. Paths are relative to this folder.

## A game made of scripts

The central idea is a split between the game and the engine. Everything the player experiences
is written in a small scripting language, SCUMM ("Script Creation Utility for Maniac Mansion"):
the title screen, the intro, kid selection, every puzzle, every reaction, the endings and even
the save/load screen. The engine knows nothing about tentacles or meteors. It knows how to
load rooms, draw them, move characters, play sounds, take input, and run scripts. It acts as a
virtual machine, and the scripts are its programs.

That split is what made the game portable. Lucasfilm wrote the scripts once, on Unix machines,
and wrote a new engine for each computer. It is also what makes an interpreter possible today:
reproduce the engine faithfully and the original scripts do the rest.

The game's scripts are in [scripts/](scripts/README.md), disassembled and commented.

## The things the engine knows about

The engine manages five kinds of **resource**, the data loaded from disk:

| Resource | What it is | How many (C64) |
|---|---|---|
| Room | A location: background graphics, the objects in it, its walkable floor, its own scripts and sounds | up to 54 (numbered 1 to `$36`) |
| Costume | A character's animation set | 25 |
| Script | A global script, callable from anywhere | 160 |
| Sound | A sound effect or a piece of music (the music is 6502 code) | 70 |
| Object | Anything in a room the player can name: doors, the doormat, the key under it | stored inside rooms |

On screen, these become a smaller set of live **representations**, because the C64 cannot run
everything at once:

| Representation | Of | Limit |
|---|---|---|
| Actor | A costume shown in the current room | 4 at once (one hardware sprite each) |
| Task (script slot) | A running script | 16 |
| Voice | A playing sound | 7 logical voices over the SID's 3 |

This vocabulary, rooms and costumes on one side, actors and tasks on the other, runs through the
whole engine. The [glossary](glossary.md) defines each term.

Read more: [c64-engine/README.md](c64-engine/README.md), [c64-engine/data-structures.txt](c64-engine/data-structures.txt).

## The screen

The C64 screen is 40 by 25 character cells. Maniac Mansion splits it into bands, each drawn in a
different video mode by switching the video chip mid-frame from raster interrupts:

| Rows | Band |
|---|---|
| 0 | The message line, where characters speak |
| 1-17 | The room view: 40 x 17 cells in multicolor character mode (160 x 136 "fat" pixels) |
| 18 | The sentence line, showing the sentence being built ("Open front door") |
| 19-21 | The 15 verbs, five to a row |
| 22-23 | The inventory: four visible items, with arrows to scroll |

The room is not a bitmap. It is drawn with characters, like text, but each room has its own
character set of up to 256 tiles. A room is a matrix of tile numbers, a color layer giving each
cell its fourth color, and a mask layer that marks which pixels are in front of characters.
Rooms are 17 cells high and from 40 to 160 cells wide; wider rooms scroll.

Read more: [c64-engine/graphics/](c64-engine/graphics/), [c64-engine/include/hotspots_metadata.inc](c64-engine/include/hotspots_metadata.inc).

## Memory and disk

Memory is the constraint behind every design decision. The engine's code fills the lower part of
memory. One heap of about 19 KB (`$4C85` bytes), between `$7B7B` and `$C7FF`, holds every loaded resource: the
current room, the costumes of the characters present, the running scripts, the sounds. The rest
holds the two screen buffers, sprite buffers, the room's character set, and the game state.

Resources are loaded on demand, from two disk sides. Each room has a known disk side and start
sector. Every costume, script and sound belongs to a room and sits at a known offset from that
room's start. A 4-byte header with a size and an XOR checksum guards each one; a bad read shows a
disk error and retries.

When the heap is full, the memory manager first compacts it, sliding blocks down and fixing every
pointer to them (a running script recomputes its program counter afterwards). If that is not
enough, it evicts something: the oldest unlocked room, a costume not in the current room, a
script or sound nobody is using. Scripts can lock resources they will need soon, so a cutscene
does not stall on the disk mid-scene.

The disk itself is read with a custom fast loader and protected against copying. That part runs
before the engine starts; see [c64-loader/](c64-loader/README.md).

Read more: [c64-engine/resources/](c64-engine/resources/), [c64-engine/disk/](c64-engine/disk/),
[scripts/c64-disk-layout.txt](scripts/c64-disk-layout.txt).

## Rooms and objects

Loading a room unpacks its graphics. Each layer is compressed with a simple run-length scheme
with a four-entry dictionary of common bytes. Decompressing a whole wide room would not fit, so on
load the engine decodes each layer once and records the decoder's state at the start of every
17-byte column. When the view scrolls by one column, it restores the saved state for the new
column and decodes only that.

Objects are records inside the room: a 16-bit number, position and size, a parent, a walk-to
point, the direction the kid faces when using it, a name, and a table of verb handlers, which says
"for Open, run this code; for Pick up, that code". An object's state lives in a global table: who
owns it (a room, a kid, or nowhere), and a few state bits. Objects only draw their own graphics
in some states. The open door is drawn over the closed door in the background. An object can
depend on a parent: the key under the doormat is visible only once the mat is moved.

Read more: [c64-engine/resources/room_loader.asm](c64-engine/resources/room_loader.asm),
[c64-engine/resources/decompressor.asm](c64-engine/resources/decompressor.asm),
[c64-engine/graphics/render_object.asm](c64-engine/graphics/render_object.asm),
the object tables in [scripts/retail/tables/objects.md](scripts/retail/tables/objects.md).

## Characters: actors and costumes

A costume is a character's animation set: 8 limbs, each with sequences of picture cels; clips
choose a sequence per limb for walking, standing and talking in each direction. Left-facing frames
are not stored; the right-facing ones are mirrored as they are drawn.

On screen a character is an actor, and there are only four. Each is drawn with a single C64
hardware sprite, re-pointed to a new shape in each of eight 21-line bands down the room view by the interrupt
chain, so one sprite covers a figure 12 multicolor pixels wide and the height of the room. This is
a form of sprite multiplexing. When an actor's
pose changes, the engine composes its limbs into a sprite buffer and cuts out the pixels the
room's mask puts in front. Actors are sorted by depth, so the one higher on screen is behind.
Mouths flap while a character talks.

Read more: [c64-engine/actors/](c64-engine/actors/), [c64-engine/graphics/render_actor.asm](c64-engine/graphics/render_actor.asm),
[c64-engine/graphics/irq_handlers.asm](c64-engine/graphics/irq_handlers.asm).

## Walking: walkboxes

Characters walk on the room's floor, which is a set of **walkboxes**: rectangles, some with a
slanted side, each with an attribute (a ladder that forces facing up, a slope, whether actors on it
go behind the foreground mask). Each box lists the boxes it touches.

When a kid is sent somewhere, the engine snaps the target into the nearest box, searches the box
graph depth first for a route (the first route found, not necessarily the shortest), and walks it
one junction at a time, aiming for a corner of each shared edge unless the target is in a
straight line. Each step is a line-drawing step with an integer ratio, and a step that would leave
the floor is undone. The behaviour is precise and the scripts rely on it: a kid has to stop in the
same place for a scene to look right.

Read more: [c64-engine/walkboxes/](c64-engine/walkboxes/), [c64-engine/actors/actor_path_dda.asm](c64-engine/actors/actor_path_dda.asm),
[c64-engine/actors/actor_targeting.asm](c64-engine/actors/actor_targeting.asm).

## The script engine

Scripts run in 16 **task** slots. Each slot holds a script, its kind (global, room or object
script) and its position as an offset into the script, so the script can move in memory. The
engine is cooperative: once per game step it resumes each running task, and the task runs opcode
after opcode until it yields with `breakHere`, a delay, a stop, or a wait. Opcodes are dispatched
through a 256-entry table; the top bits of each opcode say which operands are constants and which
are variable numbers.

State is simple and global: byte variables (variable 0 is the current kid), 55 bytes of bit
flags, a byte of flags per character (frozen, invisible, strong, is a friend of the tentacle,
saw the TV ad, is Ed's enemy), and the object owner and state tables. Puzzles are mostly tests
and updates of this state. The [variable table](scripts/retail/tables/variables.md) names them.

Scripts start other scripts, which run immediately until they yield, then the caller resumes.
Rooms have entry and exit scripts that run when the player arrives and leaves. A **cutscene**
suspends every other task and takes away control; an **override** marks where the script should
jump if the player presses the skip key. Delays count video frames.

Read more: [c64-engine/interpreter/](c64-engine/interpreter/), [scripts/README.md](scripts/README.md).

## Sentences: how a click becomes a script

The player builds a sentence: a verb, an object, perhaps a preposition and a second object
("Use key in front door"). When it is complete it goes on a sentence stack (up to six entries;
scripts can push sentences too). The engine then:

1. Drops sentences that make no sense (an object used with itself).
2. If neither object is held and the object can be picked up, inserts "Pick up" first.
3. Walks the kid to the object's walk-to point.
4. Runs the object's handler for that verb. If there is none, Give to a kid just hands the object
   over, Walk to does nothing, and anything else runs global script 3, which produces the stock
   replies.

"New kid" switches the player to another of the three kids; the function keys do the same.

Read more: [c64-engine/ui/sentence_action.asm](c64-engine/ui/sentence_action.asm),
[c64-engine/ui/ui_interaction.asm](c64-engine/ui/ui_interaction.asm).

## Changing rooms

Going through a door is a sequence the engine performs in a fixed order: clear the sentence; run
the old room's exit script; hide the actors; reset the camera; load and unpack the new room; run
its entry script; load its sounds and scripts; give actors to the costumes present; open the
**shutter**, a rectangle that grows to reveal the room (only when the lights are on). The entry script runs
before the actors appear, which is why a room can position its characters before you see them.

Read more: [c64-engine/resources/room_loader.asm](c64-engine/resources/room_loader.asm),
[c64-engine/graphics/shutter.asm](c64-engine/graphics/shutter.asm).

## The camera, lights and the flashlight

The camera is the centre column of the view. It can follow an actor, scrolling when the actor
nears either edge, or pan to a target one column per step; distant targets jump. Scrolling copies
the screen one column sideways and decodes the newly revealed column.

Rooms can be lit, dark, or dark with the flashlight. The flashlight is a 6 by 4 cell window around
the cursor, copied in from the off-screen buffer as it moves.

Read more: [c64-engine/graphics/camera.asm](c64-engine/graphics/camera.asm), [c64-engine/graphics/flashlight.asm](c64-engine/graphics/flashlight.asm).

## Text

Speech appears on the message line in the speaker's color while the speaker's mouth moves. Each
character adds to a display countdown, so long lines stay longer; the player can change the text
speed. The text itself is packed: a byte with its top bit set means "this letter and a space".

Read more: [c64-engine/ui/ui_messages.asm](c64-engine/ui/ui_messages.asm), [c64-engine/interpreter/ops_messages.asm](c64-engine/interpreter/ops_messages.asm).

## Input

A joystick or the keyboard moves the cursor, which has momentum: velocity with drag, rather than a
fixed step. The screen is divided into hotspots (the room, the sentence line, each verb, each
inventory slot, the scroll arrows), and the hotspot under the cursor decides what a click means.
Keys switch kids (F1, F3, F5), skip a cutscene (F7), restart (F8), pause (space), open the
save/load screen (F2) and change the text speed (+ and -). All of this runs in the interrupt
handler, every frame, independent of the game step.

Read more: [c64-engine/ui/](c64-engine/ui/).

## Sound

The SID chip has three voices. The engine offers seven: the three real ones, the filter, and three
virtual voices that are time-shared onto the real ones. A sound effect says which voices it
needs and its priority; when voices run short, lower-priority sounds are stopped. Each voice plays
a stream of compact instructions (gate, frequency, envelope, waveform, glide, repeat). Music is
different: each piece is 6502 code inside its sound resource, which the engine calls every frame,
and it takes the three real voices. Footsteps play from the walking costume, except outside,
where the crickets are.

Read more: [c64-engine/sound/](c64-engine/sound/), [c64-engine/listings/music-player-listing.txt](c64-engine/listings/music-player-listing.txt).

## Saving

A saved game is a snapshot of memory written to a separate formatted disk: the engine state and
variables, a few zero-page bytes, and everything from the inventory table to the top of the heap,
which means every resident resource too. Loading puts it all back and rebuilds the scripts'
program counters. There is one save per disk.

Read more: [c64-engine/system/save_game.asm](c64-engine/system/save_game.asm).

## The main loop

The engine has two clocks. The raster interrupt runs every video frame (50 times a second on a
European machine, 60 in America). It splits the screen into its bands, multiplexes the actor
sprites, moves the cursor, scans the keyboard, counts down script delays and the message timer,
and drives the sound. The main loop runs the game, at most once every four frames:

1. Handle a key; update the cursor and the variables the scripts read.
2. Run every task until it yields.
3. Process the sentence; walk and animate the actors; move the camera.
4. Redraw the room if it scrolled; compose the actors that changed; swap buffers.
5. Run the shutter, the footsteps, the flashlight; hand the frame to the interrupt and wait.

So the game itself runs at 12 to 15 steps a second, while the cursor, sound and screen run at the
full frame rate. A slow step (a scroll, a disk load) just takes longer.

Read more: [c64-engine/system/main.asm](c64-engine/system/main.asm), [c64-engine/graphics/irq_handlers.asm](c64-engine/graphics/irq_handlers.asm).

## From here

- To see the model in action, read script 1 (the intro and kid selection) in
  [scripts/retail/global-scripts.c](scripts/retail/global-scripts.c) and follow a few of its
  statements into [c64-engine/interpreter/](c64-engine/interpreter/).
- To build an interpreter, continue with [building-an-interpreter.md](building-an-interpreter.md).
- [technical-aspects.pdf](c64-engine/technical-aspects.pdf) is a short magazine article on the
  C64 release's technical tricks: the room format, the sprite multiplexing, the copy protection, and
  why the C64 release has no door code.
