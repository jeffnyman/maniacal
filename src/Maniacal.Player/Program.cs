using Maniacal.Player.Sdl;
using Maniacal.Scumm;
using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Hosting;
using Maniacal.Scumm.Inspection;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.Storage;
using Maniacal.Scumm.View;

namespace Maniacal.Player;

internal static class Program
{
    // exit codes: done, not found, a mistake on the command line, data unreadable or no window
    private const int Done = 0, NotFound = 1, BadCommandLine = 2, CannotRun = 3;

    /// <summary>A mistake on the command line, said in one line.</summary>
    private sealed class UsageException(string message) : Exception(message);

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (UsageException ex)
        {
            Error(ex.Message);
            Console.Error.WriteLine("Run 'maniacal --help' for more.");
            return BadCommandLine;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return BadCommandLine;
        }

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
        string? wav = null;
        string? loadFile = null;
        string? savesDir = null;
        string target = "maniac";
        string? saveFile = null;
        int saveVersion = Maniacal.Scumm.Saves.SaveFile.CompatibleVersion;
        bool speaker = false;
        bool copyProtection = false;
        var settings = new List<(int Variable, int Value)>();
        var skips = new List<int>();
        var inputs = new List<(int Step, Action<Maniacal.Scumm.Game.GameWorld> Give)>();
        int? cameraX = null;
        var actors = new List<StandingActor>();
        string? text = null;
        bool mask = false;
        string objects = "initial";
        string? output = null;
        var modes = new List<string>();

        int i = 0;

        // the value after an option, which must be there and not be another option
        string Next(string option, string what)
            => i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : throw new UsageException($"{option} needs {what}");

        int Number(string option, string what, int min = int.MinValue, int max = int.MaxValue)
        {
            string value = Next(option, what);
            return int.TryParse(value, out int number) && number >= min && number <= max
                ? number
                : throw new UsageException($"{option} needs {what}, not '{value}'");
        }

        T Parsed<T>(string option, string what, Func<string, T?> parse)
            where T : struct
        {
            string value = Next(option, what);
            return parse(value) ?? throw new UsageException($"{option} needs {what}, not '{value}'");
        }

        void Mode(string option)
        {
            if (!modes.Contains(option))
            {
                modes.Add(option);
            }
        }

        for (; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h" or "-?":
                    PrintHelp();
                    return Done;
                case "--version":
                    Console.WriteLine($"maniacal {Version()}");
                    return Done;
                case "--list":
                    listOnly = true;
                    Mode("--list");
                    break;
                case "--fullscreen":
                    fullscreen = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--frames":
                    frames = Number("--frames", "a number of frames", 1);
                    break;
                case "--scale":
                    scale = Number("--scale", "a scale from 1 to 8", 1, 8);
                    break;
                case "--room":
                    room = Number("--room", "a room number", 0);
                    Mode("--room");
                    break;
                case "--costume":
                    costume = Number("--costume", "a costume number", 0);
                    Mode("--costume");
                    break;
                case "--zoom":
                    zoom = Number("--zoom", "a zoom from 1 to 8", 1, 8);
                    break;
                case "--view":
                    view = Number("--view", "a room number", 0);
                    Mode("--view");
                    break;
                case "--camera":
                    cameraX = Number("--camera", "a room column");
                    break;
                case "--actor":
                    actors.Add(Parsed("--actor", "costume,x,y or costume,x,y,left|right|front|back", ParseActor));
                    break;
                case "--script":
                    script = Number("--script", "a script number", 0);
                    Mode("--script");
                    break;
                case "--room-scripts":
                    roomScripts = Number("--room-scripts", "a room number", 0);
                    Mode("--script");
                    break;
                case "--run":
                    runSteps = i + 1 < args.Length && int.TryParse(args[i + 1], out int steps) && steps >= 0 ? steps : 0;
                    if (runSteps > 0 || (i + 1 < args.Length && args[i + 1] == "0"))
                    {
                        i++;
                    }

                    Mode("--run");
                    break;
                case "--screenshot":
                    screenshot = Next("--screenshot", "a file name");
                    break;
                case "--wav":
                    wav = Next("--wav", "a file name");
                    break;
                case "--saves":
                    savesDir = Next("--saves", "a folder");
                    break;
                case "--target":
                    target = Next("--target", "a target name");
                    break;
                case "--load":
                    loadFile = Next("--load", "a save file");
                    break;
                case "--save":
                    saveFile = Next("--save", "a file name");
                    break;
                case "--save-version":
                    saveVersion = Number("--save-version", "a save version from 108 to 124", 108, 124);
                    break;
                case "--speaker":
                    speaker = true;
                    break;
                case "--copy-protection":
                    copyProtection = true;
                    break;
                case "--click":
                    var click = Parsed("--click", "step:x,y", ParseClick);
                    inputs.Add((click.Step, w => w.Click(click.X, click.Y)));
                    break;
                case "--key":
                    var key = Parsed("--key", "step:key (a character, esc, enter, space or f1 to f9)", ParseKey);
                    inputs.Add((key.Step, w => w.PressKey(key.Code)));
                    break;
                case "--skip":
                    skips.Add(Number("--skip", "a step number", 0));
                    break;
                case "--set":
                    settings.Add(Parsed("--set", "variable=value", ParseSetting));
                    break;
                case "--font":
                    font = true;
                    Mode("--font");
                    break;
                case "--text":
                    text = Next("--text", "the words to write");
                    Mode("--font");
                    break;
                case "--mask":
                    mask = true;
                    break;
                case "--objects":
                    objects = Next("--objects", "none, initial or all");
                    if (objects is not ("none" or "initial" or "all"))
                    {
                        throw new UsageException($"--objects needs none, initial or all, not '{objects}'");
                    }

                    break;
                case "--output":
                    output = Next("--output", "a file name");
                    break;
                case var arg when arg.StartsWith('-'):
                    throw new UsageException($"unknown option {arg}");
                case var arg when gamePath is null:
                    gamePath = arg;
                    break;
                case var arg:
                    throw new UsageException($"one game folder only: '{gamePath}' and '{arg}' were both given");
            }
        }

        if (modes.Count > 1)
        {
            throw new UsageException($"{string.Join(" and ", modes)} cannot be used together");
        }

        if (gamePath is null)
        {
            throw new UsageException("no game folder given");
        }

        if (!Directory.Exists(gamePath))
        {
            Error($"no such folder: {gamePath}");
            return NotFound;
        }

        var log = new TextWriterLog(Console.Out, verbose ? LogLevel.Debug : LogLevel.Info);
        var registry = new EngineRegistry().Add(new ScummEngineProvider());

        if (registry.DetectInDirectory(gamePath) is not var (container, detected))
        {
            Error($"no Maniac Mansion release found in {gamePath} or its subfolders");
            return NotFound;
        }

        foreach (var game in detected)
        {
            Console.WriteLine(game);
        }

        try
        {
            if (listOnly)
            {
                return Done;
            }

            if (runSteps is { } stepCount)
            {
                return RunHeadless(container, stepCount, screenshot, wav, speaker, zoom, settings, skips, inputs, loadFile, saveFile, saveVersion, savesDir, target, copyProtection);
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

            using var host = new SdlHost(new SdlHostOptions
            {
                Title = "Maniacal",
                Scale = scale,
                Fullscreen = fullscreen,
                Log = log,
                Saves = new FileSaveStore(Path.Combine(SavesFolder, detected[0].GameId)),
            });

            using var engine = registry.Create(host, container, detected[0]);
            if (engine is ScummEngine scumm)
            {
                scumm.Pcjr = !speaker;
                scumm.CopyProtection = copyProtection;
            }

            // the game draws its own cursor, the crosshair
            SDL.SDL3.SDL_HideCursor();
            host.Run(engine, frames);
            return Done;
        }
        catch (Exception ex) when (ex is SdlException or InvalidDataException or FileNotFoundException or NotSupportedException or IOException)
        {
            Error(ex.Message);
            return CannotRun;
        }
    }

    /// <summary>Where the windowed player keeps saves, a folder for each game under it.</summary>
    private static string SavesFolder
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Maniacal", "saves");

    /// <summary>The package version, without the commit Source Link adds after a '+'.</summary>
    private static string Version()
    {
        string version = typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "unknown";
        int plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }

    /// <summary>An error, in one line on standard error: <c>maniacal: no such folder: games</c>.</summary>
    private static void Error(string message)
    {
        // a sentence's first letter in lower case after the prefix, but not an acronym's (SDL_Init, C64)
        if (message.Length > 1 && char.IsUpper(message[0]) && char.IsLower(message[1]))
        {
            message = char.ToLowerInvariant(message[0]) + message[1..];
        }

        Console.Error.WriteLine($"maniacal: {message}");
    }

    /// <summary>A variable set at the start of a run: <c>variable=value</c>.</summary>
    private static (int Variable, int Value)? ParseSetting(string text)
        => text.Split('=') is [var name, var value] && int.TryParse(name, out int variable) && int.TryParse(value, out int number)
            ? (variable, number)
            : null;

    /// <summary>
    /// Writes a room's picture, or its mask in black and white, as a PNG. The objects are drawn as
    /// the game starts (<c>initial</c>), not at all (<c>none</c>), or all of them whatever their state (<c>all</c>).
    /// </summary>
    private static int WriteRoom(IGameContainer container, int number, string objects, bool mask, int zoom, string path)
    {
        try
        {
            var inspector = new GameInspector(ResourceLoader.Open(container));
            var which = objects switch
            {
                "none" => RoomObjects.None,
                "all" => RoomObjects.All,
                _ => RoomObjects.Initial,
            };
            if (inspector.Room(number, which) is not { } room)
            {
                Error($"there is no room {number}");
                return 1;
            }

            using var file = File.Create(path);
            if (mask)
            {
                PngWriter.Write(file, room.Mask, [Rgb.Black, Rgb.White], zoom, zoom);
            }
            else
            {
                PngWriter.Write(file, room.Pixels, Palettes.ForVersion(inspector.Version), zoom, zoom);
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
            Error(ex.Message);
            return 3;
        }
    }

    /// <summary>
    /// Writes a sheet of a costume's figures as a PNG: standing in the four directions, then the
    /// walk cycle facing right. Version 0 and 1 colours are those of the actor with the costume's number.
    /// </summary>
    private static int WriteCostume(IGameContainer container, int number, int zoom, string path)
    {
        try
        {
            var inspector = new GameInspector(ResourceLoader.Open(container));
            if (inspector.Costume(number) is not { } costume)
            {
                Error($"there is no costume {number}");
                return 1;
            }

            var palette = new Rgb[256];
            Palettes.ForVersion(inspector.Version).CopyTo(palette);
            palette[CostumeFigures.Transparent] = new Rgb(0x40, 0x40, 0x60);

            using var file = File.Create(path);
            PngWriter.Write(file, Sheet([[.. costume.Standing], [.. costume.Walking]]), palette, zoom, zoom);
            Console.WriteLine(costume.Format is { } format
                ? $"Costume {number}: format 0x{format:X2}, {costume.Animations} animations, {costume.Walking.Count} walk steps, written to {path}"
                : $"Costume {number}: {costume.Animations} clips, {costume.Walking.Count} walk steps, written to {path}");
            return 0;
        }
        catch (InvalidDataException ex)
        {
            Error(ex.Message);
            return 3;
        }
    }

    /// <summary>
    /// Runs the boot script headless, then the given number of main-loop steps: script delays count
    /// down by the step's length in ticks (<c>VAR_TIMER_NEXT</c>, at least 1, as <c>scummLoop</c>
    /// does) and every running script runs once. Prints what the scripts asked of the world, the
    /// slots still alive and the variables that are not 0.
    /// </summary>
    private static int RunHeadless(IGameContainer container, int steps, string? screenshot, string? wav, bool speaker, int zoom, List<(int Variable, int Value)> settings, List<int> skips,
        List<(int Step, Action<Maniacal.Scumm.Game.GameWorld> Give)> inputs, string? loadFile = null, string? saveFile = null, int saveVersion = Maniacal.Scumm.Saves.SaveFile.CompatibleVersion,
        string? savesDir = null, string target = "maniac", bool copyProtection = false)
    {
        var world = new Maniacal.Scumm.Game.GameWorld(ResourceLoader.Open(container), pcjr: !speaker);
        world.CopyProtection = copyProtection;

        // the game's own save screen saves to and loads from a folder, named as ScummVM names saves
        if (savesDir is not null)
        {
            world.SaveStore = new FileSaveStore(savesDir);
            world.SaveTarget = target;
        }

        // with a WAV to write, the run reads the sound as a host would, a step's worth after each step
        var audio = new List<short>();
        long framesRead = 0;
        void ReadAudio()
        {
            long due = (long)(world.Time.TotalSeconds * Maniacal.Scumm.Game.GameWorld.DefaultSampleRate);
            if (wav is null || due <= framesRead)
            {
                return;
            }

            var buffer = new short[(due - framesRead) * 2];
            world.Audio.Read(buffer);
            for (int i = 0; i < buffer.Length; i += 2)
            {
                audio.Add(buffer[i]);
            }

            framesRead = due;
        }

        bool stopped = false;
        try
        {
            // a load takes the place of the boot script, as ScummVM does when started on a save
            if (loadFile is not null)
            {
                var info = world.Load(File.ReadAllBytes(loadFile));
                Console.WriteLine($"Loaded \"{info.Name}\": save version {info.Version}, {info.PlayTime} played");
            }
            else
            {
                world.Boot();
            }

            // variables set after the boot stand in for input there is no way to give yet
            foreach (var (variable, value) in settings)
            {
                world.SetVariable(variable, value);
            }

            for (int step = 0; step < steps; step++)
            {
                // a skip presses the cutscene key: the cutscene's override jump is taken
                if (skips.Contains(step))
                {
                    world.SkipCutscene();
                }

                foreach (var input in inputs.Where(input => input.Step == step))
                {
                    input.Give(world);
                }

                world.Step();
                ReadAudio();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            // the report below still shows where the run got to
            Error($"stopped in script {world.CurrentScript}: {ex.Message}");
            stopped = true;
        }

        foreach (var call in world.Unhandled)
        {
            Console.WriteLine(call);
        }

        if (saveFile is not null)
        {
            File.WriteAllBytes(saveFile, world.Save(Path.GetFileNameWithoutExtension(saveFile), DateTime.Now, saveVersion));
            Console.WriteLine($"Saved to {saveFile} in save version {saveVersion}");
        }

        if (wav is not null)
        {
            File.WriteAllBytes(wav, Maniacal.Scumm.Sound.WavWriter.Encode(audio.ToArray(), Maniacal.Scumm.Game.GameWorld.DefaultSampleRate, 1));
            Console.WriteLine($"Sound written to {wav}: {audio.Count / (double)Maniacal.Scumm.Game.GameWorld.DefaultSampleRate:F1} seconds, peak {audio.DefaultIfEmpty().Max(s => Math.Abs((int)s))}");
        }

        Console.WriteLine();
        Console.WriteLine($"Room {world.CurrentRoom?.Number ?? 0}, camera at {world.Camera.X}, {world.Steps} steps, {world.Time.TotalSeconds:F1} seconds");
        Console.WriteLine("Actors here: " + string.Join(", ", world.ActorsHere()
            .Select(a => $"{a.Number} \"{a.Name}\" costume {a.Costume} at ({a.X}, {a.Y}) facing {a.Facing}{(a.Visible ? "" : " hidden")}")));
        if (screenshot is not null)
        {
            using var file = File.Create(screenshot);
            var screen = world.Screen();
            world.DrawCursor(screen);
            PngWriter.Write(file, screen, Palettes.ForVersion(world.Version), zoom, zoom);
            Console.WriteLine($"Screen written to {screenshot}");
        }

        Console.WriteLine();
        Console.WriteLine("Slots: " + string.Join(", ", world.RunningScripts()
            .Select(s => $"{s.Slot}: {s.Kind} {s.Number} at {s.Offset:X4} status {s.Status:X2}{(s.Status == SlotStatus.Paused ? $" delay {s.Delay}" : "")}")));
        Console.WriteLine("Variables: " + string.Join(", ", world.Variables.Select((v, i) => (v, i)).Where(p => p.v != 0).Select(p => $"{p.i}={p.v}")));
        return stopped ? CannotRun : Done;
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
        var inspector = new GameInspector(ResourceLoader.Open(container));

        static void Print(ScriptListing listing)
        {
            Console.WriteLine(listing.Title);
            foreach (var line in listing.Lines)
            {
                foreach (var verb in listing.Verbs.Where(v => v.Offset == line.Offset && line.Instruction is not null))
                {
                    Console.WriteLine($"  verb {verb.Verb}:");
                }

                Console.WriteLine(line.Instruction is { } text
                    ? $"    [{line.Offset:X4}] {text}"
                    : $"    [{line.Offset:X4}] {line.Length} unreachable bytes");
            }

            if (listing.Error is { } error)
            {
                Console.WriteLine($"    {error}");
            }

            Console.WriteLine();
        }

        try
        {
            if (script is { } number)
            {
                if (inspector.Script(number) is not { } listing)
                {
                    Error($"there is no script {number}");
                    return 1;
                }

                Print(listing);
            }

            if (roomNumber is { } n)
            {
                if (inspector.RoomScripts(n) is not { } listings)
                {
                    Error($"there is no room {n}");
                    return 1;
                }

                foreach (var listing in listings)
                {
                    Print(listing);
                }
            }

            return 0;
        }
        catch (InvalidDataException ex)
        {
            Error(ex.Message);
            return 3;
        }
    }

    /// <summary>Parses "costume,x,y" or "costume,x,y,direction", the position in actor units and the direction left, right, front or back.</summary>
    private static StandingActor? ParseActor(string value)
    {
        var parts = value.Split(',');
        if (parts.Length is < 3 or > 4 || !int.TryParse(parts[0], out int costume)
            || !int.TryParse(parts[1], out int x) || !int.TryParse(parts[2], out int y))
        {
            return null;
        }

        Facing? facing = parts.Length == 3 ? Facing.Front : parts[3] switch
        {
            "left" => Facing.Left,
            "right" => Facing.Right,
            "front" => Facing.Front,
            "back" => Facing.Back,
            _ => null,
        };

        return facing is { } f ? new StandingActor(costume, x, y, f) : null;
    }

    /// <summary>
    /// Writes the 320x200 screen with a room as the game starts it, the camera at the given centre
    /// (or the room's left edge), and standing actors. Version 1 colours are those of the actor with
    /// the costume's number. An actor goes behind the room mask when the walkbox under it says so.
    /// </summary>
    private static int WriteView(IGameContainer container, int number, int? cameraX, List<StandingActor> actors, int zoom, string path)
    {
        try
        {
            var inspector = new GameInspector(ResourceLoader.Open(container));
            if (inspector.View(number, cameraX, actors) is not { } view)
            {
                Error($"there is no room {number}");
                return 1;
            }

            foreach (var actor in view.Actors)
            {
                Console.WriteLine($"Costume {actor.Costume} at ({actor.X}, {actor.Y}): walkbox {actor.Box}{(actor.Behind ? ", behind the mask" : "")}");
            }

            using var file = File.Create(path);
            PngWriter.Write(file, view.Screen, Palettes.ForVersion(inspector.Version), zoom, zoom);
            Console.WriteLine($"Room {number}: camera at {view.CameraX}, view from x {view.ViewLeft}, written to {path}");
            return 0;
        }
        catch (InvalidDataException ex)
        {
            Error(ex.Message);
            return 3;
        }
        catch (KeyNotFoundException ex)
        {
            Error(ex.Message);
            return 1;
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
                Error("the font has only the 128 ASCII characters");
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
        sheet.Fill(CostumeFigures.Transparent);
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
                        if (frame.Pixels[x, y] != CostumeFigures.Transparent)
                        {
                            sheet[x0 + x, y0 + y] = frame.Pixels[x, y];
                        }
                    }
                }
            }
        }

        return sheet;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($$"""
            maniacal {{Version()}}: Maniac Mansion from the game's own files, the Commodore 64 release
            and the DOS releases of 1987 and 1988.

            Usage:
              maniacal <game folder> [options]     play in a window
              maniacal <game folder> <tool>        look inside the game, or run it headless
              maniacal --help | --version

            The game folder holds a release's files (00.LFL and the rest, or the C64's two .d64
            disk images), or holds them in a folder below it. No game files come with Maniacal.

            Playing:
              --fullscreen          start in full screen
              --scale N             the window N times 320x200, 1 to 8 (3)
              --speaker             the PC speaker's sound, not the PCjr's (DOS releases)
              --copy-protection     keep the security door's code (bypassed, as in ScummVM)
              --list                name the release found, and stop
              --verbose             log more

              In the game: click a verb, then an object; click the object again, or the sentence
              line, to carry it out. Escape, F4 or the right button skips a cutscene, the period
              skips a line of speech, and F5 opens the game's save screen.
              Saves go in {{SavesFolder}}, a folder for each game.
              They are ScummVM's own files, so they move between Maniacal and ScummVM 2.7 or later.

            Looking inside (a PNG is written to the current folder, or --output):
              --room N              a room's picture, its objects listed
                [--objects none|initial|all] [--mask]
              --costume N           a sheet of a costume's figures
              --view N              the screen with a room in view
                [--camera X] [--actor costume,x,y[,left|right|front|back]]...
              --script N            a global script decoded
              --room-scripts N      a room's scripts decoded
              --font                the built-in font
              --text "words"        words in the built-in font
              --output file.png     where to write the picture
              --zoom N              the picture N times as large, 1 to 8

            Headless runs, for testing and recording:
              --run [steps]         boot (or --load) and run the steps, then report
                --click step:x,y    --key step:key    --skip step    --set variable=value
                --load file    --save file [--save-version N]    --saves folder [--target name]
                --screenshot file.png    --wav file.wav    --zoom N
              --frames N            in the window, stop after N frames

            Exit codes: 0 done; 1 not found (folder, release, room, costume or script);
            2 a mistake on the command line; 3 the game's data could not be read or the
            window could not open.
            """);
    }
}
