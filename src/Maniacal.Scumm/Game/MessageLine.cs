using System.Globalization;
using System.Text;
using Daventry.Core.Graphics;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Scripts;

namespace Maniacal.Scumm.Game;

/// <summary>
/// The message lines above the room, where actors' speech and the game's text appear. Mirrors
/// ScummVM's text of versions 1 and 2: <c>ScummEngine_v2::decodeParseString</c>,
/// <c>convertMessageToString</c>, <c>actorTalk</c>, <c>displayDialog</c>,
/// <c>handleNextCharsetCode</c>, <c>newLine</c>, <c>stopTalk</c> and <c>restoreCharsetBg</c>.
/// </summary>
/// <remarks>
/// A message shows all at once, from the top left, wrapping at the right edge onto the next 8-line
/// row, and stays for 60 ticks plus <see cref="TextSpeed"/> a character. Escape 3 waits there and
/// shows the rest in a fresh line; escape 2 keeps the text so the next message carries on after it.
/// </remarks>
public sealed class MessageLine
{
    /// <summary>The text area: the 16 lines above the room (<c>initScreens(16, ...)</c>).</summary>
    public const int Width = 320, Height = 16;

    /// <summary>No actor talking. Mirrors <c>setTalkingActor(0xFF)</c>.</summary>
    public const int NoActor = 0xFF;

    private const int Escape = 0xFF, NewLine = 1, Keep = 2, Wait = 3;

    // the engine's character count (VAR_CHARCOUNT of ScummEngine_v2::setupScummVars)

    private readonly Font _font;
    private readonly ScriptEngine _engine;
    private readonly Func<int, Actor?> _actorOf;
    private readonly Func<int, string> _nameOf;
    private readonly Func<int, string> _verbNameOf;
    private byte[] _buffer = [0];
    private int _position;
    private int _nextLeft;
    private int _nextTop;
    private int _messageCount;
    private bool _keepText;
    private bool _useTalkAnims;
    private int _color;
    private int _talkingActor;

    public MessageLine(Font font, ScriptEngine engine, Func<int, Actor?> actorOf, Func<int, string> nameOf, Func<int, string> verbNameOf)
    {
        _font = font;
        _engine = engine;
        _actorOf = actorOf;
        _nameOf = nameOf;
        _verbNameOf = verbNameOf;
    }

    /// <summary>The text area as it is drawn now.</summary>
    public Surface8 Text { get; } = new(Width, Height);

    /// <summary>0 for none, 0xFF while a message (or part of one) is to show, 1 once it has all shown. Mirrors <c>_haveMsg</c>.</summary>
    public int HaveMessage { get; private set; }

    /// <summary>
    /// The actor whose speech shows, <see cref="NoActor"/> when nobody's does, or 0 before anyone
    /// has spoken. Mirrors <c>getTalkingActor</c> and <c>setTalkingActor</c>: version 2 keeps it in
    /// <c>VAR_TALK_ACTOR</c>, variable 41.
    /// </summary>
    public int TalkingActor
    {
        get => _talkingActor;
        private set
        {
            _talkingActor = value;
            if (_engine.Version >= 2)
            {
                _engine.Variables[_engine.Vars.TalkActor] = value;
            }
        }
    }

    /// <summary>Ticks the message still shows. Mirrors <c>_talkDelay</c>.</summary>
    public int TalkDelay { get; private set; }

    /// <summary>Ticks each character adds. Mirrors <c>_defaultTextSpeed</c>, which <c>resetScumm</c> sets to 6.</summary>
    public int TextSpeed { get; set; } = 6;

    /// <summary>The colour of text not said by an actor. Mirrors <c>_string[0].color</c>.</summary>
    public int StringColor { get; set; } = 15;

    /// <summary>
    /// Turns the text of a print instruction into a message. Mirrors
    /// <c>ScummEngine_v2::decodeParseString</c>: a byte with bit 7 has a space after it; bytes 0
    /// to 7 are escapes, written as 0xFF and the code, with the next byte and a 0 after codes 4 to 7.
    /// </summary>
    public static byte[] Decode(ReadOnlySpan<byte> text)
    {
        var output = new List<byte>(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            byte c = text[i];
            bool space = (c & 0x80) != 0;
            c &= 0x7F;
            if (c < 8)
            {
                output.Add(Escape);
                output.Add(c);
                if (c > 3 && i + 1 < text.Length)
                {
                    output.Add(text[++i]);
                    output.Add(0);
                }
            }
            else
            {
                output.Add(c);
            }

            if (space)
            {
                output.Add((byte)' ');
            }
        }

        return [.. output];
    }

    /// <summary>
    /// Turns the text of a version 0 print instruction into a message. Mirrors
    /// <c>ScummEngine_v0::decodeParseString</c>: a byte with bit 7 has a space after it, and a
    /// slash is a new line; there are no escapes.
    /// </summary>
    public static byte[] DecodeV0(ReadOnlySpan<byte> text)
    {
        var output = new List<byte>(text.Length + 8);
        foreach (byte b in text)
        {
            byte c = (byte)(b & 0x7F);
            output.Add(c == '/' ? (byte)13 : c);
            if ((b & 0x80) != 0)
            {
                output.Add((byte)' ');
            }
        }

        return [.. output];
    }

    /// <summary>
    /// Shows a message as said by an actor (or by nobody, <see cref="NoActor"/>). An actor outside
    /// the current room says nothing. Mirrors <c>actorTalk</c>.
    /// </summary>
    public void Talk(int actor, ReadOnlySpan<byte> message)
    {
        _buffer = Expand(message);

        if (actor == NoActor)
        {
            if (!_keepText)
            {
                Stop();
            }

            TalkingActor = NoActor;
        }
        else
        {
            var speaker = _actorOf(actor);
            if (speaker is null || !speaker.InCurrentRoom)
            {
                return;
            }

            if (!_keepText)
            {
                Stop();
            }

            TalkingActor = actor;
            TalkAnimation(speaker, CostumeFrameTalkStart);
            _useTalkAnims = true;
        }

        _color = TalkingActor == NoActor ? StringColor : _actorOf(TalkingActor)!.TalkColor;
        _position = 0;
        TalkDelay = 0;
        HaveMessage = 0xFF;
        _engine.Variables[_engine.Vars.HaveMessage] = 0xFF;
        _engine.Variables[_engine.Vars.CharCount] = 0;
        Display();
    }

    /// <summary>
    /// Once a frame: while the message's time runs, nothing; when it has all shown and its time is
    /// up, it goes; otherwise the next part shows. Mirrors <c>displayDialog</c> for versions 1 and 2.
    /// </summary>
    public void Display()
    {
        if (HaveMessage == 0 || TalkDelay != 0)
        {
            return;
        }

        if (HaveMessage == 1)
        {
            Stop();
            return;
        }

        var speaker = TalkingActor == NoActor ? null : _actorOf(TalkingActor);
        if (speaker is not null)
        {
            TalkAnimation(speaker, CostumeFrameTalkStart);
            _useTalkAnims = true;
        }

        TalkDelay = 60;
        if (!_keepText)
        {
            Clear();
            _messageCount = 0;
        }
        else
        {
            TalkDelay += _messageCount * TextSpeed;
        }

        while (NextCode(out int c))
        {
            if (c == 0)
            {
                HaveMessage = 1;
                _keepText = false;
                _messageCount = 0;
                break;
            }

            if (c == 13)
            {
                LineBreak();
                continue;
            }

            if (_nextLeft >= Width)
            {
                LineBreak();
            }

            // CharsetRendererV3::printChar skips a character that would not fit, and a filler (@)
            // takes no room, though it counts towards the message's time
            if (c != '@')
            {
                if (_nextLeft + Font.CharWidth <= Width && c < Font.GlyphCount)
                {
                    _font.DrawGlyph(Text, c, _nextLeft, _nextTop, (byte)_color);
                }

                _nextLeft += Font.CharWidth;
            }

            _messageCount++;
            TalkDelay += TextSpeed;
            _engine.Variables[_engine.Vars.CharCount]++;
        }
    }

    /// <summary>The message's time is up: the period key. Mirrors the <c>talkstopKeyEnabled</c> case of <c>processKeyboard</c>.</summary>
    public void SkipDelay() => TalkDelay = 0;

    /// <summary>Counts the message's time down. Mirrors the <c>_talkDelay</c> lines of <c>scummLoop</c>.</summary>
    public void CountDown(int ticks) => TalkDelay = Math.Max(0, TalkDelay - ticks);

    /// <summary>The message goes and the speaker stops talking. Mirrors <c>stopTalk</c>.</summary>
    public void Stop()
    {
        HaveMessage = 0;
        TalkDelay = 0;
        if (TalkingActor != NoActor && TalkingActor != 0 && _actorOf(TalkingActor) is { } speaker)
        {
            if (speaker.InCurrentRoom && _useTalkAnims)
            {
                TalkAnimation(speaker, CostumeFrameTalkStop);
                _useTalkAnims = false;
            }

            TalkingActor = NoActor;
        }

        _keepText = false;
        Clear();
    }

    /// <summary>
    /// Copies the message into a save as ScummVM keeps it: the expanded text and how far it has
    /// shown (<c>_charsetBuffer</c>, <c>_charsetBufPos</c>), whether one shows, its time, colour and
    /// speaker, and the default text colour (<c>_string[0].color</c>).
    /// </summary>
    internal void SaveTo(Saves.ScummState s)
    {
        Array.Clear(s.CharsetBuffer);
        Array.Copy(_buffer, s.CharsetBuffer, Math.Min(_buffer.Length, s.CharsetBuffer.Length));
        s.CharsetBufPos = _position;
        s.HaveMessage = HaveMessage;
        s.TalkDelay = TalkDelay;
        s.DefaultTextSpeed = TextSpeed;
        s.KeepText = _keepText ? 1 : 0;
        s.UseTalkAnims = _useTalkAnims ? 1 : 0;
        s.CharsetColor = _color;
        s.CharsetRendererColor = _color;
        s.ActorToPrintStrFor = TalkingActor;
        s.Strings[0].Color = StringColor;
    }

    /// <summary>
    /// Takes the message from a save. Like ScummVM after a load, the text area starts clear, so
    /// a message still showing goes on from where it was without what had already shown.
    /// </summary>
    internal void LoadFrom(Saves.ScummState s, int talkingActor)
    {
        int length = Array.IndexOf(s.CharsetBuffer, (byte)0);
        _buffer = [.. s.CharsetBuffer.AsSpan(0, length < 0 ? s.CharsetBuffer.Length : length), 0];
        _position = Math.Min(s.CharsetBufPos, _buffer.Length - 1);
        HaveMessage = s.HaveMessage;
        TalkDelay = s.TalkDelay;
        TextSpeed = s.DefaultTextSpeed;
        _keepText = s.KeepText != 0;
        _useTalkAnims = s.UseTalkAnims != 0;
        _color = s.CharsetColor;
        TalkingActor = talkingActor;
        StringColor = s.Strings[0].Color;
        _messageCount = 0;
        Clear();
    }

    /// <summary>The text of a message so far, for logs and tests: printable characters, and | for line breaks.</summary>
    public string Plain()
    {
        var text = new StringBuilder();
        for (int i = 0; i < _buffer.Length && _buffer[i] != 0; i++)
        {
            if (_buffer[i] == Escape && i + 1 < _buffer.Length)
            {
                if (_buffer[++i] == NewLine)
                {
                    text.Append('|');
                }

                continue;
            }

            text.Append((char)_buffer[i]);
        }

        return text.ToString();
    }

    private const int CostumeFrameTalkStart = Costumes.CostumeRenderer.TalkStartFrame;
    private const int CostumeFrameTalkStop = Costumes.CostumeRenderer.TalkStopFrame;

    /// <summary>Mirrors <c>runActorTalkScript</c> without talk scripts: the frame starts unless it is the actor's frame already.</summary>
    private void TalkAnimation(Actor actor, int frame)
    {
        if (TalkingActor != 0 && actor.InCurrentRoom && actor.Frame != frame)
        {
            actor.StartAnim(frame);
        }
    }

    /// <summary>Mirrors <c>restoreCharsetBg</c>: the text area is black again, and the next character goes top left.</summary>
    private void Clear()
    {
        Text.Fill(0);
        _nextLeft = 0;
        _nextTop = 0;
    }

    /// <summary>Mirrors <c>newLine</c>: back to the left, down a row.</summary>
    private void LineBreak()
    {
        _nextLeft = 0;
        _nextTop += Font.Height;
    }

    /// <summary>
    /// The next character, past the escapes it acts on. Returns false at a keep or wait escape.
    /// Mirrors <c>handleNextCharsetCode</c> for the codes versions 1 and 2 have.
    /// </summary>
    private bool NextCode(out int code)
    {
        while (true)
        {
            int c = _position < _buffer.Length ? _buffer[_position++] : 0;
            if (c != Escape)
            {
                code = c;
                return true;
            }

            c = _position < _buffer.Length ? _buffer[_position++] : 0;
            switch (c)
            {
                case NewLine:
                    _messageCount = Width;
                    code = 13;
                    return true;
                case Keep:
                    HaveMessage = 0;
                    _keepText = true;
                    code = Keep;
                    return false;
                case Wait:
                    HaveMessage = 0xFF;
                    _keepText = false;
                    _messageCount = 0;
                    code = Wait;
                    return false;
                default:
                    // 8 does nothing; the codes with operands were expanded or dropped already
                    continue;
            }
        }
    }

    /// <summary>
    /// Writes the values a message asks for into it: 4 a variable's number, 5 the name of the verb
    /// in a variable, 6 the name of the object or actor in one, 7 text kept in variables from one.
    /// Escapes 1, 2, 3 and 8 stay. Mirrors <c>convertMessageToString</c> and its <c>convert...Message</c>
    /// helpers for versions 1 and 2.
    /// </summary>
    private byte[] Expand(ReadOnlySpan<byte> message)
    {
        var output = new List<byte>(message.Length + 16);
        for (int i = 0; i < message.Length && message[i] != 0; i++)
        {
            byte c = message[i];
            if (c != Escape)
            {
                output.Add(c);
                continue;
            }

            if (i + 1 >= message.Length)
            {
                break;
            }

            c = message[++i];
            if (c is NewLine or Keep or Wait or 8)
            {
                output.Add(Escape);
                output.Add(c);
                continue;
            }

            int value = i + 2 < message.Length ? message[i + 1] | (message[i + 2] << 8) : 0;
            string inserted = c switch
            {
                4 => _engine.ReadVar(value).ToString(CultureInfo.InvariantCulture),
                5 => _verbNameOf(_engine.ReadVar(value)),
                6 => _engine.ReadVar(value) is int number and not 0 ? _nameOf(number) : string.Empty,
                7 => VariableText(value),
                _ => string.Empty,
            };

            if (c is >= 4 and <= 7)
            {
                output.AddRange(Encoding.Latin1.GetBytes(inserted));
                i += 2;
            }
        }

        output.Add(0);
        return [.. output];
    }

    /// <summary>Version 1 and 2 text kept a character a variable, up to a 0, without its @ fillers. Mirrors <c>convertStringMessage</c>.</summary>
    private string VariableText(int variable)
    {
        var text = new StringBuilder();
        for (int v = variable; v < ScriptEngine.VariableCount && _engine.Variables[v] != 0; v++)
        {
            char c = (char)(byte)_engine.Variables[v];
            if (c != '@')
            {
                text.Append(c);
            }
        }

        return text.ToString();
    }
}
