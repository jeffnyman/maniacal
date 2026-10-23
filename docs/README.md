# Maniac Mansion, inside and out

This is a guide to Maniac Mansion (Lucasfilm Games, 1987): how the game was built, how its engine
worked on the Commodore 64, and how to build an interpreter that plays it today. It brings
together a fully commented reconstruction of the original C64 engine, a commented disassembly of
the game's own scripts with tables naming every variable, object and room, the C64 boot loader and
copy protection, an editable rebuild of the NES release, and the game's maps, manual and walkthroughs.

It is written for two kinds of reader, and the material serves both:

- **Someone who wants to understand how Maniac Mansion worked.** How a 1987 adventure game fit
  rooms, characters, path finding, a script language and music into a 64 KB home computer.
- **Someone building an interpreter for it**, such as [Maniacal](../README.md) or ScummVM: what the
  parts are, what each must do, and where to check the details.

## Reading paths

**To understand the game and its engine:**

1. [The game](game/README.md): the story, the kids, the releases, the maps. Know the game first.
2. [How Maniac Mansion works](how-maniac-mansion-works.md): the engine and the game's model,
   explained from top to bottom, with links into the source for every part.
3. [The game's scripts](scripts/README.md): the game itself as code. Read the intro script and
   the scripts of a few objects you know.
4. [The C64 engine](c64-engine/README.md): the 6502 source, module by module.
5. [The C64 boot loader](c64-loader/README.md): how the game loaded itself and protected itself
   from copying.

**To build an interpreter:**

1. [Building an interpreter](building-an-interpreter.md): the layers, the differences between
   versions 0, 1 and 2, and a table mapping each subsystem to the C64 engine, to ScummVM and to Maniacal.
2. [How Maniac Mansion works](how-maniac-mansion-works.md), for the model the interpreter must
   reproduce.
3. [The game's scripts](scripts/README.md) and their [tables](scripts/retail/tables/), as the
   specification and as the source of test cases.
4. [The C64 engine](c64-engine/README.md), when ScummVM's code and the game disagree, or the
   reason for a behaviour isn't clear.

The [glossary](glossary.md) defines the terms used throughout.

## Contents

| Section | What it holds |
|---|---|
| [how-maniac-mansion-works.md](how-maniac-mansion-works.md) | The narrative overview of the engine and the game's model |
| [building-an-interpreter.md](building-an-interpreter.md) | The guide to reimplementing the engine |
| [glossary.md](glossary.md) | Terms: room, object, actor, costume, walkbox, script slot, sentence, and more |
| [game/](game/README.md) | The game as a player sees it: premise, releases, walkthrough, hints, FAQ, dead ends, manual, maps |
| [c64-engine/](c64-engine/README.md) | The commented 6502 source of the C64 engine (SCUMM version 0), in module folders, with full listings and data-structure notes |
| [c64-loader/](c64-loader/README.md) | The ten-stage C64 fast loader, decryption and copy protection, with diagrams |
| [scripts/](scripts/README.md) | The game's global, room and object scripts for the C64 retail release and demo, with tables of actors, rooms, scripts, variables, sounds, objects and codes |
| [nes/](nes/README.md) | A rebuild of the NES release with all graphics and layouts decompressed into source, bug fixes, and a new puzzle written from scratch |

## Versions

Most of the source material is the **Commodore 64 release**, the original, which runs
**SCUMM version 0**. The DOS releases run versions 1 and 2, and the NES release a version 1
variant. The game logic is the same everywhere, but the encodings and some engine behaviour differ.
[Building an interpreter](building-an-interpreter.md#the-versions-you-have-to-handle) lists the
differences.
