using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Game;

/// <summary>
/// A character of version 0, the C64: its costume plays clips rather than frames, it talks by
/// moving its mouth itself, and it walks its own way, a column or line a step, from walkbox to
/// walkbox along a path it plans when it sets off. Mirrors ScummVM's <c>Actor_v0</c>.
/// </summary>
/// <remarks>
/// <see cref="Actor.Moving"/> is 2 for an actor standing still (<c>stopActorMoving</c>). Walking,
/// its low four bits say how the walk goes: 0 on the way, 1 there, 3 blocked across and going
/// along, 4 blocked along and going across; bit 7 is set while the actor turns.
/// </remarks>
public sealed class ActorV0 : Actor
{
    /// <summary>What <see cref="Actor.Moving"/> is for an actor standing still.</summary>
    public const int Standing = 2;

    /// <summary>Version 0's actor flags the scripts keep (<c>ActorV0MiscFlags</c>): the actor stops, or is not drawn.</summary>
    public const int MiscFlagFreeze = 0x40, MiscFlagHide = 0x80;

    private const int Turning = 0x80;
    private const int WalkOn = 0, WalkArrived = 1, WalkBlockedX = 3, WalkBlockedY = 4;

    // the clips setDirection plays to face left, right, front and back
    private const int ClipFaceLeft = 4;

    // the clip speakCheck adds to the direction, mouth open or shut
    private const int ClipMouthOpen = 0x0C, ClipMouthShut = 0x10;

    // talk flags (v0ActorTalk): no mouth at all, or no talking animation
    private const int TalkNoMouth = 0x80, TalkNoAnimation = 0x40;

    private const int QueueSize = 0x10;

    private V0Costume? _costumeV0;
    private bool _costumeV0Loaded = true;

    // the walk (_CurrentWalkTo, _NewWalkTo, the walkbox queue and its history)
    private Point _currentWalkTo, _newWalkTo;
    private readonly byte[] _queue = new byte[QueueSize];
    private int _queueIndex;
    private readonly List<int> _history = [];
    private bool _newWalkBoxEntered;

    // a leg's steps: which way on each axis, and the counts that space the steps on the shorter axis
    private int _walkDirX, _walkDirY;
    private bool _walkYCountGreaterThanXCount;
    private byte _walkXCount, _walkXCountInc, _walkYCount, _walkYCountInc, _walkCountModulo;

    // where the actor tries a step from (_tmp_Pos, _tmp_NewPos, _tmp_WalkBox, _tmp_NewWalkBoxEntered)
    private Point _tmpPos, _tmpNewPos;
    private int _tmpWalkBox;
    private bool _tmpNewWalkBoxEntered;

    /// <summary>Mirrors <c>Actor_v0::initActor</c>: standing, with its talk flags from <c>v0ActorTalk</c>.</summary>
    public ActorV0(int number, IActorWorld world) : base(number, world)
    {
        Moving = Standing;
        Sound = number < V0Actors.Talk.Length ? V0Actors.Talk[number] : 0;
        Array.Fill(_queue, (byte)BoxGeometry.NoBox);
    }

    /// <summary>Where the limbs are in their clips.</summary>
    public V0CostumeState Limbs { get; } = new();

    /// <summary>The costume, loaded when first needed, as <see cref="Actor.Costume"/> is for versions 1 and 2.</summary>
    public V0Costume? CostumeV0
    {
        get
        {
            if (!_costumeV0Loaded)
            {
                _costumeV0 = CostumeNumber == 0 ? null : _world.LoadCostumeV0(CostumeNumber);
                _costumeV0Loaded = true;
            }

            return _costumeV0;
        }
    }

    /// <summary>The scripts' flags for the actor (<c>_miscflags</c>), among them <see cref="MiscFlagFreeze"/> and <see cref="MiscFlagHide"/>.</summary>
    public int MiscFlags { get; set; }

    /// <summary>
    /// Whether the mouth moves: 1 while talking, and bit 7 for open, which the drawing flips every
    /// frame; 0x80 set by a script holds it open (<c>_speaking</c>).
    /// </summary>
    public int Speaking { get; set; }

    /// <summary>Mirrors <c>showActor</c> for version 0: into a walkbox, its clips started afresh, and standing.</summary>
    public override void Show()
    {
        if (_world.CurrentRoom == 0 || Visible)
        {
            return;
        }

        AdjustPosition();
        Limbs.Restart();
        _dest = _currentWalkTo;
        Speaking = 0;
        StartAnim(CostumeRenderer.StandFrame);
        Visible = true;
    }

    /// <summary>Mirrors the end of <c>putActor</c> for version 0: the walk starts from where the actor is.</summary>
    protected override void AfterPut()
    {
        _newWalkBoxEntered = false;
        _currentWalkTo = _newWalkTo = new Point(X, Y);
    }

    protected override void ResetCostume() => Limbs.Reset();

    protected override void SelectCostume(int costume)
    {
        base.SelectCostume(costume);
        _costumeV0 = null;
        _costumeV0Loaded = false;
    }

    /// <summary>
    /// Plays a clip: in the current room the limbs take it, and clips 0 to 7 (walking, then
    /// standing, each left, right, front and back) turn the actor that way. Elsewhere only the
    /// standing clips turn it. Mirrors <c>Actor_v0::animateActor</c>.
    /// </summary>
    public override void Animate(int animation)
    {
        int direction = animation is >= 0 and <= 7 ? animation & 3 : -1;
        if (InCurrentRoom)
        {
            Limbs.NewClip = animation;
            if (CostumeNumber != 0 && CostumeV0 is { } costume)
            {
                Limbs.Decode(costume);
            }

            if (direction != -1)
            {
                Facing = OldDirToNewDir(direction);
            }
        }
        else if (animation is >= ClipFaceLeft and <= ClipFaceLeft + 3)
        {
            Facing = OldDirToNewDir(direction);
        }
    }

    /// <summary>
    /// Version 0 has no frames: talking starts the mouth moving (unless the actor has no talking
    /// animation), stopping stops it, and standing faces the way the actor faces. Mirrors
    /// <c>Actor_v0::startAnimActor</c>.
    /// </summary>
    public override void StartAnim(int frame)
    {
        if (frame == CostumeRenderer.TalkStartFrame)
        {
            if ((Sound & TalkNoAnimation) != 0)
            {
                return;
            }

            Speaking = 1;
            SpeakCheck();
        }
        else if (frame == CostumeRenderer.TalkStopFrame)
        {
            Speaking = 0;
        }
        else if (frame == CostumeRenderer.StandFrame)
        {
            SetDirection(Facing);
        }
    }

    /// <summary>Faces a way with its standing clip, held. Mirrors <c>Actor_v0::setDirection</c>.</summary>
    public override void SetDirection(int direction)
    {
        Limbs.Repeat = -1;
        Animate(ClipFaceLeft + NewDirToOldDir(direction));
    }

    /// <summary>Version 0 turns at once. Mirrors <c>turnToDirection</c> for version 0.</summary>
    public override void TurnTo(int direction)
    {
        if (direction == -1)
        {
            return;
        }

        TargetFacing = direction;
        SetDirection(direction);
    }

    /// <summary>Mirrors <c>stopActorMoving</c> for version 0: standing, facing the way it faces.</summary>
    public override void StopMoving()
    {
        Moving = Standing;
        SetDirection(Facing);
    }

    /// <summary>Stops, and leaves the box it walks into unheeded: a frozen kid stays put (<c>walkToActorOrObject</c>).</summary>
    public void Halt()
    {
        StopMoving();
        _newWalkBoxEntered = false;
    }

    /// <summary>One frame: the mouth, then a step of every limb. Mirrors <c>Actor_v0::animateCostume</c>.</summary>
    public override void AnimateCostume()
    {
        SpeakCheck();
        if (CostumeV0 is { } costume)
        {
            Limbs.Increase(costume);
        }
    }

    /// <summary>A talking actor's mouth opens and shuts frame by frame. Mirrors the <c>_speaking</c> flip of <c>processActors</c> for version 0.</summary>
    public void FlipMouth()
    {
        if ((Speaking & 1) != 0)
        {
            Speaking ^= 0xFE;
        }
    }

    /// <summary>
    /// Sets off towards a point in actor units, to face <paramref name="direction"/> there (-1: as
    /// it arrives), planning the walkboxes on the way. The point is moved into the nearest walkbox.
    /// An actor outside the current room is simply put there. Mirrors <c>startWalkActor</c> for version 0.
    /// </summary>
    public override void StartWalk(int x, int y, int direction)
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
        WalkBoxQueuePrepare();
    }

    /// <summary>
    /// One frame of the walk: a new box on the path gives a new point to walk to; a new point plans
    /// the leg and turns the actor to it; then a turn goes on, or the actor steps, across and along
    /// as the leg's counts say, going round a box's edge when a step would leave the boxes. Mirrors
    /// <c>Actor_v0::walkActor</c>.
    /// </summary>
    public override void Walk()
    {
        ActorSetWalkTo();

        bool plan = false;
        if (_newWalkTo != _currentWalkTo)
        {
            _currentWalkTo = _newWalkTo;
            plan = true;
        }
        else if ((Moving & 0x7F) != WalkArrived && _newWalkTo == new Point(X, Y))
        {
            return;
        }

        while (true)
        {
            if (plan)
            {
                plan = false;
                _tmpNewPos = new Point(X, Y);
                Moving = (Moving & 0xF0) | (CalcWalkDistances() ? WalkArrived : WalkOn);
                TargetFacing = _walkYCountGreaterThanXCount ? (_walkDirY != 0 ? 180 : 0) : (_walkDirX != 0 ? 90 : 270);
                DirectionUpdate();
                if ((Moving & Turning) != 0)
                {
                    return;
                }

                Animate(OldDirection);
            }

            if (Moving == Standing)
            {
                return;
            }

            if ((Moving & 0x0F) == WalkArrived)
            {
                StopMoving();
                return;
            }

            if ((Moving & Turning) != 0)
            {
                DirectionUpdate();
                if ((Moving & Turning) != 0)
                {
                    return;
                }

                Animate(OldDirection);
            }

            switch (Moving & 0x0F)
            {
                case WalkBlockedX:
                    plan = StepAlong();
                    break;
                case WalkBlockedY:
                    plan = StepAcross();
                    break;
                case WalkOn:
                    Step();
                    break;
            }

            if (!plan)
            {
                return;
            }
        }
    }

    /// <summary>
    /// The nearest point of the walkboxes to a point in actor units, and its box: the first box
    /// containing it, searched from box 0, or else the nearest, columns counting double; then kept
    /// off a slanted box's diagonal edge. Mirrors <c>Actor_v0::adjustXYToBeInBox</c> and
    /// <c>Actor_v2::adjustXYToBeInBox</c> for version 0, whose boxes have no flags.
    /// </summary>
    public override (Point Point, int Box) AdjustToBox(int x, int y)
    {
        var result = (Point: new Point(x, y), Box: BoxGeometry.NoBox);
        int bestDist = 0xFF;
        for (int box = 0; box < _world.BoxCount; box++)
        {
            var (dist, point) = BoxGeometry.NearestInBox(_world.BoxCorners(box), x, y, doubleColumns: true);
            if (dist == 0)
            {
                result = (point, box);
                break;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                result = (point, box);
            }
        }

        return result.Box == BoxGeometry.NoBox ? result : AdjustPosInBorderWalkbox(result);
    }

    /// <summary>
    /// A point in a slanted box is kept on the inside of its diagonal side, which runs
    /// <see cref="V0Actors.SlantedModifier"/> columns in a line from the top corner: the left side
    /// for boxes whose mask is 0x88 (with any layer bits), the right for 0x8C. Mirrors
    /// <c>adjustPosInBorderWalkbox</c>, but for its columns counting as the C64's bytes do and a
    /// column past 160 becoming 0, where ScummVM makes every column up to 160 0
    /// (<c>clamp_to_diagonal_edge</c> in <c>walkboxes/walkbox_snap.asm</c>).
    /// </summary>
    private (Point Point, int Box) AdjustPosInBorderWalkbox((Point Point, int Box) found)
    {
        const int SlantBit = 0x80, SlantKind = 0x7C, SlantedLeft = 0x08, SlantedRight = 0x0C, RoomXMax = 0xA0;
        int mask = _world.BoxMask(found.Box);
        if ((mask & SlantBit) == 0 || (mask & SlantKind) is not (SlantedLeft or SlantedRight))
        {
            return found;
        }

        var corners = _world.BoxCorners(found.Box);
        int line = (found.Point.Y - corners.UpperLeft.Y) & 0xFF;
        if (line >= V0Actors.SlantedModifier.Length)
        {
            throw new InvalidOperationException($"Line {line} of slanted walkbox {found.Box} is past the slant's table.");
        }

        int x = found.Point.X;
        if ((mask & SlantKind) == SlantedLeft)
        {
            int edge = (corners.UpperRight.X - V0Actors.SlantedModifier[line]) & 0xFF;
            if (edge >= x)
            {
                x = edge <= RoomXMax ? edge : 0;
            }
        }
        else
        {
            int edge = (corners.UpperLeft.X + V0Actors.SlantedModifier[line]) & 0xFF;
            if (edge <= x)
            {
                x = edge;
            }
        }

        return (found.Point with { X = x }, found.Box);
    }

    /// <summary>
    /// Plans the boxes from the actor's to the destination's, a search through each box's
    /// neighbours that passes over boxes it has tried; the path is kept to be walked from the start.
    /// Mirrors <c>walkBoxQueuePrepare</c> and <c>walkboxQueueReverse</c>.
    /// </summary>
    private bool WalkBoxQueuePrepare()
    {
        WalkBoxQueueReset();
        int found = Walkbox;
        if (found == _destBox)
        {
            _newWalkBoxEntered = true;
            return true;
        }

        do
        {
            if (!WalkBoxQueueAdd(found))
            {
                return false;
            }

            while (_queueIndex > 0)
            {
                // a neighbour of the box is the destination, or else the first of the last box's
                // neighbours not tried yet
                if ((found = NextBox(found, _destBox)) == BoxGeometry.NoBox)
                {
                    found = BoxGeometry.NoBox;
                    foreach (byte neighbour in _world.BoxNeighbours(_queue[_queueIndex - 1]))
                    {
                        if (!_history.Contains(neighbour))
                        {
                            found = neighbour;
                            break;
                        }
                    }
                }

                if (found != BoxGeometry.NoBox)
                {
                    if (found == _destBox)
                    {
                        _newWalkBoxEntered = true;
                        WalkBoxQueueAdd(found);
                        WalkBoxQueueReverse();
                        return true;
                    }

                    break;
                }

                // no way on from this box
                _queue[--_queueIndex] = BoxGeometry.NoBox;
                found = _queueIndex > 0 ? _queue[_queueIndex - 1] : BoxGeometry.NoBox;
            }
        }
        while (_queueIndex > 0);

        return false;
    }

    private void WalkBoxQueueReset()
    {
        _history.Clear();
        _queueIndex = 0;
        Array.Fill(_queue, (byte)BoxGeometry.NoBox);
    }

    private bool WalkBoxQueueAdd(int box)
    {
        if (_queueIndex == QueueSize)
        {
            return false;
        }

        _queue[_queueIndex++] = (byte)box;
        _history.Add(box & 0xFF);
        return true;
    }

    /// <summary>The path but its first box turns round, so the box after the actor's comes off the end first.</summary>
    private void WalkBoxQueueReverse()
    {
        int j = QueueSize - 1;
        while (_queue[j] == BoxGeometry.NoBox && j >= 1)
        {
            j--;
        }

        for (int i = 1; i < j; i++, j--)
        {
            (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
        }
    }

    /// <summary>The box itself when it is a neighbour, else none. Mirrors <c>getNextBox</c> for version 0, which answers 0xFF, not -1, for no way.</summary>
    private int NextBox(int from, int to)
    {
        if (from == to)
        {
            return to;
        }

        if (to == BoxGeometry.NoBox)
        {
            return -1;
        }

        if (from == BoxGeometry.NoBox)
        {
            return to;
        }

        return _world.BoxNeighbours(from).Contains((byte)to) ? to : BoxGeometry.NoBox;
    }

    /// <summary>Having come into a box of the path, the next point to walk to. Mirrors <c>actorSetWalkTo</c>.</summary>
    private void ActorSetWalkTo()
    {
        if (!_newWalkBoxEntered)
        {
            return;
        }

        _newWalkBoxEntered = false;
        int next = WalkboxFindTarget();
        if (next != BoxGeometry.NoBox)
        {
            _curBox = next;
        }
    }

    /// <summary>
    /// The next box of the path, and where to walk: into the destination's box, the destination;
    /// into another, the nearest point of it; with none left, the destination, or where the actor
    /// was going when it has none. Mirrors <c>ScummEngine_v0::walkboxFindTarget</c>.
    /// </summary>
    private int WalkboxFindTarget()
    {
        int next = BoxGeometry.NoBox;
        if (_queueIndex > 1)
        {
            next = _queue[--_queueIndex];
            if (_queueIndex <= 1)
            {
                WalkBoxQueueReset();
            }
        }

        if (next != BoxGeometry.NoBox && next == _destBox && _world.AreBoxesNeighbours(Walkbox, next))
        {
            _newWalkTo = _dest;
            return next;
        }

        if (next != BoxGeometry.NoBox && next != Walkbox)
        {
            (_, _newWalkTo) = BoxGeometry.ClosestPointOnBox(_world.BoxCorners(next), X, Y);
        }
        else
        {
            _newWalkTo = _dest.X == -1 ? _currentWalkTo : _dest;
        }

        return next;
    }

    /// <summary>
    /// Plans a leg: which way on each axis, how far, and the counts that space the shorter axis's
    /// steps; true when the actor is there already. Mirrors <c>calcWalkDistances</c>, counting in bytes.
    /// </summary>
    private bool CalcWalkDistances()
    {
        _walkDirX = _currentWalkTo.X >= _tmpNewPos.X ? 1 : 0;
        _walkXCountInc = (byte)Math.Abs(_currentWalkTo.X - _tmpNewPos.X);
        _walkDirY = _currentWalkTo.Y >= _tmpNewPos.Y ? 1 : 0;
        _walkYCountInc = (byte)Math.Abs(_currentWalkTo.Y - _tmpNewPos.Y);
        _walkYCountGreaterThanXCount = false;
        if (_walkXCountInc == 0 && _walkYCountInc == 0)
        {
            return true;
        }

        _walkYCountGreaterThanXCount = _walkXCountInc <= _walkYCountInc;
        _walkCountModulo = Math.Max(_walkXCountInc, _walkYCountInc);
        _walkXCount = _walkXCountInc;
        _walkYCount = _walkYCountInc;
        return false;
    }

    /// <summary>
    /// A step on the way: across when its count comes round, then along; a step that would leave
    /// the boxes blocks that axis, and the actor turns to go along the other. Mirrors the
    /// <c>_moving</c> 0 branch of <c>Actor_v0::walkActor</c>.
    /// </summary>
    private void Step()
    {
        int result = ActorWalkXCalculate();
        if (result == WalkArrived)
        {
            result = ActorWalkYCalculate();
            if (result == WalkArrived)
            {
                Moving = (Moving & 0xF0) | WalkArrived;
            }
            else if (result == WalkBlockedY)
            {
                StopMoving();
            }

            return;
        }

        if (result == WalkBlockedX)
        {
            Moving = (Moving & 0xF0) | WalkBlockedX;
            TargetFacing = _walkDirY != 0 ? 180 : 0;
            DirectionUpdate();
            Animate(OldDirection);
            return;
        }

        if (ActorWalkYCalculate() != WalkBlockedY)
        {
            return;
        }

        Moving = (Moving & 0xF0) | WalkBlockedY;
        TargetFacing = _walkDirX != 0 ? 90 : 270;
        DirectionUpdate();
        Animate(OldDirection);
    }

    /// <summary>
    /// Blocked across: while a step across would still leave the boxes, a step along, stopping on
    /// the destination's line or where that leaves them too; true when across is open again.
    /// Mirrors the <c>_moving</c> 3 branch of <c>Actor_v0::walkActor</c>.
    /// </summary>
    private bool StepAlong()
    {
        SetActorToTempPosition();
        X += _walkDirX == 0 ? -1 : 1;
        if (UpdateWalkbox() != BoxGeometry.NoBox)
        {
            SetActorToOriginalPosition();
            return true;
        }

        SetActorToOriginalPosition();
        if (_currentWalkTo.Y == _tmpNewPos.Y)
        {
            StopMoving();
            return false;
        }

        _tmpNewPos = _tmpNewPos with { Y = _tmpNewPos.Y + (_walkDirY == 0 ? -1 : 1) };
        SetActorToTempPosition();
        if (UpdateWalkbox() == BoxGeometry.NoBox)
        {
            SetActorToOriginalPosition();
            StopMoving();
        }

        return false;
    }

    /// <summary>
    /// Blocked along: while a step along would still leave the boxes, a step across, stopping on
    /// the destination's column or where that leaves them too; true when along is open again.
    /// Mirrors the <c>_moving</c> 4 branch of <c>Actor_v0::walkActor</c>.
    /// </summary>
    private bool StepAcross()
    {
        SetActorToTempPosition();
        Y += _walkDirY == 0 ? -1 : 1;
        if (UpdateWalkbox() != BoxGeometry.NoBox)
        {
            SetActorToOriginalPosition();
            return true;
        }

        SetActorToOriginalPosition();
        if (_currentWalkTo.X == _tmpNewPos.X)
        {
            StopMoving();
            return false;
        }

        _tmpNewPos = _tmpNewPos with { X = _tmpNewPos.X + (_walkDirX == 0 ? -1 : 1) };
        SetActorToTempPosition();
        if (UpdateWalkbox() == BoxGeometry.NoBox)
        {
            SetActorToOriginalPosition();
            StopMoving();
        }

        return false;
    }

    /// <summary>A column across when the count comes round: 3 when that leaves the boxes, 1 on the destination's column. Mirrors <c>actorWalkXCalculate</c>.</summary>
    private int ActorWalkXCalculate()
    {
        int count = (_walkXCount + _walkXCountInc) & 0xFF;
        if (count >= _walkCountModulo)
        {
            _tmpNewPos = _tmpNewPos with { X = _tmpNewPos.X + (_walkDirX == 0 ? -1 : 1) };
            count -= _walkCountModulo;
        }

        _walkXCount = (byte)count;
        SetActorToTempPosition();
        if (UpdateWalkbox() == BoxGeometry.NoBox)
        {
            SetActorToOriginalPosition();
            return WalkBlockedX;
        }

        return _tmpNewPos.X == _currentWalkTo.X ? WalkArrived : WalkOn;
    }

    /// <summary>A line along when the count comes round: 4 when that leaves the boxes, 1 on the destination's line. Mirrors <c>actorWalkYCalculate</c>.</summary>
    private int ActorWalkYCalculate()
    {
        int count = (_walkYCount + _walkYCountInc) & 0xFF;
        if (count >= _walkCountModulo)
        {
            _tmpNewPos = _tmpNewPos with { Y = _tmpNewPos.Y + (_walkDirY == 0 ? -1 : 1) };
            count -= _walkCountModulo;
        }

        _walkYCount = (byte)count;
        SetActorToTempPosition();
        if (UpdateWalkbox() == BoxGeometry.NoBox || _walkYCountInc == 0xFF)
        {
            SetActorToOriginalPosition();
            return WalkBlockedY;
        }

        return _currentWalkTo.Y == _tmpNewPos.Y ? WalkArrived : WalkOn;
    }

    /// <summary>
    /// The box the actor is in: 0 when it is still in its own, the next box of the path when it has
    /// come into that (and turns towards where it goes), or none. Mirrors <c>Actor_v0::updateWalkbox</c>.
    /// </summary>
    private int UpdateWalkbox()
    {
        if (InBox(Walkbox))
        {
            return 0;
        }

        for (int box = 0; box < _world.BoxCount; box++)
        {
            if (InBox(box) && _curBox == box)
            {
                Walkbox = box;
                DirectionUpdate();
                _newWalkBoxEntered = true;
                return box;
            }
        }

        return BoxGeometry.NoBox;
    }

    private bool InBox(int box) => box >= 0 && box < _world.BoxCount && BoxGeometry.Contains(_world.BoxCorners(box), X, Y);

    /// <summary>A quarter turn towards <see cref="Actor.TargetFacing"/>, with bit 7 of <see cref="Actor.Moving"/> while more is to come. Mirrors <c>directionUpdate</c>.</summary>
    private void DirectionUpdate()
    {
        int next = UpdateActorDirection();
        if (Facing != next)
        {
            SetDirection(next);
            if (Facing != TargetFacing)
            {
                Moving |= Turning;
                return;
            }
        }

        Moving &= ~Turning;
    }

    /// <summary>
    /// The facing for this frame of a turn: the target a quarter turn at a time, but straight to
    /// the back on a ladder (a box whose mask has bits 0x84 of 0x8C). Mirrors
    /// <c>updateActorDirection</c> and <c>Actor::remapDirection</c> for version 0, whose boxes have no flags.
    /// </summary>
    private int UpdateActorDirection()
    {
        const int LadderBits = 0x8C, Ladder = 0x84;
        if ((_world.BoxMask(Walkbox) & LadderBits) == Ladder)
        {
            return 0;
        }

        int target = (TargetFacing + 360) % 360;
        return OldDirToNewDir(TurnInterpolation[NewDirToOldDir(target) | (NewDirToOldDir(Facing) << 2)]);
    }

    private void SetActorToTempPosition()
    {
        _tmpPos = new Point(X, Y);
        (X, Y) = (_tmpNewPos.X, _tmpNewPos.Y);
        _tmpWalkBox = Walkbox;
        _tmpNewWalkBoxEntered = _newWalkBoxEntered;
    }

    private void SetActorToOriginalPosition()
    {
        (X, Y) = (_tmpPos.X, _tmpPos.Y);
        _tmpNewPos = _tmpPos;
        Walkbox = _tmpWalkBox;
        _newWalkBoxEntered = _tmpNewWalkBoxEntered;
    }

    /// <summary>
    /// Copies the actor into a save: <c>Actor</c>'s fields, the limbs, and <c>Actor_v0</c>'s own.
    /// Version 0 never sets the walk's current and next points, which stay as <c>initActor</c> left them.
    /// </summary>
    internal override void SaveTo(Saves.ScummState.ActorState a)
    {
        base.SaveTo(a);
        (a.CurX, a.CurY, a.NextX, a.NextY) = (0, 0, 0, 0);
        Limbs.SaveTo(a);
        a.MiscFlags = MiscFlags;
        a.Speaking = Speaking;
        (a.CurrentWalkToX, a.CurrentWalkToY, a.NewWalkToX, a.NewWalkToY) = (_currentWalkTo.X, _currentWalkTo.Y, _newWalkTo.X, _newWalkTo.Y);
        a.WalkCountModulo = (sbyte)_walkCountModulo;
        a.NewWalkBoxEntered = _newWalkBoxEntered;
        (a.WalkDirX, a.WalkDirY, a.WalkYCountGreaterThanXCount) = (_walkDirX, _walkDirY, _walkYCountGreaterThanXCount ? 1 : 0);
        (a.WalkXCount, a.WalkXCountInc, a.WalkYCount, a.WalkYCountInc) = (_walkXCount, _walkXCountInc, _walkYCount, _walkYCountInc);
        a.WalkMaxXYCountInc = _walkCountModulo;
        _queue.CopyTo(a.WalkboxQueue, 0);
        a.WalkboxQueueIndex = _queueIndex;
    }

    /// <summary>
    /// Takes the actor from a save, then starts its limbs again as <c>Actor_v0::saveLoadWithSerializer</c>
    /// does: an actor in a clip with no walk queued stands facing its way, one walking turns and walks on.
    /// </summary>
    internal override void LoadFrom(Saves.ScummState.ActorState a)
    {
        base.LoadFrom(a);
        Limbs.LoadFrom(a);
        MiscFlags = a.MiscFlags;
        Speaking = a.Speaking;
        _currentWalkTo = new Point(a.CurrentWalkToX, a.CurrentWalkToY);
        _newWalkTo = new Point(a.NewWalkToX, a.NewWalkToY);
        _walkCountModulo = (byte)a.WalkCountModulo;
        _newWalkBoxEntered = a.NewWalkBoxEntered;
        (_walkDirX, _walkDirY, _walkYCountGreaterThanXCount) = (a.WalkDirX, a.WalkDirY, a.WalkYCountGreaterThanXCount != 0);
        (_walkXCount, _walkXCountInc, _walkYCount, _walkYCountInc) = ((byte)a.WalkXCount, (byte)a.WalkXCountInc, (byte)a.WalkYCount, (byte)a.WalkYCountInc);
        a.WalkboxQueue.CopyTo(_queue, 0);
        _queueIndex = a.WalkboxQueueIndex;
        _history.Clear();
        (_tmpPos, _tmpNewPos, _tmpWalkBox, _tmpNewWalkBoxEntered) = (default, default, 0, false);

        if (Limbs.Clip == V0CostumeState.NoClip)
        {
            return;
        }

        if (_queueIndex < 1)
        {
            Limbs.ForgetClip();
            SetDirection(Facing);
            SpeakCheck();
        }
        else
        {
            Facing = 0;
            DirectionUpdate();
            Animate(OldDirection);
        }
    }

    /// <summary>The mouth's clip for the way the actor faces, unless it has no mouth. Mirrors <c>speakCheck</c>.</summary>
    private void SpeakCheck()
    {
        if ((Sound & TalkNoMouth) != 0)
        {
            return;
        }

        Limbs.Repeat = -1;
        Animate(OldDirection + ((Speaking & 0x80) != 0 ? ClipMouthOpen : ClipMouthShut));
    }
}
