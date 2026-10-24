using System.Buffers.Binary;

namespace Maniacal.Scumm.Scripts;

/// <summary>
/// Decodes script instructions: which handler an opcode runs, and the operands it fetches. The
/// operand layouts mirror the fetches of ScummVM's handlers in <c>script_v2.cpp</c>, the
/// <c>o5_</c>, <c>o4_</c> and <c>o3_</c> handlers the version 2 table borrows, and for version 0
/// (the C64) <c>script_v0.cpp</c> with the version 2 handlers its table borrows.
/// </summary>
/// <remarks>
/// <para>Operand kinds (<c>ScummEngine_v2::getVar</c>, <c>getResultPos</c>, and
/// <c>ScummEngine_v5::getVarOrDirectByte</c>, <c>getVarOrDirectWord</c>, <c>jumpRelative</c>):
/// a variable is a byte; a "variable or byte" or "variable or word" is a variable byte when the
/// opcode has the operand's bit (0x80, 0x40, 0x20 for the first three), otherwise a byte or a
/// signed word; a result is a variable byte; a jump is a signed word added to the position after it.</para>
/// <para>Two operand layouts depend on values: <c>doSentence</c> stops after its first operand when
/// it is the constant 0xFC or 0xFB (when it is a variable, the full layout is assumed), and
/// <c>actorOps</c> sub-operation 2 has an extra byte in version 2 only.</para>
/// <para>Version 0 reads every word but a jump's as a byte (<c>ScummEngine_v0::fetchScriptWord</c>
/// and <c>getVarOrDirectWord</c>), and takes the object of the state instructions as a byte, or
/// with bit 0x40 as no operand at all: the object the running sentence is about
/// (<c>ScummEngine_v0::getActiveObject</c>). Its text is bytes up to a zero, without escapes.</para>
/// </remarks>
public sealed class ScriptDecoder(int version)
{
    private const byte Param1 = 0x80, Param2 = 0x40, Param3 = 0x20;

    public int Version { get; } = version;

    private readonly string[] _handlers = version == 0 ? OpcodeTable.HandlersV0 : OpcodeTable.Handlers;

    /// <summary>The ScummVM handler an opcode runs in this version.</summary>
    public string HandlerOf(byte opcode) => _handlers[opcode];

    public Instruction Decode(ReadOnlySpan<byte> code, int offset)
    {
        var r = new Reader(code, offset);
        byte opcode = r.Byte();
        string handler = _handlers[opcode];
        bool v0 = Version == 0;
        var operands = new List<Operand>();
        int? result = null;
        bool indirect = false;
        int? jump = null;

        void VarOrByte(ref Reader reader, byte mask) => operands.Add((opcode & mask) != 0 ? Operand.Variable(reader.Byte()) : Operand.Constant(reader.Byte()));
        void VarOrWord(ref Reader reader, byte mask) => operands.Add((opcode & mask) != 0 ? Operand.Variable(reader.Byte()) : Operand.Constant(v0 ? reader.Byte() : reader.SignedWord()));
        void ActiveObject(ref Reader reader)
        {
            if (!v0)
            {
                VarOrWord(ref reader, Param1);
            }
            else
            {
                operands.Add((opcode & Param2) != 0 ? Operand.CommandObject : Operand.Constant(reader.Byte()));
            }
        }
        void Var(ref Reader reader) => operands.Add(Operand.Variable(reader.Byte()));
        void Byte(ref Reader reader) => operands.Add(Operand.Constant(reader.Byte()));
        void Word(ref Reader reader) => operands.Add(Operand.Constant(reader.Word()));
        void Result(ref Reader reader) => result = reader.Byte();
        void Jump(ref Reader reader)
        {
            int delta = reader.SignedWord();
            jump = reader.Position + delta;
        }

        switch (handler)
        {
            // no operands
            case "o5_stopObjectCode" or "o5_breakHere" or "o5_stopMusic" or "o2_cutscene" or "o2_endCutscene"
                or "o2_restart" or "o2_drawSentence" or "o2_waitForMessage" or "o2_waitForSentence" or "o2_dummy"
                or "o2_switchCostumeSet" or "o_stopCurrentScript" or "o_nop" or "o_cutscene" or "o_endCutscene"
                or "o_screenPrepare":
                break;

            // the active object (getActiveObject)
            case "o2_setStateIntrinsicOn" or "o2_setStateIntrinsicOff" or "o2_setStateLocked" or "o2_setStateUnlocked"
                or "o2_setStateTouchable" or "o2_setStateUntouchable" or "o2_setStatePickupable" or "o2_setStateUnpickupable":
                ActiveObject(ref r);
                break;

            case "o2_pickupObject" or "o2_cursorCommand":
                VarOrWord(ref r, Param1);
                break;

            case "o2_ifStateIntrinsicOn" or "o2_ifStateIntrinsicOff" or "o2_ifStateLocked" or "o2_ifStateUnlocked"
                or "o2_ifStateTouchable" or "o2_ifStateUntouchable" or "o2_ifStatePickupable" or "o2_ifStateUnpickupable":
                ActiveObject(ref r);
                Jump(ref r);
                break;

            // version 0: one byte (the resource instructions, setMode, pickupObject)
            case "o_loadSound" or "o_lockSound" or "o_unlockSound" or "o_lockCostume" or "o_unlockCostume"
                or "o_lockScript" or "o_unlockScript" or "o_lockRoom" or "o_unlockRoom" or "o_setMode" or "o_pickupObject":
                Byte(ref r);
                break;

            // version 0: one variable or byte
            case "o_loadCostume" or "o_loadScript" or "o_loadRoom" or "o_lights":
                VarOrByte(ref r, Param1);
                break;

            // version 0: an actor, then an object number whose type the opcode's bit 0x40 gives
            case "o_walkActorToObject" or "o_putActorAtObject":
                VarOrByte(ref r, Param1);
                Byte(ref r);
                break;

            case "o_loadRoomWithEgo":
                Byte(ref r);
                Byte(ref r);
                break;

            case "o_animateActor":
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                operands.Add(Operand.Constant((sbyte)r.Byte()));
                break;

            case "o_getActorMoving" or "o_getClosestActor":
                Result(ref r);
                VarOrByte(ref r, Param1);
                break;

            case "o_setObjectName":
                Byte(ref r);
                operands.Add(r.ResourceString());
                break;

            case "o_setActorBitVar" or "o_setBitVar":
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                VarOrByte(ref r, Param3);
                break;

            case "o_getActorBitVar" or "o_getBitVar":
                Result(ref r);
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                break;

            case "o_getObjectOwner":
                Result(ref r);
                VarOrWord(ref r, Param1);
                break;

            case "o_setOwnerOf":
                VarOrWord(ref r, Param1);
                VarOrByte(ref r, Param2);
                break;

            case "o_print":
                Byte(ref r);
                operands.Add(r.PlainText());
                break;

            case "o_printEgo":
                operands.Add(r.PlainText());
                break;

            // the verb and two objects: 0xFE and 0xFF the sentence's, else numbers whose types the opcode's bits 0x80 and 0x40 give
            case "o_doSentence":
                Byte(ref r);
                Byte(ref r);
                Byte(ref r);
                break;

            case "o_ifEqualActiveObject2" or "o_ifNotEqualActiveObject2":
                Byte(ref r);
                Jump(ref r);
                break;

            // one variable or byte
            case "o5_startMusic" or "o5_startSound" or "o5_stopSound" or "o5_loadRoom" or "o5_actorFollowCamera"
                or "o2_waitForActor" or "o2_startScript" or "o2_stopScript" or "o2_chainScript" or "o2_panCameraTo"
                or "o2_setCameraAt":
                VarOrByte(ref r, Param1);
                break;

            // result, then one variable or byte
            case "o5_getActorRoom" or "o5_getRandomNr" or "o5_getActorMoving" or "o5_isScriptRunning" or "o5_getActorCostume"
                or "o5_isSoundRunning" or "o5_getActorFacing" or "o4_saveLoadGame" or "o2_getActorElevation"
                or "o2_getActorX" or "o2_getActorY" or "o2_getActorWalkBox":
                Result(ref r);
                VarOrByte(ref r, Param1);
                break;

            // result, then one variable or word
            case "o5_getObjectOwner" or "o5_move" or "o5_getClosestObjActor" or "o2_getObjPreposition" or "o2_add" or "o2_subtract":
                Result(ref r);
                VarOrWord(ref r, Param1);
                break;

            case "o2_addIndirect" or "o2_subIndirect" or "o2_assignVarWordIndirect":
                Result(ref r);
                indirect = true;
                VarOrWord(ref r, Param1);
                break;

            case "o5_increment" or "o5_decrement":
                Result(ref r);
                break;

            case "o2_assignVarByte":
                Result(ref r);
                Byte(ref r);
                break;

            // comparisons: a variable, a variable or word, a jump
            case "o5_isEqual" or "o5_isNotEqual" or "o2_isGreater" or "o2_isGreaterEqual" or "o2_isLess" or "o2_isLessEqual":
                Var(ref r);
                VarOrWord(ref r, Param1);
                Jump(ref r);
                break;

            case "o5_equalZero" or "o5_notEqualZero":
                Var(ref r);
                Jump(ref r);
                break;

            case "o5_jumpRelative":
                Jump(ref r);
                break;

            case "o5_delayVariable":
                Var(ref r);
                break;

            case "o2_delay":
                operands.Add(Operand.Constant(r.Byte() | (r.Byte() << 8) | (r.Byte() << 16)));
                break;

            case "o2_putActor" or "o2_walkActorTo":
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                VarOrByte(ref r, Param3);
                break;

            case "o2_putActorAtObject" or "o2_walkActorToObject" or "o5_faceActor":
                VarOrByte(ref r, Param1);
                VarOrWord(ref r, Param2);
                break;

            case "o2_putActorInRoom" or "o5_animateActor":
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                break;

            case "o2_setActorElevation":
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                break;

            case "o5_walkActorToActor":
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                Byte(ref r);
                break;

            case "o2_actorFromPos" or "o2_findObject":
                Result(ref r);
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                break;

            case "o5_getDist":
                Result(ref r);
                VarOrWord(ref r, Param1);
                VarOrWord(ref r, Param2);
                break;

            case "o2_setOwnerOf":
                VarOrWord(ref r, Param1);
                VarOrByte(ref r, Param2);
                break;

            case "o2_setObjPreposition":
                VarOrWord(ref r, Param1);
                Byte(ref r);
                break;

            case "o2_ifClassOfIs":
                VarOrWord(ref r, Param1);
                VarOrByte(ref r, Param2);
                Jump(ref r);
                break;

            case "o2_drawObject":
                VarOrWord(ref r, Param1);
                VarOrByte(ref r, Param2);
                VarOrByte(ref r, Param3);
                break;

            case "o2_loadRoomWithEgo":
                VarOrWord(ref r, Param1);
                VarOrByte(ref r, Param2);
                operands.Add(Operand.Constant((sbyte)r.Byte()));
                operands.Add(Operand.Constant((sbyte)r.Byte()));
                break;

            case "o2_setBitVar":
                Word(ref r);
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                break;

            case "o2_getBitVar":
                Result(ref r);
                Word(ref r);
                VarOrByte(ref r, Param1);
                break;

            case "o2_lights":
                VarOrByte(ref r, Param1);
                Byte(ref r);
                Byte(ref r);
                break;

            case "o2_resourceRoutines":
                VarOrByte(ref r, Param1);
                Byte(ref r);
                break;

            case "o2_roomOps":
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                Byte(ref r);
                break;

            case "o3_setBoxFlags":
                VarOrByte(ref r, Param1);
                Byte(ref r);
                break;

            case "o2_beginOverride":
                Byte(ref r);
                Word(ref r);
                break;

            case "o5_setVarRange":
                Result(ref r);
                {
                    int count = r.Byte();
                    operands.Add(Operand.Constant(count));
                    do
                    {
                        operands.Add(Operand.Constant((opcode & 0x80) != 0 ? r.SignedWord() : r.Byte()));
                    }
                    while (--count > 0);
                }

                break;

            case "o5_pseudoRoom":
                Byte(ref r);
                for (byte b = r.Byte(); b != 0; b = r.Byte())
                {
                    operands.Add(Operand.Constant(b));
                }

                break;

            case "o5_print":
                VarOrByte(ref r, Param1);
                operands.Add(v0 ? r.PlainText() : r.Message());
                break;

            case "o5_printEgo":
                operands.Add(r.Message());
                break;

            case "o5_setObjectName":
                VarOrWord(ref r, Param1);
                operands.Add(r.ResourceString());
                break;

            case "o2_actorOps":
                VarOrByte(ref r, Param1);
                VarOrByte(ref r, Param2);
                {
                    byte sub = r.Byte();
                    operands.Add(Operand.Constant(sub));
                    switch (sub)
                    {
                        case 1 or 4 or 5:
                            break;
                        case 2:
                            if (Version != 1)
                            {
                                Byte(ref r);
                            }

                            break;
                        case 3:
                            operands.Add(r.ResourceString());
                            break;
                        default:
                            throw new InvalidDataException($"actorOps sub-operation {sub} at {offset:X4} is not one ScummVM knows.");
                    }
                }

                break;

            case "o2_verbOps":
                {
                    byte verb = r.Byte();
                    operands.Add(Operand.Constant(verb));
                    if (verb == 0)
                    {
                        VarOrByte(ref r, Param1);
                    }
                    else if (verb == 0xFF)
                    {
                        Byte(ref r);
                        Byte(ref r);
                    }
                    else
                    {
                        Byte(ref r);
                        Byte(ref r);
                        VarOrByte(ref r, Param1);
                        Byte(ref r);
                        operands.Add(r.ResourceString());
                    }
                }

                break;

            case "o2_doSentence":
                VarOrByte(ref r, Param1);
                if (operands[0] is { Kind: OperandKind.Constant, Value: 0xFC or 0xFB })
                {
                    break;
                }

                VarOrWord(ref r, Param2);
                VarOrWord(ref r, Param3);
                Byte(ref r);
                break;

            default:
                throw new InvalidOperationException($"No operand layout for {handler}.");
        }

        return new Instruction(offset, opcode, handler, operands, r.Position - offset, result, indirect, jump);
    }

    /// <summary>Decodes instructions one after another from <paramref name="start"/> to the end of the code.</summary>
    public IEnumerable<Instruction> DecodeAll(ReadOnlyMemory<byte> code, int start = 0)
    {
        for (int offset = start; offset < code.Length;)
        {
            var instruction = Decode(code.Span, offset);
            yield return instruction;
            offset = instruction.End;
        }
    }

    /// <summary>
    /// Decodes the instructions execution can reach from the entry points: each instruction falls
    /// through to the next unless it ends the flow, and conditions and jumps also lead to their
    /// targets. Bytes no path reaches are left out; some scripts end with leftover bytes after their
    /// last instruction. Throws <see cref="InvalidDataException"/> when a reachable instruction does
    /// not decode, or a jump leaves the code.
    /// </summary>
    public SortedDictionary<int, Instruction> DecodeReachable(ReadOnlyMemory<byte> code, IEnumerable<int> entries)
    {
        var decoded = new SortedDictionary<int, Instruction>();
        var pending = new Stack<int>(entries);
        while (pending.Count > 0)
        {
            int offset = pending.Pop();
            if (offset == code.Length || decoded.ContainsKey(offset))
            {
                continue;
            }

            if (offset < 0 || offset > code.Length)
            {
                throw new InvalidDataException($"Execution reaches {offset:X4}, outside the code (0000 to {code.Length:X4}).");
            }

            var instruction = Decode(code.Span, offset);
            decoded[offset] = instruction;
            if (!instruction.EndsFlow)
            {
                pending.Push(instruction.End);
            }

            if (instruction.JumpTarget is { } target)
            {
                pending.Push(target);
            }
        }

        return decoded;
    }

    private ref struct Reader(ReadOnlySpan<byte> code, int position)
    {
        private readonly ReadOnlySpan<byte> _code = code;

        public int Position { get; private set; } = position;

        public byte Byte()
        {
            if (Position >= _code.Length)
            {
                throw new InvalidDataException($"An instruction runs past the end of the script, at {Position:X4}.");
            }

            return _code[Position++];
        }

        public int Word() => Byte() | (Byte() << 8);

        public int SignedWord() => (short)Word();

        /// <summary>
        /// Message text, up to a zero byte. Mirrors <c>ScummEngine_v2::decodeParseString</c>: without
        /// bit 7 (a following space), bytes 0 to 7 are escapes, and 4 to 7 take one more byte.
        /// </summary>
        public Operand Message()
        {
            int start = Position;
            for (byte c = Byte(); c != 0; c = Byte())
            {
                if ((c & 0x7F) is > 3 and < 8)
                {
                    Byte();
                }
            }

            return new Operand(OperandKind.Text, Position - start - 1, _code[start..(Position - 1)].ToArray());
        }

        /// <summary>
        /// Version 0 text, up to a zero byte: characters, bit 7 for a space after one. Mirrors
        /// <c>ScummEngine_v0::decodeParseString</c>.
        /// </summary>
        public Operand PlainText()
        {
            int start = Position;
            while (Byte() != 0)
            {
            }

            return new Operand(OperandKind.Text, Position - start - 1, _code[start..(Position - 1)].ToArray());
        }

        /// <summary>
        /// A name, up to a zero byte. Mirrors <c>ScummEngine::resStrLen</c>: 0xFF takes the next
        /// byte, and two more unless it is 1, 2, 3 or 8.
        /// </summary>
        public Operand ResourceString()
        {
            int start = Position;
            for (byte c = Byte(); c != 0; c = Byte())
            {
                if (c == 0xFF && Byte() is not (1 or 2 or 3 or 8))
                {
                    Byte();
                    Byte();
                }
            }

            return new Operand(OperandKind.Text, Position - start - 1, _code[start..(Position - 1)].ToArray());
        }
    }
}
