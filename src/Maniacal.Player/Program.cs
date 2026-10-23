using Daventry.Core.Graphics;
using Daventry.Core.Hosting;
using Daventry.Core.Storage;
using Daventry.Host.Sdl;
using Maniacal.Scumm;
using Maniacal.Scumm.Costumes;
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
        int? costume = null;
        int zoom = 1;
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
            host.Run(engine);
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
        Console.Error.WriteLine("usage: maniacal <game folder> [--list] [--scale N] [--fullscreen] [--verbose]");
        Console.Error.WriteLine("       maniacal <game folder> --room N [--objects none|initial|all] [--mask] [--zoom N] [--output file.png]");
        Console.Error.WriteLine("       maniacal <game folder> --costume N [--zoom N] [--output file.png]");
    }
}
