using System.Numerics;
using Sandrail.Core.Generation;

namespace Sandrail.Core.Pieces;

/// <summary>One stretch of road on the test ground: its centrelines and the cross-section swept along them.</summary>
public sealed record RoadRun(string Name, IReadOnlyList<ICentreline> Pieces, Profile Profile);

/// <summary>Somewhere the car can be put with a number key, facing the way to go.</summary>
public sealed record Station(int Number, string Name, Frame Start, string Hint);

/// <summary>
/// M1c's test ground, laid out on the 640 m plain: a jump, a slope onto a plateau, a banked hairpin, a
/// loop and a bump patch, each with a run-up and a station to start from (M1-PLAN, M1c).
/// </summary>
/// <remarks>
/// <para>
/// <b>Data, in Core</b>, so a test can hold the stations apart and the oracles can find the lip and the
/// loop without Game knowing the numbers twice. Godot sweeps and draws it.
/// </para>
/// <para>
/// <b>Ground-level road is lifted <see cref="Lift"/> above the plain</b>, so the two surfaces never share
/// a plane and flicker. Raised pieces use thick slabs, so they read as earthworks down to the ground
/// rather than floating.
/// </para>
/// </remarks>
public sealed class TestGroundLayout
{
    public const float Lift = 0.05f;

    public TestGroundLayout()
    {
        var runs = new List<RoadRun>();
        var degrees = MathF.PI / 180.0f;

        // 1. The jump: a run-up into a kicker, then a tabletop. A short jump lands on the table, a good one
        // on the down-ramp beyond it, and a long one on the flat.
        Chain kicker = new Chain(Frame.Level(new Vector3(0.0f, Lift, 60.0f), 0.0f)).Line(160.0f).Pitch(20.0f, 15.0f).Line(6.0f);
        Lip = kicker.End;
        float tableHeight = Lip.Position.Y;
        float downRamp = (tableHeight - Lift - (30.0f * (1.0f - MathF.Cos(8.0f * degrees))) - (40.0f * (1.0f - MathF.Cos(8.0f * degrees))))
            / MathF.Sin(8.0f * degrees);
        Chain table = new Chain(Frame.Level(Lip.Position, 0.0f)).Line(TableLengthM).Pitch(-8.0f, 30.0f).Line(downRamp).Pitch(8.0f, 40.0f).Line(60.0f);
        ICentreline foot = table.Pieces[3];
        DownRampEndM = Vector3.Distance(Lip.Position with { Y = 0.0f }, foot.FrameAt(foot.Length).Position with { Y = 0.0f });
        Profile earthworks = Profile.Slab(10.0f, 3.5f);
        runs.Add(new RoadRun("kicker", kicker.Pieces, earthworks));
        runs.Add(new RoadRun("table", table.Pieces, earthworks));

        // 2. Up onto a plateau and down over a crest that a fast car leaves the ground on.
        Chain slope = new Chain(Frame.Level(new Vector3(-60.0f, Lift, 140.0f), 0.0f))
            .Line(30.0f).Pitch(8.0f, 50.0f).Line(35.0f).Pitch(-8.0f, 50.0f).Line(40.0f);
        PlateauHeight = slope.End.Position.Y;
        float descent = (PlateauHeight - Lift - (2.0f * 25.0f * (1.0f - MathF.Cos(12.0f * degrees)))) / MathF.Sin(12.0f * degrees);
        slope.Pitch(-12.0f, 25.0f).Line(descent).Pitch(12.0f, 25.0f).Line(60.0f);
        runs.Add(new RoadRun("slope", slope.Pieces, Profile.Slab(10.0f, 7.0f)));

        // 3. A 30° banked hairpin, pivoting on its inside edge.
        Chain banked = new Chain(Frame.Level(new Vector3(-200.0f, Lift, 160.0f), 0.0f))
            .Line(80.0f).Turn(180.0f, 35.0f, 30.0f, 5.0f).Line(80.0f);
        runs.Add(new RoadRun("banked", banked.Pieces, Profile.Slab(10.0f, 7.0f)));

        // 4. The loop, tall rather than round and offset to the right, arrived at along its entry
        // heading so straight wheels follow it (see LoopCentreline).
        Loop = new LoopCentreline(Frame.Level(new Vector3(120.0f, Lift, 40.0f), 0.0f), 18.0f, 7.0f, 15.0f);
        LoopEntry = Loop.FrameAt(0.0f);
        LoopExit = Loop.FrameAt(Loop.Length);
        var runUp = new LineCentreline(LoopEntry with { Position = LoopEntry.Position - (LoopEntry.Forward * 150.0f) }, 150.0f);
        var runOut = new LineCentreline(LoopExit, 80.0f);
        Profile loopRoad = Profile.Slab(8.0f, 0.5f, 0.1f);
        runs.Add(new RoadRun("loop run-up", [runUp], loopRoad));
        runs.Add(new RoadRun("loop", [Loop], loopRoad));
        runs.Add(new RoadRun("loop run-out", [runOut], loopRoad));

        // 5. The bump patch, on the sand.
        Bumps = new BumpField(seed: 1);
        BumpCentre = new Vector3(200.0f, 0.0f, -190.0f);

        // 6. A closed lap of six different corners, to feel out radius and bank: his ask, 2026-11-23, with
        // "dont go overboard its all going to mean more tiles". Which of these feel right is what M2's
        // corner tiles should be made of.
        Chain lap = CornerLap(new Vector3(-190.0f, Lift, -150.0f));
        runs.Add(new RoadRun("corners", lap.Pieces, Profile.Slab(10.0f, 8.0f)));
        CornerLapStart = lap.Start;

        Runs = runs;
        Frame loopStart = runUp.FrameAt(0.0f);
        Stations =
        [
            new Station(1, "Jump", kicker.Start, "80-95 km/h at the lip lands on the down-ramp; slower lands on the table"),
            new Station(2, "Slope", slope.Start, "up onto a plateau and over a crest; fast over the crest and you fly"),
            new Station(3, "Banked hairpin", banked.Start, "a 30° banked 180, 35 m radius"),
            new Station(4, "Loop", Frame.Of(loopStart.Position, loopStart.Forward, Vector3.UnitY),
                "arrive at 90-105 km/h with the wheels straight; much faster bottoms the suspension out"),
            new Station(5, "Bumps", Frame.Level(new Vector3(BumpCentre.X, 0.0f, BumpCentre.Z + (Bumps.SizeM / 2.0f) + 60.0f), 0.0f),
                "128 m of dirt bumps up to 1.5 m"),
            new Station(6, "Corners", lap.Start,
                "a lap of six: 90° flat, 45° fast kink at 10°, 135° tight at 20°, 60° left flat, 90° at 35°, 60° at 15°"),
        ];
    }

    /// <summary>
    /// The corners, in order round the lap: angle (positive right), radius, bank. Banks pivot on the
    /// inside edge, and their sum of angles is 360°, so the lap comes back facing the way it started.
    /// </summary>
    public static readonly (float AngleDeg, float RadiusM, float BankDeg)[] Corners =
    [
        (90.0f, 25.0f, 0.0f),
        (45.0f, 60.0f, 10.0f),
        (135.0f, 18.0f, 20.0f),
        (-60.0f, 30.0f, 0.0f),
        (90.0f, 30.0f, 35.0f),
        (60.0f, 40.0f, 15.0f),
    ];

    /// <summary>
    /// The lap's straights before each corner, metres. The two marked NaN are solved for, so the lap
    /// closes exactly instead of nearly: they're the first and third, whose headings (0° and 135°) are far
    /// enough apart for the two equations to have a clear answer.
    /// </summary>
    private static readonly float[] Straights = [float.NaN, 30.0f, float.NaN, 25.0f, 25.0f, 25.0f];

    /// <summary>Where the corner lap starts and ends: the station's frame.</summary>
    public Frame CornerLapStart { get; }

    /// <summary>
    /// Builds the lap with its middle near <paramref name="centre"/>. Straights add their length times a
    /// fixed direction to where the lap ends, so the end is linear in the two unknown lengths: three trial
    /// builds give the gap and each unknown's effect on it, and a 2 × 2 solve closes it.
    /// </summary>
    public static Chain CornerLap(Vector3 centre)
    {
        Vector2 Gap(float a, float b)
        {
            Chain c = BuildLap(Frame.Level(Vector3.Zero, 0.0f), a, b);
            return new Vector2(c.End.Position.X, c.End.Position.Z);
        }

        Vector2 gap = Gap(0.0f, 0.0f);
        Vector2 perA = Gap(1.0f, 0.0f) - gap;
        Vector2 perB = Gap(0.0f, 1.0f) - gap;
        float det = (perA.X * perB.Y) - (perA.Y * perB.X);
        float a = ((-gap.X * perB.Y) - (-gap.Y * perB.X)) / det;
        float b = ((perA.X * -gap.Y) - (perA.Y * -gap.X)) / det;

        // Centre it: build once at the origin, measure its extent, and start it offset by the difference.
        Chain atOrigin = BuildLap(Frame.Level(Vector3.Zero, 0.0f), a, b);
        var low = new Vector2(float.MaxValue);
        var high = new Vector2(float.MinValue);
        foreach (ICentreline piece in atOrigin.Pieces)
        {
            for (int k = 0; k <= 20; k++)
            {
                Vector3 p = piece.FrameAt(piece.Length * k / 20.0f).Position;
                low = Vector2.Min(low, new Vector2(p.X, p.Z));
                high = Vector2.Max(high, new Vector2(p.X, p.Z));
            }
        }

        Vector2 middle = (low + high) / 2.0f;
        return BuildLap(Frame.Level(new Vector3(centre.X - middle.X, centre.Y, centre.Z - middle.Y), 0.0f), a, b);
    }

    private static Chain BuildLap(Frame start, float firstStraight, float thirdStraight)
    {
        var chain = new Chain(start);
        for (int i = 0; i < Corners.Length; i++)
        {
            float straight = i == 0 ? firstStraight : i == 2 ? thirdStraight : Straights[i];
            (float angle, float radius, float bank) = Corners[i];
            chain.Line(straight).Turn(angle, radius, bank, bank > 0.0f ? 5.0f : 0.0f);
        }

        return chain;
    }

    public IReadOnlyList<RoadRun> Runs { get; }

    public IReadOnlyList<Station> Stations { get; }

    /// <summary>The kicker's lip: where the jump oracle measures from.</summary>
    public Frame Lip { get; }

    /// <summary>How far the flat table runs past the lip before the down-ramp starts.</summary>
    public float TableLengthM => 20.0f;

    /// <summary>How far past the lip, along the ground, the down-ramp levels out onto the run-out.</summary>
    public float DownRampEndM { get; }

    public float PlateauHeight { get; }

    public LoopCentreline Loop { get; }

    public Frame LoopEntry { get; }

    public Frame LoopExit { get; }

    public BumpField Bumps { get; }

    /// <summary>The middle of the bump patch, on the plain.</summary>
    public Vector3 BumpCentre { get; }
}
