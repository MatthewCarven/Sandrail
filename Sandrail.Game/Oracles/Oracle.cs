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
/// figures stay comparable, and so do handbrake, sweeper and respawn (M1d); jump, air, loop, bumps and lap run on the test
/// ground (<c>Oracle.Ground.cs</c>).
/// </para>
/// </remarks>
public partial class Oracle : Node
{
    public const string Straight = "straight";
    public const string Drop = "drop";
    public const string Brake = "brake";
    public const string Circle = "circle";
    public const string Handbrake = "handbrake";
    public const string Sweeper = "sweeper";
    public const string Respawn = "respawn";

    /// <summary>The sweeper's circle, metres: its centre is this far to the right of the start, so the car starts on it, facing along it.</summary>
    private const float SweepRadius = 120.0f;

    /// <summary>How fast the sweeper's target speed climbs once it's at <see cref="SweepFrom"/>, m/s each second: slow enough to read as steady.</summary>
    private const float SweepRamp = 0.25f;

    private const float SweepFrom = 20.0f;

    /// <summary>How far above rest height the drop oracle lets go.</summary>
    public const float DropHeight = 1.0f;

    /// <summary>The handbrake oracle's speed going in: 60 km/h, a hairpin's.</summary>
    private const float HandbrakeEntry = 60.0f / 3.6f;
    private const float TurnSeconds = 2.0f;
    private const float PullSeconds = 0.5f;

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
    private float? _offTheEndAt;
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

    // sweeper
    private float? _sweepRampFrom;
    private readonly Queue<float> _sweepLateral = new();
    private float _sweepBest;
    private float _sweepBestG;
    private float _sweepBestDownforce;
    private float _sweepShort;

    // respawn: the car stuck two ways, 20 m from the start, and how it's brought back
    private enum StuckRun { OnItsRoof, BelowTheWorld }

    private StuckRun _stuckRun;
    private float? _stuckPutAt;
    private bool _stuckArrived;
    private float? _stuckRestingAt;
    private float _stuckRestedFrom;
    private float _stuckLowest;

    private float StuckAwayX => _stuckRun == StuckRun.OnItsRoof ? 20.0f : 1050.0f;
    private readonly List<string> _stuckFigures = new();

    // handbrake: three runs from the same spot, each from rest
    private enum HandbrakeRun { Stop, SteeringAlone, WithTheHandbrake }

    private HandbrakeRun _run;
    private Transform3D _home;
    private float _runFrom;
    private float? _upToSpeedAt;
    private float? _manoeuvreFrom;
    private float _entry;
    private float _turned;
    private float _peakSlip;
    private string _stopFigures = string.Empty;
    private string _steerFigures = string.Empty;

    /// <summary>
    /// One of <see cref="Straight"/>, <see cref="Drop"/>, <see cref="Brake"/>, <see cref="Circle"/>,
    /// <see cref="Handbrake"/>, <see cref="Sweeper"/>, <see cref="Respawn"/>, <see cref="Jump"/>, <see cref="Air"/>, <see cref="Loop"/>, <see cref="Bumps"/>, <see cref="Lap"/>.
    /// </summary>
    public required string Kind { get; init; }

    public required Buggy Car { get; init; }

    public static bool IsKnown(string kind) => kind is Straight or Drop or Brake or Circle or Handbrake or Sweeper or Respawn || NeedsTestGround(kind);

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
            case Handbrake:
                RunHandbrake();
                break;
            case Sweeper:
                RunSweeper();
                break;
            case Respawn:
                RunRespawn();
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
            case Air:
                RunAir();
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

        // Top speed counts only with all four wheels down. Thirty seconds flat out is over a kilometre, and
        // the plain ends a kilometre from the start: a car near 50 m/s goes off its end at about 27 s, and
        // a falling car's speed is gravity's, not the engine's (found 2026-11-23, when a wing seemed to
        // add top speed).
        if (Car.GroundedCount == 4)
        {
            _top = MathF.Max(_top, v);
        }
        else if (_offTheEndAt is null && t > 5.0f)
        {
            _offTheEndAt = t;
        }

        if (t >= 30.0f)
        {
            float drift = Car.GlobalPosition.X - _start.X;
            Finish(
                $"0-100 km/h {Seconds(_timeTo100)} | at 5 s {_at5:F1} m/s | 10 s {_at10:F1} | 20 s {_at20:F1} | "
                + $"top {_top:F2} m/s ({_top * 3.6f:F0} km/h; the model's estimate {WheelModel.TopSpeedEstimate(Car.Spec, Surfaces.Road):F2}) | "
                + $"drift {drift:F2} m{(_offTheEndAt is float off ? $" | off the plain's end at {off:F1} s" : string.Empty)}");
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

    /// <summary>
    /// The auto-respawn's oracle (M1d, decision 4), the one oracle that runs with it: the car dropped on its
    /// roof 20 m from the start, then dropped just past the plain's end, and when each time it's back at
    /// the start on its wheels. On its roof it should be three seconds from coming to rest; past the end,
    /// as soon as it's 30 m down. (The plain is Jolt's world boundary: solid all the way down, so a car put
    /// under it is pushed back up, and 2 km across, its default size, so it ends 1000 m out. On its side
    /// isn't tried: the chassis box, its centre of mass low, tips back onto its wheels.
    /// <c>StuckWatchTests</c> covers it.)
    /// </summary>
    private void RunRespawn()
    {
        Car.Driver = default;
        if (_stuckPutAt is null)
        {
            Transform3D at = _stuckRun == StuckRun.OnItsRoof
                ? new Transform3D(new Basis(Vector3.Back, Mathf.Pi), new Vector3(StuckAwayX, 1.5f, 0.0f))
                : new Transform3D(Basis.Identity, new Vector3(StuckAwayX, 5.0f, 0.0f));
            Car.Respawn(at);
            _stuckPutAt = Time;
            _stuckArrived = false;
            _stuckRestingAt = null;
            _stuckLowest = float.MaxValue;
            return;
        }

        Vector3 p = Car.GlobalPosition;
        float upright = Car.GlobalBasis.Y.Normalized().Dot(Vector3.Up);
        _stuckArrived |= MathF.Abs(p.X - StuckAwayX) < 2.0f;
        _stuckLowest = MathF.Min(_stuckLowest, p.Y);

        // When it last came to rest tipped over: the moment the auto-respawn's three seconds start from.
        bool resting = upright <= Core.Driving.StuckWatch.TippedCosine && Car.LinearVelocity.Length() < Core.Driving.StuckWatch.NearlyStoppedMps;
        _stuckRestingAt = resting ? _stuckRestingAt ?? Time : null;
        if (_stuckRestingAt is float restingFrom)
        {
            _stuckRestedFrom = restingFrom;
        }

        if (_stuckArrived && MathF.Abs(p.X) < 1.0f && upright > 0.9f)
        {
            _stuckFigures.Add(_stuckRun == StuckRun.OnItsRoof
                ? $"on its roof: back at the start on its wheels {Time - _stuckRestedFrom:F2} s after it came to rest ({Time - _stuckPutAt.Value:F2} s after the drop)"
                : $"dropped past the plain's end: back {Time - _stuckPutAt.Value:F2} s after the drop, from {_stuckLowest:F0} m");
            if (_stuckRun == StuckRun.BelowTheWorld)
            {
                Finish(string.Join("; ", _stuckFigures));
                return;
            }

            _stuckRun++;
            _stuckPutAt = null;
            return;
        }

        if (_stuckRun == StuckRun.OnItsRoof && _stuckRestingAt is float rest && Time - rest > Core.Driving.StuckWatch.Seconds + 0.5f)
        {
            Finish($"on its roof: NOT put back, {Time - rest:F1} s at rest upside down");
        }
        else if (Time - _stuckPutAt.Value > 20.0f)
        {
            Finish($"{_stuckRun}: NOT put back in 20 s (upright {upright:F2}, at {p.X:F1}, {p.Y:F1}, {p.Z:F1})");
        }
    }

    /// <summary>
    /// The most grip the car has at speed (M1d): it drives round a 120 m circle, steered back onto it,
    /// while the speed it's asked for climbs a quarter of a metre a second each second, until it runs
    /// 4 m wide, spins or runs out of power. It reports its best sideways acceleration and the speed and
    /// downforce that came with it, which is where a wing shows: at 72 km/h, where <c>circle</c> runs, a
    /// wing hardly presses at all.
    /// </summary>
    private void RunSweeper()
    {
        if (Time < SettleSeconds)
        {
            Car.Driver = default;
            return;
        }

        float dt = 1.0f / Engine.PhysicsTicksPerSecond;
        Vector3 p = Car.GlobalPosition;
        var fromCentre = new Vector2(p.X - SweepRadius, p.Z);
        float distance = fromCentre.Length();
        Vector2 outward = fromCentre / distance;

        // Clockwise seen from above, the way a right turn goes; in (x, z), the right of a heading (x, z) is (-z, x).
        var along = new Vector2(-outward.Y, outward.X);
        Vector2 heading = new Vector2(Forward.X, Forward.Z).Normalized();
        var rightOfHeading = new Vector2(-heading.Y, heading.X);
        float headingError = Mathf.RadToDeg(MathF.Atan2(along.Dot(rightOfHeading), along.Dot(heading)));
        float wide = distance - SweepRadius;

        // A Stanley tracker: the circle's own steer, the heading error, and the distance off it over the speed.
        float v = ForwardSpeed;
        float steerDeg = Mathf.RadToDeg(MathF.Atan(Car.Spec.WheelbaseM / SweepRadius))
            + headingError + Mathf.RadToDeg(MathF.Atan(3.0f * wide / MathF.Max(v, 5.0f)));
        float steer = Math.Clamp(steerDeg / MathF.Max(Car.SteerLimitDeg, 1.0f), -1.0f, 1.0f);

        // A throttle held in proportion to the shortfall settles a little short of its target, so the climb
        // starts within a couple of metres a second of the first target, not at it.
        if (_sweepRampFrom is null && v >= SweepFrom - 2.0f)
        {
            _sweepRampFrom = Time;
        }

        float target = _sweepRampFrom is float from ? SweepFrom + (SweepRamp * (Time - from)) : SweepFrom;
        Car.Driver = new DriverInput { Throttle = Math.Clamp((target - v) * 0.6f, 0.0f, 1.0f), Steer = steer };

        // The grip: the path's own sideways acceleration (the change in velocity across the direction of
        // travel, not speed times yaw rate, which a car starting to spin inflates), averaged over half a
        // second, its best and the speed and downforce it came at. Off the circle by a metre or two it still
        // counts: a car held in a slide sits a little wide, its nose turned in, and that's the tyres, not
        // the tracker.
        var travel = new Vector3(Car.LinearVelocity.X, 0.0f, Car.LinearVelocity.Z);
        var accel = new Vector3(Car.Acceleration.X, 0.0f, Car.Acceleration.Z);
        Vector3 along3 = travel.LengthSquared() > 1.0f ? travel.Normalized() : Forward;
        _sweepLateral.Enqueue((accel - (along3 * accel.Dot(along3))).Length());
        if (_sweepLateral.Count > Engine.PhysicsTicksPerSecond / 2)
        {
            _sweepLateral.Dequeue();
        }

        float lateral = _sweepLateral.Average() / WheelModel.Gravity;
        if (_sweepRampFrom is not null && lateral > _sweepBestG)
        {
            _sweepBestG = lateral;
            _sweepBest = v;
            _sweepBestDownforce = Car.WingDownforce;
        }

        _sweepShort = _sweepRampFrom is not null && target - v > 3.0f ? _sweepShort + dt : 0.0f;

        string held = $"on a {SweepRadius:F0} m circle: max sideways {_sweepBestG:F2} g at {_sweepBest * 3.6f:F0} km/h, "
            + $"wing pressing {_sweepBestDownforce:F0} N";
        Vector3 up = Car.GlobalBasis.Y.Normalized();
        float bodySlip = Mathf.RadToDeg(MathF.Atan2(Car.LinearVelocity.Dot(Car.GlobalBasis.X.Normalized()), MathF.Max(v, 0.1f)));

        if (up.Dot(Vector3.Up) < 0.5f)
        {
            Finish($"{held} | ROLLED at {v * 3.6f:F0} km/h");
        }
        else if (MathF.Abs(bodySlip) > 20.0f)
        {
            Finish($"{held} | spun at {v * 3.6f:F0} km/h (body slip {bodySlip:F0}°)");
        }
        else if (wide > 4.0f)
        {
            Finish($"{held} | ran wide at {v * 3.6f:F0} km/h");
        }
        else if (_sweepShort > 5.0f)
        {
            Finish($"{held} | out of power at {v * 3.6f:F0} km/h, still on the circle");
        }
        else if (Time > 240.0f)
        {
            Finish($"{held} | still on the circle at {v * 3.6f:F0} km/h after 240 s");
        }
    }

    /// <summary>
    /// Three runs from rest, each up to 60 km/h first: a stop on the handbrake alone; two seconds at full
    /// lock with the throttle off; and the same with the handbrake pulled for the first half second. The
    /// last against the second is what the handbrake does to a turn.
    /// </summary>
    private void RunHandbrake()
    {
        if (_tick == 1)
        {
            _home = Car.GlobalTransform;
        }

        if (Time - _runFrom < SettleSeconds)
        {
            Car.Driver = default;
            return;
        }

        float v = ForwardSpeed;
        if (_manoeuvreFrom is null)
        {
            // Up to speed on full throttle, then held there a second so the pitch has settled.
            if (_upToSpeedAt is null)
            {
                Car.Driver = new DriverInput { Throttle = 1.0f };
                _upToSpeedAt = v >= HandbrakeEntry ? Time : null;
                return;
            }

            Car.Driver = new DriverInput { Throttle = Math.Clamp((HandbrakeEntry - v) * 0.5f, 0.0f, 1.0f) };
            if (Time - _upToSpeedAt.Value >= 1.0f)
            {
                _manoeuvreFrom = Time;
                _entry = v;
                _start = Car.GlobalPosition;
                _brakeHeading = Forward;
                _turned = 0.0f;
                _peakSlip = 0.0f;
            }

            return;
        }

        float since = Time - _manoeuvreFrom.Value;
        Vector3 up = Car.GlobalBasis.Y.Normalized();
        Vector3 right = Car.GlobalBasis.X.Normalized();
        Vector3 velocity = Car.LinearVelocity;

        // Godot's yaw is anticlockwise from above, so a right turn is negative; counted positive here.
        _turned -= Car.AngularVelocity.Dot(up) / Engine.PhysicsTicksPerSecond;
        if (velocity.Length() > 2.0f)
        {
            _peakSlip = MathF.Max(_peakSlip, MathF.Abs(Mathf.RadToDeg(MathF.Atan2(velocity.Dot(right), v))));
        }

        switch (_run)
        {
            case HandbrakeRun.Stop:
                Car.Driver = new DriverInput { Handbrake = true };
                if (velocity.Length() <= 0.3f || since > 15.0f)
                {
                    Vector3 travelled = Car.GlobalPosition - _start;
                    float distance = new Vector2(travelled.X, travelled.Z).Length();
                    float mean = _entry * _entry / (2.0f * distance * WheelModel.Gravity);
                    float moved = Mathf.RadToDeg(_brakeHeading.AngleTo(Forward));
                    _stopFigures = $"stop from {_entry * 3.6f:F0} km/h on the handbrake alone: {distance:F1} m, "
                        + $"{since:F2} s, mean {mean:F2} g, heading moved {moved:F1}°";
                    NextHandbrakeRun();
                }

                break;

            case HandbrakeRun.SteeringAlone:
                Car.Driver = new DriverInput { Steer = 1.0f };
                if (since >= TurnSeconds)
                {
                    _steerFigures = $"{Mathf.RadToDeg(_turned):F0}° on steering alone (out at {velocity.Length() * 3.6f:F0} km/h, "
                        + $"body slip peaked {_peakSlip:F0}°)";
                    NextHandbrakeRun();
                }

                break;

            case HandbrakeRun.WithTheHandbrake:
                Car.Driver = new DriverInput { Steer = 1.0f, Handbrake = since < PullSeconds };
                if (since >= TurnSeconds)
                {
                    Finish(
                        $"{_stopFigures} | full lock from {_entry * 3.6f:F0} km/h, throttle off, {TurnSeconds:F0} s: "
                        + $"turned {_steerFigures}; {Mathf.RadToDeg(_turned):F0}° with the handbrake for the first {PullSeconds:F1} s "
                        + $"(out at {velocity.Length() * 3.6f:F0} km/h, body slip peaked {_peakSlip:F0}°)");
                }

                break;
        }
    }

    private void NextHandbrakeRun()
    {
        _run++;
        _runFrom = Time;
        _upToSpeedAt = null;
        _manoeuvreFrom = null;
        Car.Driver = default;
        Car.Respawn(_home);
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
