using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Game.Cars;

namespace Sandrail.Game.Oracles;

/// <summary>
/// Drives the car headless and prints one line of figures, then quits (M1-PLAN, M1a).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why they exist: Claude can't drive</b> (M1-PLAN fact 1). A tuning change shows up here as a
/// number before anyone sits down to play, and a figure that moves when nothing should have is a
/// regression caught.
/// </para>
/// <para>
/// Run them with <c>--fixed-fps 120</c> so every frame is exactly one physics tick and a run takes
/// however long the CPU needs, not the seconds it simulates. <c>tools/oracles.ps1</c> runs them all.
/// Every line ends with the spec's ID and fingerprint, so a figure says which numbers it came from.
/// </para>
/// <para>
/// Straight, drop, brake and circle run on the bare plain with road grip, as they did at M1a, so their
/// figures stay comparable; jump, loop and bumps run on the test ground (<c>Oracle.Ground.cs</c>).
/// </para>
/// </remarks>
public partial class Oracle : Node
{
    public const string Straight = "straight";
    public const string Drop = "drop";
    public const string Brake = "brake";
    public const string Circle = "circle";

    /// <summary>How far above rest height the drop oracle lets go.</summary>
    public const float DropHeight = 1.0f;

    private const float SettleSeconds = 1.0f;
    private const float HundredKmh = 100.0f / 3.6f;
    private const float Band = 0.005f;

    private int _tick;
    private bool _done;

    // straight
    private float? _timeTo100;
    private float _at5;
    private float _at10;
    private float _at20;
    private float _top;
    private Vector3 _start;

    // drop
    private float? _contactAt;
    private float _deepest;
    private readonly List<(float T, float Y)> _heights = new();

    // brake
    private bool _braking;
    private float _brakeFrom;
    private float _brakeSpeed;
    private Vector3 _brakeHeading;

    // circle
    private float? _rampFrom;
    private readonly Queue<float> _lateral = new();
    private float _bestLateral;
    private float _steerAtBest;
    private float _steerDegAtBest;

    /// <summary>
    /// One of <see cref="Straight"/>, <see cref="Drop"/>, <see cref="Brake"/>, <see cref="Circle"/>,
    /// <see cref="Jump"/>, <see cref="Loop"/>, <see cref="Bumps"/>.
    /// </summary>
    public required string Kind { get; init; }

    public required Buggy Car { get; init; }

    public static bool IsKnown(string kind) => kind is Straight or Drop or Brake or Circle || NeedsTestGround(kind);

    private float Time => _tick / (float)Engine.PhysicsTicksPerSecond;

    private Vector3 Forward => -Car.GlobalBasis.Z.Normalized();

    private float ForwardSpeed => Car.LinearVelocity.Dot(Forward);

    public override void _PhysicsProcess(double delta)
    {
        if (_done)
        {
            return;
        }

        _tick++;
        switch (Kind)
        {
            case Straight:
                RunStraight();
                break;
            case Drop:
                RunDrop();
                break;
            case Brake:
                RunBrake();
                break;
            case Circle:
                RunCircle();
                break;
            case Jump:
                RunJump();
                break;
            case Loop:
                RunLoop();
                break;
            case Bumps:
                RunBumps();
                break;
            case Lap:
                RunLap();
                break;
        }
    }

    private void RunStraight()
    {
        if (Time < SettleSeconds)
        {
            Car.Driver = default;
            _start = Car.GlobalPosition;
            return;
        }

        float t = Time - SettleSeconds;
        Car.Driver = new DriverInput { Throttle = 1.0f };
        float v = ForwardSpeed;

        if (_timeTo100 is null && v >= HundredKmh)
        {
            _timeTo100 = t;
        }

        if (_at5 == 0.0f && t >= 5.0f)
        {
            _at5 = v;
        }

        if (_at10 == 0.0f && t >= 10.0f)
        {
            _at10 = v;
        }

        if (_at20 == 0.0f && t >= 20.0f)
        {
            _at20 = v;
        }

        _top = MathF.Max(_top, v);

        if (t >= 30.0f)
        {
            float drift = Car.GlobalPosition.X - _start.X;
            Finish(
                $"0-100 km/h {Seconds(_timeTo100)} | at 5 s {_at5:F1} m/s | 10 s {_at10:F1} | 20 s {_at20:F1} | "
                + $"top {_top:F2} m/s ({_top * 3.6f:F0} km/h; the model's estimate {WheelModel.TopSpeedEstimate(Car.Spec, Surfaces.Road):F2}) | "
                + $"drift {drift:F2} m");
        }
    }

    private void RunDrop()
    {
        float y = Car.GlobalPosition.Y;
        for (int i = 0; i < 4; i++)
        {
            _deepest = MathF.Max(_deepest, Car.Compression(i));
        }

        if (_contactAt is null)
        {
            if (Car.GroundedCount > 0)
            {
                _contactAt = Time;
            }

            return;
        }

        _heights.Add((Time - _contactAt.Value, y));
        if (Time - _contactAt.Value < 4.0f)
        {
            return;
        }

        float rest = _heights.Where(h => h.T >= 3.5f).Average(h => h.Y);

        // Turning points of the height after contact; the first is the bottom of the landing, every
        // later one outside the band is an overshoot.
        int turns = 0;
        for (int i = 1; i < _heights.Count - 1; i++)
        {
            float before = _heights[i].Y - _heights[i - 1].Y;
            float after = _heights[i + 1].Y - _heights[i].Y;
            if (MathF.Sign(before) != MathF.Sign(after) && before != 0.0f && MathF.Abs(_heights[i].Y - rest) > Band)
            {
                turns++;
            }
        }

        float settled = _heights.LastOrDefault(h => MathF.Abs(h.Y - rest) > Band).T;
        float travel = Car.Spec.SuspensionTravelM;
        bool stop = _deepest > travel * (1.0f - Car.Spec.BumpStopFraction);

        Finish(
            $"from {DropHeight:F1} m: deepest compression {_deepest:F3} of {travel:F2} m"
            + $"{(stop ? " (on the bump stop)" : string.Empty)} | overshoots {Math.Max(0, turns - 1)} | "
            + $"settled {settled:F2} s after contact | rest height {rest:F3} m (expected {Buggy.RestHeight(Car.Spec):F3})");
    }

    private void RunBrake()
    {
        if (Time < SettleSeconds)
        {
            Car.Driver = default;
            return;
        }

        float v = ForwardSpeed;
        if (!_braking)
        {
            Car.Driver = new DriverInput { Throttle = 1.0f };
            if (v >= HundredKmh)
            {
                _braking = true;
                _brakeFrom = Time;
                _brakeSpeed = v;
                _start = Car.GlobalPosition;
                _brakeHeading = Forward;
            }

            return;
        }

        Car.Driver = new DriverInput { Brake = 1.0f };

        // Let go before a standstill: held, the brake would reverse.
        if (v <= 0.3f)
        {
            Car.Driver = default;
            Vector3 travelled = Car.GlobalPosition - _start;
            float distance = new Vector2(travelled.X, travelled.Z).Length();
            float mean = _brakeSpeed * _brakeSpeed / (2.0f * distance * WheelModel.Gravity);
            float physicsLimit = _brakeSpeed * _brakeSpeed / (2.0f * Car.Spec.Grip * WheelModel.Gravity);
            float turned = Mathf.RadToDeg(_brakeHeading.AngleTo(Forward));
            Finish(
                $"from {_brakeSpeed * 3.6f:F0} km/h: stopped in {distance:F1} m and {Time - _brakeFrom:F2} s "
                + $"(the grip's limit is {physicsLimit:F1} m) | mean {mean:F2} g | heading moved {turned:F1}°");
        }
    }

    private void RunCircle()
    {
        const float Target = 20.0f;
        const float RampSeconds = 15.0f;

        if (Time < SettleSeconds)
        {
            Car.Driver = default;
            return;
        }

        float v = ForwardSpeed;
        float throttle = Math.Clamp((Target - v) * 0.3f, 0.0f, 1.0f);
        float brake = Math.Clamp((v - Target) * 0.3f, 0.0f, 1.0f);

        if (_rampFrom is null)
        {
            Car.Driver = new DriverInput { Throttle = throttle, Brake = brake };
            if (Time > SettleSeconds + 8.0f)
            {
                _rampFrom = Time;
            }

            return;
        }

        float steer = Math.Clamp((Time - _rampFrom.Value) / RampSeconds, 0.0f, 1.0f);
        Car.Driver = new DriverInput { Throttle = throttle, Brake = brake, Steer = steer };

        Vector3 up = Car.GlobalBasis.Y.Normalized();
        Vector3 right = Car.GlobalBasis.X.Normalized();
        float yawRate = Car.AngularVelocity.Dot(up);
        _lateral.Enqueue(MathF.Abs(v * yawRate));
        if (_lateral.Count > Engine.PhysicsTicksPerSecond / 2)
        {
            _lateral.Dequeue();
        }

        float average = _lateral.Average() / WheelModel.Gravity;
        if (average > _bestLateral)
        {
            _bestLateral = average;
            _steerAtBest = steer;
            _steerDegAtBest = Car.SteerDeg;
        }

        float bodySlip = Mathf.RadToDeg(MathF.Atan2(Car.LinearVelocity.Dot(right), v));
        string held = $"max sideways {_bestLateral:F2} g at steer {_steerAtBest:F2} ({_steerDegAtBest:F1}°)";

        if (up.Dot(Vector3.Up) < 0.5f)
        {
            Finish($"{held} | ROLLED at {Time - _rampFrom.Value:F1} s into the ramp");
        }
        else if (MathF.Abs(bodySlip) > 25.0f)
        {
            Finish($"{held} | SPUN at {Time - _rampFrom.Value:F1} s into the ramp (body slip {bodySlip:F0}°)");
        }
        else if (Time - _rampFrom.Value >= RampSeconds + 3.0f)
        {
            Finish($"{held} | held to full lock (body slip {bodySlip:F1}°, speed {v:F1} m/s)");
        }
    }

    private static string Seconds(float? t) => t is float s ? $"{s:F2} s" : "never";

    private void Finish(string line)
    {
        _done = true;
        Car.Driver = default;
        GD.Print($"oracle {Kind}: {line}  [{Car.Spec.Id} {Car.Spec.Fingerprint()}]");
        GetTree().Quit();
    }
}
