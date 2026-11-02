using System.Text;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Game;

/// <summary>A sentence waiting for the sentence script: a verb and up to two objects. Mirrors <c>SentenceTab</c>.</summary>
internal readonly record struct Sentence(int Verb, int ObjectA, int ObjectB)
{
    public bool HasPreposition => ObjectB != 0;
}

/// <summary>
/// The player's side of the world: the verbs, the sentence, the inventory and the mouse and
/// keyboard. Mirrors ScummVM's <c>processInput</c>, <c>ScummEngine_v2::checkExecVerbs</c>,
/// <c>runInputScript</c>, <c>checkAndRunSentenceScript</c>, <c>handleMouseOver</c> and the
/// verb, sentence and inventory instructions of <c>script_v2.cpp</c>.
/// </summary>
public sealed partial class GameWorld
{
    // engine variables of the interface (ScummEngine_v2::setupScummVars)
    internal const int VarActiveVerb = 8, VarActiveObject1 = 9, VarActiveObject2 = 10, VarVerbAllowed = 18,
        VarSentenceVerb = 26, VarSentenceObject1 = 27, VarSentenceObject2 = 28, VarSentencePreposition = 29,
        VarVirtMouseX = 30, VarVirtMouseY = 31, VarClickArea = 32, VarClickVerb = 33, VarClickObject = 35,
        VarBackupVerb = 38, VarKeypress = 39;

    /// <summary>The global script clicks and keys go to. Mirrors <c>verbScript = 4</c> in <c>ScummEngine_v2::runInputScript</c>.</summary>
    internal const int InputScript = 4;

    // where a click was (verbs.h: kVerbClickArea and the rest)
    internal const int VerbClickArea = 1, SceneClickArea = 2, InventoryClickArea = 3, KeyClickArea = 4, SentenceClickArea = 5;

    /// <summary>Key codes the input takes besides characters: the function keys are 315 to 323, as in ScummVM.</summary>
    public const int KeyEscape = 27, KeyF1 = 315, KeyF4 = 318, KeyF9 = 323;

    // the Amiga's cross: its arms' first pixel from the hotspot, and their length
    private const int AmigaCursorGap = 3, AmigaCursorArm = 5;

    // the pixels of a cursor picture that take the cursor's colour
    private const byte AtariStCursorMarked = 0x0F;

    // the NES's save screen's script (processKeyboard's runScript(163))
    private const int NesSaveMenuScript = 163;

    /// <summary>The owner of an object that is in its room. Mirrors <c>OF_OWNER_ROOM</c>.</summary>
    internal const int OwnerRoom = 0x0F;

    // the sentence verbs o2_doSentence handles itself
    private const int VerbStopObject = 254, VerbSpecial = 253, VerbBackground = 250, ClearSentences = 252, ResetSentenceLine = 251;

    // Mirrors NUM_SENTENCE.
    private const int SentenceCount = 6;

    // Mirrors MBS_LEFT_CLICK and MBS_MAX_KEY.
    private const int LeftClick = 0x8000, MaxKey = 0x200;

    // the cursor's colours as it animates (default_cursor_colors), and the C64's (default_v0_cursor_colors)
    private static readonly byte[] CursorColors = [15, 15, 7, 8];
    private static readonly byte[] CursorColorsV0 = [1, 1, 12, 11];

    private readonly List<Sentence> _sentences = [];
    private readonly List<int> _inventory = [];
    private readonly Dictionary<int, RoomObject> _carried = [];
    private readonly Dictionary<int, (int Left, int Right)> _actorStrips = [];
    private int _input;
    private bool _clicked, _rightClicked;
    private int _key;
    private int _cursorFrame;
    private (int Room, CameraMode Mode) _beforeCutscene;

    /// <summary>The verbs, the sentence line and the inventory below the room.</summary>
    internal VerbArea Verbs { get; private set; } = null!;

    /// <summary>The sentences waiting for the sentence script. Mirrors <c>_sentence</c> and <c>_sentenceNum</c>.</summary>
    internal IReadOnlyList<Sentence> Sentences => _sentences;

    /// <summary>The objects the kids carry, in the order they were picked up. Mirrors <c>_inventory</c>.</summary>
    public IReadOnlyList<int> Inventory => _inventory;

    /// <summary>The mouse, in screen pixels. Mirrors <c>_mouse</c>.</summary>
    public (int X, int Y) Mouse { get; private set; }

    /// <summary>Whether the cursor shows. Mirrors <c>_cursor.state &gt; 0</c>, which version 2 keeps with <c>_userPut</c>.</summary>
    public bool CursorShown => Engine.CursorOn;

    /// <summary>Moves the mouse to a screen point.</summary>
    public void MoveMouse(int x, int y) => Mouse = (Math.Clamp(x, 0, Layout.Width - 1), Math.Clamp(y, 0, Layout.Height - 1));

    /// <summary>
    /// A mouse click for the next step to handle; the right button skips cutscenes, as Escape does.
    /// On the NES, whose scripts make what is under the mouse the sentence's object as it passes
    /// over (script 7, by <c>findObject</c>), a click somewhere the mouse was not is handled a step
    /// later, once the mouse has been there for a step, as a player's mouse is before it clicks.
    /// </summary>
    public void Click(int x, int y, bool right = false)
    {
        var before = Mouse;
        MoveMouse(x, y);
        if (right)
        {
            _rightClicked = true;
        }
        else if (IsNes && Mouse != before)
        {
            _clickAfterHover = true;
        }
        else
        {
            _clicked = true;
        }
    }

    // the NES: a click waiting a step for the scripts to see the mouse where it is
    private bool _clickAfterHover;

    /// <summary>A key for the next step to handle: a character, <see cref="KeyEscape"/>, or a function key from <see cref="KeyF1"/>.</summary>
    public void PressKey(int key) => _key = key;

    /// <summary>
    /// Draws the cursor on a screen: the version 2 crosshair, its hotspot at the mouse, in the
    /// colour its animation is at; the Atari ST's is a picture of its own, the NES's a tile of its
    /// costume set. Mirrors
    /// <c>ScummEngine_v2::setBuiltinCursor</c>.
    /// </summary>
    public void DrawCursor(Surface8 screen)
    {
        if (!CursorShown)
        {
            return;
        }

        if (IsNes)
        {
            DrawNesCursor(screen);
            return;
        }

        // paused, the snail (printMessageAndPause's setSnailCursor)
        if (_banner == Banner.Pause)
        {
            DrawSnail(screen);
            return;
        }

        byte color = (Version == 0 ? CursorColorsV0 : CursorColors)[(_cursorFrame >> 1) & 3];
        var (x, y) = Mouse;

        // the Amiga's is a cross, its arms 3 to 7 pixels from the hotspot, each with an arrowhead
        // pointing in (setBuiltinCursor)
        if (IsAmiga)
        {
            for (int i = AmigaCursorGap; i < AmigaCursorGap + AmigaCursorArm; i++)
            {
                Plot(-i, 0);
                Plot(i, 0);
                Plot(0, -i);
                Plot(0, i);
            }

            for (int i = 1; i <= 2; i++)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    Plot(side * (AmigaCursorGap + i), -i);
                    Plot(side * (AmigaCursorGap + i), i);
                    Plot(-i, side * (AmigaCursorGap + i));
                    Plot(i, side * (AmigaCursorGap + i));
                }
            }

            return;
        }

        // the Atari ST's is a picture of its own, its marked pixels in the colour (atari_main_cursor)
        if (IsAtariSt)
        {
            for (int i = 0; i < CursorImages.AtariSt.Length; i++)
            {
                int px = x - AtariStCursorHotspot + i % AtariStCursorSize, py = y - AtariStCursorHotspot + i / AtariStCursorSize;
                if (CursorImages.AtariSt[i] == AtariStCursorMarked && px >= 0 && px < screen.Width && py >= 0 && py < screen.Height)
                {
                    screen[px, py] = color;
                }
            }

            return;
        }

        void Plot(int dx, int dy)
        {
            int px = x + dx, py = y + dy;
            if (px >= 0 && px < screen.Width && py >= 0 && py < screen.Height)
            {
                screen[px, py] = color;
            }
        }

        for (int i = 0; i < 7; i++)
        {
            Plot(-5 - i, 0);
            Plot(5 + i, 0);
        }

        for (int i = 0; i < 8; i++)
        {
            Plot(0, -3 - i);
            Plot(0, 3 + i);
        }

        for (int i = 1; i <= 3; i++)
        {
            Plot(-5 - i, -i);
            Plot(-5 - i, i);
            Plot(5 + i, -i);
            Plot(5 + i, i);
            Plot(-i, -3 - i);
            Plot(i, -3 - i);
            Plot(-i, 3 + i);
            Plot(i, 3 + i);
        }

        Plot(-7, -1);
        Plot(7, -1);
        Plot(-7, 1);
        Plot(7, 1);
        Plot(-1, -5);
        Plot(1, -5);
        Plot(-1, 5);
        Plot(1, 5);
    }

    // the NES's cursor tile in its costume sets' tiles (setBuiltinCursor)
    private const int NesCursorTile = 0xFA;

    /// <summary>
    /// The NES's cursor: tile 0xFA of the costume set, 8 by 8 from the mouse, in the set's first
    /// sub-palette, its second at the animation's fourth step; the colour of its last pixel is
    /// not drawn (<c>transColor</c>). Nothing shows before the scripts choose a set.
    /// </summary>
    private void DrawNesCursor(Surface8 screen)
    {
        if (NesCursorImage() is not { } image)
        {
            return;
        }

        var (x, y) = Mouse;
        for (int i = 0; i < 64; i++)
        {
            int px = x + i % 8, py = y + i / 8;
            if (image[i] != image[63] && px < screen.Width && py < screen.Height)
            {
                screen[px, py] = image[i];
            }
        }
    }

    /// <summary>The NES's cursor tile, 8 by 8 in the colours the animation is at, or null before the scripts choose a costume set.</summary>
    private byte[]? NesCursorImage()
    {
        if (CurrentNesCostumeSet is not { } set)
        {
            return null;
        }

        int subPalette = ((_cursorFrame >> 1) & 3) == 3 ? 4 : 0;
        var image = new byte[64];
        for (int row = 0; row < 8; row++)
        {
            byte plane0 = set.Patterns[NesCursorTile * 16 + row], plane1 = set.Patterns[NesCursorTile * 16 + row + 8];
            for (int bit = 0; bit < 8; bit++)
            {
                image[row * 8 + bit] = set.Palette[((plane0 >> (7 - bit)) & 1) | (((plane1 >> (7 - bit)) & 1) << 1) | subPalette];
            }
        }

        return image;
    }

    /// <summary>
    /// Where an object is: 1 carried, 2 in the current room, 0 nowhere to be found. Mirrors
    /// <c>whereIsObject</c>: an object not owned by the room is only ever in the inventory. In
    /// version 0 an object that is part of a room, or an actor, has no owner and is only looked
    /// for in the room.
    /// </summary>
    internal int WhereIs(int obj)
    {
        bool owned = Version != 0 || obj >> 8 == TypeForeground;
        if (obj < 1 || (owned && obj >= Engine.ObjectOwners.Length))
        {
            return 0;
        }

        if (owned && Engine.GetOwner(obj) != OwnerRoom)
        {
            return obj != 0 && _inventory.Contains(obj) ? 1 : 0;
        }

        return CurrentRoom?.FindObject(obj) is not null ? 2 : 0;
    }

    /// <summary>An object's code block, carried or in the room. Mirrors <c>getOBCDFromObject</c>.</summary>
    internal RoomObject? ObjectCode(int obj) => WhereIs(obj) switch
    {
        1 => _carried[obj],
        2 => CurrentRoom!.FindObject(obj),
        _ => null,
    };

    /// <summary>The n-th object (from 1) an actor carries, or 0. Mirrors <c>findInventory</c>.</summary>
    internal int FindInventory(int owner, int index)
    {
        int count = 1;
        foreach (int obj in _inventory)
        {
            if (obj != 0 && Engine.GetOwner(obj) == owner && count++ == index)
            {
                return obj;
            }
        }

        return 0;
    }

    /// <summary>How many objects an actor carries. Mirrors <c>getInventoryCount</c>.</summary>
    internal int InventoryCount(int owner) => _inventory.Count(obj => obj != 0 && Engine.GetOwner(obj) == owner);

    /// <summary>
    /// The topmost object at a point of the room, in pixels, that is not untouchable and whose
    /// parents are in the states it needs; or 0. Mirrors <c>findObject</c> for versions 1 and 2.
    /// </summary>
    internal int FindObject(int x, int y)
    {
        if (CurrentRoom is not { } room)
        {
            return 0;
        }

        foreach (var obj in room.Objects)
        {
            if (obj.Number < 1 || (StateOf(obj.Number) & ScriptEngine.StateUntouchable) != 0
                || !RoomRenderer.ParentsAllow(room, obj, StateOf))
            {
                continue;
            }

            var (left, top) = PositionOf(obj);
            if (left <= x && left + obj.Width > x && top <= y && top + obj.Height > y)
            {
                return obj.Number;
            }
        }

        return 0;
    }

    /// <summary>
    /// The actor at a point of the room, in pixels: one drawn in the point's column, standing at most
    /// 40 lines below it. The kid being played is never found. Mirrors <c>getActorFromPos</c>.
    /// </summary>
    internal int ActorFromPos(int x, int y)
    {
        int strip = x / 8;
        for (int i = 1; i < ActorCount; i++)
        {
            if (i == Engine.Variables[ScriptEngine.VarEgo] || !_actorStrips.TryGetValue(i, out var strips) || strip < strips.Left || strip > strips.Right)
            {
                continue;
            }

            var actor = Actors[i];
            int bottom = actor.Y * 2;
            int top = bottom - 80;
            if (Version <= 1)
            {
                // only version 1 of Maniac Mansion lifts the range by the actor's elevation
                bottom = (byte)(bottom - actor.Elevation);
                top = (byte)(top - actor.Elevation);
            }
            else if ((ushort)top > 128)
            {
                top = 1;
            }

            if (y >= top && y <= bottom)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Carries out the verb, sentence and inventory instructions; false for the others.</summary>
    private bool ExecuteInterface(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments, out int result)
    {
        result = 0;
        switch (instruction.Handler)
        {
            case "o2_verbOps":
                VerbOps(arguments, instruction);
                return true;

            case "o2_doSentence":
                DoSentence(arguments);
                return true;

            // o2_waitForSentence: the script waits while sentences wait or the sentence script runs
            case "o2_waitForSentence":
                if (_sentences.Count > 0 || engine.IsScriptRunning(ScriptEngine.SentenceScript))
                {
                    engine.Repeat();
                }

                return true;

            case "o2_drawSentence":
                DrawSentence();
                return true;

            // o2_findObject and o2_actorFromPos take actor units
            case "o2_findObject":
                result = FindObject(arguments[0] * 8, arguments[1] * 2);

                // the NES finds an object in its inventory by the box under the mouse, its list
                // the other way round (o2_findObject's "simulate inverse order")
                if (result == 0 && IsNes && (Engine.Interface & VerbArea.ShowInventory) != 0 && Verbs.MouseOverBox is >= 0 and < 4)
                {
                    int ego = Engine.Variables[ScriptEngine.VarEgo];
                    result = FindInventory(ego, InventoryCount(ego) - Verbs.InventoryOffset - Verbs.MouseOverBox);
                }

                return true;
            case "o2_actorFromPos":
                result = ActorFromPos(arguments[0] * 8, arguments[1] * 2);
                return true;

            case "o2_getObjPreposition":
                result = ObjectCode(arguments[0]) is { } code ? code.Preposition : 0xFF;
                return true;

            case "o2_pickupObject":
                PickUp(arguments[0]);
                return true;

            // the NES keeps its prepositions apart from its objects, and changes none
            case "o2_setObjPreposition" when IsNes:
                return true;

            // the NES's costumes come in two sets, the kids' and the rest (NES_loadCostumeSet)
            case "o2_switchCostumeSet" when IsNes:
                NesCostumeSetNumber = arguments[0] & 1;
                _nesCostumeSetChosen = true;
                return true;

            case "o4_saveLoadGame":
                result = SaveLoadGame(arguments[0], instruction);
                return true;

            // the user state changes of cursorCommand, cutscene and endCutscene redraw the interface (ScummEngine_v2::setUserState)
            case "o2_cursorCommand":
                RedrawInterface();
                return true;

            // o2_cutscene: the room and camera are kept for version 1, and the sentences go
            case "o2_cutscene":
                CutsceneBegun();
                return true;

            // o2_endCutscene: the camera follows the kid again; version 1 puts back the camera it had, or the room
            case "o2_endCutscene":
                RedrawInterface();
                EndCutscene();
                return true;

            case "o2_restart":
                Restart();
                return true;

            // setOwnerOf: an object given to nobody leaves the inventory; either way the inventory redraws (runInventoryScript)
            case "o2_setOwnerOf":
                if (arguments[1] == 0)
                {
                    ClearOwnerOf(arguments[0]);
                }

                RedrawInventory();
                return true;

            default:
                return false;
        }
    }

    /// <summary>Mirrors <c>o2_verbOps</c>: delete a verb, switch one on or off, or make one.</summary>
    private void VerbOps(IReadOnlyList<int> arguments, Instruction instruction)
    {
        switch (arguments[0])
        {
            case 0:
                Verbs.Kill(arguments[1] + 1);
                break;
            case 0xFF:
                Verbs.SetMode(arguments[1], arguments[2]);
                break;
            default:
                Verbs.SetVerb(arguments[3] + 1, arguments[0], arguments[1], arguments[2], arguments[4], instruction.Operands[5].Text, Version, IsNes);
                break;
        }
    }

    /// <summary>
    /// Mirrors <c>o2_doSentence</c>: 252 clears the sentences and stops the sentence script, 251
    /// resets the sentence line; otherwise a sentence is queued and then, as the last operand says,
    /// left there, carried out at once (the object's verb script runs), or shown.
    /// </summary>
    private void DoSentence(IReadOnlyList<int> arguments)
    {
        int verb = arguments[0];
        if (verb == ClearSentences)
        {
            _sentences.Clear();
            Engine.StopScript(ScriptEngine.SentenceScript);
            return;
        }

        if (verb == ResetSentenceLine)
        {
            ResetSentence();
            return;
        }

        if (_sentences.Count >= SentenceCount)
        {
            throw new InvalidOperationException($"More than {SentenceCount} sentences are waiting.");
        }

        var sentence = new Sentence(verb, arguments[1], arguments[2]);
        switch (arguments[3])
        {
            case 0:
                _sentences.Add(sentence);
                break;

            case 1 when verb == VerbStopObject:
                Engine.StopObjectScript(sentence.ObjectA);
                break;

            case 1:
                {
                    bool special = verb is VerbSpecial or VerbBackground;
                    bool background = verb == VerbBackground;
                    if (!special)
                    {
                        Engine.Variables[VarActiveVerb] = verb;
                        Engine.Variables[VarActiveObject1] = sentence.ObjectA;
                        Engine.Variables[VarActiveObject2] = sentence.ObjectB;
                    }
                    else
                    {
                        verb = VerbSpecial;
                    }

                    // a script of the object already running with the same flags gives up its slot
                    int slot = Engine.FindObjectSlot(sentence.ObjectA, background, special);
                    RunObjectScript(sentence.ObjectA, verb, background, special, slot);
                    break;
                }

            case 2:
                Engine.Variables[VarSentenceVerb] = verb;
                Engine.Variables[VarSentenceObject1] = sentence.ObjectA;
                Engine.Variables[VarSentenceObject2] = sentence.ObjectB;
                DrawSentence();
                break;

            default:
                throw new InvalidOperationException($"doSentence has no way {arguments[3]}.");
        }
    }

    /// <summary>
    /// Runs an object's script for a verb, from its room or the inventory; nothing happens when the
    /// object is not there or has no script for the verb. Mirrors <c>runObjectScript</c>.
    /// </summary>
    private void RunObjectScript(int obj, int verb, bool freezeResistant, bool recursive, int slot = -1)
    {
        int where = WhereIs(obj);
        if (where == 0 || ObjectCode(obj) is not { } code || code.FindVerb((byte)verb) is not { } offset || offset == 0)
        {
            return;
        }

        Engine.RunObjectScript(obj, code.Code, offset, where == 1, freezeResistant, recursive, slot);
    }

    /// <summary>Whether an object has a script for a verb. Mirrors <c>getVerbEntrypoint(obj, verb) != 0</c>.</summary>
    private bool HasVerb(int obj, int verb) => ObjectCode(obj)?.FindVerb((byte)verb) is { } offset && offset != 0;

    /// <summary>Mirrors <c>ScummEngine_v2::resetSentence</c>: the sentence goes back to the verb kept in <c>VAR_BACKUP_VERB</c>.</summary>
    private void ResetSentence()
    {
        Engine.Variables[VarSentenceVerb] = Engine.Variables[VarBackupVerb];
        Engine.Variables[VarSentenceObject1] = 0;
        Engine.Variables[VarSentenceObject2] = 0;
        Engine.Variables[VarSentencePreposition] = 0;
    }

    /// <summary>
    /// Shows the sentence the variables hold: the verb's name, the first object's, the preposition,
    /// the second object's. Version 1 works the preposition out itself, from the verb or the object.
    /// Mirrors <c>ScummEngine_v2::drawSentence</c> and <c>drawPreposition</c> for English.
    /// </summary>
    private void DrawSentence()
    {
        if ((Engine.Interface & VerbArea.ShowSentence) == 0)
        {
            return;
        }

        int slot = Verbs.SlotOf(Engine.Variables[VarSentenceVerb]);
        if (slot == 0 || Verbs.Slots[slot].Id == 0)
        {
            return;
        }

        var sentence = new List<byte>(Verbs.Slots[slot].Name);
        int first = Engine.Variables[VarSentenceObject1];
        if (first > 0)
        {
            sentence.Add((byte)' ');
            sentence.AddRange(Encoding.Latin1.GetBytes(NameOf(first)));

            if (Version == 1 && !IsNes && Engine.Variables[VarSentencePreposition] == 0)
            {
                int preposition = Verbs.Slots[slot].Preposition;
                Engine.Variables[VarSentencePreposition] = preposition == 0xFF ? ObjectCode(first)?.Preposition ?? 0 : preposition;
            }
        }

        int prepositionIndex = Engine.Variables[VarSentencePreposition];
        if (prepositionIndex is > 0 and <= 4 && IsNes)
        {
            sentence.AddRange(NesPreposition(prepositionIndex));
        }
        else if (prepositionIndex is > 0 and <= 4)
        {
            string[] prepositions = [" ", " in", " with", " on", " to"];
            sentence.AddRange(Encoding.Latin1.GetBytes(prepositions[prepositionIndex]));
        }

        int second = Engine.Variables[VarSentenceObject2];
        if (second > 0)
        {
            sentence.Add((byte)' ');
            sentence.AddRange(Encoding.Latin1.GetBytes(NameOf(second)));
        }

        Verbs.DrawSentence([.. sentence]);
    }

    // the NES's sound for an object picked up (o2_pickupObject)
    private const int NesPickupSound = 51;

    /// <summary>The costume set the NES's scripts last chose: 0 or 1.</summary>
    internal int NesCostumeSetNumber { get; private set; }

    // the NES's preposition list, from the cartridge (costume 78): five words of 8 bytes
    private const int NesPrepositions = 78, NesPrepositionLength = 8;

    /// <summary>
    /// One of the NES's prepositions, a space and the word, as its cartridge has them. Mirrors the
    /// NES case of <c>drawSentence</c>, which reads them from costume 78.
    /// </summary>
    private byte[] NesPreposition(int index)
    {
        if (_loader.Load(ResourceType.Costume, NesPrepositions) is not { } list)
        {
            return [];
        }

        var word = list.Span.Slice(2 + index * NesPrepositionLength, NesPrepositionLength);
        int end = word.IndexOf((byte)0);
        return word[..(end < 0 ? word.Length : end)].ToArray();
    }

    /// <summary>
    /// The kid takes an object from the room: a copy of its code goes into the inventory, the kid
    /// owns it, and its state turns its image on and makes it untouchable. Mirrors <c>o2_pickupObject</c>.
    /// </summary>
    internal void PickUp(int obj)
    {
        if (CurrentRoom?.FindObject(obj) is not { } code || WhereIs(obj) == 1)
        {
            return;
        }

        int free = _inventory.IndexOf(0);
        if (free < 0)
        {
            _inventory.Add(obj);
        }
        else
        {
            _inventory[free] = obj;
        }

        _carried[obj] = code;
        Engine.ObjectOwners[obj] = (byte)Engine.Variables[ScriptEngine.VarEgo];
        Engine.PutState(obj, Engine.GetState(obj) | ScriptEngine.StateIntrinsic | ScriptEngine.StateUntouchable);
        RedrawInventory();

        // the NES's pickup sound, at once, not through the queue (Trac #2536)
        if (IsNes)
        {
            Sound.Trigger(NesPickupSound);
        }
    }

    /// <summary>
    /// An object given to nobody: its scripts stop and, when carried, it leaves the inventory,
    /// leaving a gap the next object picked up fills. Mirrors <c>clearOwnerOf</c> before version 4.
    /// </summary>
    private void ClearOwnerOf(int obj)
    {
        Engine.StopObjectScript(obj);
        int index = _inventory.IndexOf(obj);
        if (index >= 0)
        {
            _inventory[index] = 0;
            _carried.Remove(obj);
        }
    }

    /// <summary>The world side of <c>o2_cutscene</c>: the room and camera mode are kept, and the sentences go.</summary>
    private void CutsceneBegun()
    {
        _beforeCutscene = (CurrentRoom?.Number ?? 0, Camera.Mode);
        RedrawInterface();
        _sentences.Clear();
        ResetSentence();
    }

    /// <summary>Mirrors <c>o2_endCutscene</c>'s camera: version 2 and the NES follow the kid, version 1 puts back the camera mode or room the cutscene began with.</summary>
    private void EndCutscene()
    {
        int ego = Engine.Variables[ScriptEngine.VarEgo];
        if (Version >= 2 || IsNes)
        {
            if (ego is > 0 and < ActorCount)
            {
                FollowActor(Actors[ego]);
            }

            return;
        }

        if (_beforeCutscene.Mode == CameraMode.FollowActor)
        {
            if (ego is > 0 and < ActorCount)
            {
                FollowActor(Actors[ego]);
            }
        }
        else if (_beforeCutscene.Room != (CurrentRoom?.Number ?? 0))
        {
            StartScene(_beforeCutscene.Room);
        }
    }

    /// <summary>
    /// The interface after a user state change: cleared, then the verbs, the inventory and the
    /// sentence drawn again as far as they are on. Mirrors the end of <c>ScummEngine_v2::setUserState</c>.
    /// </summary>
    private void RedrawInterface()
    {
        Verbs.Clear();
        Verbs.Redraw(CursorShown ? Verbs.FindAt(Mouse.X, Mouse.Y) : 0);
        RedrawInventory();
        DrawSentence();
    }

    /// <summary>Mirrors <c>redrawV2Inventory</c>: the names of what the kid carries.</summary>
    private void RedrawInventory()
    {
        int ego = Engine.Variables[ScriptEngine.VarEgo];
        var names = new List<string>();
        for (int i = 1; FindInventory(ego, i) is var obj and not 0; i++)
        {
            names.Add(NameOf(obj));
        }

        Verbs.DrawInventory(names);
    }

    /// <summary>The name of a verb in a slot, for messages. Mirrors <c>convertVerbMessage</c>.</summary>
    private string VerbName(int id)
    {
        int slot = Verbs.SlotOf(id);
        return slot == 0 ? string.Empty : Encoding.Latin1.GetString(Verbs.Slots[slot].Name);
    }

    /// <summary>
    /// The step's input: a right click is Escape, Escape and F4 skip the cutscene and become the
    /// cutscene key, function keys count from 315, and a key sets <c>VAR_KEYPRESS</c> (function
    /// keys from 1). Mirrors <c>processInput</c> and <c>processKeyboard</c> for these versions.
    /// </summary>
    private void ProcessInput()
    {
        _input = 0;
        int key = _key;
        bool hovered = _clickAfterHover;
        _clickAfterHover = false;
        if (_rightClicked)
        {
            key = KeyEscape;
        }
        else if (_clicked)
        {
            _input = LeftClick;
        }

        _clicked = hovered;
        _rightClicked = false;
        _key = 0;
        if (key == 0)
        {
            return;
        }

        // F4 skips a cutscene, as Escape does; on the NES, Shift-B (processKeyboard)
        if (key == (IsNes ? 'B' : KeyF4))
        {
            key = KeyEscape;
        }

        // the NES's own save screen comes up on Space, as ScummVM's original-menu option has it,
        // and on F5, for ScummVM's Alt-F5; the key goes no further (processKeyboard)
        if (IsNes && key is ' ' or KeyF5)
        {
            Engine.RunScript(NesSaveMenuScript);
            return;
        }

        // Space, F8 and F6 are the interpreter's, and go no further (ScummEngine::processKeyboard)
        if (InterpreterKey(key))
        {
            _input = 0;
            return;
        }

        if (key == '.')
        {
            Messages.SkipDelay();
        }
        else if (key == KeyEscape && Engine.Variables[ScriptEngine.VarCutsceneExitKey] != 0)
        {
            Engine.AbortCutscene();
            _input = Engine.Variables[ScriptEngine.VarCutsceneExitKey];
        }
        else
        {
            _input = key;
        }

        if (_input != 0)
        {
            Engine.Variables[VarKeypress] = _input is >= KeyF1 and <= KeyF9 ? _input - (KeyF1 - 1) : _input;
        }
    }

    /// <summary>
    /// The mouse in the room, in actor units, or -1 for a line outside it. Mirrors the
    /// <c>_virtualMouse</c> lines of <c>processInput</c> and <c>scummLoop_updateScummVars</c>.
    /// </summary>
    private void UpdateVirtualMouse()
    {
        var layout = Layout;
        int x = Mouse.X + Camera.Left;
        int y = Mouse.Y - layout.RoomTop;
        if (y < 0 || y >= layout.RoomHeight)
        {
            y = -1;
        }

        Engine.Variables[VarVirtMouseX] = x >> 3;
        Engine.Variables[VarVirtMouseY] = y >> 1;

        // the NES's narrow rooms show two strips further right (scummLoop_updateScummVars)
        if (CurrentRoom is { NesStartStrip: > 0 })
        {
            Engine.Variables[VarVirtMouseX] = Math.Max(0, Engine.Variables[VarVirtMouseX] - 2);
        }
    }

    /// <summary>
    /// Hands the step's click or key to the input script, saying where it was: a verb's key, the
    /// inventory keys, the sentence line, an inventory place, a verb, the room or elsewhere.
    /// Nothing happens while the cursor is off. Mirrors <c>ScummEngine_v2::checkExecVerbs</c>.
    /// </summary>
    private void CheckExecVerbs()
    {
        if (!Engine.CursorOn || _input == 0)
        {
            return;
        }

        if (_input < MaxKey)
        {
            for (int i = 1; i < VerbArea.SlotCount; i++)
            {
                var verb = Verbs.Slots[i];
                if (verb.Id != 0 && verb.Mode == 1 && verb.Key == _input)
                {
                    RunInputScript(VerbClickArea, verb.Id);
                    return;
                }
            }

            int ego = Engine.Variables[ScriptEngine.VarEgo];
            int place = -1;
            switch (_input)
            {
                case 'u':
                    if (Verbs.InventoryOffset >= 2)
                    {
                        Verbs.InventoryOffset -= 2;
                        RedrawInventory();
                    }

                    return;
                case 'j':
                    if (Verbs.InventoryOffset + 4 < InventoryCount(ego))
                    {
                        Verbs.InventoryOffset += 2;
                        RedrawInventory();
                    }

                    return;
                case 'i':
                    place = 0;
                    break;
                case 'o':
                    place = 1;
                    break;
                case 'k':
                    place = 2;
                    break;
                case 'l':
                    place = 3;
                    break;
            }

            if (place != -1)
            {
                int obj = FindInventory(ego, place + 1 + Verbs.InventoryOffset);
                if (obj > 0)
                {
                    RunInputScript(InventoryClickArea, obj);
                }

                return;
            }

            RunInputScript(KeyClickArea, _input);
            return;
        }

        var layout = Layout;
        var (x, y) = Mouse;
        bool inVerbs = y >= layout.RoomBottom;
        if (inVerbs && y <= Verbs.Top + 8)
        {
            RunInputScript(SentenceClickArea, 0);
        }
        else if (inVerbs && y >= Verbs.Top + Verbs.InventoryLine)
        {
            int ego = Engine.Variables[ScriptEngine.VarEgo];
            int place = Verbs.ClickInventory(x, y, InventoryCount(ego));
            if (place == -2)
            {
                RedrawInventory();
            }
            else if (place >= 0 && FindInventory(ego, place + 1 + Verbs.InventoryOffset) is var obj and > 0)
            {
                RunInputScript(InventoryClickArea, obj);
            }
        }
        else if (Verbs.FindAt(x, y) is var over and not 0)
        {
            RunInputScript(VerbClickArea, Verbs.Slots[over].Id);
        }
        else
        {
            bool inRoom = y >= layout.RoomTop && y < layout.RoomBottom;
            RunInputScript(inRoom ? SceneClickArea : VerbClickArea, 0);
        }
    }

    /// <summary>
    /// Mirrors <c>ScummEngine_v2::runInputScript</c>: the click goes into the variables, then script
    /// 4 runs. ScummVM also passes the click as script locals, which these versions' scripts never read.
    /// </summary>
    private void RunInputScript(int area, int value)
    {
        Engine.Variables[VarClickArea] = area;
        if (area == VerbClickArea)
        {
            Engine.Variables[VarClickVerb] = value;
        }
        else if (area == InventoryClickArea)
        {
            Engine.Variables[VarClickObject] = value;
        }

        Engine.RunScript(InputScript);
    }

    /// <summary>
    /// Runs the next waiting sentence once the sentence script is free: the sentence goes into the
    /// variables, with whether its object has a script for its verb, and script 2 runs. A sentence
    /// whose two objects are the same is dropped. Mirrors <c>checkAndRunSentenceScript</c>.
    /// </summary>
    private void CheckAndRunSentenceScript()
    {
        if (Engine.IsScriptRunning(ScriptEngine.SentenceScript) || _sentences.Count == 0)
        {
            return;
        }

        var sentence = _sentences[^1];
        _sentences.RemoveAt(_sentences.Count - 1);
        if (sentence.HasPreposition && sentence.ObjectB == sentence.ObjectA)
        {
            return;
        }

        Engine.Variables[VarActiveVerb] = sentence.Verb;
        Engine.Variables[VarActiveObject1] = sentence.ObjectA;
        Engine.Variables[VarActiveObject2] = sentence.ObjectB;
        Engine.Variables[VarVerbAllowed] = HasVerb(sentence.ObjectA, sentence.Verb) ? 1 : 0;
        Engine.RunScript(ScriptEngine.SentenceScript);
    }

    /// <summary>
    /// The highlights follow the mouse: the verb under it, and the inventory box. When the kid
    /// changes, the inventory goes back to its start. Mirrors <c>ScummEngine_v2::handleMouseOver</c>.
    /// </summary>
    private void HandleMouseOver(bool egoChanged)
    {
        if (CursorShown)
        {
            Verbs.MouseOver(Verbs.FindAt(Mouse.X, Mouse.Y));
        }

        if (egoChanged)
        {
            Verbs.InventoryOffset = 0;
            RedrawInventory();
        }

        Verbs.CheckMouseOver(Mouse.X, Mouse.Y, CursorShown);
    }
}
