using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>The shortlist's pieces (PIECES-PLAN, P1 on): their shapes on the grid, and tracks laid through them.</summary>
public class MorePiecesTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static PlacedPiece Placed(string piece, int column, int row, bool mirrored = false) =>
        new(Catalogue.Value.Find($"base:{piece}")!, new Placement($"base:{piece}", new Tile(column, row), 0, (int)Compass.North, mirrored));

    private static TrackPiece P(string piece, int column, int row, Compass facing = Compass.North, bool mirrored = false) =>
        new() { Piece = $"base:{piece}", At = [column, row], Facing = facing, Mirrored = mirrored };

    /// <summary>The 70 m corner, on 4 × 4 tiles (M2-PLAN fact 1's 20n − 10), leaves by its far corner's right side.</summary>
    [Theory]
    [InlineData("corner-70")]
    [InlineData("corner-70-banked")]
    public void TheSeventyMetreCornerTurnsOnFourByFour(string piece)
    {
        PlacedPiece corner = Placed(piece, 10, 20);
        Assert.Equal(16, corner.Tiles.Count());
        Assert.Equal(new WorldConnector(new Tile(13, 17), Compass.East, 0, ConnectorKind.Road), corner.Out);
        TurnCentreline turn = Assert.IsType<TurnCentreline>(Assert.Single(corner.Road().Pieces));
        Assert.Equal(70.0f, turn.Length / (MathF.PI / 2.0f), 2);
    }

    /// <summary>
    /// The U-turn's way out faces back the way it came, two tiles to the right (to the left, mirrored), the one piece
    /// whose way out is on its back: a road runs on beside the road that led in.
    /// </summary>
    [Theory]
    [InlineData(false, 12)]
    [InlineData(true, 8)]
    public void TheUTurnComesBackBesideItself(bool mirrored, int column)
    {
        PlacedPiece turn = Placed("u-turn", 10, 20, mirrored);
        Assert.Equal(6, turn.Tiles.Count());
        Assert.Equal(new WorldConnector(new Tile(column, 20), Compass.South, 0, ConnectorKind.Road), turn.Out);
        Assert.Equal(new Tile(column, 21), turn.Out.Beyond);
        Assert.True(turn.PathMisfitM() <= PieceDef.FitM);
    }

    [Fact]
    public void ASprintDoublesBackThroughAUTurn()
    {
        foreach (string turn in new[] { "u-turn", "u-turn-banked" })
        {
            var track = new Track
            {
                Id = "test:u-turn",
                Name = "U-turn",
                Pieces = [P("start", 10, 22), P("straight", 10, 21), P(turn, 10, 20), P("straight", 12, 21, Compass.South), P("finish", 12, 22, Compass.South)],
            };
            LaidTrack laid = track.Lay(Catalogue.Value);
            Assert.Empty(laid.Problems);
            Assert.Equal(5, laid.Route.Count);
        }
    }

    /// <summary>
    /// The hump (P2) climbs 2 m over four 50 m arcs and comes down again in two tiles, level at both ends; its crest is
    /// in the middle.
    /// </summary>
    [Fact]
    public void TheHumpRisesTwoMetresAndComesBackLevel()
    {
        PlacedPiece hump = Placed("hump", 10, 20);
        Assert.Equal(2, hump.Tiles.Count());
        Assert.Equal(new WorldConnector(new Tile(10, 19), Compass.North, 0, ConnectorKind.Road), hump.Out);
        Assert.True(hump.PathMisfitM() <= PieceDef.FitM);

        (float along, float height) = Highest(hump);
        Assert.Equal(2.02f, height, 2);
        Assert.Equal(hump.Road().Pieces.Sum(c => c.Length) / 2.0f, along, 1);
    }

    /// <summary>
    /// The tabletop (P2) goes up a 15° ramp to a lip 3 m up, along a flat top for 16 m, and down a 10° ramp to the ground
    /// again, in three tiles: the top is the highest road, flat from the lip on.
    /// </summary>
    [Fact]
    public void TheTabletopHasALipAFlatTopAndAWayDown()
    {
        PlacedPiece table = Placed("tabletop", 10, 20);
        Assert.Equal(3, table.Tiles.Count());
        Assert.Equal(new WorldConnector(new Tile(10, 18), Compass.North, 0, ConnectorKind.Road), table.Out);
        Assert.True(table.PathMisfitM() <= PieceDef.FitM);

        IReadOnlyList<ICentreline> steps = table.Road().Pieces;
        float start = table.Start.Position.Y;
        Assert.Equal(3.0f, steps[2].FrameAt(steps[2].Length).Position.Y - start, 3);
        Assert.Equal(3.0f, steps[3].FrameAt(steps[3].Length).Position.Y - start, 3);
        Assert.Equal(16.0f, steps[3].Length, 3);
        Assert.Equal(3.0f, Highest(table).Height, 3);
    }

    /// <summary>
    /// Both are stunts: each has a speed hint, so the autodriver takes it at the hint down the middle, as the jumps,
    /// rather than holding the car under its crest.
    /// </summary>
    [Theory]
    [InlineData("hump")]
    [InlineData("tabletop")]
    public void TheHumpAndTheTabletopAreStunts(string piece)
    {
        PieceDef def = Catalogue.Value.Find($"base:{piece}")!;
        Assert.True(TrackRacing.IsStunt(def));
        Assert.Equal(2, def.SpeedKmh!.Length);
    }

    [Fact]
    public void ASprintGoesOverAHumpAndATabletop()
    {
        var track = new Track
        {
            Id = "test:hump",
            Name = "Hump and tabletop",
            Pieces = [P("start", 10, 28), P("straight", 10, 27), P("hump", 10, 26), P("straight", 10, 24), P("tabletop", 10, 23), P("straight", 10, 20), P("finish", 10, 19)],
        };
        LaidTrack laid = track.Lay(Catalogue.Value);
        Assert.Empty(laid.Problems);
        Assert.Equal(7, laid.Route.Count);
    }

    /// <summary>
    /// The dirt and sand straights (P3) are the straight's road in another surface, which lends the car less grip: the
    /// same tile, the same way out, the same 20 m line.
    /// </summary>
    [Theory]
    [InlineData("straight-dirt", "base:dirt")]
    [InlineData("straight-sand", "base:sand")]
    public void TheSurfaceStraightsAreTheStraightInAnotherSurface(string piece, string surface)
    {
        PlacedPiece straight = Placed(piece, 10, 20);
        Assert.Equal(Placed("straight", 10, 20).Out, straight.Out);
        Assert.Equal(20.0f, Assert.Single(straight.Road().Pieces).Length, 3);
        Assert.Equal(surface, straight.Def.Surface);
        Assert.True(Surfaces.Find(surface)!.Grip < Surfaces.Road.Grip);
        Assert.False(TrackRacing.IsStunt(straight.Def));
    }

    /// <summary>
    /// A track's fingerprint reads each piece's surface, so a road straight swapped for a dirt one is another track to a
    /// ghost, as it drives differently; the road straights in every track already made keep theirs.
    /// </summary>
    [Fact]
    public void ADirtStraightIsAnotherTrackToAGhost()
    {
        Track Sprint(string middle) => new()
        {
            Id = "test:surface",
            Name = "Surface",
            Pieces = [P("start", 10, 22), P(middle, 10, 21), P("finish", 10, 20)],
        };

        string road = TrackFingerprint.Of(Sprint("straight"), Catalogue.Value);
        Assert.NotEqual(road, TrackFingerprint.Of(Sprint("straight-dirt"), Catalogue.Value));
        Assert.NotEqual(road, TrackFingerprint.Of(Sprint("straight-sand"), Catalogue.Value));
    }

    /// <summary>
    /// The steep-banked corner (P3, reshaped at his word) sits on the 50 m corner's 3 × 3 tiles and leaves by its way
    /// out, but tightens gradually over each half: nearly straight at its ends, about 27 m through its middle, where it's
    /// banked 40° and its outer edge stands its width times sin 40°, 6.4 m, over the way in (the bank pivots on the inner
    /// edge). The bank leads the curve: halfway into the corner's first half the curve is at half its middle's and the bank
    /// already at 71% of 40°. It's a stunt with a hint, since the speed plan can't see what holds it (the springs, the lock).
    /// </summary>
    [Fact]
    public void TheSteepCornerTightensGraduallyOnTheFiftyMetreCornersTiles()
    {
        PlacedPiece steep = Placed("corner-50-steep", 10, 20);
        PlacedPiece gentle = Placed("corner-50-banked", 10, 20);
        Assert.Equal(gentle.Out, steep.Out);
        Assert.Equal(gentle.Tiles, steep.Tiles);
        Assert.True(steep.PathMisfitM() <= PieceDef.FitM);
        Assert.True(TrackRacing.IsStunt(steep.Def));

        var turn = Assert.IsType<TransitionTurnCentreline>(Assert.Single(steep.Road().Pieces));
        Assert.Equal(27.0f, turn.MiddleRadius, 0);

        // A quarter of the way round, the curve is at half its middle's, and the bank at 40° times the root of a half.
        float quarter = turn.Length / 4.0f;
        Assert.Equal(40.0f * MathF.Sqrt(0.5f), BankDeg(turn.FrameAt(quarter)), 1);
        Assert.Equal(0.5f / turn.MiddleRadius * (180.0f / MathF.PI), Vector3Angle(turn.FrameAt(quarter - 0.5f).Forward, turn.FrameAt(quarter + 0.5f).Forward), 2);

        // A metre at either end turns a small fraction of a metre through the middle.
        float Turned(float from) => Vector3Angle(turn.FrameAt(from).Forward, turn.FrameAt(from + 1.0f).Forward);
        Assert.True(Turned(0.0f) < Turned(turn.Length / 2.0f) / 20.0f);
        Assert.True(Turned(turn.Length - 1.0f) < Turned(turn.Length / 2.0f) / 20.0f);
        Assert.Equal(1.0f / turn.MiddleRadius * (180.0f / MathF.PI), Turned(turn.Length / 2.0f - 0.5f), 2);

        // Flat at its ends, 40° through its middle.
        Frame middle = turn.FrameAt(turn.Length / 2.0f);
        Assert.Equal(0.0f, BankDeg(turn.FrameAt(0.0f)), 2);
        Assert.Equal(0.0f, BankDeg(turn.FrameAt(turn.Length)), 2);
        Assert.Equal(40.0f, BankDeg(middle), 1);
        float halfSin = (steep.Def.WidthM / 2.0f) * MathF.Sin(40.0f * MathF.PI / 180.0f);
        Assert.Equal(halfSin, middle.Position.Y - steep.Start.Position.Y, 2);
        Assert.Equal(6.43f, 2.0f * halfSin, 2);
    }

    /// <summary>
    /// A turn with gradual ends, through any angle up to 180° and with any transition, ends where the arc it stands
    /// in for ends, facing the same way, to well under the grid's millimetre; with no bank it stays on the level.
    /// </summary>
    [Theory]
    [InlineData(90.0f, 50.0f, 0.25f)]
    [InlineData(90.0f, 30.0f, 0.5f)]
    [InlineData(-45.0f, 70.0f, 0.1f)]
    [InlineData(180.0f, 20.0f, 0.3f)]
    [InlineData(-180.0f, 40.0f, 0.5f)]
    [InlineData(135.0f, 25.0f, 0.4f)]
    public void AGradualTurnEndsWhereItsArcEnds(float angle, float radius, float transition)
    {
        Frame start = Frame.Level(new System.Numerics.Vector3(3.0f, 0.0f, -7.0f), 30.0f);
        Frame arc = new TurnCentreline(start, angle, radius) is var a ? a.FrameAt(a.Length) : default;
        var eased = new TransitionTurnCentreline(start, angle, radius, transition);
        Frame end = eased.FrameAt(eased.Length);
        Assert.True(System.Numerics.Vector3.Distance(arc.Position, end.Position) < 2e-4f);
        Assert.True(Vector3Angle(arc.Forward, end.Forward) < 0.01f);
        Assert.True(eased.MiddleRadius < radius);
        Assert.All(Enumerable.Range(0, 21), k => Assert.Equal(0.0f, eased.FrameAt(eased.Length * k / 20.0f).Position.Y, 4));
    }

    /// <summary>A transition must be a share of the corner from above 0 to a half, on a turn of at most 180°.</summary>
    [Theory]
    [InlineData(0.0f, 90.0f)]
    [InlineData(0.6f, 90.0f)]
    [InlineData(0.25f, 270.0f)]
    public void AGradualTurnRefusesWhatItCantMake(float transition, float angle)
    {
        PieceDef def = Catalogue.Value.Find("base:corner-50-steep")! with
        {
            Path = [new PathStep { Turn = new TurnStep { Angle = angle, Radius = 50.0f, Bank = 40.0f, Transition = transition } }],
        };
        Assert.Contains(def.Problems(), p => p.Contains("transition", StringComparison.Ordinal));
    }

    private static float Vector3Angle(System.Numerics.Vector3 a, System.Numerics.Vector3 b) =>
        MathF.Acos(Math.Clamp(System.Numerics.Vector3.Dot(System.Numerics.Vector3.Normalize(a), System.Numerics.Vector3.Normalize(b)), -1.0f, 1.0f)) * (180.0f / MathF.PI);

    private static float BankDeg(Frame frame) => MathF.Acos(Math.Clamp(frame.Up.Y, -1.0f, 1.0f)) * (180.0f / MathF.PI);

    /// <summary>
    /// The autodriver's plan reads each piece's surface (P3): with the straights before a corner made sand, it brakes for
    /// the corner sooner and from no faster, and the corner itself, road, is planned as it was. On a track of road the
    /// line lends the car all its grip everywhere, which keeps every plan before P3 to the last bit.
    /// </summary>
    [Fact]
    public void TheAutodriverBrakesSoonerOnSand()
    {
        Track Sprint(string run) => new()
        {
            Id = "test:sand",
            Name = "Sand",
            Pieces =
            [
                P("start", 10, 28), P("straight", 10, 27), P("straight", 10, 26), P("straight", 10, 25), P("straight", 10, 24),
                P(run, 10, 23), P(run, 10, 22), P(run, 10, 21), P("corner-30", 10, 20), P("straight", 12, 19, Compass.East), P("finish", 13, 19, Compass.East),
            ],
        };

        CarSpec spec = CarSpec.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "cars", "buggy.json")));
        RacingLine road = Sprint("straight").Lay(Catalogue.Value).CentreLine(spec);
        RacingLine sand = Sprint("straight-sand").Lay(Catalogue.Value).CentreLine(spec);
        Assert.Equal(road.Count, sand.Count);
        Assert.All(Enumerable.Range(0, road.Count), i => Assert.Equal(1.0f, road.GripAt(i)));
        Assert.Contains(Enumerable.Range(0, sand.Count), i => sand.GripAt(i) == Surfaces.Sand.Grip);

        float[] onRoad = road.SpeedProfile(spec, 0.8f);
        float[] onSand = sand.SpeedProfile(spec, 0.8f);
        Assert.All(Enumerable.Range(0, road.Count), i => Assert.True(onSand[i] <= onRoad[i] + 1e-4f));

        // Where the road plan is still flat out, the sand plan is already braking.
        int braking = Enumerable.Range(0, road.Count).First(i => onRoad[i] < onRoad.Max() - 0.01f);
        Assert.True(onSand[braking - 10] < onRoad[braking - 10] - 0.5f);

        // The corner's own speed doesn't change: it's road.
        int apex = Enumerable.Range(0, road.Count).MinBy(i => onRoad[i]);
        Assert.Equal(onRoad[apex], onSand[apex], 3);
    }

    [Fact]
    public void ASprintRunsOverDirtAndSandAndRoundTheSteepBank()
    {
        var track = new Track
        {
            Id = "test:surfaces",
            Name = "Dirt, sand and a steep bank",
            Pieces = [P("start", 10, 26), P("straight-dirt", 10, 25), P("straight-sand", 10, 24), P("corner-50-steep", 10, 23), P("straight", 13, 21, Compass.East), P("finish", 14, 21, Compass.East)],
        };
        LaidTrack laid = track.Lay(Catalogue.Value);
        Assert.Empty(laid.Problems);
        Assert.Equal(6, laid.Route.Count);
    }

    /// <summary>The highest point of a piece's road, sampled every 10 cm: how far along its road, and how far above its way in.</summary>
    private static (float Along, float Height) Highest(PlacedPiece piece)
    {
        float start = piece.Start.Position.Y;
        (float Along, float Height) best = (0.0f, float.MinValue);
        float before = 0.0f;
        foreach (ICentreline c in piece.Road().Pieces)
        {
            for (float s = 0.0f; s <= c.Length; s += 0.1f)
            {
                float height = c.FrameAt(s).Position.Y - start;
                if (height > best.Height + 1e-4f)
                {
                    best = (before + s, height);
                }
            }

            before += c.Length;
        }

        return best;
    }

    /// <summary>The editor lays onto a U-turn's end as onto any other: the road goes on south from beside where it came in.</summary>
    [Fact]
    public void TheEditorLaysOnFromAUTurn()
    {
        var editor = new TrackEditor(TrackEditor.Empty(), Catalogue.Value);
        Assert.Null(editor.Place(new Placement("base:u-turn", new Tile(10, 20), 0, (int)Compass.North, false)));
        WorldConnector end = Assert.Single(editor.OpenEnds());
        Placement? next = editor.Next(end, "base:straight", false, out _);
        Assert.Equal(new Placement("base:straight", new Tile(12, 21), 0, (int)Compass.South, false), next);
        Assert.Null(editor.Fits(next!));
    }
}
