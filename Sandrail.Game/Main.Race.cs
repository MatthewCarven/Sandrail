using System.Collections.Generic;
using System.Linq;
using Godot;
using Sandrail.Core.Driving;
using Sandrail.Core.Ghosts;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;
using Sandrail.Game.Cars;
using Sandrail.Game.World;

namespace Sandrail.Game;

/// <summary>
/// Racing a track (M2-PLAN, M2d): the clock from the first throttle at the start gate, the checkpoints in
/// order with a split at each against the best, the finish, and the car put back at the last checkpoint.
/// </summary>
/// <remarks>
/// <para>
/// <b>R and the auto-respawn put the car back at the last gate passed</b>, stopped and facing along the
/// road, with the clock running (decision 3): the retry is instant and the cost is the time. Before the
/// first checkpoint, or once it's finished, that's the start, and the run starts again: the clock at zero,
/// waiting for the throttle.
/// <b>Backspace starts the run again</b> from the start gate, clock and all, and so does <b>R held for a
/// second</b>, for a pad with no button to spare.
/// </para>
/// <para>
/// <b>Bests are kept per track for the session</b>, his and the autodriver's apart, as the lap's are; a run
/// the autodriver had the car for at any point is its run. Keeping them between sessions waits for M4,
/// whose ghosts are a saved best run anyway.
/// </para>
/// </remarks>
public partial class Main
{
    /// <summary>How far past a checkpoint's arch the car is put back: clear of it, so it isn't passed again.</summary>
    private const float PastTheArchM = 4.0f;

    private RaceTimer? _race;
    private RacingLine? _routeLine;
    private int _raceArmIn;
    private bool _autoThisRun;
    private RaceRun? _bestRunYou;
    private RaceRun? _bestRunAuto;
    private RaceRun? _lastRun;

    // A circuit's laps (M3g): the last one's time this run, and the best lap of the session, his and the autodriver's.
    private double? _lastLapTime;
    private double? _bestLapYou;
    private double? _bestLapAuto;

    /// <param name="Car">The fingerprint of the car it was driven on (M4c, decision 6: a best counts whatever the car, and the board says when it's another).</param>
    private sealed record RaceRun(double Seconds, IReadOnlyList<double> Splits, bool Auto, string Car);

    // The run as a ghost (M4b): recorded from the clock's start, every 4th tick, and the last finished run kept.
    private GhostRecorder? _ghostRecorder;
    private int _teleportsSeen;
    private Ghost? _lastGhost;

    /// <summary>Whether the track being raced is a circuit, run in laps.</summary>
    private bool OnACircuit => _track?.Track.Circuit == true;

    /// <summary>The line the autodriver drives and L shows: the track's route, or the test ground's corner lap.</summary>
    private RacingLine? DrivenLine => _routeLine ?? _lapLine;

    /// <summary>The track's timer and its racing line, once it's built.</summary>
    private void SetUpRace(LaidTrack track, Sandrail.Core.Cars.CarSpec spec)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _race = new RaceTimer(track.Gates());
        _ghostRecorder = new GhostRecorder(Engine.PhysicsTicksPerSecond);
        _routeLine = track.RouteLine(spec);
        DrawLine(spec);
        GD.Print($"Race: {_race.Checkpoints} checkpoints; the racing line ({_routeLine.Count} samples) in {clock.Elapsed.TotalMilliseconds:F0} ms");
        ArmRace();
    }

    /// <summary>A new run from the start: the clock waits two ticks, until the car has really been moved there, then for the throttle.</summary>
    private void ArmRace()
    {
        _raceArmIn = 2;
        _lastRun = null;
        _lastLapTime = null;
        _ghostRecorder?.Stop();

        // The ghost back on the line, and a best filed since the last run racing from now (M4c).
        _raceGoTick = -1;
        _saveGhostAs = null;
        _ghostCar?.Play(ChosenGhost);
    }

    /// <summary>Where the car is put back on a track: the last gate passed while a run is on, the start otherwise.</summary>
    private Transform3D TrackHome()
    {
        // An untimed test drive (M5c) goes back to where it began.
        if (_testDriveFrom is not null)
        {
            return _spawn;
        }

        if (_track is null || _race is null || !_race.Running || _race.Passed == 0)
        {
            return SpawnAt(TrackStart(_track!), _car!.Spec);
        }

        Frame gate = _race.LastGate.Frame;
        return SpawnAt(gate with { Position = gate.Position + (gate.Forward * PastTheArchM) }, _car!.Spec);
    }

    /// <summary>
    /// The car has been put back on a track. During a run past its first checkpoint, the clock runs on;
    /// otherwise it's back at the start, and the run starts again with the clock at zero.
    /// </summary>
    /// <remarks>
    /// Before the first checkpoint the start used to count as one, the clock running on (M2d). On generated
    /// tracks, which have no checkpoints until M3c, that made every R a restart with the time so far added,
    /// so at his word the start is a fresh run (2026-11-24).
    /// </remarks>
    private string AfterTrackRespawn()
    {
        if (_race is { Running: true, Passed: > 0 })
        {
            _race.Moved();
            return $"checkpoint {_race.Passed}";
        }

        if (_race is not null)
        {
            ArmRace();
        }

        return "the start: the clock starts on the throttle";
    }

    /// <summary>R on a track: back to the last checkpoint at once, and if it's held, the run starts again.</summary>
    private void RespawnOnTrack()
    {
        // Only a trip to a checkpoint has anything to hold for: a trip to the start has already started again.
        bool toCheckpoint = _race is { Running: true, Passed: > 0 };
        _car!.Respawn(TrackHome());
        _autoRespawn?.Reset();
        string where = AfterTrackRespawn();
        _respawnHeldTicks = toCheckpoint ? 0 : -1;
        _hud?.Flash($"Back to {where}.{(toCheckpoint ? " Hold to start again." : string.Empty)}", 2.0);
    }

    /// <summary>
    /// Once a tick on a track: R held this long since it was pressed starts the run again, as Backspace does,
    /// so a pad without a spare button can start again too (his ask, 2026-11-24, to learn seed 5 in whole
    /// runs). The tap's trip back to the checkpoint has already happened, so a tap costs nothing.
    /// </summary>
    private void WatchRespawnHeld()
    {
        if (!Input.IsActionPressed(Controls.Respawn))
        {
            _respawnHeldTicks = -1;
            return;
        }

        // Counting only from a press seen on a track, and only once a hold: a held R doesn't restart twice.
        if (_respawnHeldTicks < 0)
        {
            return;
        }

        if (++_respawnHeldTicks >= HoldToRestartSeconds * Engine.PhysicsTicksPerSecond)
        {
            _respawnHeldTicks = -1;
            RestartRun();
        }
    }

    /// <summary>How long R is held to start the run again.</summary>
    private const float HoldToRestartSeconds = 1.0f;

    /// <summary>Ticks R has been held since it was pressed on a track; -1 when not counting.</summary>
    private int _respawnHeldTicks = -1;

    /// <summary>Backspace: the run starts again from the start gate.</summary>
    private void RestartRun()
    {
        if (_track is null || _car is null)
        {
            return;
        }

        if (_testDriveFrom is not null)
        {
            _car.Respawn(_spawn);
            _autoRespawn?.Reset();
            _hud?.Flash("Back where the test drive began.", 2.0);
            return;
        }

        _car.Respawn(SpawnAt(TrackStart(_track), _car.Spec));
        _autoRespawn?.Reset();
        ArmRace();
        _hud?.Flash("From the start: the clock starts on the throttle.", 3.0);
    }

    /// <summary>Once a tick: the race timer sees where the car is, starts on the first throttle, and says what was passed.</summary>
    private void TimeRace()
    {
        if (_race is null || _car is null || _hud is null)
        {
            return;
        }

        double now = _ticks / (double)Engine.PhysicsTicksPerSecond;
        if (_raceArmIn > 0)
        {
            // Until the run is armed afresh, the old run reads no gates: the trip back to the start from
            // wherever the car was mustn't count as passing one, or finish the old run on the way.
            if (--_raceArmIn > 0)
            {
                _race.Moved();
                return;
            }

            _race.Arm();
        }

        if (_race.Armed && (_car.Driver.Throttle > 0.0f || _car.Driver.Brake > 0.0f))
        {
            _race.Go(now);
            _autoThisRun = _autodriver is not null;
            _ghostRecorder?.Go();
            _teleportsSeen = _car.Teleports;
            _raceGoTick = _ticks;
            _ghostGap = 0.0f;
        }

        RaceEvent e = _race.Update(_car.GlobalPosition.ToCore(), now);
        _autoThisRun |= _autodriver is not null && _race.Running;
        switch (e.Kind)
        {
            case RaceEventKind.Checkpoint when OnACircuit && e.Gate % _track!.GatesPerLap() == 0:
                _hud.Flash(LapDone(e.Gate) + SplitAgainstBest(e.Gate - 1, e.Seconds), 4.0);
                break;
            case RaceEventKind.Checkpoint:
                _hud.Flash($"Checkpoint {CheckpointName(e.Gate)}: {LapTime(e.Seconds)}{SplitAgainstBest(e.Gate - 1, e.Seconds)}", 4.0);
                break;
            case RaceEventKind.OutOfOrder:
                _hud.Flash($"You missed checkpoint {CheckpointName(e.Gate)}: go back through it, or it won't count.", 4.0);
                break;
            case RaceEventKind.Finished:
                FileRun(e.Seconds, OnACircuit ? LapDone(_race.Passed) + ".  " : string.Empty);
                break;
        }

        RecordGhost();
    }

    /// <summary>
    /// Once a tick on a track: the car into the run's ghost, every 4th tick from the clock's start until just
    /// past the finish, the snapshot after a trip back to a checkpoint marked as a cut (M4b). A finished run's
    /// ghost is kept as the last run's, for M4c's ghosts and M4d's replays.
    /// </summary>
    private void RecordGhost()
    {
        if (_ghostRecorder is not { Recording: true } recorder || _car is null || _race is null)
        {
            return;
        }

        if (_car.Teleports != _teleportsSeen)
        {
            _teleportsSeen = _car.Teleports;
            recorder.Cut();
        }

        Snapshot now = _car.Snapshot();
        MeasureGhostGap(now);
        recorder.Tick(now, _race.Finished);
        if (!recorder.Done)
        {
            return;
        }

        IReadOnlyList<double> splits = _race.Splits;
        _lastGhost = recorder.ToGhost(new GhostHeader
        {
            Track = _trackPrint ?? string.Empty,
            TrackId = _track!.Track.Id,
            TrackName = _track.Track.Name,
            Driver = _autoThisRun ? GhostDriver.Autodriver : GhostDriver.You,
            Seconds = splits[^1],
            Splits = splits.ToArray(),
            Laps = _track.Track.Laps,
            When = System.DateTimeOffset.Now,
        }, _car.Spec);
        GD.Print($"The run as a ghost: {_lastGhost.Snapshots.Count} snapshots over {_lastGhost.Duration:F2} s, {_lastGhost.Save().Length / 1024.0:F1} KB as a file");
        GhostFiled(_lastGhost);
    }

    /// <summary>
    /// A checkpoint in words: "2 of 5" on a sprint; on a circuit, which of the lap's and which lap, since its
    /// gates come round again (M3g).
    /// </summary>
    private string CheckpointName(int gate)
    {
        if (!OnACircuit)
        {
            return $"{gate} of {_race!.Checkpoints}";
        }

        int perLap = _track!.GatesPerLap();
        int inLap = ((gate - 1) % perLap) + 1;
        return inLap == perLap ? $"the line, lap {_track.LapOf(gate)}" : $"{inLap} of {perLap - 1}, lap {_track.LapOf(gate)}";
    }

    /// <summary>A circuit's lap ended at a gate: its time, kept as the last lap and the best if it is, in words.</summary>
    private string LapDone(int gate)
    {
        IReadOnlyList<double> splits = _race!.Splits;
        int perLap = _track!.GatesPerLap();
        int lap = gate / perLap;
        double time = splits[gate - 1] - (lap > 1 ? splits[gate - perLap - 1] : 0.0);
        _lastLapTime = time;
        double? best = _autoThisRun ? _bestLapAuto : _bestLapYou;
        // A lap within a hundredth of the best equals it, rather than beating it "by 0.00 s".
        string verdict = best is not double b ? string.Empty
            : System.Math.Abs(time - b) < 0.005 ? ", equal to the best lap"
            : time < b ? $", a new best lap by {b - time:F2} s"
            : $", +{time - b:F2} s on the best lap";
        if (best is not double old || time < old)
        {
            if (_autoThisRun)
            {
                _bestLapAuto = time;
            }
            else
            {
                _bestLapYou = time;
            }
        }

        return $"Lap {lap} of {_track.Track.Laps}: {LapTime(time)}{verdict}";
    }

    /// <summary>A split against the same driver's best run at that gate: " (-1.20)", or nothing before there's a best.</summary>
    private string SplitAgainstBest(int split, double seconds)
    {
        RaceRun? best = _autoThisRun ? _bestRunAuto : _bestRunYou;
        if (best is null || split >= best.Splits.Count)
        {
            return string.Empty;
        }

        double delta = seconds - best.Splits[split];
        return $"  ({(delta < 0.0 ? "-" : "+")}{System.Math.Abs(delta):F2} on {(_autoThisRun ? "its" : "your")} best)";
    }

    /// <param name="lastLap">On a circuit, the last lap in words, to go before the run's time.</param>
    private void FileRun(double seconds, string lastLap = "")
    {
        var run = new RaceRun(seconds, _race!.Splits.ToList(), _autoThisRun, CarPrint);
        _lastRun = run;
        RaceRun? best = run.Auto ? _bestRunAuto : _bestRunYou;
        string who = run.Auto ? "The autodriver finishes" : "Finish";
        string verdict = best is null ? ". Saved as the ghost."
            : seconds < best.Seconds ? $", a new best by {best.Seconds - seconds:F2} s. Saved as the ghost."
            : $", +{seconds - best.Seconds:F2} s on the best.";
        if (best is null || seconds < best.Seconds)
        {
            // Its ghost is saved a tick or three from now, once the snapshot past the line is in (M4c).
            _saveGhostAs = run.Auto ? GhostDriver.Autodriver : GhostDriver.You;
            if (run.Auto)
            {
                _bestRunAuto = run;
            }
            else
            {
                _bestRunYou = run;
            }
        }

        GD.Print($"{(run.Auto ? "Autodriver's run" : "Run")} on {_track!.Track.Id}: {LapTime(seconds)} (splits {string.Join(", ", run.Splits.SkipLast(1).Select(LapTime))})"
            + (OnACircuit ? $"; laps {string.Join(", ", LapTimes(run.Splits).Select(LapTime))}" : string.Empty));
        _hud!.Flash($"{lastLap}{who}: {LapTime(seconds)}{verdict}  Backspace to go again, Enter to watch it.", 8.0);
    }

    /// <summary>A circuit run's lap times, from its splits: the time at each line less the one before.</summary>
    private IEnumerable<double> LapTimes(IReadOnlyList<double> splits)
    {
        int perLap = _track!.GatesPerLap();
        double before = 0.0;
        for (int g = perLap; g <= splits.Count; g += perLap)
        {
            yield return splits[g - 1] - before;
            before = splits[g - 1];
        }
    }

    /// <summary>The run board for the HUD: the clock, the gates passed, the bests; and on a circuit, the lap.</summary>
    private string RaceBoard()
    {
        RaceTimer race = _race!;
        double now = _ticks / (double)Engine.PhysicsTicksPerSecond;
        string clock = race.Running ? LapTime(race.Elapsed(now))
            : race.Finished ? $"{LapTime(race.Elapsed(now))}  finished"
            : $"{LapTime(0.0)}  ready";
        if (OnACircuit)
        {
            int perLap = _track!.GatesPerLap();
            int laps = _track.Track.Laps;
            int lap = System.Math.Min(laps, (race.Passed / perLap) + 1);
            int inLap = race.Passed % perLap;
            string next = race.Finished ? "all passed"
                : inLap == perLap - 1 ? (lap == laps ? "next: the line, to finish" : "next: the line")
                : $"next: checkpoint {inLap + 1} of {perLap - 1}";
            string last = _lastLapTime is double l ? $"   last {LapTime(l)}" : string.Empty;
            string runs = $"you {Best(_bestRunYou)}   auto {Best(_bestRunAuto)}";
            string laps2 = $"you {(_bestLapYou is double ly ? LapTime(ly) : "-")}   auto {(_bestLapAuto is double la ? LapTime(la) : "-")}";
            return $"RUN   {clock}\nLAP   {lap} of {laps}{last}\nGATES {next}\nBEST  run {runs}\n      lap {laps2}{GhostLine()}";
        }

        string gates = race.Finished ? "all passed"
            : race.Checkpoints == 0 ? "next: the finish"
            : race.Passed == 0 ? $"next: checkpoint 1 of {race.Checkpoints}"
            : race.Passed < race.Checkpoints ? $"{race.Passed} of {race.Checkpoints} passed, next {race.Passed + 1}"
            : $"{race.Checkpoints} of {race.Checkpoints} passed, next the finish";
        string best = $"you {Best(_bestRunYou)}   auto {Best(_bestRunAuto)}";
        return $"RUN   {clock}\nGATES {gates}\nBEST  {best}{GhostLine()}";
    }

    /// <summary>A best run's time, marked when it was set on another car than the one being driven (M4c, decision 6).</summary>
    private string Best(RaceRun? run) =>
        run is null ? "-" : run.Car == CarPrint ? LapTime(run.Seconds) : $"{LapTime(run.Seconds)} (another car)";

    /// <summary>The board's ghost line, when there's a ghost car to speak of (M4c).</summary>
    private string GhostLine() => _ghostCar is null ? string.Empty : $"\nGHOST {GhostWords()}  (G)";
}
