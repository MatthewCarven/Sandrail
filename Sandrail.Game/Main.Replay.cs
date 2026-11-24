using System;
using System.Collections.Generic;
using Godot;
using Sandrail.Core.Driving;
using Sandrail.Core.Generation;
using Sandrail.Core.Ghosts;
using Sandrail.Game.Cars;

namespace Sandrail.Game;

/// <summary>
/// Replays (M4-PLAN, M4d): the last run or a saved best, played back with cameras beside the track, the chase
/// camera, or a free one. Space pauses, the left and right arrows skip 5 seconds, up and down change the speed
/// (¼, ½, 1 and 2), C changes the camera, Esc goes back to the car as it was.
/// </summary>
/// <remarks>
/// <para>
/// <b>The race stops while it's on</b>: no physics tick of the game runs, so the clock waits; the car is held
/// still and hidden, and comes back moving as it was. Decision 7: the last run and the saved bests, from the
/// Esc menu, or Enter for the last run.
/// </para>
/// <para>
/// <b>The trackside cameras</b> (<see cref="ReplayCameras"/>) are placed from the laid track the first time
/// a replay starts on it, and the one covering the car's stretch of road follows it, zooming so the car stays
/// about the same size on the screen.
/// </para>
/// </remarks>
public partial class Main
{
    private enum ReplayView
    {
        Trackside,
        Chase,
        Free,
    }

    private static readonly float[] ReplaySpeeds = [0.25f, 0.5f, 1.0f, 2.0f];

    /// <summary>A trackside camera keeps this much of the scene across at the car, metres, zooming in as it's further off.</summary>
    private const float FrameAcrossM = 28.0f;

    private sealed class Replay
    {
        public required Ghost Run { get; init; }

        public required string Title { get; init; }

        /// <summary>How far along the route the car is at each snapshot.</summary>
        public required float[] Along { get; init; }

        public double T { get; set; }

        public int SpeedIndex { get; set; } = 2;

        public bool Paused { get; set; }

        public ReplayView View { get; set; } = ReplayView.Trackside;

        public int Camera { get; set; } = -1;

        public Vector3 LastAt { get; set; }

        public Vector3 CarVelocity { get; init; }

        public Vector3 CarSpin { get; init; }

        public float Speed => ReplaySpeeds[SpeedIndex];
    }

    private Replay? _replay;
    private ulong _replayEndedFrame = ulong.MaxValue;
    private int _replayTicks;
    private RacingLine? _cameraLine;
    private IReadOnlyList<ReplayCamera>? _replayCameras;
    private GhostCar? _replayCar;
    private Camera3D? _trackside;
    private ChaseCamera? _replayChase;
    private FreeCamera? _free;

    /// <summary>Whether Esc is a replay's (leaving it), so the menu leaves it alone; the frame it ends included.</summary>
    private bool EscTakenByReplay() => _replay is not null || _replayEndedFrame == Engine.GetProcessFrames();

    /// <summary>What can be watched, for the Esc menu: the last run, and each driver's best when it isn't the last run.</summary>
    private IReadOnlyList<(string Label, Action Watch)> ReplaysOnOffer()
    {
        var offer = new List<(string, Action)>();
        if (_track is null)
        {
            return offer;
        }

        if (_lastGhost is Ghost last)
        {
            offer.Add(($"Watch the last run ({LapTime(last.Header.Seconds)})", () => StartReplay(last, "the last run")));
        }

        if (_ghostYou is Ghost you && !ReferenceEquals(you, _lastGhost))
        {
            offer.Add(($"Watch your best ({LapTime(you.Header.Seconds)})", () => StartReplay(you, "your best")));
        }

        if (_ghostAuto is Ghost auto && !ReferenceEquals(auto, _lastGhost))
        {
            offer.Add(($"Watch the autodriver's best ({LapTime(auto.Header.Seconds)})", () => StartReplay(auto, "the autodriver's best")));
        }

        return offer;
    }

    /// <summary>
    /// Each profiled car's best on this track (P4), the fastest first, for the Esc menu's own list of them: there are as many
    /// as there are cars, which would run the menu off the window as buttons of their own.
    /// </summary>
    private IReadOnlyList<(string Label, Action Watch)> ProfileReplaysOnOffer()
    {
        var offer = new List<(string, Action)>();
        if (_track is null)
        {
            return offer;
        }

        foreach (Ghost car in _profileGhosts)
        {
            string at = car.Header.Caution is float c ? $" at {c * 100.0f:F0}%" : string.Empty;
            string driving = car.Header.Car == CarPrint ? ", this car" : string.Empty;
            offer.Add(($"{ProfileName(car)}: {LapTime(car.Header.Seconds)}{at}{driving}", () => StartReplay(car, $"{ProfileName(car)}'s profile best")));
        }

        return offer;
    }

    private T Added<T>(T node)
        where T : Node
    {
        AddChild(node);
        return node;
    }

    /// <summary>Starts a replay of a run, from a time into it: the car held and hidden, the replay car and its cameras up.</summary>
    private void StartReplay(Ghost run, string title, double from = 0.0)
    {
        if (_track is null || _car is null || _hud is null)
        {
            return;
        }

        if (_replay is not null)
        {
            EndReplay();
        }

        if (_replayCameras is null)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _cameraLine = ReplayCameras.Line(_track);
            Terrain? ground = _terrain;
            _replayCameras = ReplayCameras.Place(_track, ground is null ? (_, _) => 0.0f : (x, z) => ground.HeightAt(x, z));
            GD.Print($"Replay cameras: {_replayCameras.Count} beside {_track.Track.Id}, placed in {clock.Elapsed.TotalMilliseconds:F0} ms");
        }

        _replay = new Replay
        {
            Run = run,
            Title = title,
            Along = ReplayCameras.AlongRoute(_cameraLine!, run),
            T = Math.Clamp(from, 0.0, run.Duration),
            CarVelocity = _car.LinearVelocity,
            CarSpin = _car.AngularVelocity,
        };

        // The car held where it is, and hidden; the clock waits with it, since no game tick runs.
        _car.Freeze = true;
        _car.Visible = false;
        if (_autoRespawn is not null)
        {
            _autoRespawn.ProcessMode = ProcessModeEnum.Disabled;
        }

        if (_ghostCar is not null)
        {
            _ghostCar.Visible = false;
        }

        _replayCar ??= Added(new GhostCar { Solidity = 1.0f, Fades = false });
        _replayCar.Play(run);
        _trackside ??= Added(new Camera3D { Near = 0.1f, Far = 2000.0f, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off });
        _replayChase ??= Added(new ChaseCamera { Target = _replayCar, Speed = () => _replayCar.SpeedAt(_replay?.T ?? 0.0) });
        _free ??= Added(new FreeCamera());
        _replayCar.ShowAt(_replay.T, 0.0f, null);
        _replay.LastAt = _replayCar.GlobalPosition;
        ShowReplayView();
        _hud.Banner = $"REPLAY   {title}, {LapTime(run.Header.Seconds)}";
        _hud.ShowKeys = false;
        GD.Print($"Replay of {title} ({run.Header.Driver}, {LapTime(run.Header.Seconds)}) from {_replay.T:F1} s");
    }

    /// <summary>Back to the car as it was: shown, moving as it was, its own camera, the clock going again.</summary>
    private void EndReplay()
    {
        if (_replay is not Replay r || _car is null)
        {
            return;
        }

        _replay = null;
        _replayEndedFrame = Engine.GetProcessFrames();
        if (_replayCar is not null)
        {
            _replayCar.Visible = false;
        }

        _car.Freeze = false;
        _car.Visible = true;
        _car.LinearVelocity = r.CarVelocity;
        _car.AngularVelocity = r.CarSpin;
        if (_autoRespawn is not null)
        {
            _autoRespawn.ProcessMode = ProcessModeEnum.Inherit;
        }

        if (_camera is not null)
        {
            _camera.Current = true;
        }

        if (_hud is not null)
        {
            _hud.Banner = _autodriver is not null ? AutodriverBanner() : null;
            _hud.SpeedOverrideMps = null;
            _hud.ShowKeys = true;
        }
    }

    /// <summary>Once a frame in a replay: its keys, its clock, the car posed, the camera pointed, the board.</summary>
    private void UpdateReplay(double delta)
    {
        Replay r = _replay!;
        if (Input.IsActionJustPressed(Controls.Menu))
        {
            EndReplay();
            return;
        }

        if (Input.IsActionJustPressed(Controls.ReplayPause))
        {
            if (r.T >= r.Run.Duration)
            {
                r.T = 0.0;
                r.Paused = false;
            }
            else
            {
                r.Paused = !r.Paused;
            }
        }

        if (Input.IsActionJustPressed(Controls.ReplayBack))
        {
            r.T = Math.Max(0.0, r.T - 5.0);
        }

        if (Input.IsActionJustPressed(Controls.ReplayForward))
        {
            r.T = Math.Min(r.Run.Duration, r.T + 5.0);
        }

        if (Input.IsActionJustPressed(Controls.ReplayFaster))
        {
            r.SpeedIndex = Math.Min(ReplaySpeeds.Length - 1, r.SpeedIndex + 1);
        }

        if (Input.IsActionJustPressed(Controls.ReplaySlower))
        {
            r.SpeedIndex = Math.Max(0, r.SpeedIndex - 1);
        }

        if (Input.IsActionJustPressed(Controls.Camera))
        {
            r.View = (ReplayView)(((int)r.View + 1) % 3);
            ShowReplayView();
        }

        float played = 0.0f;
        if (!r.Paused)
        {
            played = (float)(delta * r.Speed);
            r.T += played;
            if (r.T >= r.Run.Duration)
            {
                r.T = r.Run.Duration;
                r.Paused = true;
            }
        }

        _replayCar!.ShowAt(r.T, played, GetViewport().GetCamera3D());
        Vector3 at = _replayCar.GlobalPosition;

        // A cut (the car put back at a checkpoint), or a skip: the chase camera starts behind it again.
        if (at.DistanceTo(r.LastAt) > 30.0f)
        {
            _replayChase!.Snap();
        }

        r.LastAt = at;
        if (r.View == ReplayView.Trackside)
        {
            PointTrackside(r, at);
        }

        _hud!.SpeedOverrideMps = _replayCar.SpeedAt(r.T);
        _hud.Laps = ReplayBoard(r);
    }

    /// <summary>The camera for the view chosen: trackside, chase, or free from wherever the last one was.</summary>
    private void ShowReplayView()
    {
        if (_replay is not Replay r)
        {
            return;
        }

        switch (r.View)
        {
            case ReplayView.Trackside:
                _trackside!.Current = true;
                PointTrackside(r, _replayCar!.GlobalPosition);
                break;
            case ReplayView.Chase:
                _replayChase!.Snap();
                _replayChase.Current = true;
                break;
            default:
                _free!.TakeOver(GetViewport().GetCamera3D() ?? _trackside!);
                break;
        }
    }

    /// <summary>The trackside camera covering the car's stretch of road, looking at the car and zoomed to it.</summary>
    private void PointTrackside(Replay r, Vector3 at)
    {
        r.Camera = ReplayCameras.Covering(_replayCameras!, AlongAt(r));
        System.Numerics.Vector3 p = _replayCameras![r.Camera].Position;
        var spot = new Vector3(p.X, p.Y, p.Z);
        Vector3 look = at + (Vector3.Up * 0.6f);
        _trackside!.GlobalPosition = spot;
        Vector3 toward = look - spot;
        if (toward.LengthSquared() > 1e-4f && toward.Normalized().Cross(Vector3.Up).LengthSquared() > 1e-4f)
        {
            _trackside.LookAt(look, Vector3.Up);
        }

        float d = MathF.Max(1.0f, toward.Length());
        _trackside.Fov = Math.Clamp(2.0f * Mathf.RadToDeg(MathF.Atan(FrameAcrossM / 2.0f / d)), 12.0f, 70.0f);
    }

    /// <summary>How far along the route the replay car is now: between its snapshots, round the lap on a circuit, held at a cut.</summary>
    private float AlongAt(Replay r)
    {
        float[] along = r.Along;
        double f = Math.Clamp(r.T * Ghost.Rate, 0.0, along.Length - 1);
        int k = (int)Math.Floor(f);
        if (k >= along.Length - 1 || r.Run.Snapshots[k + 1].Cut)
        {
            return along[Math.Min(k, along.Length - 1)];
        }

        float a = along[k];
        float b = along[k + 1];
        float length = _cameraLine!.Length;
        if (_cameraLine.Closed && MathF.Abs(b - a) > length / 2.0f)
        {
            b += b < a ? length : -length;
        }

        float now = a + ((b - a) * (float)(f - k));
        return _cameraLine.Closed ? ((now % length) + length) % length : now;
    }

    /// <summary>The replay's board: what's playing, the time, the speed, the camera and the keys.</summary>
    private string ReplayBoard(Replay r)
    {
        string view = r.View switch
        {
            ReplayView.Trackside when r.Camera >= 0 => $"trackside, by {_replayCameras![r.Camera].Why}",
            ReplayView.Trackside => "trackside",
            ReplayView.Chase => "chase",
            _ => "free: right mouse to look, W/A/S/D and Q/E to fly",
        };
        string state = r.T >= r.Run.Duration ? "  the end (Space: again)" : r.Paused ? "  paused" : string.Empty;
        return $"TIME   {LapTime(r.T)} of {LapTime(r.Run.Duration)}   x{r.Speed:0.##}{state}\n"
            + $"CAMERA {view}  (C)\n"
            + "KEYS   Space pause  ·  ←/→ 5 s  ·  ↑/↓ speed  ·  Esc back";
    }
}
