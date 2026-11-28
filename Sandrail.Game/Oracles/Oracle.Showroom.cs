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

    /// <summary>
    /// The pieces added since the starter set (PIECES-PLAN): their trials kept apart from <see cref="Pieces"/>, so that
    /// oracle's line never grows and stays comparable byte for byte.
    /// </summary>
    public const string Pieces2 = "pieces2";

    public static bool NeedsShowroom(string kind) => kind is Pieces or Pieces2;

    /// <summary>
    /// A loop, a jump or a slope as M2b drove them; the crossing driven under its deck; a corner held at a speed; or a
    /// piece that throws the car off its own road (the hump, the tabletop), held to its top and coasted from there; or a
    /// surface straight braked, or pulled down at full throttle, from a speed.
    /// </summary>
    private enum TrialKind { Loop, Jump, Slope, Under, Corner, Fly, Brake, Pull }

    /// <summary>
    /// A piece at a speed; or with <paramref name="Station"/>, a station's whole road, as for the crossing; with
    /// <paramref name="Shift"/>, a braking or pulling trial measured that many metres from the piece's way in (−20: the
    /// road tile before it).
    /// </summary>
    private sealed record PieceTrial(string Piece, float Kmh, TrialKind Kind, string? Station = null, float Shift = 0.0f);

    private PieceTrial[] ActiveTrials => Kind == Pieces2 ? Trials2 : Trials;

    /// <summary>
    /// The steep slopes either side of 143 km/h, where today's slope's crest throws the car (theirs are gentler, 181 m to
    /// 161), and the crossing: under its deck flat out, and over it, up two levels and down.
    /// </summary>
    private static readonly PieceTrial[] Trials2 =
    [
        new("base:slope-up-steep", 100.0f, TrialKind.Slope),
        new("base:slope-up-steep", 120.0f, TrialKind.Slope),
        new("base:slope-up-steep", 140.0f, TrialKind.Slope),
        new("base:slope-up-steep", 160.0f, TrialKind.Slope),
        new("base:slope-down-steep", 100.0f, TrialKind.Slope),
        new("base:slope-down-steep", 140.0f, TrialKind.Slope),
        new("base:straight", 120.0f, TrialKind.Under, Sandrail.Core.Tracks.Showroom.CrossingUnder),
        new("base:straight", 160.0f, TrialKind.Under, Sandrail.Core.Tracks.Showroom.CrossingUnder),
        new("base:straight", 80.0f, TrialKind.Slope, Sandrail.Core.Tracks.Showroom.CrossingOver),
        new("base:straight", 120.0f, TrialKind.Slope, Sandrail.Core.Tracks.Showroom.CrossingOver),

        // P1: each corner held at rising speeds, steered down its middle, to find where it stops holding. A bank eases in
        // from flat at each end, so a banked corner's limit is nearer its flat twin's than its full bank suggests.
        new("base:corner-70", 70.0f, TrialKind.Corner),
        new("base:corner-70", 80.0f, TrialKind.Corner),
        new("base:corner-70", 90.0f, TrialKind.Corner),
        new("base:corner-70-banked", 80.0f, TrialKind.Corner),
        new("base:corner-70-banked", 90.0f, TrialKind.Corner),
        new("base:corner-70-banked", 100.0f, TrialKind.Corner),
        new("base:corner-70-banked", 110.0f, TrialKind.Corner),
        new("base:u-turn", 40.0f, TrialKind.Corner),
        new("base:u-turn", 45.0f, TrialKind.Corner),
        new("base:u-turn", 50.0f, TrialKind.Corner),
        new("base:u-turn", 55.0f, TrialKind.Corner),
        new("base:u-turn-banked", 45.0f, TrialKind.Corner),
        new("base:u-turn-banked", 50.0f, TrialKind.Corner),
        new("base:u-turn-banked", 55.0f, TrialKind.Corner),
        new("base:u-turn-banked", 60.0f, TrialKind.Corner),
        new("base:u-turn-banked", 65.0f, TrialKind.Corner),

        // P2: the hump either side of where it starts throwing the car, and on to where it flies past itself; the
        // tabletop from landing on its top, through its down-ramp, to landing on its run-out.
        new("base:hump", 60.0f, TrialKind.Fly),
        new("base:hump", 70.0f, TrialKind.Fly),
        new("base:hump", 80.0f, TrialKind.Fly),
        new("base:hump", 90.0f, TrialKind.Fly),
        new("base:hump", 100.0f, TrialKind.Fly),
        new("base:hump", 120.0f, TrialKind.Fly),
        new("base:hump", 140.0f, TrialKind.Fly),
        new("base:tabletop", 50.0f, TrialKind.Fly),
        new("base:tabletop", 60.0f, TrialKind.Fly),
        new("base:tabletop", 70.0f, TrialKind.Fly),
        new("base:tabletop", 75.0f, TrialKind.Fly),
        new("base:tabletop", 80.0f, TrialKind.Fly),
        new("base:tabletop", 85.0f, TrialKind.Fly),
        new("base:tabletop", 90.0f, TrialKind.Fly),
        new("base:tabletop", 100.0f, TrialKind.Fly),

        // P3: each surface straight braked from 80 km/h and pulled from 40 down its one tile, against the road tile before
        // the dirt one; and the 40° corner held at speeds, beside the 20° 30 m corner of the starter set.
        new("base:straight-dirt", 80.0f, TrialKind.Brake, Shift: -TrackGrid.TileM),
        new("base:straight-dirt", 80.0f, TrialKind.Brake),
        new("base:straight-sand", 80.0f, TrialKind.Brake),
        new("base:straight-dirt", 40.0f, TrialKind.Pull, Shift: -TrackGrid.TileM),
        new("base:straight-dirt", 40.0f, TrialKind.Pull),
        new("base:straight-sand", 40.0f, TrialKind.Pull),
        new("base:corner-30-banked", 60.0f, TrialKind.Corner),
        new("base:corner-30-banked", 65.0f, TrialKind.Corner),
        new("base:corner-30-banked", 70.0f, TrialKind.Corner),
        new("base:corner-30-banked", 75.0f, TrialKind.Corner),
        new("base:corner-30-banked", 80.0f, TrialKind.Corner),
        new("base:corner-30-steep", 65.0f, TrialKind.Corner),
        new("base:corner-30-steep", 70.0f, TrialKind.Corner),
        new("base:corner-30-steep", 75.0f, TrialKind.Corner),
        new("base:corner-30-steep", 80.0f, TrialKind.Corner),
        new("base:corner-30-steep", 90.0f, TrialKind.Corner),
        new("base:corner-30-steep", 100.0f, TrialKind.Corner),
    ];

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

    /// <summary>A flying trial's top (the hump's crest, the tabletop's lip), the piece's way forward, and how far past the top each of its road's steps ends.</summary>
    private System.Numerics.Vector3 _flyTakeOff;
    private System.Numerics.Vector3 _flyForward;
    private float[] _flyEnds = [];

    private void RunPieces()
    {
        ShowroomLayout room = Showroom!;
        if (_trial < 0 || _trialLine is null)
        {
            StartTrial(room, 0);
            return;
        }

        PieceTrial trial = ActiveTrials[_trial];
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
            case TrialKind.Under:
                RunUnderTrial(trial, steer, along);
                break;
            case TrialKind.Corner:
                RunCornerTrial(trial, steer, along);
                break;
            case TrialKind.Fly:
                RunFlyTrial(trial, steer, along);
                break;
            case TrialKind.Brake:
            case TrialKind.Pull:
                RunPedalTrial(trial, steer, along);
                break;
        }

        if (_trialFigures.Count > _trial)
        {
            if (_trial + 1 < ActiveTrials.Length)
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

        int tps = GameClock.TicksPerSecond;
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
            string where = LandingSpot(_trialLandedPast - (lip.Gap * TrackGrid.TileM));
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
            float air = _trialAirTicks / (float)GameClock.TicksPerSecond;
            string name = trial.Piece == "base:slope-up" && trial.Station is null ? "slope" : TrialName(trial);
            _trialFigures.Add($"{name} at {_trialInSpeed * 3.6f:F0} km/h: {(air > 0.0f ? $"{air:F2} s in the air" : "wheels down")}");
        }
    }

    /// <summary>
    /// A corner held at a speed (PIECES-PLAN, P1), steered down its middle: whether the car kept its wheels on the road,
    /// by the profile meter's rule (its middle no further out than the road leaves its tyres, and half a metre), and the
    /// widest it ran.
    /// </summary>
    private void RunCornerTrial(PieceTrial trial, float steer, float along)
    {
        Car.Driver = Hold(trial.Kmh / 3.6f) with { Steer = steer };
        if (along >= _trialMainStart && along <= _trialMainEnd)
        {
            if (_trialInAt is null)
            {
                _trialInAt = Time;
                _trialInSpeed = Car.LinearVelocity.Length();
                _trialSlowestTop = 0.0f;
            }

            _trialSlowestTop = MathF.Max(_trialSlowestTop, MathF.Abs(_trialLine!.LateralOffset(Math.Max(_trialHint, 0), Car.GlobalPosition.ToCore())));
        }

        // Off the road ends the trial there and then: a car that's lost the corner only wanders.
        float leaves = (_trialPiece!.Def.WidthM / 2.0f) - 0.15f - ((Car.Spec.TrackM / 2.0f) + 0.15f) + 0.5f;
        if (_trialInAt is not null && _trialSlowestTop > leaves)
        {
            _trialFigures.Add($"{TrialName(trial)} at {_trialInSpeed * 3.6f:F0} km/h: OFF THE ROAD, {along - _trialMainStart:F0} m into it");
        }
        else if (_trialInAt is not null && along > _trialMainEnd + 20.0f)
        {
            _trialFigures.Add($"{TrialName(trial)} at {_trialInSpeed * 3.6f:F0} km/h: held it, at most {_trialSlowestTop:F1} m off its middle");
        }
    }

    /// <summary>
    /// A hump or a tabletop at a speed (PIECES-PLAN, P2): the speed held to its top (the hump's crest, the tabletop's lip)
    /// and coasted from there, steered on the ground and hands off in the air, as the jumps are. How long it was in the
    /// air, where it came down, by how far past the top and on which part of the piece, its pitch as it landed, and
    /// whether it stayed upright and on the road. A car that never leaves the ground for three ticks running isn't thrown.
    /// </summary>
    private void RunFlyTrial(PieceTrial trial, float steer, float along)
    {
        float past = Past(_flyTakeOff, _flyForward);
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

        Car.Driver = default(DriverInput) with { Steer = steer };
        if (_trialUpAt is null && _trialAirRun >= 3)
        {
            _trialUpAt = Time;
        }

        if (_trialUpAt is not null && _trialLandedAt is null && Car.GroundedCount >= 2)
        {
            _trialLandedAt = Time;
            _trialLandedPast = past;
            _trialLandedPitch = CarReadout.Of(Car).PitchDeg;
        }

        float leaves = (_trialPiece!.Def.WidthM / 2.0f) - 0.15f - ((Car.Spec.TrackM / 2.0f) + 0.15f) + 0.5f;
        bool off = MathF.Abs(_trialLine!.LateralOffset(Math.Max(_trialHint, 0), Car.GlobalPosition.ToCore())) > leaves;
        bool upright = Car.GlobalBasis.Y.Normalized().Y > 0.7f;
        string top = trial.Piece == "base:tabletop" ? "lip" : "crest";
        string how = $"{(upright ? string.Empty : ", NOT UPRIGHT")}{(off ? ", OFF THE ROAD" : string.Empty)}";
        if (_trialLandedAt is float landed && Time - landed > 2.0f)
        {
            float air = _trialAirTicks / (float)GameClock.TicksPerSecond;
            _trialFigures.Add($"{TrialName(trial)} at {_trialInSpeed * 3.6f:F0} km/h: {air:F2} s in the air, lands {_trialLandedPast:F1} m past its {top}, "
                + $"{FlySpot(trial.Piece, _trialLandedPast)}, pitch {_trialLandedPitch:F0}°{how}");
        }
        else if (_trialUpAt is null && along > _trialMainEnd + 20.0f)
        {
            _trialFigures.Add($"{TrialName(trial)} at {_trialInSpeed * 3.6f:F0} km/h: wheels down{how}");
        }
    }

    /// <summary>Which part of a hump or tabletop a car came down on, from how far past its top: each of its road's steps by name.</summary>
    private string FlySpot(string piece, float past)
    {
        string[] parts = piece == "base:tabletop"
            ? ["before its lip", "before its lip", "before its lip", "on the top", "on the down-ramp's crest", "on the down-ramp", "at its foot", "on its run-out"]
            : ["before its crest", "on its far side", "at its foot"];
        for (int k = 0; k < _flyEnds.Length && k < parts.Length; k++)
        {
            if (past < _flyEnds[k])
            {
                return parts[k];
            }
        }

        return "past it";
    }

    /// <summary>
    /// The crossing driven under its deck at a speed (PIECES-PLAN, X2): through or not, how high the car's middle rose
    /// over the road under the deck against the headroom there, and its time with a wheel off the road.
    /// </summary>
    private void RunUnderTrial(PieceTrial trial, float steer, float along)
    {
        Car.Driver = Hold(trial.Kmh / 3.6f) with { Steer = steer };
        bool under = along >= _trialMainStart && along <= _trialMainEnd;
        if (_trialInAt is null && under)
        {
            _trialInAt = Time;
            _trialInSpeed = Car.LinearVelocity.Length();
        }

        if (under)
        {
            float road = _trialLine!.PositionAt(Math.Max(_trialHint, 0)).Y;
            _trialSlowestTop = MathF.Max(_trialSlowestTop == float.MaxValue ? 0.0f : _trialSlowestTop, Car.GlobalPosition.Y - road);
        }

        if (_trialInAt is not null && along > _trialMainEnd + 20.0f)
        {
            PlacedPiece deck = Showroom!.Pieces.First(p => p.Pillars);
            float headroom = deck.Start.Position.Y - Stacking.DeckM - _trialPiece!.Start.Position.Y;
            bool upright = Car.GlobalBasis.Y.Normalized().Y > 0.7f;
            _trialFigures.Add($"under the deck at {_trialInSpeed * 3.6f:F0} km/h: {(upright ? "THROUGH" : "NOT UPRIGHT")}, its middle at most "
                + $"{_trialSlowestTop:F2} m over the road, under {headroom:F1} m of headroom");
        }
    }

    /// <summary>
    /// A surface straight braked or pulled at a speed (PIECES-PLAN, P3): the speed held to its way in (shifted, to the
    /// road tile before it), then full brake or full throttle down the middle for one tile, and the speed it leaves at.
    /// </summary>
    private void RunPedalTrial(PieceTrial trial, float steer, float along)
    {
        float from = _trialMainStart + trial.Shift;
        if (_trialInAt is null)
        {
            Car.Driver = Hold(trial.Kmh / 3.6f) with { Steer = steer };
            if (along >= from)
            {
                _trialInAt = Time;
                _trialInSpeed = Car.LinearVelocity.Length();
            }

            return;
        }

        bool brake = trial.Kind == TrialKind.Brake;
        Car.Driver = new DriverInput { Throttle = brake ? 0.0f : 1.0f, Brake = brake ? 1.0f : 0.0f, Steer = steer };
        float speed = ForwardSpeed;
        bool stopped = brake && speed < 0.5f;
        if (along >= from + TrackGrid.TileM || stopped)
        {
            string surface = trial.Shift < 0.0f ? "road" : trial.Piece == "base:straight-sand" ? "sand" : "dirt";
            string after = stopped ? $"stops {along - from:F1} m in" : $"{speed * 3.6f:F1} km/h after {TrackGrid.TileM:F0} m";
            _trialFigures.Add($"{surface} at {_trialInSpeed * 3.6f:F0} km/h, {(brake ? "braked" : "full throttle")}: {after}");
        }
    }

    private void StartTrial(ShowroomLayout room, int index)
    {
        _trial = index;
        PieceTrial trial = ActiveTrials[index];
        IReadOnlyList<PlacedPiece> group;
        Func<PlacedPiece, bool> main;
        if (trial.Station is string station)
        {
            // A station's whole road: under the deck, the piece beneath it; over it, the climb, the deck and the descent.
            group = room.Groups[room.Stations.ToList().FindIndex(s => s.Name == station)];
            var decks = room.Pieces.Where(p => p.Pillars).SelectMany(p => p.Tiles).ToHashSet();
            main = trial.Kind == TrialKind.Under ? p => p.Tiles.Any(decks.Contains) : p => p.Pillars || p.Def.Id != trial.Piece;
            _trialPiece = group.First(main);
        }
        else
        {
            group = room.Groups.First(g => g.Any(p => p.Def.Id == trial.Piece));
            _trialPiece = group.First(p => p.Def.Id == trial.Piece);
            main = p => p == _trialPiece;
        }

        _trialRoad = new List<ICentreline>();
        float start = -1.0f;
        float end = 0.0f;
        float length = 0.0f;
        foreach (PlacedPiece piece in group)
        {
            if (main(piece) && start < 0.0f)
            {
                start = length;
            }

            foreach (ICentreline c in piece.Road().Pieces)
            {
                _trialRoad.Add(c);
                length += c.Length;
            }

            if (main(piece))
            {
                end = length;
            }
        }

        start = MathF.Max(start, 0.0f);

        if (trial.Kind == TrialKind.Fly)
        {
            // The top is the first of the road's highest ground: the hump's crest, the tabletop's lip.
            IReadOnlyList<ICentreline> steps = _trialPiece!.Road().Pieces;
            var samples = steps.SelectMany(c => Enumerable.Range(0, (int)MathF.Ceiling(c.Length / 0.1f) + 1)
                .Select(k => c.FrameAt(MathF.Min(k * 0.1f, c.Length)))).ToList();
            float highest = samples.Max(f => f.Position.Y);
            Frame takeOff = samples.First(f => f.Position.Y >= highest - 0.002f);
            _flyTakeOff = takeOff.Position;
            _flyForward = _trialPiece.Start.Forward;
            var flat = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(_flyForward.X, 0.0f, _flyForward.Z));
            _flyEnds = steps.Select(c => System.Numerics.Vector3.Dot(c.FrameAt(c.Length).Position - _flyTakeOff, flat)).ToArray();
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

    /// <summary>Where on the landing ramp a car came down, from how far past its start: its table, crest, down-ramp and foot.</summary>
    private static string LandingSpot(float into) =>
        into < 0.0f ? "SHORT, in the gap"
        : into < 10.0f ? "on the landing's table"
        : into < 14.2f ? "on its crest"
        : into < 27.1f ? "on its down-ramp"
        : into < 32.7f ? "on its foot"
        : "past it, on the run-out";

    private static string TrialName(PieceTrial trial) => trial.Station is string station ? station.ToLowerInvariant() : trial.Piece switch
    {
        "base:launch-short" => "short jump",
        "base:launch-long" => "long jump",
        "base:slope-up-steep" => "steep slope up",
        "base:slope-down-steep" => "steep slope down",
        "base:corner-70" => "70 m corner",
        "base:corner-70-banked" => "banked 70 m corner",
        "base:u-turn" => "U-turn",
        "base:u-turn-banked" => "banked U-turn",
        "base:hump" => "hump",
        "base:tabletop" => "tabletop",
        "base:corner-30-banked" => "banked 30 m corner",
        "base:corner-30-steep" => "40° banked 30 m corner",
        _ => trial.Piece,
    };
}
