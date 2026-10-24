namespace Maniacal.Scumm.Costumes;

/// <summary>
/// Where each of a C64 actor's 8 limbs is in its costume, and the clip that set them. Mirrors the
/// parts of ScummVM's <c>CostumeData</c> version 0 uses and the costume fields of <c>Actor_v0</c>,
/// with <c>V0CostumeLoader::costumeDecodeData</c> (starting a clip), <c>Actor_v0::limbFrameCheck</c>
/// and <c>V0CostumeLoader::increaseAnims</c> (one animation step).
/// </summary>
/// <remarks>
/// A clip sets each limb's sequence (<see cref="Frame"/>); the next step starts it, from its first
/// frame (<see cref="Position"/>, counted from <see cref="AnimType"/>). At a sequence's end the limb
/// starts it again as many times as the clip's repeat says, -1 for ever; then it stays on its last
/// frame and the clip is done.
/// </remarks>
public sealed class V0CostumeState
{
    /// <summary>No position, sequence or clip (<c>CostumeData::reset</c>'s 0xFFFF, and the 0xFF of <c>_costCommand</c>).</summary>
    public const int None = 0xFFFF, NoClip = 0xFF;

    private readonly int[] _position = new int[V0Costume.Limbs];
    private readonly int[] _start = new int[V0Costume.Limbs];
    private readonly int[] _frame = new int[V0Costume.Limbs];
    private readonly byte[] _animType = new byte[V0Costume.Limbs];
    private readonly bool[] _flipped = new bool[V0Costume.Limbs];
    private readonly sbyte[] _repeat = new sbyte[V0Costume.Limbs];
    private readonly sbyte[] _repeatNew = new sbyte[V0Costume.Limbs];

    public V0CostumeState() => Reset();

    /// <summary>The clip the limbs were last set from (<c>_costCommand</c>).</summary>
    public int Clip { get; private set; } = NoClip;

    /// <summary>The clip asked for, which <see cref="Decode"/> sets the limbs from (<c>_costCommandNew</c>).</summary>
    public int NewClip { get; set; } = NoClip;

    /// <summary>How many times the next clip's sequences play again, -1 for ever (<c>_animFrameRepeat</c>).</summary>
    public sbyte Repeat { get; set; }

    /// <summary>A limb's frame, counted from <see cref="AnimType"/>, or <see cref="None"/> (<c>curpos</c>).</summary>
    public int Position(int limb) => _position[limb];

    /// <summary>Where a limb's sequence starts in the sequences table (<c>animType</c>).</summary>
    public int AnimType(int limb) => _animType[limb];

    /// <summary>The sequence a clip set on a limb, or <see cref="None"/> (<c>frame</c>).</summary>
    public int Frame(int limb) => _frame[limb];

    /// <summary>Whether a limb is drawn mirrored (<c>_limb_flipped</c>).</summary>
    public bool Flipped(int limb) => _flipped[limb];

    /// <summary>Mirrors <c>CostumeData::reset</c>: no positions, starts or sequences.</summary>
    public void Reset()
    {
        Array.Fill(_position, None);
        Array.Fill(_start, None);
        Array.Fill(_frame, None);
        Array.Clear(_animType);
    }

    /// <summary>A shown actor's clip and repeats start afresh (<c>showActor</c> for version 0).</summary>
    public void Restart()
    {
        Clip = NewClip = NoClip;
        Array.Clear(_repeat);
        Array.Clear(_repeatNew);
        Reset();
        Repeat = 1;
    }

    /// <summary>
    /// Sets the limbs from <see cref="NewClip"/>, unless it is none or already the clip: each limb
    /// the clip names gets its sequence, whether it is mirrored, and the repeat; a limb whose mirroring
    /// changes starts its sequence again. Mirrors <c>V0CostumeLoader::costumeDecodeData</c>.
    /// </summary>
    public void Decode(V0Costume costume)
    {
        if (NewClip == NoClip || Clip == NewClip)
        {
            return;
        }

        Clip = NewClip;
        for (int limb = 0; limb < V0Costume.Limbs; limb++)
        {
            byte sequence = costume.Clip(Clip, limb);
            if (sequence == V0Costume.End)
            {
                continue;
            }

            bool flipped = (sequence & 0x80) != 0;
            _frame[limb] = sequence & 0x7F;
            if (_flipped[limb] != flipped)
            {
                _start[limb] = None;
            }

            _flipped[limb] = flipped;
            _repeatNew[limb] = Repeat;
        }
    }

    /// <summary>One animation step of every limb; true when some limb moved. Mirrors <c>V0CostumeLoader::increaseAnims</c>.</summary>
    public bool Increase(V0Costume costume)
    {
        bool changed = false;
        for (int limb = 0; limb < V0Costume.Limbs; limb++)
        {
            changed |= StartSequence(costume, limb);
            changed |= Increase(costume, limb);
        }

        return changed;
    }

    /// <summary>
    /// A limb with a new sequence starts it, from its first frame. Mirrors <c>Actor_v0::limbFrameCheck</c>
    /// and <c>V0CostumeLoader::getFrame</c>; true when the limb is to be drawn again (<c>_needRedraw</c>).
    /// </summary>
    private bool StartSequence(V0Costume costume, int limb)
    {
        if (_frame[limb] == None || _start[limb] == _frame[limb])
        {
            return false;
        }

        _start[limb] = _frame[limb];
        _repeat[limb] = _repeatNew[limb];
        _animType[limb] = costume.Sequence(costume.Sequence(limb) + _start[limb]);
        _position[limb] = 0;
        return true;
    }

    /// <summary>
    /// A limb's next frame. At the sequence's end it starts again while it has repeats; with none
    /// left it stays on its last frame, its sequence and the clip done. Mirrors <c>V0CostumeLoader::increaseAnim</c>,
    /// positions counting as 16 bits do.
    /// </summary>
    private bool Increase(V0Costume costume, int limb)
    {
        int previous = _position[limb];
        _position[limb] = (_position[limb] + 1) & 0xFFFF;
        if (costume.Sequence(_position[limb] + _animType[limb]) == V0Costume.End)
        {
            if (_repeat[limb] == 0)
            {
                _position[limb] = (_position[limb] - 1) & 0xFFFF;
                NewClip = Clip = NoClip;
                _frame[limb] = None;
                _start[limb] = None;
            }
            else
            {
                if (_repeat[limb] != -1)
                {
                    _repeat[limb]--;
                }

                _position[limb] = 0;
            }
        }

        return previous != _position[limb];
    }
}
