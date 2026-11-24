using System.Numerics;
using System.Text;
using Sandrail.Core.Cars;
using Sandrail.Core.Ghosts;

namespace Sandrail.Core.Tests;

/// <summary>M4b: the snapshot stream, the ghost file, playback and the recorder (M4-PLAN, M4b's checks).</summary>
public class GhostTests
{
    /// <summary>A car going round a 30 m circle at 20 m/s for ten seconds, wheels and pedals moving, put back once.</summary>
    private static Snapshot[] Circle(int count = 301, int cutAt = 150)
    {
        var snapshots = new Snapshot[count];
        for (int k = 0; k < count; k++)
        {
            float t = k / (float)Ghost.Rate;
            float angle = t * 20.0f / 30.0f;
            snapshots[k] = new Snapshot
            {
                Position = new Vector3(30.0f * MathF.Cos(angle), 2.0f + (0.1f * MathF.Sin(t)), 30.0f * MathF.Sin(angle)) + (k >= cutAt ? new Vector3(200.0f, 0.0f, 0.0f) : Vector3.Zero),
                Rotation = Quaternion.CreateFromYawPitchRoll(-angle, 0.05f * MathF.Sin(t), 0.1f),
                SteerDeg = 8.0f * MathF.Sin(t),
                Droop = new Vector4(0.1f, 0.12f, 0.2f + (0.05f * MathF.Sin(t)), 0.35f),
                Throttle = 0.5f + (0.5f * MathF.Sin(t)),
                Brake = MathF.Max(0.0f, -MathF.Sin(t)),
                Handbrake = k % 50 < 5 ? 1.0f : 0.0f,
                Cut = k == cutAt,
            };
        }

        return snapshots;
    }

    private static Ghost Make(Snapshot[]? snapshots = null) => new(
        new GhostHeader
        {
            Track = "0070ecd8",
            TrackId = "gen:5v1",
            TrackName = "Seed 5",
            Driver = GhostDriver.Autodriver,
            Seconds = 9.98,
            Splits = [3.5, 7.25, 9.98],
            When = new DateTimeOffset(2026, 11, 24, 18, 0, 0, TimeSpan.Zero),
        },
        new CarSpec(),
        snapshots ?? Circle());

    [Fact]
    public void ASnapshotQuantisesOnce()
    {
        foreach (Snapshot s in Circle())
        {
            Snapshot q = s.Quantised();
            Assert.Equal(q, q.Quantised());
            Assert.True(q.Rotation.W >= 0.0f);
            // Half a millimetre on each axis at most, so √3 of one along a diagonal.
            Assert.True(Vector3.Distance(s.Position, q.Position) <= 0.0009f, $"{Vector3.Distance(s.Position, q.Position)}");
        }
    }

    [Fact]
    public void AGhostSavedAndLoadedIsTheSameGhost()
    {
        Ghost ghost = Make();
        byte[] file = ghost.Save();
        Ghost back = Ghost.Load(file);

        Assert.Equal(ghost.Snapshots, back.Snapshots);
        Assert.Equal(file, back.Save());
        Assert.Equal("0070ecd8", back.Header.Track);
        Assert.Equal("gen:5v1", back.Header.TrackId);
        Assert.Equal(GhostDriver.Autodriver, back.Header.Driver);
        Assert.Equal([3.5, 7.25, 9.98], back.Header.Splits);
        Assert.Equal(ghost.Header.When, back.Header.When);
        Assert.Equal(new CarSpec().Fingerprint(), back.Header.Car);
        Assert.Equal(new CarSpec().Fingerprint(), back.Spec.Fingerprint());
        Assert.Equal(301, back.Header.Snapshots);
    }

    /// <summary>The header is a line of JSON a person can read, naming the track and the car.</summary>
    [Fact]
    public void TheHeaderIsReadableByEye()
    {
        byte[] file = Make().Save();
        string header = Encoding.UTF8.GetString(file, 0, Array.IndexOf(file, (byte)'\n'));
        Assert.StartsWith("{\"format\":1,\"kind\":\"sandrail-ghost\",\"track\":\"0070ecd8\"", header, StringComparison.Ordinal);
        Assert.Contains("\"driver\":\"autodriver\"", header, StringComparison.Ordinal);
        Assert.Contains("\"spec\":{", header, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackLandsOnEverySnapshotExactly()
    {
        Ghost ghost = Make();
        for (int k = 0; k < ghost.Snapshots.Count; k++)
        {
            // As the game asks: ticks of 120 a second.
            Assert.Equal(ghost.Snapshots[k], ghost.At(k * 4 / 120.0));
        }
    }

    [Fact]
    public void BetweenSnapshotsPlaybackIsOnTheStraightLine()
    {
        Ghost ghost = Make();
        for (int k = 0; k < 140; k += 7)
        {
            Snapshot a = ghost.Snapshots[k];
            Snapshot b = ghost.Snapshots[k + 1];
            Snapshot mid = ghost.At((k + 0.5) / Ghost.Rate);
            Assert.True(Vector3.Distance((a.Position + b.Position) / 2.0f, mid.Position) < 1e-4f);
            Assert.Equal((a.SteerDeg + b.SteerDeg) / 2.0f, mid.SteerDeg, 4);
            Assert.Equal(1.0f, mid.Rotation.Length(), 4);
        }
    }

    [Fact]
    public void ACutIsNeverInterpolatedAcross()
    {
        Ghost ghost = Make();
        Snapshot before = ghost.Snapshots[149];
        Assert.True(ghost.Snapshots[150].Cut);
        Assert.Equal(before, ghost.At(149.5 / Ghost.Rate));
        Assert.Equal(before, ghost.At(149.99 / Ghost.Rate));
        Assert.Equal(ghost.Snapshots[150], ghost.At(150.0 / Ghost.Rate));
        Assert.Equal(Vector3.Zero, ghost.VelocityAt(149.5 / Ghost.Rate));
        Assert.InRange(ghost.VelocityAt(10.0 / Ghost.Rate).Length(), 19.5f, 20.5f);
    }

    [Fact]
    public void BeforeTheStartAndAfterTheEndItHolds()
    {
        Ghost ghost = Make();
        Assert.Equal(ghost.Snapshots[0], ghost.At(-3.0));
        Assert.Equal(ghost.Snapshots[^1], ghost.At(ghost.Duration + 5.0));
        Assert.Equal(10.0, ghost.Duration, 6);
    }

    [Fact]
    public void ANewerFormatIsRefusedByName()
    {
        byte[] file = Make().Save();
        byte[] newer = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(file).Replace("{\"format\":1,", "{\"format\":2,", StringComparison.Ordinal));
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Ghost.Load(newer));
        Assert.Contains("newer Sandrail", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatIsntAGhostIsRefusedInWords()
    {
        Assert.Throws<InvalidDataException>(() => Ghost.Load(Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"base:shakedown\"}\n")));
        Assert.Throws<InvalidDataException>(() => Ghost.Load(Encoding.UTF8.GetBytes("no header at all")));

        // Its snapshots cut off part way.
        byte[] file = Make().Save();
        Assert.Throws<InvalidDataException>(() => Ghost.Load(file[..^40]));
    }

    /// <summary>A snapshot every 4th tick from the clock's zero, until the first at or after the finish.</summary>
    [Fact]
    public void TheRecorderTakesEveryFourthTickUntilPastTheFinish()
    {
        var recorder = new GhostRecorder(120);
        recorder.Tick(new Snapshot { Position = new Vector3(-1.0f, 0.0f, 0.0f) }, false);
        Assert.Empty(recorder.Snapshots);

        recorder.Go();
        for (int tick = 0; tick < 30; tick++)
        {
            recorder.Tick(new Snapshot { Position = new Vector3(tick, 0.0f, 0.0f) }, finished: tick >= 10);
        }

        Assert.True(recorder.Done);
        Assert.Equal([0.0f, 4.0f, 8.0f, 12.0f], recorder.Snapshots.Select(s => s.Position.X));
    }

    [Fact]
    public void TheRecorderMarksTheSnapshotAfterACarPutBack()
    {
        var recorder = new GhostRecorder(120);
        recorder.Cut();
        recorder.Go();
        for (int tick = 0; tick < 20; tick++)
        {
            if (tick == 5)
            {
                recorder.Cut();
            }

            recorder.Tick(new Snapshot { Position = new Vector3(tick, 0.0f, 0.0f) }, false);
        }

        Assert.Equal([false, false, true, false, false], recorder.Snapshots.Select(s => s.Cut));

        recorder.Stop();
        Assert.False(recorder.Recording);
        Assert.Empty(recorder.Snapshots);
    }

    [Fact]
    public void TheRecorderNeedsAWholeNumberOfTicksASnapshot()
    {
        Assert.Throws<ArgumentException>(() => new GhostRecorder(100));
        Assert.Throws<ArgumentException>(() => new GhostRecorder(20));
        _ = new GhostRecorder(60);
    }
}
