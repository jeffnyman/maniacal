# The game's scripts

Maniac Mansion is a program written in SCUMM, Lucasfilm's script language, running on an engine
that knows nothing about mansions, kids or tentacles. This folder holds that program: a commented
disassembly of every script in the Commodore 64 release (SCUMM version 0) and in the C64 demo,
plus tables that name the actors, rooms, scripts, variables, sounds, objects and codes the scripts
use.

| Path | What it is |
|---|---|
| [retail/global-scripts.c](retail/global-scripts.c) | The global scripts of the retail game, `Script-1` to `Script-158` |
| [retail/room-scripts.c](retail/room-scripts.c) | The entry and exit script of each room |
| [retail/object-scripts.c](retail/object-scripts.c) | The verb scripts of each object, room by room |
| [retail/tables/](retail/tables/) | The names and meanings, as Markdown, with the source spreadsheet [`maniac-mansion.xlsx`](retail/tables/maniac-mansion.xlsx) |
| [demo/global-scripts.c](demo/global-scripts.c), [demo/room-scripts.c](demo/room-scripts.c), [demo/object-scripts.c](demo/object-scripts.c) | The same three files for the demo |
| [demo/tables/](demo/tables/) | The demo's tables, with [`maniac-mansion-demo.xlsx`](demo/tables/maniac-mansion-demo.xlsx) |
| [c64-disk-layout.txt](c64-disk-layout.txt) | Where the index tables sit on the C64 disk, explained [below](#where-the-scripts-are-on-the-disk) |

The `.c` extension is only there so editors colour the files. They are not C and do not compile.

## What a game script is

The engine (Lucasfilm called it SPUTM) provides mechanisms: it loads rooms, draws them, walks
actors along walkboxes, prints text, plays sounds, builds sentences from the verbs the player
clicks, and runs scripts. Everything that makes the game *this* game is in the scripts. Which kid
can open which door, what Dr. Fred says, when Nurse Edna leaves her room, how the meltdown timer
counts down: all of it is script code.

That includes things that look like engine features in most programs:

- **The title screen and kid selection** are script 1. It loads room 45 ("Kid Selection"), puts
  the cursor into keypad mode, prints the copyright line, waits for three kids to be picked and
  then starts the intro cutscene, script 115.
- **The intro** (comet, mansion, the kids at the fence) is scripts 115, 130 and 126.
- **The save/load screen** is room 50, whose three objects (442 Load Game, 443 Save Game,
  444 Continue Playing) are its buttons. When the player presses
  F2 the engine does nothing more than start global script 2
  ([`ui/key_handler.asm`](../c64-engine/ui/key_handler.asm), `SCRIPT_ID_SAVELOAD = $02`). Script 2
  loads room 50, waits for a button object to set `Var[50]`, and calls the `saveLoadGame` opcode,
  which is the only part the engine supplies.
- **The randomised codes** (Edna's phone number, the attic safe, the lab door, the meteor police)
  are chosen by script 1 with `getRandomNr` and stored in ordinary variables.

Apart from room and object scripts, which it runs as part of changing rooms and executing
sentences, the engine starts only three global scripts by itself: script 1 when the game boots
([`system/start_game.asm`](../c64-engine/system/start_game.asm) calls `launch_global_script` with
`SCRIPT_IDX_START_GAME = $01`), script 2 on F2, and script 3 when an object has no handler for a
verb. Everything else follows from script 1 and from the player's clicks.

For an interpreter this means the engine can stay generic. It has to execute the opcodes faithfully;
it does not have to know the game.

## The three kinds of script

The engine runs scripts as cooperative tasks. The C64 engine has 16 task slots (`TASK_TOTAL = $10`
in [`include/constants.inc`](../c64-engine/include/constants.inc)), each with a script number, a
type (object, room or global) and a program counter. Every frame the scheduler in
[`interpreter/script_engine.asm`](../c64-engine/interpreter/script_engine.asm) runs each active task
until it yields with `breakHere()` or `delay()`, or ends with `stopObjectCode()`. A script that
loops waiting for something (`breakHere(); unless (...) goto ...`) costs one pass per frame.

### Global scripts

Numbered resources, each stored in some room's file on disk and loaded on demand. They are started
by number with `startScript(n)` from any other script and run in their own slot; only one instance
of a given global script runs at a time (`launch_global_script` in `script_engine.asm`). They do
the long-running work: cutscenes, the meltdown timer, characters that patrol the mansion, the phone
and keypad logic. The retail index has 160 script slots and the
disassembly contains 156 scripts; the demo index has 55 slots. They are in `global-scripts.c`, one `Script-N { ... }` block each.

### Room entry and exit scripts

Each room carries an exit script and an entry script inside its own resource (offsets `$14` and
`$16` of the room header, `ROOM_EXIT_SCRIPT_OFS` and `ROOM_ENTRY_SCRIPT_OFS` in
[`resources/room_loader.asm`](../c64-engine/resources/room_loader.asm)). When the engine switches
rooms it runs the old room's exit script while that room's data is still in memory, loads the new
room, and runs the new room's entry script (`execute_room_exit_script` and
`execute_room_entry_script` in [`system/misc.asm`](../c64-engine/system/misc.asm)). The scripts
set the lights, start and stop ambient sounds and background scripts, and fire triggers. In
`room-scripts.c` each room has an `Entry { }` and an `Exit { }` block under a `Room N: name`
header.

### Object (verb) scripts

Each object in a room has a small verb table: pairs of one-byte verb number and one-byte offset of
the handler code within the object's resource, ended by a zero. When the player completes a
sentence such as "Open Mailbox", the sentence system looks up the verb in the direct object's table
(`find_object_verb_handler_offset` in
[`ui/sentence_action.asm`](../c64-engine/ui/sentence_action.asm)) and runs the code at that offset
(`execute_verb_handler_for_object`). If the object has no handler for the verb, the engine falls back
to global script 3, "Verb: Defaults", which prints the stock refusals ("It doesn't seem to
open.", "I can't pick that up.", "That doesn't seem to work."). "Walk to" without a handler just walks; "Give" to a kid without a handler transfers the
object; "Read" in the dark always goes to the defaults.

One rule matters for anyone reading `object-scripts.c`: in version 0, verb number `$0F` in a verb
table matches *any* verb (`DEFAULT_VERB = $0F` in `sentence_action.asm`; ScummVM's
`getVerbEntrypoint` in `engines/scumm/script.cpp` has `kFallbackEntry = 0x0F` for version 0 and
`0xFF` for versions 1 and 2). `$0F` is also the number of "What is", so the disassembly labels
those entries `WhatIs`. The kid portraits in room 45 and the buttons in room 50 have only a
`WhatIs` entry, and that is why clicking them works in keypad mode, where the verb is forced to
"Push".

## Reading the notation

A short excerpt from the start of script 1 shows most of the notation:

```c
Script-1 {
    [0000] cutscene();                                                          // Start Cutscene
    [0001] cursorCommand( kModeKeypad )                                         // Keypad Mode
    ...
    [000B] Var[97] = 3;                                                         // Kid1 is Dave
    [000E] loadRoom(45);
    [0010] Var[91] = 0;                                                         // Clear 'Start' status

    [0013] delay(60);                                                           // Sleep 1 second
    [0017] print(255,"=1987 Lucasfilm Ltd. All rights reserved");
    [003D] beginOverride();
    {
        [0041] Var[48] = getRandomNr(1);
        [0044] if (VAR_HAVE_MSG == 0) {                                             // if No Text
        [0049]   delay(120);                                                        //  Sleep 2 seconds
        ...
        [0099] }
        [0099] breakHere();                                                     // Engine Tick
        [009A] unless (Var[91] == 1) goto 0041;                                 // Loop until start pressed

        [009F] if (Var[92] < 3) {                                               // Number of Selected Kids < 3?
        [00A4]   doSentence(9,395,0);                                           //  Yes, Press the start button (display message)
        [00A8]   goto 0013;                                                     //  Back to Start
        [00AB] }
    }
    [00AB] endcutscene();                                                       // End Cutscene
```

**The `[offset]` column.** The hexadecimal number in brackets is the byte offset of the statement
within the script's code. For global scripts it counts from the first opcode, which follows a
4-byte resource header (two bytes of length and two more). For object scripts the offsets are from
the start of the object's resource, which is why the first handler of an object starts at `[001C]`
or so rather than `[0000]`: those are the same offsets the verb table stores. Jump targets
(`goto 0041`) are written as offsets in the same column, so they can be found by eye. Where the
bracketed offsets of an `if` block and the next statement are equal (`[0099] }` followed by
`[0099] breakHere();`), the brace is notation only and occupies no bytes.

**Variables.** `Var[n]` is global game variable *n*, one byte each on the C64. Their meanings are in
[retail/tables/variables.md](retail/tables/variables.md), which starts at `Var[11]`. The low numbers
are engine variables that the interpreter reads or writes itself, and the disassembly gives them
names. The names and numbers follow ScummVM's version 0 assignment (`ScummEngine_v0::setupScummVars`
in `engines/scumm/vars.cpp`):

| Name | Var | Meaning |
|---|---|---|
| `VAR_EGO` | 0 | The actor number of the kid the player controls |
| `VAR_RESULT` | 1 | Not an engine variable: the scratch variable the scripts use for results (`VAR_RESULT = isScriptRunning(115)` encodes as `68 01 73`) |
| `VAR_CAMERA_POS_X` | 2 | Camera position |
| `VAR_HAVE_MSG` | 3 | Non-zero while a message is being shown; scripts wait for it to clear before the next line |
| `VAR_ROOM` | 4 | The current room |
| `activeObject2` | 5 | The indirect object of the sentence being run (ScummVM's `VAR_ACTIVE_OBJECT2`) |
| `VAR_OVERRIDE` | 6 | Set when the player skipped a cutscene |
| `VAR_IS_SOUND_RUNNING` | 8 | Non-zero while music plays |
| `VAR_ACTIVE_VERB` | 9 | The verb of the sentence being run |
| `VAR_CHARCOUNT` | 10 | How many characters of the current message have been printed |

`VAR_HAVE_MSG` as variable 3 and `VAR_RESULT` as variable 1 were checked against the bytes of script 1
on the retail disk. `activeObject` (without the 2) is the direct object of the running verb script.

**Bit variables.** Besides the byte variables there are two sets of flags:

- `setBitVar(n, bit, value)` and `getBitVar(n, bit)` set and test bit *bit* of flag byte *n*
  (ScummVM: `_bitVars[n] |= 1 << bit`). The game uses the room number as *n* for the lighting
  flags: `setBitVar(44,2,1)` makes room 44 always lit, `setBitVar(8,1,1)` makes the basement dark.
  See [room-bitvars.md](retail/tables/room-bitvars.md) and [bitvars.md](retail/tables/bitvars.md).
- `setActorBitVar(actor, mask, value)` and `getActorBitVar` set and test flags of an actor. Here the
  second argument is a mask, not a bit number: `setActorBitVar(Var[100],128,0)` clears `$80`, the
  "hidden" flag. See [actor-bitvars.md](retail/tables/actor-bitvars.md).

**Object state.** `getState08(obj)`, `setState08(obj)` and `clearState08(obj)` test and change bit
`$08` of an object's state, which the game uses for open/closed, on/off and similar.
`setState04` and `setState02` work the same way on bits `$04` (used for "locked" and "broken") and
`$02`. ScummVM calls these bits `kObjectStateIntrinsic` (8), `kObjectStateLocked` (4) and
`kObjectStateUntouchable` (2) in `object.h`. Where the disassembly shows no argument, the statement
applies to the object whose script is running. [objects.md](retail/tables/objects.md) gives the meaning of the bits for each
object, and the verbs it has handlers for.

**Conditions and jumps.** SCUMM has no `if` statement. A comparison opcode compares two operands
and, if the comparison is *false*, adds a 16-bit relative offset to the program counter; if it is
true, execution continues with the next statement (`op_if_operand_eq` and its siblings in
[`interpreter/ops_primitives.asm`](../c64-engine/interpreter/ops_primitives.asm)). The disassembly
shows one opcode in two ways:

- `if (cond) { ... }` when the jump goes forward to the end of a block;
- `unless (cond) goto X;` when it goes anywhere else, usually backward to form a loop.

In the excerpt, `[0044] if (VAR_HAVE_MSG == 0) {` is the bytes `48 03 00 50 00`: opcode `$48`
(is equal), variable 3, value 0, and offset `$0050`, which lands at `$0049 + $0050 = $0099`, the
closing brace. `[009A] unless (Var[91] == 1) goto 0041;` is `48 5B 01 A2 FF`, the same opcode with
offset -94. A plain `goto` is the unconditional jump opcode `$18`. `} else {` is a `goto` over the
else part placed at the end of the then part.

**Cutscenes.** `cutscene()` takes control from the player: it saves the control mode and the room,
freezes the other scripts and clears the sentence line. `endcutscene()` restores
them. Between the two the script drives everything. The `Cutscene` column of
[scripts.md](retail/tables/scripts.md) says which scripts do this.

**Overrides.** `beginOverride()` marks the point where the player may skip. The opcode records the
current position and is always followed by a jump that normal execution steps over. When the
player presses the skip key (F7 on the C64, handled in
[`ui/key_handler.asm`](../c64-engine/ui/key_handler.asm)), the engine resumes the script at that jump,
which leads past the skippable part. The braces after `beginOverride()` show the skippable region;
the real landing point is the jump's target. In script 1 that jump goes to `[009F]`, the check that
three kids have been chosen, so skipping the title screen with fewer than three kids still asks for
more.

**Object script entries.** In `object-scripts.c`, each object starts with its name and a line
such as `roomobj-1-132.dmp`, meaning object 132 of room 1. Its handlers follow, labelled with the
verbs whose table entries point to them. When several verbs share one handler the label joins them
with dots: `Pull.Pickup { ... }` is the code that both "Pull" and "Pick up" jump to. The list of
verb numbers is at the top of the file and in [values.md](retail/tables/values.md):

| | | | | | | | |
|---|---|---|---|---|---|---|---|
| 1 Open | 2 Close | 3 Give | 4 Turn on | 5 Turn off | 6 Fix | 7 New kid | 8 Unlock |
| 9 Push | 10 Pull | 11 Use | 12 Read | 13 Walk to | 14 Pick up | 15 What is (any verb) | 16 Lock |

`doSentence(verb, obj, obj2)` queues a sentence as if the player had built it, so one object can
hand its verb on to another. `stopObjectCode()` ends a script of any kind.

**Comments.** Everything after `//` is annotation. Comments give the meaning of numbers using the
tables ("Kid1 is Dave", "if Envelope Stamped"). They are interpretation and occasionally wrong; the
statement is what the bytes say.

## Three excerpts

A room exit script. When a kid leaves the front of the mansion with the stamped, addressed envelope
in the closed mailbox and the flag raised, the publisher's mail cutscene trigger starts
([retail/room-scripts.c](retail/room-scripts.c), room 1):

```c
Exit {
    [0000] stopScript(52);                                                      // stop 'Repeat Outside Sound'
    [0002] stopSound(38);
    [0004] if (getState08(25)) {                                                // if Envelope in mailbox
    [0008]   if (Var[64] == 1) {                                                //  if Envelope Stamped
    [000D]     if (Var[85] == 1) {                                              //   if Envelope: Addressed
    [0012]       if (!getState08(17)) {                                         //    mailbox closed
    [0016]         if (getState08(18)) {                                        //     flag raised
    [001A]           VAR_RESULT = isScriptRunning(100);
    [001D]           if (VAR_RESULT == 0) {                                     //     if trigger not running
    [0022]             startScript(100);                                        //      start Cutscene: 3 Guys Who Pub Anything: Receive Mail Trigger
    [0024]           }
    ...
    [002E] stopObjectCode();
}
```

Object verb scripts. The mailbox flag in the same room answers four verbs; "Open" and "Close" hand
the sentence to the mailbox object ([retail/object-scripts.c](retail/object-scripts.c)):

```c
/** Mailbox Flag **/
roomobj-1-18.dmp

Open {
    [001E] doSentence(1,254,255);                                               // Open _cmdObject with _cmdObject2
    [0022] stopObjectCode();                                                    // --
}

Pull {
    [0028] setState08();                                                        // Raise Flag
    [0029] stopObjectCode();                                                    // --
}

Use {
    [002C] if (getState08(activeObject)) {                                      // If Flag Up
    [002F]   clearState08();                                                    //  Lower Flag
    [0030] } else {                                                             // -
    [0033]   setState08();                                                      //  Raise Flag
    [0034] }
    [0034] stopObjectCode();                                                    // --
}
```

A global script written as a user interface. The save/load screen waits for a button, then
records the result in `Var[108]`
([retail/global-scripts.c](retail/global-scripts.c), script 2):

```c
Script-2 {
    [0000] cutscene();                                                          // Start Cutscene
    [0001] cursorCommand( kModeKeypad )                                         // Keypad Mode
    [0003] setCameraAt(20);
    [0005] loadRoom(50);                                                        // SaveGame Room
    [0007] Var[108] = 0;                                                        // Game: Save/Load Status = Insert save-load disk

    [000A] beginOverride();
    {
        [000E] startScript(128);                                                    // start Game: Save/Load Disk Check Script
        [0010] Var[50] = 0;                                                         // SaveLoad: Button Pressed = Null
        [0013] breakHere();                                                         // Engine Tick
        [0014] unless (Var[50] != 0) goto 0013;                                     // Wait for button press
        ...
        [0043]     Var[90] = saveLoadGame(Var[50]);                                 // Load the game
```

## Retail and demo

The demo is a separate, much smaller game built with the same engine to advertise the full one.

| | Retail | Demo |
|---|---|---|
| Script slots in the index | 160 | 55 |
| Sound slots | 70 | 40 |
| Rooms used | 52 ([rooms.md](retail/tables/rooms.md)) | 14 ([rooms.md](demo/tables/rooms.md)) |
| Script 1 | Kid selection (any three kids, Dave fixed) | "Demo Mode Selection": the kids are fixed as Dave, Bernard and Razor (`Var[97..99] = 3, 5, 2`) |
| Hands-off mode | None | Script 6, "Demo: Hands-Off": Dave walks through the mansion and explains the game |
| Phone | Edna's number, the meteor police | One number, 1138 ([pins.md](demo/tables/pins.md)), which plays a recorded advertisement (script 48) |
| Story scripts | The full game: Edna, Ed, the tentacles, the meteor, the endings | The Edison family cutscene (7), Dr. Fred and Weird Ed at the doorbell (41), the intro (44, 45), a few room props |

The script numbers are not shared. The same piece of logic has different numbers in the two
releases: "Cutscene: Intro: Comet / Mansion" is script 115 in the retail game and 44 in the demo,
"Cutscene: Kid Dies" is 56 and 24, "Door: Open" is 4 in both. Object numbers mostly match (the
stamps are object 132 of room 1 in both) but object resources moved, so the offsets differ: the
stamps' "Pull/Pick up" handler starts at `[001C]` in the retail game and `[001A]` in the demo.
Always use the tables that belong to the release being run.

## The tables

The Markdown tables are conversions of the spreadsheets kept next to them, one sheet per file.

| Retail | Demo | Contents |
|---|---|---|
| [actors.md](retail/tables/actors.md) | [actors.md](demo/tables/actors.md) | Actor numbers: the seven kids (1 to 7), the Edisons, the tentacles, Sandy, the meteor |
| [rooms.md](retail/tables/rooms.md) | [rooms.md](demo/tables/rooms.md) | Room numbers and names, and whether each is outside or off the mansion grounds |
| [scripts.md](retail/tables/scripts.md) | [scripts.md](demo/tables/scripts.md) | Global script numbers, purpose, whether each is a cutscene, which script starts it, and its input and output variables |
| [variables.md](retail/tables/variables.md) | [variables.md](demo/tables/variables.md) | Game variables from 11 up: purpose, the script that owns them, and the meaning of their values |
| [objects.md](retail/tables/objects.md) | [objects.md](demo/tables/objects.md) | Objects by room and number, the meaning of their state bits, and the verbs they have handlers for |
| [sound.md](retail/tables/sound.md) | [sound.md](demo/tables/sound.md) | Sound and music numbers |
| [pins.md](retail/tables/pins.md) | [pins.md](demo/tables/pins.md) | The possible codes for the lab door, Edna's phone, Edna's safe and the meteor police (the demo has one phone number) |
| [bitvars.md](retail/tables/bitvars.md) | [bitvars.md](demo/tables/bitvars.md) | The bits used with `setBitVar` / `getBitVar` |
| [room-bitvars.md](retail/tables/room-bitvars.md) | | The per-room light flags |
| [actor-bitvars.md](retail/tables/actor-bitvars.md) | [actor-bitvars.md](demo/tables/actor-bitvars.md) | The actor flag masks: strong, Green Tentacle's friend, watched TV, Ed's enemy, frozen, hidden |
| [values.md](retail/tables/values.md) | [values.md](demo/tables/values.md) | Facing directions for `animateActor` and the verb numbers |

## Landmark scripts

Numbers from [retail/tables/scripts.md](retail/tables/scripts.md) unless marked as demo.

| Script | What it is |
|---|---|
| 1 | Intro: kid selection. The first script the engine runs. Also picks the random codes |
| 109 | Kid selection: update the selection when a portrait is clicked |
| 115 | Cutscene: intro, comet and mansion. Started by script 1 when the player presses start |
| 130 | Intro: the comet flies and crashes |
| 126 | Intro: Dave's welcome at the fence |
| 2, 128 | The save/load screen and its disk check |
| 3 | Verb defaults: what happens when an object has no handler for a verb |
| 6, 41, 118 | Meltdown timer, Dr. Fred's meltdown cutscene, the nuclear meltdown countdown |
| 31, 84 | Nurse Edna and Weird Ed take a kid to the dungeon |
| 143, 150 | Dr. Fred and Sandy; Purple Tentacle chases Sandy |
| 94, 110 | Weird Ed and Green Tentacle storm Dr. Fred's lab |
| 101 | The three guys who publish anything receive the mail |
| 119 | The talk show with the meteor |
| 56 | Cutscene: kid dies (`Var[76]` gives the cause) |
| 57 | Cutscene: game over (all kids dead, or the meltdown; `Var[78]`) |
| 58 | Cutscene: winning. Branches on `Var[82]` (the meteor's contract, which leads to the talk show) and `Var[96]` (whether Dave is dead) |
| Demo 1, 6 | Demo mode selection; the hands-off tour |
| Demo 7, 44, 48 | The Edison family cutscene; the intro; the recorded advertisement |

## From statement to engine handler

Every statement in these files is one opcode, and each opcode is handled by a routine in
[`../c64-engine/interpreter/`](../c64-engine/interpreter/). The dispatcher reads the opcode byte and
looks up the handler's address in two 256-entry tables, low bytes and high bytes, in
[`include/opcode_handlers.inc`](../c64-engine/include/opcode_handlers.inc). Bits 7, 6 and 5 of an
opcode say whether each operand is a constant or a variable number, so most statements have several
opcode values that reach the same handler (`$1D`, `$3D`, `$5D` ... `$FD` are all `setBitVar`).

These were checked by looking up each opcode in the table (ScummVM's `ScummEngine_v0::setupOpcodes`
in `engines/scumm/script_v0.cpp` gives the opcode for each statement) and finding the handler at the
resulting address:

| Statement | Opcode | Table entry | Handler |
|---|---|---|---|
| `cutscene()` | `$40` | `$677A` | `op_enter_cutscene`, [interpreter/ops_cutscene.asm](../c64-engine/interpreter/ops_cutscene.asm) |
| `endcutscene()` | `$C0` | `$67B0` | `op_exit_cutscene`, [interpreter/ops_cutscene.asm](../c64-engine/interpreter/ops_cutscene.asm) |
| `beginOverride()` | `$58` | `$60B6` | `op_interrupt_script`, [interpreter/ops_misc.asm](../c64-engine/interpreter/ops_misc.asm) |
| `startScript(n)` | `$42` | `$61CA` | `op_start_script`, [interpreter/ops_primitives.asm](../c64-engine/interpreter/ops_primitives.asm) |
| `Var[n] = x` | `$1A` | `$6106` | `op_copy_operand_to_variable`, [interpreter/ops_primitives.asm](../c64-engine/interpreter/ops_primitives.asm) |
| `if (a == b)` / `unless (a == b) goto` | `$48` | `$61A1` | `op_if_operand_eq`, [interpreter/ops_primitives.asm](../c64-engine/interpreter/ops_primitives.asm) |
| `setBitVar(n, bit, v)` | `$1D` | `$63AE` | `op_apply_bitmask_to_var`, [interpreter/ops_primitives.asm](../c64-engine/interpreter/ops_primitives.asm) |
| `breakHere()` | `$10` | `$6100` | `op_pause_task`, [interpreter/ops_primitives.asm](../c64-engine/interpreter/ops_primitives.asm) |
| `lights(n)` | `$70` | `$69AB` | `op_set_lights`, [interpreter/ops_misc.asm](../c64-engine/interpreter/ops_misc.asm) |
| `saveLoadGame(op)` | `$22` | `$6A04` | `op_save_load_game`, [interpreter/ops_misc.asm](../c64-engine/interpreter/ops_misc.asm) |

The handler names are the C64 engine's, not ScummVM's: `beginOverride` is `op_interrupt_script`
there because it records where to resume when the player interrupts. The full engine listing,
[`listings/engine-listing.txt`](../c64-engine/listings/engine-listing.txt), has the same mapping as a
list of `;#opcode address` lines.

## Using the scripts to build an interpreter

The scripts are the specification of the game, and the tables are its symbol file.

**Name things in the debugger.** An interpreter's trace of `Var[92] = 3` says nothing; "Kid
Select: Number Selected = 3" does. Load [variables.md](retail/tables/variables.md),
[scripts.md](retail/tables/scripts.md), [rooms.md](retail/tables/rooms.md) and
[objects.md](retail/tables/objects.md) into the debug output, so that a script slot shows as
"115 Cutscene: Intro: Comet / Mansion" and a state change as "Mailbox (17): Open".

**Compare a disassembly with a trace.** When a scene goes wrong, find the script that drives it in
the tables, read it here, and step the interpreter through the same offsets. The `[offset]` column
is what the interpreter's program counter shows, relative to the start of the script.

**Write tests from the scripts.** Statements and their comments make assertions that can be checked
against an interpreter running the real data:

- After script 1 starts, `Var[97]` is 3 (Dave is always kid 1), and room 45 is loaded.
- After script 1 finishes, `Var[32..35]` hold one of the five phone numbers in
  [pins.md](retail/tables/pins.md) (5235, 1547, 2275, 3444, 7537), and if it is not 5235 one of
  objects 86 to 89 has state `$08` set; bit 2 of bit variable 44 is set and bit 1 of bit variable 8
  is set.
- Entering room 1 when script 58 is not running starts script 52 (the outside sound); leaving it
  while script 6 runs starts sound 32 (the alarm).
- Running "Pull" on object 18 sets its state bit `$08`; "Use" toggles it.
- When an object has no handler for a verb, global script 3 runs with `VAR_ACTIVE_VERB` set to that verb.

Tests of this kind check opcode semantics, the scheduler, the sentence system and the resource
loader together, using the game itself as the test oracle.

## The DOS releases

The DOS releases (version 1 in 1987, version 2 "enhanced" in 1988) run the same game logic with a
different opcode encoding: ScummVM decodes them with `ScummEngine_v2::setupOpcodes` in
`engines/scumm/script_v2.cpp` rather than `script_v0.cpp`. Many opcodes keep their value
(`$40` cutscene, `$42` startScript, `$48` is equal, `$58` beginOverride, `$1A` assignment,
`$70` lights) but others do not: `breakHere` is `$10` in version 0 and `$80` in version 2, where `$10`
is `getObjectOwner`; `setBitVar` is `$1D` in version 0 and `$1B` in version 2, where `$1D` is
`ifClassOfIs`. The verb-table wildcard is `$0F` in version 0 and `$FF` in versions 1 and 2. The
engine variables also move (`VAR_OVERRIDE` is 6 in version 0 and 5 in version 2, see `vars.cpp`).

The numbers in these files therefore do not carry over unchanged:

- **Script numbers are shifted, by an amount that varies.** Comparing the index files (which room
  each script is stored in) and the text inside the scripts shows that C64 script 9 ("Memoir:
  Reading") is DOS script 15 and script 41 (Dr. Fred's meltdown) is 47, a shift of 6. From roughly
  script 50 on the shift is 5: scripts 56, 57 and 58 (kid dies, game over, winning) are 61, 62 and
  63, script 115 (the intro) is 120 and 126 (Dave's welcome) is 131. V1 and V2 agree on all of
  these. ScummVM's workarounds, which all concern high-numbered scripts, use the macro
  `MM_SCRIPT(script)` (`script + 5` outside version 0) in `script_v2.cpp`. The first scripts have
  no simple counterpart: V1 stores its scripts 1 to 6 in room 45 (the kid selection room) and V2
  its scripts 1 to 5, where the C64 stores only script 1 of its first scripts there.
- **Variable and object numbers differ by no fixed amount.** The same file has `MM_VALUE(v0, v1)`
  for these: `Var[104]` here ("Weird Ed: Is Storming Lab") is variable 120 in DOS, and object 124
  (the circuit breaker) is 157.

To use these scripts with a DOS release, find the corresponding script by its content (its text, or
the room it is stored in), not by number.

## Where the scripts are on the disk

[c64-disk-layout.txt](c64-disk-layout.txt) is a terse note on how the C64 game finds its resources.
In full:

The game disks are standard 1541 images: 35 tracks of 256-byte sectors, 21 sectors per track on
tracks 1 to 17, 19 on 18 to 24, 18 on 25 to 30 and 17 on 31 to 35. A `.d64` file is those sectors
in order, so a track and sector become a file offset with a table of the number of sectors before
each track. That is the `C64SectorOffset` table in the note (entry 0 is unused because tracks start
at 1):

```
offset = (C64SectorOffset[track] + sector) * 256
```

The game does not use the disk's file directory for its data. Disk 1 starts (track 1, sector 0) with
an index, read in this order (ScummVM's `ScummDiskImage::extractIndex` in
`engines/scumm/file.cpp`):

| Disk 1 offset | Size | Contents |
|---|---|---|
| `0x000` | 2 | Signature: the bytes `31 0A` (disk 2 starts with `32 01`) |
| `0x002` | 256 | One byte per global object: owner in the low nibble, state in the high nibble |
| `0x102` | 55 | Room disk numbers, the characters `'1'` or `'2'`; 0 for unused rooms |
| `0x139` | 110 | Room locations: for each room, one byte sector then one byte track |
| `0x1A7` | 25 | Costume room numbers |
| `0x1C0` | 50 | Costume offsets within their rooms, 16-bit little-endian |
| `0x1F2` | scripts | Script room numbers, one byte per script |
| then | 2 × scripts | Script offsets within their rooms, 16-bit |
| then | sounds, then 2 × sounds | Sound room numbers and offsets |

The number of scripts and sounds differs: the demo has 55 and 40, the retail game 160 and 70. The
note's figures (`0x37` script room bytes, script offsets at `0x229`, the per-room resource counts
`maniacDemoResourcesPerFile`) are the demo's. In the retail game the script offsets begin at
`0x292`.

A room's data, starting at its track and sector, is a run of resources, each prefixed with its own
16-bit length (the note's "Room Format: Length of resource, Resource"). The first is the room
itself; the others are the scripts, costumes and sounds stored with it. The number of resources in
each room is not in the index, so ScummVM keeps it in tables (`maniacResourcesPerFile` and
`maniacDemoResourcesPerFile` in `file.cpp`). A script is found by taking its room's location and
adding the script's offset.

The note's example is from the demo: room 10 at track 6 (105 sectors in), sector 13, so `(0x69 +
0x0D) * 256 = 0x7600`, and script 6 at offset `0x0D42` from there. The same calculation on the
retail disk: script 1 is stored in room 45, which is on disk 1 at track 1 sector 5 (offset `0x500`);
its offset is `0x0AD1`, so its resource starts at `0x0FD1` with the length `0x0245`, and after the
4-byte header its code begins `40 60 01 0C 01 53 01`: `cutscene()`, `cursorCommand(1)`,
`loadSound(1)`, `lockSound(1)`, the first statements of `Script-1` above.

The C64 engine does the same with raw sector reads ([`disk/`](../c64-engine/disk/)); ScummVM does it
in `ScummDiskImage`. See [../c64-loader/README.md](../c64-loader/README.md) for why the boot loader
plays no part in this.
