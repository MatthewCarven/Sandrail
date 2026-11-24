using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sandrail.Core.Driving;
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
    public const string Lap = "lap";
    public const string Air = "air";

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

    /// <summary>The share of grip the lap oracle's autodriver plans for (<c>--caution=</c>).</summary>
    public float Caution { get; init; } = Autodriver.DefaultCaution;

    private string? _firstSpin;

    public static bool NeedsTestGround(string kind) => kind is Jump or Loop or Bumps or Lap or Air;

    /// <summary>The station a ground oracle starts from.</summary>
    public static int StationFor(string kind) => kind switch
    {
        Loop => 4,
        Bumps => 5,
        Lap => 6,
        _ => 1,
    };

    // lap
    private RacingLine? _lapLine;
    private Autodriver? _autodriver;
    private float[] _planned = [];
    private LapTimer? _lapTimer;
    private readonly List<float> _lapTimes = new();
    private bool _allCounted = true;
    private readonly float[] _cornerSlowest = new float[TestGroundLayout.Corners.Length];
    private readonly float[] _cornerPlanned = new float[TestGroundLayout.Corners.Length];
    private int _offRoadTicks;
    private float _worstFromLine;
    private float _worstSlip;
    private int _spins;
    private bool _spinning;

    /// <summary>
    /// The autodriver laps the corner lap twice from station 6: a standing lap, then a flying one, which is
    /// the one measured. The figures are what a tune buys: lap time, and where it's won or lost.
    /// </summary>
    private void RunLap()
    {
        TestGroundLayout layout = Layout!;
        if (_autodriver is null)
        {
            RoadRun lap = layout.Runs.Single(r => r.Name == "corners");
            _lapLine = TestGround.CornerLapLine(layout, Car.Spec);
            _autodriver = new Autodriver(_lapLine, Car.Spec, Caution);
            _planned = Enumerable.Range(0, _lapLine.Count).Select(_autodriver.SpeedAt).ToArray();
            Array.Fill(_cornerSlowest, float.MaxValue);
            Array.Fill(_cornerPlanned, float.MaxValue);
            for (int i = 0; i < _lapLine.Count; i++)
            {
                if (_lapLine.PieceAt(i) % 2 == 1)
                {
                    int corner = _lapLine.PieceAt(i) / 2;
                    _cornerPlanned[corner] = MathF.Min(_cornerPlanned[corner], _planned[i]);
                }
            }

            _roadHalf = lap.Profile.SurfaceWidth / 2.0f;

            // Timed as the game times him: the clock starts at the first throttle, and a lap ends where
            // it crosses the line, interpolated between ticks (since 2026-11-23; before, at the tick the
            // nearest sample wrapped round, which put both a few hundredths early).
            _lapTimer = new LapTimer(new RacingLine(lap.Pieces, closed: true));
            _lapTimer.StandingStart();
        }

        RacingLine line = _lapLine!;
        Basis basis = Car.GlobalBasis;
        Vector3 forward = (-basis.Z).Normalized();
        Vector3 up = basis.Y.Normalized();
        DriveCommand cmd = _autodriver.Drive(
            Car.GlobalPosition.ToCore(), forward.ToCore(), up.ToCore(), Car.LinearVelocity.ToCore(), -Car.AngularVelocity.Dot(up));
        Car.Driver = new DriverInput { Throttle = cmd.Throttle, Brake = cmd.Brake, Steer = cmd.Steer };

        int index = _autodriver.Index;
        LapEvent crossing = _lapTimer!.Update(Car.GlobalPosition.ToCore(), Time);
        if (_lapTimer.Armed && cmd.Throttle > 0.0f)
        {
            _lapTimer.Go(Time);
        }

        if (crossing.Kind == LapEventKind.Finished)
        {
            _lapTimes.Add((float)crossing.Seconds);
            _allCounted &= crossing.Counted;
        }

        // Below 5 m/s a slide angle means nothing: pulling away with the wheels turned reads as 30° at walking pace.
        bool moving = Car.LinearVelocity.Length() > 5.0f;
        float slide = moving ? MathF.Abs(CarReadout.Of(Car).BodySlipDeg) : 0.0f;
        if (_firstSpin is null && slide > 30.0f)
        {
            int at = line.PieceAt(index);
            _firstSpin = $"first spin on lap {_lapTimes.Count + 1}, {(at % 2 == 1 ? $"in corner {(at / 2) + 1}" : $"on the straight before corner {((at / 2) % TestGroundLayout.Corners.Length) + 1}")}, "
                + $"at {Car.LinearVelocity.Length() * 3.6f:F0} km/h with throttle {Car.Throttle:F2} and brake {Car.Brake:F2}";
        }

        // Measured on the flying lap only.
        if (_lapTimes.Count == 1)
        {
            float speed = Car.LinearVelocity.Length();
            int piece = line.PieceAt(index);
            if (piece % 2 == 1)
            {
                _cornerSlowest[piece / 2] = MathF.Min(_cornerSlowest[piece / 2], speed);
            }

            float carHalf = (Car.Spec.TrackM / 2.0f) + 0.15f;
            if (MathF.Abs(line.OffsetFromMiddle(index, Car.GlobalPosition.ToCore())) > _roadHalf - carHalf)
            {
                _offRoadTicks++;
                _offRoadByPiece[piece]++;
            }

            if (MathF.Abs(_autodriver.Lateral) > _worstFromLine)
            {
                _worstFromLine = MathF.Abs(_autodriver.Lateral);
                _worstFromLinePiece = piece;
            }

            _worstSlip = MathF.Max(_worstSlip, slide);
            bool spinning = slide > 30.0f;
            if (spinning && !_spinning)
            {
                _spins++;
            }

            _spinning = spinning;
        }

        if (_lapTimes.Count >= 2)
        {
            string corners = string.Join(", ", Enumerable.Range(0, _cornerSlowest.Length)
                .Select(c => $"{c + 1}: {_cornerSlowest[c] * 3.6f:F0}/{_cornerPlanned[c] * 3.6f:F0}"));
            float tps = GameClock.TicksPerSecond;
            string offWhere = string.Join(", ", Enumerable.Range(0, _offRoadByPiece.Length)
                .Where(p => _offRoadByPiece[p] > 0)
                .Select(p => $"{PieceName(p)} {_offRoadByPiece[p] / tps:F2} s"));
            Finish(
                $"caution {Caution:F2}: standing lap {_lapTimes[0]:F2} s, flying lap {_lapTimes[1]:F2} s{(_allCounted ? string.Empty : " (NOT ALL COUNTED: it cut or left the lap)")} | slowest in each corner, km/h driven/planned: {corners} | "
                + $"wheels off the road {_offRoadTicks / tps:F2} s{(offWhere.Length > 0 ? $" ({offWhere})" : string.Empty)}, "
                + $"widest off the line {_worstFromLine:F2} m ({PieceName(_worstFromLinePiece)}), "
                + $"worst slide {_worstSlip:F0}°, spins {_spins}{(_firstSpin is null ? string.Empty : $" | {_firstSpin}")}");
        }
        else if (Time > 150.0f)
        {
            Finish($"didn't finish two laps in 150 s: {_lapTimes.Count} done, stuck {line.DistanceAt(index):F0} m round, {Car.LinearVelocity.Length() * 3.6f:F0} km/h");
        }
    }

    private float _roadHalf;
    private readonly int[] _offRoadByPiece = new int[2 * TestGroundLayout.Corners.Length];
    private int _worstFromLinePiece;

    /// <summary>A lap piece in words: odd pieces are corners, even ones the straights before them.</summary>
    private static string PieceName(int piece) =>
        piece % 2 == 1 ? $"corner {(piece / 2) + 1}" : $"into corner {(piece / 2) + 1}";

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

    // air: the jump five times from station 1, each with one input from the lip to the landing
    private enum AirRun { Coasting, Throttle, Brake, BrakeTap, Steer }

    private AirRun _airTry;
    private Transform3D _airHome;
    private float _airRunFrom;
    private float? _airLipAt;
    private float? _airUpAt;
    private Vector3 _airLipHeading;
    private float _airLipSpeed;
    private readonly List<string> _airFigures = new();

    /// <summary>
    /// Air control's oracle (M1d): station 1's jump at its hinted speed, five times: holding nothing; full
    /// throttle, full brake, and full right steering from the lip until a wheel touches; and a 0.3 s tap
    /// of brake off the lip, which is what a player would do. It reports the car's attitude at that first
    /// touch. The first is the jump's own landing; the rest are what a player can do to it. (At the first touch, not two wheels down as the jump oracle waits for: a car landing
    /// rolled has rocked back level by the time its second wheel is down.)
    /// </summary>
    private void RunAir()
    {
        TestGroundLayout layout = Layout!;
        if (_tick == 1)
        {
            _airHome = Car.GlobalTransform;
        }

        if (Time - _airRunFrom < SettleSeconds)
        {
            Car.Driver = default;
            return;
        }

        if (_airLipAt is null)
        {
            Car.Driver = Hold(JumpSpeed);
            if (Past(layout.Lip.Position, layout.Lip.Forward) >= 0.0f)
            {
                _airLipAt = Time;
                _airLipHeading = FlatForward();
                _airLipSpeed = Car.LinearVelocity.Length();
            }
            else if (Time - _airRunFrom > 40.0f)
            {
                Finish($"{_airTry}: never reached the lip");
            }

            return;
        }

        Car.Driver = _airTry switch
        {
            AirRun.Throttle => new DriverInput { Throttle = 1.0f },
            AirRun.Brake => new DriverInput { Brake = 1.0f },
            AirRun.BrakeTap => new DriverInput { Brake = Time - _airLipAt.Value < 0.3f ? 1.0f : 0.0f },
            AirRun.Steer => new DriverInput { Steer = 1.0f },
            _ => default,
        };

        if (_airUpAt is null && Car.GroundedCount == 0)
        {
            _airUpAt = Time;
        }

        if (_airUpAt is float up && Car.GroundedCount >= 1)
        {
            CarReadout r = CarReadout.Of(Car);
            Vector3 now = FlatForward();
            float turned = -Mathf.RadToDeg(MathF.Atan2(_airLipHeading.Cross(now).Y, _airLipHeading.Dot(now)));
            _airFigures.Add(_airTry switch
            {
                AirRun.Coasting => $"coasting, {Time - up:F2} s up, touched down at pitch {r.PitchDeg:F0}°",
                AirRun.Throttle => $"throttle held: pitch {r.PitchDeg:F0}°",
                AirRun.Brake => $"brake held: pitch {r.PitchDeg:F0}°",
                AirRun.BrakeTap => $"brake tapped for 0.3 s: pitch {r.PitchDeg:F0}°",
                _ => $"full right steer held: turned {turned:F0}°, rolled {r.RollDeg:F0}° right side down, pitch {r.PitchDeg:F0}°",
            });

            if (_airTry == AirRun.Steer)
            {
                Finish($"off the lip at {_airLipSpeed * 3.6f:F0} km/h: {string.Join("; ", _airFigures)}");
                return;
            }

            _airTry++;
            _airLipAt = null;
            _airUpAt = null;
            _airRunFrom = Time;
            Car.Respawn(_airHome);
        }
        else if (Time - _airLipAt.Value > 10.0f)
        {
            Finish($"{_airTry}: left the lip and never touched down");
        }
    }

    private Vector3 FlatForward()
    {
        Vector3 f = -Car.GlobalBasis.Z;
        return new Vector3(f.X, 0.0f, f.Z).Normalized();
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

        int tps = GameClock.TicksPerSecond;
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
        int tps = GameClock.TicksPerSecond;
        Finish(
            $"{(through ? "through" : rolled ? "ROLLED in" : "stuck in")} the patch at {_speedSum / n * 3.6f:F0} km/h on average: "
            + $"a wheel off the ground {100.0f * _oneOffTicks / n:F0}% of the time, all four {100.0f * _allOffTicks / n:F0}% "
            + $"(longest {_longestAir / (float)tps:F2} s) | deepest compression {_deepest:F3} of {Car.Spec.SuspensionTravelM:F2} m, "
            + $"on the bump stop {100.0f * _stopTicks / n:F0}% | worst roll {_worstRoll:F0}°, pitch {_worstPitch:F0}°");
    }
}
