using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Generation;

/// <summary>The stunt stage's dials (M3-PLAN, stage 3): how many stunts, which kinds, and how far apart the checkpoints are.</summary>
public sealed record StuntSettings
{
    /// <summary>
    /// Of the places along the route a stunt fits, the share offered one, before the speed estimate turns
    /// any down. 0 is none: the route as M3b left it.
    /// </summary>
    public float Density { get; init; } = 0.8f;

    /// <summary>The short jump's weight, where it and another stunt both fit.</summary>
    public float ShortJump { get; init; } = 1.0f;

    public float LongJump { get; init; } = 1.0f;

    public float Loop { get; init; } = 1.0f;

    /// <summary>Metres of road from one gate to the next checkpoint; 0 for none.</summary>
    public float CheckpointEveryM { get; init; } = 400.0f;

    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!(Density >= 0.0f && Density <= 1.0f))
        {
            problems.Add($"stunt density is {Density}, and must be 0 to 1");
        }

        if (!(ShortJump >= 0.0f && LongJump >= 0.0f && Loop >= 0.0f && ShortJump + LongJump + Loop <= 1000.0f))
        {
            problems.Add($"stunt weights are {ShortJump}, {LongJump} and {Loop}, and must be 0 or more");
        }

        if (!(CheckpointEveryM == 0.0f || (CheckpointEveryM >= 100.0f && CheckpointEveryM <= 5000.0f)))
        {
            problems.Add($"checkpoints every {CheckpointEveryM} m: 0 for none, or 100 m to 5 km");
        }

        return problems;
    }
}

public enum StuntKind
{
    ShortJump,
    LongJump,
    Loop,
}

/// <summary>What the stunt stage did: the stunts placed by kind, the ones the speed estimate turned down and why, the checkpoints.</summary>
public sealed record StuntStats(int ShortJumps, int LongJumps, int Loops, int TooSlow, int MustBrake, int Checkpoints)
{
    public static readonly StuntStats None = new(0, 0, 0, 0, 0, 0);

    public int Placed => ShortJumps + LongJumps + Loops;
}

/// <summary>
/// Stunts swapped into the route (M3-PLAN, stage 3, M3c), each only where its footprint and way out are
/// already road (fact 5), so the stunt dial can't move the route: five straights at one level become the
/// short jump (a launch ramp, a tile of gap, a landing); five straights and a slope up become the long
/// jump, which comes out a level up; an S-bend becomes the loop (decision 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Where</b>: walking the route, each place a stunt fits is offered one at <see cref="StuntSettings.Density"/>,
/// the kind drawn by weight where more than one fits, from the stunt stage's own seed. A stunt keeps
/// <see cref="RoadBetweenM"/> of ordinary road from the last, for the car to settle.
/// </para>
/// <para>
/// <b>The speed estimate approves each one</b> (PLAN suggestion 4): the route's speed plan for the stock
/// car at the autodriver's caution, met by full throttle from the start (<see cref="SpeedEstimate"/>). A
/// stunt the car can't arrive at the bottom of its speed hint for is <i>too slow</i>; one the plan has to
/// brake through, for a corner or a crest after it, <i>must brake</i>. The first refused, in route order,
/// comes out and the rest are judged again without it, until every one left is approved.
/// </para>
/// <para>
/// <b>The estimate's car is pinned here</b>, the stock car's numbers, not read from a file: a tune of his
/// mustn't change the tracks a seed makes.
/// </para>
/// </remarks>
internal static class StuntPlanner
{
    /// <summary>Ordinary road kept between one stunt's end and the next one's start: three tiles.</summary>
    private const float RoadBetweenM = 60.0f;

    /// <summary>The share of grip the estimate plans for: the autodriver's default.</summary>
    private const float Caution = 0.8f;

    /// <summary>A jump's landing may need no harder braking than down to this share of the hint's bottom.</summary>
    private const float LandingShare = 0.9f;

    /// <summary>The stock car's numbers that make its speed (buggy.json, 2026-11-24).</summary>
    internal static readonly CarSpec Reference = new()
    {
        MassKg = 650.0f,
        Grip = 1.1f,
        DriveForceN = 4_500.0f,
        PowerW = 110_000.0f,
        DragNPerMps2 = 0.86f,
        BrakeForceN = 9_000.0f,
    };

    private const string Straight = "base:straight";

    public static (List<Placement> Plan, StuntStats Stats) Choose(
        ulong seed, StuntSettings settings, List<Placement> plan, PieceCatalogue catalogue, Func<List<Placement>, LaidTrack> lay)
    {
        List<Stunt> chosen = Offer(seed, settings, plan, catalogue);
        int tooSlow = 0;
        int mustBrake = 0;
        while (chosen.Count > 0)
        {
            List<Placement> stunted = Swap(plan, chosen);
            LaidTrack laid = lay(stunted);
            if (laid.Problems.Count > 0)
            {
                throw new InvalidOperationException("A stunt swap broke the track: " + string.Join("; ", laid.Problems));
            }

            (int refused, bool slow) = FirstRefused(laid, chosen.Select(c => Swapped(plan, c)[0]).ToList());
            if (refused < 0)
            {
                return (stunted, new StuntStats(
                    chosen.Count(c => c.Kind == StuntKind.ShortJump),
                    chosen.Count(c => c.Kind == StuntKind.LongJump),
                    chosen.Count(c => c.Kind == StuntKind.Loop),
                    tooSlow,
                    mustBrake,
                    0));
            }

            tooSlow += slow ? 1 : 0;
            mustBrake += slow ? 0 : 1;
            chosen.RemoveAt(refused);
        }

        return (plan, new StuntStats(0, 0, 0, tooSlow, mustBrake, 0));
    }

    /// <summary>Every place a stunt fits, walked in route order, offered one by chance: the stunts before the estimate's say.</summary>
    private static List<Stunt> Offer(ulong seed, StuntSettings settings, List<Placement> plan, PieceCatalogue catalogue)
    {
        var chosen = new List<Stunt>();
        if (settings.Density <= 0.0f)
        {
            return chosen;
        }

        var startM = new float[plan.Count + 1];
        for (int k = 0; k < plan.Count; k++)
        {
            startM[k + 1] = startM[k] + TrackGenerator.RoadLength(catalogue, plan[k]);
        }

        ulong state = seed;
        float clearFromM = 0.0f;

        // Not on the start gate or the straight after it. A jump goes in a run of road in a line (straights and
        // slopes), as late in it as it fits, so the rest of the run is its run-up: on the first five straights
        // of a run, nearly every one came after a corner too slowly to fly, and runs of flat straights alone
        // were rare, the levels having made slopes of them (the bench, 2026-11-24).
        for (int k = 2; k < plan.Count;)
        {
            var fits = new List<(StuntKind Kind, float Weight, int At, int Count)>();
            int next = k + 1;
            if (InLine(plan[k].Piece))
            {
                int end = k;
                while (end < plan.Count && InLine(plan[end].Piece))
                {
                    end++;
                }

                next = end;
                if (settings.ShortJump > 0.0f && Latest(plan, k, end, 5, 0, startM, clearFromM) is (int s5, int n5))
                {
                    fits.Add((StuntKind.ShortJump, settings.ShortJump, s5, n5));
                }

                if (settings.LongJump > 0.0f && Latest(plan, k, end, 7, 1, startM, clearFromM) is (int s7, int n7))
                {
                    fits.Add((StuntKind.LongJump, settings.LongJump, s7, n7));
                }
            }
            else if (plan[k].Piece == "base:s-bend" && startM[k] >= clearFromM && settings.Loop > 0.0f)
            {
                fits.Add((StuntKind.Loop, settings.Loop, k, 1));
            }

            if (fits.Count == 0 || Hash64.ToUnitFloat(Next()) >= settings.Density)
            {
                k = next;
                continue;
            }

            float pick = Hash64.ToUnitFloat(Next()) * fits.Sum(f => f.Weight);
            int which = 0;
            while (which < fits.Count - 1 && pick >= fits[which].Weight)
            {
                pick -= fits[which].Weight;
                which++;
            }

            (StuntKind kind, _, int at, int count) = fits[which];
            chosen.Add(new Stunt(at, count, kind));
            k = at + count;
            clearFromM = startM[k] + RoadBetweenM;
        }

        return chosen;

        ulong Next()
        {
            state += 0x9E3779B97F4A7C15UL;
            return Hash64.Mix(state);
        }
    }

    /// <summary>A stunt chosen: where in the route it starts, how many of the route's placements it replaces, and what it is.</summary>
    private sealed record Stunt(int At, int Count, StuntKind Kind);

    /// <summary>Road in a line that a jump can replace: a straight or a slope, each running straight on.</summary>
    private static bool InLine(string piece) => piece is Straight or "base:slope-up" or "base:slope-down";

    private static int TilesOf(string piece) => piece == Straight ? 1 : 2;

    private static int LevelAfter(Placement p) => p.Piece switch
    {
        "base:slope-up" => p.Level + 1,
        "base:slope-down" => p.Level - 1,
        _ => p.Level,
    };

    /// <summary>
    /// The latest stretch of a run in a line, [<paramref name="from"/>, <paramref name="to"/>), that is
    /// <paramref name="tiles"/> tiles long and comes out <paramref name="rise"/> levels above where it went
    /// in, starting no earlier than <paramref name="clearFromM"/>: where a jump of that length and rise can
    /// go, the rest of the run before it its run-up. Null if none.
    /// </summary>
    private static (int At, int Count)? Latest(List<Placement> plan, int from, int to, int tiles, int rise, float[] startM, float clearFromM)
    {
        for (int end = to; end > from; end--)
        {
            int count = 0;
            int length = 0;
            for (int s = end - 1; s >= from && length < tiles; s--)
            {
                length += TilesOf(plan[s].Piece);
                count++;
            }

            int at = end - count;
            int level = plan[at].Level;
            if (length == tiles && LevelAfter(plan[end - 1]) == level + rise && startM[at] >= clearFromM
                && level + rise + 1 <= Track.MaxLevel)
            {
                return (at, count);
            }
        }

        return null;
    }

    /// <summary>The route with the stunts in place of what they replace.</summary>
    private static List<Placement> Swap(List<Placement> plan, List<Stunt> chosen)
    {
        var result = new List<Placement>();
        int k = 0;
        foreach (Stunt stunt in chosen)
        {
            result.AddRange(plan.Skip(k).Take(stunt.At - k));
            result.AddRange(Swapped(plan, stunt));
            k = stunt.At + stunt.Count;
        }

        result.AddRange(plan.Skip(k));
        return result;
    }

    /// <summary>
    /// The pieces one stunt is: a launch ramp at the stretch's start and its landing across the gap a level
    /// or two up (as the shakedown has them), two or four tiles on; or the loop where the S-bend was, turned
    /// and mirrored as it was.
    /// </summary>
    private static List<Placement> Swapped(List<Placement> plan, Stunt stunt)
    {
        Placement first = plan[stunt.At];
        var heading = (Compass)first.Rotation;
        return stunt.Kind switch
        {
            StuntKind.ShortJump =>
            [
                first with { Piece = "base:launch-short", Mirrored = false },
                first with { Piece = "base:landing", At = first.At.Step(heading, 2), Level = first.Level + 1, Mirrored = false },
            ],
            StuntKind.LongJump =>
            [
                first with { Piece = "base:launch-long", Mirrored = false },
                first with { Piece = "base:landing", At = first.At.Step(heading, 4), Level = first.Level + 2, Mirrored = false },
            ],
            _ => [first with { Piece = "base:loop" }],
        };
    }

    /// <summary>
    /// The first stunt, in route order, the speed estimate turns down, and whether for being too slow (else
    /// for having to brake through it); -1 if it approves them all.
    /// </summary>
    private static (int Index, bool TooSlow) FirstRefused(LaidTrack laid, List<Placement> stunts)
    {
        RacingLine line = laid.CentreLine(Reference);
        float[] plan = line.SpeedProfile(Reference, Caution);
        float[] can = SpeedEstimate.Achievable(line, plan, Reference);
        (_, IReadOnlyList<int> owner) = laid.RouteRoad();

        // Each sample's piece of the route, and each route piece's first and last sample.
        var first = new Dictionary<int, int>();
        var last = new Dictionary<int, int>();
        for (int i = 0; i < line.Count; i++)
        {
            int piece = owner[line.PieceAt(i)];
            first.TryAdd(piece, i);
            last[piece] = i;
        }

        for (int s = 0; s < stunts.Count; s++)
        {
            int piece = Find(laid, stunts[s]);
            PlacedPiece stunt = laid.Route[piece];
            float bottom = stunt.Def.SpeedKmh![0] / 3.6f;
            int from = first[piece];
            int to = last[piece];

            float lowestPlan = float.MaxValue;
            for (int i = from; i <= to; i++)
            {
                lowestPlan = MathF.Min(lowestPlan, plan[i]);
            }

            // A jump comes down on its landing at about the speed it left: the plan mustn't need it braking hard there.
            if (stunt.Out.Kind == ConnectorKind.Lip && first.TryGetValue(piece + 1, out int landing))
            {
                lowestPlan = MathF.Min(lowestPlan, plan[landing] / LandingShare);
            }

            if (lowestPlan < bottom)
            {
                return (s, false);
            }

            if (can[from] < bottom)
            {
                return (s, true);
            }
        }

        return (-1, false);
    }

    private static int Find(LaidTrack laid, Placement p)
    {
        for (int k = 0; k < laid.Route.Count; k++)
        {
            Placement q = laid.Route[k].Placement;
            if (q.Piece == p.Piece && q.At == p.At)
            {
                return k;
            }
        }

        throw new InvalidOperationException($"{p.Piece} at {p.At} isn't on the route.");
    }

    /// <summary>
    /// Straights become checkpoints (M3c), the first plain straight once <see cref="StuntSettings.CheckpointEveryM"/>
    /// of road has gone by since the last gate, but none in the last half of that before the finish.
    /// </summary>
    public static (List<Placement> Plan, int Checkpoints) Checkpoints(List<Placement> plan, float everyM, PieceCatalogue catalogue)
    {
        if (everyM <= 0.0f)
        {
            return (plan, 0);
        }

        float total = plan.Sum(p => TrackGenerator.RoadLength(catalogue, p));
        var result = new List<Placement>();
        float along = 0.0f;
        float since = 0.0f;
        int count = 0;
        for (int k = 0; k < plan.Count; k++)
        {
            Placement p = plan[k];
            float length = TrackGenerator.RoadLength(catalogue, p);
            if (k >= 2 && p.Piece == Straight && since >= everyM && total - along >= everyM * 0.5f)
            {
                result.Add(p with { Piece = "base:checkpoint" });
                since = 0.0f;
                count++;
            }
            else
            {
                result.Add(p);
            }

            since += length;
            along += length;
        }

        return (result, count);
    }
}
