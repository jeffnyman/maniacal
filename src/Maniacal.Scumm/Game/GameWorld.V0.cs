using System.Text;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Game;

/// <summary>
/// What version 0, the C64, does differently: its own instructions (<c>ScummEngine_v0</c>'s
/// <c>o_</c> handlers), its modes in place of the user state of versions 1 and 2, and its verbs
/// and sentences, which its engine handles itself where versions 1 and 2 leave them to scripts.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>Version 0's modes (<c>CurrentMode</c>): what the interface shows and whether the scripts freeze.</summary>
    public const int ModeCutscene = 0, ModeKeypad = 1, ModeNoNewKid = 2, ModeNormal = 3;

    /// <summary>Version 0's object types, in the high byte of an object's number (<c>ObjectV0Type</c>).</summary>
    public const int TypeForeground = 0, TypeBackground = 1, TypeActor = 2;

    // version 0's verbs (VerbsV0) and the prepositions they take (VerbPrepsV0)
    private const int V0VerbNone = 0, V0VerbGive = 3, V0VerbFix = 6, V0VerbNewKid = 7, V0VerbUnlock = 8, V0VerbPush = 9,
        V0VerbUse = 11, V0VerbRead = 12, V0VerbWalkTo = 13, V0VerbPickUp = 14, V0VerbWhatIs = 15;
    private const int PrepNone = 0, PrepWith = 2, PrepTo = 4, PrepFromObject = 0xFF;

    // version 0 prints nobody's text in this colour (decodeParseString)
    private const int V0NobodyColor = 14;

    // the variables the kids are in (VAR(97 + slot)), and the script for verbs no object has a script for
    private const int VarKids = 97, V0FallbackScript = 3;

    // the kid version 0 starts with, Dave (ScummEngine_v0::resetScummVars)
    private const int V0FirstKid = 3;

    // the plant, which version 0 draws behind all and never turns to a kid (processActors, checkPendingWalkAction)
    private const int V0Plant = 19;

    // where the ego stands, walking to an object for a sentence (_walkToObjectState)
    private const int WalkDone = 0, WalkWalking = 1, WalkTurning = 2;

    /// <summary>Version 0's mode (<c>_currentMode</c>).</summary>
    public int CurrentMode { get; private set; }

    /// <summary>Version 0: the verb chosen (<c>_activeVerb</c>).</summary>
    public int ActiveVerb { get; private set; } = V0VerbWalkTo;

    /// <summary>Version 0: the objects chosen for it, with their types (<c>_activeObject</c>, <c>_activeObject2</c>).</summary>
    public int ActiveObject { get; private set; }

    /// <inheritdoc cref="ActiveObject"/>
    public int ActiveObject2 { get; private set; }

    // the cutscene's mode and room, for its end (vm.cutSceneData[0] and [2])
    private (int Mode, int Room) _beforeV0Cutscene;

    private int _walkToObject, _walkToObjectState, _sentenceNestedCount;
    private bool _redrawSentenceLine;

    /// <summary>An object number with its type, as version 0 keeps them (<c>OBJECT_V0</c>).</summary>
    public static int V0Object(int id, int type) => type << 8 | id;

    /// <summary>Version 0's instructions that touch the world; false for those it does not have.</summary>
    private bool ExecuteV0(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments, out int result)
    {
        result = 0;
        switch (instruction.Handler)
        {
            // o_cutscene: the mode and room are kept, the other scripts freeze, the sentence goes
            case "o_cutscene":
                _beforeV0Cutscene = (CurrentMode, CurrentRoom?.Number ?? 0);
                engine.FreezeScripts();
                SetMode(ModeCutscene);
                ResetSentenceV0();
                engine.Override = null;
                return true;

            // o_endCutscene: the mode comes back; after the keypad the room does too, else the camera follows the kid
            case "o_endCutscene":
                engine.Variables[engine.Vars.Override] = 0;
                engine.Override = null;
                SetMode(_beforeV0Cutscene.Mode);
                if (CurrentMode == ModeKeypad)
                {
                    StartScene(_beforeV0Cutscene.Room);
                    engine.UnfreezeScripts();
                }
                else
                {
                    engine.UnfreezeScripts();
                    int ego = engine.Variables[ScriptEngine.VarEgo];
                    if (ego is > 0 and < ActorCount)
                    {
                        FollowActor(Actors[ego]);
                    }

                    SetMode(_beforeV0Cutscene.Mode);
                    _redrawSentenceLine = true;
                }

                return true;

            case "o_setMode":
                SetMode(arguments[0]);
                return true;

            // o_lights: 2 lights the room, 1 leaves only the flashlight, else all is dark
            case "o_lights":
                CurrentLights = arguments[0] switch
                {
                    2 => LightActorBasePalette | Flashlight.RoomLightsOn | LightActorColors,
                    1 => Flashlight.FlashlightOn,
                    _ => 0,
                };
                return true;

            case "o_print":
                PrintV0(arguments[0], instruction.Operands[1].Text!);
                return true;
            case "o5_print":
                PrintV0(arguments[0], instruction.Operands[1].Text!);
                return true;
            case "o_printEgo":
                PrintV0(engine.Variables[ScriptEngine.VarEgo], instruction.Operands[0].Text!);
                return true;

            // o_animateActor: a clip, with its repeats; 0xFE holds the mouth open, 0xFD stops it, 0xFF stops the actor
            case "o_animateActor":
                {
                    var actor = ActorV0Of(arguments[0]);
                    actor.Limbs.Repeat = (sbyte)arguments[2];
                    switch (arguments[1])
                    {
                        case 0xFE:
                            actor.Speaking = 0x80;
                            break;
                        case 0xFD:
                            actor.Speaking = 0;
                            break;
                        case 0xFF:
                            actor.StopMoving();
                            break;
                        default:
                            actor.Animate(arguments[1]);
                            break;
                    }

                    return true;
                }

            // o_getActorMoving: 2 for an actor standing still
            case "o_getActorMoving":
                result = ActorOf(arguments[0]).Moving;
                return true;

            // o_setActorBitVar: an actor's flags; one that freezes it stops it (script 158, the tentacle's "Oomph!")
            case "o_setActorBitVar":
                if ((byte)arguments[0] < ActorCount)
                {
                    var actor = ActorV0Of(arguments[0]);
                    int mask = (byte)arguments[1];
                    actor.MiscFlags = arguments[2] != 0 ? actor.MiscFlags | mask : actor.MiscFlags & ~mask;
                    if ((actor.MiscFlags & ActorV0.MiscFlagFreeze) != 0)
                    {
                        actor.StopMoving();
                    }
                }

                return true;
            case "o_getActorBitVar":
                result = (ActorV0Of(arguments[0]).MiscFlags & (byte)arguments[1]) != 0 ? 1 : 0;
                return true;

            // o_putActorAtObject, o_walkActorToObject: the opcode's bit 0x40 makes the object part of the room
            case "o_putActorAtObject":
                {
                    var actor = ActorOf(arguments[0]);
                    int obj = V0Object(arguments[1], (instruction.Opcode & 0x40) != 0 ? TypeBackground : TypeForeground);
                    var (x, y) = (30, 60);
                    if (WhereIs(obj) != 0 && ObjectPosition(obj) is { } position)
                    {
                        var (point, _) = actor.AdjustToBox(position.X, position.Y);
                        (x, y) = (point.X, point.Y);
                    }

                    PutActor(actor, x, y);
                    return true;
                }

            case "o_walkActorToObject":
                {
                    int obj = V0Object(arguments[1], (instruction.Opcode & 0x40) != 0 ? TypeBackground : TypeForeground);
                    if (WhereIs(obj) != 0)
                    {
                        WalkActorToObject(ActorOf(arguments[0]), obj);
                    }

                    return true;
                }

            case "o_loadRoomWithEgo":
                LoadRoomWithEgoV0(engine, V0Object(arguments[0], TypeForeground), arguments[1]);
                return true;

            // o_getClosestActor: of actors 7 down to 1 (or with bit 0x40, 25 down: there is no 25), the nearest, or 0xFF
            case "o_getClosestActor":
                {
                    int closest = 0xFF, closestDistance = 0xFF;
                    for (int other = (instruction.Opcode & 0x40) != 0 ? 25 : 7; other > 0; other--)
                    {
                        int distance = other < ActorCount ? Distance(arguments[0], other) : 0xFF;
                        if (distance < closestDistance)
                        {
                            (closest, closestDistance) = (other, distance);
                        }
                    }

                    result = closest;
                    return true;
                }

            // o_pickupObject: 0 is the sentence's object (ScummEngine_v0::o_pickupObject)
            case "o_pickupObject":
                PickUp(arguments[0] != 0 ? arguments[0] : engine.CommandObject);
                return true;

            // o_setOwnerOf: 0 is the sentence's object; given to nobody, it leaves the inventory (setOwnerOf)
            case "o_setOwnerOf":
                {
                    int obj = arguments[0] != 0 ? arguments[0] : engine.CommandObject;
                    if (obj <= 0 || obj >= engine.ObjectOwners.Length)
                    {
                        throw new InvalidOperationException($"Object {obj} has no owner to set.");
                    }

                    if (arguments[1] == 0)
                    {
                        ClearOwnerOf(obj);
                    }

                    engine.ObjectOwners[obj] = (byte)arguments[1];
                    RedrawInventory();
                    return true;
                }

            // o_setObjectName: 0 is the sentence's object, else the opcode's bit 0x80 makes it part of the room
            case "o_setObjectName":
                {
                    int obj = arguments[0] == 0
                        ? engine.CommandObject
                        : V0Object(arguments[0], (instruction.Opcode & 0x80) != 0 ? TypeBackground : TypeForeground);
                    if (obj >> 8 == TypeActor)
                    {
                        throw new InvalidOperationException($"Actor {obj & 0xFF} cannot be renamed with setObjectName.");
                    }

                    _newNames[obj] = Encoding.Latin1.GetString(instruction.Operands[1].Text!);
                    RedrawInventory();
                    return true;
                }

            // o_doSentence: 0xFF and 0xFE are the running sentence's objects, else the opcode's bits give the types
            case "o_doSentence":
                {
                    int Object(int b, int typeBit) => b switch
                    {
                        0xFF => engine.CommandObject2,
                        0xFE => engine.CommandObject,
                        _ => V0Object(b, (instruction.Opcode & typeBit) != 0 ? TypeBackground : TypeForeground),
                    };

                    _sentences.Add(new Sentence(arguments[0], Object(arguments[1], 0x80), Object(arguments[2], 0x40)));
                    return true;
                }

            default:
                return false;
        }
    }

    private ActorV0 ActorV0Of(int number) => (ActorV0)ActorOf(number);

    /// <summary>
    /// Each frame, before the actors are drawn: a talking actor's mouth opens or shuts, and every
    /// actor in the room but a hidden kid takes an animation step. Mirrors <c>processActors</c> for version 0.
    /// </summary>
    private void AnimateActorsV0()
    {
        foreach (var actor in ActorsInRoom().Cast<ActorV0>())
        {
            actor.FlipMouth();
            if ((actor.MiscFlags & ActorV0.MiscFlagHide) == 0 && actor.CostumeNumber != 0)
            {
                actor.AnimateCostume();
            }
        }
    }

    /// <summary>
    /// The kid goes into a room at an object: put there first at where they stood, then at the
    /// object's walk-to point in a walkbox, the camera following. A frozen kid goes nowhere, and the
    /// script stops. Mirrors <c>ScummEngine_v0::o_loadRoomWithEgo</c>.
    /// </summary>
    private void LoadRoomWithEgoV0(ScriptEngine engine, int obj, int room)
    {
        var ego = ActorV0Of(engine.Variables[ScriptEngine.VarEgo]);
        if ((ego.MiscFlags & ActorV0.MiscFlagFreeze) != 0)
        {
            engine.StopObjectCode(engine.CurrentSlot);
            return;
        }

        // the original puts the kid in the new room where they were in the old (for script 158 in room 12)
        PutActor(ego, ego.X, ego.Y, room);
        StartScene(ego.Room);
        if (ObjectPosition(obj) is not { } target)
        {
            return;
        }

        var (point, _) = ego.AdjustToBox(target.X, target.Y);
        PutActor(ego, point.X, point.Y, CurrentRoom!.Number);
        Camera.SetAtEx(ego.X * 8);
        Camera.Follow(ego.Number, ego.X * 8, setCamera: true);
        ResetSentenceV0();
        if (point.X >= 0 && point.Y >= 0)
        {
            ego.StartWalk(point.X, point.Y, -1);
        }
    }

    /// <summary>A version 0 message, nobody's in its own colour. Mirrors <c>ScummEngine_v0::decodeParseString</c> and <c>actorTalk</c>.</summary>
    private void PrintV0(int actor, byte[] text)
    {
        if (actor == MessageLine.NoActor)
        {
            Messages.StringColor = V0NobodyColor;
        }

        Messages.Talk(actor, MessageLine.DecodeV0(text));
    }

    /// <summary>
    /// A mode: the interface, the cursor and the freezing of scripts each mode has. Mirrors
    /// <c>ScummEngine_v0::setMode</c> for the full game.
    /// </summary>
    private void SetMode(int mode)
    {
        CurrentMode = mode;
        int state = mode switch
        {
            // the cutscene leaves the freezing as it is
            ModeCutscene => ScriptEngine.UserSetInterface | ScriptEngine.UserSetCursor,
            ModeKeypad => ScriptEngine.UserSetInterface | ScriptEngine.UserSetCursor | ScriptEngine.UserCursorOn
                | ScriptEngine.UserSetFreeze | ScriptEngine.UserFreezeOn,
            ModeNormal or ModeNoNewKid => ScriptEngine.UserSetInterface | ScriptEngine.UserInterfaceMask
                | ScriptEngine.UserSetCursor | ScriptEngine.UserCursorOn | ScriptEngine.UserSetFreeze,
            _ => throw new InvalidOperationException($"Mode {mode} is not one of version 0's."),
        };

        Engine.SetUserState(state);
        RedrawInterface();
    }

    /// <summary>
    /// The C64's verbs, which its engine makes itself: the 15 of its table, each with the
    /// preposition it takes. Mirrors <c>ScummEngine_v0::resetVerbs</c> and <c>verbPrepIdType</c>.
    /// </summary>
    private void ResetVerbsV0()
    {
        for (int slot = 1; slot < 16; slot++)
        {
            Verbs.Kill(slot);
        }

        for (int slot = 1; slot < 16; slot++)
        {
            var (id, column, row, name) = V0Verbs.English[slot - 1];
            int preposition = id switch
            {
                V0VerbUse => PrepFromObject,
                V0VerbGive => PrepTo,
                V0VerbUnlock or V0VerbFix => PrepWith,
                _ => PrepNone,
            };
            Verbs.SetVerbV0(slot, id, column, row, preposition, name);
        }
    }

    /// <summary>
    /// The sentence goes back to Walk to with nothing chosen, and the sentences waiting go. Mirrors
    /// <c>ScummEngine_v0::resetSentence</c>.
    /// </summary>
    private void ResetSentenceV0()
    {
        ActiveVerb = V0VerbWalkTo;
        ActiveObject = 0;
        ActiveObject2 = 0;
        _walkToObjectState = WalkDone;
        _redrawSentenceLine = true;
        _sentences.Clear();
        _sentenceNestedCount = 0;
    }

    /// <summary>
    /// The step's input: F1 to F3 switch to a kid, the right button or Escape skips a cutscene, the
    /// period skips a line of speech, and a left click is noted for <see cref="CheckExecVerbsV0"/>.
    /// Mirrors <c>ScummEngine_v0::processInput</c> and what it shares with <c>processKeyboard</c>.
    /// </summary>
    private void ProcessInputV0()
    {
        _input = _clicked ? LeftClick : 0;
        int key = _rightClicked ? KeyEscape : _key;
        _clicked = _rightClicked = false;
        _key = 0;

        if (key is >= KeyF1 and < KeyF1 + 3)
        {
            SwitchKid(key - KeyF1);
        }
        else if (key == '.')
        {
            Messages.SkipDelay();
        }
        else if (key == KeyEscape)
        {
            // version 0 has no cutscene exit key variable: Escape always skips
            Engine.AbortCutscene();
        }
        else if (key != 0)
        {
            _input = key;
        }
    }

    /// <summary>Plays another of the three kids, in the normal mode only. Mirrors <c>switchActor</c>.</summary>
    private void SwitchKid(int slot)
    {
        ResetSentenceV0();
        if (CurrentMode != ModeNormal)
        {
            return;
        }

        int kid = Engine.Variables[VarKids + slot];
        Engine.Variables[ScriptEngine.VarEgo] = kid;
        if (kid is > 0 and < ActorCount)
        {
            FollowActor(Actors[kid]);
        }
    }

    /// <summary>
    /// The click makes the sentence: a verb chooses the verb, or carries out the sentence when it is
    /// the verb already chosen and the sentence is whole; the sentence line carries it out, or picks a
    /// kid after New kid; an object in the room or the inventory becomes the first object, or the
    /// second after a preposition, and clicking it again carries the sentence out. In the keypad mode
    /// the verb is Push and a click carries it out at once; Walk to walks wherever the room is
    /// clicked. Mirrors <c>ScummEngine_v0::checkExecVerbs</c>.
    /// </summary>
    private void CheckExecVerbsV0()
    {
        int egoNumber = Engine.Variables[ScriptEngine.VarEgo];
        var layout = ScreenLayout.ForVersion(Version);
        var (x, y) = Mouse;
        bool inVerbs = y >= layout.RoomBottom, inRoom = y >= layout.RoomTop && y < layout.RoomBottom;
        bool clicked = _input == LeftClick;
        bool execute = false;

        if (clicked && Verbs.FindAt(x, y) is var over and not 0)
        {
            int verb = Verbs.Slots[over].Id;
            if (ActiveVerb != verb)
            {
                // a new verb keeps the first object unless a preposition was in use
                if (ActiveVerbPreposition() != PrepNone)
                {
                    ActiveObject = 0;
                }

                ActiveObject2 = 0;
                ActiveVerb = verb;
                _redrawSentenceLine = true;
            }
            else if (SentenceComplete())
            {
                execute = true;
            }
        }

        if (CurrentMode != ModeCutscene)
        {
            if (CurrentMode == ModeKeypad)
            {
                ActiveVerb = V0VerbPush;
            }

            if (_input is > 0 and < MaxKey)
            {
                // keys were handled with the input
            }
            else if (clicked || ActiveVerb == V0VerbWhatIs)
            {
                if (inVerbs && y <= Verbs.Top + 8)
                {
                    if (ActiveVerb == V0VerbNewKid)
                    {
                        if (CurrentMode == ModeNormal)
                        {
                            int column = x >> 3;
                            int kid = column < 11 ? 0 : column < 25 ? 1 : 2;
                            ActiveVerb = V0VerbWalkTo;
                            DrawSentenceLineV0();
                            SwitchKid(kid);
                        }

                        ActiveVerb = V0VerbWalkTo;
                        _redrawSentenceLine = true;
                        return;
                    }

                    if (SentenceComplete())
                    {
                        execute = true;
                    }
                }
                else if ((inVerbs && y > Verbs.Top + VerbArea.InventoryTop) || inRoom)
                {
                    int obj = 0;
                    if (inVerbs)
                    {
                        int offset = Verbs.InventoryOffset;
                        int place = Verbs.ClickInventory(x, y, InventoryCount(egoNumber));
                        if (offset != Verbs.InventoryOffset)
                        {
                            RedrawInventory();
                            return;
                        }

                        obj = place >= 0 ? FindInventory(egoNumber, place + 1 + Verbs.InventoryOffset) : 0;

                        // the second object of Give has to be an actor
                        if (ActiveVerb == V0VerbGive && ActiveObject != 0)
                        {
                            obj = 0;
                        }
                    }
                    else
                    {
                        int roomX = x + Camera.Left, roomY = y - layout.RoomTop;
                        if (ActiveVerb == V0VerbGive && ActiveObject != 0)
                        {
                            int actor = ActorFromPos(roomX, roomY);
                            obj = actor != 0 ? V0Object(actor, TypeActor) : 0;
                        }
                        else
                        {
                            obj = FindObject(roomX, roomY);
                        }
                    }

                    if (obj == 0)
                    {
                        if (ActiveVerb == V0VerbWalkTo)
                        {
                            ActiveObject = 0;
                            ActiveObject2 = 0;
                        }
                    }
                    else if (ActiveVerbPreposition() == PrepNone)
                    {
                        if (obj == ActiveObject)
                        {
                            execute = true;
                        }
                        else
                        {
                            ActiveObject = obj;
                        }

                        // the keypad carries a click out at once
                        if (CurrentMode == ModeKeypad)
                        {
                            execute = true;
                        }
                    }
                    else
                    {
                        if (obj == ActiveObject2)
                        {
                            execute = true;
                        }

                        if (obj != ActiveObject)
                        {
                            ActiveObject2 = obj;
                            if (CurrentMode == ModeKeypad)
                            {
                                execute = true;
                            }
                        }
                    }

                    _redrawSentenceLine = true;
                    if (ActiveVerb == V0VerbWalkTo && inRoom)
                    {
                        _walkToObjectState = WalkDone;
                        execute = true;
                    }
                }
            }
        }

        if (_redrawSentenceLine)
        {
            DrawSentenceLineV0();
        }

        if (!execute || ActiveVerb == V0VerbNone)
        {
            return;
        }

        if (ActiveVerb == V0VerbWalkTo)
        {
            VerbExec();
        }
        else if (ActiveObject != 0 && (ActiveVerbPreposition() == PrepNone || ActiveObject2 != 0))
        {
            VerbExec();
        }
    }

    /// <summary>
    /// The chosen sentence is carried out: it waits for its turn, and the verb goes back to Walk to;
    /// Walk to on no object walks the kid to the mouse; What is does nothing. Mirrors <c>verbExec</c>.
    /// </summary>
    private void VerbExec()
    {
        _sentences.Clear();
        _sentenceNestedCount = 0;
        if (ActiveVerb == V0VerbWhatIs)
        {
            return;
        }

        if (!(ActiveVerb == V0VerbWalkTo && ActiveObject == 0))
        {
            _sentences.Add(new Sentence(ActiveVerb, ActiveObject, ActiveObject2));
            if (ActiveVerb != V0VerbWalkTo)
            {
                ActiveVerb = V0VerbWalkTo;
                ActiveObject = 0;
                ActiveObject2 = 0;
            }

            _walkToObjectState = WalkDone;
            return;
        }

        // the room, in actor units (VAR(6) and VAR(7) hold where the kid goes); a frozen kid stays
        var layout = ScreenLayout.ForVersion(Version);
        int walkX = (Mouse.X + Camera.Left) / 8, walkY = (Mouse.Y - layout.RoomTop) / 2;
        Engine.Variables[6] = walkX;
        Engine.Variables[7] = walkY;
        int ego = Engine.Variables[ScriptEngine.VarEgo];
        if (ego is > 0 and < ActorCount && (ActorV0Of(ego).MiscFlags & ActorV0.MiscFlagFreeze) == 0)
        {
            Actors[ego].StartWalk(walkX, walkY, -1);
        }
    }

    /// <summary>Whether the sentence is whole: a verb that is not Walk to or What is, its object, and a second when it takes a preposition. Mirrors <c>checkSentenceComplete</c>.</summary>
    private bool SentenceComplete()
        => ActiveVerb is not (V0VerbNone or V0VerbWalkTo or V0VerbWhatIs)
        && ActiveObject != 0 && (ActiveVerbPreposition() == PrepNone || ActiveObject2 != 0);

    /// <summary>
    /// The preposition the chosen verb takes with the chosen object: the verb's own, or for Use the
    /// object's. Mirrors <c>activeVerbPrep</c> and <c>getVerbPrepId</c>.
    /// </summary>
    private int ActiveVerbPreposition()
    {
        if (ActiveVerb == V0VerbNone || ActiveObject == 0)
        {
            return PrepNone;
        }

        int slot = Verbs.SlotOf(ActiveVerb);
        int preposition = slot == 0 ? PrepNone : Verbs.Slots[slot].Preposition;
        return preposition != PrepFromObject ? preposition : ObjectCode(ActiveObject)?.Preposition ?? PrepNone;
    }

    /// <summary>
    /// The sentence line: the verb, its object, the preposition and the second object; after New kid,
    /// the three kids' names. Mirrors <c>ScummEngine_v0::drawSentenceLine</c>.
    /// </summary>
    private void DrawSentenceLineV0()
    {
        _redrawSentenceLine = false;
        if ((Engine.Interface & VerbArea.ShowSentence) == 0)
        {
            return;
        }

        var sentence = new StringBuilder();
        if (ActiveVerb == V0VerbNewKid)
        {
            for (int i = 0; i < 3; i++)
            {
                // a kid's variable is 0 after the radiation suit
                int kid = Engine.Variables[VarKids + i];
                sentence.Append((kid is > 0 and < ActorCount ? Actors[kid].Name : " ").PadRight(13));
            }
        }
        else
        {
            if (ActiveVerb == V0VerbNone)
            {
                ActiveVerb = V0VerbWalkTo;
            }

            sentence.Append(VerbName(ActiveVerb));
            if (ActiveObject != 0)
            {
                sentence.Append(' ').Append(NameOf(ActiveObject));
                int preposition = ActiveVerbPreposition();
                if (preposition is > 0 and <= 4)
                {
                    string[] prepositions = [" ", " in", " with", " on", " to"];
                    sentence.Append(prepositions[preposition]);
                    if (ActiveObject2 != 0)
                    {
                        sentence.Append(' ').Append(NameOf(ActiveObject2));
                    }
                }
            }
        }

        Verbs.DrawSentence(Encoding.Latin1.GetBytes(sentence.ToString()));
    }

    /// <summary>
    /// Runs the next sentence: first any walk to its object is waited for. A sentence with two
    /// objects neither of which the kid carries has the kid pick one up first; one whose object is
    /// in the room has the kid walk to it first, but in the keypad mode. Six nested sentences in a
    /// row are given up. Mirrors <c>ScummEngine_v0::checkAndRunSentenceScript</c>.
    /// </summary>
    private void CheckAndRunSentenceScriptV0()
    {
        if (CheckPendingWalkAction() || _sentences.Count == 0)
        {
            return;
        }

        var sentence = _sentences[^1];
        if (sentence.HasPreposition && sentence.ObjectB == sentence.ObjectA)
        {
            _sentences.RemoveAt(_sentences.Count - 1);
            return;
        }

        int ego = Engine.Variables[ScriptEngine.VarEgo];
        bool Carried(int obj) => obj >> 8 == TypeForeground && Engine.GetOwner(obj) == ego;
        if (sentence.ObjectB != 0 && !Carried(sentence.ObjectA) && !Carried(sentence.ObjectB))
        {
            if (HasVerb(sentence.ObjectA, V0VerbPickUp))
            {
                _sentences.Add(new Sentence(V0VerbPickUp, sentence.ObjectA, 0));
            }
            else if (HasVerb(sentence.ObjectB, V0VerbPickUp))
            {
                _sentences.Add(new Sentence(V0VerbPickUp, sentence.ObjectB, 0));
            }
            else
            {
                _sentences.RemoveAt(_sentences.Count - 1);
            }

            return;
        }

        Engine.CommandVerb = sentence.Verb;
        Engine.CommandObject = sentence.ObjectA;
        Engine.CommandObject2 = sentence.ObjectB;
        _sentences.RemoveAt(_sentences.Count - 1);

        if (++_sentenceNestedCount > 6)
        {
            _sentenceNestedCount = 0;
            _sentences.Clear();
            return;
        }

        if (WhereIs(sentence.ObjectA) != 1)
        {
            if (CurrentMode != ModeKeypad)
            {
                WalkToActorOrObject(sentence.ObjectA);
                return;
            }
        }
        else if (sentence.ObjectB != 0 && WhereIs(sentence.ObjectB) != 1)
        {
            WalkToActorOrObject(sentence.ObjectB);
            return;
        }

        RunSentenceScript();
        if (CurrentMode == ModeKeypad)
        {
            _walkToObjectState = WalkDone;
        }
    }

    /// <summary>
    /// The sentence's object runs its script for the verb, but Read in the dark; Give with no such
    /// script gives the object to the kid named; else script 3 does what the verb does on its own.
    /// Mirrors <c>ScummEngine_v0::runSentenceScript</c>.
    /// </summary>
    private void RunSentenceScript()
    {
        _redrawSentenceLine = true;
        int verb = Engine.CommandVerb, obj = Engine.CommandObject;
        if (HasVerb(obj, verb))
        {
            if (!(verb == V0VerbRead && CurrentLights == 0))
            {
                Engine.Variables[Engine.Vars.ActiveObject2] = Engine.CommandObject2 & 0xFF;
                RunObjectScript(obj, verb, false, false);
                return;
            }
        }
        else if (verb == V0VerbGive)
        {
            // with no script for it, Give hands the object to another kid, or to nobody
            int actor = Engine.CommandObject2 & 0xFF;
            if (actor < 8)
            {
                Engine.ObjectOwners[obj & 0xFF] = (byte)actor;
                RedrawInventory();
            }

            return;
        }

        if (verb != V0VerbWalkTo)
        {
            Engine.Variables[Engine.Vars.ActiveVerb] = verb;
            Engine.RunScript(V0FallbackScript);
        }
    }

    /// <summary>
    /// The kid walks to the sentence's object first, or beside its actor; VAR(6) and VAR(7) hold the
    /// object's point, or the kid's own. A frozen kid stays. Mirrors <c>walkToActorOrObject</c>.
    /// </summary>
    private void WalkToActorOrObject(int obj)
    {
        int ego = Engine.Variables[ScriptEngine.VarEgo];
        if (ego is <= 0 or >= ActorCount)
        {
            return;
        }

        var actor = ActorV0Of(ego);
        _walkToObject = obj;
        _walkToObjectState = WalkWalking;
        if (obj >> 8 == TypeActor)
        {
            WalkActorToActor(actor, Actors[obj & 0xFF], 4);
            Engine.Variables[6] = actor.X;
            Engine.Variables[7] = actor.Y;
        }
        else
        {
            WalkActorToObject(actor, obj);
            if (ObjectPosition(obj) is { } target)
            {
                Engine.Variables[6] = target.X;
                Engine.Variables[7] = target.Y;
            }
        }

        if ((actor.MiscFlags & ActorV0.MiscFlagFreeze) != 0)
        {
            actor.Halt();
        }
    }

    /// <summary>
    /// Whether the sentence still waits for the kid: walking, then, once near the object, turning
    /// to it (or kid and actor to each other, but for the plant), after which its script runs. A
    /// kid who stopped short gives the sentence up. Mirrors <c>checkPendingWalkAction</c>.
    /// </summary>
    private bool CheckPendingWalkAction()
    {
        if (_walkToObjectState == WalkDone)
        {
            return false;
        }

        int ego = Engine.Variables[ScriptEngine.VarEgo];
        if (ego is <= 0 or >= ActorCount)
        {
            _walkToObjectState = WalkDone;
            return false;
        }

        var actor = Actors[ego];
        if (actor.Moving != ActorV0.Standing)
        {
            return true;
        }

        if (_walkToObjectState == WalkTurning)
        {
            RunSentenceScript();
        }
        else
        {
            bool toActor = _walkToObject >> 8 == TypeActor;
            (int X, int Y)? target = null;
            int direction = -1;
            if (toActor)
            {
                var other = Actors[_walkToObject & 0xFF];
                target = (other.X < actor.X ? other.X + 4 : other.X - 4, other.Y);
            }
            else if (ObjectPosition(_walkToObject) is { } position)
            {
                target = (position.X, position.Y);
                direction = position.Direction;
            }

            if (target is { } found)
            {
                var (point, _) = actor.AdjustToBox(found.X, found.Y);
                if (Math.Abs(actor.X - point.X) <= 4 && Math.Abs(actor.Y - point.Y) <= 8)
                {
                    if (toActor)
                    {
                        var other = Actors[_walkToObject & 0xFF];
                        actor.FaceTowards(other.X, other.Y);
                        if (other.Number != V0Plant)
                        {
                            other.FaceTowards(actor.X, actor.Y);
                        }
                    }
                    else
                    {
                        actor.TurnTo(direction);
                    }

                    _walkToObjectState = WalkTurning;
                    return true;
                }
            }
        }

        _walkToObjectState = WalkDone;
        return false;
    }
}
