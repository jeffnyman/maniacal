using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>What the library promises about threads: worlds share nothing, and a world's sound may be read from another thread.</summary>
public class ThreadingTests
{
    private static string Played(Game.GameWorld world, int steps)
    {
        world.Boot();
        for (int i = 0; i < steps; i++)
        {
            world.Step();
        }

        return Md5.ComputeHex(world.Screen().Pixels);
    }

    /// <summary>Worlds from one shared release, stepped on threads at once, each reach the screen one world alone does.</summary>
    [Fact]
    public void Worlds_on_separate_threads_do_not_disturb_each_other()
    {
        if (GameFolders.Find(WalkthroughTests.V2) is not { } path)
        {
            return;
        }

        var game = ManiacMansion.Open(path);
        string alone = Played(game.Play(), 600);

        var screens = new string[4];
        Parallel.For(0, screens.Length, i => screens[i] = Played(game.Play(), 600));

        Assert.All(screens, screen => Assert.Equal(alone, screen));
    }

    /// <summary>
    /// A thread of its own reading the sound, as an audio device's callback does, while the game
    /// steps on this one; after each step the game waits for a read, so the two do interleave
    /// whatever else the machine is busy with.
    /// </summary>
    [Fact]
    public void The_sound_is_read_from_another_thread_while_the_game_steps()
    {
        if (GameFolders.Find(WalkthroughTests.V2) is not { } path)
        {
            return;
        }

        var world = ManiacMansion.Open(path).Play();
        world.Boot();
        int reads = 0, peak = 0;
        bool done = false;
        var reader = new Thread(() =>
        {
            var buffer = new short[512];
            while (!Volatile.Read(ref done))
            {
                world.Audio.Read(buffer);
                Volatile.Write(ref peak, Math.Max(Volatile.Read(ref peak), buffer.Max(s => Math.Abs((int)s))));
                Interlocked.Increment(ref reads);
            }
        }) { IsBackground = true };
        reader.Start();

        // the kid selection clicked through, so the intro's music plays
        for (int step = 0; step < 1200; step++)
        {
            switch (step)
            {
                case 300: world.Click(82, 118); break;
                case 310: world.Click(200, 118); break;
                case 320: world.Click(290, 75); break;
            }

            world.Step();
            int before = Volatile.Read(ref reads);
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref reads) > before, TimeSpan.FromSeconds(5)));
        }

        Volatile.Write(ref done, true);
        Assert.True(reader.Join(TimeSpan.FromSeconds(5)));

        Assert.True(peak > 0);
        Assert.NotEqual(45, world.CurrentRoom?.Number);
    }
}
