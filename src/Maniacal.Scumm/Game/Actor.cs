using Maniacal.Scumm.Costumes;

namespace Maniacal.Scumm.Game;

/// <summary>
/// A character of versions 1 and 2: its costume, where it is, which way it faces and where its
/// limbs are in their animations. Positions are in actor units: 8-pixel columns and 2-pixel lines.
/// Mirrors ScummVM's <c>Actor</c> and <c>Actor_v2</c> (<c>initActor</c>, <c>putActor</c>,
/// <c>showActor</c>, <c>hideActor</c>, <c>setActorCostume</c>, <c>animateActor</c>,
/// <c>startAnimActor</c>, <c>setDirection</c>, <c>animateCostume</c>) without walking, which comes
/// with the walkboxes.
/// </summary>
public sealed class Actor
{
    private readonly Func<int, Costume?> _loadCostume;
    private readonly Func<int> _currentRoom;

    public Actor(int number, Func<int, Costume?> loadCostume, Func<int> currentRoom)
    {
        Number = number;
        _loadCostume = loadCostume;
        _currentRoom = currentRoom;
        for (int i = 0; i < Palette.Length; i++)
        {
            Palette[i] = (byte)i;
        }
    }

    public int Number { get; }

    public int Room { get; set; }

    public int X { get; private set; }

    public int Y { get; private set; }

    /// <summary>The facing in degrees: 0 back, 90 right, 180 front, 270 left. <c>initActor</c> starts at 180.</summary>
    public int Facing { get; private set; } = 180;

    public int CostumeNumber { get; private set; }

    /// <summary>The costume, loaded when first needed: setting a costume loads nothing until the actor shows, as in ScummVM.</summary>
    public Costume? Costume
    {
        get
        {
            if (!_costumeLoaded)
            {
                _costume = CostumeNumber == 0 ? null : _loadCostume(CostumeNumber);
                _costumeLoaded = true;
            }

            return _costume;
        }
    }

    private Costume? _costume;
    private bool _costumeLoaded = true;

    public string Name { get; set; } = string.Empty;

    /// <summary>Mirrors <c>initActor</c>'s 15.</summary>
    public int TalkColor { get; set; } = 15;

    public int Sound { get; set; }

    /// <summary>Height above the ground, in 2-pixel lines.</summary>
    public int Elevation { get; set; }

    /// <summary>Drawn in its room. Mirrors <c>_visible</c>.</summary>
    public bool Visible { get; private set; }

    /// <summary>The frame last started. Mirrors <c>_frame</c>.</summary>
    public int Frame { get; private set; }

    /// <summary>Frames between animation steps; 0 steps every frame. Mirrors <c>_animSpeed</c>.</summary>
    public int AnimSpeed { get; set; }

    /// <summary>The version 2 colours of the costume, by colour number. Mirrors <c>_palette</c>.</summary>
    public byte[] Palette { get; } = new byte[16];

    public CostumeState State { get; } = new();

    public bool InCurrentRoom => Room != 0 && Room == _currentRoom();

    /// <summary>The direction the costume's animations use: 0 left, 1 right, 2 front, 3 back. Mirrors <c>newDirToOldDir</c>.</summary>
    public int OldDirection => NewDirToOldDir(Facing);

    private int _animProgress;

    public static int NewDirToOldDir(int facing) => facing switch
    {
        >= 71 and <= 109 => 1,
        >= 109 and <= 251 => 2,
        >= 251 and <= 289 => 0,
        _ => 3,
    };

    public static int OldDirToNewDir(int direction) => direction switch
    {
        0 => 270,
        1 => 90,
        2 => 180,
        _ => 0,
    };

    /// <summary>
    /// Moves the actor, in actor units, into a room (its own when not given). It shows when it
    /// comes into the current room and hides when it leaves. Version 1 actors face front after
    /// being put. Mirrors <c>putActor</c> without <c>adjustActorPos</c>, which comes with the walkboxes.
    /// </summary>
    public void Put(int x, int y, int? room = null, int version = 2)
    {
        X = x;
        Y = y;
        Room = room ?? Room;

        if (Visible)
        {
            if (!InCurrentRoom)
            {
                Hide();
            }
        }
        else if (InCurrentRoom)
        {
            Show();
        }

        if (version <= 1)
        {
            SetDirection(180);
        }
    }

    /// <summary>Mirrors <c>showActor</c> for versions 1 and 2: the stand, init and talk-stop frames, in that order.</summary>
    public void Show()
    {
        if (_currentRoom() == 0 || Visible)
        {
            return;
        }

        State.Reset();
        StartAnim(CostumeRenderer.StandFrame);
        StartAnim(CostumeRenderer.InitFrame);
        StartAnim(CostumeRenderer.TalkStopFrame);
        Visible = true;
    }

    public void Hide() => Visible = false;

    /// <summary>Mirrors <c>setActorCostume</c> for these games: shown again with the new costume when it shows.</summary>
    public void SetCostume(int costume)
    {
        if (Visible)
        {
            Hide();
            State.Reset();
            LoadCostume(costume);
            Show();
        }
        else
        {
            LoadCostume(costume);
            State.Reset();
        }
    }

    /// <summary>
    /// What the scripts' <c>animateActor</c> asks: <c>frame * 4 + direction</c>, with frames 0x3F,
    /// 0x3E and 0x3D meaning stop walking, face the direction now, and turn to it. Mirrors
    /// <c>animateActor</c>; turning is immediate until the walking code turns actors gradually.
    /// </summary>
    public void Animate(int animation)
    {
        int chore = 0x3F - (animation >> 2) + 2;
        int direction = OldDirToNewDir(animation & 3);
        switch (chore)
        {
            case 2:
                StartAnim(CostumeRenderer.StandFrame);
                break;
            case 3 or 4:
                SetDirection(direction);
                break;
            default:
                StartAnim(animation >> 2);
                break;
        }
    }

    /// <summary>Starts a frame in the current direction, when the actor is in the current room. Mirrors <c>startAnimActor</c>.</summary>
    public void StartAnim(int frame)
    {
        if (InCurrentRoom && Costume is { } costume)
        {
            _animProgress = 0;
            State.Decode(costume, OldDirection + frame * 4);
            Frame = frame;
        }
    }

    /// <summary>
    /// Turns to a facing. Every limb whose animation is for another direction, or another frame, is
    /// started again in the new direction. Mirrors <c>setDirection</c> for versions 1 and 2.
    /// </summary>
    public void SetDirection(int direction)
    {
        direction = (direction + 360) % 360;
        if (Facing == direction)
        {
            return;
        }

        Facing = direction;
        if (Costume is not { } costume)
        {
            return;
        }

        int oldDirection = OldDirection;
        for (int limb = 0; limb < CostumeState.Limbs; limb++)
        {
            int animation = State.Animation(limb);
            if (animation == CostumeState.None)
            {
                continue;
            }

            if ((animation & 3) == oldDirection && (animation >> 2) == Frame)
            {
                continue;
            }

            Frame = animation >> 2;
            State.Decode(costume, oldDirection + Frame * 4);
        }
    }

    /// <summary>One frame of animation: every <see cref="AnimSpeed"/> + 1 frames the limbs step on. Mirrors <c>animateCostume</c>.</summary>
    public void AnimateCostume()
    {
        if (Costume is not { } costume)
        {
            return;
        }

        if (++_animProgress >= AnimSpeed)
        {
            _animProgress = 0;
            State.Increase(costume);
        }
    }

    private void LoadCostume(int costume)
    {
        CostumeNumber = costume;
        _costume = null;
        _costumeLoaded = false;
    }
}
