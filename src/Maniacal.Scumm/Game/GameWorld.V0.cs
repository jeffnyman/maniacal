using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Game;

/// <summary>
/// What version 0, the C64, does differently: its own instructions (<c>ScummEngine_v0</c>'s
/// <c>o_</c> handlers) and its modes in place of the user state of versions 1 and 2.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>Version 0's modes (<c>CurrentMode</c>): what the interface shows and whether the scripts freeze.</summary>
    public const int ModeCutscene = 0, ModeKeypad = 1, ModeNoNewKid = 2, ModeNormal = 3;

    // version 0 prints nobody's text in this colour (decodeParseString)
    private const int V0NobodyColor = 14;

    /// <summary>Version 0's mode (<c>_currentMode</c>).</summary>
    public int CurrentMode { get; private set; }

    // the cutscene's mode and room, for its end (vm.cutSceneData[0] and [2])
    private (int Mode, int Room) _beforeV0Cutscene;

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
                _sentences.Clear();
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

            default:
                return false;
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
    /// Version 0's sentences go (<c>_sentenceNum = 0</c> in <c>ScummEngine_v0::resetSentence</c>);
    /// the verb and objects chosen go back with version 0's verbs.
    /// </summary>
    private void ResetSentenceV0() => _sentences.Clear();
}
