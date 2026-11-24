using System.Numerics;

namespace Sandrail.Core.Ghosts;

/// <summary>
/// The car at one moment of a run (M4-PLAN, "The model"): where it is, which way it faces, its steering,
/// how far each wheel hangs, and the pedals. Thirty a second make a ghost (fact 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>What happened, not the inputs</b> (fact 1): a pose can't drift, where inputs fed back through the
/// physics would.
/// </para>
/// <para>
/// <b>Kept as the file holds it</b>: <see cref="Quantised"/> rounds each part to what's saved, and a ghost
/// keeps only quantised snapshots, so a ghost saved and loaded is the same ghost to the last bit. Position to
/// the millimetre, rotation to a 16-bit component, steering to a hundredth of a degree, droop to the
/// millimetre, pedals to a 255th.
/// </para>
/// </remarks>
public readonly record struct Snapshot
{
    /// <summary>The body's origin, metres, in the game's axes (Y up, forward -Z).</summary>
    public Vector3 Position { get; init; }

    /// <summary>The body's rotation; quantised ones have W at or above 0.</summary>
    public Quaternion Rotation { get; init; }

    /// <summary>The steering angle at the road wheels, degrees, positive right; each front wheel's own comes from the spec.</summary>
    public float SteerDeg { get; init; }

    /// <summary>How far each wheel hangs below its mount, metres: front left, front right, rear left, rear right.</summary>
    public Vector4 Droop { get; init; }

    /// <summary>0..1.</summary>
    public float Throttle { get; init; }

    /// <summary>The brakes as applied, 0..1: the brake key rolling forward, the throttle rolling backward. What the brake lights show.</summary>
    public float Brake { get; init; }

    /// <summary>Driving backwards: what the reversing lights show. Kept in the flags' second bit, which older ghosts leave clear.</summary>
    public bool Reversing { get; init; }

    /// <summary>How far the rear wheels are locked, 0..1.</summary>
    public float Handbrake { get; init; }

    /// <summary>
    /// The car was put back here (R, or the auto-respawn): playback mustn't draw it flying from where it was
    /// (fact 6), so it holds the snapshot before until this one's time.
    /// </summary>
    public bool Cut { get; init; }

    /// <summary>Each part rounded to what the file keeps.</summary>
    public Snapshot Quantised()
    {
        // Not normalised here: what comes in is a rotation already, and normalising a quantised one again
        // would nudge its components, so a ghost loaded and saved again wouldn't be the same bytes.
        Quaternion q = Rotation == default ? Quaternion.Identity : Rotation;
        if (q.W < 0.0f)
        {
            q = -q;
        }

        return this with
        {
            Position = new Vector3(Millimetres(Position.X), Millimetres(Position.Y), Millimetres(Position.Z)),
            Rotation = new Quaternion(Unit(q.X), Unit(q.Y), Unit(q.Z), Unit(q.W)),
            SteerDeg = FromCentidegrees(ToCentidegrees(SteerDeg)),
            Droop = new Vector4(DroopMm(Droop.X), DroopMm(Droop.Y), DroopMm(Droop.Z), DroopMm(Droop.W)),
            Throttle = FromByte(ToByte(Throttle)),
            Brake = FromByte(ToByte(Brake)),
            Handbrake = FromByte(ToByte(Handbrake)),
        };
    }

    // The steps the file counts in. Each pair turns a value into the file's integer and back, and a quantised
    // value turned into its integer gives the same integer, so saving and loading changes nothing.
    internal static int ToMillimetres(float metres) => (int)MathF.Round(Math.Clamp(metres, -2_000_000.0f, 2_000_000.0f) * 1000.0f);

    internal static float FromMillimetres(int mm) => mm / 1000.0f;

    internal static short ToUnit(float component) => (short)MathF.Round(Math.Clamp(component, -1.0f, 1.0f) * 32767.0f);

    internal static float FromUnit(short unit) => unit / 32767.0f;

    internal static short ToCentidegrees(float degrees) => (short)MathF.Round(Math.Clamp(degrees, -327.0f, 327.0f) * 100.0f);

    internal static float FromCentidegrees(short centi) => centi / 100.0f;

    internal static ushort ToDroopMm(float metres) => (ushort)MathF.Round(Math.Clamp(metres, 0.0f, 65.0f) * 1000.0f);

    internal static float FromDroopMm(ushort mm) => mm / 1000.0f;

    internal static byte ToByte(float share) => (byte)MathF.Round(Math.Clamp(share, 0.0f, 1.0f) * 255.0f);

    internal static float FromByte(byte b) => b / 255.0f;

    private static float Millimetres(float metres) => FromMillimetres(ToMillimetres(metres));

    private static float Unit(float component) => FromUnit(ToUnit(component));

    private static float DroopMm(float metres) => FromDroopMm(ToDroopMm(metres));

    /// <summary>Part way from one snapshot to the next: straight lines, and the rotation slerped.</summary>
    public static Snapshot Between(Snapshot a, Snapshot b, float share) => new()
    {
        Position = Vector3.Lerp(a.Position, b.Position, share),
        Rotation = Quaternion.Normalize(Quaternion.Slerp(a.Rotation, b.Rotation, share)),
        SteerDeg = a.SteerDeg + ((b.SteerDeg - a.SteerDeg) * share),
        Droop = Vector4.Lerp(a.Droop, b.Droop, share),
        Throttle = a.Throttle + ((b.Throttle - a.Throttle) * share),
        Brake = a.Brake + ((b.Brake - a.Brake) * share),
        Handbrake = a.Handbrake + ((b.Handbrake - a.Handbrake) * share),
        Reversing = share < 0.5f ? a.Reversing : b.Reversing,
    };
}
