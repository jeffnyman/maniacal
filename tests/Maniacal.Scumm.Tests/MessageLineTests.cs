using System.Text;
using Daventry.Core.Storage;
using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Scripts;

namespace Maniacal.Scumm.Tests;

public class MessageLineTests
{
    private const byte Escape = 0xFF;

    /// <summary>A room with no boxes and no costumes, for actors that only talk.</summary>
    private sealed class Room1 : IActorWorld
    {
        public int CurrentRoom => 1;

        public int Version => 2;

        public int BoxCount => 0;

        public BoxCorners BoxCorners(int box) => default;

        public int BoxFlags(int box) => 0;

        public int NextBox(int from, int to) => -1;

        public bool IsPlayer(int actor) => false;

        public Costume? LoadCostume(int costume) => null;
    }

    private static (MessageLine Line, ScriptEngine Engine, Actor[] Actors) Line()
    {
        var engine = new ScriptEngine(2, _ => null, new RecordingHost(), new byte[600], new byte[600]);
        var world = new Room1();
        var actors = Enumerable.Range(0, 4).Select(n => new Actor(n, world)).ToArray();
        var line = new MessageLine(Font.English(2), engine, n => n is > 0 and < 4 ? actors[n] : null, n => $"thing {n}", _ => "Push");
        return (line, engine, actors);
    }

    private static byte[] Text(string text) => Encoding.Latin1.GetBytes(text);

    private static byte[] Message(params object[] parts) =>
        [.. parts.SelectMany(p => p is string s ? Text(s) : (byte[])p)];

    /// <summary>Whether any pixel of a colour is in a character cell of the text area.</summary>
    private static bool Drawn(MessageLine line, int column, int row, int color)
    {
        for (int y = row * Font.Height; y < (row + 1) * Font.Height; y++)
        {
            if (line.Text.Row(y).Slice(column * Font.CharWidth, Font.CharWidth).Contains((byte)color))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Decoding_spaces_and_escapes()
    {
        // 'i' with bit 7 has a space after it; 3 is a wait; 4 takes a byte, then a 0 is written after it
        byte[] decoded = MessageLine.Decode([(byte)'H', (byte)('i' | 0x80), 3, (byte)'!', 4, 60]);

        Assert.Equal([(byte)'H', (byte)'i', (byte)' ', Escape, 3, (byte)'!', Escape, 4, 60, 0], decoded);
    }

    [Fact]
    public void A_message_shows_for_60_ticks_and_6_a_character()
    {
        var (line, engine, _) = Line();

        line.Talk(MessageLine.NoActor, Message("Hello"));

        Assert.Equal(1, line.HaveMessage);
        Assert.Equal(60 + 5 * 6, line.TalkDelay);
        Assert.Equal(5, engine.Variables[7]);
        Assert.True(Drawn(line, 0, 0, line.StringColor));
        Assert.Equal("Hello", line.Plain());

        line.CountDown(89);
        line.Display();
        Assert.Equal(1, line.HaveMessage);

        line.CountDown(1);
        line.Display();
        Assert.Equal(0, line.HaveMessage);
        Assert.False(Drawn(line, 0, 0, line.StringColor));
    }

    [Fact]
    public void Long_messages_wrap_onto_the_second_line()
    {
        var (line, _, _) = Line();

        line.Talk(MessageLine.NoActor, Message(new string('A', 41)));

        Assert.True(Drawn(line, 39, 0, line.StringColor));
        Assert.True(Drawn(line, 0, 1, line.StringColor));
        Assert.False(Drawn(line, 1, 1, line.StringColor));
    }

    [Fact]
    public void A_new_line_escape_breaks_the_line()
    {
        var (line, _, _) = Line();

        line.Talk(MessageLine.NoActor, Message("A", new[] { Escape, (byte)1 }, "B"));

        Assert.True(Drawn(line, 0, 0, line.StringColor));
        Assert.True(Drawn(line, 0, 1, line.StringColor));
        Assert.False(Drawn(line, 1, 0, line.StringColor));
        Assert.Equal("A|B", line.Plain());
    }

    [Fact]
    public void A_wait_shows_the_rest_once_the_first_part_is_done()
    {
        var (line, _, _) = Line();

        line.Talk(MessageLine.NoActor, Message("Ab", new[] { Escape, (byte)3 }, "Cde"));

        Assert.Equal(0xFF, line.HaveMessage);
        Assert.Equal(60 + 2 * 6, line.TalkDelay);
        Assert.False(Drawn(line, 2, 0, line.StringColor));

        line.CountDown(line.TalkDelay);
        line.Display();

        // the rest shows from the left of a clear line
        Assert.Equal(1, line.HaveMessage);
        Assert.Equal(60 + 3 * 6, line.TalkDelay);
        Assert.True(Drawn(line, 2, 0, line.StringColor));
        Assert.False(Drawn(line, 3, 0, line.StringColor));
    }

    [Fact]
    public void A_kept_message_carries_on_after_the_next()
    {
        var (line, _, _) = Line();

        line.Talk(MessageLine.NoActor, Message("Ab", new[] { Escape, (byte)2 }));
        Assert.Equal(0, line.HaveMessage);

        line.Talk(MessageLine.NoActor, Message("Cd"));

        Assert.True(Drawn(line, 0, 0, line.StringColor));
        Assert.True(Drawn(line, 3, 0, line.StringColor));
        Assert.Equal(60 + 2 * 6 + 2 * 6, line.TalkDelay);
    }

    [Fact]
    public void Messages_take_numbers_and_names_from_variables()
    {
        var (line, engine, _) = Line();
        engine.Variables[60] = 42;
        engine.Variables[61] = 7;
        engine.Variables[62] = 3;

        line.Talk(MessageLine.NoActor, Message(new[] { Escape, (byte)4, (byte)60, (byte)0 }, " ", new[] { Escape, (byte)6, (byte)61, (byte)0 }, " ", new[] { Escape, (byte)5, (byte)62, (byte)0 }));

        Assert.Equal("42 thing 7 Push", line.Plain());
    }

    [Fact]
    public void Text_kept_in_variables()
    {
        var (line, engine, _) = Line();
        engine.Variables[100] = 'D';
        engine.Variables[101] = '@';
        engine.Variables[102] = 'r';

        line.Talk(MessageLine.NoActor, Message(new[] { Escape, (byte)7, (byte)100, (byte)0 }));

        Assert.Equal("Dr", line.Plain());
    }

    [Fact]
    public void Actors_speak_in_their_colour_only_in_the_current_room()
    {
        var (line, _, actors) = Line();
        actors[2].TalkColor = 14;
        actors[2].Room = 5;

        line.Talk(2, Message("Hi"));
        Assert.Equal(0, line.HaveMessage);

        actors[2].Room = 1;
        line.Talk(2, Message("Hi"));

        Assert.Equal(2, line.TalkingActor);
        Assert.True(Drawn(line, 0, 0, 14));

        line.Stop();
        Assert.Equal(MessageLine.NoActor, line.TalkingActor);
    }

    /// <summary>A talking kid starts the talk frame, and stops it when the message goes.</summary>
    [Fact]
    public void Talking_animates_the_speaker()
    {
        if (GameFolders.Find("maniac_mansion_enhanced_DOS.com") is not { } path)
        {
            return;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.StartScene(1);
        var dave = world.Actors[3];
        dave.SetCostume(3);
        dave.Put(10, 60, 1);

        world.Messages.Talk(3, Message("Hi"));
        Assert.Equal(CostumeRenderer.TalkStartFrame, dave.Frame);

        world.Messages.CountDown(world.Messages.TalkDelay);
        world.Messages.Display();
        Assert.Equal(CostumeRenderer.TalkStopFrame, dave.Frame);
    }
}
