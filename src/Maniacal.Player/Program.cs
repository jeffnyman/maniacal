using Daventry.Core.Graphics;
using Daventry.Core.Hosting;
using Daventry.Core.Storage;
using Daventry.Host.Sdl;
using Maniacal.Scumm;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;

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
        int? room = null;
        bool mask = false;
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
                case "--scale" when i + 1 < args.Length && int.TryParse(args[i + 1], out scale):
                    i++;
                    break;
                case "--room" when i + 1 < args.Length && int.TryParse(args[i + 1], out int number):
                    room = number;
                    i++;
                    break;
                case "--mask":
                    mask = true;
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

        if (room is { } roomNumber)
        {
            return WriteRoom(container, roomNumber, mask, output ?? $"room-{roomNumber:D2}{(mask ? "-mask" : "")}.png");
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
            host.Run(engine);
        }

        return 0;
    }

    /// <summary>Writes a room's background picture, or its mask in black and white, as a PNG.</summary>
    private static int WriteRoom(IGameContainer container, int number, bool mask, string path)
    {
        try
        {
            if (ResourceLoader.Open(container).LoadRoom(number) is not { } room)
            {
                Console.Error.WriteLine($"There is no room {number}");
                return 1;
            }

            var image = RoomBackground.Decode(room);
            using var file = File.Create(path);
            if (mask)
            {
                PngWriter.Write(file, image.Mask, [Rgb.Black, Rgb.White]);
            }
            else
            {
                PngWriter.Write(file, image.Pixels, Palettes.Ega);
            }

            Console.WriteLine($"Room {number}: {room.Width}x{room.Height}, {room.Objects.Count} objects, written to {path}");
            return 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("usage: maniacal <game folder> [--list] [--scale N] [--fullscreen] [--verbose]");
        Console.Error.WriteLine("       maniacal <game folder> --room N [--mask] [--output file.png]");
    }
}
