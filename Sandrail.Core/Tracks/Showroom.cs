using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tracks;

/// <summary>The showroom: every piece placed, a station at each run-up's start, and each piece with its run-up and run-out as a group; the legs under any deck.</summary>
public sealed record ShowroomLayout(IReadOnlyList<PlacedPiece> Pieces, IReadOnlyList<Station> Stations, IReadOnlyList<IReadOnlyList<PlacedPiece>> Groups)
{
    /// <summary>The legs under the crossing's deck (PIECES-PLAN, X2); none before crossings.</summary>
    public IReadOnlyList<Leg> Legs { get; init; } = [];
}

/// <summary>
/// Every piece in the catalogue laid out on the grid, one at a time (M2-PLAN, M2b's showroom, begun in
/// M2a): each behind a run-up of straights and followed by a run-out, with a station at the start of its
/// run-up, for screenshots and for driving one piece at a time.
/// </summary>
/// <remarks>
/// <para>
/// Groups go left to right across the grid's south and wrap northward into bands. A group's tiles are
/// found by placing it at the grid's corner and measuring, then it's shifted to where it fits, so a
/// piece of any shape or exit finds room.
/// </para>
/// <para>
/// <b>The starter set comes first, where it always was</b> (PIECES-PLAN fact 9): the pieces oracle drives it,
/// and a new piece file sorting among the old ones would otherwise move every station after it. Pieces added
/// since come after it, then the crossing, two stations on one block.
/// </para>
/// </remarks>
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

    /// <summary>The crossing's two stations: driven under the deck, and over it.</summary>
    public const string CrossingUnder = "Crossing, under";

    public const string CrossingOver = "Crossing, over";

    /// <summary>The seventeen pieces of M2 and M3, in the order the showroom has always laid them (their files' order).</summary>
    public static readonly IReadOnlyList<string> StarterSet =
    [
        "base:checkpoint", "base:chicane", "base:corner-30-banked", "base:corner-30", "base:corner-50-banked", "base:corner-50",
        "base:finish", "base:landing", "base:launch-long", "base:launch-short", "base:loop", "base:s-bend", "base:slope-down",
        "base:slope-up", "base:start-finish", "base:start", "base:straight",
    ];

    /// <summary>Where the crossing's block comes among <see cref="Added"/>.</summary>
    private const string CrossingBlock = "(crossing)";

    /// <summary>
    /// The pieces added since the starter set, in the order they were added, the crossing among them, so each new slice's
    /// stations come after the last one's and none moves: the <c>pieces2</c> oracle drives them. A piece not listed comes
    /// after these, in the catalogue's order.
    /// </summary>
    public static readonly IReadOnlyList<string> Added =
    [
        "base:slope-down-steep", "base:slope-up-steep", CrossingBlock,
        "base:corner-70", "base:corner-70-banked", "base:u-turn", "base:u-turn-banked",
    ];

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

        // Each block is laid where it fits, its groups together: one group for a piece, two for the crossing. The
        // starter set in bands, as it always was; anything since in the first free spot, a tile clear of the rest, so
        // the gaps the bands leave are filled before anything runs off the grid.
        var taken = new HashSet<Tile>();
        List<Tile> TilesOf(IReadOnlyList<(string Name, string Hint, List<Placement> Group)> block) =>
            block.SelectMany(b => b.Group).SelectMany(p => new PlacedPiece(catalogue.Find(p.Piece)!, p).Tiles).ToList();

        void Block(IReadOnlyList<(string Name, string Hint, List<Placement> Group)> block, bool starter)
        {
            // Facing north as laid; where that finds no room, on its side, facing east, which the space above a band
            // left by its tallest group often has.
            if (!starter && FreeSpot(TilesOf(block), taken) is null)
            {
                var turned = block.Select(b => (b.Name, b.Hint, b.Group.Select(QuarterTurned).ToList())).ToList();
                if (FreeSpot(TilesOf(turned), taken) is not null)
                {
                    block = turned;
                }
            }

            var tiles = TilesOf(block);
            int minColumn = tiles.Min(t => t.Column);
            int maxColumn = tiles.Max(t => t.Column);
            int minRow = tiles.Min(t => t.Row);
            int maxRow = tiles.Max(t => t.Row);
            int width = maxColumn - minColumn + 1;
            int height = maxRow - minRow + 1;

            int dc;
            int dr;
            if (starter || FreeSpot(tiles, taken) is not (int, int) spot)
            {
                if (cursor + width > TrackGrid.Size - 1)
                {
                    cursor = 1;
                    bandBottom -= bandHeight + 1;
                    bandHeight = 0;
                }

                dc = cursor - minColumn;
                dr = bandBottom - maxRow;
                cursor += width + 1;
                bandHeight = Math.Max(bandHeight, height);
            }
            else
            {
                (dc, dr) = spot;
            }

            taken.UnionWith(tiles.Select(t => new Tile(t.Column + dc, t.Row + dr)));
            foreach ((string name, string hint, List<Placement> group) in block)
            {
                foreach (Placement p in group)
                {
                    pieces.Add(new PlacedPiece(catalogue.Find(p.Piece)!, p with { At = new Tile(p.At.Column + dc, p.At.Row + dr) }));
                }

                groups.Add(pieces.GetRange(pieces.Count - group.Count, group.Count));
                stations.Add(new Station(stations.Count + 1, name, pieces[^group.Count].Start, hint));
            }
        }

        // The starter set, then what's been added in the order it was, then anything else; a landing is shown after
        // each launch ramp, across its gap, not on its own.
        IEnumerable<string> order = StarterSet.Concat(Added).Concat(catalogue.All.Select(p => p.Id).Where(id => !StarterSet.Contains(id) && !Added.Contains(id)));
        foreach (string id in order)
        {
            if (id == CrossingBlock)
            {
                if (Crossing(catalogue) is var (under, over))
                {
                    Block([(CrossingUnder, "under the deck, as fast as you like", under), (CrossingOver, "up two levels, over, and down", over)], false);
                }

                continue;
            }

            if (catalogue.Find(id) is not PieceDef piece || piece.In == ConnectorKind.Landing)
            {
                continue;
            }

            string hint = piece.SpeedKmh is float[] speed ? $"take it at {speed[0]:F0}-{speed[1]:F0} km/h" : "drive it as it comes";
            Block([(piece.Name, hint, Group(piece, straight, catalogue))], StarterSet.Contains(piece.Id));
        }

        // Where one road runs over another, it goes up on pillars, as on a track.
        var raised = new HashSet<int>();
        for (int a = 0; a < pieces.Count; a++)
        {
            for (int b = a + 1; b < pieces.Count; b++)
            {
                foreach (Tile tile in pieces[a].Tiles.Intersect(pieces[b].Tiles))
                {
                    Stack stack = Stacking.Of(pieces[a], pieces[b], tile);
                    if (!stack.Overlap)
                    {
                        raised.Add(ReferenceEquals(stack.Upper, pieces[a]) ? a : b);
                    }
                }
            }
        }

        if (raised.Count == 0)
        {
            return new ShowroomLayout(pieces, stations, groups);
        }

        var all = pieces.Select((p, k) => raised.Contains(k) ? new PlacedPiece(p.Def, p.Placement) { Pillars = true } : p).ToList();
        var swapped = groups.Select(g => (IReadOnlyList<PlacedPiece>)g.Select(p => all[pieces.IndexOf(p)]).ToList()).ToList();
        var legs = all.Where(p => p.Pillars).SelectMany(p => Stacking.Legs(p, all)).ToList();
        return new ShowroomLayout(all, stations, swapped) { Legs = legs };
    }

    /// <summary>A placement turned a quarter clockwise about the grid's corner, as a whole group turns: north becomes east.</summary>
    private static Placement QuarterTurned(Placement p) =>
        p with { At = new Tile(-p.At.Row, p.At.Column), Rotation = (p.Rotation + 1) % 4 };

    /// <summary>
    /// The first shift, scanning from the grid's south-west corner row by row, that puts a block's tiles on the grid,
    /// inside its edge, with a tile clear of everything taken; null if there's none.
    /// </summary>
    private static (int Dc, int Dr)? FreeSpot(List<Tile> tiles, HashSet<Tile> taken)
    {
        int minColumn = tiles.Min(t => t.Column);
        int maxColumn = tiles.Max(t => t.Column);
        int minRow = tiles.Min(t => t.Row);
        int maxRow = tiles.Max(t => t.Row);
        for (int bottom = TrackGrid.Size - 2; bottom - (maxRow - minRow) >= 1; bottom--)
        {
            for (int left = 1; left + (maxColumn - minColumn) <= TrackGrid.Size - 2; left++)
            {
                int dc = left - minColumn;
                int dr = bottom - maxRow;
                bool clear = tiles.All(t =>
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        for (int j = -1; j <= 1; j++)
                        {
                            if (taken.Contains(new Tile(t.Column + dc + i, t.Row + dr + j)))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                });
                if (clear)
                {
                    return (dc, dr);
                }
            }
        }

        return null;
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
        if (exit.Kind == ConnectorKind.Lip && (catalogue.Find("base:landing") ?? catalogue.All.FirstOrDefault(p => p.In == ConnectorKind.Landing)) is PieceDef landing)
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

    /// <summary>
    /// The crossing (PIECES-PLAN, X2): a road north at the plain's level, its run-up, the crossing tile and its
    /// run-out; and a road east over it, a run-up, up two levels on a steep slope, a straight over the crossing
    /// tile, and down again. Null without the steep slopes.
    /// </summary>
    private static (List<Placement> Under, List<Placement> Over)? Crossing(PieceCatalogue catalogue)
    {
        if (catalogue.Find("base:slope-up-steep") is null || catalogue.Find("base:slope-down-steep") is null)
        {
            return null;
        }

        const string Straight = "base:straight";
        int east = (int)Compass.East;
        var under = new List<Placement>();
        for (int i = 0; i <= RunUp + RunOut; i++)
        {
            under.Add(new Placement(Straight, new Tile(0, -i), 0, 0, false));
        }

        // The crossing tile comes after a run-up, as a piece does.
        var crossing = new Tile(0, -RunUp);
        var over = new List<Placement>();
        for (int i = RunUp + 3; i > 3; i--)
        {
            over.Add(new Placement(Straight, crossing.Step(Compass.West, i), 0, east, false));
        }

        over.Add(new Placement("base:slope-up-steep", crossing.Step(Compass.West, 3), 0, east, false));
        over.Add(new Placement(Straight, crossing, 2, east, false));
        over.Add(new Placement("base:slope-down-steep", crossing.Step(Compass.East), 2, east, false));
        for (int i = 4; i < 4 + RunOut; i++)
        {
            over.Add(new Placement(Straight, crossing.Step(Compass.East, i), 0, east, false));
        }

        return (under, over);
    }
}
