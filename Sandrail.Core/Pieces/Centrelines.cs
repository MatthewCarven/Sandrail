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
