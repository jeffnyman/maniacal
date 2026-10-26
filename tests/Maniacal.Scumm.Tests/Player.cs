using System.Text;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// Plays a release by the names on screen, as a walkthrough reads: <c>Do("Pull", "door mat")</c>,
/// <c>Do("Unlock", "front door", "key")</c>. Every action is a click: the verb, then the object in
/// the room, the inventory or an actor (the kid walking along while it is off screen), then the
/// second object, and the last one again to carry the sentence out. Where a room has several
/// objects of a name, <c>"door[2]"</c> is the second from the left. Before each action the player
/// waits out any cutscene, as a player does.
/// </summary>
internal sealed class Player(GameWorld world)
{
    public GameWorld World => world;

    public Actor Kid => world.Actors[world.Engine.Variables[ScriptEngine.VarEgo]];

    public int Room => world.CurrentRoom?.Number ?? 0;

    /// <summary>The instructions left undone on the way, but for DOS's resource routines, which only manage memory.</summary>
    public IEnumerable<string> Unhandled => world.Unhandled.Where(u => !u.Contains("resourceRoutines", StringComparison.Ordinal)).Distinct();

    // the C64's kids, in the order New kid names them
    private const int VarKids = 97;

    private int ActiveVerb => world.Version == 0 ? world.ActiveVerb : world.Engine.Variables[GameWorld.VarSentenceVerb];

    private ScreenLayout Layout => ScreenLayout.ForVersion(world.Version);

    // version 0's kid walks until he stands (0 is walking on there)
    private bool KidWalking => world.Version == 0 ? Kid.Moving != ActorV0.Standing : Kid.Moving != 0;

    public void Steps(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Step();
        }
    }

    private readonly List<string> _heard = [];

    private readonly HashSet<int> _seen = [];

    // the sounds started, as they changed from step to step
    private readonly List<int> _sounds = [];

    // a step, and what is said and shown in it, as a player hears and sees it
    private void Step()
    {
        world.Step();
        _seen.Add(Room);
        if (world.Sound.LastSound is var sound and not 0 && (_sounds.Count == 0 || _sounds[^1] != sound))
        {
            _sounds.Add(sound);
        }

        string said = world.Messages.Plain();
        if (said.Trim().Length > 0 && (_heard.Count == 0 || _heard[^1] != said))
        {
            _heard.Add(said);
        }
    }

    /// <summary>Whether someone has said the words, on screen, since the player began.</summary>
    public bool Heard(string words) => _heard.Any(said => said.Contains(words, StringComparison.Ordinal));

    /// <summary>Whether the screen has shown the room, since the player began.</summary>
    public bool Saw(int room) => _seen.Contains(room);

    /// <summary>Plays on until a condition holds, at most some steps; whether it came to hold.</summary>
    public bool Until(Func<bool> condition, int steps = 3000)
    {
        for (int i = 0; i < steps && !condition(); i++)
        {
            Step();
        }

        return condition();
    }

    /// <summary>Lets the game play on, the kid standing where he is, until it has run so long.</summary>
    public void WaitUntil(TimeSpan played)
    {
        Note($"wait until {played}");
        Until(() => world.Time >= played, int.MaxValue);
        Ready();
    }

    /// <summary>Lets the game play on, the kid standing where he is, for a while.</summary>
    public void Wait(TimeSpan span) => WaitUntil(world.Time + span);

    /// <summary>Lets the game play on, the kid standing where he is, until someone says something, as a cutscene shows it, then through the cutscene.</summary>
    public void WaitToHear(string words, TimeSpan within)
    {
        Hear(words, within);
        Ready();
    }

    /// <summary>Lets the game play on until someone says something, and no further.</summary>
    public void Hear(string words, TimeSpan within)
    {
        Note($"wait to hear \"{words}\"");
        var end = world.Time + within;
        if (!Until(() => Heard(words) || world.Time >= end, int.MaxValue) || !Heard(words))
        {
            throw new InvalidOperationException($"\"{words}\" never said, {Situation()}");
        }
    }

    /// <summary>Lets the game play on, the kid standing where he is, until a sound plays, a ring of the doorbell say.</summary>
    public void WaitToHearSound(int sound, TimeSpan within)
    {
        Note($"wait to hear sound {sound}");
        var end = world.Time + within;
        int before = _sounds.Count;
        bool Started() => _sounds.Skip(before).Contains(sound);
        if (!Until(() => Started() || world.Time >= end, int.MaxValue) || !Started())
        {
            throw new InvalidOperationException($"sound {sound} never heard, {Situation()}");
        }

        Ready();
    }

    /// <summary>Lets the game play on, the kid standing where he is, until the screen shows a room, as a cutscene shows it, then through the cutscene.</summary>
    public void WaitToSee(int room, TimeSpan within)
    {
        Note($"wait to see room {room}");
        var end = world.Time + within;
        if (!Until(() => Room == room || world.Time >= end, int.MaxValue) || Room != room)
        {
            throw new InvalidOperationException($"room {room} never shown, {Situation()}");
        }

        Ready();
    }

    /// <summary>Skips the cutscene on, with Escape, then plays on until the kid can be played.</summary>
    public void Skip()
    {
        Note("skip the cutscene");
        world.PressKey(GameWorld.KeyEscape);
        Steps(2);
        Ready();
    }

    /// <summary>Plays on until nobody speaks, as a player lets a line end.</summary>
    public void Quiet() => Until(() => world.Messages.HaveMessage == 0, 600);

    /// <summary>Whether the kid can be played: the cursor shows and the room on screen is his, not a cutscene's.</summary>
    public bool Playable => world.CursorShown && Kid.Room == Room;

    /// <summary>Plays on through whatever cutscene is on until the kid can be played.</summary>
    public void Ready()
    {
        if (!Until(() => Playable, 20000))
        {
            throw new InvalidOperationException($"{Kid.Name} cannot be played, {Situation()}");
        }
    }

    /// <summary>Clicks a verb by its name, as shown.</summary>
    public void Verb(string name)
    {
        var slot = Slot(name);
        world.Click(slot.X + 4, slot.Y + 4);
        Steps(2);
    }

    private const int WalkToVerb = 13;

    private bool Shows(string verb) => world.Verbs.Slots.Any(s => s.Id != 0 && s.Mode == 1 && Shown(s.Name).Equals(verb, StringComparison.OrdinalIgnoreCase));

    private int VerbId(string name) => Slot(name).Id;

    private VerbSlot Slot(string name) =>
        world.Verbs.Slots.FirstOrDefault(s => s.Id != 0 && s.Mode == 1 && Shown(s.Name).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"no verb \"{name}\" shows; there are {string.Join(", ", world.Verbs.Slots.Where(s => s.Id != 0 && s.Mode == 1).Select(s => Shown(s.Name)))}, {Situation()}");

    /// <summary>
    /// A sentence carried out, and played on until the kid has done it. With an outcome to look for,
    /// the sentence is given again while it has not come about, as a player does when a cutscene
    /// broke in on the kid.
    /// </summary>
    public void Do(string verb, string thing, string? with = null, Func<bool>? until = null)
    {
        for (int tries = 1; ; tries++)
        {
            Sentence(verb, thing, with);
            if (until is null || until())
            {
                return;
            }

            if (tries == 3)
            {
                throw new InvalidOperationException($"{verb} {thing} did nothing, {Situation()}");
            }
        }
    }

    /// <summary>A sentence given, not waited out, for what it shows on the way.</summary>
    public void Begin(string verb, string thing, string? with = null) => Sentence(verb, thing, with, settle: false);

    /// <summary>
    /// A sentence that brings up a keypad, and the code entered on it: its keys laid out as a
    /// telephone's, 1 to 9 in rows and 0 under the 8.
    /// </summary>
    public void Keypad(string verb, string thing, string code)
    {
        Sentence(verb, thing, null, settle: false);
        if (!Until(() => Room != Kid.Room && world.CursorShown))
        {
            throw new InvalidOperationException($"{verb} {thing} shows no keypad, {Situation()}");
        }

        Note($"enter {code}");
        foreach (char digit in code)
        {
            int key = digit == '0' ? 10 : digit - '1';
            world.Click(KeypadLeft + 24 * (key % 3) + 8 - world.Camera.Left, Layout.RoomTop + KeypadTop + 16 * (key / 3) + 8);
            Steps(10);
        }

        Settle();
    }

    // the keypad's top left key, in its room
    private const int KeypadLeft = 128, KeypadTop = 48;

    private void Sentence(string verb, string thing, string? with, bool settle = true)
    {
        Ready();
        Note($"{verb} {thing}{(with is null ? string.Empty : " with " + with)}");

        // walking towards the second object would undo the first, so the kid goes first
        if (with is not null)
        {
            Click(with, second: true, click: false);
        }

        Verb(verb);
        Click(thing, second: false);
        if (with is not null)
        {
            Click(with, second: true);
        }

        // the last object clicked again carries the sentence out, unless it went already (the
        // verb back to Walk to): an object still chosen from before completes it at once
        int id = VerbId(verb);
        if (id != WalkToVerb && ActiveVerb == id)
        {
            Click(with ?? thing, second: with is not null);
        }

        if (settle)
        {
            Settle();
        }
    }

    /// <summary>Walks the kid through a door, or up stairs, into the room it leads to.</summary>
    public void Go(string thing)
    {
        int room = Kid.Room;
        Do("Walk to", thing, until: () => Kid.Room != room);
    }

    /// <summary>
    /// Walks the kid out of the room by its left or right edge: to the nameless object there that
    /// leads on, as a player clicks it, or else to the edge itself.
    /// </summary>
    public void Leave(bool left)
    {
        Ready();
        Note($"leave by the {(left ? "left" : "right")}");
        int room = Room;
        int width = world.CurrentRoom!.Width;
        var exit = world.CurrentRoom.Objects
            .Where(o => o.Number > 0 && Shown(world.NameOf(o.Number)).Length == 0 && (left ? o.X == 0 : o.X + o.Width == width) && Points(o, anywhere: true).Any())
            .FirstOrDefault();
        Verb("Walk to");
        if (exit is not null)
        {
            ClickObject(exit);
            Until(() => Room != room, 600);
        }

        for (int tries = 0; tries < 20 && Room == room; tries++)
        {
            Until(() => !KidWalking || Room != room, 400);
            if (Room == room)
            {
                world.Click(left ? 4 : 315, KidLine);
                Steps(2);
            }
        }

        if (Room == room)
        {
            throw new InvalidOperationException($"{Kid.Name} could not leave by the {(left ? "left" : "right")}, {Situation()}");
        }

        Settle();
    }

    // a point on the floor where the kid stands, on screen
    private int KidLine => Math.Min(Layout.RoomTop + Kid.Y * 2 - 2, Layout.RoomBottom - 1);

    /// <summary>
    /// Switches to another kid with New kid: on the C64 the sentence line names the three kids, on
    /// DOS the verbs give way to their names.
    /// </summary>
    public void NewKid(string name)
    {
        Note($"New kid {name}");

        // a cutscene breaking in takes the click, so it is made again
        for (int tries = 0; tries < 3 && Kid.Name != name; tries++)
        {
            Ready();
            Verb("New kid");
            Steps(3);
            if (world.Version == 0)
            {
                int slot = Enumerable.Range(0, 3).First(i => world.Actors[world.Engine.Variables[VarKids + i]].Name == name);
                world.Click(new[] { 4, 13 * 8, 27 * 8 }[slot], world.Verbs.Top + 2);
                Steps(3);
            }
            else if (Shows(name))
            {
                Verb(name);
            }

            Steps(10);
        }

        if (Kid.Name != name)
        {
            throw new InvalidOperationException($"New kid {name} plays {Kid.Name}, {Situation()}");
        }
    }

    /// <summary>What was done so far, with the room each began in, for failures to tell.</summary>
    public List<string> Log { get; } = [];

    /// <summary>The room as a failure tells it: its objects (findable or not), the actors in it, and what was done.</summary>
    private string Situation() =>
        $"in room {Room}, camera at {world.Camera.Left}; objects {string.Join(", ", world.CurrentRoom?.Objects.Where(o => o.Number > 0).Select(o => $"{Shown(world.NameOf(o.Number))}#{o.Number}{(Points(o, anywhere: true).Any() ? string.Empty : " (not found)")}") ?? [])}; "
        + $"actors {string.Join(", ", world.Actors.Skip(1).Where(a => a.InCurrentRoom && a.Visible).Select(a => $"{a.Name} at {a.X},{a.Y}"))}; carried {string.Join(", ", world.Inventory.Where(o => o != 0 && world.Engine.GetOwner(o) == Kid.Number).Select(o => Shown(world.NameOf(o))))}; heard {string.Join(" / ", _heard.TakeLast(4))}; after {string.Join("; ", Log)}";

    private void Note(string action) => Log.Add($"{world.Time.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)} {Kid.Name} in {Room} at {Kid.X},{Kid.Y}: {action}");

    /// <summary>Whether the player acts at once, not looking on after each action, as where someone will soon be back.</summary>
    public bool Hurried { get; set; }

    /// <summary>Plays on until no sentence waits and the kid stands, then, unless hurried, a little more for what follows to show.</summary>
    public void Settle()
    {
        Until(() => world.Sentences.Count == 0 && !KidWalking && !world.Engine.IsScriptRunning(SentenceScript), 1500);
        Steps(Hurried ? 2 : 20);
        Ready();
    }

    private int SentenceScript => world.Version == 0 ? 0 : ScriptEngine.SentenceScript;

    /// <summary>Whether the kid carries a thing of the name.</summary>
    public bool Carries(string name) => Carried(name) != 0;

    /// <summary>Whether a kid, played or not, carries a thing of the name.</summary>
    public bool Carries(string name, string kid) => Carried(name, world.Actors.First(a => a.Name == kid).Number) != 0;

    /// <summary>How many things of the name the kid carries.</summary>
    public int CarriedCount(string name) =>
        world.Inventory.Count(obj => obj != 0 && world.Engine.GetOwner(obj) == Kid.Number && Shown(world.NameOf(obj)) == name);

    private int Carried(string name) => Carried(name, Kid.Number);

    private int Carried(string name, int owner) =>
        world.Inventory.FirstOrDefault(obj => obj != 0 && world.Engine.GetOwner(obj) == owner && Shown(world.NameOf(obj)) == name);

    /// <summary>Whether the room's thing of the name, a door say, stands open.</summary>
    public bool IsOpen(string thing) =>
        InRoom(thing).FirstOrDefault() is { } obj && (world.StateOf(obj.Number) & ScriptEngine.StateIntrinsic) != 0;

    /// <summary>The objects of the room by the name, left to right, that the mouse can find somewhere.</summary>
    private List<Rooms.RoomObject> InRoom(string name) =>
        world.CurrentRoom?.Objects
            .Where(o => o.Number > 0 && Shown(world.NameOf(o.Number)) == name && Points(o, anywhere: true).Any())
            .OrderBy(o => world.PositionOf(o).X)
            .ToList() ?? [];

    // a thing clicked, or with click false only brought on screen
    private void Click(string thing, bool second, bool click = true)
    {
        Ready();
        string name = thing;
        int nth = 1;
        bool numbered = thing.EndsWith(']') && thing.LastIndexOf('[') > 0;
        if (numbered)
        {
            int open = thing.LastIndexOf('[');
            name = thing[..open];
            nth = int.Parse(thing[(open + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
        }

        // something carried goes first, unless the room's is meant by number
        if (!numbered && Carried(name) is var carried and not 0)
        {
            if (click)
            {
                ClickCarried(carried);
            }

            return;
        }

        if (Actor(name) is { } actor)
        {
            ClickActor(actor, click);
            return;
        }

        var matches = InRoom(name);
        if (matches.Count < nth || !ClickObject(matches[nth - 1], click))
        {
            throw new InvalidOperationException($"no \"{thing}\"{(second ? " for the second object" : string.Empty)}, {Situation()}");
        }
    }

    /// <summary>Clicks an object of the room, walking the kid towards it first while it is off screen.</summary>
    private bool ClickObject(Rooms.RoomObject target, bool click = true)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            Ready();
            if (Points(target, anywhere: false).FirstOrDefault() is { X: > 0 } point)
            {
                if (click)
                {
                    world.Click(point.X, point.Y);
                    Steps(2);
                }

                return true;
            }

            WalkTowards(world.PositionOf(target).X + target.Width / 2);
        }

        return false;
    }

    private Actor? Actor(string name) =>
        world.Actors.Skip(1).FirstOrDefault(a => a.InCurrentRoom && a.Visible && a != Kid && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Clicks an actor where the mouse finds him, walking the kid towards him first while he is off screen.</summary>
    private void ClickActor(Actor actor, bool click = true)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            Ready();
            int x = actor.X * 8 - world.Camera.Left, y = Layout.RoomTop + actor.Y * 2 - 20;
            if (x is >= 4 and < 316)
            {
                if (click)
                {
                    world.Click(x, y);
                    Steps(2);
                }

                return;
            }

            WalkTowards(actor.X * 8);
        }

        throw new InvalidOperationException($"{actor.Name} stays off screen, {Situation()}");
    }

    private void ClickCarried(int obj)
    {
        int owner = Kid.Number;
        int index = world.Inventory.Where(o => o != 0 && world.Engine.GetOwner(o) == owner).ToList().IndexOf(obj);
        for (int tries = 0; tries < 20 && (index < world.Verbs.InventoryOffset || index >= world.Verbs.InventoryOffset + 4); tries++)
        {
            bool down = index >= world.Verbs.InventoryOffset + 4;
            world.Click(160, world.Verbs.Top + VerbArea.InventoryTop + (down ? 12 : 4));
            Steps(2);
        }

        int place = index - world.Verbs.InventoryOffset;
        world.Click((place % 2 == 0 ? 8 : 184) + 2, world.Verbs.Top + VerbArea.InventoryTop + (place / 2) * 8 + 4);
        Steps(2);
    }

    /// <summary>Walks the kid along his line towards a point of the room, to floor short of the screen's edge where exits are.</summary>
    private void WalkTowards(int roomX)
    {
        int x = roomX - world.Camera.Left;
        int verb = ActiveVerb;
        Verb("Walk to");

        // the farthest spot on his line or near it of floor, or of an object only to look at, which
        // he walks up to; not one he would walk into, through a door or out of the room
        var spot = (from d in Enumerable.Range(0, 140)
                    let sx = x < 160 ? 20 + d : 300 - d
                    from dy in new[] { 0, 8, 16, 24, -8, -16, -24 }
                    let y = Math.Clamp(KidLine + dy, Layout.RoomTop, Layout.RoomBottom - 1)
                    where Harmless(world.FindObject(sx + world.Camera.Left, y - Layout.RoomTop))
                    select (X: sx, Y: y)).FirstOrDefault((X: 160, Y: KidLine));
        world.Click(spot.X, spot.Y);
        Steps(5);
        Until(() => !KidWalking, 300);
        if (verb != ActiveVerb)
        {
            var slot = world.Verbs.Slots[world.Verbs.SlotOf(verb)];
            world.Click(slot.X + 4, slot.Y + 4);
            Steps(2);
        }
    }

    /// <summary>Whether walking to a thing clicked runs nothing: no object, or a named one with no script for Walk to, nor one for every verb.</summary>
    private bool Harmless(int obj) =>
        obj == 0 || (world.CurrentRoom?.FindObject(obj) is { } o && Shown(world.NameOf(obj)).Length > 0
            && o.Verbs.All(v => v.Verb is not (WalkToVerb or Rooms.RoomObject.FallbackVerb or Rooms.RoomObject.FallbackVerbV0)));

    /// <summary>The screen points where the mouse finds the object, nearest its middle first: on screen now, or anywhere the camera could show.</summary>
    private IEnumerable<(int X, int Y)> Points(Rooms.RoomObject target, bool anywhere)
    {
        var (left, top) = world.PositionOf(target);
        return from dy in Enumerable.Range(0, target.Height)
               from dx in Enumerable.Range(0, target.Width)
               let px = left + dx
               let py = top + dy
               orderby Math.Abs(dx - target.Width / 2) + Math.Abs(dy - target.Height / 2)
               where (anywhere || (px >= world.Camera.Left + 2 && px < world.Camera.Left + 318)) && py + Layout.RoomTop < Layout.RoomBottom
                   && world.FindObject(px, py) == target.Number
               select (px - world.Camera.Left, py + Layout.RoomTop);
    }

    private static string Shown(byte[] name) => Shown(Encoding.Latin1.GetString(name));

    private static string Shown(string name) => name.Replace("@", string.Empty, StringComparison.Ordinal).Trim();
}
