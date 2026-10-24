using Daventry.Core.Storage;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;

namespace Maniacal.Scumm.Tests;

public class GameWorldTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    private static GameWorld? Open(string folder)
        => GameFolders.Find(folder) is { } path ? new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;

    /// <summary>
    /// The boot script loads room 45, the title screen; its entry script turns the lights to 11.
    /// The room is the screen's width, so the camera's limits are both 160.
    /// </summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Booting_shows_the_title_room(string folder)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.Boot();
        for (int i = 0; i < 60; i++)
        {
            world.Step();
        }

        Assert.Equal(45, world.CurrentRoom?.Number);
        Assert.Equal(45, world.Engine.Variables[ScriptEngine.VarRoom]);
        Assert.Equal(11, world.Engine.Variables[GameWorld.VarCurrentLights]);
        Assert.Equal((160, 160), (world.Engine.Variables[GameWorld.VarCameraMinX], world.Engine.Variables[GameWorld.VarCameraMaxX]));
        Assert.Equal(160, world.Camera.X);
    }

    /// <summary>Pinned after checking the screens by eye: the title, START and the seven kids, after 60 steps with seed 1.</summary>
    [Theory]
    [InlineData(V1Folder, "21eb3e754d23cd738783c0d2e40a04a4")]
    [InlineData(V2Folder, "a7588ef2fdfe8fb44fb02ff190564522")]
    public void The_title_screen_is_stable(string folder, string md5)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.Boot();
        for (int i = 0; i < 60; i++)
        {
            world.Step();
        }

        var screen = world.Screen();
        Assert.Contains(screen.Pixels, pixel => pixel != 0);
        Assert.Equal(md5, Md5.ComputeHex(screen.Pixels));
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Changing_room_kills_its_scripts_and_clamps_the_camera(string folder)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.StartScene(1);
        world.Camera.SetAtEx(2000);
        world.Step();
        Assert.Equal(world.CurrentRoom!.Width - 160, world.Camera.X);

        // an object script of room 1 is running when the room changes
        var door = world.CurrentRoom.FindObject(61)!;
        world.Engine.RunObjectScript(61, door.Code, door.FindVerb(1)!.Value);
        world.StartScene(45);

        Assert.DoesNotContain(world.Engine.Slots, s => s.Kind == ScriptKind.Room && !s.IsDead);
        Assert.Equal(45, world.Engine.Variables[ScriptEngine.VarRoom]);
        Assert.Equal(160, world.Camera.X);
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Room_0_is_no_room(string folder)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.StartScene(45);
        world.StartScene(0);

        Assert.Null(world.CurrentRoom);
        Assert.All(world.Screen().Pixels, pixel => Assert.Equal(0, pixel));
    }
}
