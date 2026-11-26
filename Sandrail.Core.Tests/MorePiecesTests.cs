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
