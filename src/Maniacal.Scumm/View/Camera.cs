namespace Maniacal.Scumm.View;

/// <summary>What the camera does each step. Mirrors ScummVM's <c>kNormalCameraMode</c>, <c>kFollowActorCameraMode</c> and <c>kPanningCameraMode</c>.</summary>
public enum CameraMode
{
    Normal = 1,
    FollowActor = 2,
    Panning = 3,
}

/// <summary>
/// The camera of versions 1 and 2: the x of the centre of the 320-pixel view, in room pixels,
/// moving 8 pixels a step. Mirrors ScummVM's <c>camera.cpp</c> (<c>setCameraAt</c>,
/// <c>setCameraFollows</c>, <c>moveCamera</c>, <c>cameraMoved</c>, <c>panCameraTo</c>) for
/// versions before 7, which have no fast-scroll or scroll-script variables.
/// </summary>
public sealed class Camera
{
    public const int ScreenWidth = 320;
    public const int Strips = ScreenWidth / 8;

    /// <summary>Mirrors <c>camera._leftTrigger</c> and <c>_rightTrigger</c>: the view columns past which a followed actor moves the camera.</summary>
    public const int LeftTrigger = 10, RightTrigger = 30;

    private int _roomWidth = ScreenWidth;

    public CameraMode Mode { get; private set; } = CameraMode.Normal;

    /// <summary>The centre of the view, in room pixels.</summary>
    public int X { get; private set; } = ScreenWidth / 2;

    /// <summary>Where the camera is heading.</summary>
    public int DestinationX { get; private set; } = ScreenWidth / 2;

    /// <summary>The actor followed in <see cref="CameraMode.FollowActor"/>.</summary>
    public int Follows { get; private set; }

    public bool MovingToActor { get; private set; }

    /// <summary>The scripts' limits on the camera centre: variables 23 and 24 (<c>VAR_CAMERA_MIN_X</c>, <c>VAR_CAMERA_MAX_X</c>).</summary>
    public int MinX { get; set; } = ScreenWidth / 2;

    public int MaxX { get; set; } = ScreenWidth / 2;

    /// <summary>The first strip of the room in view (<c>_screenStartStrip</c>).</summary>
    public int StartStrip { get; private set; }

    public int EndStrip => StartStrip + Strips - 1;

    /// <summary>The room x at the left edge of the view (<c>_virtscr[kMainVirtScreen].xstart</c>).</summary>
    public int Left => StartStrip * 8;

    /// <summary>
    /// Starts a room: the camera clamps to its width from now on, and the script limits are reset
    /// to the whole room, as <c>startScene</c> sets <c>VAR_CAMERA_MIN_X</c> and <c>VAR_CAMERA_MAX_X</c>.
    /// </summary>
    public void SetRoom(int roomWidth)
    {
        _roomWidth = roomWidth;
        MinX = ScreenWidth / 2;
        MaxX = roomWidth - ScreenWidth / 2;
        Moved();
    }

    /// <summary>Mirrors <c>setCameraAtEx</c>: stop following and jump.</summary>
    public void SetAtEx(int x)
    {
        Mode = CameraMode.Normal;
        X = x;
        SetAt(x);
        MovingToActor = false;
    }

    /// <summary>Mirrors <c>setCameraAt</c>: jump, unless following an actor that is near enough to scroll to.</summary>
    public void SetAt(int x)
    {
        if (Mode != CameraMode.FollowActor || Math.Abs(x - X) > ScreenWidth / 2)
        {
            X = x;
        }

        DestinationX = x;
        X = Math.Clamp(X, MinX, Math.Max(MinX, MaxX));
    }

    /// <summary>Mirrors <c>setCameraFollows</c>, for an actor already in the room; <paramref name="actorX"/> in room pixels.</summary>
    public void Follow(int actor, int actorX, bool setCamera = false)
    {
        Mode = CameraMode.FollowActor;
        Follows = actor;

        int column = actorX / 8 - StartStrip;
        if (column < LeftTrigger || column > RightTrigger || setCamera)
        {
            SetAt(actorX);
        }
    }

    /// <summary>Mirrors <c>panCameraTo</c>.</summary>
    public void PanTo(int x)
    {
        DestinationX = x;
        Mode = CameraMode.Panning;
        MovingToActor = false;
    }

    /// <summary>
    /// One step of the main loop. Mirrors <c>moveCamera</c>: back inside the script limits first,
    /// then towards the destination, or the followed actor once it passes a trigger column.
    /// </summary>
    /// <param name="followedActorX">The followed actor's x in room pixels, when following.</param>
    public void Move(Func<int>? followedActorX = null)
    {
        X &= ~7;

        if (X < MinX)
        {
            X += 8;
            Moved();
            return;
        }

        if (X > MaxX)
        {
            X -= 8;
            Moved();
            return;
        }

        if (Mode == CameraMode.FollowActor && followedActorX is not null)
        {
            int column = followedActorX() / 8 - StartStrip;
            if (column < LeftTrigger || column > RightTrigger)
            {
                MovingToActor = true;
            }
        }

        if (MovingToActor && followedActorX is not null)
        {
            DestinationX = followedActorX();
        }

        DestinationX = Math.Clamp(DestinationX, MinX, Math.Max(MinX, MaxX));

        if (X < DestinationX)
        {
            X += 8;
        }

        if (X > DestinationX)
        {
            X -= 8;
        }

        if (MovingToActor && followedActorX is not null && X / 8 == followedActorX() / 8)
        {
            MovingToActor = false;
        }

        Moved();
    }

    /// <summary>Mirrors <c>cameraMoved</c> before version 7: keep the view inside the room, and find its strips.</summary>
    private void Moved()
    {
        if (X < ScreenWidth / 2)
        {
            X = ScreenWidth / 2;
        }
        else if (X > _roomWidth - ScreenWidth / 2)
        {
            X = Math.Max(ScreenWidth / 2, _roomWidth - ScreenWidth / 2);
        }

        StartStrip = X / 8 - Strips / 2;
    }
}
