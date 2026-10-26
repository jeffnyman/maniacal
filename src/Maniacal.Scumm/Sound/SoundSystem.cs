
namespace Maniacal.Scumm.Sound;

/// <summary>
/// The sounds the scripts ask for: queued during a step and handed to the player at its end, as
/// ScummVM's <c>Sound</c> does for these versions (<c>startSound</c>, <c>addSoundToQueue</c>,
/// <c>processSoundQueues</c>, <c>triggerSound</c>, <c>stopSound</c>, <c>stopAllSounds</c> and
/// <c>isSoundRunning</c>). The player is version 1's or version 2's, on the PCjr by default, as
/// ScummVM prefers it when a release supports both, or the C64's SID player.
/// </summary>
public sealed class SoundSystem
{
    private readonly Func<int, byte[]?> _load;
    private readonly List<int> _queue = [];

    public SoundSystem(int version, Func<int, byte[]?> load, int sampleRate, bool pcjr = true)
    {
        _load = load;

        // ScummVM gives Player_V1 to version 1 of Maniac Mansion only, and Player_SID to the C64
        Player = version switch
        {
            0 => new PlayerSid(sampleRate),
            1 => new PlayerV1(sampleRate, pcjr),
            _ => new PlayerV2(sampleRate, pcjr),
        };
        Output = new ClockedSource(Player, sampleRate);
    }

    public MusicPlayer Player { get; }

    /// <summary>What a host plays; it renders the sound itself in step with the game when the host reads none.</summary>
    public ClockedSource Output { get; }

    /// <summary>The last sound asked for. Mirrors <c>_lastSound</c>.</summary>
    public int LastSound { get; private set; }

    /// <summary>The sounds started this step, waiting for its end.</summary>
    public IReadOnlyList<int> Queue => _queue;

    /// <summary>Mirrors <c>Sound::startSound</c>: the sound waits in the queue until the step's end.</summary>
    public void Start(int sound)
    {
        LastSound = sound;
        _queue.Add(sound);
    }

    /// <summary>The queued sounds go to the player, the last asked for first. Mirrors <c>processSoundQueues</c> and <c>triggerSound</c>.</summary>
    public void ProcessQueue()
    {
        for (int i = _queue.Count - 1; i >= 0; i--)
        {
            int sound = _queue[i];
            if (sound != 0 && _load(sound) is { } data)
            {
                Player.StartSound(sound, data);
            }
        }

        _queue.Clear();
    }

    /// <summary>Mirrors <c>Sound::stopSound</c>: the sound stops, and leaves the queue.</summary>
    public void Stop(int sound)
    {
        Player.StopSound(sound);
        _queue.RemoveAll(s => s == sound);
    }

    /// <summary>Mirrors <c>Sound::stopAllSounds</c>, which <c>stopMusic</c> calls.</summary>
    public void StopAll()
    {
        LastSound = 0;
        _queue.Clear();
        Player.StopAllSounds();
    }

    /// <summary>Whether a sound waits in the queue or in the player, or plays. Mirrors <c>Sound::isSoundRunning</c>.</summary>
    public bool IsRunning(int sound) => _queue.Contains(sound) || Player.IsPlaying(sound);

    /// <summary>Keeps the sound going in step with the game when the host plays none. Called once a step.</summary>
    public void Update(TimeSpan gameTime) => Output.Update(gameTime);
}
