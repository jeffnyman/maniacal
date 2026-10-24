using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Game;

/// <summary>What an actor needs of the room it walks in.</summary>
public interface IActorWorld
{
    int CurrentRoom { get; }

    int Version { get; }

    /// <summary>How many walkboxes the current room has.</summary>
    int BoxCount { get; }

    BoxCorners BoxCorners(int box);

    /// <summary>A box's flags as they are now (scripts change them), or 0 for no box.</summary>
    int BoxFlags(int box);

    /// <summary>
    /// The next box on the way from one to another, or -1. From a box to itself is that box, to no
    /// box is -1, and from no box is the destination. Mirrors <c>getNextBox</c>.
    /// </summary>
    int NextBox(int from, int to);

    /// <summary>Whether the actor is one the player controls. Mirrors <c>Actor_v2::isPlayer</c>.</summary>
    bool IsPlayer(int actor);

    Costume? LoadCostume(int costume);

    /// <summary>Version 0: a box's mask, which holds its drawing layer, slant and ladder bits, or 0 for no box. Mirrors <c>getMaskFromBox</c>.</summary>
    int BoxMask(int box);

    /// <summary>Version 0: the boxes a box joins directly, none for no box. Mirrors <c>getBoxConnectionBase</c>.</summary>
    IReadOnlyList<byte> BoxNeighbours(int box);

    /// <summary>Version 0: whether two boxes are neighbours, as ScummVM tests it. Mirrors <c>areBoxesNeighbors</c>.</summary>
    bool AreBoxesNeighbours(int box1, int box2);

    Costumes.V0Costume? LoadCostumeV0(int costume);
}

/// <summary>
/// A character of versions 1 and 2: its costume, where it is, which way it faces, where its
/// limbs are in their animations, and its walk. Positions are in actor units: 8-pixel columns and
/// 2-pixel lines. Mirrors ScummVM's <c>Actor</c>, <c>Actor_v3</c> and <c>Actor_v2</c>
/// (<c>initActor</c>, <c>putActor</c>, <c>showActor</c>, <c>hideActor</c>, <c>setActorCostume</c>,
/// <c>animateActor</c>, <c>startAnimActor</c>, <c>setDirection</c>, <c>animateCostume</c>,
/// <c>startWalkActor</c>, <c>walkActor</c>, <c>calcMovementFactor</c>, <c>actorWalkStep</c>,
/// <c>updateActorDirection</c>, <c>remapDirection</c>, <c>adjustXYToBeInBox</c>, <c>adjustActorPos</c>).
/// </summary>
public class Actor
{
    /// <summary>The bits of <see cref="Moving"/>. Mirrors <c>MF_NEW_LEG</c>, <c>MF_IN_LEG</c>, <c>MF_TURN</c> and <c>MF_LAST_LEG</c>.</summary>
    public const int NewLeg = 1, InLeg = 2, Turn = 4, LastLeg = 8;

    // versions 1 and 2 walk one unit a step on each axis (Actor_v2::initActor)
    private const int SpeedX = 1, SpeedY = 1, StepX = 1;

    private static readonly byte[] RemapTable1 =
    [
        0x04, 0x01, 0x02, 0x00, 0x01, 0x02, 0x03, 0x00,
        0x06, 0x01, 0x03, 0x00, 0x01, 0x02, 0x03, 0x00,
        0x07, 0x00, 0x03, 0x00, 0x01, 0x02, 0x03, 0x00,
        0x05, 0x00, 0x02, 0x00, 0x01, 0x02, 0x03, 0x00,
    ];

    private static readonly byte[] RemapTable2 =
    [
        0x00, 0x01, 0x03, 0x02, 0x03, 0x00, 0x02, 0x00,
        0x00, 0x01, 0x03, 0x02, 0x01, 0x03, 0x01, 0x02,
        0x00, 0x01, 0x03, 0x02, 0x01, 0x00, 0x02, 0x02,
        0x00, 0x01, 0x03, 0x02, 0x03, 0x03, 0x01, 0x01,
    ];

    private static readonly byte[] RemapTable3 =
    [
        0x00, 0x00, 0x02, 0x00, 0x01, 0x03, 0x02, 0x00,
        0x01, 0x01, 0x02, 0x00, 0x01, 0x03, 0x02, 0x00,
        0x02, 0x01, 0x02, 0x00, 0x01, 0x03, 0x02, 0x00,
        0x03, 0x00, 0x03, 0x00, 0x01, 0x03, 0x02, 0x00,
    ];

    protected static readonly byte[] TurnInterpolation = [0, 2, 2, 3, 2, 1, 2, 3, 0, 1, 2, 1, 0, 1, 0, 3];

    protected readonly IActorWorld _world;
    private Costume? _costume;
    private bool _costumeLoaded = true;
    private int _animProgress;

    // the walk (ScummVM's ActorWalkData)
    protected Point _dest;
    protected int _destBox = BoxGeometry.NoBox;
    protected int _destDir = -1;
    protected int _curBox = BoxGeometry.NoBox;
    private Point _next;
    private int _xfrac, _yfrac, _xAdd, _yAdd, _deltaX, _deltaY, _stepThreshold;
    private int _walkFacing;

    public Actor(int number, IActorWorld world)
    {
        Number = number;
        _world = world;
        for (int i = 0; i < Palette.Length; i++)
        {
            Palette[i] = (byte)i;
        }
    }

    public int Number { get; }

    public int Room { get; set; }

    public int X { get; protected set; }

    public int Y { get; protected set; }

    /// <summary>The facing in degrees: 0 back, 90 right, 180 front, 270 left. <c>initActor</c> starts at 180.</summary>
    public int Facing { get; protected set; } = 180;

    /// <summary>The facing a turn is heading for. Mirrors <c>_targetFacing</c>.</summary>
    public int TargetFacing { get; protected set; } = 180;

    public int CostumeNumber { get; private set; }

    /// <summary>The costume, loaded when first needed: setting a costume loads nothing until the actor shows, as in ScummVM.</summary>
    public Costume? Costume
    {
        get
        {
            if (!_costumeLoaded)
            {
                _costume = CostumeNumber == 0 ? null : _world.LoadCostume(CostumeNumber);
                _costumeLoaded = true;
            }

            return _costume;
        }
    }

    public string Name { get; set; } = string.Empty;

    /// <summary>Mirrors <c>initActor</c>'s 15.</summary>
    public int TalkColor { get; set; } = 15;

    public int Sound { get; set; }

    /// <summary>Height above the ground, in 2-pixel lines.</summary>
    public int Elevation { get; set; }

    /// <summary>Drawn in its room. Mirrors <c>_visible</c>.</summary>
    public bool Visible { get; protected set; }

    /// <summary>The frame last started. Mirrors <c>_frame</c>.</summary>
    public int Frame { get; private set; }

    /// <summary>Frames between animation steps; 0 steps every frame. Mirrors <c>_animSpeed</c>.</summary>
    public int AnimSpeed { get; set; }

    /// <summary>The version 2 colours of the costume, by colour number. Mirrors <c>_palette</c>.</summary>
    public byte[] Palette { get; } = new byte[16];

    public CostumeState State { get; } = new();

    /// <summary>Walking and turning, as <see cref="NewLeg"/>, <see cref="InLeg"/>, <see cref="Turn"/> and <see cref="LastLeg"/> bits; 0 when still. Mirrors <c>_moving</c>.</summary>
    public int Moving { get; protected set; }

    /// <summary>The box the actor stands in, or <see cref="BoxGeometry.NoBox"/>. Mirrors <c>_walkbox</c>.</summary>
    public int Walkbox { get; protected set; } = BoxGeometry.NoBox;

    public bool InCurrentRoom => Room != 0 && Room == _world.CurrentRoom;

    /// <summary>The direction the costume's animations use: 0 left, 1 right, 2 front, 3 back. Mirrors <c>newDirToOldDir</c>.</summary>
    public int OldDirection => NewDirToOldDir(Facing);

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
    /// comes into the current room and hides when it leaves; in the room it stops and is put into
    /// a walkbox. Version 1 actors face front after being put. Mirrors <c>putActor</c>.
    /// </summary>
    public void Put(int x, int y, int? room = null)
    {
        X = x;
        Y = y;
        Room = room ?? Room;

        if (Visible)
        {
            if (InCurrentRoom)
            {
                if (Moving != 0)
                {
                    StopMoving();
                    StartAnim(CostumeRenderer.StandFrame);
                }

                AdjustPosition();
            }
            else
            {
                Hide();
            }
        }
        else if (InCurrentRoom)
        {
            Show();
        }

        AfterPut();
        if (_world.Version <= 1)
        {
            SetDirection(180);
        }
    }

    /// <summary>What a version does more when an actor is put, before version 1's turn to the front.</summary>
    protected virtual void AfterPut()
    {
    }

    /// <summary>Mirrors <c>showActor</c> for versions 1 and 2: into a walkbox, then the stand, init and talk-stop frames, in that order.</summary>
    public virtual void Show()
    {
        if (_world.CurrentRoom == 0 || Visible)
        {
            return;
        }

        AdjustPosition();
        State.Reset();
        StartAnim(CostumeRenderer.StandFrame);
        StartAnim(CostumeRenderer.InitFrame);
        StartAnim(CostumeRenderer.TalkStopFrame);
        StopMoving();
        Visible = true;
    }

    /// <summary>Mirrors <c>hideActor</c>: a walking actor stops and stands.</summary>
    public void Hide()
    {
        if (!Visible)
        {
            return;
        }

        if (Moving != 0)
        {
            StopMoving();
            StartAnim(CostumeRenderer.StandFrame);
        }

        Visible = false;
    }

    /// <summary>Mirrors <c>setActorCostume</c> for these games: shown again with the new costume when it shows.</summary>
    public void SetCostume(int costume)
    {
        if (Visible)
        {
            Hide();
            ResetCostume();
            SelectCostume(costume);
            Show();
        }
        else
        {
            SelectCostume(costume);
            ResetCostume();
        }
    }

    /// <summary>The limbs lose their animations. Mirrors <c>_cost.reset()</c>.</summary>
    protected virtual void ResetCostume() => State.Reset();

    /// <summary>
    /// What the scripts' <c>animateActor</c> asks: <c>frame * 4 + direction</c>, with frames 0x3F,
    /// 0x3E and 0x3D meaning stop walking, face the direction now, and turn to it. Mirrors <c>animateActor</c>.
    /// </summary>
    public virtual void Animate(int animation)
    {
        int chore = 0x3F - (animation >> 2) + 2;
        int direction = OldDirToNewDir(animation & 3);
        switch (chore)
        {
            case 2:
                StartAnim(CostumeRenderer.StandFrame);
                StopMoving();
                break;
            case 3:
                Moving &= ~Turn;
                SetDirection(direction);
                break;
            case 4:
                TurnTo(direction);
                break;
            default:
                StartAnim(animation >> 2);
                break;
        }
    }

    /// <summary>Starts a frame in the current direction, when the actor is in the current room. Mirrors <c>startAnimActor</c>.</summary>
    public virtual void StartAnim(int frame)
    {
        if (InCurrentRoom && Costume is { } costume)
        {
            _animProgress = 0;
            State.Decode(costume, OldDirection + frame * 4);
            Frame = frame;
        }
    }

    /// <summary>
    /// Turns to a facing at once. Every limb whose animation is for another direction, or another
    /// frame, is started again in the new direction. Mirrors <c>setDirection</c> for versions 1 and 2.
    /// </summary>
    public virtual void SetDirection(int direction)
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

    /// <summary>Faces a way without changing the limbs. Mirrors <c>setFacing</c>.</summary>
    public void SetFacing(int facing) => Facing = OldDirToNewDir(NewDirToOldDir((facing + 360) % 360));

    /// <summary>Turns to a facing a quarter at a time, as the walk does each frame. Mirrors <c>turnToDirection</c> for versions 1 and 2.</summary>
    public virtual void TurnTo(int direction)
    {
        if (direction == -1)
        {
            return;
        }

        TargetFacing = direction;
        Moving |= Turn;
    }

    /// <summary>
    /// Turns towards a point in actor units: right or left, or front or back when it is within two
    /// columns. Mirrors <c>faceToObject</c> for versions before 3.
    /// </summary>
    public void FaceTowards(int x, int y)
    {
        if (!InCurrentRoom)
        {
            return;
        }

        int direction = X < x ? 1 : 0;
        if (Math.Abs(X - x) < 2)
        {
            direction = Y > y ? 3 : 2;
        }

        TurnTo(OldDirToNewDir(direction));
    }

    /// <summary>One frame of animation: every <see cref="AnimSpeed"/> + 1 frames the limbs step on. Mirrors <c>animateCostume</c>.</summary>
    public virtual void AnimateCostume()
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

    public virtual void StopMoving() => Moving = 0;

    /// <summary>
    /// Sets off towards a point in actor units, to stand facing <paramref name="direction"/> there
    /// (-1: as it arrives). The point is moved into the nearest walkbox. An actor outside the
    /// current room is simply put there. Mirrors <c>startWalkActor</c> for versions 1 and 2.
    /// </summary>
    public virtual void StartWalk(int x, int y, int direction)
    {
        if (!InCurrentRoom)
        {
            X = x;
            Y = y;
            if (direction != -1)
            {
                Facing = direction;
            }

            return;
        }

        var (point, box) = AdjustToBox(x, y);
        if (X == point.X && Y == point.Y && (direction == -1 || Facing == direction))
        {
            return;
        }

        _dest = point;
        _destBox = box;
        _destDir = direction;
        _curBox = Walkbox;
        Moving = (Moving & ~LastLeg) | InLeg | NewLeg;
    }

    /// <summary>
    /// One frame of the walk: finish a turn first; then a step along the current leg; at the end of
    /// a leg, the next one: straight to the destination in its box, or to where the next box on the
    /// way meets this one. Mirrors <c>Actor_v2::walkActor</c>.
    /// </summary>
    public virtual void Walk()
    {
        if ((Moving & Turn) != 0)
        {
            int direction = UpdateDirection();
            if (TargetFacing == direction)
            {
                Moving &= ~Turn;
            }

            SetDirection(direction);
            return;
        }

        if ((Moving & NewLeg) == 0)
        {
            return;
        }

        if ((Moving & InLeg) == 0)
        {
            WalkStep();
            return;
        }

        if ((Moving & LastLeg) != 0)
        {
            Moving = Turn;
            StartAnim(CostumeRenderer.StandFrame);
            if (_destDir != -1)
            {
                TurnTo(_destDir);
            }

            return;
        }

        Point target;
        Walkbox = _curBox;
        if (Walkbox == _destBox)
        {
            target = _dest;
            Moving |= LastLeg;
        }
        else
        {
            int next = _world.NextBox(Walkbox, _destBox);
            if (next < 0)
            {
                Moving |= LastLeg;
                return;
            }

            int flags = _world.BoxFlags(next);
            if ((flags & BoxGeometry.Locked) != 0 && !((flags & BoxGeometry.PlayerOnly) != 0 && !_world.IsPlayer(Number)))
            {
                Moving |= LastLeg;
                _destDir = -1;
            }

            _curBox = next;
            var (_, onNext) = BoxGeometry.ClosestPointOnBox(_world.BoxCorners(_curBox), X, Y);
            (_, target) = BoxGeometry.ClosestPointOnBox(_world.BoxCorners(Walkbox), onNext.X, onNext.Y);
        }

        CalcMovement(target);
    }

    /// <summary>
    /// The nearest point of the walkboxes to a point in actor units, and its box: the first box
    /// containing it, searched from the last box, or else the nearest. Invisible boxes are passed
    /// over, except player-only ones for actors the player does not control. Mirrors <c>Actor_v2::adjustXYToBeInBox</c>.
    /// </summary>
    public virtual (Point Point, int Box) AdjustToBox(int x, int y)
    {
        var result = (new Point(x, y), BoxGeometry.NoBox);
        int bestDist = 0xFF;
        for (int box = _world.BoxCount - 1; box >= 0; box--)
        {
            int flags = _world.BoxFlags(box);
            if ((flags & BoxGeometry.Invisible) != 0 && !((flags & BoxGeometry.PlayerOnly) != 0 && !_world.IsPlayer(Number)))
            {
                continue;
            }

            var (dist, point) = BoxGeometry.NearestInBox(_world.BoxCorners(box), x, y);
            if (dist == 0)
            {
                return (point, box);
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                result = (point, box);
            }
        }

        return result;
    }

    /// <summary>Moves the actor into a walkbox and stops it. Mirrors <c>adjustActorPos</c>.</summary>
    protected void AdjustPosition()
    {
        var (point, box) = AdjustToBox(X, Y);
        X = point.X;
        Y = point.Y;
        _destBox = box;
        Walkbox = box;
        _dest = new Point(-1, _dest.Y);
        StopMoving();
        if (Walkbox != BoxGeometry.NoBox && (_world.BoxFlags(Walkbox) & 7) != 0)
        {
            TurnTo(Facing);
        }
    }

    /// <summary>
    /// Plans a leg to a point: one unit a step on the longer axis, and a turn first when the leg
    /// goes another way than the actor faces. Mirrors <c>Actor_v3::calcMovementFactor</c> for version 2.
    /// </summary>
    private void CalcMovement(Point next)
    {
        if (X == next.X && Y == next.Y)
        {
            Moving |= InLeg;
            return;
        }

        int diffX = next.X - X;
        int diffY = next.Y - Y;
        _stepThreshold = Math.Max(Math.Abs(diffY) / SpeedY, Math.Abs(diffX) / StepX);
        _deltaX = diffX < 0 ? -StepX : StepX;
        _deltaY = diffY < 0 ? -SpeedY : SpeedY;
        _xfrac = _xAdd = diffX / _deltaX;
        _yfrac = _yAdd = diffY / _deltaY;
        _next = next;
        _walkFacing = diffX >= 0 ? (diffY >= 0 ? 1 : 0) : (diffY >= 0 ? 2 : 3);

        // the facing ratio is 1 in version 2 (Actor_v3's _facingXYratio)
        TargetFacing = OldDirToNewDir(Math.Abs(diffY) > Math.Abs(diffX) ? 3 - (diffY >= 0 ? 1 : 0) : (diffX >= 0 ? 1 : 0));

        Moving &= ~InLeg;
        if (Facing != TargetFacing)
        {
            Moving |= Turn;
        }

        if (Frame != CostumeRenderer.WalkFrame || Facing != TargetFacing)
        {
            StartWalkAnim(1, Facing);
        }

        if ((Moving & Turn) == 0)
        {
            WalkStep();
        }
    }

    /// <summary>One step along the leg, an axis at a time as its fraction comes round. Mirrors <c>Actor_v2::actorWalkStep</c>.</summary>
    private void WalkStep()
    {
        if ((_xfrac += _xAdd) >= _stepThreshold)
        {
            if (X != _next.X)
            {
                X += _deltaX;
            }

            _xfrac -= _stepThreshold;
        }

        if ((_yfrac += _yAdd) >= _stepThreshold)
        {
            if (Y != _next.Y)
            {
                Y += _deltaY;
            }

            _yfrac -= _stepThreshold;
        }

        if (X == _next.X && Y == _next.Y)
        {
            Moving |= InLeg;
        }
    }

    /// <summary>Mirrors <c>startWalkAnim</c> without walk scripts: face the way (or turn, for a stop), then the walk or stand frame.</summary>
    private void StartWalkAnim(int command, int angle)
    {
        if (angle == -1)
        {
            angle = Facing;
        }

        if (command == 3)
        {
            TurnTo(angle);
        }
        else
        {
            SetDirection(angle);
        }

        if (command == 1)
        {
            StartAnim(CostumeRenderer.WalkFrame);
        }
        else if (command == 3)
        {
            StartAnim(CostumeRenderer.StandFrame);
        }
    }

    /// <summary>
    /// The facing for this frame of a turn: the target, bent by the walkbox's flags, approached a
    /// quarter turn at a time. Mirrors <c>updateActorDirection</c> and <c>Actor_v2::remapDirection</c>.
    /// </summary>
    private int UpdateDirection()
    {
        int boxFlags = _world.BoxFlags(Walkbox) & 7;
        int target = NewDirToOldDir(TargetFacing);
        TargetFacing = (Moving & ~Turn) != 0
            ? OldDirToNewDir(RemapTable2[target * 8 + RemapTable1[_walkFacing * 8 + boxFlags]])
            : OldDirToNewDir(RemapTable3[target * 8 + boxFlags]);

        return OldDirToNewDir(TurnInterpolation[NewDirToOldDir(TargetFacing) | (NewDirToOldDir(Facing) << 2)]);
    }

    /// <summary>
    /// Copies the actor into a save as ScummVM keeps it: position, costume and its limbs, the walk
    /// and its leg, and the fields this interpreter does not keep at their <c>Actor_v2::initActor</c>
    /// values. Mirrors what <c>Actor::saveLoadWithSerializer</c> writes.
    /// </summary>
    internal void SaveTo(Saves.ScummState.ActorState a)
    {
        a.X = X;
        a.Y = Y;
        a.Elevation = Elevation;
        a.Facing = Facing;
        a.Costume = CostumeNumber;
        a.Room = Room;
        a.TalkColor = TalkColor;
        a.Sound[0] = Sound;
        a.TargetFacing = TargetFacing;
        a.Moving = Moving;
        a.Visible = Visible ? 1 : 0;
        a.Frame = Frame;
        a.AnimSpeed = AnimSpeed;
        a.AnimProgress = _animProgress;
        a.Walkbox = Walkbox;

        // version 1 keeps no palette: what scripts set lands on zeros (setActorCostume returns early)
        Array.Clear(a.Palette);
        for (int i = 0; i < Palette.Length; i++)
        {
            a.Palette[i] = _world.Version <= 1 && Palette[i] == i ? 0 : Palette[i];
        }

        a.DestX = _dest.X;
        a.DestY = _dest.Y;
        a.DestBox = _destBox & 0xFF;
        a.DestDir = _destDir & 0xFFFF;
        a.CurBox = _curBox & 0xFF;
        a.CurX = X;
        a.CurY = Y;
        a.NextX = _next.X;
        a.NextY = _next.Y;
        a.DeltaXFactor = _deltaX;
        a.DeltaYFactor = _deltaY;
        a.XFrac = _xfrac & 0xFFFF;
        a.YFrac = _yfrac & 0xFFFF;
        a.WalkFacing = _walkFacing;
        a.XAdd = _xAdd & 0xFFFF;
        a.YAdd = _yAdd & 0xFFFF;
        a.StepThreshold = _stepThreshold & 0xFFFF;
        a.Stopped = State.Stopped;
        State.SaveTo(a.CurPos, a.Start, a.End, a.CostumeFrame);
    }

    /// <summary>Takes the actor from a save; the costume loads when it is next drawn.</summary>
    internal void LoadFrom(Saves.ScummState.ActorState a)
    {
        X = a.X;
        Y = a.Y;
        Elevation = (short)a.Elevation;
        Facing = a.Facing;
        SelectCostume(a.Costume);
        Room = a.Room;
        TalkColor = a.TalkColor;
        Sound = a.Sound[0];
        TargetFacing = a.TargetFacing;
        Moving = a.Moving;
        Visible = a.Visible != 0;
        Frame = a.Frame;
        AnimSpeed = a.AnimSpeed;
        _animProgress = a.AnimProgress;
        Walkbox = a.Walkbox;
        for (int i = 0; i < Palette.Length; i++)
        {
            Palette[i] = _world.Version <= 1 && a.Palette[i] == 0 ? (byte)i : (byte)a.Palette[i];
        }

        _dest = new Point(a.DestX, a.DestY);
        _destBox = a.DestBox;
        _destDir = a.DestDir == 0xFFFF ? -1 : a.DestDir;
        _curBox = a.CurBox;
        _next = new Point(a.NextX, a.NextY);
        _deltaX = a.DeltaXFactor;
        _deltaY = a.DeltaYFactor;
        _xfrac = (short)a.XFrac;
        _yfrac = (short)a.YFrac;
        _walkFacing = a.WalkFacing;
        _xAdd = (short)a.XAdd;
        _yAdd = (short)a.YAdd;
        _stepThreshold = a.StepThreshold;
        State.LoadFrom(a.CurPos, a.Start, a.End, a.CostumeFrame, a.Stopped);
    }

    protected virtual void SelectCostume(int costume)
    {
        CostumeNumber = costume;
        _costume = null;
        _costumeLoaded = false;
    }
}
