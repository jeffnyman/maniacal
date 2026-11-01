using System.Buffers.Binary;

namespace Maniacal.Scumm.Sound;

/// <summary>
/// The Amiga release's sound: each sound known by the CRC of its data and played by the class
/// ScummVM wrote for it, on the Amiga's sample channels (<see cref="AmigaMixer"/>), in stereo,
/// eight at once, stepped 60 times a second among the samples. A sound the table does not know
/// is not played. Mirrors ScummVM's <c>Player_V2A</c>.
/// </summary>
internal sealed class PlayerV2a : MusicPlayer
{
    private const int Slots = 8, UpdatesPerSecond = 60;

    // where a sound's data starts that its CRC is taken of, and the word that says how long it is
    private const int CrcLengthAt = 0x08, CrcStart = 0x0A;

    private static readonly uint[] CrcTable = MakeCrcTable();

    private static readonly Dictionary<uint, V2aSoundEntry> ByCrc = V2aSoundTables.Sounds.ToDictionary(s => s.Crc);

    private readonly object _sync = new();
    private readonly AmigaMixer _mixer;
    private readonly (int Id, V2aSound? Sound)[] _slots = new (int, V2aSound?)[Slots];
    private short[] _stereo = [];

    public PlayerV2a(int sampleRate)
        : base(sampleRate)
    {
        _mixer = new AmigaMixer(sampleRate);
        _mixer.SetUpdate(UpdateSounds, UpdatesPerSecond);
    }

    /// <summary>The Amiga's songs keep no timer for the scripts (<c>getMusicTimer</c>).</summary>
    public override int MusicTimer => 0;

    /// <summary>
    /// The CRC of a sound's data, as the table knows it: of its bytes from 0x0A, as many as the
    /// big-endian word at 0x08 says. Mirrors <c>GetCRC</c> on <c>startSound</c>'s data.
    /// </summary>
    internal static uint Crc(byte[] data)
    {
        if (data.Length < CrcStart)
        {
            return 0;
        }

        int length = Math.Min(BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(CrcLengthAt)), data.Length - CrcStart);
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data.AsSpan(CrcStart, length))
        {
            crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
        }

        return crc ^ 0xFFFFFFFF;
    }

    /// <summary>Whether the table knows a sound's data.</summary>
    internal static bool Knows(byte[] data) => ByCrc.ContainsKey(Crc(data));

    /// <summary>Starts a sound the table knows, stopping it first if it plays, in a free slot. Mirrors <c>startSound</c>.</summary>
    public override void StartSound(int number, byte[] data)
    {
        if (!ByCrc.TryGetValue(Crc(data), out var entry))
        {
            return;
        }

        lock (_sync)
        {
            StopLocked(number);
            int slot = Array.FindIndex(_slots, s => s.Id == 0);
            if (slot < 0)
            {
                return;
            }

            var sound = V2aSound.Create(entry);
            _slots[slot] = (number, sound);
            sound.Start(_mixer, number, data);
        }
    }

    /// <summary>Mirrors <c>stopSound</c>.</summary>
    public override void StopSound(int number)
    {
        if (number == 0)
        {
            return;
        }

        lock (_sync)
        {
            StopLocked(number);
        }
    }

    private void StopLocked(int number)
    {
        int slot = Array.FindIndex(_slots, s => s.Id == number);
        if (slot >= 0)
        {
            _slots[slot].Sound!.Stop();
            _slots[slot] = (0, null);
        }
    }

    /// <summary>Mirrors <c>stopAllSounds</c>.</summary>
    public override void StopAllSounds()
    {
        lock (_sync)
        {
            for (int i = 0; i < Slots; i++)
            {
                if (_slots[i].Id != 0)
                {
                    _slots[i].Sound!.Stop();
                    _slots[i] = (0, null);
                }
            }
        }
    }

    /// <summary>Mirrors <c>getSoundStatus</c>.</summary>
    public override bool IsPlaying(int number)
    {
        lock (_sync)
        {
            return Array.Exists(_slots, s => s.Id == number && number != 0);
        }
    }

    // every sound a 60th of a second on, those done stopped (updateSound)
    private void UpdateSounds()
    {
        for (int i = 0; i < Slots; i++)
        {
            if (_slots[i].Id != 0 && !_slots[i].Sound!.Update())
            {
                _slots[i].Sound!.Stop();
                _slots[i] = (0, null);
            }
        }
    }

    internal override void RenderStereo(Span<short> interleavedStereo)
    {
        lock (_sync)
        {
            _mixer.Mix(interleavedStereo);
        }
    }

    // the two sides as one, for a caller that asks for mono
    protected override void RenderMono(Span<short> buffer)
    {
        if (_stereo.Length < buffer.Length * 2)
        {
            _stereo = new short[buffer.Length * 2];
        }

        var stereo = _stereo.AsSpan(0, buffer.Length * 2);
        RenderStereo(stereo);
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (short)((stereo[2 * i] + stereo[2 * i + 1]) / 2);
        }
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint n = i;
            for (int j = 0; j < 8; j++)
            {
                n = (n & 1) != 0 ? (n >> 1) ^ 0xEDB88320 : n >> 1;
            }

            table[i] = n;
        }

        return table;
    }
}
