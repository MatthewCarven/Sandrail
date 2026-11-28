using System.Numerics;

namespace Sandrail.Core.Pieces;

/// <summary>
/// The middle of a road's surface, as a frame at any distance along it (M1-PLAN, M1c). The sweep
/// places a cross-section at frames along one and joins them up.
/// </summary>
public interface ICentreline
{
    float Length { get; }

    /// <summary>
    /// How far the road turns, pitches or loops over its length, degrees: the sweep cuts a curve into
    /// enough segments that none turns more than a few degrees, and a straight into one.
    /// </summary>
    float TurnDeg { get; }

    /// <summary>The frame at a distance from the start, clamped to [0, <see cref="Length"/>].</summary>
    Frame FrameAt(float distance);
}

/// <summary>A straight, in whatever direction its start faces, so after a pitch it's a ramp.</summary>
public sealed class LineCentreline(Frame start, float length) : ICentreline
{
    public float Length { get; } = length;

    public float TurnDeg => 0.0f;

    public Frame FrameAt(float distance) =>
        start with { Position = start.Position + (start.Forward * Math.Clamp(distance, 0.0f, Length)) };
}

/// <summary>
/// An arc in the vertical plane: positive angles bend nose-up (the foot of a ramp, a landing's
/// run-out), negative bend nose-down (a crest, the top of a ramp).
/// </summary>
public sealed class PitchCentreline : ICentreline
{
    private readonly Frame _start;
    private readonly float _radius;
    private readonly float _bend;

    public PitchCentreline(Frame start, float angleDeg, float radius)
    {
        _start = start;
        _radius = radius;
        _bend = MathF.Sign(angleDeg);
        Length = radius * MathF.Abs(angleDeg) * (MathF.PI / 180.0f);
        TurnDeg = MathF.Abs(angleDeg);
    }

    public float Length { get; }

    public float TurnDeg { get; }

    public Frame FrameAt(float distance)
    {
        float phi = Math.Clamp(distance, 0.0f, Length) / _radius;
        float cos = MathF.Cos(phi);
        float sin = MathF.Sin(phi);
        float k = _bend;
        Vector3 f0 = _start.Forward;
        Vector3 u0 = _start.Up;

        // The centre of the arc is above the road for a nose-up bend and below it for a crest.
        Vector3 centre = _start.Position + (u0 * (k * _radius));
        Vector3 position = centre + (((-k * cos * u0) + (sin * f0)) * _radius);
        Vector3 forward = (f0 * cos) + (u0 * (k * sin));
        Vector3 up = (u0 * cos) - (f0 * (k * sin));
        return new Frame(position, Vector3.Normalize(forward), Vector3.Normalize(up), _start.Right);
    }
}

/// <summary>
/// An arc in the road's own plane: positive angles turn right. A bank tilts the surface toward the
/// inside of the turn, easing in and out so both ends are flat and meet flat road.
/// </summary>
/// <remarks>
/// The bank pivots on an edge, not the middle: without that, a 30° bank on a 10 m road sinks its inner
/// edge 2.5 m into the ground. <c>bankPivotM</c> is how far that edge is from the middle, usually half
/// the road's width.
/// </remarks>
public sealed class TurnCentreline : ICentreline
{
    private readonly Frame _start;
    private readonly float _radius;
    private readonly float _side;
    private readonly float _bank;
    private readonly float _pivot;
    private readonly float _ease;

    public TurnCentreline(Frame start, float angleDeg, float radius, float bankDeg = 0.0f, float bankPivotM = 0.0f, float bankEase = 0.25f)
    {
        _start = start;
        _radius = radius;
        _side = MathF.Sign(angleDeg);
        _bank = bankDeg * (MathF.PI / 180.0f);
        _pivot = bankPivotM;
        _ease = bankEase;
        Length = radius * MathF.Abs(angleDeg) * (MathF.PI / 180.0f);
        TurnDeg = MathF.Abs(angleDeg);
    }

    public float Length { get; }

    public float TurnDeg { get; }

    public Frame FrameAt(float distance)
    {
        float d = Math.Clamp(distance, 0.0f, Length);
        float phi = d / _radius;
        float s = _side;
        Vector3 f0 = _start.Forward;
        Vector3 u0 = _start.Up;
        Vector3 r0 = _start.Right;

        Vector3 centre = _start.Position + (r0 * (s * _radius));
        Vector3 position = centre + (((-s * MathF.Cos(phi) * r0) + (MathF.Sin(phi) * f0)) * _radius);
        Vector3 forward = Vector3.Normalize((f0 * MathF.Cos(phi)) + (r0 * (s * MathF.Sin(phi))));
        Vector3 flatRight = Vector3.Normalize(Vector3.Cross(forward, u0));

        float bank = _bank * BankShare(d / Length);
        Vector3 up = Vector3.Normalize((u0 * MathF.Cos(bank)) + (flatRight * (s * MathF.Sin(bank))));
        position += u0 * (_pivot * MathF.Sin(MathF.Abs(bank)));
        return Frame.Of(position, forward, up);
    }

    /// <summary>0 at both ends, 1 through the middle, with smoothstep ramps over the ease fraction.</summary>
    private float BankShare(float t)
    {
        if (_ease <= 0.0f)
        {
            return 1.0f;
        }

        float x = Math.Clamp(MathF.Min(t, 1.0f - t) / _ease, 0.0f, 1.0f);
        return x * x * (3.0f - (2.0f * x));
    }
}

/// <summary>
/// A turn with gradual ends (PIECES-PLAN, P3): its curve tightens from straight to its middle radius over the first
/// <c>transition</c> share of its length and loosens to straight again over the last, and its bank rises and falls ahead
/// of it, so the corner is nearly straight wherever it's nearly flat. It ends exactly where an arc of
/// <c>radius</c> through the same angle would, so it fits the grid as that arc does; its middle is tighter to make up for
/// its gentle ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why</b>: a <see cref="TurnCentreline"/>'s bank eases in from flat while its curve is at full tightness from end to
/// end, so both its ends are a flat corner whatever its bank in the middle. A 40° bank on a 30 m arc held 69 km/h, as a
/// 20° one did, and a quicker ease only launched the car (worklog, 2026-11-28).
/// </para>
/// <para>
/// <b>The shape</b>: curvature is the middle's times a share that ramps 0 to 1 by smoothstep, the shape of
/// <see cref="TurnCentreline"/>'s bank ease, and the bank is the full bank times the share's square root. Heading is closed-form (smoothstep's integral, <c>x³ − x⁴/2</c>) and
/// position is integrated in doubles at a few centimetres. The curve is symmetric about its middle, so its chord points
/// half the angle round, as the arc's does, and its size scales with the middle radius: the middle radius is the arc's
/// chord over the chord of the same shape at a radius of 1.
/// </para>
/// </remarks>
public sealed class TransitionTurnCentreline : ICentreline
{
    private const double StepM = 0.02;

    private readonly Frame _start;
    private readonly float _side;
    private readonly float _bank;
    private readonly float _pivot;
    private readonly double _transition;
    private readonly double _turn;
    private readonly double _length;
    private readonly double[] _forward;
    private readonly double[] _across;

    /// <param name="start">Where it begins.</param>
    /// <param name="angleDeg">How far it turns, positive to the right; at most 180 either way.</param>
    /// <param name="radius">The arc it stands in for: it ends where this arc through the angle would.</param>
    /// <param name="transition">The share of its length each end takes to tighten or loosen, above 0 and at most 0.5.</param>
    /// <param name="bankDeg">Its bank through the middle, toward the inside.</param>
    /// <param name="bankPivotM">How far from the middle the bank pivots: the inside edge, as a turn's.</param>
    public TransitionTurnCentreline(Frame start, float angleDeg, float radius, float transition, float bankDeg = 0.0f, float bankPivotM = 0.0f)
    {
        _start = start;
        _side = MathF.Sign(angleDeg);
        _bank = bankDeg * (MathF.PI / 180.0f);
        _pivot = bankPivotM;
        _transition = transition;
        _turn = Math.Abs(angleDeg) * (Math.PI / 180.0);

        // The same shape at a middle radius of 1, then scaled so its chord is the arc's. Both chords point half the angle
        // round; past 180° gentle ends can point this one backwards, which no radius mends, so it's held to 180°.
        if (Math.Abs(angleDeg) > 180.0f || !(transition > 0.0f && transition <= 0.5f))
        {
            throw new ArgumentOutOfRangeException(nameof(angleDeg), $"A turn with gradual ends turns at most 180° with a transition above 0 and at most 0.5, not {angleDeg}° and {transition}.");
        }

        double unitLength = _turn / (1.0 - _transition);
        (double f, double a) = Integrate(unitLength, null, null);
        double along = (f * Math.Cos(_turn / 2.0)) + (a * Math.Sin(_turn / 2.0));
        double middle = 2.0 * radius * Math.Sin(_turn / 2.0) / along;
        MiddleRadius = (float)middle;
        _length = unitLength * middle;
        Length = (float)_length;
        TurnDeg = (float)(_turn / (1.0 - _transition) * (180.0 / Math.PI));

        int steps = (int)Math.Ceiling(_length / StepM);
        _forward = new double[steps + 1];
        _across = new double[steps + 1];
        Integrate(_length, _forward, _across);
    }

    public float Length { get; }

    /// <summary>
    /// The turn as the sweep reads it, at the middle's rate over the whole length, so no segment of the mesh turns more
    /// than a few degrees where it's tightest.
    /// </summary>
    public float TurnDeg { get; }

    /// <summary>Its radius through the middle, where the curve is tightest and the bank full.</summary>
    public float MiddleRadius { get; }

    public Frame FrameAt(float distance)
    {
        double d = Math.Clamp(distance, 0.0f, Length);
        double u = _length > 0.0 ? d / _length : 0.0;
        double phi = Heading(u);

        int steps = _forward.Length - 1;
        double at = d / _length * steps;
        int i = Math.Min((int)at, steps - 1);
        double w = at - i;
        double forwardM = _forward[i] + ((_forward[i + 1] - _forward[i]) * w);
        double acrossM = _across[i] + ((_across[i + 1] - _across[i]) * w);

        float s = _side;
        Vector3 f0 = _start.Forward;
        Vector3 u0 = _start.Up;
        Vector3 r0 = _start.Right;
        Vector3 position = _start.Position + (f0 * (float)forwardM) + (r0 * (float)(s * acrossM));
        Vector3 forward = Vector3.Normalize((f0 * (float)Math.Cos(phi)) + (r0 * (float)(s * Math.Sin(phi))));
        Vector3 flatRight = Vector3.Normalize(Vector3.Cross(forward, u0));

        // The bank leads the curve, as the square root of its share: banked in step, halfway in the curve is a 53 m arc on
        // half the bank, slower than the middle; at the root, it's on 71% of it, and nowhere is slower than the middle.
        float bank = _bank * (float)Math.Sqrt(Share(u));
        Vector3 up = Vector3.Normalize((u0 * MathF.Cos(bank)) + (flatRight * (s * MathF.Sin(bank))));
        position += u0 * (_pivot * MathF.Sin(MathF.Abs(bank)));
        return Frame.Of(position, forward, up);
    }

    /// <summary>0 at both ends, 1 through the middle, by smoothstep over the transition share: the curve's and the bank's.</summary>
    private double Share(double u)
    {
        double x = Math.Clamp(Math.Min(u, 1.0 - u) / _transition, 0.0, 1.0);
        return x * x * (3.0 - (2.0 * x));
    }

    /// <summary>How far it has turned at a share of its length, radians: the curvature's share integrated, closed-form.</summary>
    private double Heading(double u)
    {
        double t = _transition;
        static double Ramp(double x) => (x * x * x) - (x * x * x * x / 2.0);
        double ramped;
        if (u <= t)
        {
            ramped = t * Ramp(u / t);
        }
        else if (u >= 1.0 - t)
        {
            ramped = (1.0 - t) - (t * Ramp((1.0 - u) / t));
        }
        else
        {
            ramped = (t / 2.0) + (u - t);
        }

        return _turn * ramped / (1.0 - t);
    }

    /// <summary>
    /// Forward and across from the start, over a length, by the midpoint rule in steps of about <see cref="StepM"/>;
    /// into the tables when given. Where it ends.
    /// </summary>
    private (double Forward, double Across) Integrate(double length, double[]? forward, double[]? across)
    {
        int steps = forward?.Length - 1 ?? (int)Math.Ceiling(length / StepM * 50.0);
        double f = 0.0;
        double a = 0.0;
        for (int i = 0; i < steps; i++)
        {
            double phi = Heading((i + 0.5) / steps);
            double ds = length / steps;
            f += Math.Cos(phi) * ds;
            a += Math.Sin(phi) * ds;
            if (forward is not null)
            {
                forward[i + 1] = f;
                across![i + 1] = a;
            }
        }

        return (f, a);
    }
}

/// <summary>
/// A level turn that climbs as it goes (PIECES-PLAN, the climbing corner): the turn's own frames lifted by a height that
/// eases from nothing to <c>rise</c> by smoothstep over its whole length, and pitched to match, so it leaves and meets
/// level road and its frames face the way the road climbs (the slab, the racing line and the crest cap read that).
/// </summary>
/// <remarks>
/// Steepest halfway, at 1.5 × rise / length: a 30 m corner climbing a level (2.5 m) is at 8% there, 4.6°, gentler than
/// the slope's 7.2°. Its ends bend at 6 × rise / length², a crest of about 150 m at the top of that one, which throws the
/// car only far above what the corner can be taken at. The distance along it is the turn's own, measured on the level:
/// the climb makes the road under 1% longer than that.
/// </remarks>
public sealed class ClimbingCentreline : ICentreline
{
    private readonly ICentreline _level;
    private readonly Vector3 _up;
    private readonly float _rise;

    /// <param name="level">The turn on the level, from <paramref name="start"/>.</param>
    /// <param name="start">Where the turn begins, whose up the road climbs along.</param>
    /// <param name="riseM">How far it climbs, metres; negative to come down.</param>
    public ClimbingCentreline(ICentreline level, Frame start, float riseM)
    {
        _level = level;
        _up = start.Up;
        _rise = riseM;
        Length = level.Length;
        float steepest = MathF.Atan(1.5f * MathF.Abs(riseM) / MathF.Max(Length, 1e-3f)) * (180.0f / MathF.PI);
        TurnDeg = level.TurnDeg + (2.0f * steepest);
    }

    public float Length { get; }

    /// <summary>The turn's, and the climb's pitching up and back, for the sweep.</summary>
    public float TurnDeg { get; }

    public Frame FrameAt(float distance)
    {
        float d = Math.Clamp(distance, 0.0f, Length);
        Frame level = _level.FrameAt(d);
        float u = Length > 0.0f ? d / Length : 0.0f;
        float height = _rise * u * u * (3.0f - (2.0f * u));
        float slope = Length > 0.0f ? _rise * 6.0f * u * (1.0f - u) / Length : 0.0f;

        // Pitched about the level right, nose up for a climb: the turn's forward and its banked up together.
        Vector3 axis = Vector3.Normalize(Vector3.Cross(level.Forward, _up));
        var pitch = Quaternion.CreateFromAxisAngle(axis, MathF.Atan(slope));
        return Frame.Of(level.Position + (_up * height), Vector3.Transform(level.Forward, pitch), Vector3.Transform(level.Up, pitch));
    }
}

/// <summary>
/// A loop: tall rather than round, and offset sideways so its exit runs beside its entry.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tall, because a round loop is brutal.</b> At the least speed that gets a car over the top of a
/// circle, the bottom pulls 6 g whatever the radius, so anything faster bottoms the buggy's suspension
/// out. Here the radius runs from <c>bottomRadius</c> at the foot to <c>topRadius</c> at the top
/// (<c>R = top + (bottom − top) · ((1 + cos θ) / 2)^sharpness</c>), the shape real stunt loops use:
/// gentle where the car is fast and low, tight where it's slow and high.
/// </para>
/// <para>
/// <b>Offset in proportion to the distance round, not to the angle</b>, and that makes it drivable
/// with the wheels straight. The road is an extrusion of the loop's outline along one fixed right, so
/// unrolled it's a flat strip, and a car rolling straight on a flat strip goes in a straight line: a
/// sideways shift that grows evenly with distance is exactly that line. The price is that the road
/// approaches at an angle, <see cref="EntryHeadingDeg"/>, so the run-up and the exit are built along
/// <see cref="FrameAt"/>'s first frame, which carries the same fixed right.
/// </para>
/// </remarks>
public sealed class LoopCentreline : ICentreline
{
    private const int Steps = 2048;

    private readonly Frame _plane;
    private readonly float _bottomRadius;
    private readonly float _topRadius;
    private readonly float _sharpness;
    private readonly float _slope;
    private readonly float[] _planarDistance = new float[Steps + 1];
    private readonly Vector2[] _planarPosition = new Vector2[Steps + 1];

    /// <param name="plane">
    /// The foot of the loop, square and level: its forward is the loop's direction of travel, its right
    /// the way the exit is offset.
    /// </param>
    public LoopCentreline(Frame plane, float bottomRadius, float topRadius, float offset, float sharpness = 1.5f)
    {
        _plane = plane;
        _bottomRadius = bottomRadius;
        _topRadius = topRadius;
        _sharpness = sharpness;

        // Midpoint integration of the outline in the (forward, up) plane. The radius is symmetric
        // about the top, so the rises and falls pair off and the exit is level with the entry.
        float step = 2.0f * MathF.PI / Steps;
        var at = Vector2.Zero;
        float travelled = 0.0f;
        for (int i = 0; i < Steps; i++)
        {
            _planarPosition[i] = at;
            _planarDistance[i] = travelled;
            float theta = (i + 0.5f) * step;
            float ds = RadiusAt(theta) * step;
            at += new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * ds;
            travelled += ds;
        }

        _planarPosition[Steps] = at;
        _planarDistance[Steps] = travelled;
        PlanarLength = travelled;
        Offset = offset;
        _slope = offset / travelled;
        Length = travelled * MathF.Sqrt(1.0f + (_slope * _slope));
        EntryHeadingDeg = MathF.Atan(_slope) * (180.0f / MathF.PI);
    }

    public float Length { get; }

    public float TurnDeg => 360.0f;

    /// <summary>The length of the loop's outline, without its sideways shift.</summary>
    public float PlanarLength { get; }

    public float Offset { get; }

    /// <summary>How far right of the loop's own forward the road must arrive and leave, degrees.</summary>
    public float EntryHeadingDeg { get; }

    public float RadiusAt(float theta) =>
        _topRadius + ((_bottomRadius - _topRadius) * MathF.Pow((1.0f + MathF.Cos(theta)) / 2.0f, _sharpness));

    public Frame FrameAt(float distance)
    {
        float planar = Math.Clamp(distance, 0.0f, Length) / MathF.Sqrt(1.0f + (_slope * _slope));
        int i = Array.BinarySearch(_planarDistance, planar);
        if (i < 0)
        {
            i = Math.Max(0, ~i - 1);
        }

        i = Math.Min(i, Steps - 1);
        float span = _planarDistance[i + 1] - _planarDistance[i];
        float t = span > 0.0f ? (planar - _planarDistance[i]) / span : 0.0f;
        float theta = (i + t) * (2.0f * MathF.PI / Steps);
        Vector2 outline = Vector2.Lerp(_planarPosition[i], _planarPosition[i + 1], t);

        Vector3 f0 = _plane.Forward;
        Vector3 u0 = _plane.Up;
        Vector3 r0 = _plane.Right;
        Vector3 along = (f0 * MathF.Cos(theta)) + (u0 * MathF.Sin(theta));
        Vector3 inward = (u0 * MathF.Cos(theta)) - (f0 * MathF.Sin(theta));

        Vector3 position = _plane.Position + (f0 * outline.X) + (u0 * outline.Y) + (r0 * (_slope * planar));
        Vector3 forward = Vector3.Normalize(along + (r0 * _slope));
        return new Frame(position, forward, inward, r0);
    }
}
