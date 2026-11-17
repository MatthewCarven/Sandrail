using System;
using Godot;
using Sandrail.Core.Pieces;
using Sandrail.Game.Cars;
using Sandrail.Game.World;

namespace Sandrail.Game.Oracles;

/// <summary>
/// The oracles that drive M1c's test ground: the jump, the loop and the bumps. Each holds a speed with
/// a simple throttle-and-brake controller, keeps the wheels straight, and reports what happened.
/// </summary>
/// <remarks>
/// <para>
/// The speeds are the ones each station's hint gives, so a figure here is what a driver doing as told
/// would get: 86 km/h at the jump's lip, 97 km/h into the loop, 72 km/h over the bumps.
/// </para>
/// <para>
/// <b>The loop needs no steering</b>, because it's built so that straight wheels follow it (see
/// <see cref="LoopCentreline"/>). How far off its exit road the car comes out says how true that is.
/// </para>
/// </remarks>
public partial class Oracle
{
    public const string Jump = "jump";
    public const string Loop = "loop";
    public const string Bumps = "bumps";

    private const float JumpSpeed = 24.0f;
    private const float LoopSpeed = 27.0f;
    private const float BumpSpeed = 20.0f;

    // jump
    private float? _launchedAt;
    private float _launchSpeed;
    private float? _airborneAt;
    private float? _landedAt;
    private float _landedPast;
    private float _landingPitch;
    private float _landingDeepest;

    // loop
    private float? _footAt;
    private float _entrySpeed;
    private float _slowestOverTop = float.MaxValue;
    private int _stopTicks;
    private int _airTicks;
    private int _longestAir;
    private int _airRun;

    // bumps
    private int _insideTicks;
    private int _oneOffTicks;
    private int _allOffTicks;
    private float _worstRoll;
    private float _worstPitch;
    private float _speedSum;

    /// <summary>The test ground the ground oracles drive; null for the four on the bare plain.</summary>
    public TestGroundLayout? Layout { get; init; }

    public static bool NeedsTestGround(string kind) => kind is Jump or Loop or Bumps;

    /// <summary>The station a ground oracle starts from.</summary>
    public static int StationFor(string kind) => kind switch
    {
        Loop => 4,
        Bumps => 5,
        _ => 1,
    };

    private float BumpStopStart => Car.Spec.SuspensionTravelM * (1.0f - Car.Spec.BumpStopFraction);

    private float Deepest()
    {
        float deepest = 0.0f;
        for (int i = 0; i < 4; i++)
        {
            deepest = MathF.Max(deepest, Car.Compression(i));
        }

        return deepest;
    }

    private DriverInput Hold(float target)
    {
        float v = ForwardSpeed;
        return new DriverInput
        {
            Throttle = Math.Clamp((target - v) * 0.5f, 0.0f, 1.0f),
            Brake = Math.Clamp((v - target) * 0.5f, 0.0f, 1.0f),
        };
    }

    /// <summary>How far past a point, along a direction flattened onto the ground.</summary>
    private float Past(System.Numerics.Vector3 point, System.Numerics.Vector3 direction)
    {
        Vector3 flat = new Vector3(direction.X, 0.0f, direction.Z).Normalized();
        Vector3 offset = Car.GlobalPosition - point.ToGodot();
        return new Vector3(offset.X, 0.0f, offset.Z).Dot(flat);
    }

    private void RunJump()
    {
        TestGroundLayout layout = Layout!;
        float past = Past(layout.Lip.Position, layout.Lip.Forward);

        if (_launchedAt is null)
        {
            Car.Driver = Hold(JumpSpeed);
            if (past >= 0.0f)
            {
                _launchedAt = Time;
                _launchSpeed = Car.LinearVelocity.Length();
            }
            else if (Time > 40.0f)
            {
                Finish("never reached the lip");
            }

            return;
        }

        Car.Driver = default;
        if (_airborneAt is null && Car.GroundedCount == 0)
        {
            _airborneAt = Time;
        }

        if (_airborneAt is not null && _landedAt is null && Car.GroundedCount >= 2)
        {
            _landedAt = Time;
            _landedPast = past;
            _landingPitch = CarReadout.Of(Car).PitchDeg;
        }

        if (_landedAt is float landed)
        {
            if (Time - landed < 0.6f)
            {
                _landingDeepest = MathF.Max(_landingDeepest, Deepest());
            }

            if (Time - landed >= 3.0f)
            {
                string where = _landedPast <= layout.TableLengthM ? "on the table"
                    : _landedPast <= layout.DownRampEndM ? "on the down-ramp"
                    : "on the flat beyond it";
                bool upright = Car.GlobalBasis.Y.Normalized().Y > 0.7f;
                Finish(
                    $"off the lip at {_launchSpeed * 3.6f:F0} km/h: {landed - _airborneAt!.Value:F2} s in the air, landed "
                    + $"{_landedPast:F1} m past the lip, {where} (the down-ramp is {layout.TableLengthM:F0}-{layout.DownRampEndM:F0} m), "
                    + $"pitch {_landingPitch:F0}° | deepest compression {_landingDeepest:F3} of {Car.Spec.SuspensionTravelM:F2} m"
                    + $"{(_landingDeepest > BumpStopStart ? " (on the bump stop)" : string.Empty)} | {(upright ? "upright" : "NOT UPRIGHT")}");
            }
        }
        else if (Time - _launchedAt.Value > 10.0f)
        {
            Finish("left the lip and never landed on two wheels");
        }
    }

    private void RunLoop()
    {
        TestGroundLayout layout = Layout!;
        float past = Past(layout.LoopEntry.Position, layout.LoopEntry.Forward);

        // Where the loop comes back down, measured along the run-up the same way as the car is.
        System.Numerics.Vector3 toExit = layout.LoopExit.Position - layout.LoopEntry.Position;
        float exitAt = new Vector3(toExit.X, 0.0f, toExit.Z)
            .Dot(new Vector3(layout.LoopEntry.Forward.X, 0.0f, layout.LoopEntry.Forward.Z).Normalized());
        Car.Driver = Hold(LoopSpeed);

        if (_footAt is null)
        {
            if (past >= 0.0f)
            {
                _footAt = Time;
                _entrySpeed = Car.LinearVelocity.Length();
            }
            else if (Time > 40.0f)
            {
                Finish("never reached the loop");
            }

            return;
        }

        float deepest = Deepest();
        _deepest = MathF.Max(_deepest, deepest);
        if (deepest > BumpStopStart)
        {
            _stopTicks++;
        }

        if (Car.GroundedCount == 0)
        {
            _airTicks++;
            _airRun++;
            _longestAir = Math.Max(_longestAir, _airRun);
        }
        else
        {
            _airRun = 0;
        }

        Vector3 up = Car.GlobalBasis.Y.Normalized();
        if (up.Y < -0.5f)
        {
            _slowestOverTop = MathF.Min(_slowestOverTop, Car.LinearVelocity.Length());
        }

        int tps = Engine.PhysicsTicksPerSecond;
        string stress = $"deepest compression {_deepest:F3} of {Car.Spec.SuspensionTravelM:F2} m, "
            + $"{_stopTicks / (float)tps:F2} s on the bump stop | all four wheels off the road {_airTicks / (float)tps:F2} s";

        if (past > exitAt + 10.0f && Car.GlobalPosition.Y < 1.5f && up.Y > 0.8f)
        {
            Vector3 d = new Vector3(layout.LoopEntry.Forward.X, 0.0f, layout.LoopEntry.Forward.Z).Normalized();
            var across = new Vector3(-d.Z, 0.0f, d.X);
            float off = (Car.GlobalPosition - layout.LoopExit.Position.ToGodot()).Dot(across);
            string top = _slowestOverTop < float.MaxValue ? $"{_slowestOverTop * 3.6f:F0} km/h" : "never upside down";
            Finish($"in at {_entrySpeed * 3.6f:F0} km/h: THROUGH, {off:+0.0;-0.0} m off the exit road's centre | slowest over the top {top} | {stress}");
        }
        else if (_longestAir > tps)
        {
            Finish($"in at {_entrySpeed * 3.6f:F0} km/h: FELL OFF, a second with no wheel on the road, {Car.GlobalPosition.Y:F1} m up | {stress}");
        }
        else if (Time - _footAt.Value > 20.0f)
        {
            Finish($"in at {_entrySpeed * 3.6f:F0} km/h: never came out | {stress}");
        }
    }

    private void RunBumps()
    {
        TestGroundLayout layout = Layout!;
        Car.Driver = Hold(BumpSpeed);
        Vector3 at = Car.GlobalPosition;
        float half = layout.Bumps.SizeM / 2.0f;
        bool inside = MathF.Abs(at.X - layout.BumpCentre.X) < half && MathF.Abs(at.Z - layout.BumpCentre.Z) < half;

        if (inside)
        {
            _insideTicks++;
            _speedSum += Car.LinearVelocity.Length();
            float deepest = Deepest();
            _deepest = MathF.Max(_deepest, deepest);
            if (deepest > BumpStopStart)
            {
                _stopTicks++;
            }

            if (Car.GroundedCount < 4)
            {
                _oneOffTicks++;
            }

            if (Car.GroundedCount == 0)
            {
                _allOffTicks++;
                _airRun++;
                _longestAir = Math.Max(_longestAir, _airRun);
            }
            else
            {
                _airRun = 0;
            }

            CarReadout r = CarReadout.Of(Car);
            _worstRoll = MathF.Max(_worstRoll, MathF.Abs(r.RollDeg));
            _worstPitch = MathF.Max(_worstPitch, MathF.Abs(r.PitchDeg));
        }

        bool rolled = Car.GlobalBasis.Y.Normalized().Y < 0.3f;
        bool through = at.Z < layout.BumpCentre.Z - half - 5.0f;
        if (!(rolled || through || Time > 40.0f))
        {
            return;
        }

        if (_insideTicks == 0)
        {
            Finish("never reached the bumps");
            return;
        }

        float n = _insideTicks;
        int tps = Engine.PhysicsTicksPerSecond;
        Finish(
            $"{(through ? "through" : rolled ? "ROLLED in" : "stuck in")} the patch at {_speedSum / n * 3.6f:F0} km/h on average: "
            + $"a wheel off the ground {100.0f * _oneOffTicks / n:F0}% of the time, all four {100.0f * _allOffTicks / n:F0}% "
            + $"(longest {_longestAir / (float)tps:F2} s) | deepest compression {_deepest:F3} of {Car.Spec.SuspensionTravelM:F2} m, "
            + $"on the bump stop {100.0f * _stopTicks / n:F0}% | worst roll {_worstRoll:F0}°, pitch {_worstPitch:F0}°");
    }
}
