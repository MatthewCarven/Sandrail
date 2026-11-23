using System.Numerics;

namespace Sandrail.Core.Driving;

public enum LapEventKind
{
    None,

    /// <summary>The clock started: a flying start across the line, or a lap begun again there.</summary>
    Started,

    /// <summary>The line was crossed at the end of a lap; the next lap began at the same instant.</summary>
    Finished,
}

/// <param name="Seconds">A finished lap's time.</param>
/// <param name="Counted">A finished lap went all the way round, in order, without leaving the lap.</param>
public readonly record struct LapEvent(LapEventKind Kind, double Seconds = 0.0, bool Counted = false);

/// <summary>
/// Times laps of a closed road: from crossing its start line forwards to crossing it again, having been all
/// the way round (his ask, 2026-11-23: "a lap timer for the main track").
/// </summary>
/// <remarks>
/// <para>
/// <b>Progress is measured along the road's middle</b>, every metre, so a car anywhere across the road is
/// as far round as anyone level with it. <b>A lap counts only if it passes through each quarter of the
/// lap in order</b> and never strays more than <see cref="OffLapM"/> from the middle: a jump of half a lap
/// (a cut, or the car put somewhere else) or a trip off across the sand makes it "not counted". Cutting
/// the inside of a corner isn't caught; it's a timer, not a steward.
/// </para>
/// <para>
/// <b>The moment of crossing is interpolated</b> between the two updates either side of the line, so a
/// time doesn't depend on where the ticks happened to fall.
/// </para>
/// <para>
/// <b>A standing start</b> (<see cref="StandingStart"/>, the car sitting on the line) starts the clock when
/// the driver asks to go (<see cref="Go"/>), not when the car first creeps: settling on the springs would
/// otherwise start it, and the lap oracle has always timed its standing lap from the first throttle.
/// </para>
/// </remarks>
public sealed class LapTimer
{
    /// <summary>Farther than this from the road's middle, metres, the car isn't on the lap at all.</summary>
    public const float OffLapM = 15.0f;

    private readonly RacingLine _road;
    private int _hint = -1;
    private bool _onLap;
    private float _progress;
    private double _time;
    private int _quarter;
    private int _reached;
    private bool _armed;
    private double _startedAt;

    /// <param name="road">The road's middle, sampled: a closed <see cref="RacingLine"/> built from its pieces.</param>
    public LapTimer(RacingLine road)
    {
        if (!road.Closed)
        {
            throw new ArgumentException("A lap timer needs a closed road.", nameof(road));
        }

        _road = road;
    }

    /// <summary>A lap is being timed.</summary>
    public bool Running { get; private set; }

    /// <summary>A standing start is waiting for <see cref="Go"/>.</summary>
    public bool Armed => _armed;

    /// <summary>The lap being timed has so far gone round in order and stayed on the lap.</summary>
    public bool Clean { get; private set; }

    /// <summary>How long the lap being timed has run, seconds; 0 when none is.</summary>
    public double Elapsed(double now) => Running ? now - _startedAt : 0.0;

    /// <summary>The car has been put on the line, standing: the clock waits for <see cref="Go"/>.</summary>
    public void StandingStart()
    {
        Running = false;
        _armed = true;
    }

    /// <summary>The car has been taken off the lap: no lap is running until it next crosses the line.</summary>
    public void Stop()
    {
        Running = false;
        _armed = false;
    }

    /// <summary>The driver asked to go: starts the clock if a standing start is waiting.</summary>
    public void Go(double time)
    {
        if (_armed)
        {
            _armed = false;
            Begin(time);
        }
    }

    /// <summary>Where the car is now, once a tick, with the time; says if the line was crossed.</summary>
    public LapEvent Update(Vector3 position, double time)
    {
        int i = _road.Nearest(position, _hint);
        if (Vector3.Distance(position, _road.PositionAt(i)) > OffLapM)
        {
            // Looking only near the last answer misses a car that has been moved: look everywhere.
            i = _road.Nearest(position);
        }

        _hint = i;
        if (Vector3.Distance(position, _road.PositionAt(i)) > OffLapM)
        {
            Clean = false;
            _onLap = false;
            return default;
        }

        float length = _road.Length;
        float progress = Wrap(_road.DistanceAt(i) + Vector3.Dot(position - _road.PositionAt(i), _road.ForwardAt(i)), length);
        int quarter = Math.Min((int)(4.0f * progress / length), 3);

        if (!_onLap)
        {
            // Just arrived, or the first update: take where it is, without reading a move into it.
            _onLap = true;
            Remember(progress, quarter, time);
            return default;
        }

        LapEvent result = default;
        int step = (quarter - _quarter + 4) % 4;
        if (step == 1 && quarter == 0)
        {
            // Across the line forwards. When exactly: as far between the two updates as the line is
            // between the two places.
            float behind = length - _progress;
            float past = progress;
            double crossed = _time + ((time - _time) * (behind / MathF.Max(behind + past, 1e-6f)));

            if (!_armed)
            {
                if (Running && (_reached == 3 || !Clean))
                {
                    result = new LapEvent(LapEventKind.Finished, crossed - _startedAt, Clean && _reached == 3);
                }
                else
                {
                    // A flying start; or back over the line and forward again, which begins the lap anew.
                    result = new LapEvent(LapEventKind.Started);
                }

                Begin(crossed);
            }
        }
        else if (step == 1)
        {
            if (Running && quarter == _reached + 1)
            {
                _reached = quarter;
            }
        }
        else if (step == 2)
        {
            // Half a lap in one update: cut, or moved.
            Clean = false;
        }

        Remember(progress, quarter, time);
        return result;
    }

    private void Begin(double time)
    {
        Running = true;
        Clean = true;
        _startedAt = time;
        _reached = 0;
    }

    private void Remember(float progress, int quarter, double time)
    {
        _progress = progress;
        _quarter = quarter;
        _time = time;
    }

    private static float Wrap(float distance, float length)
    {
        float d = distance % length;
        if (d < 0.0f)
        {
            d += length;
        }

        return d >= length ? 0.0f : d;
    }
}
