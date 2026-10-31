using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Inspection;

/// <summary>Which objects a room picture shows: none, those the game starts with drawn (by their states at the start), or all of them.</summary>
public enum RoomObjects
{
    None,
    Initial,
    All,
}

/// <summary>The way a figure faces.</summary>
public enum Facing
{
    Left,
    Right,
    Front,
    Back,
}

/// <summary>An object of a room as the room's data gives it: its number, name, rectangle in pixels, the point a kid walks to, and the verbs it answers to with where their code starts.</summary>
public sealed record ObjectInfo
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int WalkX { get; init; }
    public int WalkY { get; init; }
    public IReadOnlyList<VerbEntry> Verbs { get; init; } = [];
}

/// <summary>
/// A room's whole picture, as wide as the room, in the release's colour numbers
/// (<see cref="Palettes.ForVersion"/>), with its mask (non-zero where actors in a masked walkbox go
/// behind) and its objects.
/// </summary>
public sealed record RoomPicture
{
    public int Number { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public required Surface8 Pixels { get; init; }
    public required Surface8 Mask { get; init; }
    public IReadOnlyList<ObjectInfo> Objects { get; init; } = [];
}

/// <summary>
/// A costume's figures, in the release's colour numbers with <see cref="Transparent"/> around them,
/// coloured as the actor with the costume's number: standing facing left, right, front and back,
/// then each step of the walk to the right. <see cref="Format"/> is the costume format (none on the
/// C64); <see cref="Animations"/> counts its animations, or on the C64 its clips.
/// </summary>
public sealed record CostumeFigures
{
    /// <summary>The colour number of the pixels a figure does not cover.</summary>
    public const byte Transparent = CostumeRenderer.Transparent;

    public int Number { get; init; }
    public int? Format { get; init; }
    public int Animations { get; init; }
    public IReadOnlyList<CostumeFrame> Standing { get; init; } = [];
    public IReadOnlyList<CostumeFrame> Walking { get; init; } = [];
}

/// <summary>A line of a script listing: an instruction as text (<c>putActor(1, 80, 60)</c>), or, with no text, bytes that execution never reaches.</summary>
public readonly record struct ScriptLine
{
    public int Offset { get; init; }
    public int Length { get; init; }
    public string? Instruction { get; init; }
}

/// <summary>
/// Script code decoded along every path execution can take: what it is (<c>Script 12 (345
/// bytes)</c>, <c>Room 3 entry</c>, <c>Object 52 "door"</c>), the verbs that start into it for an
/// object, its lines in order, and, when the decoding stopped on a bad instruction, why.
/// </summary>
public sealed record ScriptListing
{
    public string Title { get; init; } = "";
    public IReadOnlyList<VerbEntry> Verbs { get; init; } = [];
    public IReadOnlyList<ScriptLine> Lines { get; init; } = [];
    public string? Error { get; init; }
}

/// <summary>A figure to stand on a room's screen: its costume, its position in the room in actor units, and which way it faces.</summary>
public readonly record struct StandingActor(int Costume, int X, int Y, Facing Facing);

/// <summary>Where a standing figure was drawn: the walkbox under it (-1 for none) and whether that walkbox put it behind the room's mask.</summary>
public readonly record struct PlacedActor
{
    public int Costume { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Box { get; init; }
    public bool Behind { get; init; }
}

/// <summary>The 320x200 screen with a room in view, the camera's centre and the room's first column in view, and where the figures went.</summary>
public sealed record RoomScreen
{
    public required Surface8 Screen { get; init; }
    public int CameraX { get; init; }
    public int ViewLeft { get; init; }
    public IReadOnlyList<PlacedActor> Actors { get; init; } = [];
}

/// <summary>
/// Looks inside a release's data without playing it: room pictures and their objects, costumes,
/// scripts decoded, and a room's screen with figures standing in it. Rooms and objects are as the
/// game starts them. Damaged data throws <see cref="InvalidDataException"/>.
/// </summary>
public sealed class GameInspector(ResourceLoader loader)
{
    // the C64's clips: walking right, and standing facing left (then right, front and back)
    private const int V0WalkRight = 1, V0StandLeft = 4;

    /// <summary>The SCUMM version of the release: 0 for the C64, 1 and 2 for DOS.</summary>
    public int Version => loader.Index.Version;

    /// <summary>A room's picture with its objects, or null if there is no such room.</summary>
    public RoomPicture? Room(int number, RoomObjects objects = RoomObjects.Initial)
    {
        if (loader.LoadRoom(number) is not { } room)
        {
            return null;
        }

        var image = objects switch
        {
            RoomObjects.None => RoomRenderer.DrawBackground(room),
            RoomObjects.All => RoomRenderer.RenderAll(room),
            _ => RoomRenderer.Render(room, InitialState),
        };
        var infos = room.Objects
            .Select(o => new ObjectInfo
            {
                Number = o.Number, Name = o.Name, X = o.X, Y = o.Y, Width = o.Width, Height = o.Height,
                WalkX = o.WalkX, WalkY = o.WalkY, Verbs = o.Verbs,
            })
            .ToList();
        return new RoomPicture { Number = number, Width = room.Width, Height = room.Height, Pixels = image.Pixels, Mask = image.Mask, Objects = infos };
    }

    /// <summary>A costume's standing and walking figures, or null if there is no such costume.</summary>
    public CostumeFigures? Costume(int number)
    {
        if (Version == 0)
        {
            if (loader.LoadCostumeV0(number) is not { } clips)
            {
                return null;
            }

            int steps = V0CostumeRenderer.ClipLength(clips, V0WalkRight);
            return new CostumeFigures
            {
                Number = number,
                Animations = clips.ClipCount,
                Standing = Enumerable.Range(V0StandLeft, 4).Select(clip => V0CostumeRenderer.DrawClip(clips, clip, number)).ToList(),
                Walking = Enumerable.Range(0, steps).Select(step => V0CostumeRenderer.DrawClip(clips, V0WalkRight, number, step)).ToList(),
            };
        }

        if (loader.LoadCostume(number) is not { } costume)
        {
            return null;
        }

        int[] standing = [CostumeRenderer.InitFrame, CostumeRenderer.StandFrame];
        int[] walking = [CostumeRenderer.InitFrame, CostumeRenderer.WalkFrame];
        int walkSteps = costume.Animation(CostumeRenderer.WalkFrame * 4 + CostumeRenderer.Right)
            .Where(l => l.Start is not null).Select(l => l.Length + 1).DefaultIfEmpty(1).Max();
        return new CostumeFigures
        {
            Number = number,
            Format = costume.Format,
            Animations = costume.AnimationCount,
            Standing = new[] { CostumeRenderer.Left, CostumeRenderer.Right, CostumeRenderer.Front, CostumeRenderer.Back }
                .Select(d => CostumeRenderer.DrawFrame(costume, d, standing, number)).ToList(),
            Walking = Enumerable.Range(0, walkSteps)
                .Select(step => CostumeRenderer.DrawFrame(costume, CostumeRenderer.Right, walking, number, step)).ToList(),
        };
    }

    /// <summary>A global script decoded, or null if there is no such script.</summary>
    public ScriptListing? Script(int number)
    {
        if (loader.Load(ResourceType.Script, number) is not { } data)
        {
            return null;
        }

        // global script code starts after the 4-byte resource header (_resourceHeaderSize)
        return List($"Script {number} ({data.Length} bytes)", data, [new VerbEntry(0, 4)], []);
    }

    /// <summary>A room's entry and exit scripts and its objects' verb scripts decoded, or null if there is no such room.</summary>
    public IReadOnlyList<ScriptListing>? RoomScripts(int number)
    {
        if (loader.LoadRoom(number) is not { } room)
        {
            return null;
        }

        var listings = new List<ScriptListing>();
        if (!room.EntryScript.IsEmpty)
        {
            listings.Add(List($"Room {number} entry", room.EntryScript, [new VerbEntry(0, 0)], []));
        }

        if (!room.ExitScript.IsEmpty)
        {
            listings.Add(List($"Room {number} exit", room.ExitScript, [new VerbEntry(0, 0)], []));
        }

        foreach (var obj in room.Objects.Where(o => o.Verbs.Count > 0))
        {
            listings.Add(List($"Object {obj.Number} \"{obj.Name}\"", obj.Code, obj.Verbs, obj.Verbs));
        }

        return listings;
    }

    /// <summary>
    /// The screen with a room as the game starts it, the camera centred at <paramref name="cameraX"/>
    /// (or at the room's left edge), and the figures standing back to front; null if there is no
    /// such room. A figure goes behind the room's mask when the walkbox under it says so. A costume
    /// that is not there throws <see cref="KeyNotFoundException"/>.
    /// </summary>
    public RoomScreen? View(int number, int? cameraX = null, IReadOnlyList<StandingActor>? actors = null)
    {
        if (loader.LoadRoom(number) is not { } room)
        {
            return null;
        }

        var camera = new Camera();
        camera.SetRoom(room.Width);
        camera.SetAtEx(cameraX ?? 0);
        camera.Move();

        var image = RoomView.Window(RoomRenderer.Render(room, InitialState), camera);
        var placed = new List<PlacedActor>();
        foreach (var actor in (actors ?? []).OrderBy(a => a.Y))
        {
            int direction = (int)actor.Facing;
            CostumeFrame frame = room.Version == 0
                ? V0CostumeRenderer.DrawClip(loader.LoadCostumeV0(actor.Costume) ?? throw NoCostume(actor.Costume), V0StandLeft + direction, actor.Costume)
                : CostumeRenderer.DrawFrame(loader.LoadCostume(actor.Costume) ?? throw NoCostume(actor.Costume), direction,
                    [CostumeRenderer.InitFrame, CostumeRenderer.StandFrame], actor.Costume);

            // version 0's layer is the mask's low 2 bits
            int box = room.WalkBoxes.FindBox(actor.X, actor.Y);
            int mask = box >= 0 ? room.WalkBoxes.Boxes[box].Mask : 0;
            bool behind = (room.Version == 0 ? mask & 3 : mask) != 0;
            RoomView.DrawActor(image, room.Version, camera, new ActorDrawing(frame, actor.X, actor.Y, actor.Facing == Facing.Left, behind));
            placed.Add(new PlacedActor { Costume = actor.Costume, X = actor.X, Y = actor.Y, Box = box, Behind = behind });
        }

        return new RoomScreen { Screen = RoomView.Screen(image, ScreenLayout.ForVersion(room.Version)), CameraX = camera.X, ViewLeft = camera.Left, Actors = placed };
    }

    private int InitialState(int obj)
    {
        var states = loader.Index.ObjectStates;
        return obj < states.Count ? states[obj] : 0;
    }

    private static KeyNotFoundException NoCostume(int number) => new($"There is no costume {number}");

    /// <summary>
    /// Decodes what execution can reach from the entries; the bytes between are lines without
    /// text. A bad instruction ends the listing with the reason.
    /// </summary>
    private ScriptListing List(string title, ReadOnlyMemory<byte> code, IReadOnlyList<VerbEntry> entries, IReadOnlyList<VerbEntry> verbs)
    {
        var lines = new List<ScriptLine>();
        int covered = entries.Min(v => v.Offset);
        try
        {
            var decoder = new ScriptDecoder(Version);
            foreach (var instruction in decoder.DecodeReachable(code, entries.Select(v => v.Offset)).Values)
            {
                if (instruction.Offset > covered)
                {
                    lines.Add(new ScriptLine { Offset = covered, Length = instruction.Offset - covered });
                }

                // the instruction's text without the offset its ToString starts with
                string text = instruction.ToString();
                lines.Add(new ScriptLine { Offset = instruction.Offset, Length = instruction.Length, Instruction = text[(text.IndexOf("] ", StringComparison.Ordinal) + 2)..] });
                covered = Math.Max(covered, instruction.End);
            }

            if (covered < code.Length)
            {
                lines.Add(new ScriptLine { Offset = covered, Length = code.Length - covered });
            }

            return new ScriptListing { Title = title, Verbs = verbs, Lines = lines };
        }
        catch (InvalidDataException ex)
        {
            return new ScriptListing { Title = title, Verbs = verbs, Lines = lines, Error = ex.Message };
        }
    }
}
