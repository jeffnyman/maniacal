using System.Text;
using Maniacal.Scumm.Detection;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Saves;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Game;

/// <summary>
/// The world as a ScummVM save holds it, and back. Mirrors what <c>ScummEngine::saveState</c>
/// captures and what <c>loadState</c> does with it: the room is entered without its scripts, the
/// actors, scripts, objects, verbs, message and interface are put back, sounds stop, and the
/// screen is drawn again from scratch.
/// </summary>
public sealed partial class GameWorld
{
    // the variable ScummVM keeps the talking actor in for version 2 (VAR_TALK_ACTOR)
    private const int VarTalkActor = 41;

    // the shape of the version 2 cursor (ScummVM's _cursor for setBuiltinCursor)
    private const int CursorWidth = 23, CursorHeight = 21, CursorHotspotX = 11, CursorHotspotY = 10;

    // where a script's code is (WhereIsObject)
    private const int WhereInventory = 0, WhereRoom = 1, WhereGlobal = 2;

    /// <summary>The room the game's save screen is in; a game loaded from elsewhere starts again from the boot script first (<c>saveRoom</c>).</summary>
    public const int SaveRoom = 50;

    /// <summary>Where the game's own save screen keeps its saves; with none, saving fails and no save exists.</summary>
    public Daventry.Core.Storage.ISaveStore? SaveStore { get; set; }

    /// <summary>The name save files start with, as ScummVM names them after the game's target (<c>maniac.s01</c>).</summary>
    public string SaveTarget { get; set; } = "maniac";

    /// <summary>The save version the save screen writes.</summary>
    public int SaveVersion { get; set; } = SaveFile.CompatibleVersion;

    /// <summary>Saves the game as a ScummVM save file, in a save version (108, ScummVM 2.7's, by default).</summary>
    public byte[] Save(string name, DateTime saved, int version = SaveFile.CompatibleVersion)
        => SaveFile.Write(Capture(), name, Time, saved, version);

    /// <summary>Loads a ScummVM save file made here or by ScummVM.</summary>
    public SaveInfo Load(byte[] file)
    {
        var (info, state) = SaveFile.Read(file, Engine.ObjectOwners.Length);
        Restore(state);
        return info;
    }

    /// <summary>The world as a ScummVM save state. Mirrors the state <c>saveLoadWithSerializer</c> writes.</summary>
    public ScummState Capture()
    {
        var s = new ScummState(Engine.ObjectOwners.Length);
        var room = CurrentRoom;
        var layout = ScreenLayout.ForVersion(Version);

        s.GameMd5 = ScummDetector.IndexMd5(_loader.Game);
        s.CurrentRoom = s.RoomResource = room?.Number ?? 0;
        s.CurrentScript = Engine.CurrentSlot < 0 ? 0xFF : Engine.CurrentSlot;
        s.ObjectsInRoom = room?.Objects.Count ?? 0;
        Camera.SaveTo(s);
        Messages.SaveTo(s);

        // cutscene: the override's slot and where it skips to, and what o2_cutscene kept
        if (Engine.Override is { } skip)
        {
            s.CutScenePtr[0] = BaseOffset(Engine.Slots[skip.Slot]) + skip.Offset;
            s.CutSceneScript[0] = (byte)skip.Slot;
        }

        s.CutSceneData[0] = Engine.SavedForCutscene.UserState;
        s.CutSceneData[1] = Engine.SavedForCutscene.CursorState;
        s.CutSceneData[2] = _beforeCutscene.Room;
        s.CutSceneData[3] = (int)_beforeCutscene.Mode;

        s.UserPut = s.CursorState = Engine.CursorOn ? 1 : 0;
        s.UserState = Engine.Interface;
        DrawCursorImage(s.GrabbedCursor);
        (s.CursorWidth, s.CursorHeight, s.CursorHotspotX, s.CursorHotspotY) = (CursorWidth, CursorHeight, CursorHotspotX, CursorHotspotY);
        s.CursorAnimate = 1;
        s.CursorAnimateIndex = _cursorFrame & 0xFF;
        (s.MouseX, s.MouseY) = Mouse;

        // what resetScumm and the room's entry leave for these versions on DOS
        s.SwitchRoomEffect = 5;
        s.SwitchRoomEffect2 = 1;
        s.NewEffect = 129;
        s.ScreenEffectFlag = 1;
        s.ScreenB = layout.RoomTop;
        s.ScreenH = layout.RoomBottom;
        var ega = Palettes.Ega;
        for (int i = 0; i < ega.Length; i++)
        {
            s.CurrentPalette[i * 3] = s.DarkenPalette[i * 3] = ega[i].R;
            s.CurrentPalette[i * 3 + 1] = s.DarkenPalette[i * 3 + 1] = ega[i].G;
            s.CurrentPalette[i * 3 + 2] = s.DarkenPalette[i * 3 + 2] = ega[i].B;
        }

        for (int i = 0; i < 256; i++)
        {
            s.RoomPalette[i] = (byte)i;
            s.ShadowPalette[i] = Version >= 2 && i < 16 ? (byte)i : (byte)0;
        }

        for (int i = 0; i < ScummState.ActorCount; i++)
        {
            Actors[i].SaveTo(s.Actors[i]);
            if (Actors[i].Name.Length > 0)
            {
                s.Resources[(ScummState.TypeActorName, i)] = new ScummState.Resource([.. Encoding.Latin1.GetBytes(Actors[i].Name), 0]);
            }
        }

        for (int i = 0; i < ScummState.SlotCount; i++)
        {
            SaveSlot(Engine.Slots[i], s.Slots[i]);
        }

        if (room is not null)
        {
            for (int i = 0; i < room.Objects.Count && i + 1 < ScummState.LocalObjectCount; i++)
            {
                SaveObject(room.Objects[i], s.Objects[i + 1]);
            }
        }

        Verbs.SaveTo(s);
        s.SentenceNumber = _sentences.Count;
        for (int i = 0; i < _sentences.Count; i++)
        {
            var sentence = _sentences[i];
            (s.Sentences[i].Verb, s.Sentences[i].ObjectA, s.Sentences[i].ObjectB) = (sentence.Verb, sentence.ObjectA, sentence.ObjectB);
            s.Sentences[i].Preposition = sentence.HasPreposition ? 1 : 0;
        }

        SaveStrings(s);
        SaveResources(s, room);

        Array.Copy(Engine.ObjectOwners, s.ObjectOwners, s.ObjectOwners.Length);
        Array.Copy(Engine.ObjectStates, s.ObjectStates, s.ObjectStates.Length);
        Array.Copy(Engine.Variables, s.Variables, ScummState.VariableCount);
        if (Version >= 2)
        {
            s.Variables[VarTalkActor] = Messages.TalkingActor;
        }

        (s.FlashlightStrips, s.FlashlightRows) = _flashlight;
        return s;
    }

    /// <summary>
    /// Puts the world in the state a save holds. Mirrors <c>loadState</c>: sounds stop, the room is
    /// set up without running its scripts, and everything else is taken from the save.
    /// </summary>
    public void Restore(ScummState s)
    {
        Sound.StopAll();
        foreach (var slot in Engine.Slots)
        {
            slot.Kill();
        }

        Array.Copy(s.ObjectOwners, Engine.ObjectOwners, Math.Min(s.ObjectOwners.Length, Engine.ObjectOwners.Length));
        Array.Copy(s.ObjectStates, Engine.ObjectStates, Math.Min(s.ObjectStates.Length, Engine.ObjectStates.Length));
        Array.Copy(s.Variables, Engine.Variables, ScummState.VariableCount);

        // the room, entered without its scripts, with the boxes and objects as the save has them
        _moved.Clear();
        CurrentRoom = s.CurrentRoom == 0 ? null : _loader.LoadRoom(s.CurrentRoom) ?? throw new InvalidDataException($"The save is in room {s.CurrentRoom}, which this release does not have.");
        var room = CurrentRoom;
        _boxFlags = room?.WalkBoxes.Boxes.Select(b => b.Flags).ToArray() ?? [];
        if (room is not null)
        {
            if (s.Resources.TryGetValue((ScummState.TypeMatrix, 2), out var boxes))
            {
                for (int i = 0; i < _boxFlags.Length && 1 + i * WalkBox.Size + 7 < boxes.Data.Length; i++)
                {
                    _boxFlags[i] = boxes.Data[1 + i * WalkBox.Size + 7];
                }
            }

            for (int i = 0; i < room.Objects.Count && i + 1 < ScummState.LocalObjectCount; i++)
            {
                var saved = s.Objects[i + 1];
                var obj = room.Objects[i];
                if (saved.Number == obj.Number && (saved.X, saved.Y) != (obj.X, obj.Y))
                {
                    _moved[obj.Number] = (saved.X, saved.Y);
                }
            }
        }

        Camera.LoadFrom(s, room?.Width ?? ScreenLayout.Width);
        SyncCameraLimits();
        for (int i = 0; i < ScummState.ActorCount; i++)
        {
            Actors[i].LoadFrom(s.Actors[i]);
            Actors[i].Name = s.Resources.TryGetValue((ScummState.TypeActorName, i), out var name) ? Text(name.Data) : string.Empty;
        }

        RestoreInventoryAndNames(s);
        for (int i = 0; i < ScummState.SlotCount; i++)
        {
            RestoreSlot(s.Slots[i], Engine.Slots[i]);
        }

        Engine.Override = s.CutScenePtr[0] != 0 && s.CutSceneScript[0] < ScummState.SlotCount && !Engine.Slots[s.CutSceneScript[0]].IsDead
            ? (s.CutSceneScript[0], s.CutScenePtr[0] - BaseOffset(Engine.Slots[s.CutSceneScript[0]]))
            : null;
        Engine.SavedForCutscene = (s.CutSceneData[0], s.CutSceneData[1]);
        _beforeCutscene = (s.CutSceneData[2], Enum.IsDefined((CameraMode)s.CutSceneData[3]) ? (CameraMode)s.CutSceneData[3] : CameraMode.Normal);
        Engine.Interface = s.UserState & VerbArea.ShowSentence | s.UserState & VerbArea.ShowInventory | s.UserState & VerbArea.ShowVerbs;
        Engine.CursorOn = s.UserPut > 0;

        _sentences.Clear();
        for (int i = 0; i < Math.Min(s.SentenceNumber, ScummState.SentenceCount); i++)
        {
            _sentences.Add(new Sentence(s.Sentences[i].Verb, s.Sentences[i].ObjectA, s.Sentences[i].ObjectB));
        }

        int talking = Version >= 2 ? s.Variables[VarTalkActor] : s.HaveMessage != 0 ? s.ActorToPrintStrFor : MessageLine.NoActor;
        Messages.LoadFrom(s, talking);
        _flashlight = (s.FlashlightStrips, s.FlashlightRows);
        _cursorFrame = s.CursorAnimateIndex;
        MoveMouse(s.MouseX, s.MouseY);

        // loadState redraws the verbs and the inventory; the sentence line stays clear until a sentence is drawn
        Verbs.LoadFrom(s);
        Verbs.Redraw(CursorShown ? Verbs.FindAt(Mouse.X, Mouse.Y) : 0);
        RedrawInventory();
        _frame = null;
        _actorStrips.Clear();
    }

    /// <summary>
    /// The save screen's instruction: how many slots, which drive, whether a slot has a save, save
    /// to a slot, load from one. A load replaces the world; the script that asked carries on after
    /// the instruction in the loaded state, or, when the save was not made in the save room, the
    /// game starts again from the boot script and is loaded once more. Mirrors <c>o4_saveLoadGame</c>.
    /// </summary>
    private int SaveLoadGame(int argument, Instruction instruction)
    {
        int slot, operation;
        if (Version <= 1)
        {
            // version 1 of Maniac Mansion has one save, and asks for it as 1 (load) or 2 (save)
            slot = 1;
            operation = argument switch
            {
                1 => 0x40,
                2 => 0x80,
                _ => throw new InvalidOperationException($"saveLoadGame has no operation {argument}."),
            };
        }
        else
        {
            slot = (argument & 0x1F) + 1;
            operation = argument & 0xE0;
        }

        string fileName = SaveFile.FileName(SaveTarget, slot);
        switch (operation)
        {
            case 0x00:
                return 100;
            case 0x20:
                return 3;
            case 0xC0:
                return SaveStore?.OpenRead(fileName) is { } existing ? Exists(existing) : 7;
            case 0x80:
                {
                    if (SaveStore is null)
                    {
                        return 2;
                    }

                    var data = SaveFile.Write(Capture(), $"Game {(char)('A' + slot - 1)}", Time, DateTime.Now, SaveVersion);
                    using var output = SaveStore.OpenWrite(fileName);
                    output.Write(data);
                    return 0;
                }

            case 0x40:
                {
                    if (SaveStore?.OpenRead(fileName) is not { } input)
                    {
                        return 5;
                    }

                    byte[] file;
                    using (input)
                    {
                        using var copy = new MemoryStream();
                        input.CopyTo(copy);
                        file = copy.ToArray();
                    }

                    ScummState state;
                    try
                    {
                        (_, state) = SaveFile.Read(file, Engine.ObjectOwners.Length);
                    }
                    catch (InvalidDataException)
                    {
                        return 5;
                    }

                    Restore(state);
                    if (state.CurrentRoom != SaveRoom)
                    {
                        // a save from outside the save room: the game starts over, then loads again
                        Engine.ContinueAfterLoad(-1, 0);
                        Boot();
                        Restore(state);
                        Engine.ContinueAfterLoad(state.CurrentScript == 0xFF ? -1 : state.CurrentScript, Engine.Slots[Math.Min(state.CurrentScript, ScummState.SlotCount - 1)].Offset);
                        return instruction.Result is { } result ? Engine.ReadVar(result) : 0;
                    }

                    Engine.ContinueAfterLoad(state.CurrentScript == 0xFF ? -1 : state.CurrentScript, instruction.End);
                    return 3;
                }

            default:
                throw new InvalidOperationException($"saveLoadGame has no operation {operation:X2}.");
        }

        static int Exists(Stream stream)
        {
            stream.Dispose();
            return 6;
        }
    }

    /// <summary>Where a slot's offsets count from in ScummVM: its object's code block in the room, the room for entry and exit scripts, else 0.</summary>
    private int BaseOffset(ScriptSlot slot)
    {
        if (slot.Kind != ScriptKind.Room || CurrentRoom is not { } room)
        {
            return 0;
        }

        return slot.Number switch
        {
            ScriptEngine.EntryScriptNumber => room.EntryScriptOffset,
            ScriptEngine.ExitScriptNumber => room.ExitScriptOffset,
            _ => room.FindObject(slot.Number)?.CodeOffset ?? 0,
        };
    }

    private void SaveSlot(ScriptSlot slot, ScummState.SlotState saved)
    {
        if (slot.IsDead)
        {
            return;
        }

        saved.Number = slot.Number;
        saved.Status = slot.Status;
        saved.Where = slot.Kind switch
        {
            ScriptKind.Global => WhereGlobal,
            ScriptKind.Inventory => WhereInventory,
            _ => WhereRoom,
        };
        // the running slot (a save made by the save screen) is where it last yielded, as ScummVM's offs is
        saved.Offset = BaseOffset(slot) + (Engine.CurrentSlot >= 0 && Engine.Slots[Engine.CurrentSlot] == slot ? slot.ResumeOffset : slot.Offset);
        saved.Delay = slot.Delay;
        saved.FreezeResistant = slot.FreezeResistant ? 1 : 0;
        saved.Recursive = slot.Recursive ? 1 : 0;
        saved.Cycle = 1;
    }

    /// <summary>A slot from a save: its code is found again from its number and where it lives.</summary>
    private void RestoreSlot(ScummState.SlotState saved, ScriptSlot slot)
    {
        if (saved.Status == SlotStatus.Dead || saved.Number == 0)
        {
            return;
        }

        var kind = saved.Where switch
        {
            WhereGlobal => ScriptKind.Global,
            WhereInventory => ScriptKind.Inventory,
            _ => ScriptKind.Room,
        };

        ReadOnlyMemory<byte>? code = kind switch
        {
            ScriptKind.Global => _loader.Index.Locate(ResourceType.Script, saved.Number).IsPresent ? _loader.Load(ResourceType.Script, saved.Number) : null,
            ScriptKind.Inventory => _carried.TryGetValue(saved.Number, out var carried) ? carried.Code : null,
            _ => saved.Number switch
            {
                ScriptEngine.EntryScriptNumber => CurrentRoom?.EntryScript,
                ScriptEngine.ExitScriptNumber => CurrentRoom?.ExitScript,
                _ => CurrentRoom?.FindObject(saved.Number)?.Code,
            },
        };

        if (code is null)
        {
            return;
        }

        slot.Number = saved.Number;
        slot.Kind = kind;
        slot.Code = code.Value;
        slot.Status = (byte)saved.Status;
        slot.Delay = saved.Delay;
        slot.FreezeResistant = saved.FreezeResistant != 0;
        slot.Recursive = saved.Recursive != 0;
        slot.Offset = saved.Offset - BaseOffset(slot);
    }

    /// <summary>A room object as ScummVM's <c>ObjectData</c> has it: its offsets, rectangle and walk-to point, moved if drawn elsewhere.</summary>
    private void SaveObject(RoomObject obj, ScummState.ObjectState saved)
    {
        var (x, y) = PositionOf(obj);
        saved.ImageOffset = obj.ImageOffset;
        saved.CodeOffset = obj.CodeOffset;
        saved.Number = obj.Number;
        saved.X = x;
        saved.Y = y;
        saved.WalkX = obj.WalkX + x - obj.X;
        saved.WalkY = obj.WalkY + y - obj.Y;
        saved.Width = obj.Width;
        saved.Height = obj.Height;
        saved.ActorDir = obj.ActorDir;
        saved.ParentState = obj.ParentState;
        saved.Parent = obj.Parent;
        saved.State = obj.Number < Engine.ObjectStates.Length ? Engine.ObjectStates[obj.Number] : 0;
    }

    /// <summary>The text slots as ScummVM leaves them: each with <c>resetScumm</c>'s defaults; 0 for messages, the others for the verbs, the inventory and the sentence.</summary>
    private void SaveStrings(ScummState s)
    {
        foreach (var text in s.Strings)
        {
            text.Right = 319;
        }

        s.Strings[0].Color = Messages.StringColor;
        (s.Strings[1].Charset, s.Strings[1].Color) = (1, 13);
        (s.Strings[2].Charset, s.Strings[2].Color, s.Strings[2].Y) = (1, 13, Verbs.Top);
    }

    /// <summary>The resources ScummVM keeps outside the game files: carried objects, new names, the room's boxes and the sounds loaded.</summary>
    private void SaveResources(ScummState s, Room? room)
    {
        for (int i = 0; i < _inventory.Count; i++)
        {
            int obj = _inventory[i];
            if (obj != 0 && _carried.TryGetValue(obj, out var code))
            {
                s.Resources[(ScummState.TypeInventory, i)] = new ScummState.Resource(code.Code.ToArray(), obj);
            }
        }

        int nameIndex = 0;
        foreach (var (obj, name) in _newNames)
        {
            s.Resources[(ScummState.TypeObjectName, nameIndex++)] = new ScummState.Resource([.. Encoding.Latin1.GetBytes(name), 0], obj);
        }

        if (room is not null && room.BoxOffset != 0)
        {
            var data = room.Data.Span;
            int count = data[room.BoxOffset];
            var boxes = data.Slice(room.BoxOffset, count * WalkBox.Size + 1).ToArray();
            for (int i = 0; i < count && i < _boxFlags.Length; i++)
            {
                boxes[1 + i * WalkBox.Size + 7] = _boxFlags[i];
            }

            s.Resources[(ScummState.TypeMatrix, 2)] = new ScummState.Resource(boxes);
            int matrix = count * (count + 1);
            if (matrix > 0)
            {
                s.Resources[(ScummState.TypeMatrix, 1)] = new ScummState.Resource(data.Slice(room.BoxOffset + boxes.Length, matrix).ToArray());
            }
        }

        foreach (var (sound, data) in _sounds)
        {
            if (data is not null)
            {
                s.Resources[(ScummState.TypeSound, sound)] = new ScummState.Resource([]);
            }
        }
    }

    /// <summary>The carried objects with their code, and the new names, from a save's resources.</summary>
    private void RestoreInventoryAndNames(ScummState s)
    {
        _inventory.Clear();
        _carried.Clear();
        _newNames.Clear();
        foreach (var ((type, index), resource) in s.Resources)
        {
            if (type == ScummState.TypeInventory && resource.Number != 0 && resource.Data.Length >= 16)
            {
                while (_inventory.Count <= index)
                {
                    _inventory.Add(0);
                }

                _inventory[index] = resource.Number;
                _carried[resource.Number] = RoomObject.Parse(resource.Data, 0, 0);
            }
            else if (type == ScummState.TypeObjectName && resource.Number != 0)
            {
                _newNames[resource.Number] = Text(resource.Data);
            }
        }
    }

    /// <summary>The version 2 crosshair in the colour it has now, on transparent 0xFF, as <c>setBuiltinCursor</c> leaves <c>_grabbedCursor</c>.</summary>
    private void DrawCursorImage(byte[] image)
    {
        Array.Fill(image, (byte)0xFF);
        var surface = new Daventry.Core.Graphics.Surface8(CursorWidth, CursorHeight);
        surface.Fill(0xFF);
        var mouse = Mouse;
        bool shown = Engine.CursorOn;
        Mouse = (CursorHotspotX, CursorHotspotY);
        Engine.CursorOn = true;
        DrawCursor(surface);
        Engine.CursorOn = shown;
        Mouse = mouse;
        surface.Pixels.CopyTo(image, 0);
    }

    private static string Text(byte[] data)
    {
        int end = Array.IndexOf(data, (byte)0);
        return Encoding.Latin1.GetString(data, 0, end < 0 ? data.Length : end);
    }
}
