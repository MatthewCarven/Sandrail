using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tracks;

/// <summary>
/// Every piece in the catalogue laid out on the grid, one at a time (M2-PLAN, M2b's showroom, begun in
/// M2a): each behind a run-up of straights and followed by a run-out, with a station at the start of its
/// run-up, for screenshots and for driving one piece at a time.
/// </summary>
/// <remarks>
/// Groups go left to right across the grid's south and wrap northward into bands. A group's tiles are
/// found by placing it at the grid's corner and measuring, then it's shifted to where it fits, so a
/// piece of any shape or exit finds room.
/// </remarks>
/// <summary>The showroom: every piece placed, a station at each run-up's start, and each piece with its run-up and run-out as a group.</summary>
public sealed record ShowroomLayout(IReadOnlyList<PlacedPiece> Pieces, IReadOnlyList<Station> Stations, IReadOnlyList<IReadOnlyList<PlacedPiece>> Groups);

public static class Showroom
{
    /// <summary>Straights before each piece: 80 m, about what the car needs from rest to 100 km/h.</summary>
    public const int RunUp = 4;

    /// <summary>Straights before a piece meant to be taken faster than 100 km/h: 140 m, enough for about 120.</summary>
    public const int LongRunUp = 7;

    /// <summary>How many straights of run-up a piece gets, by the speed it's meant for.</summary>
    public static int RunUpFor(PieceDef piece) => piece.SpeedKmh is float[] speed && speed[1] > 100.0f ? LongRunUp : RunUp;

    /// <summary>Straights after it, along its way out.</summary>
    public const int RunOut = 2;

    public static ShowroomLayout Lay(PieceCatalogue catalogue)
    {
        PieceDef straight = catalogue.Find("base:straight")
            ?? throw new InvalidOperationException("The showroom lays run-ups of base:straight, and the catalogue hasn't one.");

        var pieces = new List<PlacedPiece>();
        var stations = new List<Station>();
        var groups = new List<IReadOnlyList<PlacedPiece>>();
        int cursor = 1;
        int bandBottom = TrackGrid.Size - 2;
        int bandHeight = 0;

        // A landing is shown after each launch ramp, across its gap, not on its own.
        foreach (PieceDef piece in catalogue.All.Where(p => p.In != ConnectorKind.Landing))
        {
            List<Placement> group = Group(piece, straight, catalogue);
            var placed = group.Select(p => new PlacedPiece(catalogue.Find(p.Piece)!, p)).ToList();
            var tiles = placed.SelectMany(p => p.Tiles).ToList();
            int minColumn = tiles.Min(t => t.Column);
            int maxColumn = tiles.Max(t => t.Column);
            int minRow = tiles.Min(t => t.Row);
            int maxRow = tiles.Max(t => t.Row);
            int width = maxColumn - minColumn + 1;
            int height = maxRow - minRow + 1;

            if (cursor + width > TrackGrid.Size - 1)
            {
                cursor = 1;
                bandBottom -= bandHeight + 1;
                bandHeight = 0;
            }

            int dc = cursor - minColumn;
            int dr = bandBottom - maxRow;
            foreach (Placement p in group)
            {
                pieces.Add(new PlacedPiece(catalogue.Find(p.Piece)!, p with { At = new Tile(p.At.Column + dc, p.At.Row + dr) }));
            }

            groups.Add(pieces.GetRange(pieces.Count - group.Count, group.Count));
            PlacedPiece first = pieces[^group.Count];
            string hint = piece.SpeedKmh is float[] speed ? $"take it at {speed[0]:F0}-{speed[1]:F0} km/h" : "drive it as it comes";
            stations.Add(new Station(stations.Count + 1, piece.Name, first.Start, hint));

            cursor += width + 1;
            bandHeight = Math.Max(bandHeight, height);
        }

        return new ShowroomLayout(pieces, stations, groups);
    }

    /// <summary>
    /// A piece with its run-up and run-out, placed from the grid's corner, facing north. A piece that
    /// goes down starts as high as it goes down, its run-up raised with it. A launch ramp is followed
    /// across its gap by the catalogue's landing, then the run-out.
    /// </summary>
    private static List<Placement> Group(PieceDef piece, PieceDef straight, PieceCatalogue catalogue)
    {
        int level = Math.Max(0, -piece.Out.Rise);
        int runUp = RunUpFor(piece);
        var group = new List<Placement>();
        for (int i = 0; i < runUp; i++)
        {
            group.Add(new Placement(straight.Id, new Tile(0, -i), level, 0, false));
        }

        var main = new Placement(piece.Id, new Tile(0, -runUp), level, 0, false);
        group.Add(main);

        WorldConnector exit = new PlacedPiece(piece, main).Out;
        if (exit.Kind == ConnectorKind.Lip && catalogue.All.FirstOrDefault(p => p.In == ConnectorKind.Landing) is PieceDef landing)
        {
            var down = new Placement(landing.Id, exit.Landing, exit.Level, (int)exit.Facing, false);
            group.Add(down);
            exit = new PlacedPiece(landing, down).Out;
        }

        if (exit.Kind == ConnectorKind.Road)
        {
            Tile at = exit.Beyond;
            for (int i = 0; i < RunOut; i++)
            {
                group.Add(new Placement(straight.Id, at, exit.Level, (int)exit.Facing, false));
                at = at.Step(exit.Facing);
            }
        }

        return group;
    }
}
