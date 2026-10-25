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
            world.Step();
        }
    }

    /// <summary>Plays on until a condition holds, at most some steps; whether it came to hold.</summary>
    public bool Until(Func<bool> condition, int steps = 3000)
    {
        for (int i = 0; i < steps && !condition(); i++)
        {
            world.Step();
        }

        return condition();
    }

    /// <summary>Lets the game play on, the kid standing where he is, until it has run so long.</summary>
    public void Wait(TimeSpan played)
    {
        Note($"wait until {played}");
        Until(() => world.Time >= played, int.MaxValue);
        Ready();
    }

    /// <summary>Whether the kid can be played: the cursor shows and the room on screen is his, not a cutscene's.</summary>
    public bool Playable => world.CursorShown && Kid.Room == Room;

    /// <summary>Plays on through whatever cutscene is on until the kid can be played.</summary>
    public void Ready()
    {
        if (!Until(() => Playable, 20000))
        {
            throw new InvalidOperationException($"{Kid.Name} cannot be played, in room {Room}, after {string.Join("; ", Log)}");
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

    private int VerbId(string name) => Slot(name).Id;

    private VerbSlot Slot(string name) =>
        world.Verbs.Slots.FirstOrDefault(s => s.Id != 0 && s.Mode == 1 && Shown(s.Name).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"no verb \"{name}\" shows; there are {string.Join(", ", world.Verbs.Slots.Where(s => s.Id != 0 && s.Mode == 1).Select(s => Shown(s.Name)))}");

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
                throw new InvalidOperationException($"{verb} {thing} did nothing, after {string.Join("; ", Log)}");
            }
        }
    }

    private void Sentence(string verb, string thing, string? with)
    {
        Ready();
        Note($"{verb} {thing}{(with is null ? string.Empty : " with " + with)}");
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

        Settle();
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
            throw new InvalidOperationException($"{Kid.Name} could not leave room {room} by the {(left ? "left" : "right")}");
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
        Ready();
        Note($"New kid {name}");
        Verb("New kid");
        Steps(3);
        if (world.Version == 0)
        {
            int slot = Enumerable.Range(0, 3).First(i => world.Actors[world.Engine.Variables[VarKids + i]].Name == name);
            world.Click(new[] { 4, 13 * 8, 27 * 8 }[slot], world.Verbs.Top + 2);
            Steps(3);
        }
        else
        {
            Verb(name);
        }

        Steps(10);
        if (Kid.Name != name)
        {
            throw new InvalidOperationException($"New kid {name} plays {Kid.Name}");
        }
    }

    /// <summary>What was done so far, with the room each began in, for failures to tell.</summary>
    public List<string> Log { get; } = [];

    private void Note(string action) => Log.Add($"{Kid.Name} in {Room} at {Kid.X},{Kid.Y}: {action}");

    /// <summary>Plays on until no sentence waits and the kid stands, then a little more for what follows to show.</summary>
    public void Settle()
    {
        Until(() => world.Sentences.Count == 0 && !KidWalking && !world.Engine.IsScriptRunning(SentenceScript), 1500);
        Steps(20);
        Ready();
    }

    private int SentenceScript => world.Version == 0 ? 0 : ScriptEngine.SentenceScript;

    /// <summary>Whether the kid carries a thing of the name.</summary>
    public bool Carries(string name) => Carried(name) != 0;

    private int Carried(string name) =>
        world.Inventory.FirstOrDefault(obj => obj != 0 && world.Engine.GetOwner(obj) == Kid.Number && Shown(world.NameOf(obj)) == name);

    /// <summary>The objects of the room by the name, left to right, that the mouse can find somewhere.</summary>
    private List<Rooms.RoomObject> InRoom(string name) =>
        world.CurrentRoom?.Objects
            .Where(o => o.Number > 0 && Shown(world.NameOf(o.Number)) == name && Points(o, anywhere: true).Any())
            .OrderBy(o => world.PositionOf(o).X)
            .ToList() ?? [];

    private void Click(string thing, bool second)
    {
        Ready();
        string name = thing;
        int nth = 1;
        if (thing.EndsWith(']') && thing.LastIndexOf('[') is var open and > 0)
        {
            name = thing[..open];
            nth = int.Parse(thing[(open + 1)..^1], System.Globalization.CultureInfo.InvariantCulture);
        }

        // something carried goes first, unless the room's is meant by number
        if (nth == 1 && Carried(name) is var carried and not 0)
        {
            ClickCarried(carried);
            return;
        }

        if (Actor(name) is { } actor)
        {
            ClickActor(actor);
            return;
        }

        var matches = InRoom(name);
        if (matches.Count < nth || !ClickObject(matches[nth - 1]))
        {
            throw new InvalidOperationException($"no \"{thing}\" in room {Room}{(second ? " for the second object" : string.Empty)}, after {string.Join("; ", Log)}");
        }
    }

    /// <summary>Clicks an object of the room, walking the kid towards it first while it is off screen.</summary>
    private bool ClickObject(Rooms.RoomObject target)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            Ready();
            if (Points(target, anywhere: false).FirstOrDefault() is { X: > 0 } point)
            {
                world.Click(point.X, point.Y);
                Steps(2);
                return true;
            }

            WalkTowards(target);
        }

        return false;
    }

    private Actor? Actor(string name) =>
        world.Actors.Skip(1).FirstOrDefault(a => a.InCurrentRoom && a.Visible && a != Kid && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void ClickActor(Actor actor)
    {
        world.Click(Math.Clamp(actor.X * 8 - world.Camera.Left, 4, 315), Layout.RoomTop + actor.Y * 2 - 20);
        Steps(2);
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

    /// <summary>Walks the kid along his line towards where the object lies, to floor short of the screen's edge where exits are.</summary>
    private void WalkTowards(Rooms.RoomObject target)
    {
        int x = world.PositionOf(target).X + target.Width / 2 - world.Camera.Left;
        int verb = ActiveVerb;
        Verb("Walk to");

        // the farthest spot of floor, not an object the kid would walk into, on his line or near it
        var spot = (from d in Enumerable.Range(0, 140)
                    let sx = x < 160 ? 20 + d : 300 - d
                    from dy in new[] { 0, 8, 16, 24, -8, -16, -24 }
                    let y = Math.Clamp(KidLine + dy, Layout.RoomTop, Layout.RoomBottom - 1)
                    where world.FindObject(sx + world.Camera.Left, y - Layout.RoomTop) == 0
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
