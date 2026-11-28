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
    /// <summary>
    /// The route's gates in the order they're driven: the start, the checkpoints, the finish. On a circuit
    /// (M3g) the line, then each lap's checkpoints and the line again, once a lap, so the race timer runs a
    /// circuit as it runs a sprint and each lap's time is the split at the line.
    /// </summary>
    public static IReadOnlyList<RaceGate> Gates(this LaidTrack track)
    {
        var once = track.Route
            .Where(p => p.Def.Gate is not null)
            .Select(p => new RaceGate(p.Def.Gate!.Value, p.GateFrame!.Value, p.GateHalfWidthM))
            .ToList();
        if (!track.Track.Circuit || once.Count == 0)
        {
            return once;
        }

        // The line is the timer's start, a checkpoint at the end of every lap but the last, and the finish.
        RaceGate line = once[0];
        var gates = new List<RaceGate> { line with { Kind = GateKind.Start } };
        for (int lap = 1; lap <= track.Track.Laps; lap++)
        {
            gates.AddRange(once.Skip(1));
            gates.Add(line with { Kind = lap == track.Track.Laps ? GateKind.Finish : GateKind.Checkpoint });
        }

        return gates;
    }

    /// <summary>On a circuit, the gates a lap passes: its checkpoints and the line. Gate g (from 1) ends a lap when it divides by this.</summary>
    public static int GatesPerLap(this LaidTrack track) => track.Route.Count(p => p.Def.Gate is not null);

    /// <summary>On a circuit, which lap gate g (from 1) is in, from 1.</summary>
    public static int LapOf(this LaidTrack track, int gate) => ((gate - 1) / Math.Max(1, track.GatesPerLap())) + 1;

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
    public static RacingLine RouteLine(this LaidTrack track, CarSpec spec, float marginM = 0.5f) =>
        Line(track, spec, marginM, minimumCurvature: true, stepM: 1.0f);

    /// <summary>
    /// The route's middle with the stunts' hints and the crests' caps, but not the racing line through the
    /// corners: what the generator's speed estimate reads (M3c), many times a track, where the minimum
    /// curvature's 100 ms each would add up. Its corners are tighter than the racing line's, so it's the
    /// cautious side of what the autodriver will plan.
    /// </summary>
    public static RacingLine CentreLine(this LaidTrack track, CarSpec spec, float stepM = 2.0f) =>
        Line(track, spec, 0.5f, minimumCurvature: false, stepM);

    private static RacingLine Line(LaidTrack track, CarSpec spec, float marginM, bool minimumCurvature, float stepM)
    {
        (IReadOnlyList<ICentreline> road, IReadOnlyList<int> owner) = track.RouteRoad();

        // A circuit's line goes round (M3g): the plan brakes for the first corner of the next lap, and the
        // autodriver laps it as it laps station 6.
        var centre = new RacingLine(road, closed: track.Track.Circuit, stepM);
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

        // A dirt or sand piece lends its surface's share of the grip, for its corners and the braking over it (P3).
        float Grip(int sample) => (Surfaces.Find(PieceOf(sample).Def.Surface) ?? Surfaces.Road).Grip;

        RacingLine line = minimumCurvature ? centre.MinimumCurvature(Room) : centre;
        return line.WithStunts(Hint, sample => road[centre.PieceAt(sample)] is LoopCentreline, Cap, Grip);
    }

    /// <summary>Whether a piece is a stunt: one with a speed hint, a landing, a lip, or a loop.</summary>
    public static bool IsStunt(PieceDef piece) =>
        piece.SpeedKmh is not null || piece.In == ConnectorKind.Landing || piece.Out.Kind == ConnectorKind.Lip || piece.Path.Any(s => s.Loop is not null);

    /// <summary>The share of a crest's take-off speed the plan holds the car under: where the slope piece threw it in M2b.</summary>
    private const float CrestShare = 0.8f;

    /// <summary>The slab's rounded lip, which isn't road to drive on: the profile's own.</summary>
    private const float LipM = 0.15f;
}
