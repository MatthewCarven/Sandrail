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
        ];
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
