using System.Collections.Generic;
using System.Linq;
using Godot;
using Sandrail.Core.Driving;
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
/// run starts, or once it's finished, that's the start, and the clock waits for the throttle again.
/// <b>Backspace starts the run again</b> from the start gate, clock and all.
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

    private sealed record RaceRun(double Seconds, IReadOnlyList<double> Splits, bool Auto);

    /// <summary>The line the autodriver drives and L shows: the track's route, or the test ground's corner lap.</summary>
    private RacingLine? DrivenLine => _routeLine ?? _lapLine;

    /// <summary>The track's timer and its racing line, once it's built.</summary>
    private void SetUpRace(LaidTrack track, Sandrail.Core.Cars.CarSpec spec)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _race = new RaceTimer(track.Gates());
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
    }

    /// <summary>Where the car is put back on a track: the last gate passed while a run is on, the start otherwise.</summary>
    private Transform3D TrackHome()
    {
        if (_track is null || _race is null || !_race.Running || _race.Passed == 0)
        {
            return SpawnAt(TrackStart(_track!), _car!.Spec);
        }

        Frame gate = _race.LastGate.Frame;
        return SpawnAt(gate with { Position = gate.Position + (gate.Forward * PastTheArchM) }, _car!.Spec);
    }

    /// <summary>
    /// The car has been put back on a track. During a run past its first checkpoint, the clock runs on;
    /// otherwise it's back at the start, and the run starts again.
    /// </summary>
    private string AfterTrackRespawn()
    {
        if (_race is null)
        {
            return "the start";
        }

        if (_race.Running && _race.Passed > 0)
        {
            _race.Moved();
            return $"checkpoint {_race.Passed}";
        }

        if (_race.Running)
        {
            // Back to the start mid-run: the clock runs on, as it would from a checkpoint.
            _race.Moved();
            return "the start, clock running";
        }

        ArmRace();
        return "the start";
    }

    /// <summary>R on a track.</summary>
    private void RespawnOnTrack()
    {
        _car!.Respawn(TrackHome());
        _autoRespawn?.Reset();
        string where = AfterTrackRespawn();
        _hud?.Flash($"Back to {where}.", 2.0);
    }

    /// <summary>Backspace: the run starts again from the start gate.</summary>
    private void RestartRun()
    {
        if (_track is null || _car is null)
        {
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
        if (_raceArmIn > 0 && --_raceArmIn == 0)
        {
            _race.Arm();
        }

        if (_race.Armed && (_car.Driver.Throttle > 0.0f || _car.Driver.Brake > 0.0f))
        {
            _race.Go(now);
            _autoThisRun = _autodriver is not null;
        }

        RaceEvent e = _race.Update(_car.GlobalPosition.ToCore(), now);
        _autoThisRun |= _autodriver is not null && _race.Running;
        switch (e.Kind)
        {
            case RaceEventKind.Checkpoint:
                _hud.Flash($"Checkpoint {e.Gate} of {_race.Checkpoints}: {LapTime(e.Seconds)}{SplitAgainstBest(e.Gate - 1, e.Seconds)}", 4.0);
                break;
            case RaceEventKind.OutOfOrder:
                _hud.Flash($"You missed checkpoint {e.Gate}: go back through it, or it won't count.", 4.0);
                break;
            case RaceEventKind.Finished:
                FileRun(e.Seconds);
                break;
        }
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

    private void FileRun(double seconds)
    {
        var run = new RaceRun(seconds, _race!.Splits.ToList(), _autoThisRun);
        _lastRun = run;
        RaceRun? best = run.Auto ? _bestRunAuto : _bestRunYou;
        string who = run.Auto ? "The autodriver finishes" : "Finish";
        string verdict = best is null ? "."
            : seconds < best.Seconds ? $", a new best by {best.Seconds - seconds:F2} s."
            : $", +{seconds - best.Seconds:F2} s on the best.";
        if (best is null || seconds < best.Seconds)
        {
            if (run.Auto)
            {
                _bestRunAuto = run;
            }
            else
            {
                _bestRunYou = run;
            }
        }

        GD.Print($"{(run.Auto ? "Autodriver's run" : "Run")} on {_track!.Track.Id}: {LapTime(seconds)} (splits {string.Join(", ", run.Splits.SkipLast(1).Select(LapTime))})");
        _hud!.Flash($"{who}: {LapTime(seconds)}{verdict}  Backspace to go again.", 8.0);
    }

    /// <summary>The run board for the HUD: the clock, the gates passed, the bests.</summary>
    private string RaceBoard()
    {
        RaceTimer race = _race!;
        double now = _ticks / (double)Engine.PhysicsTicksPerSecond;
        string clock = race.Running ? LapTime(race.Elapsed(now))
            : race.Finished ? $"{LapTime(race.Elapsed(now))}  finished"
            : $"{LapTime(0.0)}  ready";
        string gates = race.Finished ? "all passed"
            : race.Checkpoints == 0 ? "next: the finish"
            : race.Passed == 0 ? $"next: checkpoint 1 of {race.Checkpoints}"
            : race.Passed < race.Checkpoints ? $"{race.Passed} of {race.Checkpoints} passed, next {race.Passed + 1}"
            : $"{race.Checkpoints} of {race.Checkpoints} passed, next the finish";
        string best = $"you {(_bestRunYou is RaceRun y ? LapTime(y.Seconds) : "-")}   auto {(_bestRunAuto is RaceRun a ? LapTime(a.Seconds) : "-")}";
        return $"RUN   {clock}\nGATES {gates}\nBEST  {best}";
    }
}
