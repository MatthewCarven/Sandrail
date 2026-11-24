using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sandrail.Core.Ghosts;
using Sandrail.Core.Tracks;
using Num = System.Numerics;

namespace Sandrail.Game.Oracles;

/// <summary>
/// M4b's oracle: the autodriver drives a track as the <c>track</c> oracle does (the shakedown unless told, for
/// its loop and both jumps), recorded as a ghost the way the game records a run; the ghost is saved and
/// loaded, and played back against where the car really was at every one of the 120 ticks a second.
/// </summary>
/// <remarks>
/// It prints the ghost's size and the worst miss on straights, in corners, in the loop and in the air (M4-PLAN
/// fact 2 says millimetres), and the worst rotation miss: the proof that 30 snapshots a second are enough
/// before anything is built that shows them.
/// </remarks>
public partial class Oracle
{
    public const string GhostDrive = "ghost";

    /// <summary>The track's fingerprint, for the ghost's header.</summary>
    public string? TrackPrint { get; init; }

    private enum GhostSpot { Straights, Corners, Loop, Air }

    private GhostRecorder? _ghostRecorder;
    private int _ghostFrom;
    private int _ghostTeleports;
    private readonly List<(int Tick, Num.Vector3 At, Num.Quaternion Turn, GhostSpot Spot, int Piece)> _ghostTruth = new();

    private bool GhostDone => _ghostRecorder?.Done == true;

    /// <summary>The clock started: the recorder starts with it.</summary>
    private void StartGhost()
    {
        _ghostRecorder = new GhostRecorder(Engine.PhysicsTicksPerSecond);
        _ghostRecorder.Go();
        _ghostFrom = _tick;
        _ghostTeleports = Car.Teleports;
    }

    /// <summary>Once a tick from the clock's start: the car into the ghost, and where it really is into the truth.</summary>
    private void WatchGhost(PlacedPiece here, int piece, bool airborne, bool finished)
    {
        if (_ghostRecorder is not { Recording: true } recorder)
        {
            return;
        }

        if (Car.Teleports != _ghostTeleports)
        {
            _ghostTeleports = Car.Teleports;
            recorder.Cut();
        }

        Snapshot pose = Car.Snapshot();
        GhostSpot spot = airborne ? GhostSpot.Air
            : here.Def.Path.Any(s => s.Loop is not null) ? GhostSpot.Loop
            : here.Def.Path.Any(s => s.Turn is not null) ? GhostSpot.Corners
            : GhostSpot.Straights;
        _ghostTruth.Add((_tick - _ghostFrom, pose.Position, pose.Rotation, spot, piece));
        recorder.Tick(pose, finished);
    }

    private string GhostLine(LaidTrack track, RaceTimer timer, string verdict)
    {
        if (_ghostRecorder is null || _ghostRecorder.Snapshots.Count == 0)
        {
            return $"{track.Track.Id} at caution {Caution:F2}: {verdict} | NO GHOST: the clock never started";
        }

        Ghost ghost = _ghostRecorder.ToGhost(new GhostHeader
        {
            Track = TrackPrint ?? string.Empty,
            TrackId = track.Track.Id,
            TrackName = track.Track.Name,
            Driver = GhostDriver.Autodriver,
            Seconds = timer.Splits.Count > 0 ? timer.Splits[^1] : 0.0,
            Splits = timer.Splits.ToArray(),
            Laps = track.Track.Laps,
            When = DateTimeOffset.Now,
        }, Car.Spec);
        byte[] file = ghost.Save();
        Ghost back = Ghost.Load(file);
        bool same = back.Snapshots.SequenceEqual(ghost.Snapshots) && back.Save().AsSpan().SequenceEqual(file);

        float tps = Engine.PhysicsTicksPerSecond;
        var worst = new Dictionary<GhostSpot, float>();
        var ticks = new Dictionary<GhostSpot, int>();
        float worstTurn = 0.0f;
        (float Miss, double T, int Piece) worstOfAll = default;
        foreach ((int tick, Num.Vector3 at, Num.Quaternion turn, GhostSpot spot, int piece) in _ghostTruth)
        {
            double t = tick / (double)tps;
            if (t > back.Duration)
            {
                continue;
            }

            Snapshot played = back.At(t);
            float miss = Num.Vector3.Distance(played.Position, at);
            worst[spot] = MathF.Max(worst.GetValueOrDefault(spot), miss);
            ticks[spot] = ticks.GetValueOrDefault(spot) + 1;
            if (miss > worstOfAll.Miss)
            {
                worstOfAll = (miss, t, piece);
            }

            float dot = MathF.Min(1.0f, MathF.Abs(Num.Quaternion.Dot(Num.Quaternion.Normalize(played.Rotation), Num.Quaternion.Normalize(turn))));
            worstTurn = MathF.Max(worstTurn, 2.0f * MathF.Acos(dot) * 180.0f / MathF.PI);
        }

        string misses = string.Join(", ", Enum.GetValues<GhostSpot>().Select(s => ticks.ContainsKey(s)
            ? $"{Spot(s)} {worst[s] * 1000.0f:F1} mm ({ticks[s] / tps:F1} s)"
            : $"{Spot(s)} -"));
        return $"{track.Track.Id} at caution {Caution:F2}: {verdict} | ghost: {ghost.Snapshots.Count} snapshots over {ghost.Duration:F2} s, "
            + $"{file.Length / 1024.0:F1} KB ({file.Length / ghost.Duration / 1024.0:F2} KB a second), saved and loaded {(same ? "the same" : "DIFFERENT")} "
            + $"| played back against the car at {tps:F0} a second, worst miss: {misses}; the worst of all {Clock(worstOfAll.T)} in, on {Label(track, worstOfAll.Piece)}; rotation {worstTurn:F2}°";
    }

    private static string Spot(GhostSpot spot) => spot switch
    {
        GhostSpot.Straights => "straights",
        GhostSpot.Corners => "corners",
        GhostSpot.Loop => "the loop",
        _ => "in the air",
    };
}
