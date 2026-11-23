using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tracks;

/// <summary>
/// What racing a track reads from its route (M2-PLAN, M2d): its gates for the <see cref="RaceTimer"/>,
/// and its road as one racing line for the autodriver.
/// </summary>
public static class TrackRacing
{
    /// <summary>The route's gates in the order they're driven: the start, the checkpoints, the finish.</summary>
    public static IReadOnlyList<RaceGate> Gates(this LaidTrack track) => track.Route
        .Where(p => p.Def.Gate is not null)
        .Select(p => new RaceGate(p.Def.Gate!.Value, p.GateFrame!.Value, p.GateHalfWidthM))
        .ToList();

    /// <summary>The route's road as centrelines in order, and for each, which piece of the route it belongs to.</summary>
    public static (IReadOnlyList<ICentreline> Road, IReadOnlyList<int> Owner) RouteRoad(this LaidTrack track)
    {
        var road = new List<ICentreline>();
        var owner = new List<int>();
        for (int k = 0; k < track.Route.Count; k++)
        {
            foreach (ICentreline c in track.Route[k].Road().Pieces)
            {
                road.Add(c);
                owner.Add(k);
            }
        }

        return (road, owner);
    }

    /// <summary>
    /// The racing line over the route, start to finish: the line that turns least inside the room each
    /// piece leaves the car's wheels, held to the middle of a stunt (a launch ramp, a landing, the loop),
    /// with each stunt's speed hint and the loop's wheels held straight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A stunt is taken down its middle</b> because each was measured that way (M2b's oracle): a jump
    /// taken at an angle lands across its landing, and a loop's road is built for a car on its centre.
    /// </para>
    /// <para>
    /// <b>A hint's middle is the speed held</b>: 90 km/h for the short jump, 114 for the long one, 97.5 for
    /// the loop. M2b's oracle held its speeds the same way up to the lip, where the car comes off at about
    /// 5 km/h less, which lands it in the middle of the landing's down-ramp.
    /// </para>
    /// </remarks>
    public static RacingLine RouteLine(this LaidTrack track, CarSpec spec, float marginM = 0.5f)
    {
        (IReadOnlyList<ICentreline> road, IReadOnlyList<int> owner) = track.RouteRoad();
        var centre = new RacingLine(road, closed: false);
        float carHalf = (spec.TrackM / 2.0f) + 0.15f;

        PlacedPiece PieceOf(int sample) => track.Route[owner[centre.PieceAt(sample)]];

        float Room(int sample)
        {
            PlacedPiece p = PieceOf(sample);
            return IsStunt(p.Def) ? 0.0f : (p.Def.WidthM / 2.0f) - LipM - carHalf - marginM;
        }

        float Hint(int sample) => PieceOf(sample).Def.SpeedKmh is float[] s ? (s[0] + s[1]) / 2.0f / 3.6f : float.NaN;

        // A crest throws the car above √(g R cos θ) (M2-PLAN fact 3), and the slope measured it leaving the
        // ground from about 0.8 of that (M2b): held under it on ordinary road. On a stunt, the hint rules.
        float Cap(int sample)
        {
            if (IsStunt(PieceOf(sample).Def))
            {
                return float.PositiveInfinity;
            }

            int before = Math.Max(0, sample - 2);
            int after = Math.Min(centre.Count - 1, sample + 2);
            float span = centre.DistanceAt(after) - centre.DistanceAt(before);
            float pitchBefore = MathF.Asin(Math.Clamp(centre.ForwardAt(before).Y, -1.0f, 1.0f));
            float pitchAfter = MathF.Asin(Math.Clamp(centre.ForwardAt(after).Y, -1.0f, 1.0f));
            float over = span > 0.0f ? (pitchBefore - pitchAfter) / span : 0.0f;
            if (over < 1e-4f)
            {
                return float.PositiveInfinity;
            }

            float pitch = MathF.Asin(Math.Clamp(centre.ForwardAt(sample).Y, -1.0f, 1.0f));
            return CrestShare * MathF.Sqrt(9.81f * MathF.Cos(pitch) / over);
        }

        return centre.MinimumCurvature(Room).WithStunts(Hint, sample => road[centre.PieceAt(sample)] is LoopCentreline, Cap);
    }

    /// <summary>The share of a crest's take-off speed the plan holds the car under: where the slope piece threw it in M2b.</summary>
    private const float CrestShare = 0.8f;

    /// <summary>The slab's rounded lip, which isn't road to drive on: the profile's own.</summary>
    private const float LipM = 0.15f;

    private static bool IsStunt(PieceDef piece) =>
        piece.SpeedKmh is not null || piece.In == ConnectorKind.Landing || piece.Out.Kind == ConnectorKind.Lip || piece.Path.Any(s => s.Loop is not null);
}
