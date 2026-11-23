using System.Numerics;
using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>M2d: the race timer, the route's gates and its racing line (M2-PLAN, M2d's checks).</summary>
public class RaceTimerTests
{
    private const float Step = 0.01f;

    /// <summary>A start at the origin and gates every 100 m north (-Z), the finish at 300 m: 10 m wide each way.</summary>
    private static RaceTimer Straight()
    {
        RaceGate Gate(GateKind kind, float north) => new(kind, Frame.Level(new Vector3(0.0f, 0.0f, -north), 0.0f), 5.4f);
        return new RaceTimer([Gate(GateKind.Start, 0.0f), Gate(GateKind.Checkpoint, 100.0f), Gate(GateKind.Checkpoint, 200.0f), Gate(GateKind.Finish, 300.0f)]);
    }

    /// <summary>Drives from one point to another a metre a tick, from a time, and returns the events and the time at the end.</summary>
    private static (List<RaceEvent> Events, double Time) Drive(RaceTimer timer, Vector3 from, Vector3 to, double time)
    {
        var events = new List<RaceEvent>();
        int steps = (int)MathF.Ceiling(Vector3.Distance(from, to));
        for (int k = 0; k <= steps; k++)
        {
            RaceEvent e = timer.Update(Vector3.Lerp(from, to, k / (float)steps), time);
            if (e.Kind != RaceEventKind.None)
            {
                events.Add(e);
            }

            time += Step;
        }

        return (events, time - Step);
    }

    private static Vector3 North(float metres, float east = 0.0f) => new(east, 0.5f, -metres);

    [Fact]
    public void TheClockWaitsForTheFirstThrottle()
    {
        RaceTimer timer = Straight();
        timer.Arm();
        (List<RaceEvent> events, _) = Drive(timer, North(-5.0f), North(150.0f), 0.0);
        Assert.Empty(events);
        Assert.True(timer.Armed);
        Assert.Equal(0, timer.Passed);
        Assert.Equal(0.0, timer.Elapsed(10.0));
    }

    [Fact]
    public void ARunThroughEveryGateCounts()
    {
        RaceTimer timer = Straight();
        timer.Arm();
        Assert.Equal(RaceEventKind.Started, timer.Go(0.0).Kind);
        (List<RaceEvent> events, _) = Drive(timer, North(-5.0f), North(305.0f), 0.0);

        Assert.Equal([RaceEventKind.Checkpoint, RaceEventKind.Checkpoint, RaceEventKind.Finished], events.Select(e => e.Kind));
        Assert.Equal([1, 2], events.Take(2).Select(e => e.Gate));

        // A metre a hundredth of a second from 5 m short of the start: each gate at its distance, exactly.
        Assert.Equal(1.05, events[0].Seconds, 6);
        Assert.Equal(2.05, events[1].Seconds, 6);
        Assert.Equal(3.05, events[2].Seconds, 6);
        Assert.True(timer.Finished);
        Assert.False(timer.Running);
        Assert.Equal(3.05, timer.Elapsed(99.0), 6);
        Assert.Equal([1.05, 2.05, 3.05], timer.Splits.Select(s => Math.Round(s, 6)));
    }

    /// <summary>Between ticks the crossing is interpolated: a gate halfway between two updates is crossed halfway between their times.</summary>
    [Fact]
    public void ACrossingBetweenTicksIsInterpolated()
    {
        RaceTimer timer = Straight();
        timer.Arm();
        timer.Go(0.0);
        timer.Update(North(99.0f), 1.0);
        RaceEvent e = timer.Update(North(103.0f), 1.2);
        Assert.Equal(RaceEventKind.Checkpoint, e.Kind);
        Assert.Equal(1.05, e.Seconds, 6);
    }

    [Fact]
    public void ASkippedCheckpointHoldsTheRunUpUntilItsGoneBackFor()
    {
        RaceTimer timer = Straight();
        timer.Arm();
        timer.Go(0.0);

        // Round checkpoint 1 on the sand, 20 m to its side, and on through checkpoint 2.
        (List<RaceEvent> events, double time) = Drive(timer, North(50.0f, 20.0f), North(250.0f, 20.0f), 0.0);
        Assert.Empty(events);
        (events, time) = Drive(timer, North(150.0f), North(250.0f), time);
        Assert.Equal([new RaceEvent(RaceEventKind.OutOfOrder, 0.0, 1)], events);
        Assert.Equal(0, timer.Passed);

        // On to the finish: still out of order, and the run goes on.
        (events, time) = Drive(timer, North(250.0f), North(310.0f), time);
        Assert.Equal([new RaceEvent(RaceEventKind.OutOfOrder, 0.0, 1)], events);
        Assert.True(timer.Running);

        // Back for it, and through it all again.
        timer.Moved();
        (events, _) = Drive(timer, North(90.0f), North(310.0f), time);
        Assert.Equal([RaceEventKind.Checkpoint, RaceEventKind.Checkpoint, RaceEventKind.Finished], events.Select(e => e.Kind));
        Assert.True(timer.Finished);
    }

    [Fact]
    public void CrossingAGateBackwardsDoesntPassIt()
    {
        RaceTimer timer = Straight();
        timer.Arm();
        timer.Go(0.0);
        (List<RaceEvent> events, double time) = Drive(timer, North(110.0f), North(90.0f), 0.0);
        Assert.Empty(events);
        Assert.Equal(0, timer.Passed);

        // And turning round again, it's passed going forwards.
        (events, _) = Drive(timer, North(90.0f), North(110.0f), time);
        Assert.Equal(RaceEventKind.Checkpoint, Assert.Single(events).Kind);
    }

    [Fact]
    public void PassingBesideTheArchIsntPassingThroughIt()
    {
        RaceTimer timer = Straight();
        timer.Arm();
        timer.Go(0.0);
        Assert.Empty(Drive(timer, North(90.0f, 6.0f), North(110.0f, 6.0f), 0.0).Events);
        Assert.Empty(Drive(timer, North(90.0f, -6.0f), North(110.0f, -6.0f), 1.0).Events);
        Assert.Single(Drive(timer, North(90.0f, 5.0f), North(110.0f, 5.0f), 2.0).Events);
    }

    [Fact]
    public void FlyingOverTheArchIsntPassingThroughIt()
    {
        RaceTimer timer = Straight();
        timer.Arm();
        timer.Go(0.0);
        Assert.Empty(Drive(timer, new Vector3(0.0f, 8.0f, -95.0f), new Vector3(0.0f, 8.0f, -105.0f), 0.0).Events);
    }

    /// <summary>A respawn moves the car; the move isn't a drive, even across a gate.</summary>
    [Fact]
    public void BeingPutPastAGateIsntPassingIt()
    {
        RaceTimer timer = Straight();
        timer.Arm();
        timer.Go(0.0);
        timer.Update(North(50.0f), 1.0);
        timer.Moved();
        Assert.Equal(RaceEventKind.None, timer.Update(North(150.0f), 1.01).Kind);
        Assert.Equal(0, timer.Passed);
    }

    [Fact]
    public void TheLastGatePassedIsWhereARespawnGoes()
    {
        RaceTimer timer = Straight();
        timer.Arm();
        Assert.Equal(GateKind.Start, timer.LastGate.Kind);
        timer.Go(0.0);
        Drive(timer, North(-5.0f), North(150.0f), 0.0);
        Assert.Equal(-100.0f, timer.LastGate.Frame.Position.Z, 3);
    }

    [Fact]
    public void ARaceNeedsAStartAndAFinish()
    {
        RaceGate g = new(GateKind.Checkpoint, Frame.Level(Vector3.Zero, 0.0f), 5.0f);
        Assert.Throws<ArgumentException>(() => new RaceTimer([g, g]));
        Assert.Throws<ArgumentException>(() => new RaceTimer([g with { Kind = GateKind.Start }, g, g with { Kind = GateKind.Start }, g with { Kind = GateKind.Finish }]));
    }

    // The shakedown's route, raced.

    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static readonly Lazy<LaidTrack> Shakedown = new(() => Track.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "tracks", "shakedown.json")), Catalogue.Value));

    private static CarSpec Buggy() => CarSpec.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "cars", "buggy.json")));

    [Fact]
    public void TheShakedownsGatesAreItsStartTwoCheckpointsAndFinish()
    {
        IReadOnlyList<RaceGate> gates = Shakedown.Value.Gates();
        Assert.Equal([GateKind.Start, GateKind.Checkpoint, GateKind.Checkpoint, GateKind.Finish], gates.Select(g => g.Kind));
        _ = new RaceTimer(gates);
    }

    /// <summary>
    /// The route's racing line keeps to the middle of every stunt, holds each stunt's hint, and holds the
    /// wheels straight only in the loop itself, not its S-bends.
    /// </summary>
    [Fact]
    public void TheRouteLineTakesStuntsDownTheMiddleAtTheirHints()
    {
        LaidTrack track = Shakedown.Value;
        (IReadOnlyList<ICentreline> road, IReadOnlyList<int> owner) = track.RouteRoad();
        RacingLine line = track.RouteLine(Buggy());
        float[] speeds = line.SpeedProfile(Buggy(), 0.8f);

        bool usedRoom = false;
        for (int i = 0; i < line.Count; i++)
        {
            PieceDef def = track.Route[owner[line.PieceAt(i)]].Def;
            bool stunt = def.SpeedKmh is not null || def.In == ConnectorKind.Landing;
            if (stunt)
            {
                Assert.Equal(0.0f, line.OffsetAt(i));
            }
            else
            {
                usedRoom |= MathF.Abs(line.OffsetAt(i)) > 1.0f;
            }

            if (def.SpeedKmh is float[] hint)
            {
                Assert.Equal((hint[0] + hint[1]) / 2.0f / 3.6f, line.HintAt(i), 4);
                Assert.True(speeds[i] <= line.HintAt(i) + 1e-4f);
            }
            else
            {
                Assert.True(float.IsNaN(line.HintAt(i)));
            }

            Assert.Equal(road[line.PieceAt(i)] is LoopCentreline, line.StraightAt(i));
        }

        Assert.True(usedRoom, "The line should use the road's width in the corners.");
        Assert.Contains(Enumerable.Range(0, line.Count), i => line.StraightAt(i));
    }

    [Fact]
    public void ALineWithoutStuntsPlansAsBefore()
    {
        LaidTrack track = Shakedown.Value;
        (IReadOnlyList<ICentreline> road, _) = track.RouteRoad();
        var plain = new RacingLine(road, closed: false);
        RacingLine marked = plain.WithStunts(_ => float.NaN, _ => false);
        Assert.Equal(plain.SpeedProfile(Buggy(), 0.8f), marked.SpeedProfile(Buggy(), 0.8f));
    }

    [Fact]
    public void TheAutodriverKeepsItsHandsOffInTheAir()
    {
        RacingLine line = Shakedown.Value.RouteLine(Buggy());
        var driver = new Autodriver(line, Buggy());
        Vector3 at = line.PositionAt(200);
        Vector3 forward = line.ForwardAt(200);
        DriveCommand cmd = driver.Drive(at + new Vector3(0.0f, 3.0f, 0.0f), forward, Vector3.UnitY, forward * 5.0f, 0.0f, airborne: true);
        Assert.Equal(default, cmd);
        Assert.Equal(200, driver.Index, 3.0);
    }

    [Fact]
    public void TheAutodriverStopsAtTheEndOfAnOpenLine()
    {
        RacingLine line = Shakedown.Value.RouteLine(Buggy());
        var driver = new Autodriver(line, Buggy());
        int last = line.Count - 1;
        Vector3 forward = line.ForwardAt(last);
        DriveCommand cmd = driver.Drive(line.PositionAt(last) + (forward * 5.0f), forward, Vector3.UnitY, forward * 20.0f, 0.0f);
        Assert.True(driver.AtTheEnd);
        Assert.Equal(0.0f, cmd.Throttle);
        Assert.True(cmd.Brake > 0.5f);
    }

    [Fact]
    public void TheAutodriversWheelsAreStraightInTheLoop()
    {
        RacingLine line = Shakedown.Value.RouteLine(Buggy());
        int inLoop = Enumerable.Range(0, line.Count).First(line.StraightAt) + 20;
        var driver = new Autodriver(line, Buggy());

        // Pointing well off the line, which anywhere else would be steered hard against.
        Vector3 forward = Vector3.Normalize(line.ForwardAt(inLoop) + (line.UpAt(inLoop) * 0.0f) + new Vector3(0.3f, 0.0f, 0.0f));
        DriveCommand cmd = driver.Drive(line.PositionAt(inLoop), forward, line.UpAt(inLoop), forward * 25.0f, 0.0f);
        Assert.Equal(0.0f, cmd.Steer);
    }
}
