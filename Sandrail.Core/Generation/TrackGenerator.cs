using System.Numerics;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Generation;

/// <summary>The route's dials (M3-PLAN, stage 2): how long a track is and how often it turns.</summary>
public sealed record RouteSettings
{
    /// <summary>The shortest a route may be, metres of road (decision 4: 1 to 2 km).</summary>
    public float MinLengthM { get; init; } = 1000.0f;

    public float MaxLengthM { get; init; } = 2000.0f;

    /// <summary>How much more often than the shipped weights it turns: 1 is as shipped, 2 twice as twisty.</summary>
    public float Turniness { get; init; } = 1.0f;

    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!(MinLengthM >= 200.0f && MaxLengthM >= MinLengthM + 200.0f && MaxLengthM <= 10_000.0f))
        {
            problems.Add($"lengths are {MinLengthM} to {MaxLengthM} m, and need 200 m at least, a 200 m window and at most 10 km");
        }

        if (!(Turniness > 0.0f && Turniness <= 10.0f))
        {
            problems.Add($"turniness is {Turniness}, and must be above 0 and at most 10");
        }

        return problems;
    }
}

/// <summary>Every dial the generator has, stage by stage (M3-PLAN, "The model").</summary>
public sealed record GenSettings
{
    public TerrainSettings Terrain { get; init; } = new();

    public RouteSettings Route { get; init; } = new();
}

/// <summary>What making a track took, for the bench: tries, how often it backed out, how long it came out, and how the road met the ground.</summary>
public sealed record GenStats(int Attempts, int Backtracks, int Placements, float LengthM, int Slopes, int Banked, float WorstFillM, float WorstCutM);

/// <summary>A generated track, the ground under it, and what making it took.</summary>
public sealed record Generated(Track Track, Terrain Terrain, GenStats Stats);

/// <summary>
/// Makes a track from a seed (M3-PLAN, M3b so far: terrain and the route, no stunts or checkpoints yet).
/// </summary>
/// <remarks>
/// <para>
/// <b>The route is walked in pieces, not tiles</b> (M3-PLAN fact 4): from a start gate on one edge, each
/// step lays a straight or a run of them, a 30 m or 50 m corner, an S-bend or the odd chicane, each only on
/// free tiles of the grid, until it reaches the opposite edge heading out of the grid, where the finish
/// gate goes. <b>Which step</b> is chosen by chance from the route's seed, weighted: toward the finish
/// harder as the road nears its length; toward ground the road can follow (fact 2); never into a place the
/// finish can't be reached from. A step with nowhere to go is undone and the next choice tried; a walk that
/// takes too long is begun again from another start.
/// </para>
/// <para>
/// <b>Then levels</b>: every piece's level, chosen all at once so the road keeps to the ground, a pair of
/// straights becoming a slope where it should climb or fall. <b>A road never runs below the ground if it
/// can help it</b>: until M3d carves the ground to meet it, a buried road is a hole in the track, while one
/// on fill is an embankment, which M2's earthworks already make. So burying costs six times what building
/// up does. <b>Then banks</b>: a corner the car arrives at well over its flat speed becomes its banked twin.
/// </para>
/// <para>
/// <b>Everything is a pure function of the seed and the dials</b>; the only chance is <see cref="Hash64"/>,
/// counted out from the route's own stage seed (CLAUDE.md non-negotiables 2 and 4).
/// </para>
/// </remarks>
public static class TrackGenerator
{
    public static Generated Generate(ulong seed, GenSettings settings, PieceCatalogue catalogue, Terrain? terrain = null)
    {
        IReadOnlyList<string> problems = settings.Route.Problems();
        if (problems.Count > 0)
        {
            throw new ArgumentException("Route settings refused: " + string.Join("; ", problems), nameof(settings));
        }

        terrain ??= new Terrain(seed, settings.Terrain);
        var walker = new RouteWalker(GenStage.Seed(seed, GenStage.Route), settings.Route, terrain, catalogue);
        (List<Placement> plan, int attempts, int backtracks, int placements) = walker.Walk();
        (List<Placement> levelled, int slopes, float fill, float cut) = Levels.Choose(plan, terrain, catalogue);
        (List<Placement> banked, int banks) = Banks.Choose(levelled, catalogue);

        var track = new Track
        {
            Id = $"gen:{seed}",
            Name = $"Seed {seed}",
            Ground = new TrackGround { Seed = seed, Terrain = settings.Terrain },
            Pieces = banked.Select(TrackPiece.Of).ToArray(),
        };

        // The road's bed (M3d): the ground set under it, with 25° banks and cuttings either side.
        LaidTrack laid = track.Lay(catalogue);
        Terrain bedded = laid.Problems.Count == 0 ? Bed.Lay(terrain, laid) : terrain;

        float length = banked.Sum(p => RoadLength(catalogue, p));
        return new Generated(track, bedded, new GenStats(attempts, backtracks, placements, length, slopes, banks, fill, cut));
    }

    internal static float RoadLength(PieceCatalogue catalogue, Placement p) =>
        new PlacedPiece(catalogue.Find(p.Piece)!, p).Road().Pieces.Sum(c => c.Length);
}

/// <summary>The walk (see <see cref="TrackGenerator"/>), with backtracking.</summary>
internal sealed class RouteWalker(ulong seed, RouteSettings settings, Terrain terrain, PieceCatalogue catalogue)
{
    private const int Size = TrackGrid.Size;

    /// <summary>Placements tried in one attempt before it's begun again from another start.</summary>
    private const int PlacementsPerAttempt = 4000;

    private const int Attempts = 24;

    private enum Move
    {
        Straight1,
        Straight2,
        Straight3,
        Corner30Left,
        Corner30Right,
        Corner50Left,
        Corner50Right,
        SBendLeft,
        SBendRight,
        Chicane,
    }

    private static readonly Move[] Moves = Enum.GetValues<Move>();

    /// <summary>Starting weights, there to be tuned: straights most, corners next, S-bends and chicanes for variety.</summary>
    private static float BaseWeight(Move move) => move switch
    {
        Move.Straight1 => 0.8f,
        Move.Straight2 => 1.0f,
        Move.Straight3 => 0.6f,
        Move.Corner30Left or Move.Corner30Right => 0.5f,
        Move.Corner50Left or Move.Corner50Right => 0.45f,
        Move.SBendLeft or Move.SBendRight => 0.2f,
        _ => 0.12f,
    };

    private static bool Turns(Move move) => move is not (Move.Straight1 or Move.Straight2 or Move.Straight3);

    private sealed class Frame
    {
        public required Tile Cursor { get; init; }

        public required Compass Heading { get; init; }

        public required float Length { get; init; }

        /// <summary>The pieces laid to arrive here, undone when this frame is.</summary>
        public required List<Placement> Laid { get; init; }

        public List<Move> Order { get; set; } = [];

        public int Next { get; set; }
    }

    private readonly bool[] _taken = new bool[Size * Size];
    private readonly Dictionary<string, float> _lengths = new();
    private ulong _state = seed;
    private Compass _finish;

    public (List<Placement> Plan, int Attempts, int Backtracks, int Placements) Walk()
    {
        int backtracks = 0;
        int placements = 0;
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            Array.Clear(_taken);
            _finish = (Compass)(Next() % 4);
            Compass startEdge = TrackGrid.Opposite(_finish);
            int along = 3 + (int)(Next() % (Size - 6));
            Tile start = EdgeTile(startEdge, along);

            // The start gate, and a straight to pull away on.
            var opening = new List<Placement>
            {
                new("base:start", start, 0, (int)_finish, false),
                new("base:straight", start.Step(_finish), 0, (int)_finish, false),
            };
            foreach (Placement p in opening)
            {
                Take(p, true);
            }

            var stack = new List<Frame>
            {
                new() { Cursor = start.Step(_finish, 2), Heading = _finish, Length = 40.0f, Laid = [] },
            };
            stack[0].Order = Options(stack[0]);
            int tried = 0;

            while (stack.Count > 0 && tried < PlacementsPerAttempt)
            {
                Frame here = stack[^1];
                if (CanFinish(here))
                {
                    var plan = new List<Placement>(opening);
                    plan.AddRange(stack.SelectMany(f => f.Laid));
                    plan.Add(new Placement("base:finish", here.Cursor, 0, (int)here.Heading, false));
                    return (plan, attempt, backtracks, placements);
                }

                if (here.Next >= here.Order.Count)
                {
                    foreach (Placement p in here.Laid)
                    {
                        Take(p, false);
                    }

                    stack.RemoveAt(stack.Count - 1);
                    backtracks++;
                    continue;
                }

                Move move = here.Order[here.Next++];
                List<Placement> laid = Lay(move, here.Cursor, here.Heading);
                foreach (Placement p in laid)
                {
                    Take(p, true);
                }

                (Tile cursor, Compass heading) = After(laid[^1]);
                var next = new Frame
                {
                    Cursor = cursor,
                    Heading = heading,
                    Length = here.Length + laid.Sum(Length),
                    Laid = laid,
                };
                next.Order = Options(next);
                stack.Add(next);
                tried++;
                placements++;
            }
        }

        throw new InvalidOperationException($"No route found in {Attempts} attempts ({placements} placements, {backtracks} backtracks).");
    }

    /// <summary>At the finish edge, heading out of the grid, long enough, on a free tile: the finish gate can go here.</summary>
    private bool CanFinish(Frame f) =>
        Distance(f.Cursor) == 0 && f.Heading == _finish && f.Length >= settings.MinLengthM && Free(f.Cursor);

    /// <summary>The moves that fit from here, in the order to try them: drawn by weight, without putting any back.</summary>
    private List<Move> Options(Frame f)
    {
        var weighted = new List<(Move Move, float Weight)>();
        if (f.Length > settings.MaxLengthM || !Free(f.Cursor))
        {
            return [];
        }

        foreach (Move move in Moves)
        {
            List<Placement> laid = Lay(move, f.Cursor, f.Heading);
            var tiles = new HashSet<Tile>();
            bool fits = true;
            foreach (Placement p in laid)
            {
                foreach (Tile t in Placed(p).Tiles)
                {
                    fits &= TrackGrid.Contains(t) && Free(t) && tiles.Add(t);
                }
            }

            if (!fits)
            {
                continue;
            }

            (Tile cursor, Compass heading) = After(laid[^1]);
            if (!TrackGrid.Contains(cursor) || tiles.Contains(cursor) || !Free(cursor) || !FinishReachable(cursor, tiles))
            {
                continue;
            }

            float weight = BaseWeight(move) * (Turns(move) ? settings.Turniness : 1.0f);
            weight *= MathF.Exp(Pull(f) * (Distance(f.Cursor) - Distance(cursor)));
            weight *= GroundFactor(move, f.Cursor, f.Heading, cursor, heading, laid);
            weighted.Add((move, weight));
        }

        var order = new List<Move>();
        while (weighted.Count > 0)
        {
            float total = weighted.Sum(w => w.Weight);
            float pick = Hash64.ToUnitFloat(Next()) * total;
            int k = 0;
            while (k < weighted.Count - 1 && pick >= weighted[k].Weight)
            {
                pick -= weighted[k].Weight;
                k++;
            }

            order.Add(weighted[k].Move);
            weighted.RemoveAt(k);
        }

        return order;
    }

    /// <summary>
    /// How hard a step toward the finish is favoured, per tile of progress: away from it while the straight
    /// line home is shorter than the road still owed, gently toward it in between, and hard toward it once the
    /// road is running long.
    /// </summary>
    private float Pull(Frame f)
    {
        float home = (Distance(f.Cursor) + 1) * TrackGrid.TileM;
        float owed = settings.MinLengthM - f.Length;
        if (home < owed * 0.8f)
        {
            return -0.2f;
        }

        return f.Length + (home * 1.6f) > settings.MaxLengthM ? 0.8f : 0.25f;
    }

    /// <summary>
    /// Favour ground the road can follow (M3-PLAN fact 2): how far the ground rises and falls across the
    /// step, its way in, its way out and the middle of every tile it covers, beyond what its straights could
    /// climb as slopes (a level every two tiles), costs it weight.
    /// </summary>
    /// <remarks>
    /// The first version compared only the ground at the way in with the way out, so a corner over a
    /// hilltop, level at both ends, looked flat; a corner can't climb, and seed 2's road went 9 m into a
    /// hill (2026-11-23).
    /// </remarks>
    private float GroundFactor(Move move, Tile from, Compass heading, Tile to, Compass toHeading, List<Placement> laid)
    {
        Vector3 a = TrackGrid.EdgeMiddle(from, TrackGrid.Opposite(heading), 0);
        Vector3 b = TrackGrid.EdgeMiddle(to, TrackGrid.Opposite(toHeading), 0);
        float low = MathF.Min(terrain.HeightAt(a.X, a.Z), terrain.HeightAt(b.X, b.Z));
        float high = MathF.Max(terrain.HeightAt(a.X, a.Z), terrain.HeightAt(b.X, b.Z));
        foreach (Placement p in laid)
        {
            foreach (Tile t in Placed(p).Tiles)
            {
                Vector3 m = TrackGrid.Middle(t, 0);
                float h = terrain.HeightAt(m.X, m.Z);
                low = MathF.Min(low, h);
                high = MathF.Max(high, h);
            }
        }

        int straights = move switch { Move.Straight1 => 1, Move.Straight2 => 2, Move.Straight3 => 3, _ => 0 };
        float excess = MathF.Max(0.0f, high - low - (straights * TrackGrid.LevelM / 2.0f) - 1.0f);
        return MathF.Exp(-excess / 0.75f);
    }

    /// <summary>The placements a move makes from a cursor: a straight run, or one piece turned to the heading and mirrored for a left.</summary>
    private static List<Placement> Lay(Move move, Tile at, Compass heading)
    {
        int rot = (int)heading;
        return move switch
        {
            Move.Straight1 or Move.Straight2 or Move.Straight3 => Enumerable.Range(0, move - Move.Straight1 + 1)
                .Select(k => new Placement("base:straight", at.Step(heading, k), 0, rot, false)).ToList(),
            Move.Corner30Left => [new("base:corner-30", at, 0, rot, true)],
            Move.Corner30Right => [new("base:corner-30", at, 0, rot, false)],
            Move.Corner50Left => [new("base:corner-50", at, 0, rot, true)],
            Move.Corner50Right => [new("base:corner-50", at, 0, rot, false)],
            Move.SBendLeft => [new("base:s-bend", at, 0, rot, true)],
            Move.SBendRight => [new("base:s-bend", at, 0, rot, false)],
            _ => [new("base:chicane", at, 0, rot, false)],
        };
    }

    /// <summary>Whether a free tile on the finish edge can be reached over free tiles from a cursor, with some tiles about to be taken.</summary>
    private bool FinishReachable(Tile from, HashSet<Tile> taking)
    {
        var seen = new bool[Size * Size];
        var queue = new Queue<Tile>();
        queue.Enqueue(from);
        seen[Index(from)] = true;
        while (queue.Count > 0)
        {
            Tile t = queue.Dequeue();
            if (Distance(t) == 0)
            {
                return true;
            }

            foreach (Compass way in new[] { Compass.North, Compass.East, Compass.South, Compass.West })
            {
                Tile n = t.Step(way);
                if (TrackGrid.Contains(n) && !seen[Index(n)] && Free(n) && !taking.Contains(n))
                {
                    seen[Index(n)] = true;
                    queue.Enqueue(n);
                }
            }
        }

        return false;
    }

    private PlacedPiece Placed(Placement p) => new(catalogue.Find(p.Piece)!, p);

    private (Tile Cursor, Compass Heading) After(Placement last)
    {
        WorldConnector o = Placed(last).Out;
        return (o.Beyond, o.Facing);
    }

    private float Length(Placement p)
    {
        if (!_lengths.TryGetValue(p.Piece, out float length))
        {
            length = TrackGenerator.RoadLength(catalogue, p);
            _lengths[p.Piece] = length;
        }

        return length;
    }

    private void Take(Placement p, bool taken)
    {
        foreach (Tile t in Placed(p).Tiles)
        {
            _taken[Index(t)] = taken;
        }
    }

    private bool Free(Tile t) => TrackGrid.Contains(t) && !_taken[Index(t)];

    private static int Index(Tile t) => (t.Row * Size) + t.Column;

    /// <summary>Tiles from here to the finish edge, along the way out.</summary>
    private int Distance(Tile t) => _finish switch
    {
        Compass.North => t.Row,
        Compass.South => Size - 1 - t.Row,
        Compass.East => Size - 1 - t.Column,
        _ => t.Column,
    };

    /// <summary>A tile on one edge of the grid, some way along it.</summary>
    private static Tile EdgeTile(Compass edge, int along) => edge switch
    {
        Compass.North => new Tile(along, 0),
        Compass.South => new Tile(along, Size - 1),
        Compass.East => new Tile(Size - 1, along),
        _ => new Tile(0, along),
    };

    /// <summary>The next number from the route's seed: splitmix64's step and <see cref="Hash64.Mix"/>.</summary>
    private ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Hash64.Mix(_state);
    }
}

/// <summary>
/// Every piece's level at once (see <see cref="TrackGenerator"/>): the cheapest way, in earth moved, to
/// keep the road on the ground, where a level changes only on a slope and a slope takes two straights.
/// </summary>
internal static class Levels
{
    private const int Lowest = -Track.MaxLevel;
    private const int Highest = Track.MaxLevel;
    private const int Count = Highest - Lowest + 1;

    /// <summary>
    /// A metre of road below the ground costs this many metres above it. Before the bed it was all but
    /// forbidden (1000), since sand standing on a buried road is what the tyres meet: at 6 the autodriver
    /// slid off a corner on five generated tracks out of five (2026-11-23). The bed now carves a cutting with
    /// 25° sides, so a road may go into a hill again, a little dearer than building one up.
    /// </summary>
    private const float CutWeight = 1.5f;

    private const float SampleM = 2.0f;

    private enum Choice : byte
    {
        Same,
        Up,
        Down,
    }

    public static (List<Placement> Plan, int Slopes, float WorstFillM, float WorstCutM) Choose(List<Placement> plan, Terrain terrain, PieceCatalogue catalogue)
    {
        int n = plan.Count;
        var cost = new float[(n + 1) * Count];
        var from = new int[(n + 1) * Count];
        var choice = new Choice[(n + 1) * Count];
        Array.Fill(cost, float.PositiveInfinity);
        for (int l = 0; l < Count; l++)
        {
            cost[l] = 0.0f;
        }

        // The road at level 0, sampled, for each piece as it stands and for each pair of straights as a slope.
        var samples = plan.Select(p => Samples(new PlacedPiece(catalogue.Find(p.Piece)!, p))).ToList();
        var up = new List<(Vector3, float)[]?>();
        var down = new List<(Vector3, float)[]?>();
        for (int k = 0; k < n; k++)
        {
            bool pair = k + 1 < n && plan[k].Piece == "base:straight" && plan[k + 1].Piece == "base:straight";
            up.Add(pair ? Samples(new PlacedPiece(catalogue.Find("base:slope-up")!, plan[k] with { Piece = "base:slope-up" })) : null);
            down.Add(pair ? Samples(new PlacedPiece(catalogue.Find("base:slope-down")!, plan[k] with { Piece = "base:slope-down" })) : null);
        }

        for (int k = 0; k < n; k++)
        {
            for (int l = 0; l < Count; l++)
            {
                float here = cost[(k * Count) + l];
                if (float.IsPositiveInfinity(here))
                {
                    continue;
                }

                int level = l + Lowest;
                Relax(k + 1, l, here + Earth(samples[k], level, terrain), k, l, Choice.Same);
                if (up[k] is { } u && l + 1 < Count)
                {
                    Relax(k + 2, l + 1, here + Earth(u, level, terrain), k, l, Choice.Up);
                }

                if (down[k] is { } d && l > 0)
                {
                    Relax(k + 2, l - 1, here + Earth(d, level, terrain), k, l, Choice.Down);
                }
            }
        }

        // Back from the cheapest end, rebuilding the plan with its levels and slopes.
        int best = 0;
        for (int l = 1; l < Count; l++)
        {
            best = cost[(n * Count) + l] < cost[(n * Count) + best] ? l : best;
        }

        var steps = new List<(int K, int L, Choice C)>();
        for (int k = n, l = best; k > 0;)
        {
            int at = (k * Count) + l;
            int pk = from[at] / Count;
            int pl = from[at] % Count;
            steps.Add((pk, pl, choice[at]));
            k = pk;
            l = pl;
        }

        steps.Reverse();
        var result = new List<Placement>();
        int slopes = 0;
        float worstFill = 0.0f;
        float worstCut = 0.0f;
        foreach ((int k, int l, Choice c) in steps)
        {
            int level = l + Lowest;
            Placement placed = c switch
            {
                Choice.Up => plan[k] with { Piece = "base:slope-up", Level = level },
                Choice.Down => plan[k] with { Piece = "base:slope-down", Level = level },
                _ => plan[k] with { Level = level },
            };
            slopes += c == Choice.Same ? 0 : 1;
            result.Add(placed);
            foreach ((Vector3 at, float _) in Samples(new PlacedPiece(catalogue.Find(placed.Piece)!, placed)))
            {
                float gap = at.Y - terrain.HeightAt(at.X, at.Z);
                worstFill = MathF.Max(worstFill, gap);
                worstCut = MathF.Max(worstCut, -gap);
            }
        }

        return (result, slopes, worstFill, worstCut);

        void Relax(int k, int l, float total, int pk, int pl, Choice c)
        {
            int at = (k * Count) + l;
            if (total < cost[at])
            {
                cost[at] = total;
                from[at] = (pk * Count) + pl;
                choice[at] = c;
            }
        }
    }

    /// <summary>Points on the road, its middle and both edges every couple of metres, each with the length of road it stands for.</summary>
    private static (Vector3 At, float Weight)[] Samples(PlacedPiece piece)
    {
        var points = new List<(Vector3, float)>();
        float half = (piece.Def.WidthM / 2.0f) - 0.5f;
        foreach (ICentreline c in piece.Road().Pieces)
        {
            int steps = Math.Max(1, (int)MathF.Ceiling(c.Length / SampleM));
            for (int s = 0; s < steps; s++)
            {
                Frame f = c.FrameAt(c.Length * (s + 0.5f) / steps);
                float w = c.Length / steps / 3.0f;
                points.Add((f.Position, w));
                points.Add((f.Across(-half, 0.0f), w));
                points.Add((f.Across(half, 0.0f), w));
            }
        }

        return points.ToArray();
    }

    /// <summary>The earth a piece moves at a level: metres of fill, and of cut weighted heavier, along its road.</summary>
    private static float Earth((Vector3 At, float Weight)[] samples, int level, Terrain terrain)
    {
        float total = 0.0f;
        float lift = level * TrackGrid.LevelM;
        foreach ((Vector3 at, float w) in samples)
        {
            float gap = at.Y + lift - terrain.HeightAt(at.X, at.Z);
            total += w * (gap >= 0.0f ? gap : -gap * CutWeight);
        }

        return total;
    }
}

/// <summary>
/// Banks (see <see cref="TrackGenerator"/>): a corner the car arrives at well over its flat speed becomes
/// its banked twin. The speed is a cheap estimate, a car accelerating at a steady rate from each corner's
/// exit, up to its top speed, with the grip the autodriver plans for: M3c brings the full one.
/// </summary>
internal static class Banks
{
    private const float Accel = 3.5f;
    private const float Top = 49.0f;
    private const float Grip = 1.1f * 0.8f;
    private const float BankDeg = 20.0f;

    /// <summary>Over its flat speed by this much, a corner is banked.</summary>
    private const float Margin = 1.3f;

    public static (List<Placement> Plan, int Banked) Choose(List<Placement> plan, PieceCatalogue catalogue)
    {
        var result = new List<Placement>();
        int banked = 0;
        float v = 0.0f;
        foreach (Placement p in plan)
        {
            float length = TrackGenerator.RoadLength(catalogue, p);
            float? radius = p.Piece switch
            {
                "base:corner-30" => 30.0f,
                "base:corner-50" => 50.0f,
                _ => null,
            };

            if (radius is float r)
            {
                float flat = Limit(r, 0.0f);
                if (v > flat * Margin)
                {
                    result.Add(p with { Piece = p.Piece + "-banked" });
                    banked++;
                    v = MathF.Min(v, Limit(r, BankDeg));
                }
                else
                {
                    result.Add(p);
                    v = MathF.Min(v, flat);
                }

                continue;
            }

            result.Add(p);
            float limit = p.Piece switch
            {
                "base:chicane" => Limit(26.0f, 0.0f),
                "base:s-bend" => Limit(130.0f, 0.0f),
                _ => Top,
            };
            v = MathF.Min(MathF.Min(Top, MathF.Sqrt((v * v) + (2.0f * Accel * length))), limit);
        }

        return (result, banked);
    }

    /// <summary>The banked-curve formula, at the autodriver's share of the stock car's grip.</summary>
    private static float Limit(float radius, float bankDeg)
    {
        float tan = MathF.Tan(bankDeg * (MathF.PI / 180.0f));
        return MathF.Sqrt(9.81f * radius * (tan + Grip) / (1.0f - (Grip * tan)));
    }
}
