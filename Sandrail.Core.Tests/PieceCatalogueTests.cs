using System.Globalization;
using System.Numerics;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

public class PieceCatalogueTests
{
    private static IEnumerable<(string Name, string Json)> ShippedFiles() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)));

    private static PieceCatalogue Shipped() => PieceCatalogue.Parse(ShippedFiles());

    /// <summary>A right-hand 90° corner of 30 m on 2 × 2 tiles, as the starter set will have (M2-PLAN decision 1).</summary>
    private const string Corner30 = """
        {
          "id": "test:corner-30",
          "name": "Corner, 30 m",
          "tiles": [[0, 0], [1, 0], [0, 1], [1, 1]],
          "out": { "tile": [1, 1], "side": "right" },
          "path": [{ "turn": { "angle": 90, "radius": 30 } }]
        }
        """;

    /// <summary>A climb of one level over two tiles (M2-PLAN decision 2): two arcs, up and over.</summary>
    private static string Slope()
    {
        float angle = 2.0f * MathF.Atan(TrackGrid.LevelM / (2.0f * TrackGrid.TileM)) * (180.0f / MathF.PI);
        float radius = TrackGrid.TileM / MathF.Sin(angle * MathF.PI / 180.0f);
        string a = angle.ToString("R", CultureInfo.InvariantCulture);
        string r = radius.ToString("R", CultureInfo.InvariantCulture);
        return $$"""
            {
              "id": "test:slope",
              "name": "Slope",
              "tiles": [[0, 0], [0, 1]],
              "out": { "tile": [0, 1], "side": "front", "rise": 1 },
              "path": [{ "pitch": { "angle": {{a}}, "radius": {{r}} } }, { "pitch": { "angle": -{{a}}, "radius": {{r}} } }]
            }
            """;
    }

    private static IEnumerable<Placement> EveryWay(string id)
    {
        for (int rotation = 0; rotation < 4; rotation++)
        {
            yield return new Placement(id, new Tile(12, 14), 2, rotation, false);
            yield return new Placement(id, new Tile(12, 14), 2, rotation, true);
        }
    }

    [Fact]
    public void TheShippedCatalogueLoadsAndHasTheStraight()
    {
        PieceCatalogue catalogue = Shipped();

        Assert.NotNull(catalogue.Find("base:straight"));
        Assert.Null(catalogue.Find("base:nothing"));
    }

    [Fact]
    public void EveryShippedPieceEndsAtItsWayOutTurnedEveryWayAndMirrored()
    {
        foreach (PieceDef piece in Shipped().All)
        {
            foreach (Placement placement in EveryWay(piece.Id))
            {
                float off = new PlacedPiece(piece, placement).PathMisfitM();
                Assert.True(off <= PieceDef.FitM, $"{piece.Id} turned {placement.Rotation}{(placement.Mirrored ? " mirrored" : string.Empty)}: off by {off:0.#####} m");
            }
        }
    }

    [Fact]
    public void ACornerEndsAtItsWayOutEveryWayAndAMirroredOneTurnsLeft()
    {
        PieceDef corner = PieceCatalogue.ParsePiece(Corner30);
        foreach (Placement placement in EveryWay(corner.Id))
        {
            Assert.True(new PlacedPiece(corner, placement).PathMisfitM() <= PieceDef.FitM, $"turned {placement.Rotation}, mirrored {placement.Mirrored}");
        }

        // Facing north from (10, 10): it leaves east from (11, 9); mirrored, west from (9, 9).
        var right = new PlacedPiece(corner, new Placement(corner.Id, new Tile(10, 10), 0, 0, false));
        Assert.Equal(new WorldConnector(new Tile(11, 9), Compass.East, 0, ConnectorKind.Road), right.Out);
        var left = new PlacedPiece(corner, new Placement(corner.Id, new Tile(10, 10), 0, 0, true));
        Assert.Equal(new WorldConnector(new Tile(9, 9), Compass.West, 0, ConnectorKind.Road), left.Out);
        Assert.Equal([new Tile(10, 10), new Tile(9, 10), new Tile(10, 9), new Tile(9, 9)], left.Tiles);
    }

    [Fact]
    public void ASlopeClimbsItsLevelAndEndsLevel()
    {
        PieceDef slope = PieceCatalogue.ParsePiece(Slope());
        foreach (Placement placement in EveryWay(slope.Id))
        {
            Assert.True(new PlacedPiece(slope, placement).PathMisfitM() <= PieceDef.FitM, $"turned {placement.Rotation}");
        }

        var placed = new PlacedPiece(slope, new Placement(slope.Id, new Tile(4, 4), 1, 0, false));
        Assert.Equal(2, placed.Out.Level);
        Assert.Equal(placed.In.Frame.Position.Y + TrackGrid.LevelM, placed.Road().End.Position.Y, 3);
    }

    [Fact]
    public void AStraightsConnectorsAreOnTheEdgesItClaims()
    {
        PieceDef straight = Shipped().Find("base:straight")!;

        var north = new PlacedPiece(straight, new Placement(straight.Id, new Tile(5, 5), 0, 0, false));
        Assert.Equal(new WorldConnector(new Tile(5, 5), Compass.South, 0, ConnectorKind.Road), north.In);
        Assert.Equal(new WorldConnector(new Tile(5, 5), Compass.North, 0, ConnectorKind.Road), north.Out);
        Assert.Equal(new Tile(5, 4), north.Out.Beyond);

        var east = new PlacedPiece(straight, new Placement(straight.Id, new Tile(5, 5), 3, 1, false));
        Assert.Equal(Compass.East, east.Out.Facing);
        Assert.Equal(new Tile(6, 5), east.Out.Beyond);
        Assert.Equal(3, east.Out.Level);
    }

    [Fact]
    public void TwoStraightsInARowMeetExactly()
    {
        PieceDef straight = Shipped().Find("base:straight")!;
        var first = new PlacedPiece(straight, new Placement(straight.Id, new Tile(7, 7), 1, 1, false));
        var second = new PlacedPiece(straight, new Placement(straight.Id, first.Out.Beyond, 1, 1, false));

        Assert.True(Vector3.Distance(first.Road().End.Position, second.Start.Position) < PieceDef.FitM);
        Assert.Equal(first.Out.Facing, TrackGrid.Opposite(second.In.Facing));
    }

    [Fact]
    public void TheGridSitsOnThePlain()
    {
        // 32 tiles of 20 m, centred: tile (16, 16) has its middle 10 m east and south of the plain's.
        Assert.Equal(new Vector3(10.0f, TestGroundLayout.Lift, 10.0f), TrackGrid.Middle(new Tile(16, 16), 0));
        Assert.Equal(-320.0f, TrackGrid.EdgeMiddle(new Tile(0, 5), Compass.West, 0).X);
        Assert.Equal(TestGroundLayout.Lift + 5.0f, TrackGrid.Middle(new Tile(3, 3), 2).Y, 5);
        Assert.Equal(Compass.West, TrackGrid.Turned(Compass.North, -1));
        Assert.Equal(Compass.South, TrackGrid.Opposite(Compass.North));
        Assert.Equal(new Tile(3, 1), new Tile(3, 4).Step(Compass.North, 3));
    }

    [Fact]
    public void TheShowroomLaysEveryPieceOnceOnTheGridWithoutOverlap()
    {
        PieceCatalogue catalogue = PieceCatalogue.Parse(ShippedFiles().Append(("corner.json", Corner30)).Append(("slope.json", Slope())));
        ShowroomLayout layout = Showroom.Lay(catalogue);
        IReadOnlyList<PlacedPiece> pieces = layout.Pieces;
        IReadOnlyList<Station> stations = layout.Stations;

        // A station a piece, and two for the crossing (PIECES-PLAN, X2).
        Assert.Equal(catalogue.All.Count(p => p.In != ConnectorKind.Landing) + 2, stations.Count);
        Assert.Contains(stations, s => s.Name == Showroom.CrossingUnder);

        // Every tile covered once, but the crossing's, where one road runs clear over the other on pillars.
        var tiles = pieces.SelectMany(p => p.Tiles).ToList();
        var shared = tiles.GroupBy(t => t).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Tile crossing = Assert.Single(shared);
        var there = pieces.Where(p => p.Tiles.Contains(crossing)).ToList();
        Assert.True(Stacking.Of(there[0], there[1], crossing).Clear);
        Assert.Single(there, p => p.Pillars);
        Assert.NotEmpty(layout.Legs);
        Assert.All(tiles, t => Assert.True(TrackGrid.Contains(t), $"{t} is off the grid"));

        // Each run-up meets its piece, and each piece its run-out.
        for (int i = 1; i < pieces.Count; i++)
        {
            if (pieces[i - 1].Out.Beyond == pieces[i].Placement.At)
            {
                Assert.True(Vector3.Distance(pieces[i - 1].Road().End.Position, pieces[i].Start.Position) < PieceDef.FitM);
            }
        }
    }

    /// <summary>
    /// The starter set stays where it always was in the showroom (PIECES-PLAN fact 9): the pieces oracle drives it, so
    /// a piece added since, whatever its file is called, comes after it and moves nothing.
    /// </summary>
    [Fact]
    public void TheShowroomsStarterSetStaysPutWhateverIsAdded()
    {
        PieceCatalogue starter = PieceCatalogue.Parse(ShippedFiles().Where(f => Showroom.StarterSet.Contains(PieceCatalogue.ParsePiece(f.Json).Id)));
        Assert.Equal(Showroom.StarterSet.Count, starter.All.Count);
        ShowroomLayout before = Showroom.Lay(starter);
        ShowroomLayout now = Showroom.Lay(Shipped());

        Assert.Equal(before.Stations, now.Stations.Take(before.Stations.Count));
        Assert.Equal(before.Pieces.Select(p => p.Placement), now.Pieces.Take(before.Pieces.Count).Select(p => p.Placement));
        Assert.Empty(before.Legs);
    }

    [Fact]
    public void TheLoopLeavesOneTileAcrossSquareToTheGridAndStandsTall()
    {
        PieceDef loop = Shipped().Find("base:loop")!;
        var placed = new PlacedPiece(loop, new Placement(loop.Id, new Tile(10, 20), 0, 0, false));

        Assert.Equal(new WorldConnector(new Tile(11, 16), Compass.North, 0, ConnectorKind.Road), placed.Out);
        float top = placed.Road().Pieces.Max(p => Enumerable.Range(0, 101).Max(k => p.FrameAt(p.Length * k / 100.0f).Position.Y));
        Assert.InRange(top, 22.0f, 24.0f);

        // Its S-bends are gentle: about 120 m, 0.6 g at the loop's 97 km/h (M2-PLAN fact 4).
        TurnCentreline bend = placed.Road().Pieces.OfType<TurnCentreline>().First();
        Assert.InRange(bend.Length / (bend.TurnDeg * MathF.PI / 180.0f), 115.0f, 130.0f);

        var mirrored = new PlacedPiece(loop, new Placement(loop.Id, new Tile(10, 20), 0, 0, true));
        Assert.Equal(new Tile(9, 16), mirrored.Out.Tile);
    }

    [Fact]
    public void ALipIsRaisedAndPitchedAndItsLandingIsAcrossTheGap()
    {
        PieceDef ramp = Shipped().Find("base:launch-short")!;
        var placed = new PlacedPiece(ramp, new Placement(ramp.Id, new Tile(6, 20), 0, 0, false));
        WorldConnector lip = placed.Out;

        Assert.Equal(ConnectorKind.Lip, lip.Kind);
        Assert.Equal(1, lip.Level);
        Assert.Equal(new Tile(6, 18), lip.Landing);
        Frame f = lip.Frame;
        Assert.Equal(TrackGrid.Origin.Y + 2.95673f, f.Position.Y, 3);
        Assert.Equal(20.0f, MathF.Asin(f.Forward.Y) * 180.0f / MathF.PI, 2);
    }

    [Fact]
    public void ALaunchInTheShowroomComesDownOnItsLandingAtItsLevel()
    {
        PieceCatalogue catalogue = Shipped();
        IReadOnlyList<PlacedPiece> pieces = Showroom.Lay(catalogue).Pieces;
        PlacedPiece launch = pieces.First(p => p.Def.Id == "base:launch-short");
        PlacedPiece landing = pieces[pieces.ToList().IndexOf(launch) + 1];

        Assert.Equal("base:landing", landing.Def.Id);
        Assert.Equal(launch.Out.Landing, landing.Placement.At);
        Assert.Equal(launch.Out.Level, landing.Placement.Level);
        Assert.Equal(TrackGrid.Opposite(launch.Out.Facing), landing.In.Facing);
    }

    [Fact]
    public void RaisedRoadSitsOnEarthworksAndALoopOnNone()
    {
        PieceCatalogue catalogue = Shipped();
        float Depth(string id, int level) => new PlacedPiece(catalogue.Find(id)!, new Placement(id, new Tile(5, 5), level, 0, false)).DepthM;

        Assert.Equal(0.5f, Depth("base:straight", 0), 3);
        Assert.Equal(0.5f + (1.2f * TrackGrid.LevelM), Depth("base:straight", 1), 2);
        Assert.Equal(0.5f + (1.2f * TrackGrid.LevelM), Depth("base:slope-up", 0), 2);
        Assert.Equal(0.5f, Depth("base:loop", 0), 3);
    }

    [Fact]
    public void ALoopThatCantFitItsLengthIsRefusedWithWhatItNeeds()
    {
        string tight = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces", "loop.json"))
            .Replace("\"length\": 100", "\"length\": 60");
        var e = Assert.Throws<InvalidDataException>(() => PieceCatalogue.ParsePiece(tight));
        Assert.Contains("need", e.Message);
    }

    [Fact]
    public void AMisspeltFieldIsRefusedByName()
    {
        var e = Assert.Throws<InvalidDataException>(() => PieceCatalogue.ParsePiece(Corner30.Replace("\"tiles\"", "\"tilez\"")));
        Assert.Contains("tilez", e.Message);
    }

    [Fact]
    public void AnUnknownOrNumberedKindIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => PieceCatalogue.ParsePiece(Corner30.Replace("\"right\"", "\"up\"")));
        Assert.Throws<InvalidDataException>(() => PieceCatalogue.ParsePiece(Corner30.Replace("\"right\"", "3")));
    }

    [Fact]
    public void ARoadThatMissesItsWayOutIsRefusedWithHowFar()
    {
        string short1 = Corner30.Replace("\"radius\": 30", "\"radius\": 29");
        var e = Assert.Throws<InvalidDataException>(() => PieceCatalogue.ParsePiece(short1));
        Assert.Contains("path ends", e.Message);
    }

    [Fact]
    public void AWayOutOffItsOwnTilesOrIntoThemIsRefused()
    {
        Assert.Contains(new PieceDef { Id = "t:x", Name = "x", Out = new PieceConnector { Tile = [2, 0] }, Path = [new PathStep { Line = 20 }] }.Problems(),
            p => p.Contains("isn't one of its tiles"));
        Assert.Contains(new PieceDef { Id = "t:x", Name = "x", Tiles = [[0, 0], [0, 1]], Path = [new PathStep { Line = 20 }] }.Problems(),
            p => p.Contains("opens onto another of its own tiles"));
    }

    [Fact]
    public void AStepMustBeExactlyOneThing()
    {
        var piece = new PieceDef { Id = "t:x", Name = "x", Path = [new PathStep { Line = 10, Pitch = new PitchStep { Angle = 5, Radius = 10 } }] };
        Assert.Contains(piece.Problems(), p => p.Contains("exactly one"));
        Assert.Contains(new PieceDef { Id = "t:x", Name = "x", Path = [new PathStep()] }.Problems(), p => p.Contains("exactly one"));
    }

    [Fact]
    public void JumpsLeaveByLipsAndComeDownAtLandings()
    {
        Assert.Contains(new PieceDef { Id = "t:x", Name = "x", In = ConnectorKind.Lip, Path = [new PathStep { Line = 20 }] }.Problems(), p => p.Contains("in can't be a lip"));
        Assert.Contains(new PieceDef { Id = "t:x", Name = "x", Out = new PieceConnector { Kind = ConnectorKind.Landing }, Path = [new PathStep { Line = 20 }] }.Problems(),
            p => p.Contains("out can't be a landing"));
    }

    [Fact]
    public void TwoPiecesWithOneIdAreRefusedTogether()
    {
        var e = Assert.Throws<InvalidDataException>(() => PieceCatalogue.Parse([("a.json", Corner30), ("b.json", Corner30)]));
        Assert.Contains("defined 2 times", e.Message);
    }

    [Fact]
    public void AnUnnamespacedIdOrUnknownSurfaceIsRefused()
    {
        Assert.Contains(new PieceDef { Id = "straight", Name = "x", Path = [new PathStep { Line = 20 }] }.Problems(), p => p.StartsWith("id", StringComparison.Ordinal));
        Assert.Contains(new PieceDef { Id = "t:x", Name = "x", Surface = "base:lava", Path = [new PathStep { Line = 20 }] }.Problems(), p => p.StartsWith("surface", StringComparison.Ordinal));
    }
}
