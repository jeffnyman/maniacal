using Maniacal.Scumm.Costumes;

namespace Maniacal.Scumm.Game;

/// <summary>
/// An actor of the NES: a version 1 actor whose costume is the NES's, one limb whose animations
/// are lists of the costume set's frames. Mirrors <c>Actor_v2</c> with <c>NESCostumeLoader</c>.
/// </summary>
internal sealed class ActorNes(int number, IActorWorld world) : Actor(number, world)
{
    private NesCostume? _costumeNes;
    private bool _costumeNesLoaded;

    /// <summary>The costume, loaded when first needed, as <see cref="Actor.Costume"/> is for the other releases.</summary>
    public NesCostume? CostumeNes
    {
        get
        {
            if (!_costumeNesLoaded)
            {
                _costumeNes = CostumeNumber == 0 ? null : _world.LoadCostumeNes(CostumeNumber);
                _costumeNesLoaded = true;
            }

            return _costumeNes;
        }
    }

    protected override bool CostumeReady => CostumeNes is not null;

    /// <summary>An animation past the costume's last is left alone (<c>anim &gt; _numAnim</c>).</summary>
    protected override void DecodeAnimation(int animation)
    {
        if (animation <= NesCostume.LastAnimation)
        {
            State.DecodeNes(animation, CostumeNes!.FrameCount(animation));
        }
    }

    protected override bool IncreaseAnimations() => State.IncreaseNes();

    protected override void SelectCostume(int costume)
    {
        base.SelectCostume(costume);
        _costumeNes = null;
        _costumeNesLoaded = false;
    }
}
