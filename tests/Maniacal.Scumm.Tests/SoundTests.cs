using Daventry.Core.Hosting;
using Daventry.Core.Storage;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Sound;

namespace Maniacal.Scumm.Tests;

public class PlayerV2Tests
{
    private const int Rate = 22050;

    /// <summary>
    /// A version 2 sound of one note on the first channel, for both devices: the 4-byte header, the
    /// priority and whether it may be resumed, the four speaker programs, the four PCjr programs,
    /// then the program: set the tempo and a loud hull (6, starting at 60000), play the note, then
    /// set the time left to 0, which ends it.
    /// </summary>
    private static byte[] OneNote(int priority, bool restartable, int note = 24, int lengthCode = 12, int tempo = 4)
    {
        const int Program = 22;
        return
        [
            0, 0, 0, 0, (byte)priority, (byte)(restartable ? 1 : 0),
            Program, 0, 0, 0, 0, 0, 0, 0,
            Program, 0, 0, 0, 0, 0, 0, 0,
            0xFF, 14, (byte)tempo, 0,
            0xF8, 12,
            (byte)lengthCode, (byte)(0x80 | note),
            0xFF, 0, 0, 0,
        ];
    }

    private static double Rms(short[] samples) => Math.Sqrt(samples.Select(s => (double)s * s).Average());

    private static short[] Render(SpeakerSynth player, double seconds)
    {
        var stereo = new short[(int)(Rate * seconds) * 2];
        player.Read(stereo);
        return [.. stereo.Where((_, i) => i % 2 == 0)];
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_note_sounds_for_its_length_and_then_the_sound_ends(bool pcjr)
    {
        var player = new PlayerV2(Rate, pcjr);

        // length code 12 is 16 units; at tempo 4 that is 64 ticks of 236 a second
        player.StartSound(5, OneNote(1, false));

        Assert.True(player.IsPlaying(5));
        Assert.True(Rms(Render(player, 0.2)) > 100);
        Render(player, 0.2);
        Assert.False(player.IsPlaying(5));
    }

    /// <summary>Note 24 is C two octaves up: the speaker's divisor 36484 shifted twice, 1193000 / 9121, about 131 Hz.</summary>
    [Fact]
    public void The_speaker_plays_the_notes_pitch()
    {
        var player = new PlayerV2(Rate, pcjr: false);
        player.StartSound(5, OneNote(1, false, tempo: 20));

        var samples = Render(player, 1.0);
        int crossings = samples.Zip(samples.Skip(1)).Count(p => (p.First < 0) != (p.Second < 0));

        Assert.InRange(crossings / 2.0, 125, 137);
    }

    [Fact]
    public void A_higher_priority_interrupts_and_the_interrupted_sound_resumes_after_if_it_may()
    {
        var player = new PlayerV2(Rate, pcjr: false);
        player.StartSound(1, OneNote(1, restartable: true));
        player.StartSound(2, OneNote(5, restartable: false));

        Assert.True(player.IsPlaying(1));
        Assert.True(player.IsPlaying(2));

        player.StopSound(2);
        Assert.True(player.IsPlaying(1));
        Assert.False(player.IsPlaying(2));
    }

    [Fact]
    public void A_lower_priority_waits_if_it_may_be_resumed_and_is_dropped_if_not()
    {
        var player = new PlayerV2(Rate, pcjr: false);
        player.StartSound(1, OneNote(5, restartable: false));

        player.StartSound(2, OneNote(1, restartable: false));
        Assert.False(player.IsPlaying(2));

        player.StartSound(3, OneNote(1, restartable: true));
        Assert.True(player.IsPlaying(3));

        player.StopAllSounds();
        Assert.False(player.IsPlaying(1) || player.IsPlaying(3));
    }
}

/// <summary>The game's own sounds, in both releases.</summary>
public class GameSoundTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    [Theory]
    [InlineData(V1Folder, false)]
    [InlineData(V1Folder, true)]
    [InlineData(V2Folder, false)]
    [InlineData(V2Folder, true)]
    public void Nearly_every_sound_resource_makes_a_sound(string folder, bool pcjr)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        int played = 0, sounding = 0;
        for (int n = 1; n < loader.Index.Count(ResourceType.Sound); n++)
        {
            // some index entries point past their room file: they are no sound
            ReadOnlyMemory<byte>? data;
            try
            {
                data = loader.Index.Locate(ResourceType.Sound, n).IsPresent ? loader.Load(ResourceType.Sound, n) : null;
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (data is null)
            {
                continue;
            }

            SpeakerSynth player = loader.Index.Version <= 1 ? new PlayerV1(22050, pcjr) : new PlayerV2(22050, pcjr);
            player.StartSound(n, data.Value.ToArray());
            var stereo = new short[22050 * 4];
            player.Read(stereo);
            played++;
            sounding += stereo.Any(s => s != 0) ? 1 : 0;
        }

        Assert.True(played > 40, $"{played} sounds");
        Assert.True(sounding > played * 3 / 4, $"{sounding} of {played} sounds make a sound in their first two seconds");
    }

    /// <summary>The intro's theme, sound 58 of the 1988 release, starts after the kids are picked, and its programs drive the music timer.</summary>
    [Fact]
    public void The_intro_plays_its_theme()
    {
        if (GameFolders.Find(V2Folder) is not { } path)
        {
            return;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Boot();
        for (int step = 0; step < 700; step++)
        {
            switch (step)
            {
                case 300:
                    world.Click(82, 118);
                    break;
                case 400:
                    world.Click(200, 118);
                    break;
                case 500:
                    world.Click(290, 75);
                    break;
            }

            world.Step();
        }

        Assert.True(world.Sound.IsRunning(58));
        Assert.Equal(58, world.Engine.Variables[GameWorld.VarLastSound]);
        Assert.True(world.Engine.Variables[GameWorld.VarMusicTimer] > 0);
    }

    /// <summary>The engine on a host plays the world's sound.</summary>
    [Fact]
    public void The_engine_gives_the_host_its_sound()
    {
        if (GameFolders.Find(V2Folder) is not { } path)
        {
            return;
        }

        var container = new DirectoryGameContainer(path);
        var provider = new ScummEngineProvider();
        var host = new HeadlessHost();
        using var engine = (ScummEngine)provider.Create(host, container, provider.Detect(container)[0]);
        engine.Initialize();
        host.Run(engine, 2);

        Assert.Same(engine.World.Sound.Output, host.Audio.Source);
    }
}
