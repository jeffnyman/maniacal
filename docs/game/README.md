# The game

This section covers Maniac Mansion as a player sees it: the story, the kids, the puzzles and the
map. Knowing the game well makes the rest of this resource easier to follow. A script, a variable
or an object number means much more once you know which puzzle it belongs to.

## Premise

Twenty years ago a meteor fell near the Edison family's mansion. Now Dr. Fred, his wife Nurse Edna
and their son Weird Ed are under its control, and Dave's girlfriend Sandy has been kidnapped for
one of Dr. Fred's experiments. Dave and two friends, chosen from six other kids, break into the
mansion to rescue her. Also in the house: the purple and green tentacles, Cousin Ted, the
man-eating plant, and the meteor police.

The kids are Dave (always in the team), Syd, Razor, Michael, Bernard, Wendy and Jeff. Each has
different skills, so the team decides which solutions are open and which endings can be reached.
Kids can be caught and locked in the dungeon, can die, and can be left in dead ends. That is why the
game's state is mostly a large set of variables and object states that the scripts test.

The player builds sentences from a fixed list of verbs (Walk to, Push, Pull, Give, Open, Close,
Read, Pick up / Get, What is, Unlock, New kid, Turn on, Turn off, Fix, Use) and the objects on screen
or in the inventory. The sentence line shows the sentence as it is being built. How the engine
turns that sentence into a running script is covered in [../scripts/](../scripts/) and
[../c64-engine/](../c64-engine/).

## Releases

| Release | Year | SCUMM version | Notes |
|---|---|---|---|
| Commodore 64 | 1987 | 0 | The original. Two disk sides, a custom fast loader and disk copy protection, no in-game door code. |
| Apple II | 1987 | 0 | Close to the C64 release. |
| DOS ("V1") | 1987 | 1 | Keeps the C64-style character graphics. Adds the door-code copy protection. |
| DOS, Amiga, Atari ST ("V2", enhanced) | 1988 | 2 | Redrawn graphics in 16 colours, on the engine generation Zak McKracken also uses. |
| NES | 1990 | 1 (NES dialect) | Censored and partly redrawn. See [../nes/](../nes/). |

## What is here

| File | What it is |
|---|---|
| [`walkthrough.pdf`](walkthrough.pdf) | A full walkthrough, with the solutions for each kid and the tricks for escaping the dungeon and getting Ed's package. |
| [`hints.pdf`](hints.pdf) | Short hints and tricks: skipping the arcade, two hamsters, getting past Nurse Edna, and more. |
| [`dead-ends.txt`](dead-ends.txt) | The ways the game can become unwinnable. Handy as test cases. |
| [`nes-faq.txt`](nes-faq.txt) | A long FAQ for the NES release: what is in each room, the ways past the purple tentacle, how to reach each ending, and more. The computer versions differ a little. |
| [`nes-manual.pdf`](nes-manual.pdf) | The NES instruction manual. |
| [`maps/`](maps) | Maps of the mansion (below). |

## Maps

| Map | Shows |
|---|---|
| [`c64-room-map.png`](maps/c64-room-map.png) | Every room of the C64 release by number, including the rooms that are not places in the house: the title and kid-selection screens, close-ups, the arcade's high-score screen, the save/load screen. |
| [`c64-room-connections.png`](maps/c64-room-connections.png) | The same rooms as a tree of which room leads to which. |
| [`c64-demo-room-map.png`](maps/c64-demo-room-map.png) | The smaller set of rooms in the C64 demo. |
| [`dos-v2-room-map.png`](maps/dos-v2-room-map.png) | The rooms of the house in the enhanced V2 release, numbered and laid out roughly where they sit in the mansion. |
| [`nes-map.png`](maps/nes-map.png), [`nes-map-large.png`](maps/nes-map-large.png) | The NES mansion. |

The C64 and V2 maps use the same room numbers: 1 is the front of the mansion, 5 the library, 7 the kitchen.
These numbers match the room table in [../scripts/retail/tables/rooms.md](../scripts/retail/tables/rooms.md) and the
`NN.LFL` file numbers of the DOS releases. The NES room list in
[../nes/notes/ram-addresses.txt](../nes/notes/ram-addresses.txt) uses them too.
