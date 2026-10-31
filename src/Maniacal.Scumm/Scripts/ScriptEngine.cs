namespace Maniacal.Scumm.Scripts;

/// <summary>
/// The script engine of versions 1 and 2: 80 slots of cooperative threads, the variables, and the
/// instructions that work on them. Mirrors ScummVM's <c>script.cpp</c> (<c>runScript</c>,
/// <c>runScriptNested</c>, <c>executeScript</c>, <c>runAllScripts</c>, <c>decreaseScriptDelay</c>,
/// <c>stopScript</c>, <c>stopObjectCode</c>, <c>freezeScripts</c>) for <c>version &lt;= 2</c>, and
/// the handlers of <c>script_v2.cpp</c> that touch only script state. The rest goes to an
/// <see cref="IScriptHost"/>.
/// </summary>
internal sealed class ScriptEngine
{
    /// <summary>Mirrors <c>NUM_SCRIPT_SLOT</c>.</summary>
    public const int SlotCount = 80;

    /// <summary>Mirrors <c>_numVariables</c> of <c>ScummEngine_v4::readMAXS</c>, which versions 1 and 2 use.</summary>
    public const int VariableCount = 800;

    /// <summary>The numbers rooms' scripts run under. Mirrors <c>kScriptNumEXCD</c> and <c>kScriptNumENCD</c>.</summary>
    public const int ExitScriptNumber = 10001, EntryScriptNumber = 10002;

    /// <summary>Global script code starts after the 4-byte resource header (<c>_resourceHeaderSize</c>).</summary>
    public const int ScriptHeaderSize = 4;

    /// <summary>Version 0's bit variables (<c>_numBitVariables</c>, <c>ScummEngine_v4::readMAXS</c>).</summary>
    public const int BitVariableCount = 4096;

    /// <summary>A script running this many instructions without yielding is taken to be stuck.</summary>
    public const int InstructionBudget = 100_000;

    // the engine variables of version 2 (ScummEngine_v2::setupScummVars)
    public const int VarEgo = 0, VarHaveMessage = 3, VarRoom = 4, VarOverride = 5, VarCursorState = 21,
        VarTimerNext = 25, VarCutsceneExitKey = 40;

    /// <summary>The global script that runs sentences. Mirrors <c>SENTENCE_SCRIPT</c> in <c>script_v2.cpp</c>.</summary>
    public const int SentenceScript = 2;

    // user state bits (USERSTATE_*)
    public const byte UserSetFreeze = 0x01, UserSetCursor = 0x02, UserSetInterface = 0x04, UserFreezeOn = 0x08,
        UserCursorOn = 0x10, UserInterfaceMask = 0xE0;

    // object state bits (ObjectStateV2)
    public const byte StatePickupable = 1, StateUntouchable = 2, StateLocked = 4, StateIntrinsic = 8;

    private readonly Func<int, ReadOnlyMemory<byte>?> _loadScript;
    private readonly IScriptHost _host;
    private readonly ScriptDecoder _decoder;
    private readonly Random _random;
    private readonly List<(int Number, ScriptKind Kind, int Slot)> _nested = [];
    private readonly int[] _arguments = new int[16];

    public ScriptEngine(int version, Func<int, ReadOnlyMemory<byte>?> loadScript, IScriptHost host, IReadOnlyList<byte> objectOwners, IReadOnlyList<byte> objectStates, int seed = 1)
    {
        Version = version;
        Vars = EngineVariables.For(version);
        _loadScript = loadScript;
        _host = host;
        _decoder = new ScriptDecoder(version);
        _random = new Random(seed);
        ObjectOwners = [.. objectOwners];
        ObjectStates = [.. objectStates];
        for (int i = 0; i < Slots.Length; i++)
        {
            Slots[i] = new ScriptSlot();
        }
    }

    public int Version { get; }

    /// <summary>The numbers of the variables the engine itself uses, as this version gives them.</summary>
    public EngineVariables Vars { get; }

    public int[] Variables { get; } = new int[VariableCount];

    /// <summary>
    /// Version 0's bit variables, 4096 bits, which its instructions address by byte and bit
    /// (<c>_bitVars</c>; versions 1 and 2 keep theirs in <see cref="Variables"/>).
    /// </summary>
    public byte[] BitVariables { get; } = new byte[BitVariableCount / 8];

    /// <summary>
    /// Version 0: the sentence the scripts are running, its verb and objects (<c>_cmdVerb</c>,
    /// <c>_cmdObject</c>, <c>_cmdObject2</c>), the objects with their types in the high byte.
    /// </summary>
    public int CommandVerb { get; set; }

    /// <inheritdoc cref="CommandVerb"/>
    public int CommandObject { get; set; }

    /// <inheritdoc cref="CommandVerb"/>
    public int CommandObject2 { get; set; }

    public ScriptSlot[] Slots { get; } = new ScriptSlot[SlotCount];

    public byte[] ObjectOwners { get; }

    public byte[] ObjectStates { get; }

    /// <summary>The slot running now, or -1 (ScummVM's <c>_currentScript == 0xFF</c>).</summary>
    public int CurrentSlot { get; private set; } = -1;

    public int CurrentScriptNumber => CurrentSlot < 0 ? 0 : Slots[CurrentSlot].Number;

    /// <summary>The instruction running now, while the host carries it out.</summary>
    public Instruction? CurrentInstruction { get; private set; }

    /// <summary>The slot whose script set an override, and where it skips to. Mirrors <c>vm.cutSceneScript[0]</c> and <c>vm.cutScenePtr[0]</c>.</summary>
    public (int Slot, int Offset)? Override { get; internal set; }

    /// <summary>Mirrors <c>vm.cutSceneData[0]</c> and <c>[1]</c>: the user state and cursor state saved by <c>cutscene</c>.</summary>
    public (int UserState, int CursorState) SavedForCutscene { get; internal set; }

    /// <summary>Which parts of the interface show: the sentence line, inventory and verbs bits. Mirrors <c>_userState</c>.</summary>
    public int Interface { get; internal set; }

    /// <summary>Whether the cursor shows. Mirrors <c>_userPut</c>.</summary>
    public bool CursorOn { get; internal set; }

    /// <summary>
    /// Changes the interface, the cursor and whether scripts are frozen, as the bits say. Mirrors
    /// <c>ScummEngine_v2::setUserState</c>; redrawing the verbs and inventory is the host's.
    /// </summary>
    public void SetUserState(int state)
    {
        if ((state & UserSetInterface) != 0)
        {
            Interface = state & UserInterfaceMask;
        }

        if ((state & UserSetFreeze) != 0)
        {
            if ((state & UserFreezeOn) != 0)
            {
                FreezeScripts();
            }
            else
            {
                UnfreezeScripts();
            }
        }

        if ((state & UserSetCursor) != 0)
        {
            CursorOn = (state & UserCursorOn) != 0;
        }
    }

    /// <summary>
    /// Reads a variable. In versions 1 and 2, variables 14 to 16 hold the number of the variable
    /// read instead (<c>ScummEngine_v2::readVar</c>).
    /// </summary>
    public int ReadVar(int number)
    {
        if (Version >= 1 && number is >= 14 and <= 16)
        {
            number = Variables[number];
        }

        return Variables[CheckVariable(number)];
    }

    /// <summary>
    /// Writes a variable. The cutscene exit key is remapped as <c>ScummEngine_v2::writeVar</c> does:
    /// 4, 13 and 64 become 27, Escape.
    /// </summary>
    public void WriteVar(int number, int value)
    {
        CheckVariable(number);
        if (Version > 0 && number == VarCutsceneExitKey && value is 4 or 13 or 64)
        {
            value = 27;
        }

        Variables[number] = value;
    }

    /// <summary>
    /// Whether the game's copy protection is kept: the security door's code has to be found in the
    /// manual. Off by default, as ScummVM's <c>copy_protection</c> option is.
    /// </summary>
    public bool CopyProtection { get; set; }

    // the two sides of the steel security door (rooms 11 and 13), which the bypass keeps open
    internal const int SecurityDoorOutside = 182, SecurityDoorInside = 193;

    /// <summary>
    /// An object's state. Without the copy protection, the security door is always open: its
    /// Intrinsic bit is set whenever its state is read. Mirrors <c>ScummEngine::getState</c>.
    /// </summary>
    public int GetState(int obj)
    {
        if (!CopyProtection && Version != 0 && obj is SecurityDoorOutside or SecurityDoorInside)
        {
            ObjectStates[obj] |= StateIntrinsic;
        }

        return ObjectStates[obj];
    }

    public void PutState(int obj, int state) => ObjectStates[obj] = (byte)state;

    public int GetOwner(int obj) => ObjectOwners[obj];

    public bool IsScriptRunning(int number)
        => Slots.Any(s => s.Number == number && s.Kind == ScriptKind.Global && !s.IsDead);

    /// <summary>
    /// Starts a global script and runs it until it yields; then the caller, if any, carries on.
    /// Unless recursive, other copies of the script are stopped first. Mirrors <c>runScript</c>.
    /// </summary>
    public void RunScript(int number, bool freezeResistant = false, bool recursive = false)
    {
        if (number == 0)
        {
            return;
        }

        if (!recursive)
        {
            StopScript(number);
        }

        if (_loadScript(number) is not { } code)
        {
            throw new InvalidOperationException($"Script {number} does not exist.");
        }

        Start(number, ScriptKind.Global, code, ScriptHeaderSize, freezeResistant, recursive);
    }

    /// <summary>Runs a room's entry or exit script, from the start of <paramref name="code"/>.</summary>
    public void RunRoomScript(int pseudoNumber, ReadOnlyMemory<byte> code)
    {
        if (!code.IsEmpty)
        {
            Start(pseudoNumber, ScriptKind.Room, code, 0, false, false);
        }
    }

    /// <summary>
    /// Runs an object's verb script: <paramref name="code"/> is the object's code block,
    /// <paramref name="offset"/> the verb's entry. A slot can be given to run it in, replacing what
    /// ran there. Mirrors <c>runObjectScript</c>, which in these versions leaves other copies running.
    /// </summary>
    public void RunObjectScript(int obj, ReadOnlyMemory<byte> code, int offset, bool inInventory = false,
        bool freezeResistant = false, bool recursive = false, int slot = -1)
    {
        Start(obj, inInventory ? ScriptKind.Inventory : ScriptKind.Room, code, offset, freezeResistant, recursive, slot);
    }

    /// <summary>
    /// The slot running an object's script with these flags, or -1. <c>o2_doSentence</c> reuses it,
    /// using the flags to tell background and special-verb scripts apart.
    /// </summary>
    public int FindObjectSlot(int obj, bool freezeResistant, bool recursive)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = Slots[i];
            if (slot.Number == obj && slot.FreezeResistant == freezeResistant && slot.Recursive == recursive && slot.Kind != ScriptKind.Global)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Stops every script of an object, running or nested. Mirrors <c>stopObjectScript</c>.</summary>
    public void StopObjectScript(int obj)
    {
        if (obj == 0)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            var slot = Slots[i];
            if (slot.Number == obj && !slot.IsDead && slot.Kind != ScriptKind.Global)
            {
                slot.Kill();
                if (CurrentSlot == i)
                {
                    CurrentSlot = -1;
                }
            }
        }

        for (int i = 0; i < _nested.Count; i++)
        {
            if (_nested[i].Number == obj && _nested[i].Kind != ScriptKind.Global)
            {
                _nested[i] = (0, ScriptKind.Global, -1);
            }
        }
    }

    /// <summary>Runs every running slot once, from the first to the last. Mirrors <c>runAllScripts</c>.</summary>
    public void RunAllScripts()
    {
        CurrentSlot = -1;
        for (int i = 0; i < SlotCount; i++)
        {
            if (Slots[i].Status == SlotStatus.Running)
            {
                CurrentSlot = i;
                Slots[i].ResumeOffset = Slots[i].Offset;
                Execute();
            }
        }
    }

    /// <summary>Counts down paused slots and wakes those whose delay has run out. Mirrors <c>decreaseScriptDelay</c>.</summary>
    public void DecreaseScriptDelay(int ticks)
    {
        foreach (var slot in Slots)
        {
            if (slot.Status == SlotStatus.Paused)
            {
                slot.Delay -= ticks;
                if (slot.Delay < 0)
                {
                    slot.Status = SlotStatus.Running;
                    slot.Delay = 0;
                }
            }
        }
    }

    /// <summary>Stops every copy of a global script, running or nested. Mirrors <c>stopScript</c>.</summary>
    public void StopScript(int number)
    {
        if (number == 0)
        {
            return;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            var slot = Slots[i];
            if (slot.Number == number && !slot.IsDead && slot.Kind == ScriptKind.Global)
            {
                slot.Kill();
                if (CurrentSlot == i)
                {
                    CurrentSlot = -1;
                }
            }
        }

        for (int i = 0; i < _nested.Count; i++)
        {
            if (_nested[i].Number == number && _nested[i].Kind == ScriptKind.Global)
            {
                _nested[i] = (0, ScriptKind.Global, -1);
            }
        }
    }

    /// <summary>
    /// When the running script belongs to the room (its entry or exit script, or an object's), it
    /// stops running now: it is about to be killed with the room. Mirrors the start of <c>startScene</c>.
    /// </summary>
    public void LeaveRoomScript()
    {
        if (CurrentSlot >= 0 && Slots[CurrentSlot].Kind == ScriptKind.Room)
        {
            CurrentSlot = -1;
        }
    }

    /// <summary>Kills every script of the room being left; scripts of carried objects live on. Mirrors <c>killScriptsAndResources</c>.</summary>
    public void KillRoomScripts()
    {
        foreach (var slot in Slots)
        {
            if (slot.Kind == ScriptKind.Room && !slot.IsDead)
            {
                slot.Kill();
            }
        }
    }

    /// <summary>Freezes every slot but the running one and the freeze-resistant ones. Mirrors <c>freezeScripts</c> for <c>version &lt;= 2</c>.</summary>
    public void FreezeScripts()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (i != CurrentSlot && !Slots[i].IsDead && !Slots[i].FreezeResistant)
            {
                Slots[i].Status |= SlotStatus.Frozen;
            }
        }
    }

    public void UnfreezeScripts()
    {
        foreach (var slot in Slots)
        {
            slot.Status &= unchecked((byte)~SlotStatus.Frozen);
        }
    }

    /// <summary>
    /// Skips the running cutscene: the script that set the override jumps to it. Mirrors
    /// <c>abortCutscene</c> for the single cutscene level of versions 1 and 2.
    /// </summary>
    public void AbortCutscene()
    {
        if (Override is not { } skip)
        {
            return;
        }

        var slot = Slots[skip.Slot];
        slot.Offset = skip.Offset;
        slot.Status = SlotStatus.Running;
        Variables[Vars.Override] = 1;
        Override = null;
    }

    /// <summary>
    /// After a load in the middle of an instruction: the slot the save was running carries on
    /// from where the instruction ends, as ScummVM's <c>o4_saveLoadGame</c> puts the old script
    /// pointer into the loaded state's current script (<c>updateScriptPtr</c>). -1 for no slot.
    /// </summary>
    internal void ContinueAfterLoad(int slot, int offset)
    {
        _nested.Clear();
        CurrentSlot = slot;
        if (slot >= 0)
        {
            Slots[slot].Offset = offset;
        }
    }

    /// <summary>
    /// The script side of <c>o2_cutscene</c>: the interface and cursor state are kept, then hidden,
    /// the other scripts freeze and the sentence script stops.
    /// </summary>
    internal void BeginCutscene()
    {
        SavedForCutscene = (Interface | (CursorOn ? UserCursorOn : 0), Variables[VarCursorState]);
        Variables[VarCursorState] = 200;
        SetUserState(UserSetInterface | UserSetCursor | UserSetFreeze | UserFreezeOn);
        StopScript(SentenceScript);
        Override = null;
    }

    /// <summary>The script side of <c>o2_endCutscene</c>: the interface and cursor state kept by <see cref="BeginCutscene"/> come back.</summary>
    internal void EndCutscene()
    {
        Override = null;
        Variables[Vars.Override] = 0;
        Variables[VarCursorState] = SavedForCutscene.CursorState;
        SetUserState(SavedForCutscene.UserState | UserSetInterface | UserSetCursor | UserSetFreeze);
    }

    /// <summary>Stops the script in a slot as <c>stopObjectCode</c> does when the slot is the one running.</summary>
    internal void StopObjectCode(int slot)
    {
        CurrentSlot = slot;
        StopObjectCode();
    }

    /// <summary>Ends the running script's turn; it carries on from the next instruction next time. Mirrors <c>o5_breakHere</c>.</summary>
    public void BreakHere() => CurrentSlot = -1;

    /// <summary>For a host: the running instruction is to run again next time, as the wait instructions do.</summary>
    public void Repeat()
    {
        if (CurrentSlot >= 0 && CurrentInstruction is { } instruction)
        {
            Slots[CurrentSlot].Offset = instruction.Offset;
            BreakHere();
        }
    }

    private void Start(int number, ScriptKind kind, ReadOnlyMemory<byte> code, int offset, bool freezeResistant, bool recursive, int index = -1)
    {
        if (index < 0)
        {
            index = FreeSlot();
        }

        var slot = Slots[index];
        slot.Number = number;
        slot.Kind = kind;
        slot.Code = code;
        slot.Offset = offset;
        slot.Status = SlotStatus.Running;
        slot.FreezeResistant = freezeResistant;
        slot.Recursive = recursive;
        slot.Delay = 0;
        RunNested(index);
    }

    /// <summary>Mirrors <c>getScriptSlot</c>: the first dead slot from 1.</summary>
    private int FreeSlot()
    {
        for (int i = 1; i < SlotCount; i++)
        {
            if (Slots[i].IsDead)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Too many scripts running, {SlotCount} at most.");
    }

    /// <summary>Mirrors <c>runScriptNested</c>: run a slot until it yields, then go back to the caller if it is still there.</summary>
    private void RunNested(int index)
    {
        if (_nested.Count >= 15)
        {
            throw new InvalidOperationException("Too many nested scripts.");
        }

        int caller = CurrentSlot;
        _nested.Add(caller < 0 ? (0, ScriptKind.Global, -1) : (Slots[caller].Number, Slots[caller].Kind, caller));
        int level = _nested.Count - 1;

        // runScriptNested: the caller's position is kept (updateScriptPtr), and the new script starts
        if (caller >= 0)
        {
            Slots[caller].ResumeOffset = Slots[caller].Offset;
        }

        CurrentSlot = index;
        Slots[index].ResumeOffset = Slots[index].Offset;
        Execute();

        // a load or restart in the script cleared the nesting: no caller is left to go back to
        if (_nested.Count <= level)
        {
            CurrentSlot = -1;
            return;
        }

        var nest = _nested[level];
        _nested.RemoveAt(level);
        if (nest.Number != 0)
        {
            var slot = Slots[nest.Slot];
            if (slot.Number == nest.Number && slot.Kind == nest.Kind && !slot.IsDead && (slot.Status & SlotStatus.Frozen) == 0)
            {
                CurrentSlot = nest.Slot;
                return;
            }
        }

        CurrentSlot = -1;
    }

    /// <summary>Mirrors <c>executeScript</c>: run instructions until the script yields or stops.</summary>
    private void Execute()
    {
        int budget = InstructionBudget;
        while (CurrentSlot >= 0)
        {
            var slot = Slots[CurrentSlot];
            if (--budget < 0)
            {
                throw new InvalidOperationException($"Script {slot.Number} runs {InstructionBudget} instructions without yielding.");
            }

            var instruction = _decoder.Decode(slot.Code.Span, slot.Offset);
            slot.Offset = instruction.End;
            CurrentInstruction = instruction;
            Run(slot, instruction);
            CurrentInstruction = null;
        }
    }

    private void Run(ScriptSlot slot, Instruction instruction)
    {
        var operands = instruction.Operands;
        int count = operands.Count;
        var args = count <= _arguments.Length ? _arguments : new int[count];
        for (int i = 0; i < count; i++)
        {
            args[i] = operands[i].Kind switch
            {
                OperandKind.Variable => ReadVar(operands[i].Value),
                OperandKind.Constant => operands[i].Value,

                // the sentence's object, without its type (getActiveObject's OBJECT_V0_ID)
                OperandKind.CommandObject => CommandObject & 0xFF,
                _ => 0,
            };
        }

        int Arg(int i) => args[i];

        // jumpRelative: carry on when the condition holds, otherwise jump
        void Jump(bool condition)
        {
            if (!condition)
            {
                slot.Offset = instruction.JumpTarget!.Value;
            }
        }

        switch (instruction.Handler)
        {
            case "o5_stopObjectCode":
                StopObjectCode();
                break;
            case "o5_breakHere":
                BreakHere();
                break;
            case "o5_jumpRelative":
                Jump(false);
                break;

            case "o5_isEqual":
                Jump((short)Arg(1) == (short)Arg(0));
                break;
            case "o5_isNotEqual":
                Jump((short)Arg(1) != (short)Arg(0));
                break;
            case "o2_isGreater":
                Jump((ushort)Arg(1) > (ushort)Arg(0));
                break;
            case "o2_isGreaterEqual":
                Jump((ushort)Arg(1) >= (ushort)Arg(0));
                break;
            case "o2_isLess":
                Jump((ushort)Arg(1) < (ushort)Arg(0));
                break;
            case "o2_isLessEqual":
                Jump((ushort)Arg(1) <= (ushort)Arg(0));
                break;
            case "o5_equalZero":
                Jump(Arg(0) == 0);
                break;
            case "o5_notEqualZero":
                Jump(Arg(0) != 0);
                break;

            case "o5_move" or "o2_assignVarByte":
                WriteVar(instruction.Result!.Value, Arg(0));
                break;
            case "o2_assignVarWordIndirect":
                WriteVar(Variables[instruction.Result!.Value], Arg(0));
                break;
            case "o2_add":
                Variables[CheckVariable(instruction.Result!.Value)] += Arg(0);
                break;
            case "o2_subtract":
                Variables[CheckVariable(instruction.Result!.Value)] -= Arg(0);
                break;
            case "o2_addIndirect":
                Variables[CheckVariable(Variables[instruction.Result!.Value])] += Arg(0);
                break;
            case "o2_subIndirect":
                Variables[CheckVariable(Variables[instruction.Result!.Value])] -= Arg(0);
                break;
            case "o5_increment":
                WriteVar(instruction.Result!.Value, ReadVar(instruction.Result.Value) + 1);
                break;
            case "o5_decrement":
                WriteVar(instruction.Result!.Value, ReadVar(instruction.Result.Value) - 1);
                break;
            case "o5_setVarRange":
                for (int i = 0; i < Arg(0); i++)
                {
                    WriteVar(instruction.Result!.Value + i, Arg(1 + i));
                }

                break;
            case "o5_getRandomNr":
                WriteVar(instruction.Result!.Value, _random.Next(Arg(0) + 1));
                break;

            // bit variables live in the same array, 16 to a variable (o2_setBitVar, o2_getBitVar)
            case "o2_setBitVar":
                {
                    int bit = Arg(0) + (byte)Arg(1);
                    int variable = CheckVariable(bit >> 4);
                    Variables[variable] = Arg(2) != 0
                        ? Variables[variable] | (1 << (bit & 0x0F))
                        : Variables[variable] & ~(1 << (bit & 0x0F));
                }

                break;
            case "o2_getBitVar":
                {
                    int bit = Arg(0) + (byte)Arg(1);
                    WriteVar(instruction.Result!.Value, (Variables[CheckVariable(bit >> 4)] & (1 << (bit & 0x0F))) != 0 ? 1 : 0);
                }

                break;

            case "o2_delay":
                // the script holds 0xFFFFFF less the delay in ticks
                slot.Delay = 0xFFFFFF - Arg(0);
                slot.Status = SlotStatus.Paused;
                BreakHere();
                break;
            case "o5_delayVariable":
                slot.Delay = Arg(0);
                slot.Status = SlotStatus.Paused;
                BreakHere();
                break;

            case "o2_startScript":
                RunScript(Arg(0));
                break;
            case "o2_chainScript":
                StopScript(slot.Number);
                CurrentSlot = -1;
                RunScript(Arg(0));
                break;
            case "o2_stopScript":
                StopScriptCommon(Arg(0));
                break;
            case "o5_isScriptRunning":
                WriteVar(instruction.Result!.Value, IsScriptRunning(Arg(0)) ? 1 : 0);
                break;

            // o2_cutscene: save the interface, hide it and the cursor, freeze the other scripts and stop the sentence
            case "o2_cutscene":
                BeginCutscene();
                _host.Execute(this, instruction, args);
                break;

            // o2_endCutscene: put the interface back; the saved state never holds the freeze bit, so scripts thaw
            case "o2_endCutscene":
                EndCutscene();
                _host.Execute(this, instruction, args);
                break;

            // o2_cursorCommand: the low byte is the cursor state, the high byte a user state
            case "o2_cursorCommand":
                {
                    int command = (ushort)Arg(0);
                    if ((command & 0xFF) != 0)
                    {
                        Variables[VarCursorState] = command & 0xFF;
                    }

                    SetUserState(command >> 8);
                    _host.Execute(this, instruction, args);
                }

                break;
            case "o2_beginOverride":
                // the override skips to the jump that follows (o2_beginOverride)
                Override = (CurrentSlot, instruction.Offset + 1);
                break;

            case "o2_setStateIntrinsicOn":
                SetState(Arg(0), StateIntrinsic, true, instruction, args);
                break;
            case "o2_setStateIntrinsicOff":
                SetState(Arg(0), StateIntrinsic, false, instruction, args);
                break;
            case "o2_setStateLocked":
                SetState(Arg(0), StateLocked, true, instruction, args);
                break;
            case "o2_setStateUnlocked":
                SetState(Arg(0), StateLocked, false, instruction, args);
                break;
            case "o2_setStateUntouchable":
                SetState(Arg(0), StateUntouchable, true, instruction, args);
                break;
            case "o2_setStateTouchable":
                SetState(Arg(0), StateUntouchable, false, instruction, args);
                break;
            case "o2_setStatePickupable":
                SetState(Arg(0), StatePickupable, true, instruction, args);
                break;
            case "o2_setStateUnpickupable":
                SetState(Arg(0), StatePickupable, false, instruction, args);
                break;

            // ifState...: the names say when to jump (ifStateZeroCommon, ifStateNotZeroCommon)
            case "o2_ifStateIntrinsicOn":
                Jump((GetState(Arg(0)) & StateIntrinsic) == 0);
                break;
            case "o2_ifStateIntrinsicOff":
                Jump((GetState(Arg(0)) & StateIntrinsic) != 0);
                break;
            case "o2_ifStateLocked":
                Jump((GetState(Arg(0)) & StateLocked) == 0);
                break;
            case "o2_ifStateUnlocked":
                Jump((GetState(Arg(0)) & StateLocked) != 0);
                break;
            case "o2_ifStateUntouchable":
                Jump((GetState(Arg(0)) & StateUntouchable) == 0);
                break;
            case "o2_ifStateTouchable":
                Jump((GetState(Arg(0)) & StateUntouchable) != 0);
                break;
            case "o2_ifStatePickupable":
                Jump((GetState(Arg(0)) & StatePickupable) == 0);
                break;
            case "o2_ifStateUnpickupable":
                Jump((GetState(Arg(0)) & StatePickupable) != 0);
                break;

            case "o5_getObjectOwner":
                WriteVar(instruction.Result!.Value, GetOwner(Arg(0)));
                break;

            // version 0: 0 is the sentence's object (o_getObjectOwner)
            case "o_getObjectOwner":
                WriteVar(instruction.Result!.Value, GetOwner(Arg(0) != 0 ? Arg(0) : CommandObject & 0xFF));
                break;

            // version 0: the running script stops (o_stopCurrentScript, stopScriptCommon(0))
            case "o_stopCurrentScript":
                StopScriptCommon(0);
                break;

            // version 0's bit variables: a byte and a bit in it (o_setBitVar, o_getBitVar)
            case "o_setBitVar":
                BitVariables[(byte)Arg(0)] = Arg(2) != 0
                    ? (byte)(BitVariables[(byte)Arg(0)] | (1 << (Arg(1) & 7)))
                    : (byte)(BitVariables[(byte)Arg(0)] & ~(1 << (Arg(1) & 7)));
                break;
            case "o_getBitVar":
                WriteVar(instruction.Result!.Value, (BitVariables[(byte)Arg(0)] & (1 << (Arg(1) & 7))) != 0 ? 1 : 0);
                break;

            // version 0: whether the sentence's second object is the one given; without bit 0x80 it
            // must also be an object with an owner and state (ifEqualActiveObject2Common)
            case "o_ifEqualActiveObject2" or "o_ifNotEqualActiveObject2":
                {
                    bool checkType = (instruction.Opcode & 0x80) == 0;
                    bool equal = (!checkType || (CommandObject2 >> 8) == 0) && Arg(0) == (CommandObject2 & 0xFF);
                    Jump(instruction.Handler == "o_ifEqualActiveObject2" ? equal : !equal);
                }

                break;

            // version 0: loading and locking resources, and the instructions that do nothing here
            case "o_loadSound" or "o_lockSound" or "o_unlockSound" or "o_loadCostume" or "o_lockCostume" or "o_unlockCostume"
                or "o_loadScript" or "o_lockScript" or "o_unlockScript" or "o_loadRoom" or "o_lockRoom" or "o_unlockRoom"
                or "o_nop" or "o_screenPrepare":
                break;
            case "o2_setOwnerOf":
                ObjectOwners[Arg(0)] = (byte)Arg(1);
                _host.Execute(this, instruction, args);
                break;

            default:
                {
                    int value = _host.Execute(this, instruction, args);
                    if (instruction.JumpTarget is not null)
                    {
                        Jump(value != 0);
                    }
                    else if (instruction.Result is { } result)
                    {
                        WriteVar(instruction.ResultIndirect ? Variables[result] : result, value);
                    }
                }

                break;
        }
    }

    private void SetState(int obj, byte bit, bool on, Instruction instruction, int[] args)
    {
        PutState(obj, on ? GetState(obj) | bit : GetState(obj) & ~bit);
        _host.Execute(this, instruction, args);
    }

    /// <summary>Mirrors <c>stopObjectCode</c> for <c>version &lt;= 2</c>.</summary>
    private void StopObjectCode()
    {
        var slot = Slots[CurrentSlot];
        if (slot.Kind == ScriptKind.Global)
        {
            StopScript(slot.Number);
        }
        else
        {
            slot.Kill();
        }

        CurrentSlot = -1;
    }

    /// <summary>Mirrors <c>ScummEngine_v2::stopScriptCommon</c> without its game-specific workarounds: 0 means the running script.</summary>
    private void StopScriptCommon(int number)
    {
        if (number == 0)
        {
            number = Slots[CurrentSlot].Number;
        }

        if (CurrentSlot != 0 && Slots[CurrentSlot].Number == number)
        {
            StopObjectCode();
        }
        else
        {
            StopScript(number);
        }
    }

    private static int CheckVariable(int number)
    {
        if (number is < 0 or >= VariableCount)
        {
            throw new InvalidOperationException($"Variable {number} is out of range (0 to {VariableCount - 1}).");
        }

        return number;
    }
}
