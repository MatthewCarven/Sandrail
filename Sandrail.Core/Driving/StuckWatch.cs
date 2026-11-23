namespace Sandrail.Core.Driving;

/// <summary>Why a car was put back.</summary>
public enum StuckReason
{
    /// <summary>On its roof or its side, and nearly stopped, for <see cref="StuckWatch.Seconds"/>.</summary>
    TippedOver,

    /// <summary>Fallen below the world, off the end of the ground.</summary>
    Lost,
}

/// <summary>
/// The auto-respawn's rule (M1d, M1-PLAN decision 4): a car that can't drive on is put back by itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tipped over and nearly stopped for three seconds.</b> Decision 4 said upside down; on its side
/// counts too, more than 70° from upright, because a raycast car on its side has no wheel that can
/// reach the ground either. Nearly stopped is what keeps a car upside down at the top of a loop, at
/// 80 km/h, from counting. Any moment upright or moving starts the count again.
/// </para>
/// <para>
/// <b>Below the world, at once</b>: a car off the end of the ground otherwise falls for ever. Nothing is
/// built below 0, so 30 m under it is lost by any measure.
/// </para>
/// <para>
/// In Core and fed numbers, not a Godot body, so it's the same rule for a player, an oracle and
/// later a ghost or a bot, and is tested without a scene.
/// </para>
/// </remarks>
public sealed class StuckWatch
{
    /// <summary>How long a car must be tipped over and nearly stopped: decision 4's three seconds.</summary>
    public const float Seconds = 3.0f;

    /// <summary>The cosine of 70°: a car whose up is further than this from the world's is tipped over.</summary>
    public const float TippedCosine = 0.342f;

    /// <summary>Slower than this, metres a second, is nearly stopped.</summary>
    public const float NearlyStoppedMps = 2.0f;

    /// <summary>Below this height a car is lost.</summary>
    public const float LostBelowM = -30.0f;

    /// <summary>How long the car has been tipped over and nearly stopped, seconds; 0 when it isn't.</summary>
    public float StuckFor { get; private set; }

    /// <summary>How long until it's put back, while it's stuck.</summary>
    public float SecondsLeft => Seconds - StuckFor;

    /// <summary>
    /// One tick of the car: how upright it is (its up dotted with the world's, -1 on its roof to 1 on its
    /// wheels), its speed, and its height. Returns why it should be put back, on the tick it should, and
    /// null otherwise. Once it's said so the count starts again, so a respawn that takes a tick to land
    /// isn't asked for twice.
    /// </summary>
    public StuckReason? Update(float uprightness, float speedMps, float heightM, float step)
    {
        // Each test is written as a refusal of what's fine, so a NaN anywhere reads as stuck or lost.
        if (!(heightM >= LostBelowM))
        {
            StuckFor = 0.0f;
            return StuckReason.Lost;
        }

        bool tipped = !(uprightness > TippedCosine);
        bool stopped = !(speedMps >= NearlyStoppedMps);
        if (!(tipped && stopped))
        {
            StuckFor = 0.0f;
            return null;
        }

        StuckFor += step;
        if (StuckFor < Seconds)
        {
            return null;
        }

        StuckFor = 0.0f;
        return StuckReason.TippedOver;
    }

    /// <summary>Starts the count again, as after a respawn the player asked for.</summary>
    public void Reset() => StuckFor = 0.0f;
}
