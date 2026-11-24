namespace Sandrail.Core.Profiles;

/// <summary>One physics tick of a run, as the <see cref="RunMeter"/> takes it.</summary>
/// <param name="Piece">The route piece the car is on, by its place in the route.</param>
/// <param name="Lap">Laps done: 0 on a sprint, and on a circuit the lap lines passed so far.</param>
/// <param name="SpeedMps">How fast the car is going.</param>
/// <param name="OffRoad">Whether the car is off the road, on the ground: its middle further from the road's than the road leaves its tyres.</param>
/// <param name="SlideDeg">The body's slip angle, degrees, either way; NaN where a slide isn't read (slow, in the air, in a loop, where the car points at the sky).</param>
/// <param name="AlongM">How far along the route the car is, laps included.</param>
public readonly record struct MeterTick(int Piece, int Lap, float SpeedMps, bool OffRoad, float SlideDeg, float AlongM);

/// <summary>A route piece in a run: the time on it and the slowest speed on it.</summary>
/// <param name="Seconds">Time on the piece; 0 if the run never reached it.</param>
/// <param name="SlowestMps">The slowest the car went on it; NaN if the run never reached it.</param>
public readonly record struct PieceRun(double Seconds, float SlowestMps);

/// <summary>
/// What a run comes to (PROFILE-PLAN, the model): the race's times, how hard it was on the car, and the time and
/// slowest speed on each route piece, for one row of the profile sheet.
/// </summary>
public sealed record RunSummary
{
    /// <summary>Under this long with a wheel off the road, in all, a run can still be clean (a starting number).</summary>
    public const double CleanOffRoadS = 0.5;

    /// <summary>A slide past this many degrees is a spin, as the lap oracle has always counted one.</summary>
    public const float SpinDeg = 30.0f;

    /// <summary>Whether the run reached the finish with every checkpoint passed.</summary>
    public bool Finished { get; init; }

    /// <summary>The run's time if it finished.</summary>
    public double? Seconds { get; init; }

    /// <summary>The time at each gate passed after the start, as the race timer gives them: the finish last, if reached.</summary>
    public IReadOnlyList<double> Splits { get; init; } = [];

    /// <summary>On a circuit, each lap done; empty on a sprint.</summary>
    public IReadOnlyList<double> Laps { get; init; } = [];

    /// <summary>On a circuit, the fastest lap done, from 0; null on a sprint or before a lap is done.</summary>
    public int? BestLap { get; init; }

    public float TopMps { get; init; }

    /// <summary>Time with a wheel off the road, the whole run.</summary>
    public double OffRoadSeconds { get; init; }

    /// <summary>Time off the road on each route piece, the whole run, every lap.</summary>
    public IReadOnlyList<double> OffRoadByPiece { get; init; } = [];

    public float WorstSlideDeg { get; init; }

    /// <summary>Slides past <see cref="SpinDeg"/>, each counted once as it begins.</summary>
    public int Spins { get; init; }

    /// <summary>Times the car was put back on the road (R, or the auto-respawn).</summary>
    public int Respawns { get; init; }

    /// <summary>The furthest along the route the car got, laps included.</summary>
    public float FurthestM { get; init; }

    /// <summary>
    /// Each route piece in the run, in route order; on a circuit, on its fastest lap (or the lap it got to, if it
    /// never finished one).
    /// </summary>
    public IReadOnlyList<PieceRun> Pieces { get; init; } = [];

    /// <summary>Finished, under <see cref="CleanOffRoadS"/> off the road, no spin and no respawn.</summary>
    public bool Clean => Finished && OffRoadSeconds < CleanOffRoadS && Spins == 0 && Respawns == 0;
}

/// <summary>
/// Measures a run tick by tick (PROFILE-PLAN, P1): fed what the car is doing once a physics tick while the race
/// clock runs, it keeps the time and slowest speed on each route piece, lap by lap, the top speed, the time off the
/// road, the worst slide, the spins and the respawns, and gives a <see cref="RunSummary"/>.
/// </summary>
/// <remarks>
/// It's pure: what counts as off the road, or as a slide worth reading, is the caller's to say (the game reads both
/// off the car the same way for a profile, a run of his and the <c>track</c> oracle), so the same rule writes every
/// row. Time is counted in ticks and turned into seconds at the end, so a piece's time is exact to the tick.
/// </remarks>
public sealed class RunMeter
{
    private readonly int _pieces;
    private readonly int _ticksPerSecond;
    private readonly List<int[]> _ticks = new();
    private readonly List<float[]> _slowest = new();
    private readonly int[] _offTicks;
    private float _top;
    private float _worstSlide;
    private bool _spinning;
    private int _spins;
    private int _respawns;
    private float _furthest;
    private int _lastLap;

    /// <param name="pieces">How many pieces the route has.</param>
    /// <param name="ticksPerSecond">Physics ticks in a second of game time.</param>
    public RunMeter(int pieces, int ticksPerSecond)
    {
        if (pieces < 1 || ticksPerSecond < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pieces), "A run needs a piece to be on and ticks to count.");
        }

        _pieces = pieces;
        _ticksPerSecond = ticksPerSecond;
        _offTicks = new int[pieces];
    }

    /// <summary>One physics tick of the run.</summary>
    public void Tick(in MeterTick t)
    {
        if ((uint)t.Piece >= (uint)_pieces || t.Lap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(t), $"Piece {t.Piece} of {_pieces}, lap {t.Lap}: not on the route.");
        }

        while (_ticks.Count <= t.Lap)
        {
            _ticks.Add(new int[_pieces]);
            _slowest.Add(Enumerable.Repeat(float.NaN, _pieces).ToArray());
        }

        _lastLap = Math.Max(_lastLap, t.Lap);
        _ticks[t.Lap][t.Piece]++;
        float slowest = _slowest[t.Lap][t.Piece];
        _slowest[t.Lap][t.Piece] = float.IsNaN(slowest) ? t.SpeedMps : MathF.Min(slowest, t.SpeedMps);
        _top = MathF.Max(_top, t.SpeedMps);
        _furthest = MathF.Max(_furthest, t.AlongM);
        if (t.OffRoad)
        {
            _offTicks[t.Piece]++;
        }

        // A slide is counted as a spin once, as it begins, as the lap oracle counts them.
        bool read = !float.IsNaN(t.SlideDeg);
        float slide = read ? MathF.Abs(t.SlideDeg) : 0.0f;
        if (read)
        {
            _worstSlide = MathF.Max(_worstSlide, slide);
        }

        bool spinning = slide > RunSummary.SpinDeg;
        if (spinning && !_spinning)
        {
            _spins++;
        }

        _spinning = spinning;
    }

    /// <summary>The car was put back on the road.</summary>
    public void Respawned() => _respawns++;

    /// <summary>Time off the road on a piece so far, seconds, the way the <c>track</c> oracle has always printed it.</summary>
    public float OffRoadOn(int piece) => _offTicks[piece] / (float)_ticksPerSecond;

    /// <summary>Time off the road so far, seconds, the way the <c>track</c> oracle has always printed it.</summary>
    public float OffRoadTotal => _offTicks.Sum() / (float)_ticksPerSecond;

    public float TopMps => _top;

    public float WorstSlideDeg => _worstSlide;

    /// <summary>The run so far, or as it ended.</summary>
    /// <param name="finished">Whether the race timer says it finished.</param>
    /// <param name="splits">The race timer's splits.</param>
    /// <param name="gatesPerLap">On a circuit, the gates a lap passes (its checkpoints and the line); 0 on a sprint.</param>
    public RunSummary Summary(bool finished, IReadOnlyList<double> splits, int gatesPerLap = 0)
    {
        var laps = new List<double>();
        if (gatesPerLap > 0)
        {
            double before = 0.0;
            for (int g = gatesPerLap; g <= splits.Count; g += gatesPerLap)
            {
                laps.Add(splits[g - 1] - before);
                before = splits[g - 1];
            }
        }

        int? best = laps.Count == 0 ? null : laps.IndexOf(laps.Min());
        int lap = best ?? _lastLap;
        var pieces = new PieceRun[_pieces];
        for (int k = 0; k < _pieces; k++)
        {
            pieces[k] = lap < _ticks.Count
                ? new PieceRun(_ticks[lap][k] / (double)_ticksPerSecond, _slowest[lap][k])
                : new PieceRun(0.0, float.NaN);
        }

        return new RunSummary
        {
            Finished = finished,
            Seconds = finished && splits.Count > 0 ? splits[^1] : null,
            Splits = splits.ToArray(),
            Laps = laps,
            BestLap = best,
            TopMps = _top,
            OffRoadSeconds = _offTicks.Sum() / (double)_ticksPerSecond,
            OffRoadByPiece = _offTicks.Select(n => n / (double)_ticksPerSecond).ToArray(),
            WorstSlideDeg = _worstSlide,
            Spins = _spins,
            Respawns = _respawns,
            FurthestM = _furthest,
            Pieces = pieces,
        };
    }
}
