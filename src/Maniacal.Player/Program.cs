using Daventry.Core.Graphics;
using Daventry.Core.Hosting;
using Daventry.Core.Storage;
using Daventry.Host.Sdl;
using Maniacal.Scumm;
using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Player;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? gamePath = null;
        bool listOnly = false;
        bool fullscreen = false;
        bool verbose = false;
        int scale = 3;
        int? frames = null;
        int? room = null;
        int? costume = null;
        int zoom = 1;
        bool font = false;
        int? view = null;
        int? script = null;
        int? roomScripts = null;
        int? runSteps = null;
        string? screenshot = null;
        var settings = new List<(int Variable, int Value)>();
        var skips = new List<int>();
        var inputs = new List<(int Step, Action<Maniacal.Scumm.Game.GameWorld> Give)>();
        int? cameraX = null;
        var actors = new List<(int Costume, int X, int Y, int Direction)>();
        string? text = null;
        bool mask = false;
        string objects = "initial";
        string? output = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--list":
                    listOnly = true;
                    break;
                case "--fullscreen":
                    fullscreen = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--frames" when i + 1 < args.Length && int.TryParse(args[i + 1], out int frameCount):
                    frames = frameCount;
                    i++;
                    break;
                case "--scale" when i + 1 < args.Length && int.TryParse(args[i + 1], out scale):
                    i++;
                    break;
                case "--room" when i + 1 < args.Length && int.TryParse(args[i + 1], out int number):
                    room = number;
                    i++;
                    break;
                case "--costume" when i + 1 < args.Length && int.TryParse(args[i + 1], out int costumeNumber):
                    costume = costumeNumber;
                    i++;
                    break;
                case "--zoom" when i + 1 < args.Length && int.TryParse(args[i + 1], out zoom) && zoom is >= 1 and <= 8:
                    i++;
                    break;
                case "--view" when i + 1 < args.Length && int.TryParse(args[i + 1], out int viewRoom):
                    view = viewRoom;
                    i++;
                    break;
                case "--camera" when i + 1 < args.Length && int.TryParse(args[i + 1], out int cx):
                    cameraX = cx;
                    i++;
                    break;
                case "--actor" when i + 1 < args.Length && ParseActor(args[i + 1]) is { } actor:
                    actors.Add(actor);
                    i++;
                    break;
                case "--script" when i + 1 < args.Length && int.TryParse(args[i + 1], out int scriptNumber):
                    script = scriptNumber;
                    i++;
                    break;
                case "--room-scripts" when i + 1 < args.Length && int.TryParse(args[i + 1], out int scriptsRoom):
                    roomScripts = scriptsRoom;
                    i++;
                    break;
                case "--run":
                    runSteps = i + 1 < args.Length && int.TryParse(args[i + 1], out int steps) ? steps : 0;
                    if (runSteps > 0 || (i + 1 < args.Length && args[i + 1] == "0"))
                    {
                        i++;
                    }

                    break;
                case "--screenshot" when i + 1 < args.Length:
                    screenshot = args[++i];
                    break;
                case "--click" when i + 1 < args.Length && ParseClick(args[i + 1]) is { } click:
                    inputs.Add((click.Step, w => w.Click(click.X, click.Y)));
                    i++;
                    break;
                case "--key" when i + 1 < args.Length && ParseKey(args[i + 1]) is { } key:
                    inputs.Add((key.Step, w => w.PressKey(key.Code)));
                    i++;
                    break;
                case "--skip" when i + 1 < args.Length && int.TryParse(args[i + 1], out int skipStep):
                    skips.Add(skipStep);
                    i++;
                    break;
                case "--set" when i + 1 < args.Length && args[i + 1].Split('=') is [var name, var value]
                    && int.TryParse(name, out int variable) && int.TryParse(value, out int number):
                    settings.Add((variable, number));
                    i++;
                    break;
                case "--font":
                    font = true;
                    break;
                case "--text" when i + 1 < args.Length:
                    text = args[++i];
                    break;
                case "--mask":
                    mask = true;
                    break;
                case "--objects" when i + 1 < args.Length && args[i + 1] is "none" or "initial" or "all":
                    objects = args[++i];
                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case var arg when !arg.StartsWith("--", StringComparison.Ordinal) && gamePath is null:
                    gamePath = arg;
                    break;
                default:
                    PrintUsage();
                    return 2;
            }
        }

        if (gamePath is null || !Directory.Exists(gamePath))
        {
            PrintUsage();
            return 2;
        }

        var log = new TextWriterLog(Console.Out, verbose ? LogLevel.Debug : LogLevel.Info);
        var registry = new EngineRegistry().Add(new ScummEngineProvider());

        if (registry.DetectInDirectory(gamePath) is not var (container, detected))
        {
            Console.Error.WriteLine($"No Maniac Mansion release found in {gamePath} or its subfolders");
            return 1;
        }

        foreach (var game in detected)
        {
            Console.WriteLine(game);
        }

        if (listOnly)
        {
            return 0;
        }

        if (runSteps is { } stepCount)
        {
            return RunHeadless(container, stepCount, screenshot, zoom, settings, skips, inputs);
        }

        if (script is not null || roomScripts is not null)
        {
            return Disassemble(container, script, roomScripts);
        }

        if (view is { } viewNumber)
        {
            return WriteView(container, viewNumber, cameraX, actors, zoom, output ?? $"view-{viewNumber:D2}.png");
        }

        if (font || text is not null)
        {
            return WriteFont(detected[0], text, zoom, output ?? (text is null ? "font.png" : "text.png"));
        }

        if (costume is { } costumeId)
        {
            return WriteCostume(container, costumeId, zoom, output ?? $"costume-{costumeId:D2}.png");
        }

        if (room is { } roomNumber)
        {
            return WriteRoom(container, roomNumber, objects, mask, zoom, output ?? $"room-{roomNumber:D2}{(mask ? "-mask" : "")}.png");
        }

        var saveDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Maniacal", "saves", detected[0].GameId);

        using var host = new SdlHost(new SdlHostOptions
        {
            Title = "Maniacal",
            Scale = scale,
            Fullscreen = fullscreen,
            Log = log,
            Saves = new FileSaveStore(saveDirectory),
        });

        Engine engine;
        try
        {
            engine = registry.Create(host, container, detected[0]);
        }
        catch (NotSupportedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }

        using (engine)
        {
            // the game draws its own cursor, the crosshair
            SDL.SDL3.SDL_HideCursor();
            host.Run(engine, frames);
        }

        return 0;
    }

    /// <summary>
    /// Writes a room's picture, or its mask in black and white, as a PNG. The objects are drawn as
    /// the game starts (<c>initial</c>), not at all (<c>none</c>), or all of them whatever their state (<c>all</c>).
    /// </summary>
    private static int WriteRoom(IGameContainer container, int number, string objects, bool mask, int zoom, string path)
    {
        try
        {
            var loader = ResourceLoader.Open(container);
            if (loader.LoadRoom(number) is not { } room)
            {
                Console.Error.WriteLine($"There is no room {number}");
                return 1;
            }

            var states = loader.Index.ObjectStates;
            var image = objects switch
            {
                "none" => RoomRenderer.DrawBackground(room),
                "all" => RoomRenderer.RenderAll(room),
                _ => RoomRenderer.Render(room, n => n < states.Count ? states[n] : 0),
            };
            using var file = File.Create(path);
            if (mask)
            {
                PngWriter.Write(file, image.Mask, [Rgb.Black, Rgb.White], zoom, zoom);
            }
            else
            {
                PngWriter.Write(file, image.Pixels, Palettes.Ega, zoom, zoom);
            }

            Console.WriteLine($"Room {number}: {room.Width}x{room.Height}, {room.Objects.Count} objects, written to {path}");
            foreach (var obj in room.Objects)
            {
                Console.WriteLine($"  object {obj.Number} \"{obj.Name}\" at ({obj.X}, {obj.Y}) {obj.Width}x{obj.Height}, walk to ({obj.WalkX}, {obj.WalkY}), verbs {string.Join(" ", obj.Verbs.Select(v => v.Verb))}");
            }
            return 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
    }

    /// <summary>
    /// Writes a sheet of a costume's figures as a PNG: standing in the four directions, then the
    /// walk cycle facing right. Version 1 colours are those of the actor with the costume's number.
    /// </summary>
    private static int WriteCostume(IGameContainer container, int number, int zoom, string path)
    {
        try
        {
            if (ResourceLoader.Open(container).LoadCostume(number) is not { } costume)
            {
                Console.Error.WriteLine($"There is no costume {number}");
                return 1;
            }

            int[] standing = [CostumeRenderer.InitFrame, CostumeRenderer.StandFrame];
            int[] walking = [CostumeRenderer.InitFrame, CostumeRenderer.WalkFrame];
            int walkSteps = costume.Animation(CostumeRenderer.WalkFrame * 4 + CostumeRenderer.Right)
                .Where(l => l.Start is not null).Select(l => l.Length + 1).DefaultIfEmpty(1).Max();

            var rows = new List<List<CostumeFrame>>
            {
                new[] { CostumeRenderer.Left, CostumeRenderer.Right, CostumeRenderer.Front, CostumeRenderer.Back }
                    .Select(d => CostumeRenderer.DrawFrame(costume, d, standing, number)).ToList(),
                Enumerable.Range(0, walkSteps)
                    .Select(step => CostumeRenderer.DrawFrame(costume, CostumeRenderer.Right, walking, number, step)).ToList(),
            };

            var sheet = Sheet(rows);
            var palette = new Rgb[256];
            Palettes.Ega.CopyTo(palette);
            palette[CostumeRenderer.Transparent] = new Rgb(0x40, 0x40, 0x60);

            using var file = File.Create(path);
            PngWriter.Write(file, sheet, palette, zoom, zoom);
            Console.WriteLine($"Costume {number}: format 0x{costume.Format:X2}, {costume.AnimationCount} animations, {walkSteps} walk steps, written to {path}");
            return 0;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
    }

    /// <summary>
    /// Runs the boot script headless, then the given number of main-loop steps: script delays count
    /// down by the step's length in ticks (<c>VAR_TIMER_NEXT</c>, at least 1, as <c>scummLoop</c>
    /// does) and every running script runs once. Prints what the scripts asked of the world, the
    /// slots still alive and the variables that are not 0.
    /// </summary>
    private static int RunHeadless(IGameContainer container, int steps, string? screenshot, int zoom, List<(int Variable, int Value)> settings, List<int> skips,
        List<(int Step, Action<Maniacal.Scumm.Game.GameWorld> Give)> inputs)
    {
        var world = new Maniacal.Scumm.Game.GameWorld(ResourceLoader.Open(container));
        var engine = world.Engine;

        try
        {
            world.Boot();

            // variables set after the boot stand in for input there is no way to give yet
            foreach (var (variable, value) in settings)
            {
                engine.WriteVar(variable, value);
            }

            for (int step = 0; step < steps; step++)
            {
                // a skip presses the cutscene key: the cutscene's override jump is taken
                if (skips.Contains(step))
                {
                    engine.AbortCutscene();
                }

                foreach (var input in inputs.Where(input => input.Step == step))
                {
                    input.Give(world);
                }

                world.Step();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            Console.WriteLine($"Stopped in script {engine.CurrentScriptNumber}: {ex.Message}");
        }

        foreach (var call in world.Unhandled)
        {
            Console.WriteLine(call);
        }

        Console.WriteLine();
        Console.WriteLine($"Room {world.CurrentRoom?.Number ?? 0}, camera at {world.Camera.X}, {world.Steps} steps");
        Console.WriteLine("Actors here: " + string.Join(", ", world.Actors.Skip(1).Where(a => a.InCurrentRoom)
            .Select(a => $"{a.Number} \"{a.Name}\" costume {a.CostumeNumber} at ({a.X}, {a.Y}) facing {a.Facing}{(a.Visible ? "" : " hidden")}")));
        if (screenshot is not null)
        {
            using var file = File.Create(screenshot);
            var screen = world.Screen();
            world.DrawCursor(screen);
            PngWriter.Write(file, screen, Palettes.Ega, zoom, zoom);
            Console.WriteLine($"Screen written to {screenshot}");
        }

        Console.WriteLine();
        Console.WriteLine("Slots: " + string.Join(", ", engine.Slots.Select((s, i) => (s, i)).Where(p => !p.s.IsDead)
            .Select(p => $"{p.i}: {p.s.Kind} {p.s.Number} at {p.s.Offset:X4} status {p.s.Status:X2}{(p.s.Status == SlotStatus.Paused ? $" delay {p.s.Delay}" : "")}")));
        Console.WriteLine("Variables: " + string.Join(", ", engine.Variables.Select((v, i) => (v, i)).Where(p => p.v != 0).Select(p => $"{p.i}={p.v}")));
        return 0;
    }

    /// <summary>A click given at a step: <c>step:x,y</c>, in screen pixels.</summary>
    private static (int Step, int X, int Y)? ParseClick(string text)
        => text.Split(':') is [var step, var point] && point.Split(',') is [var x, var y]
            && int.TryParse(step, out int s) && int.TryParse(x, out int px) && int.TryParse(y, out int py)
            ? (s, px, py)
            : null;

    /// <summary>A key given at a step: <c>step:c</c> for a character, <c>step:esc</c>, or <c>step:f1</c> to <c>step:f9</c>.</summary>
    private static (int Step, int Code)? ParseKey(string text)
    {
        if (text.Split(':') is not [var step, var key] || !int.TryParse(step, out int s))
        {
            return null;
        }

        int? code = key.ToLowerInvariant() switch
        {
            "esc" => Maniacal.Scumm.Game.GameWorld.KeyEscape,
            "enter" => 13,
            "space" => ' ',
            [var c] => c,
            ['f', var digit] when digit is >= '1' and <= '9' => Maniacal.Scumm.Game.GameWorld.KeyF1 + digit - '1',
            _ => null,
        };
        return code is { } value ? (s, value) : null;
    }

    /// <summary>
    /// Prints the instructions of a global script, or of a room's entry and exit scripts and its
    /// objects' verb code. Decoding stops at the first instruction that does not decode, with the
    /// bytes from there.
    /// </summary>
    private static int Disassemble(IGameContainer container, int? script, int? roomNumber)
    {
        var loader = ResourceLoader.Open(container);
        var decoder = new ScriptDecoder(loader.Index.Version);

        static void Print(ScriptDecoder decoder, string title, ReadOnlyMemory<byte> code, int start, IReadOnlyList<Maniacal.Scumm.Rooms.VerbEntry>? verbs = null)
        {
            Console.WriteLine(title);
            try
            {
                // only what execution can reach is decoded; the bytes between are shown as such
                var entries = verbs?.Select(v => v.Offset).ToList() ?? [start];
                int covered = start;
                foreach (var instruction in decoder.DecodeReachable(code, entries).Values)
                {
                    if (instruction.Offset > covered)
                    {
                        Console.WriteLine($"    [{covered:X4}] {instruction.Offset - covered} unreachable bytes");
                    }

                    foreach (var verb in verbs?.Where(v => v.Offset == instruction.Offset) ?? [])
                    {
                        Console.WriteLine($"  verb {verb.Verb}:");
                    }

                    Console.WriteLine("    " + instruction);
                    covered = Math.Max(covered, instruction.End);
                }

                if (covered < code.Length)
                {
                    Console.WriteLine($"    [{covered:X4}] {code.Length - covered} unreachable bytes");
                }
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine($"    {ex.Message}");
            }

            Console.WriteLine();
        }

        try
        {
            if (script is { } number)
            {
                if (loader.Load(ResourceType.Script, number) is not { } data)
                {
                    Console.Error.WriteLine($"There is no script {number}");
                    return 1;
                }

                // global script code starts after the 4-byte resource header (_resourceHeaderSize)
                Print(decoder, $"Script {number} ({data.Length} bytes)", data, 4);
            }

            if (roomNumber is { } n)
            {
                if (loader.LoadRoom(n) is not { } room)
                {
                    Console.Error.WriteLine($"There is no room {n}");
                    return 1;
                }

                if (!room.EntryScript.IsEmpty)
                {
                    Print(decoder, $"Room {n} entry", room.EntryScript, 0);
                }

                if (!room.ExitScript.IsEmpty)
                {
                    Print(decoder, $"Room {n} exit", room.ExitScript, 0);
                }

                foreach (var obj in room.Objects.Where(o => o.Verbs.Count > 0))
                {
                    Print(decoder, $"Object {obj.Number} \"{obj.Name}\"", obj.Code, obj.Verbs.Min(v => v.Offset), obj.Verbs);
                }
            }

            return 0;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
    }

    /// <summary>Parses "costume,x,y" or "costume,x,y,direction", the position in actor units and the direction left, right, front or back.</summary>
    private static (int Costume, int X, int Y, int Direction)? ParseActor(string value)
    {
        var parts = value.Split(',');
        if (parts.Length is < 3 or > 4 || !int.TryParse(parts[0], out int costume)
            || !int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y))
        {
            return null;
        }

        int direction = parts.Length == 3 ? CostumeRenderer.Front : parts[3] switch
        {
            "left" => CostumeRenderer.Left,
            "right" => CostumeRenderer.Right,
            "front" => CostumeRenderer.Front,
            "back" => CostumeRenderer.Back,
            _ => -1,
        };

        return direction < 0 ? null : (costume, x, y, direction);
    }

    /// <summary>
    /// Writes the 320x200 screen with a room as the game starts it, the camera at the given centre
    /// (or the room's left edge), and standing actors. Version 1 colours are those of the actor with
    /// the costume's number. An actor goes behind the room mask when the walkbox under it says so.
    /// </summary>
    private static int WriteView(IGameContainer container, int number, int? cameraX, List<(int Costume, int X, int Y, int Direction)> actors, int zoom, string path)
    {
        try
        {
            var loader = ResourceLoader.Open(container);
            if (loader.LoadRoom(number) is not { } room)
            {
                Console.Error.WriteLine($"There is no room {number}");
                return 1;
            }

            var states = loader.Index.ObjectStates;
            var camera = new Camera();
            camera.SetRoom(room.Width);
            camera.SetAtEx(cameraX ?? 0);
            camera.Move();

            var image = RoomView.Window(RoomRenderer.Render(room, n => n < states.Count ? states[n] : 0), camera);
            foreach (var (costumeNumber, x, y, direction) in actors.OrderBy(a => a.Y))
            {
                if (loader.LoadCostume(costumeNumber) is not { } costume)
                {
                    Console.Error.WriteLine($"There is no costume {costumeNumber}");
                    return 1;
                }

                var frame = CostumeRenderer.DrawFrame(costume, direction, [CostumeRenderer.InitFrame, CostumeRenderer.StandFrame], costumeNumber);
                int box = room.WalkBoxes.FindBox(x, y);
                bool behind = box >= 0 && room.WalkBoxes.Boxes[box].Mask != 0;
                RoomView.DrawActor(image, room.Version, camera, new ActorDrawing(frame, x, y, direction == CostumeRenderer.Left, behind));
                Console.WriteLine($"Costume {costumeNumber} at ({x}, {y}): walkbox {box}{(behind ? ", behind the mask" : "")}");
            }

            var screen = RoomView.Screen(image, ScreenLayout.ForVersion(room.Version));
            using var file = File.Create(path);
            PngWriter.Write(file, screen, Palettes.Ega, zoom, zoom);
            Console.WriteLine($"Room {number}: camera at {camera.X}, view from x {camera.Left}, written to {path}");
            return 0;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
    }

    /// <summary>
    /// Writes the release's built-in font as a PNG: all 128 glyphs in a 16 by 8 grid, or the given
    /// text on one line. White on black.
    /// </summary>
    private static int WriteFont(DetectedGame game, string? text, int zoom, string path)
    {
        bool v1 = game.Variant.StartsWith("V1", StringComparison.Ordinal);
        var englishFont = Font.English(v1 ? 1 : 2, game.Variant.Contains("Demo", StringComparison.Ordinal));

        Surface8 surface;
        if (text is null)
        {
            surface = new Surface8(16 * Font.CharWidth, 8 * Font.Height);
            for (int glyph = 0; glyph < Font.GlyphCount; glyph++)
            {
                englishFont.DrawGlyph(surface, glyph, glyph % 16 * Font.CharWidth, glyph / 16 * Font.Height, 15);
            }
        }
        else
        {
            var bytes = System.Text.Encoding.Latin1.GetBytes(text);
            if (bytes.Any(b => b >= Font.GlyphCount))
            {
                Console.Error.WriteLine("The font has only the 128 ASCII characters");
                return 2;
            }

            surface = new Surface8(Math.Max(Font.Width(bytes), 1), Font.Height);
            englishFont.DrawText(surface, bytes, 0, 0, 15);
        }

        using var file = File.Create(path);
        PngWriter.Write(file, surface, Palettes.Ega, zoom, zoom);
        Console.WriteLine($"{(text is null ? "Font" : "Text")} ({(v1 ? "V1" : "V2")}), written to {path}");
        return 0;
    }

    /// <summary>Lays figures out in rows of equal cells, every figure standing on the same point of its cell.</summary>
    private static Surface8 Sheet(List<List<CostumeFrame>> rows)
    {
        const int gap = 4;
        var all = rows.SelectMany(r => r).ToList();
        int left = all.Max(f => f.OriginX);
        int right = all.Max(f => f.Pixels.Width - f.OriginX);
        int above = all.Max(f => f.OriginY);
        int below = all.Max(f => f.Pixels.Height - f.OriginY);
        int cellWidth = left + right + gap;
        int cellHeight = above + below + gap;

        var sheet = new Surface8(cellWidth * rows.Max(r => r.Count), cellHeight * rows.Count);
        sheet.Fill(CostumeRenderer.Transparent);
        for (int row = 0; row < rows.Count; row++)
        {
            for (int column = 0; column < rows[row].Count; column++)
            {
                var frame = rows[row][column];
                int x0 = column * cellWidth + left - frame.OriginX;
                int y0 = row * cellHeight + above - frame.OriginY;
                for (int y = 0; y < frame.Pixels.Height; y++)
                {
                    for (int x = 0; x < frame.Pixels.Width; x++)
                    {
                        if (frame.Pixels[x, y] != CostumeRenderer.Transparent)
                        {
                            sheet[x0 + x, y0 + y] = frame.Pixels[x, y];
                        }
                    }
                }
            }
        }

        return sheet;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("usage: maniacal <game folder> [--list] [--scale N] [--fullscreen] [--frames N] [--verbose]");
        Console.Error.WriteLine("       maniacal <game folder> --room N [--objects none|initial|all] [--mask] [--zoom N] [--output file.png]");
        Console.Error.WriteLine("       maniacal <game folder> --costume N [--zoom N] [--output file.png]");
        Console.Error.WriteLine("       maniacal <game folder> --view N [--camera X] [--actor costume,x,y[,left|right|front|back]]... [--zoom N] [--output file.png]");
        Console.Error.WriteLine("       maniacal <game folder> --script N | --room-scripts N");
        Console.Error.WriteLine("       maniacal <game folder> --run [steps] [--click step:x,y]... [--key step:key]... [--set var=value]... [--skip step]... [--screenshot file.png] [--zoom N]");
        Console.Error.WriteLine("       maniacal <game folder> --font | --text \"words\" [--zoom N] [--output file.png]");
    }
}
