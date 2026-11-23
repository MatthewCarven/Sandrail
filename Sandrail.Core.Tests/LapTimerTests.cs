using System.Numerics;
using Sandrail.Core.Driving;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tests;

public class LapTimerTests
{
    private const double Dt = 1.0 / 120.0;

    // A 30 m circle: at a steady speed every lap time is known exactly.
    private static readonly TurnCentreline Circle = new(Frame.Level(Vector3.Zero, 0.0f), 360.0f, 30.0f);
    private static readonly RacingLine Road = new([Circle], closed: true);

    /// <summary>Where a car's middle is when it's this far round, sitting on the road.</summary>
    private static Vector3 At(float distance)
    {
        float s = distance % Circle.Length;
        if (s < 0.0f)
        {
            s += Circle.Length;
        }

        Frame f = Circle.FrameAt(s);
        return f.Position + (f.Up * 0.6f);
    }

    /// <summary>Drives round at a steady speed from a distance, updating every tick, and keeps what the timer says.</summary>
    private static List<LapEvent> Drive(LapTimer timer, float from, float speed, double seconds, double startTime = 0.0)
    {
        var events = new List<LapEvent>();
        for (int k = 0; k * Dt <= seconds; k++)
        {
            LapEvent e = timer.Update(At(from + (speed * (float)(k * Dt))), startTime + (k * Dt));
            if (e.Kind != LapEventKind.None)
            {
                events.Add(e);
            }
        }

        return events;
    }

    private static void AssertLapTime(double expected, LapEvent lap) =>
        Assert.True(Math.Abs(lap.Seconds - expected) < 1e-3, $"{lap.Seconds:F4} s against {expected:F4}");

    [Fact]
    public void AFlyingLapIsTimedFromLineToLineToTheMillisecond()
    {
        // 20 m short of the line at 20 m/s, then round twice: the crossings fall between ticks, and the
        // interpolation finds them anyway.
        var timer = new LapTimer(Road);
        List<LapEvent> events = Drive(timer, -20.0f, 20.0f, 21.0);

        Assert.Equal(LapEventKind.Started, events[0].Kind);
        Assert.Equal(LapEventKind.Finished, events[1].Kind);
        Assert.Equal(LapEventKind.Finished, events[2].Kind);
        Assert.True(events[1].Counted && events[2].Counted);
        AssertLapTime(Circle.Length / 20.0, events[1]);
        AssertLapTime(Circle.Length / 20.0, events[2]);
    }

    [Fact]
    public void AStandingStartWaitsForGo()
    {
        var timer = new LapTimer(Road);
        timer.StandingStart();

        // A second sitting on the line, settling on the springs: no clock.
        Assert.Empty(Drive(timer, 0.0f, 0.0f, 1.0));
        Assert.True(timer.Armed);
        Assert.False(timer.Running);

        timer.Go(1.0);
        List<LapEvent> events = Drive(timer, 0.0f, 20.0f, 10.0, startTime: 1.0);

        LapEvent lap = events.Single(e => e.Kind == LapEventKind.Finished);
        Assert.True(lap.Counted);
        AssertLapTime(Circle.Length / 20.0, lap);
    }

    [Fact]
    public void HalfALapInOneGoIsTimedButNotCounted()
    {
        var timer = new LapTimer(Road);
        Drive(timer, -20.0f, 20.0f, 2.0);

        // From 20 m round to half a lap further on, as a cut or a teleport would.
        List<LapEvent> events = Drive(timer, 20.0f + (Circle.Length / 2.0f), 20.0f, 8.0, startTime: 2.0 + Dt);

        LapEvent lap = events.First(e => e.Kind == LapEventKind.Finished);
        Assert.False(lap.Counted);
    }

    [Fact]
    public void ALapThatLeavesTheRoadIsNotCounted()
    {
        var timer = new LapTimer(Road);
        Drive(timer, -20.0f, 20.0f, 2.0);
        timer.Update(new Vector3(0.0f, 0.0f, 200.0f), 2.0 + Dt);

        List<LapEvent> events = Drive(timer, 21.0f, 20.0f, 10.0, startTime: 2.0 + (2.0 * Dt));

        Assert.False(events.First(e => e.Kind == LapEventKind.Finished).Counted);
    }

    [Fact]
    public void BackOverTheLineAndForwardAgainBeginsTheLapAgain()
    {
        var timer = new LapTimer(Road);
        Drive(timer, -20.0f, 20.0f, 2.0);

        // Reversing back over the line finishes nothing; crossing it forwards again restarts the clock.
        Assert.Empty(Drive(timer, 20.0f, -10.0f, 4.0, startTime: 2.0 + Dt));
        List<LapEvent> again = Drive(timer, -20.0f, 10.0f, 3.0, startTime: 6.0 + (2.0 * Dt));

        Assert.Equal(LapEventKind.Started, Assert.Single(again).Kind);
        Assert.True(timer.Running && timer.Clean);
    }

    [Fact]
    public void StoppedItTimesNothingUntilTheLineIsCrossed()
    {
        var timer = new LapTimer(Road);
        Drive(timer, -20.0f, 20.0f, 2.0);
        timer.Stop();

        Assert.False(timer.Running);
        Assert.Equal(0.0, timer.Elapsed(3.0));
        Assert.Equal(LapEventKind.Started, Drive(timer, 21.0f, 20.0f, 9.0, startTime: 2.0 + Dt)[0].Kind);
    }
}
