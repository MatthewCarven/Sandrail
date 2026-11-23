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
/// M2d's oracle: the autodriver drives a track from its start gate to its finish, timed as the game times
/// him (M2-PLAN, M2d): the first "proof that a track can be finished" (PLAN suggestion 4), which M3 will
/// need for every track it generates.
/// </summary>
/// <remarks>
/// It prints the time and the splits, each jump's speed at the lip and where it came down, the loop, and
/// where the wheels left the road; or, if it doesn't finish, exactly where and why it stopped. There's no
/// auto-respawn here: a crash is reported as a crash.
/// </remarks>
public partial class Oracle
{
    public const string TrackDrive = "track";

    /// <summary>The track the track oracle drives; null elsewhere.</summary>
    public LaidTrack? Course { get; init; }

    public static bool NeedsTrack(string kind) => kind is TrackDrive;

    /// <summary>How long the car may go without getting a metre further before it's called stuck.</summary>
    private const float StuckSeconds = 8.0f;

    private RacingLine? _courseLine;
    private Autodriver? _courseDriver;
    private RaceTimer? _courseTimer;
    private IReadOnlyList<int> _courseOwner = [];
    private int[] _courseOffTicks = [];
    private float _courseTop;
    private float _courseWorstSlide;
    private float _courseFurthest;
    private float _courseFurthestAt;
    private float _courseTippedFor;
    private readonly List<string> _courseNotes = new();
    private readonly HashSet<int> _courseMissed = new();

    // The jump in flight: its lip's piece, the lip speed, and whether the car has left the ground yet.
    private int? _jumpFrom;
    private float _jumpLipSpeed;
    private bool _jumpUp;

    // The loop being driven: its piece, the speed in, the slowest upside down.
    private int? _loopAt;
    private float _loopIn;
    private float _loopSlowest;
    private bool _loopOver;

    private float? _lowestGround;

    /// <summary>
    /// The lowest the ground goes: 0 on the plain, the deepest hollow on generated ground. "Fell off the
    /// world" is measured from here; from the plain, seed 6's road through a hollow 6 m down read as a fall
    /// (2026-11-23).
    /// </summary>
    private float LowestGround => _lowestGround ??= MathF.Min(0.0f, Ground?.Heights.Min() ?? 0.0f);

    private void RunTrack()
    {
        LaidTrack track = Course!;
        if (_courseDriver is null)
        {
            _courseLine = track.RouteLine(Car.Spec);
            _courseDriver = new Autodriver(_courseLine, Car.Spec, Caution);
            _courseTimer = new RaceTimer(track.Gates());
            _courseTimer.Arm();
            _courseOwner = track.RouteRoad().Owner;
            _courseOffTicks = new int[track.Route.Count];
        }

        // Settled on its springs first, as the other oracles start.
        if (Time < 0.5f)
        {
            Car.Driver = default;
            return;
        }

        RacingLine line = _courseLine!;
        Basis basis = Car.GlobalBasis;
        Vector3 forward = (-basis.Z).Normalized();
        Vector3 up = basis.Y.Normalized();
        bool airborne = Car.GroundedCount == 0;
        DriveCommand cmd = _courseDriver.Drive(
            Car.GlobalPosition.ToCore(), forward.ToCore(), up.ToCore(), Car.LinearVelocity.ToCore(), -Car.AngularVelocity.Dot(up), airborne);
        Car.Driver = new DriverInput { Throttle = cmd.Throttle, Brake = cmd.Brake, Steer = cmd.Steer };

        RaceTimer timer = _courseTimer!;
        if (timer.Armed && cmd.Throttle > 0.0f)
        {
            timer.Go(Time);
        }

        RaceEvent passed = timer.Update(Car.GlobalPosition.ToCore(), Time);
        if (passed.Kind == RaceEventKind.OutOfOrder)
        {
            _courseMissed.Add(passed.Gate);
        }

        int index = _courseDriver.Index;
        int piece = _courseOwner[line.PieceAt(index)];
        PlacedPiece here = track.Route[piece];
        float speed = Car.LinearVelocity.Length();
        _courseTop = MathF.Max(_courseTop, speed);
        // A slide is read in the horizontal plane, so not in the loop, where the car points at the sky.
        if (speed > 5.0f && !airborne && !line.StraightAt(index))
        {
            _courseWorstSlide = MathF.Max(_courseWorstSlide, MathF.Abs(CarReadout.Of(Car).BodySlipDeg));
        }

        // Off the road: the car's middle further from the road's middle than the road leaves its tyres, on the ground.
        float carHalf = (Car.Spec.TrackM / 2.0f) + 0.15f;
        float roadHalf = (here.Def.WidthM / 2.0f) - 0.15f;
        if (!airborne && MathF.Abs(line.OffsetFromMiddle(index, Car.GlobalPosition.ToCore())) > roadHalf - carHalf + 0.5f)
        {
            _courseOffTicks[piece]++;
        }

        WatchJumps(track, piece, airborne);
        WatchLoop(track, piece, speed, up);

        // Progress, for calling it stuck: a metre further along the route within the time allowed.
        float along = line.DistanceAt(index);
        if (along > _courseFurthest + 1.0f)
        {
            _courseFurthest = along;
            _courseFurthestAt = Time;
        }

        bool tipped = up.Y < 0.342f && _loopAt is null;
        _courseTippedFor = tipped ? _courseTippedFor + (1.0f / Engine.PhysicsTicksPerSecond) : 0.0f;

        if (timer.Finished)
        {
            FinishTrack(track, $"FINISHED in {Clock(timer.Elapsed(Time))}", timer);
        }
        else if (Car.GlobalPosition.Y < LowestGround - 5.0f)
        {
            FinishTrack(track, $"DIDN'T FINISH: fell off the world {Where(track, piece, along)}", timer);
        }
        else if (_courseTippedFor > 3.0f)
        {
            FinishTrack(track, $"DIDN'T FINISH: tipped over {Where(track, piece, along)}", timer);
        }
        else if (Time - _courseFurthestAt > StuckSeconds)
        {
            FinishTrack(track, $"DIDN'T FINISH: stuck for {StuckSeconds:F0} s {Where(track, piece, along)}", timer);
        }
        else if (Time > 300.0f)
        {
            FinishTrack(track, $"DIDN'T FINISH in 300 s: {Where(track, piece, along)}", timer);
        }
    }

    /// <summary>Each jump: the speed as the car leaves the lip, and where on the landing it comes down.</summary>
    private void WatchJumps(LaidTrack track, int piece, bool airborne)
    {
        PlacedPiece here = track.Route[piece];
        if (_jumpFrom is null && here.Out.Kind == ConnectorKind.Lip)
        {
            Frame lip = here.Out.Frame;
            if (Past(lip.Position, lip.Forward) >= 0.0f)
            {
                _jumpFrom = piece;
                _jumpLipSpeed = Car.LinearVelocity.Length();
                _jumpUp = false;
            }

            return;
        }

        if (_jumpFrom is not int from)
        {
            return;
        }

        _jumpUp |= airborne;
        if (_jumpUp && Car.GroundedCount >= 2)
        {
            PlacedPiece launch = track.Route[from];
            Frame landing = track.Route[from + 1].Start;
            float into = Past(landing.Position, landing.Forward);
            float pastLip = into + (launch.Out.Gap * TrackGrid.TileM);
            _courseNotes.Add($"{launch.Def.Name.ToLowerInvariant()}: lip {_jumpLipSpeed * 3.6f:F0} km/h, landed {pastLip:F1} m past it, "
                + $"{LandingSpot(into)}, pitch {CarReadout.Of(Car).PitchDeg:F0}°");
            _jumpFrom = null;
        }
    }

    /// <summary>The loop: the speed in, whether it went over the top, the slowest there, and whether it came out.</summary>
    private void WatchLoop(LaidTrack track, int piece, float speed, Vector3 up)
    {
        bool inLoop = _courseDriver!.Index >= 0 && _courseLine!.StraightAt(_courseDriver.Index);
        if (_loopAt is null && inLoop)
        {
            _loopAt = piece;
            _loopIn = speed;
            _loopSlowest = float.MaxValue;
            _loopOver = false;
            _loopWorst = 0.0f;
        }

        if (_loopAt is not int at)
        {
            return;
        }

        if (up.Y < -0.5f)
        {
            _loopOver = true;
            _loopSlowest = MathF.Min(_loopSlowest, speed);
        }

        // How far off its line it got going round: the first version of this oracle called a car that slid
        // off the loop's side at the bottom and drove back onto the road "through" (2026-11-23).
        if (inLoop)
        {
            _loopWorst = MathF.Max(_loopWorst, MathF.Abs(_courseDriver.Lateral));
        }

        if (piece != at)
        {
            float room = (track.Route[at].Def.WidthM / 2.0f) - 0.15f - ((Car.Spec.TrackM / 2.0f) + 0.15f);
            string how = !_loopOver ? "NEVER WENT OVER THE TOP"
                : _loopWorst > room ? $"over the top but CAME OFF ITS SIDE, {_loopWorst:F1} m off its line"
                : $"through, at most {_loopWorst:F1} m off its line";
            _courseNotes.Add($"loop: in at {_loopIn * 3.6f:F0} km/h, {how}{(_loopOver ? $", slowest over the top {_loopSlowest * 3.6f:F0} km/h" : string.Empty)}");
            _loopAt = null;
        }
    }

    private float _loopWorst;

    private void FinishTrack(LaidTrack track, string verdict, RaceTimer timer)
    {
        float tps = Engine.PhysicsTicksPerSecond;
        string splits = timer.Splits.Count == 0 ? "none"
            : string.Join(", ", timer.Splits.Take(Math.Min(timer.Splits.Count, timer.Checkpoints)).Select(s => Clock(s)));
        string off = string.Join(", ", Enumerable.Range(0, _courseOffTicks.Length)
            .Where(k => _courseOffTicks[k] > 0)
            .Select(k => $"{Label(track, k)} {_courseOffTicks[k] / tps:F2} s"));
        float offTotal = _courseOffTicks.Sum() / tps;
        string missed = _courseMissed.Count == 0 ? string.Empty : $" | MISSED checkpoint {string.Join(", ", _courseMissed)} at first";
        string notes = _courseNotes.Count == 0 ? string.Empty : " | " + string.Join(" | ", _courseNotes);
        Finish($"{track.Track.Id} at caution {Caution:F2}: {verdict} | splits {splits} of {timer.Checkpoints} | top {_courseTop * 3.6f:F0} km/h"
            + $"{notes} | wheels off the road {offTotal:F2} s{(off.Length > 0 ? $" ({off})" : string.Empty)}, worst slide {_courseWorstSlide:F0}°{missed}");
    }

    private string Where(LaidTrack track, int piece, float along) =>
        $"on {Label(track, piece)}, {along:F0} m along, {_courseTimer!.Passed} of {_courseTimer.Checkpoints} checkpoints passed, {Clock(_courseTimer.Elapsed(Time))} in";

    /// <summary>A piece of the route in words: what it is and its tile, as the track file says it.</summary>
    private static string Label(LaidTrack track, int piece)
    {
        PlacedPiece p = track.Route[piece];
        string id = p.Def.Id.StartsWith("base:", StringComparison.Ordinal) ? p.Def.Id[5..] : p.Def.Id;
        return $"{id}{(p.Placement.Mirrored ? " (left)" : string.Empty)} at {p.Placement.At}";
    }

    private static string Clock(double seconds) =>
        seconds < 60.0
            ? seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            : $"{(int)(seconds / 60.0)}:{(seconds % 60.0).ToString("00.00", System.Globalization.CultureInfo.InvariantCulture)}";
}
