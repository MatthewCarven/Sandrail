namespace Sandrail.Core.Cars;

/// <summary>
/// A wing: a part a car is fitted with, which presses it onto the road at speed and drags it back (M1d).
/// </summary>
/// <remarks>
/// <para>
/// <b>A part, named by ID, not a set of numbers in the car's spec</b>, because his plan for them is that
/// they're earned and paid for (2026-11-23): a car's spec says which wing it has
/// (<see cref="CarSpec.WingId"/>), and how it's set is the car's own (<see cref="CarSpec.WingAngleDeg"/>,
/// <see cref="CarSpec.WingBackM"/>, <see cref="CarSpec.WingHeightM"/>). What a wing can do is the
/// part's: its size, and how steep it can be set before it stalls.
/// </para>
/// <para>
/// <b>The law is a thin wing's, made simple.</b> Downforce grows with angle up to the stall, and past it
/// falls away to half over <see cref="StallFadeDeg"/>. Drag is a little always, plus the price of the
/// downforce (its square, which is what makes a steep setting cost top speed), plus a steep climb past
/// the stall. So the angle has a best setting for each wing and a worse one either side of it, which is
/// a choice worth making in a garage.
/// </para>
/// </remarks>
/// <param name="Id">Namespaced, like every content ID: a car's file names its wing by this.</param>
/// <param name="Name">What the garage calls it.</param>
/// <param name="AreaM2">Its area seen from above. Downforce and drag both scale with it.</param>
/// <param name="StallDeg">
/// The steepest it gains at. A better wing stalls later, so it can be set steeper for more downforce.
/// </param>
public sealed record Wing(string Id, string Name, float AreaM2, float StallDeg)
{
    /// <summary>The lift coefficient gained per degree, below the stall: a stubby wing's, about two thirds of thin-aerofoil theory's.</summary>
    public const float LiftPerDeg = 0.08f;

    /// <summary>The drag coefficient it has at any angle.</summary>
    public const float ProfileDrag = 0.05f;

    /// <summary>The drag coefficient per lift coefficient squared: induced drag, for a wing about three times as wide as it is deep.</summary>
    public const float InducedDrag = 0.15f;

    /// <summary>
    /// The drag coefficient added per degree past the stall: enough that drag still climbs faster past it
    /// than before it, though the induced drag falls with the downforce there.
    /// </summary>
    public const float StallDragPerDeg = 0.06f;

    /// <summary>How many degrees past the stall it takes for the downforce to fall to half.</summary>
    public const float StallFadeDeg = 10.0f;

    /// <summary>
    /// The downforce coefficient at this angle to the air, degrees. Below 0 it's negative: the air is
    /// meeting the wing from underneath, and it lifts. That's what makes a wing a tail fin in the air.
    /// </summary>
    public float LiftCoefficient(float angleDeg)
    {
        float angle = MathF.Abs(angleDeg);
        float lift;
        if (angle <= StallDeg)
        {
            lift = LiftPerDeg * angle;
        }
        else
        {
            float past = MathF.Min((angle - StallDeg) / StallFadeDeg, 1.0f);
            lift = LiftPerDeg * StallDeg * (1.0f - (0.5f * past));
        }

        return MathF.Sign(angleDeg) * lift;
    }

    /// <summary>The drag coefficient at this angle to the air, degrees, either way up.</summary>
    public float DragCoefficient(float angleDeg)
    {
        float lift = LiftCoefficient(angleDeg);
        float stalled = MathF.Max(MathF.Abs(angleDeg) - StallDeg, 0.0f);
        return ProfileDrag + (InducedDrag * lift * lift) + (StallDragPerDeg * stalled);
    }
}

/// <summary>
/// The wings that exist, in code until there's a second set of them to express, as with
/// <see cref="Surfaces"/>. Ordered from none to the most, which is the order they'd be earned in.
/// </summary>
public static class Wings
{
    /// <summary>No wing: the car as it comes.</summary>
    public static readonly Wing None = new("base:wing-none", "No wing", 0.0f, 0.0f);

    /// <summary>A small lip on the tail: a little, for very little drag.</summary>
    public static readonly Wing Lip = new("base:wing-lip", "Lip spoiler", 0.35f, 10.0f);

    /// <summary>A plate on two struts: the everyday wing.</summary>
    public static readonly Wing Strut = new("base:wing-strut", "Strut wing", 0.8f, 14.0f);

    /// <summary>Two planes, one behind the other, so it can be set steep before it stalls: the most, and the most drag.</summary>
    public static readonly Wing TwinPlane = new("base:wing-twin", "Twin-plane wing", 1.3f, 18.0f);

    public static IReadOnlyList<Wing> All { get; } = [None, Lip, Strut, TwinPlane];

    /// <summary>The wing with this ID, or null. A bare name (<c>wing-lip</c>) is read as <c>base:</c>.</summary>
    public static Wing? Find(string id)
    {
        string full = id.Contains(':', StringComparison.Ordinal) ? id : "base:" + id;
        return All.FirstOrDefault(w => w.Id == full);
    }
}
