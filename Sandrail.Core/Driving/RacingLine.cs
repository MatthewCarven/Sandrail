using System.Numerics;
using Sandrail.Core.Cars;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Driving;

/// <summary>
/// A path along a road, sampled every metre or so, with what a driver needs from it: where it goes, how
/// sharply it turns, how steeply it's banked, and how fast each bit of it can be taken.
/// </summary>
/// <remarks>
/// <para>
/// The autodriver's map (PLAN, "His ideas": an autodriver, 2026-11-23). Built first as the road's
/// centreline; <see cref="MinimumCurvature"/> then gives the line a driver would take, at his word:
/// <i>"draw a line around the track that minimizes the cornering and aim for this position taking into
/// account the wheel positions"</i>.
/// </para>
/// <para>
/// <b>Corner speeds are the banked-curve formula</b>, <c>v = √(g R (tan θ + μ) / (1 − μ tan θ))</c>, at a
/// fraction of the car's grip (the caution), capped at the car's top speed. A backward pass then slows
/// the approach to each corner so it can brake in time: on a closed lap it goes round twice, so the
/// braking for the first corner reaches back into the end of the lap.
/// </para>
/// </remarks>
public sealed class RacingLine
{
    private readonly Vector3[] _position;
    private readonly Vector3[] _forward;
    private readonly Vector3[] _up;
    private readonly Vector3[] _right;
    private readonly float[] _distance;
    private readonly int[] _piece;
    private readonly float[] _offset;
    private readonly float[] _curvature;
    private readonly float[] _bankDeg;
    private readonly float[] _hint;
    private readonly bool[] _straight;
    private readonly float[] _cap;

    /// <summary>The road's centreline as a line.</summary>
    /// <param name="closed">A lap: the last sample runs on into the first.</param>
    public RacingLine(IReadOnlyList<ICentreline> pieces, bool closed, float stepM = 1.0f)
        : this(Sample(pieces, closed, stepM), closed, new float[SampleCount(pieces, closed, stepM)], null, null, null)
    {
    }

    private RacingLine((Vector3[] Position, Vector3[] Forward, Vector3[] Up, Vector3[] Right, int[] Piece) s, bool closed, float[] offset, float[]? hint, bool[]? straight, float[]? cap)
    {
        _position = s.Position;
        _forward = s.Forward;
        _up = s.Up;
        _right = s.Right;
        _piece = s.Piece;
        _offset = offset;
        Closed = closed;
        _hint = hint ?? Enumerable.Repeat(float.NaN, _position.Length).ToArray();
        _straight = straight ?? new bool[_position.Length];
        _cap = cap ?? Enumerable.Repeat(float.PositiveInfinity, _position.Length).ToArray();

        _distance = new float[Count];
        for (int i = 1; i < Count; i++)
        {
            _distance[i] = _distance[i - 1] + Vector3.Distance(_position[i - 1], _position[i]);
        }

        Length = _distance[Count - 1] + (closed ? Vector3.Distance(_position[Count - 1], _position[0]) : 0.0f);

        _curvature = new float[Count];
        _bankDeg = new float[Count];
        for (int i = 0; i < Count; i++)
        {
            int before = Neighbour(i, -2);
            int after = Neighbour(i, 2);

            // Road beside a loop doesn't read its bend from the loop: a heading taken off road pointing at
            // the sky swings round, and a car at the foot was steered by it just as its wheels went straight
            // (2026-11-23).
            if (!_straight[i])
            {
                while (before != i && _straight[before])
                {
                    before = Neighbour(before, 1);
                }

                while (after != i && _straight[after])
                {
                    after = Neighbour(after, -1);
                }
            }

            float span = Span(before, after);
            float turn = Wrap(Heading(_forward[after]) - Heading(_forward[before]));
            _curvature[i] = span > 0.0f ? turn / span : 0.0f;
            _bankDeg[i] = MathF.Acos(Math.Clamp(_up[i].Y, -1.0f, 1.0f)) * (180.0f / MathF.PI);
        }
    }

    public int Count => _position.Length;

    public bool Closed { get; }

    public float Length { get; }

    public Vector3 PositionAt(int i) => _position[i];

    public float DistanceAt(int i) => _distance[i];

    /// <summary>Turn per metre in the horizontal plane, positive turning right.</summary>
    public float CurvatureAt(int i) => _curvature[i];

    public float BankDegAt(int i) => _bankDeg[i];

    /// <summary>How far right of the road's middle the line runs here, metres; 0 all the way on the centreline.</summary>
    public float OffsetAt(int i) => _offset[i];

    /// <summary>Which of the road's pieces a sample is on.</summary>
    public int PieceAt(int i) => _piece[i];

    /// <summary>
    /// The speed a stunt here is meant to be taken at, m/s (a jump's run-up, a loop), which the speed plan
    /// holds to; NaN where the road's own shape says how fast (M2-PLAN, "The model": a piece's speed hint).
    /// </summary>
    public float HintAt(int i) => _hint[i];

    /// <summary>Whether the wheels are held straight here: in a loop, which is driven that way (M1c).</summary>
    public bool StraightAt(int i) => _straight[i];

    /// <summary>
    /// The same line, with the stunts on it marked: where a speed hint holds, m/s (NaN for none), where the
    /// wheels are held straight, and where the speed is capped below what the bend allows (a crest, m/s;
    /// infinity for none).
    /// </summary>
    public RacingLine WithStunts(Func<int, float> hintMps, Func<int, bool> straight, Func<int, float>? capMps = null) => new(
        ((Vector3[])_position.Clone(), (Vector3[])_forward.Clone(), (Vector3[])_up.Clone(), (Vector3[])_right.Clone(), (int[])_piece.Clone()),
        Closed,
        (float[])_offset.Clone(),
        Enumerable.Range(0, Count).Select(hintMps).ToArray(),
        Enumerable.Range(0, Count).Select(straight).ToArray(),
        capMps is null ? null : Enumerable.Range(0, Count).Select(capMps).ToArray());

    /// <summary>The most the speed plan allows here whatever the bend, m/s: a crest's, or infinity.</summary>
    public float CapAt(int i) => _cap[i];

    /// <summary>
    /// The line that turns least, inside the room the road leaves once the car's wheels are kept on it.
    /// </summary>
    /// <param name="roadHalfWidth">Half the road's flat surface.</param>
    /// <param name="carHalfWidth">
    /// From the car's middle to the outside of its tyres: half its track plus half a tyre. The line's
    /// points are where the car's middle goes, so they keep this far in from each edge, and a margin more.
    /// </param>
    public RacingLine MinimumCurvature(float roadHalfWidth, float carHalfWidth, float marginM = 0.25f, int maxSweepsPerLevel = 2000)
    {
        float room = MathF.Max(0.0f, roadHalfWidth - carHalfWidth - marginM);
        return MinimumCurvature(_ => room, maxSweepsPerLevel);
    }

    /// <summary>
    /// The line that turns least, each sample free to move across the road by as much as
    /// <paramref name="roomAt"/> says, either way: the road's own room where it varies, and none where the
    /// line must keep to the middle (a jump, a loop).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How:</b> each point may only move across the road, along its own right. Minimising the sum of
    /// squared second differences, a stand-in for squared curvature, says each point belongs at
    /// <c>(4(p₋₁ + p₊₁) − (p₋₂ + p₊₂)) / 6</c> of its neighbours. So it's moved there, clamped to the room it
    /// has, over and over until nothing moves. That gives a line that runs wide into a corner, clips the
    /// inside, and runs wide out of it. A plain average of two neighbours would give the shortest line
    /// instead, which hugs the inside and turns harder for it.
    /// </para>
    /// <para>
    /// On a bank the line stays on the surface, because it moves along the surface's own right; a line
    /// to the inside is lower on the bank, and the speed plan reads the bank from the road either way.
    /// </para>
    /// </remarks>
    public RacingLine MinimumCurvature(Func<int, float> roomAt, int maxSweepsPerLevel = 2000)
    {
        int n = Count;
        float[] rooms = Enumerable.Range(0, n).Select(i => MathF.Max(0.0f, roomAt(i))).ToArray();
        var offset = new float[n];
        var at = (Vector3[])_position.Clone();

        // Coarse to fine. Point by point at one metre, a change has to creep along the line a sample a
        // sweep, and 40,000 sweeps only got 3 of the 3.6 m of room used (2026-11-23). Every 16th point
        // first, where a corner is a handful of points and settles in a few sweeps; then each finer level
        // starts from the coarser one's answer and only has local detail to add.
        foreach (int stride in new[] { 16, 8, 4, 2, 1 })
        {
            if (stride * 6 > n)
            {
                continue;
            }

            for (int sweep = 0; sweep < maxSweepsPerLevel; sweep++)
            {
                float moved = 0.0f;
                for (int i = 0; i < n; i += stride)
                {
                    if (!Closed && (i < 2 * stride || i > n - 1 - (2 * stride)))
                    {
                        continue;
                    }

                    Vector3 ideal = ((4.0f * (at[Neighbour(i, -stride)] + at[Neighbour(i, stride)]))
                        - (at[Neighbour(i, -2 * stride)] + at[Neighbour(i, 2 * stride)])) / 6.0f;
                    float wanted = Math.Clamp(Vector3.Dot(ideal - _position[i], _right[i]), -rooms[i], rooms[i]);
                    moved = MathF.Max(moved, MathF.Abs(wanted - offset[i]));
                    offset[i] = wanted;
                    at[i] = _position[i] + (_right[i] * wanted);
                }

                if (moved < 1e-5f)
                {
                    break;
                }
            }

            // Fill in between this level's points, for the next level to start from.
            if (stride > 1)
            {
                for (int i = 0; i < n; i++)
                {
                    int low = i - (i % stride);
                    if (low == i)
                    {
                        continue;
                    }

                    int high = low + stride;
                    float t = (i - low) / (float)stride;
                    float next = high < n ? offset[high] : Closed ? offset[0] : offset[low];
                    offset[i] = offset[low] + ((next - offset[low]) * t);
                    at[i] = _position[i] + (_right[i] * offset[i]);
                }
            }
        }

        var forward = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 ahead = at[Neighbour(i, 1)] - at[Neighbour(i, -1)];
            forward[i] = ahead.LengthSquared() > 0.0f ? Vector3.Normalize(ahead) : _forward[i];
        }

        return new RacingLine((at, forward, (Vector3[])_up.Clone(), (Vector3[])_right.Clone(), (int[])_piece.Clone()), Closed, offset,
            (float[])_hint.Clone(), (bool[])_straight.Clone(), (float[])_cap.Clone());
    }

    /// <summary>
    /// The fastest each sample can be taken at, by the banked-curve formula at <paramref name="caution"/>
    /// of the car's grip, then slowed on the approach to every corner so the car can brake for it.
    /// </summary>
    /// <remarks>
    /// <b>A wing's downforce counts</b> (M1d): it presses the tyres onto the road by
    /// <c>k·v²</c>, so the grip grows with speed and the formula becomes
    /// <c>v² = g(tan θ + μ) / (κ(1 − μ tan θ) − μk / (m cos θ))</c>, κ the curvature. Where the
    /// downforce outgrows the corner's need, the bottom goes to zero and the corner is flat out. It's
    /// counted as if shared by all four tyres; a rear wing loads the rear ones, which the caution covers.
    /// Braking likewise gains the downforce at the speed being braked to, the cautious end.
    /// </remarks>
    public float[] SpeedProfile(CarSpec spec, float caution)
    {
        const float G = WheelModel.Gravity;
        float mu = spec.Grip * caution;
        float top = WheelModel.TopSpeedEstimate(spec, Surfaces.Road);
        float pressPerMass = WheelModel.DownforcePerMps2(spec) / spec.MassKg;
        var speeds = new float[Count];

        for (int i = 0; i < Count; i++)
        {
            float bend = MathF.Abs(_curvature[i]);
            if (bend < 1e-4f)
            {
                speeds[i] = top;
                continue;
            }

            // Written so that with no wing it's the old formula to the last bit, and the lap oracle's
            // figures for a wingless car don't move.
            float bank = _bankDeg[i] * (MathF.PI / 180.0f);
            float tan = MathF.Tan(bank);
            float below = MathF.Max(1.0f - (mu * tan), 0.05f) - (mu * pressPerMass / (MathF.Cos(bank) * bend));
            speeds[i] = below > 0.0f ? MathF.Min(MathF.Sqrt(G / bend * (tan + mu) / below), top) : top;
        }

        // A stunt's speed is its hint, not what its shape says: a loop's heading turns over at the top,
        // which reads as a hairpin, and a launch ramp is straight, which reads as flat out.
        for (int i = 0; i < Count; i++)
        {
            if (!float.IsNaN(_hint[i]))
            {
                speeds[i] = MathF.Min(_hint[i], top);
            }

            speeds[i] = MathF.Min(speeds[i], _cap[i]);
        }

        int passes = Closed ? 2 : 1;
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                int next = i + 1;
                if (next == Count)
                {
                    if (!Closed)
                    {
                        continue;
                    }

                    next = 0;
                }

                float ds = Span(i, next);
                float after = speeds[next] * speeds[next];
                float braking = (G + (pressPerMass * after)) * mu * 0.9f;
                speeds[i] = MathF.Min(speeds[i], MathF.Sqrt(after + (2.0f * braking * ds)));
            }
        }

        return speeds;
    }

    /// <summary>
    /// The sample nearest a point. Given the last answer as a hint it looks only nearby, which is both
    /// faster and right where the road passes close to itself.
    /// </summary>
    public int Nearest(Vector3 point, int hint = -1) => Nearest(point, hint, 40, 60);

    /// <summary>The sample nearest a point among those from <paramref name="back"/> samples behind a hint to <paramref name="ahead"/> in front of it; everywhere without one.</summary>
    public int Nearest(Vector3 point, int hint, int back, int ahead)
    {
        int from = 0;
        int to = Count - 1;
        if (hint >= 0)
        {
            from = hint - back;
            to = hint + ahead;
        }

        int best = -1;
        float bestDistance = float.MaxValue;
        for (int k = from; k <= to; k++)
        {
            int i = Closed ? Mod(k) : Math.Clamp(k, 0, Count - 1);
            float d = Vector3.DistanceSquared(point, _position[i]);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Which way the line goes at a sample.</summary>
    public Vector3 ForwardAt(int i) => _forward[i];

    /// <summary>Which way the road's surface faces at a sample.</summary>
    public Vector3 UpAt(int i) => _up[i];

    /// <summary>How far a point is to the right of the line at a sample, metres; negative is left.</summary>
    /// <remarks>
    /// Square to the line itself, not to the road: where the line crosses the road at an angle, from one
    /// side to the apex, the road's right would read a point ahead of the sample as off to one side.
    /// </remarks>
    public float LateralOffset(int i, Vector3 point) =>
        Vector3.Dot(point - _position[i], Vector3.Normalize(Vector3.Cross(_forward[i], _up[i])));

    /// <summary>How far a point is to the right of the road's middle at a sample: the line's offset plus the point's from the line.</summary>
    public float OffsetFromMiddle(int i, Vector3 point) => _offset[i] + LateralOffset(i, point);

    /// <summary>The sample a distance further along, wrapping round a lap or stopping at the end.</summary>
    public int IndexAhead(int i, float distance)
    {
        int j = i;
        float gone = 0.0f;
        while (gone < distance)
        {
            int next = j + 1;
            if (next == Count)
            {
                if (!Closed)
                {
                    return j;
                }

                next = 0;
            }

            gone += Span(j, next);
            j = next;
        }

        return j;
    }

    private static int SampleCount(IReadOnlyList<ICentreline> pieces, bool closed, float stepM) =>
        pieces.Sum(p => Math.Max(1, (int)MathF.Ceiling(p.Length / stepM))) + (closed || pieces.Count == 0 ? 0 : 1);

    private static (Vector3[], Vector3[], Vector3[], Vector3[], int[]) Sample(IReadOnlyList<ICentreline> pieces, bool closed, float stepM)
    {
        var position = new List<Vector3>();
        var forward = new List<Vector3>();
        var up = new List<Vector3>();
        var right = new List<Vector3>();
        var piece = new List<int>();

        void Add(Frame f, int p)
        {
            position.Add(f.Position);
            forward.Add(f.Forward);
            up.Add(f.Up);
            right.Add(f.Right);
            piece.Add(p);
        }

        for (int p = 0; p < pieces.Count; p++)
        {
            int n = Math.Max(1, (int)MathF.Ceiling(pieces[p].Length / stepM));
            for (int k = 0; k < n; k++)
            {
                Add(pieces[p].FrameAt(pieces[p].Length * k / n), p);
            }
        }

        if (!closed && pieces.Count > 0)
        {
            Add(pieces[^1].FrameAt(pieces[^1].Length), pieces.Count - 1);
        }

        return (position.ToArray(), forward.ToArray(), up.ToArray(), right.ToArray(), piece.ToArray());
    }

    private static float Heading(Vector3 forward) => MathF.Atan2(forward.X, -forward.Z);

    private static float Wrap(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= 2.0f * MathF.PI;
        }

        while (angle < -MathF.PI)
        {
            angle += 2.0f * MathF.PI;
        }

        return angle;
    }

    private int Mod(int k) => ((k % Count) + Count) % Count;

    private int Neighbour(int i, int step) => Closed ? Mod(i + step) : Math.Clamp(i + step, 0, Count - 1);

    /// <summary>The distance along the line from one sample to a later one, across the lap's seam if need be.</summary>
    private float Span(int from, int to)
    {
        float d = _distance[to] - _distance[from];
        return d < 0.0f && Closed ? d + Length : d;
    }
}
