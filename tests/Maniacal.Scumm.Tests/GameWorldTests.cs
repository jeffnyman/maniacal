using Daventry.Core.Storage;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;

namespace Maniacal.Scumm.Tests;

public class GameWorldTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";
    private const string C64Folder = "maniac_mansion_c64";

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

    /// <summary>
    /// The C64 boots to the same title room: its boot script sets the keypad mode for the kid
    /// selection and prints the copyright on the message line. Version 0 keeps the lights in the
    /// engine, not a variable, and every instruction on the way is one it handles.
    /// </summary>
    [Fact]
    public void The_c64_boots_to_the_kid_selection()
    {
        if (Open(C64Folder) is not { } world)
        {
            return;
        }

        world.Boot();
        for (int i = 0; i < 60; i++)
        {
            world.Step();
        }

        Assert.Equal(0, world.Version);
        Assert.Equal(45, world.CurrentRoom?.Number);
        Assert.Equal(45, world.Engine.Variables[ScriptEngine.VarRoom]);
        Assert.Equal(11, world.CurrentLights);
        Assert.Equal(GameWorld.ModeKeypad, world.CurrentMode);
        Assert.True(world.CursorShown);
        Assert.Contains("Lucasfilm Ltd. All rights reserved", world.Messages.Plain());
        Assert.Empty(world.Unhandled);

        // the C64 has no variables for the mouse in the room: 30 and 31 are the scripts' own
        Assert.Equal((0, 0), (world.Engine.Variables[30], world.Engine.Variables[31]));
        // 4 ticks a step, of 4 quarter-frames at 240 a second: 60 steps are 4 seconds
        Assert.Equal(4.0, world.Time.TotalSeconds, 3);
    }

    /// <summary>Pinned after checking the screens by eye: the title, START and the seven kids, after 60 steps with seed 1.</summary>
    [Theory]
    [InlineData(V1Folder, "09c1f7ddfc8ee22b22a3126cebd04151")]
    [InlineData(V2Folder, "94c5b94df4c443508a7ad9c8ad5bacbd")]
    [InlineData(C64Folder, "f837caf9ecdc9ae403db05f9df376c2d")]
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
