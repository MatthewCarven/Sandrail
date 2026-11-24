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

    /// <summary>
    /// The share of seeds that are circuits (M3g, decision 10): a seed is one when its own draw is at least
    /// 1 less this, which keeps seed 5 a sprint. 0 makes every seed a sprint, 1 every seed a circuit.
    /// </summary>
    public float CircuitShare { get; init; } = 0.5f;

    /// <summary>A circuit's lap, metres of road (decision 9: 0.8 to 1.5 km).</summary>
    public float MinLapM { get; init; } = 800.0f;

    public float MaxLapM { get; init; } = 1500.0f;

    /// <summary>The laps in a run on a circuit (decision 8).</summary>
    public int Laps { get; init; } = 3;

    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!(MinLengthM >= 200.0f && MaxLengthM >= MinLengthM + 200.0f && MaxLengthM <= 10_000.0f))
        {
            problems.Add($"lengths are {MinLengthM} to {MaxLengthM} m, and need 200 m at least, a 200 m window and at most 10 km");
        }

        if (!(CircuitShare >= 0.0f && CircuitShare <= 1.0f))
        {
            problems.Add($"the circuit share is {CircuitShare}, and must be 0 to 1");
        }

        if (!(MinLapM >= 400.0f && MaxLapM >= MinLapM + 200.0f && MaxLapM <= 2400.0f))
        {
            problems.Add($"laps are {MinLapM} to {MaxLapM} m, and need 400 m at least, a 200 m window and at most 2.4 km, which the grid has room for");
        }

        if (Laps is < 1 or > Track.MaxLaps)
        {
            problems.Add($"a circuit is {Laps} laps, and must be 1 to {Track.MaxLaps}");
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

    public StuntSettings Stunts { get; init; } = new();

    public RockSettings Rocks { get; init; } = new();
}

/// <summary>
/// What making a track took, for the bench: tries, how often it backed out, how long it came out (a lap, on
/// a circuit), how the road met the ground, its stunts, and whether it's a circuit.
/// </summary>
public sealed record GenStats(int Attempts, int Backtracks, int Placements, float LengthM, int Slopes, int Banked, float WorstFillM, float WorstCutM, StuntStats Stunts, bool Circuit = false);

/// <summary>A generated track, the ground under it, and what making it took.</summary>
public sealed record Generated(Track Track, Terrain Terrain, GenStats Stats)
{
    /// <summary>The rocks off its road (M3d): not in the track file, since the seed and the track make them again.</summary>
    public IReadOnlyList<Rock> Rocks { get; init; } = [];
}

/// <summary>
/// Makes a track from a seed (M3-PLAN): terrain, the route, its levels and banks, stunts and checkpoints
/// (<see cref="StuntPlanner"/>, M3c), and the road's bed.
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
    /// <summary>
    /// The generator's version (M4-PLAN, M4a): it goes up whenever a seed's track changes, so a code says
    /// exactly which track it means. <c>TheGeneratorVersionPinsItsTracks</c> holds it to that: it pins seeds 1
    /// to 10 under each version, and fails when the output changes and this doesn't.
    /// </summary>
    /// <remarks>Version 1 is the generator as M3 passed, 2026-11-24.</remarks>
    public const int Version = 1;

    /// <summary>What a generated track's ID ends with when it was made with other dials, and so has no code.</summary>
    public const string OwnDialsSuffix = "-dials";

    public static Generated Generate(ulong seed, GenSettings settings, PieceCatalogue catalogue, Terrain? terrain = null)
    {
        IReadOnlyList<string> problems = [.. settings.Route.Problems(), .. settings.Stunts.Problems(), .. settings.Rocks.Problems()];
        if (problems.Count > 0)
        {
            throw new ArgumentException("Generator settings refused: " + string.Join("; ", problems), nameof(settings));
        }

        terrain ??= new Terrain(seed, settings.Terrain);

        // Sprint or circuit (M3g), from a stage seed of its own, so the route's own draws are a sprint's as before.
        bool circuit = IsCircuit(seed, settings.Route);
        var walker = new RouteWalker(GenStage.Seed(seed, GenStage.Route), settings.Route, terrain, catalogue);
        (List<Placement> plan, int attempts, int backtracks, int placements) = circuit ? walker.WalkCircuit() : walker.Walk();
        (List<Placement> levelled, int slopes, float fill, float cut) = Levels.Choose(plan, terrain, catalogue, circuit);
        (List<Placement> banked, int banks) = Banks.Choose(levelled, catalogue, circuit);

        // Stunts and checkpoints (M3c), swapped in where the route already has their footprint, from their own seed.
        var ground = new TrackGround { Seed = seed, Terrain = settings.Terrain };
        int laps = circuit ? settings.Route.Laps : 0;

        // Its code (M4a): gen:5v1. A track made with other dials has no code, so its ID says so.
        bool ownDials = settings != new GenSettings();
        string id = TrackCode.Of(seed).TrackId + (ownDials ? OwnDialsSuffix : string.Empty);
        string name = $"Seed {seed}" + (ownDials ? " (own dials)" : string.Empty);
        Track Build(List<Placement> pieces) => new()
        {
            Id = id,
            Name = name,
            Ground = ground,
            Laps = laps,
            Pieces = pieces.Select(TrackPiece.Of).ToArray(),
        };

        // The route's length as walked (a lap, on a circuit): a jump's gap is distance driven too, through the air.
        float length = banked.Sum(p => RoadLength(catalogue, p));

        // A circuit has checkpoints a fifth of a lap apart at most, three or more a lap where its straights allow
        // (M3g fact 6): its middle invites a shortcut. A quarter apart gave a median of two.
        float every = settings.Stunts.CheckpointEveryM;
        if (circuit && every > 0.0f)
        {
            every = MathF.Min(every, length / 5.0f);
        }

        (List<Placement> stunted, StuntStats stunts) = StuntPlanner.Choose(
            GenStage.Seed(seed, GenStage.Stunts), settings.Stunts, banked, catalogue, pieces => Build(pieces).Lay(catalogue));
        (List<Placement> gated, int checkpoints) = StuntPlanner.Checkpoints(stunted, every, catalogue);
        Track track = Build(gated);

        // The road's bed (M3d): the ground set under it, with 25° banks and cuttings either side.
        LaidTrack laid = track.Lay(catalogue);
        Terrain bedded = laid.Problems.Count == 0 ? Bed.Lay(terrain, laid) : terrain;

        return new Generated(track, bedded,
            new GenStats(attempts, backtracks, placements, length, slopes, banks, fill, cut, stunts with { Checkpoints = checkpoints }, circuit))
        {
            // Rocks off the road (the rest of M3d), from the ground's own seed, on the bedded ground.
            Rocks = laid.Problems.Count == 0 ? Generation.Rocks.Scatter(seed, settings.Rocks, laid, bedded) : [],
        };
    }

    /// <summary>
    /// Whether a seed makes a circuit (M3g, decision 10): its draw from <see cref="GenStage.Kind"/> at least 1
    /// less the share. Of the two halves at 0.5, this one keeps seed 5 a sprint (fact 7).
    /// </summary>
    public static bool IsCircuit(ulong seed, RouteSettings route) =>
        route.CircuitShare > 0.0f && Hash64.ToUnitFloat(GenStage.Seed(seed, GenStage.Kind)) >= 1.0f - route.CircuitShare;

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

        /// <summary>On a circuit, whether the search for the way home has been tried from here.</summary>
        public bool CloseTried { get; set; }
    }

    /// <summary>A circuit's line stands at least this many tiles in from every edge, so the lap has room all round.</summary>
    private const int Inset = 8;

    /// <summary>The most pieces the search for the way home tries (M3g, "the close").</summary>
    private const int CloseDepth = 4;

    /// <summary>The furthest one move takes the cursor, tiles: what lets the search give up early.</summary>
    private const int MoveReach = 3;

    // A circuit (M3g): its start-finish line, the tile behind it the lap comes home through, and the way it faces.
    private bool _circuit;
    private Tile _line;
    private Tile _home;
    private Compass _lineHeading;

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

    /// <summary>
    /// A circuit (M3g): a start-finish line well inside the grid, the walk out from its front as a sprint's
    /// is, turning for home about halfway round its lap, and, from every place it reaches near home, a search
    /// over the last few pieces for a run that comes in behind the line facing along it, the lap's length
    /// right. None, and the walk carries on, backing out of dead ends as a sprint's does.
    /// </summary>
    public (List<Placement> Plan, int Attempts, int Backtracks, int Placements) WalkCircuit()
    {
        _circuit = true;
        int backtracks = 0;
        int placements = 0;
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            Array.Clear(_taken);
            _lineHeading = (Compass)(Next() % 4);
            _line = new Tile(Inset + (int)(Next() % (Size - (2 * Inset))), Inset + (int)(Next() % (Size - (2 * Inset))));
            _home = _line.Step(TrackGrid.Opposite(_lineHeading));

            var opening = new List<Placement>
            {
                new("base:start-finish", _line, 0, (int)_lineHeading, false),
                new("base:straight", _line.Step(_lineHeading), 0, (int)_lineHeading, false),
            };
            foreach (Placement p in opening)
            {
                Take(p, true);
            }

            // The tile behind the line is held for the way home: only the close may lay road on it.
            _taken[Index(_home)] = true;

            var stack = new List<Frame>
            {
                new() { Cursor = _line.Step(_lineHeading, 2), Heading = _lineHeading, Length = 40.0f, Laid = [] },
            };
            stack[0].Order = Options(stack[0]);
            int tried = 0;

            while (stack.Count > 0 && tried < PlacementsPerAttempt)
            {
                Frame here = stack[^1];
                if (!here.CloseTried)
                {
                    here.CloseTried = true;
                    if (Close(here) is List<Placement> closing)
                    {
                        var plan = new List<Placement>(opening);
                        plan.AddRange(stack.SelectMany(f => f.Laid));
                        plan.AddRange(closing);
                        return (plan, attempt, backtracks, placements);
                    }
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

        throw new InvalidOperationException($"No circuit found in {Attempts} attempts ({placements} placements, {backtracks} backtracks).");
    }

    /// <summary>
    /// The way home from a place on a circuit's walk: up to <see cref="CloseDepth"/> pieces on free tiles (the
    /// one behind the line included) that come in behind the line facing along it, the lap's length in its
    /// window. Moves are tried in a fixed order, straights first; null if none fits.
    /// </summary>
    private List<Placement>? Close(Frame f)
    {
        int reach = Manhattan(f.Cursor, _line);
        if (reach > MoveReach * CloseDepth || f.Length + (reach * TrackGrid.TileM) > settings.MaxLapM
            || f.Length + (CloseDepth * 110.0f) < settings.MinLapM)
        {
            return null;
        }

        var path = new List<Placement>();
        var tiles = new HashSet<Tile>();
        return Search(f.Cursor, f.Heading, f.Length, 0);

        List<Placement>? Search(Tile cursor, Compass heading, float length, int depth)
        {
            if (cursor == _line && heading == _lineHeading)
            {
                return length >= settings.MinLapM && length <= settings.MaxLapM ? new List<Placement>(path) : null;
            }

            if (depth == CloseDepth || Manhattan(cursor, _line) > MoveReach * (CloseDepth - depth))
            {
                return null;
            }

            foreach (Move move in Moves)
            {
                List<Placement> laid = Lay(move, cursor, heading);
                var added = new List<Tile>();
                bool fits = true;
                foreach (Placement p in laid)
                {
                    foreach (Tile t in Placed(p).Tiles)
                    {
                        if (!TrackGrid.Contains(t) || !(Free(t) || t == _home) || tiles.Contains(t) || added.Contains(t))
                        {
                            fits = false;
                        }

                        added.Add(t);
                    }
                }

                float total = length + laid.Sum(Length);
                if (!fits || total > settings.MaxLapM)
                {
                    continue;
                }

                (Tile next, Compass nextHeading) = After(laid[^1]);
                path.AddRange(laid);
                tiles.UnionWith(added);
                if (Search(next, nextHeading, total, depth + 1) is List<Placement> found)
                {
                    return found;
                }

                path.RemoveRange(path.Count - laid.Count, laid.Count);
                tiles.ExceptWith(added);
            }

            return null;
        }
    }

    private static int Manhattan(Tile a, Tile b) => Math.Abs(a.Column - b.Column) + Math.Abs(a.Row - b.Row);

    /// <summary>At the finish edge, heading out of the grid, long enough, on a free tile: the finish gate can go here.</summary>
    private bool CanFinish(Frame f) =>
        Distance(f.Cursor) == 0 && f.Heading == _finish && f.Length >= settings.MinLengthM && Free(f.Cursor);

    /// <summary>The moves that fit from here, in the order to try them: drawn by weight, without putting any back.</summary>
    private List<Move> Options(Frame f)
    {
        var weighted = new List<(Move Move, float Weight)>();
        if (f.Length > (_circuit ? settings.MaxLapM : settings.MaxLengthM) || !Free(f.Cursor))
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
        if (_circuit)
        {
            // Out from the line with a gentle push away for the first half of a lap, then home: gently while
            // road is still owed, hard once the lap is running long.
            float back = (Distance(f.Cursor) + 1) * TrackGrid.TileM;
            if (f.Length < settings.MinLapM * 0.45f)
            {
                return -0.15f;
            }

            if (back < (settings.MinLapM - f.Length) * 0.8f)
            {
                return -0.05f;
            }

            return f.Length + (back * 1.6f) > settings.MaxLapM ? 0.8f : 0.3f;
        }

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

            // A sprint's finish edge; a circuit's way home, the held tile behind its line, next door.
            if (_circuit ? Manhattan(t, _home) <= 1 : Distance(t) == 0)
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

    /// <summary>Tiles from here to the finish edge, along the way out; on a circuit, to the tile behind its line.</summary>
    private int Distance(Tile t) => _circuit ? Manhattan(t, _home) : _finish switch
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

    public static (List<Placement> Plan, int Slopes, float WorstFillM, float WorstCutM) Choose(
        List<Placement> plan, Terrain terrain, PieceCatalogue catalogue, bool circuit = false)
    {
        int n = plan.Count;

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

        // The earth each piece moves at each level, as it stands and as either slope: worked out once, since a
        // circuit runs the search once for every level it might start at.
        var same = new float[n * Count];
        var rise = new float[n * Count];
        var fall = new float[n * Count];
        for (int k = 0; k < n; k++)
        {
            for (int l = 0; l < Count; l++)
            {
                int level = l + Lowest;
                same[(k * Count) + l] = Earth(samples[k], level, terrain);
                rise[(k * Count) + l] = up[k] is { } u && l + 1 < Count ? Earth(u, level, terrain) : float.NaN;
                fall[(k * Count) + l] = down[k] is { } d && l > 0 ? Earth(d, level, terrain) : float.NaN;
            }
        }

        // A sprint starts at whatever level is cheapest and ends at whatever level is cheapest. A circuit
        // (M3g) must come back round to the level its line is at: the search is run from each starting level,
        // held to end there, and the cheapest lap kept.
        float[] cost;
        int[] from;
        Choice[] choice;
        int best;
        if (!circuit)
        {
            (cost, from, choice) = Search(-1);
            best = 0;
            for (int l = 1; l < Count; l++)
            {
                best = cost[(n * Count) + l] < cost[(n * Count) + best] ? l : best;
            }
        }
        else
        {
            (cost, from, choice) = Search(0);
            best = 0;
            for (int l = 1; l < Count; l++)
            {
                (float[] c, int[] f, Choice[] h) = Search(l);
                if (c[(n * Count) + l] < cost[(n * Count) + best])
                {
                    (cost, from, choice, best) = (c, f, h, l);
                }
            }

            if (float.IsPositiveInfinity(cost[(n * Count) + best]))
            {
                throw new InvalidOperationException("No levels bring the circuit back round to its line's level.");
            }
        }

        // Back from the chosen end, rebuilding the plan with its levels and slopes.

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

        // The cheapest way to every (piece, level), from one starting level, or from any (-1).
        (float[] Cost, int[] From, Choice[] Choice) Search(int start)
        {
            var c = new float[(n + 1) * Count];
            var f = new int[(n + 1) * Count];
            var h = new Choice[(n + 1) * Count];
            Array.Fill(c, float.PositiveInfinity);
            for (int l = 0; l < Count; l++)
            {
                if (start < 0 || l == start)
                {
                    c[l] = 0.0f;
                }
            }

            for (int k = 0; k < n; k++)
            {
                for (int l = 0; l < Count; l++)
                {
                    float here = c[(k * Count) + l];
                    if (float.IsPositiveInfinity(here))
                    {
                        continue;
                    }

                    Relax(k + 1, l, here + same[(k * Count) + l], k, l, Choice.Same);
                    if (!float.IsNaN(rise[(k * Count) + l]))
                    {
                        Relax(k + 2, l + 1, here + rise[(k * Count) + l], k, l, Choice.Up);
                    }

                    if (!float.IsNaN(fall[(k * Count) + l]))
                    {
                        Relax(k + 2, l - 1, here + fall[(k * Count) + l], k, l, Choice.Down);
                    }
                }
            }

            return (c, f, h);

            void Relax(int k, int l, float total, int pk, int pl, Choice choice)
            {
                int at = (k * Count) + l;
                if (total < c[at])
                {
                    c[at] = total;
                    f[at] = (pk * Count) + pl;
                    h[at] = choice;
                }
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

    public static (List<Placement> Plan, int Banked) Choose(List<Placement> plan, PieceCatalogue catalogue, bool circuit = false)
    {
        // A circuit's corners are judged on a flying lap (M3g): the speed it comes round to the line with.
        float flying = circuit ? Pass(plan, catalogue, 0.0f).Speed : 0.0f;
        (List<Placement> result, int banked, _) = Pass(plan, catalogue, flying);
        return (result, banked);
    }

    /// <summary>Once along the plan from a speed: each corner banked or not, and the speed at the end.</summary>
    private static (List<Placement> Plan, int Banked, float Speed) Pass(List<Placement> plan, PieceCatalogue catalogue, float start)
    {
        var result = new List<Placement>();
        int banked = 0;
        float v = start;
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

        return (result, banked, v);
    }

    /// <summary>The banked-curve formula, at the autodriver's share of the stock car's grip.</summary>
    private static float Limit(float radius, float bankDeg)
    {
        float tan = MathF.Tan(bankDeg * (MathF.PI / 180.0f));
        return MathF.Sqrt(9.81f * radius * (tan + Grip) / (1.0f - (Grip * tan)));
    }
}
