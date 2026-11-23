using System.Numerics;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tracks;

/// <summary>
/// A gate across the road, where its arch stands: its frame faces the way through, and a car passes it
/// by crossing that plane forwards between its posts.
/// </summary>
public sealed record RaceGate(GateKind Kind, Frame Frame, float HalfWidthM)
{
    /// <summary>How high above the road a crossing still counts: the arch's beam is 5 m up.</summary>
    public const float HeightM = 6.0f;

    /// <summary>How far below it: the road dips nowhere near a gate, so this only forgives a car on its springs.</summary>
    public const float BelowM = 2.0f;

    /// <summary>
    /// Where a car crossing from one point to the next passes through the gate, as a share of the way
    /// between them, or null if it doesn't: going backwards, beside the posts, or over the top.
    /// </summary>
    public float? Through(Vector3 from, Vector3 to)
    {
        float before = Vector3.Dot(from - Frame.Position, Frame.Forward);
        float after = Vector3.Dot(to - Frame.Position, Frame.Forward);
        if (!(before < 0.0f && after >= 0.0f))
        {
            return null;
        }

        float t = before / (before - after);
        Vector3 at = from + ((to - from) * t) - Frame.Position;
        float across = Vector3.Dot(at, Frame.Right);
        float up = Vector3.Dot(at, Frame.Up);
        return MathF.Abs(across) <= HalfWidthM && up >= -BelowM && up <= HeightM ? t : null;
    }
}

public enum RaceEventKind
{
    None,

    /// <summary>The clock started: the first throttle at the start gate.</summary>
    Started,

    /// <summary>A checkpoint passed in order; <see cref="RaceEvent.Gate"/> says which, from 1.</summary>
    Checkpoint,

    /// <summary>The finish crossed with every checkpoint passed: the run is over and counts.</summary>
    Finished,

    /// <summary>
    /// A later gate crossed before the next one: <see cref="RaceEvent.Gate"/> is the one still to pass
    /// (the finish is the checkpoints' count plus one). Nothing is passed; the run goes on.
    /// </summary>
    OutOfOrder,
}

/// <param name="Seconds">For a checkpoint, its split; for the finish, the run's time; both from the start.</param>
/// <param name="Gate">Which checkpoint, from 1; for out of order, the one still to pass.</param>
public readonly record struct RaceEvent(RaceEventKind Kind, double Seconds = 0.0, int Gate = 0);

/// <summary>
/// Times a run of a point-to-point track (M2-PLAN, M2d, decisions 3 and 4): from the first throttle at the
/// start gate, through each checkpoint in order with a split at each, to the finish.
/// </summary>
/// <remarks>
/// <para>
/// <b>A gate is passed by driving through its arch forwards</b>, not by getting level with it, because
/// the ground beside the road is drivable (M2-PLAN fact 5) and the checkpoints are what make a shortcut
/// not pay. <b>Gates are passed in order</b>: one crossed early is said (<see cref="RaceEventKind.OutOfOrder"/>)
/// and not counted, so a skipped checkpoint holds the run up until it's gone back for, and the finish
/// only counts once every checkpoint is behind it. A gate crossed backwards is nothing at all.
/// </para>
/// <para>
/// <b>The clock never stops for a respawn</b> (decision 3: the retry is instant and the cost is the time).
/// <see cref="Moved"/> tells it the car was put somewhere, so the move isn't read as a drive through a
/// gate. The moment a gate is crossed is interpolated between the updates either side of it, as the lap
/// timer's line is.
/// </para>
/// </remarks>
public sealed class RaceTimer
{
    private readonly IReadOnlyList<RaceGate> _gates;
    private readonly List<double> _splits = new();
    private Vector3? _last;
    private double _lastTime;
    private double _startedAt;
    private double _time;

    /// <param name="gates">The start, the checkpoints in the order they're driven, and the finish.</param>
    public RaceTimer(IReadOnlyList<RaceGate> gates)
    {
        if (gates.Count < 2 || gates[0].Kind != GateKind.Start || gates[^1].Kind != GateKind.Finish
            || gates.Skip(1).SkipLast(1).Any(g => g.Kind != GateKind.Checkpoint))
        {
            throw new ArgumentException("A race needs a start, its checkpoints in order and a finish.", nameof(gates));
        }

        _gates = gates;
    }

    public IReadOnlyList<RaceGate> Gates => _gates;

    public int Checkpoints => _gates.Count - 2;

    /// <summary>The car is at the start and the clock waits for <see cref="Go"/>.</summary>
    public bool Armed { get; private set; }

    /// <summary>A run is being timed.</summary>
    public bool Running { get; private set; }

    /// <summary>The last run reached the finish with every checkpoint passed.</summary>
    public bool Finished { get; private set; }

    /// <summary>How many gates since the start this run has passed: the checkpoints so far, and then the finish.</summary>
    public int Passed { get; private set; }

    /// <summary>The last gate passed, the start before any: where a respawn puts the car.</summary>
    public RaceGate LastGate => _gates[Math.Min(Passed, _gates.Count - 1)];

    /// <summary>The split at each checkpoint passed this run, and the run's time once it's finished.</summary>
    public IReadOnlyList<double> Splits => _splits;

    /// <summary>The run's time so far, seconds; its final time once finished; 0 before it starts.</summary>
    public double Elapsed(double now) => Running ? now - _startedAt : Finished ? _time : 0.0;

    /// <summary>The car has been put at the start: a new run, waiting for the first throttle.</summary>
    public void Arm()
    {
        Armed = true;
        Running = false;
        Finished = false;
        Passed = 0;
        _splits.Clear();
        _last = null;
    }

    /// <summary>The driver asked to go: starts the clock if the car is waiting at the start.</summary>
    public RaceEvent Go(double time)
    {
        if (!Armed)
        {
            return default;
        }

        Armed = false;
        Running = true;
        _startedAt = time;
        return new RaceEvent(RaceEventKind.Started);
    }

    /// <summary>The car was put somewhere: the next update reads no gate between where it was and where it is.</summary>
    public void Moved() => _last = null;

    /// <summary>Where the car is now, once a tick, with the time; says if a gate was passed.</summary>
    public RaceEvent Update(Vector3 position, double time)
    {
        Vector3? last = _last;
        double lastTime = _lastTime;
        _last = position;
        _lastTime = time;
        if (!Running || last is not Vector3 from)
        {
            return default;
        }

        if (_gates[Passed + 1].Through(from, position) is float t)
        {
            double at = lastTime + ((time - lastTime) * t) - _startedAt;
            Passed++;
            _splits.Add(at);
            if (Passed < _gates.Count - 1)
            {
                return new RaceEvent(RaceEventKind.Checkpoint, at, Passed);
            }

            Running = false;
            Finished = true;
            _time = at;
            return new RaceEvent(RaceEventKind.Finished, at);
        }

        for (int g = Passed + 2; g < _gates.Count; g++)
        {
            if (_gates[g].Through(from, position) is not null)
            {
                return new RaceEvent(RaceEventKind.OutOfOrder, 0.0, Passed + 1);
            }
        }

        return default;
    }
}
