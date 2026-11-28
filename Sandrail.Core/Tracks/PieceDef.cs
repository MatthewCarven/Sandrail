using System.Numerics;
using Sandrail.Core.Cars;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tracks;

/// <summary>What a connector meets (M2-PLAN fact 6).</summary>
public enum ConnectorKind
{
    /// <summary>The next piece's road, on the next tile, at the same level.</summary>
    Road,

    /// <summary>A jump's lip: it meets a landing some tiles straight ahead, with the tiles between left empty.</summary>
    Lip,

    /// <summary>Where a lip's jump comes down.</summary>
    Landing,

    /// <summary>Nothing: the start's way in and the finish's way out.</summary>
    Closed,
}

/// <summary>A side of a tile, as the piece sees it: front is the way it's driven.</summary>
public enum PieceSide
{
    Front,
    Back,
    Left,
    Right,
}

/// <summary>A gate on a piece: what the timer reads (M2-PLAN decisions 3 and 4).</summary>
public enum GateKind
{
    Start,
    Checkpoint,
    Finish,

    /// <summary>A circuit's line (M3g): where it starts, where each lap ends and where the last one finishes.</summary>
    StartFinish,
}

/// <summary>A piece's way out, in its own terms.</summary>
public sealed record PieceConnector
{
    /// <summary>The tile it's on, as [across, along] from the piece's first tile: across is to the right, along is forward.</summary>
    public int[] Tile { get; init; } = [0, 0];

    /// <summary>The side of that tile it's on.</summary>
    public PieceSide Side { get; init; } = PieceSide.Front;

    /// <summary>How many levels above the piece's way in; for a lip, the level its landing is at.</summary>
    public int Rise { get; init; }

    public ConnectorKind Kind { get; init; } = ConnectorKind.Road;

    /// <summary>A lip's height above the piece's way in, metres. Lips aren't level, so they aren't on a level.</summary>
    public float LipHeightM { get; init; }

    /// <summary>How steeply a lip points up, degrees.</summary>
    public float LipPitchDeg { get; init; }

    /// <summary>How many empty tiles a lip's jump clears before its landing begins.</summary>
    public int Gap { get; init; }
}

/// <summary>A climb or a dip in the road: an arc in its own vertical plane, as <see cref="PitchCentreline"/> draws.</summary>
public sealed record PitchStep
{
    /// <summary>Degrees: positive bends nose up, negative over a crest.</summary>
    public float Angle { get; init; }

    public float Radius { get; init; }
}

/// <summary>A corner, as <see cref="TurnCentreline"/> draws: positive turns right, and a mirrored piece turns the other way.</summary>
public sealed record TurnStep
{
    public float Angle { get; init; }

    public float Radius { get; init; }

    /// <summary>Degrees, tilted toward the inside, pivoting on the inside edge.</summary>
    public float Bank { get; init; }

    /// <summary>The share of the corner the bank takes to come in, and again to go out: the corner lap's half.</summary>
    public float Ease { get; init; } = 0.5f;

    /// <summary>
    /// The share of the corner each end takes to tighten from straight, and to loosen again, its bank in step (PIECES-PLAN,
    /// P3): a <see cref="TransitionTurnCentreline"/>, whose middle is tighter than <see cref="Radius"/> so it ends where that
    /// arc would, and whose bank ignores <see cref="Ease"/>. Null, so left out of the fingerprint, for an arc.
    /// </summary>
    public float? Transition { get; init; }

    /// <summary>
    /// Metres the corner climbs as it turns (PIECES-PLAN, the climbing corner), negative to come down: eased over the whole
    /// turn by smoothstep, so it starts and ends level, the turn itself unchanged beneath it
    /// (<see cref="ClimbingCentreline"/>). Null, so left out of the fingerprint, for a level corner.
    /// </summary>
    public float? Rise { get; init; }
}

/// <summary>
/// A loop that fits the grid (M2-PLAN fact 4): an S-bend onto the loop's skew, the loop as
/// <see cref="LoopCentreline"/> draws it, an S-bend off again, and a straight to finish, so it's entered
/// and left square to the grid, <see cref="Across"/> to the right (the left, mirrored).
/// </summary>
/// <remarks>
/// The loop is driven with the wheels straight because its offset grows evenly with distance round, and
/// the price is that its road meets it at a skew. The S-bends carry that skew inside the piece: their
/// radius is solved so that they and the offset together come to <see cref="Across"/>, and the closing
/// straight so that the whole step comes to <see cref="Length"/>, which keeps a piece file exact without
/// its author working either out to the millimetre.
/// </remarks>
public sealed record LoopStep
{
    /// <summary>Its radius at the foot, where the car is fast and low.</summary>
    public float Foot { get; init; }

    /// <summary>Its radius at the top, where it's slow and high.</summary>
    public float Top { get; init; }

    /// <summary>How far to the side the loop's own exit is from its entry.</summary>
    public float Offset { get; init; }

    public float Sharpness { get; init; } = 1.5f;

    /// <summary>How far to the side the whole step ends: a tile, to leave by the next tile's edge.</summary>
    public float Across { get; init; }

    /// <summary>How far forward the whole step goes.</summary>
    public float Length { get; init; }

    /// <summary>
    /// A straight at the skew before the loop and again after it, so the car arrives at the foot settled
    /// rather than still turning out of the S-bend: without it the bend's yaw carried into the loop, where
    /// the wheels are straight, and the car came off on the way down (2026-11-23).
    /// </summary>
    public float Settle { get; init; }

    /// <summary>The loop's skew (signed degrees), the S-bends' radius, and how far forward the loop itself goes, worked out once.</summary>
    internal (float SkewDeg, float BendM, float ClosureM) Plan(float sign)
    {
        var probe = new LoopCentreline(Frame.Level(Vector3.Zero, 0.0f), Foot, Top, sign * Offset, Sharpness);
        float skew = probe.EntryHeadingDeg;
        float s = MathF.Abs(skew) * (MathF.PI / 180.0f);
        float closure = -probe.FrameAt(probe.Length).Position.Z;
        float bend = (Across - MathF.Abs(Offset) - (2.0f * Settle * MathF.Sin(s))) / (2.0f * (1.0f - MathF.Cos(s)));
        return (skew, bend, closure);
    }

    /// <summary>How far forward the S-bends, the settling straights and the loop take, before the closing straight.</summary>
    internal float Needed()
    {
        (float skew, float bend, float closure) = Plan(1.0f);
        float s = MathF.Abs(skew) * (MathF.PI / 180.0f);
        return (2.0f * bend * MathF.Sin(s)) + (2.0f * Settle * MathF.Cos(s)) + closure;
    }
}

/// <summary>One step of a piece's road. Exactly one of the four is given.</summary>
public sealed record PathStep
{
    /// <summary>A straight, metres, in whatever direction the road faces, so after a pitch it climbs.</summary>
    public float? Line { get; init; }

    public PitchStep? Pitch { get; init; }

    public TurnStep? Turn { get; init; }

    public LoopStep? Loop { get; init; }
}

/// <summary>
/// A piece of track (M2-PLAN, "The model"): the tiles it covers, how it's entered and left, and its road
/// as steps, so the catalogue is data and not code.
/// </summary>
/// <remarks>
/// <para>
/// <b>In its own frame, a piece is entered at the back edge of its first tile, driving forward</b>, at
/// its own level. Its way out is declared, and <see cref="Problems"/> builds the road and refuses a
/// piece whose road doesn't end there to a millimetre: the catalogue holds itself to the connector rule
/// rather than trusting it.
/// </para>
/// <para>
/// As with <see cref="CarSpec"/>, <b>a field the file has and this record doesn't is refused</b>, and
/// every test is written as a refusal of what's allowed, so NaN fails it.
/// </para>
/// </remarks>
public sealed record PieceDef
{
    /// <summary>How close a road must end to its declared way out: M1c's connector rule.</summary>
    public const float FitM = 0.001f;

    public string Id { get; init; } = string.Empty;

    /// <summary>What the palette calls it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The tiles it covers, each [across, along] from its first; [0, 0] is always one.</summary>
    public int[][] Tiles { get; init; } = [[0, 0]];

    /// <summary>What its way in meets: road, a jump's landing, or nothing (a start).</summary>
    public ConnectorKind In { get; init; } = ConnectorKind.Road;

    public PieceConnector Out { get; init; } = new();

    public PathStep[] Path { get; init; } = [];

    /// <summary>The road's width; the cross-section is the slab with its rounded lips.</summary>
    public float WidthM { get; init; } = 10.0f;

    public string Surface { get; init; } = Surfaces.Road.Id;

    /// <summary>The speed it's meant to be taken at, [low, high] km/h, where the road's shape can't say (a jump, a loop); null where it can.</summary>
    public float[]? SpeedKmh { get; init; }

    public GateKind? Gate { get; init; }

    /// <summary>Every problem with the piece, in words. Empty means it can be placed.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        int colon = Id.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == Id.Length - 1)
        {
            problems.Add($"id is \"{Id}\", and must be namespaced, like base:straight");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems.Add("name is empty");
        }

        if (Tiles.Length == 0 || Tiles.Any(t => t is null || t.Length != 2))
        {
            problems.Add("tiles must each be [across, along]");
            return problems;
        }

        var covered = Tiles.Select(t => (t[0], t[1])).ToHashSet();
        if (covered.Count != Tiles.Length)
        {
            problems.Add("tiles lists a tile twice");
        }

        if (!covered.Contains((0, 0)))
        {
            problems.Add("tiles must include [0, 0], where the piece is entered");
        }

        if (covered.Contains((0, -1)))
        {
            problems.Add("the way in, at the back of [0, 0], opens onto another of its own tiles");
        }

        if (In == ConnectorKind.Lip)
        {
            problems.Add("in can't be a lip: a jump leaves by its way out");
        }

        if (Out.Tile is null || Out.Tile.Length != 2)
        {
            problems.Add("out.tile must be [across, along]");
        }
        else
        {
            (int across, int along) = (Out.Tile[0], Out.Tile[1]);
            if (!covered.Contains((across, along)))
            {
                problems.Add($"out is on [{across}, {along}], which isn't one of its tiles");
            }

            (int da, int df) = Toward(Out.Side);
            if (covered.Contains((across + da, along + df)))
            {
                problems.Add($"out, on the {Out.Side.ToString().ToLowerInvariant()} of [{across}, {along}], opens onto another of its own tiles");
            }
        }

        if (Out.Kind == ConnectorKind.Landing)
        {
            problems.Add("out can't be a landing: a jump comes down at a way in");
        }

        if (Out.Kind == ConnectorKind.Lip)
        {
            if (!(Out.LipHeightM > 0.0f && Out.LipHeightM <= 30.0f && Out.LipPitchDeg > 0.0f && Out.LipPitchDeg <= 45.0f))
            {
                problems.Add("a lip needs lipHeightM above 0 and lipPitchDeg from above 0 to 45");
            }

            if (Out.Gap is < 0 or > 10)
            {
                problems.Add($"out.gap is {Out.Gap}, and must be from 0 to 10 tiles");
            }
        }
        else if (Out.LipHeightM != 0.0f || Out.LipPitchDeg != 0.0f || Out.Gap != 0)
        {
            problems.Add("lipHeightM, lipPitchDeg and gap are only for a lip");
        }

        if (!(WidthM >= 4.0f && WidthM <= 20.0f))
        {
            problems.Add($"widthM is {WidthM}, and must be from 4 to 20");
        }

        if (!Surfaces.All.Any(s => s.Id == Surface))
        {
            problems.Add($"surface is \"{Surface}\", and must be one of {string.Join(", ", Surfaces.All.Select(s => s.Id))}");
        }

        if (SpeedKmh is not null && (SpeedKmh.Length != 2 || !(SpeedKmh[0] > 0.0f && SpeedKmh[1] >= SpeedKmh[0] && SpeedKmh[1] < 1000.0f)))
        {
            problems.Add("speedKmh must be [low, high], with 0 < low <= high");
        }

        if (Path.Length == 0)
        {
            problems.Add("path is empty");
        }

        for (int i = 0; i < Path.Length; i++)
        {
            StepProblems(Path[i], $"path[{i}]", problems);
        }

        // Only a piece that's sound so far is built: the road's end against the way out it declares.
        if (problems.Count == 0)
        {
            var placed = new PlacedPiece(this, new Placement(Id, new Tile(0, 0), 0, 0, false));
            float off = placed.PathMisfitM();
            if (!(off <= FitM))
            {
                problems.Add($"the path ends {off:0.####} m from its way out, and must end within {FitM} m");
            }
        }

        return problems;
    }

    /// <summary>The tile next to a side, as (across, along).</summary>
    internal static (int Across, int Along) Toward(PieceSide side) => side switch
    {
        PieceSide.Front => (0, 1),
        PieceSide.Back => (0, -1),
        PieceSide.Right => (1, 0),
        _ => (-1, 0),
    };

    private static void StepProblems(PathStep step, string at, List<string> problems)
    {
        int given = (step.Line is null ? 0 : 1) + (step.Pitch is null ? 0 : 1) + (step.Turn is null ? 0 : 1) + (step.Loop is null ? 0 : 1);
        if (given != 1)
        {
            problems.Add($"{at} must be exactly one of line, pitch, turn or loop");
            return;
        }

        if (step.Line is float line && !(line > 0.0f && line <= 1000.0f))
        {
            problems.Add($"{at}.line is {line}, and must be above 0");
        }

        if (step.Pitch is PitchStep pitch && !(pitch.Angle != 0.0f && MathF.Abs(pitch.Angle) <= 90.0f && pitch.Radius > 0.0f && pitch.Radius <= 10_000.0f))
        {
            problems.Add($"{at}.pitch needs an angle from -90 to 90 that isn't 0, and a radius above 0");
        }

        if (step.Turn is TurnStep turn)
        {
            if (!(turn.Angle != 0.0f && MathF.Abs(turn.Angle) <= 360.0f && turn.Radius > 0.0f && turn.Radius <= 10_000.0f))
            {
                problems.Add($"{at}.turn needs an angle up to 360 that isn't 0, and a radius above 0");
            }

            if (!(turn.Bank >= 0.0f && turn.Bank <= 60.0f))
            {
                problems.Add($"{at}.turn.bank is {turn.Bank}, and must be from 0 to 60");
            }

            if (!(turn.Ease > 0.0f && turn.Ease <= 0.5f))
            {
                problems.Add($"{at}.turn.ease is {turn.Ease}, and must be above 0 and at most 0.5");
            }

            if (turn.Transition is float transition && !(transition > 0.0f && transition <= 0.5f && MathF.Abs(turn.Angle) <= 180.0f))
            {
                problems.Add($"{at}.turn.transition is {transition}, and must be above 0 and at most 0.5, on a turn of at most 180°");
            }

            if (turn.Rise is float rise && !(rise != 0.0f && MathF.Abs(rise) <= 20.0f && MathF.Abs(turn.Angle) <= 360.0f))
            {
                problems.Add($"{at}.turn.rise is {rise}, and must be from -20 to 20 metres and not 0");
            }
        }

        if (step.Loop is LoopStep loop)
        {
            if (!(loop.Top > 0.0f && loop.Foot >= loop.Top && loop.Foot <= 1000.0f && loop.Offset > 0.0f && loop.Sharpness > 0.0f && loop.Sharpness <= 10.0f))
            {
                problems.Add($"{at}.loop needs 0 < top <= foot, an offset above 0 and a sharpness above 0");
            }
            else if (!(loop.Across > loop.Offset && loop.Across <= 1000.0f && loop.Settle >= 0.0f && loop.Settle <= 1000.0f))
            {
                problems.Add($"{at}.loop.across is {loop.Across}, and must be more than its offset ({loop.Offset}); settle can't be negative");
            }
            else if (!(loop.Plan(1.0f).BendM > 0.0f))
            {
                problems.Add($"{at}.loop: its offset and settling straights already go more than {loop.Across} m across, leaving its S-bends nothing to do");
            }
            else if (!(loop.Length >= loop.Needed() && loop.Length <= 10_000.0f))
            {
                problems.Add($"{at}.loop.length is {loop.Length}, and its S-bends and loop need {loop.Needed():0.##} m");
            }
        }
    }
}
