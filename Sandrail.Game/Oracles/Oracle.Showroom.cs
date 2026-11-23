using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sandrail.Core.Driving;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;
using Sandrail.Game.Cars;
using Sandrail.Game.World;

namespace Sandrail.Game.Oracles;

/// <summary>
/// M2b's oracle: the starter set's stunt pieces driven in the showroom, each from its run-up at a set
/// speed, as M1c's oracles drove the test ground (M2-PLAN, M2b).
/// </summary>
/// <remarks>
/// <para>
/// Each trial puts the car at its piece's run-up already moving, steers it along the piece's road with a
/// simple tracker (wheels straight in the loop, and nothing at all in the air, where steering and the
/// pedals would be air control), and holds the speed with throttle and brake until the stunt.
/// </para>
/// <para>
/// <b>The loop</b> at its hint, held through: through or not, how far off the road it comes out, and its
/// slowest over the top. <b>Each jump</b> at both ends of its hint and the middle, coasting from the lip:
/// where it lands on the landing ramp. <b>The slope</b> at 130 and 160 km/h, either side of the 143 km/h
/// its crest radius says the car leaves the ground at (M2-PLAN fact 3): how long it's in the air.
/// </para>
/// </remarks>
public partial class Oracle
{
    public const string Pieces = "pieces";

    /// <summary>The showroom the pieces oracle drives; null elsewhere.</summary>
    public ShowroomLayout? Showroom { get; init; }

    public static bool NeedsShowroom(string kind) => kind is Pieces;

    private enum TrialKind { Loop, Jump, Slope }

    private sealed record PieceTrial(string Piece, float Kmh, TrialKind Kind);

    private static readonly PieceTrial[] Trials =
    [
        new("base:loop", 90.0f, TrialKind.Loop),
        new("base:loop", 97.0f, TrialKind.Loop),
        new("base:loop", 105.0f, TrialKind.Loop),
        new("base:launch-short", 85.0f, TrialKind.Jump),
        new("base:launch-short", 90.0f, TrialKind.Jump),
        new("base:launch-short", 95.0f, TrialKind.Jump),
        new("base:launch-long", 110.0f, TrialKind.Jump),
        new("base:launch-long", 115.0f, TrialKind.Jump),
        new("base:launch-long", 120.0f, TrialKind.Jump),
        new("base:slope-up", 100.0f, TrialKind.Slope),
        new("base:slope-up", 110.0f, TrialKind.Slope),
        new("base:slope-up", 120.0f, TrialKind.Slope),
        new("base:slope-up", 130.0f, TrialKind.Slope),
        new("base:slope-up", 160.0f, TrialKind.Slope),
    ];

    private int _trial = -1;
    private float _trialFrom;
    private RacingLine? _trialLine;
    private List<ICentreline> _trialRoad = new();
    private int _trialHint = -1;
    private PlacedPiece? _trialPiece;
    private float _trialMainStart;
    private float _trialMainEnd;
    private float? _trialInAt;
    private float _trialInSpeed;
    private float? _trialUpAt;
    private float _trialSlowestTop;
    private int _trialAirTicks;
    private int _trialAirRun;
    private float _trialLandedPast;
    private float _trialLandedPitch;
    private float? _trialLandedAt;
    private readonly List<string> _trialFigures = new();

    private void RunPieces()
    {
        ShowroomLayout room = Showroom!;
        if (_trial < 0 || _trialLine is null)
        {
            StartTrial(room, 0);
            return;
        }

        PieceTrial trial = Trials[_trial];
        if (Time - _trialFrom < 0.05f)
        {
            Car.Driver = default;
            return;
        }

        RacingLine line = _trialLine;
        Vector3 up = Car.GlobalBasis.Y.Normalized();
        bool inTheAir = Car.GroundedCount == 0;
        float along = line.DistanceAt(_trialHint < 0 ? 0 : _trialHint);

        // Steer the road, but wheels straight in the loop and hands off in the air.
        float steer = inTheAir ? 0.0f : Track(line, ref _trialHint);
        bool inLoop = _trialRoad[line.PieceAt(Math.Max(_trialHint, 0))] is LoopCentreline;
        if (inLoop)
        {
            steer = 0.0f;
        }

        _trialAirRun = inTheAir ? _trialAirRun + 1 : 0;
        if (inTheAir && along >= _trialMainStart - 1.0f)
        {
            _trialAirTicks++;
        }

        switch (trial.Kind)
        {
            case TrialKind.Loop:
                RunLoopTrial(trial, steer, along, up);
                break;
            case TrialKind.Jump:
                RunJumpTrial(trial, steer, along, inTheAir);
                break;
            case TrialKind.Slope:
                RunSlopeTrial(trial, steer, along);
                break;
        }

        if (_trialFigures.Count > _trial)
        {
            if (_trial + 1 < Trials.Length)
            {
                StartTrial(room, _trial + 1);
            }
            else
            {
                Finish(string.Join("; ", _trialFigures));
            }
        }
        else if (Time - _trialFrom > 30.0f)
        {
            _trialFigures.Add($"{TrialName(trial)} at {trial.Kmh:F0} km/h: NEVER FINISHED, {along:F0} m along");
        }
    }

    private void RunLoopTrial(PieceTrial trial, float steer, float along, Vector3 up)
    {
        DriverInput hold = Hold(trial.Kmh / 3.6f);
        Car.Driver = hold with { Steer = steer };

        if (_trialInAt is null && along >= _trialMainStart)
        {
            _trialInAt = Time;
            _trialInSpeed = Car.LinearVelocity.Length();
        }

        if (up.Y < -0.5f)
        {
            _trialSlowestTop = MathF.Min(_trialSlowestTop, Car.LinearVelocity.Length());
        }

        int tps = Engine.PhysicsTicksPerSecond;
        if (_trialInAt is not null && along > _trialMainEnd + 10.0f && up.Y > 0.8f && Car.GroundedCount >= 2)
        {
            float off = _trialLine!.LateralOffset(_trialHint, Car.GlobalPosition.ToCore());
            string top = _trialSlowestTop < float.MaxValue ? $"{_trialSlowestTop * 3.6f:F0} km/h" : "never upside down";
            _trialFigures.Add($"loop: in at {_trialInSpeed * 3.6f:F0} km/h, THROUGH, {off:+0.0;-0.0} m off its exit road, slowest over the top {top}");
        }
        else if (_trialAirRun > tps)
        {
            _trialFigures.Add($"loop: in at {_trialInSpeed * 3.6f:F0} km/h, FELL OFF, {Car.GlobalPosition.Y:F1} m up");
        }
    }

    private void RunJumpTrial(PieceTrial trial, float steer, float along, bool inTheAir)
    {
        WorldConnector lip = _trialPiece!.Out;
        Frame lipFrame = lip.Frame;
        float past = Past(lipFrame.Position, lipFrame.Forward);

        if (_trialInAt is null)
        {
            Car.Driver = Hold(trial.Kmh / 3.6f) with { Steer = steer };
            if (past >= 0.0f)
            {
                _trialInAt = Time;
                _trialInSpeed = Car.LinearVelocity.Length();
            }

            return;
        }

        Car.Driver = default;
        if (_trialUpAt is null && inTheAir)
        {
            _trialUpAt = Time;
        }

        if (_trialUpAt is not null && _trialLandedAt is null && Car.GroundedCount >= 2)
        {
            _trialLandedAt = Time;
            _trialLandedPast = past;
            _trialLandedPitch = CarReadout.Of(Car).PitchDeg;
        }

        if (_trialLandedAt is float landed && Time - landed > 2.0f)
        {
            // The landing ramp begins across the gap; its table, crest, down-ramp and foot in metres from there.
            float into = _trialLandedPast - (lip.Gap * TrackGrid.TileM);
            string where = into < 0.0f ? "SHORT, in the gap"
                : into < 10.0f ? "on the landing's table"
                : into < 14.2f ? "on its crest"
                : into < 27.1f ? "on its down-ramp"
                : into < 32.7f ? "on its foot"
                : "past it, on the run-out";
            bool upright = Car.GlobalBasis.Y.Normalized().Y > 0.7f;
            _trialFigures.Add($"{TrialName(trial)} at {_trialInSpeed * 3.6f:F0} km/h: lands {_trialLandedPast:F1} m past the lip, {where}, "
                + $"pitch {_trialLandedPitch:F0}°{(upright ? string.Empty : ", NOT UPRIGHT")}");
        }
    }

    private void RunSlopeTrial(PieceTrial trial, float steer, float along)
    {
        Car.Driver = Hold(trial.Kmh / 3.6f) with { Steer = steer };
        if (_trialInAt is null && along >= _trialMainStart)
        {
            _trialInAt = Time;
            _trialInSpeed = Car.LinearVelocity.Length();
        }

        if (_trialInAt is not null && along > _trialMainEnd + 20.0f)
        {
            float air = _trialAirTicks / (float)Engine.PhysicsTicksPerSecond;
            _trialFigures.Add($"slope at {_trialInSpeed * 3.6f:F0} km/h: {(air > 0.0f ? $"{air:F2} s in the air" : "wheels down")}");
        }
    }

    private void StartTrial(ShowroomLayout room, int index)
    {
        _trial = index;
        PieceTrial trial = Trials[index];
        IReadOnlyList<PlacedPiece> group = room.Groups.First(g => g.Any(p => p.Def.Id == trial.Piece));
        _trialPiece = group.First(p => p.Def.Id == trial.Piece);

        _trialRoad = new List<ICentreline>();
        float start = 0.0f;
        float end = 0.0f;
        float length = 0.0f;
        foreach (PlacedPiece piece in group)
        {
            if (piece == _trialPiece)
            {
                start = length;
            }

            foreach (ICentreline c in piece.Road().Pieces)
            {
                _trialRoad.Add(c);
                length += c.Length;
            }

            if (piece == _trialPiece)
            {
                end = length;
            }
        }

        _trialLine = new RacingLine(_trialRoad, closed: false);
        _trialMainStart = start;
        _trialMainEnd = end;
        _trialHint = -1;
        _trialInAt = null;
        _trialUpAt = null;
        _trialLandedAt = null;
        _trialSlowestTop = float.MaxValue;
        _trialAirTicks = 0;
        _trialAirRun = 0;
        _trialFrom = Time;

        Frame f = group[0].Start;
        Vector3 upV = f.Up.ToGodot().Normalized();
        Vector3 right = f.Forward.ToGodot().Cross(upV).Normalized();
        Vector3 forward = upV.Cross(right).Normalized();
        var at = new Transform3D(new Basis(right, upV, -forward), f.Position.ToGodot() + (upV * (Buggy.RestHeight(Car.Spec) + 0.02f)));
        Car.Respawn(at, trial.Kmh / 3.6f);
        Car.Driver = default;
    }

    /// <summary>
    /// Steering along a line from the front axle: the heading error to the line there, and the distance
    /// off it over the speed (a Stanley tracker, as the sweeper's).
    /// </summary>
    private float Track(RacingLine line, ref int hint)
    {
        Vector3 forward = Forward;
        Vector3 up = Car.GlobalBasis.Y.Normalized();
        Vector3 front = Car.GlobalPosition + (forward * (Car.Spec.WheelbaseM / 2.0f));
        hint = line.Nearest(front.ToCore(), hint);
        float off = line.LateralOffset(hint, front.ToCore());
        Vector3 lineForward = line.ForwardAt(hint).ToGodot();
        float headingError = Mathf.RadToDeg(MathF.Atan2(forward.Cross(lineForward).Dot(-up), forward.Dot(lineForward)));
        float speed = MathF.Max(ForwardSpeed, 5.0f);
        float steerDeg = headingError + Mathf.RadToDeg(MathF.Atan(-2.0f * off / speed));
        return Math.Clamp(steerDeg / MathF.Max(Car.SteerLimitDeg, 1.0f), -1.0f, 1.0f);
    }

    private static string TrialName(PieceTrial trial) => trial.Piece switch
    {
        "base:launch-short" => "short jump",
        "base:launch-long" => "long jump",
        _ => trial.Piece,
    };
}
