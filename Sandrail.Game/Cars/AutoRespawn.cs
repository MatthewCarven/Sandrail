using System;
using Godot;
using Sandrail.Core.Driving;

namespace Sandrail.Game.Cars;

/// <summary>
/// The auto-respawn (M1d, decision 4): watches the car each tick with <see cref="StuckWatch"/> and puts
/// it back where R would when it's been tipped over and nearly stopped for three seconds, or has fallen
/// below the world.
/// </summary>
/// <remarks>
/// Its own node, so the game and the respawn oracle use the same one. The other oracles go without it,
/// so a crash in one of them is reported as a crash rather than quietly put right.
/// </remarks>
public partial class AutoRespawn : Node
{
    private readonly StuckWatch _watch = new();

    public required Buggy Car { get; init; }

    /// <summary>Where to put the car back: wherever R would, asked at the moment it's needed.</summary>
    public required Func<Transform3D> Home { get; init; }

    /// <summary>Called on the tick it puts the car back, with why.</summary>
    public event Action<StuckReason>? Respawned;

    /// <summary>Seconds until the car is put back, while it's stuck; null when it isn't.</summary>
    public float? SecondsLeft => _watch.StuckFor > 0.0f ? _watch.SecondsLeft : null;

    /// <summary>For a respawn the player asked for: the count starts again.</summary>
    public void Reset() => _watch.Reset();

    public override void _PhysicsProcess(double delta)
    {
        float upright = Car.GlobalBasis.Y.Normalized().Dot(Vector3.Up);
        if (_watch.Update(upright, Car.LinearVelocity.Length(), Car.GlobalPosition.Y, (float)delta) is StuckReason reason)
        {
            Car.Respawn(Home());
            Respawned?.Invoke(reason);
        }
    }
}
