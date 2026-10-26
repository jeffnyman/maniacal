// Plays a Maniac Mansion release headless from the Maniacal.Scumm package, as a stranger would
// use it: ConsoleSmoke <game folder> <steps> <screenshot.png>
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

if (args.Length != 3 || !int.TryParse(args[1], out int steps))
{
    Console.Error.WriteLine("usage: ConsoleSmoke <game folder> <steps> <screenshot.png>");
    return 2;
}

var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(args[0])));
world.Boot();
for (int i = 0; i < steps; i++)
{
    world.Step();
}

var screen = world.Screen();
using (var file = File.Create(args[2]))
{
    PngWriter.Write(file, screen, Palettes.ForVersion(world.Version));
}

// DOS's resource routines only manage memory, and are left undone
int lit = screen.Pixels.Count(p => p != 0);
int unhandled = world.Unhandled.Distinct().Count(u => !u.Contains("resourceRoutines", StringComparison.Ordinal));
Console.WriteLine($"version {world.Version}, room {world.CurrentRoom?.Number}, {lit} of {screen.Pixels.Length} pixels lit after {steps} steps, {unhandled} instructions unhandled");
return lit > 0 ? 0 : 1;
