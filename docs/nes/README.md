# The NES release: an expanded, editable ROM

Maniac Mansion on the Nintendo Entertainment System (Jaleco, 1990) runs the same game as the
computer versions on a port of the SCUMM engine written for the NES. Its graphics and room layouts
are stored run-length encoded (RLE) and packed tightly into the cartridge's banks. That makes them
hard to read or change.

This folder rebuilds the US ROM into an **expanded ROM**. All of that data is decompressed into
plain, assembler-readable source. The ROM is doubled in size, so every room has room for its full
tile set, and the engine's decompression routine is patched to read the plain data. The result
plays exactly like the original, apart from the fixes listed below, and is a convenient base for
studying or changing the NES data.

For anyone building an interpreter, this folder is a worked example of the NES variant of the
resource formats. It also shows object and script data written by hand (see [The puzzle chain](#the-puzzle-chain)).

## What is here

| Path | What it is |
|---|---|
| [`build.bat`](build.bat) | Builds the expanded ROM and the patch files from a US ROM. Windows only. |
| [`src/decompress.asm`](src/decompress.asm) | The runtime change: the engine's tile and layout loaders are patched to read uncompressed data and to switch to the new outer bank. |
| [`src/clear_data.asm`](src/clear_data.asm), [`src/clear_banks.asm`](src/clear_banks.asm) | Blank out the old compressed data and the new banks before the plain data is written in. |
| [`src/decompressed_tiles.asm`](src/decompressed_tiles.asm) | Every room's tile graphics, uncompressed (generated). |
| [`src/decompressed_layouts.asm`](src/decompressed_layouts.asm), [`src/layout_metadata.asm`](src/layout_metadata.asm) | Every room's nametable layout, uncompressed, and the per-room metadata that points to it (generated). |
| [`src/decompressed_title_screens.asm`](src/decompressed_title_screens.asm), [`src/title_sprites.asm`](src/title_sprites.asm) | The title screens and their sprites. |
| [`src/remap_table.asm`](src/remap_table.asm) | The tile table, repointed at the uncompressed tiles. |
| [`src/patches/`](src/patches) | Bug fixes and mouse support, as IPS patches (listed below). |
| [`src/patches/optional/`](src/patches/optional) | Optional graphics improvements, with their sources. |
| [`src/puzzle/`](src/puzzle) | A new puzzle chain and room, written as source (see below). |
| [`tools/generator/`](tools/generator) | The generator that reads the ROM and writes the generated sources above. `generator.js` runs under Node; `generator.html` is the same tool in a browser page. It also dumps the NES scripts with `descumm -2`. |
| [`tools/`](tools) | Third-party command line tools the build uses, with their licences: `snarfblasm` (6502 assembler), `flips` (IPS/BPS patcher), `dd`, and `descumm` (SCUMM script decompiler). |
| [`notes/costume-format.txt`](notes/costume-format.txt) | The NES costume resource format and the frame sets of the main characters. |
| [`notes/ram-addresses.txt`](notes/ram-addresses.txt) | The RAM bytes that hold each actor's current room, and the NES room numbers, for moving characters around a running game in an emulator. |

The main SCUMM structures look the same here as on the computers. `src/puzzle/chainsaw.asm` lays out
an object in full: size, object number, position, width, parent, walk-to point, height and facing,
name offset, then a verb table pointing into the object's scripts. Compare it with
[`../c64-engine/data-structures.txt`](../c64-engine/data-structures.txt) and with ScummVM's
`engines/scumm/file_nes.cpp` and `gfx_nes.cpp`.

## Building

1. Put a US ROM named `Maniac Mansion (USA).nes` in this folder. The game file is not part of this
   resource. It should match SHA-1 `8A8BBECC77FDF59826257754F357D38A7F825971` (ROM data) or
   `7317D1F1096B57F6AB8F3001BCDD35665C291B1A` (whole file).
2. Run `build.bat`.
3. Open `Maniac Mansion (USA) Expanded.nes` in an NES emulator.

The build never changes the original ROM. It patches a temporary working copy, then writes the
expanded ROM and BPS patches against the clean original.

| Command | Result |
|---|---|
| `build` | The expanded ROM with fixes and the optional patches (the default) |
| `build base` | Fixes only, no optional patches |
| `build puzzle` | Also adds the puzzle chain |
| `build title` | Also uses the Japanese title screen, which is based on the C64 artwork |
| `build ... release` | Also packages the BPS patches into a zip |
| `build patches` | Regenerates the generated sources from the ROM (needs Node) |
| `build clean` | Deletes everything the build made |

## Fixes and additions

Always applied:

- Mouse support (`maniac_mousedriver.ips`).
- The glitched graphics in the room under the house, on the arcade's coin box and on the developer
  under the house are fixed.
- A leftover pennant object in Weird Ed's room and a leftover keypad object in the hallway are
  removed.
- The radioactive slime in the basement can be interacted with again.
- The fence mask on the starting screen is fixed, so the kids are fully behind it.
- The colours of the right-hand gargoyle on the stairs are corrected.
- The border colour is fixed when selecting Michael on the character select screen.
- The meteor's sprite is fixed.
- The Lucasfilm logo is centred vertically and its misplaced shine sprite is fixed.

Optional, applied by default:

- `char_select.ips`: rounded corners on the character select screen.
- `fridge.ips`: the lettuce is green and the batteries brown.
- `portraits.ips`: better portraits of Fred in Edna's room, Edna in Fred's room, Fred in Edna's
  attic and the family portrait in the den.
- `under_house_enhanced.ips`: better post and valve graphics under the house and a fixed background mask.
- `title_screen.ips`: the Japanese title screen (only with `build title`).

## The puzzle chain

`build puzzle` adds a small new puzzle chain to the game. It changes existing objects, adds new
ones and ends in a new room. It does not touch the rest of the game. Its sources are the most
direct illustration in this resource of how SCUMM rooms, objects and scripts fit together:

| File | What it defines |
|---|---|
| [`scumm_codes.asm`](src/puzzle/scumm_codes.asm) | Verb numbers, string control codes and other script constants |
| [`object_ids.asm`](src/puzzle/object_ids.asm) | The object, room and actor numbers involved |
| [`chainsaw.asm`](src/puzzle/chainsaw.asm) | The chainsaw object and its verb scripts |
| [`stairs.asm`](src/puzzle/stairs.asm) | The rotted stairs, repaired with the tools and boards, and the fixed stairs that replace them |
| [`model_room.asm`](src/puzzle/model_room.asm) | The new room, which holds a model of an AT-AT walker scene: its header, walkboxes and box matrix, objects, entry and exit scripts, and its entry in the room table |
