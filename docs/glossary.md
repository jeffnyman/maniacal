# Glossary

Terms used throughout this resource. Where the C64 engine and ScummVM use different names for
the same thing, both are given.

**Actor.** A character on screen in the current room: a costume with a position, facing, walk
target and animation state. The C64 has 4 actor slots, one hardware sprite each. See
[c64-engine/actors/](c64-engine/actors/).

**Bit variable.** A one-bit game flag. The C64 keeps 55 bytes of them, set, cleared and tested by
their own opcodes. There is also one byte of flags per character (the *actor bit variables*:
frozen, invisible, strong, and so on).

**Box matrix.** For each walkbox, the list of boxes it connects to. The path finder searches it.

**Cel.** One picture of one limb. Costumes are built from cels.

**Clip.** A costume's choice of cel sequence for each of its limbs: walking left, standing facing
down, talking, and so on.

**Costume.** A character's animation resource: limbs, cel sequences, cels and clips. A costume
becomes an actor when it is in the current room. Scripts mostly refer to characters by costume
number.

**Cutscene.** A stretch of script during which the player has no control and other scripts are
suspended. Usually skippable with an override.

**Ego.** The character the player controls: the current kid. On the C64 it is variable 0. ScummVM
calls it `VAR_EGO`.

**Entry script, exit script.** The scripts a room runs when the player enters and leaves it.

**Global script.** A numbered script that can be started from anywhere. Script 1 is the intro and
kid selection; script 3 gives the default responses for verbs an object does not handle.

**Index.** The table, on the C64 part of the boot data and on DOS the file `00.LFL`, that says
where every room, costume, script and sound is, and holds the initial owner and state of every
object.

**Inventory.** The objects the kids carry. On the C64, one 45-slot list shared by all kids and
filtered by owner when shown.

**Jiffy.** One tick of the 60 Hz clock ScummVM uses for timing. On the C64 the clock is the video
frame, 50 Hz (PAL) or 60 Hz (NTSC).

**LFL file.** A room file of the DOS releases (`01.LFL`, `02.LFL`, ...), holding a room and the
costumes, scripts and sounds that belong to it. `00.LFL` is the index.

**Limb.** One independently animated part of a costume (head, body, legs...). Costumes have up to 8.

**Mask.** The layer of a room that marks the pixels in front of actors: a table leg, a railing,
the fence outside.

**Object.** Anything in a room the player can refer to, from doors to the key under the doormat.
It has a number, a position, a name, a state, an owner and a table of verb handlers.

**Override.** A point in a cutscene where the script jumps if the player presses the skip key (F7
on the C64). ScummVM: `beginOverride`.

**Owner.** Who has an object: a kid, a room, or nobody (limbo).

**Parent.** An object whose state decides whether another object is visible: the doormat is the
parent of the key.

**Resource.** Data loaded from disk on demand: rooms, costumes, scripts, sounds (and on the C64,
objects as part of rooms).

**Room.** A location: its graphics, objects, walkboxes, entry and exit scripts, local scripts and
sounds. Also used for screens that are not places, such as the title and the save/load screen.

**SCUMM.** The Script Creation Utility for Maniac Mansion: the script language and the tools
Lucasfilm built for this game, used in its later adventures too. Version 0 is the C64 original;
versions 1 and 2 are the DOS releases of Maniac Mansion and Zak McKracken.

**Script slot.** See *task*.

**Sentence.** A verb with one or two objects and a preposition, built by clicking or pushed by a
script. The engine queues sentences on a 6-entry stack and runs them.

**Shutter.** The effect that reveals a newly entered room: a rectangle that grows to the full view.

**SPUTM.** The "Script Presentation Utility" (TM): Lucasfilm's name for the engine that runs SCUMM
scripts.

**State.** A few bits per object that scripts set and test: open or closed, on or off, moved or not.
Some states make the object draw its own graphics.

**Strip, column.** A vertical slice of a room. The C64 decodes rooms in 17-cell columns, one cell
wide; ScummVM draws rooms in 8-pixel strips.

**Task.** A running script: one of 16 slots on the C64, each with a script, a type and a position.
ScummVM calls them script slots.

**Verb.** One of the 15 actions on the verb panel. Verb numbers on the C64: Open 1, Close 2, Give 3,
Turn on 4, Turn off 5, Fix 6, New kid 7, Unlock 8, Push 9, Pull 10, Use 11, Read 12, Walk to 13,
Pick up 14, What is 15.

**Voice.** A sound channel. The SID has 3; the C64 engine presents 7 logical voices, time-sharing
some of them onto the real ones.

**Walkbox.** One area of a room's floor: a rectangle, possibly with a slanted side, with
attributes such as ladder, slope, or behind-the-mask. Actors walk only inside walkboxes.

**Walk-to point.** Where a kid stands to use an object.
