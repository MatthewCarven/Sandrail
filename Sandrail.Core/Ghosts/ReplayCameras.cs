using System.Numerics;
using Sandrail.Core.Driving;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Ghosts;

/// <summary>
/// A camera beside the track for replays (M4-PLAN, M4d): where it stands, the stretch of road it covers
/// (metres along the route, from and to), the point it's placed for, and why it's there.
/// </summary>
public readonly record struct ReplayCamera(Vector3 Position, float AtM, float FromM, float ToM, string Why);

/// <summary>
/// Cameras beside a track for replays (PLAN suggestion 8, placed from the laid track at replay time, M4-PLAN
/// fact 9): one by each stunt, one at the start, one on the outside of a corner every 150 m or so, and one
/// partway along any straight that would otherwise go 200 m without one. Each covers the road from halfway
/// back to the camera before it to halfway on to the next, so the car is always shown by its nearest.
/// </summary>
/// <remarks>
/// <para>
/// <b>Each stands a few metres above the ground there</b>, out past the road's bed, on the side that sees
/// its stretch best, and higher if the ground hides the road from it: the hills of a generated track can
/// stand between a camera and its corner.
/// </para>
/// <para>
/// <b>A pure function of the track and its ground</b>: the same cameras every time, so nothing is saved.
/// </para>
/// </remarks>
public static class ReplayCameras
{
    /// <summary>How far apart corner cameras are, at least, metres along the route.</summary>
    public const float CornerGapM = 120.0f;

    /// <summary>The most road a camera's neighbours may leave between them before a straight gets one.</summary>
    public const float MostGapM = 200.0f;

    /// <summary>How far beyond the road's edge a camera stands, metres, before it's moved for the view.</summary>
    private const float OutM = 14.0f;

    /// <summary>How high above the ground a camera stands to begin with, metres.</summary>
    private const float HeightM = 4.0f;

    /// <summary>How much higher it may be raised to see over the ground, metres, in steps.</summary>
    private const float MostRaiseM = 24.0f;

    private const float RaiseStepM = 4.0f;

    /// <summary>The share of its stretch a camera that sees this much is good enough at, so it stops looking higher.</summary>
    public const float GoodView = 0.95f;

    /// <summary>The line the cameras are placed along and the car's distance is read from: the route's middle, 2 m a sample.</summary>
    public static RacingLine Line(LaidTrack track) => new(track.RouteRoad().Road, track.Track.Circuit, 2.0f);

    /// <param name="track">A laid track with a route.</param>
    /// <param name="groundAt">The ground's height at a point (x, z): the bedded terrain, or 0 on the plain.</param>
    public static IReadOnlyList<ReplayCamera> Place(LaidTrack track, Func<float, float, float> groundAt)
    {
        RacingLine line = Line(track);
        IReadOnlyList<int> owner = track.RouteRoad().Owner;
        PlacedPiece PieceAt(int i) => track.Route[owner[line.PieceAt(i)]];
        float length = line.Length;

        // The anchors: a sample each, and why. The start first, then each run of stunt pieces at its middle.
        var anchors = new List<(int Sample, string Why)> { (Sample(line, MathF.Min(25.0f, length / 4.0f)), "the start") };
        int runFrom = -1;
        for (int i = 0; i <= line.Count; i++)
        {
            bool stunt = i < line.Count && TrackRacing.IsStunt(PieceAt(i).Def);
            if (stunt && runFrom < 0)
            {
                runFrom = i;
            }
            else if (!stunt && runFrom >= 0)
            {
                bool loop = Enumerable.Range(runFrom, i - runFrom).Any(k => PieceAt(k).Def.Path.Any(s => s.Loop is not null));
                anchors.Add(((runFrom + i - 1) / 2, loop ? "the loop" : "the jump"));
                runFrom = -1;
            }
        }

        // Corners, at their middles, each far enough from every camera so far.
        for (int i = 0; i < line.Count;)
        {
            PlacedPiece p = PieceAt(i);
            int j = i;
            while (j < line.Count && ReferenceEquals(PieceAt(j), p))
            {
                j++;
            }

            int middle = (i + j - 1) / 2;
            if (p.Def.Path.Any(s => s.Turn is not null) && anchors.All(a => Apart(line, a.Sample, middle) >= CornerGapM))
            {
                anchors.Add((middle, "a corner"));
            }

            i = j;
        }

        // Straights: no 200 m without a camera.
        anchors.Sort((a, b) => a.Sample.CompareTo(b.Sample));
        var filled = new List<(int Sample, string Why)>();
        for (int k = 0; k < anchors.Count; k++)
        {
            filled.Add(anchors[k]);
            float from = line.DistanceAt(anchors[k].Sample);
            float to = k + 1 < anchors.Count ? line.DistanceAt(anchors[k + 1].Sample)
                : line.Closed ? line.DistanceAt(anchors[0].Sample) + length
                : length;
            int extra = (int)MathF.Floor((to - from) / MostGapM);
            for (int e = 1; e <= extra; e++)
            {
                float at = from + ((to - from) * e / (extra + 1));
                filled.Add((Sample(line, line.Closed ? at % length : at), "a straight"));
            }
        }

        filled.Sort((a, b) => a.Sample.CompareTo(b.Sample));

        // Each one's stretch: halfway back to the one before, halfway on to the next.
        var cameras = new List<ReplayCamera>();
        for (int k = 0; k < filled.Count; k++)
        {
            float at = line.DistanceAt(filled[k].Sample);
            float before = k > 0 ? line.DistanceAt(filled[k - 1].Sample) : line.Closed ? line.DistanceAt(filled[^1].Sample) - length : 0.0f;
            float after = k + 1 < filled.Count ? line.DistanceAt(filled[k + 1].Sample) : line.Closed ? line.DistanceAt(filled[0].Sample) + length : length;
            float fromM = k > 0 || line.Closed ? (before + at) / 2.0f : 0.0f;
            float toM = k + 1 < filled.Count || line.Closed ? (at + after) / 2.0f : length;
            if (line.Closed)
            {
                fromM = ((fromM % length) + length) % length;
                toM = ((toM % length) + length) % length;
            }

            Vector3 spot = Stand(line, PieceAt, filled[k].Sample, filled[k].Why, fromM, toM, groundAt, k);
            cameras.Add(new ReplayCamera(spot, at, fromM, toM, filled[k].Why));
        }

        return cameras;
    }

    /// <summary>The camera covering a distance along the route; on a circuit, distances go round.</summary>
    public static int Covering(IReadOnlyList<ReplayCamera> cameras, float alongM)
    {
        for (int k = 0; k < cameras.Count; k++)
        {
            ReplayCamera c = cameras[k];
            bool inside = c.FromM <= c.ToM ? alongM >= c.FromM && alongM < c.ToM : alongM >= c.FromM || alongM < c.ToM;
            if (inside)
            {
                return k;
            }
        }

        // Past the end of a sprint (beyond its finish), or before its start: the nearer end's camera.
        return alongM <= 0.0f ? 0 : cameras.Count - 1;
    }

    /// <summary>
    /// How far along the route the car is at each of a ghost's snapshots: the nearest point on the line, looked
    /// for near the last one, and everywhere after a cut.
    /// </summary>
    public static float[] AlongRoute(RacingLine line, Ghost ghost)
    {
        var along = new float[ghost.Snapshots.Count];
        int hint = -1;
        for (int k = 0; k < along.Length; k++)
        {
            Snapshot s = ghost.Snapshots[k];
            hint = line.Nearest(s.Position, s.Cut ? -1 : hint);
            along[k] = line.DistanceAt(hint);
        }

        return along;
    }

    /// <summary>The share of a camera's stretch it can see, the road a metre up (where a car is), the ground in between.</summary>
    public static float View(RacingLine line, Vector3 camera, float fromM, float toM, Func<float, float, float> groundAt)
    {
        int seen = 0;
        int all = 0;
        foreach (int i in Stretch(line, fromM, toM))
        {
            all++;
            if (Sees(camera, line.PositionAt(i) + (Vector3.UnitY * 1.0f), groundAt))
            {
                seen++;
            }
        }

        return all == 0 ? 1.0f : seen / (float)all;
    }

    /// <summary>Where a camera stands: out past the road, on the side and at the height that sees its stretch best.</summary>
    private static Vector3 Stand(RacingLine line, Func<int, PlacedPiece> pieceAt, int sample, string why, float fromM, float toM, Func<float, float, float> groundAt, int index)
    {
        Vector3 road = line.PositionAt(sample);
        Vector3 forward = line.ForwardAt(sample) with { Y = 0.0f };
        forward = forward.LengthSquared() > 1e-6f ? Vector3.Normalize(forward) : -Vector3.UnitZ;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

        // The outside of a corner, by which way the road turns over the next few samples; otherwise either side.
        Vector3 ahead = line.ForwardAt(Math.Min(line.Count - 1, sample + 5));
        Vector3 behind = line.ForwardAt(Math.Max(0, sample - 5));
        float turn = Vector3.Cross(behind, ahead).Y;
        float preferred = why == "a corner" ? (turn > 0.0f ? 1.0f : -1.0f) : (index % 2 == 0 ? 1.0f : -1.0f);

        float half = pieceAt(sample).Def.WidthM / 2.0f;
        float out_ = why switch
        {
            "the loop" => 34.0f,
            "the jump" => 22.0f,
            _ => OutM,
        };

        Vector3 best = default;
        float bestView = -1.0f;
        foreach (float side in new[] { preferred, -preferred })
        {
            Vector3 foot = road + (right * side * (half + out_));
            float ground = groundAt(foot.X, foot.Z);
            for (float raise = 0.0f; raise <= MostRaiseM; raise += RaiseStepM)
            {
                // Never below the road beside it, so a camera by an embankment isn't looking up at its side.
                var spot = foot with { Y = MathF.Max(ground, road.Y - 1.0f) + HeightM + raise };
                float view = View(line, spot, fromM, toM, groundAt);
                if (view > bestView + 0.02f)
                {
                    best = spot;
                    bestView = view;
                }

                if (view >= GoodView)
                {
                    break;
                }
            }

            if (bestView >= GoodView)
            {
                break;
            }
        }

        return best;
    }

    /// <summary>Whether the ground stays below the straight line from a camera to a point, short of the point itself.</summary>
    private static bool Sees(Vector3 camera, Vector3 point, Func<float, float, float> groundAt)
    {
        float distance = Vector3.Distance(camera, point);
        int steps = Math.Max(4, (int)(distance / 2.0f));
        for (int s = 1; s < steps; s++)
        {
            float t = s / (float)steps;

            // The last 3 m are the road's own bed, which a point a metre above the road always clears.
            if ((1.0f - t) * distance < 3.0f)
            {
                break;
            }

            Vector3 p = Vector3.Lerp(camera, point, t);
            if (groundAt(p.X, p.Z) > p.Y - 0.3f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The samples in a stretch, every other one (4 m apart), going round on a circuit.</summary>
    private static IEnumerable<int> Stretch(RacingLine line, float fromM, float toM)
    {
        int from = Sample(line, fromM);
        int to = Sample(line, toM);
        if (from <= to)
        {
            for (int i = from; i <= to; i += 2)
            {
                yield return i;
            }

            yield break;
        }

        for (int i = from; i < line.Count; i += 2)
        {
            yield return i;
        }

        for (int i = 0; i <= to; i += 2)
        {
            yield return i;
        }
    }

    /// <summary>The sample at a distance along the line.</summary>
    private static int Sample(RacingLine line, float alongM)
    {
        int lo = 0;
        int hi = line.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (line.DistanceAt(mid) < alongM)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>How far apart two samples are along the route, the short way round on a circuit.</summary>
    private static float Apart(RacingLine line, int a, int b)
    {
        float d = MathF.Abs(line.DistanceAt(a) - line.DistanceAt(b));
        return line.Closed ? MathF.Min(d, line.Length - d) : d;
    }
}
