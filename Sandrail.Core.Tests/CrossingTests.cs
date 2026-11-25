using System.Numerics;
using Sandrail.Core.Generation;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>Crossings (PIECES-PLAN, X1): pieces stacked on a tile two levels clear, pillars, legs, and nothing else moved.</summary>
public class CrossingTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static TrackPiece P(string piece, int column, int row, Compass facing = Compass.North, int level = 0, bool mirrored = false) =>
        new() { Piece = $"base:{piece}", At = [column, row], Facing = facing, Level = level, Mirrored = mirrored };

    private static Track Of(params TrackPiece[] pieces) => new() { Id = "test:crossing", Name = "Crossing", Pieces = pieces };

    private static PlacedPiece Placed(TrackPiece p) => new(Catalogue.Value.Find(p.Piece)!, p.ToPlacement());

    /// <summary>The crossing tile: the road north up column 10 passes under the road west along row 20 here.</summary>
    private static readonly Tile Crossing = new(10, 20);

    /// <summary>
    /// A sprint that goes under itself: north up column 10 at the plain's level, a corner, up two levels, two
    /// corners, back west along row 20 two levels up, over its own road at (10, 20), down again and to the finish.
    /// </summary>
    private static TrackPiece[] UnderItself(int overLevel = 2)
    {
        var pieces = new List<TrackPiece>
        {
            P("start", 10, 22),
            P("straight", 10, 21),
            P("straight", 10, 20),
            P("straight", 10, 19),
            P("corner-30", 10, 18),
            P("slope-up", 12, 17, Compass.East),
            P("slope-up", 14, 17, Compass.East, 1),
            P("corner-30", 16, 17, Compass.East, 2),
            P("corner-30", 17, 19, Compass.South, 2),
        };
        for (int column = 15; column >= 9; column--)
        {
            pieces.Add(P("straight", column, 20, Compass.West, column == Crossing.Column ? overLevel : 2));
        }

        pieces.Add(P("slope-down", 8, 20, Compass.West, 2));
        pieces.Add(P("slope-down", 6, 20, Compass.West, 1));
        pieces.Add(P("finish", 4, 20, Compass.West));
        return [.. pieces];
    }

    [Fact]
    public void ARoadTwoLevelsOverAnotherLaysClean()
    {
        LaidTrack laid = Of(UnderItself()).Lay(Catalogue.Value);
        Assert.Empty(laid.Problems);
        Assert.Equal(laid.Placed.Count, laid.Route.Count);

        // The route passes the crossing tile twice: under it, then over it.
        var onCrossing = laid.Route.Select((p, i) => (p, i)).Where(x => x.p.Tiles.Contains(Crossing)).ToList();
        Assert.Equal(2, onCrossing.Count);
        Assert.Equal(0, onCrossing[0].p.Placement.Level);
        Assert.Equal(2, onCrossing[1].p.Placement.Level);

        // Only the one over the road goes up on pillars.
        Assert.Equal([onCrossing[1].p.Placement], laid.Placed.Where(p => p.Pillars).Select(p => p.Placement));
        Assert.Equal(Stacking.DeckM, onCrossing[1].p.DepthM);
    }

    [Fact]
    public void OneLevelOverIsRefusedSayingByHowMuch()
    {
        IReadOnlyList<string> problems = Of(UnderItself(overLevel: 1)).Lay(Catalogue.Value).Problems;
        Assert.Contains(problems, p => p.Contains("crosses over", StringComparison.Ordinal)
            && p.Contains("at (10, 20) with 2.0 m under it", StringComparison.Ordinal)
            && p.Contains("1 level more between them", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSameHeightIsRefusedInM2sWords()
    {
        IReadOnlyList<string> problems = Of(UnderItself(overLevel: 0)).Lay(Catalogue.Value).Problems;
        Assert.Contains(problems, p => p.Contains("both cover (10, 20)", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("crosses over", StringComparison.Ordinal));
    }

    /// <summary>A banked corner's raised outer edge counts (fact 4): two levels clear a flat corner and not a banked one.</summary>
    [Fact]
    public void ABankedEdgeUnderADeckCounts()
    {
        // The corner's middle tile on its outside, [0, 1], is where its bank stands highest.
        Tile tile = new(10, 17);
        PlacedPiece deck = Placed(P("straight", 10, 17, Compass.East, 2));
        Stack flat = Stacking.Of(deck, Placed(P("corner-30", 10, 18)), tile);
        Stack banked = Stacking.Of(deck, Placed(P("corner-30-banked", 10, 18)), tile);

        Assert.True(flat.Clear, $"flat: {flat.GapM:0.00} m");
        Assert.False(banked.Clear, $"banked: {banked.GapM:0.00} m");
        Assert.InRange(banked.GapM, 0.5f, 2.0f);
        Assert.Same(deck, banked.Upper);
    }

    [Fact]
    public void ALoopNeverStacks()
    {
        PlacedPiece loop = Placed(P("loop", 10, 20));
        PlacedPiece high = Placed(P("straight", 10, 20, Compass.East, 12));
        Stack stack = Stacking.Of(loop, high, new Tile(10, 20));
        Assert.True(stack.Overlap);
        Assert.False(stack.Clear);
    }

    /// <summary>Legs stand clear of the road below, left out where they'd stand on it (fact 5).</summary>
    [Fact]
    public void ALegOverACornersRoadIsLeftOut()
    {
        TrackPiece corner = P("corner-30", 10, 18);
        TrackPiece over = P("straight", 10, 17, Compass.East, 2);
        PlacedPiece deck = Placed(over);
        PlacedPiece below = Placed(corner);
        IReadOnlyList<Leg> legs = Stacking.Legs(deck, [deck, below]);

        // A straight has a leg under each edge at each end: four, less the one beside the corner's road.
        Assert.Equal(3, legs.Count);
        float reach = (below.Def.WidthM / 2.0f) + Stacking.LegClearM;
        foreach (Leg leg in legs)
        {
            float nearest = below.Road().Pieces.SelectMany(c => Enumerable.Range(0, 101).Select(k => c.FrameAt(c.Length * k / 100.0f).Position))
                .Min(p => new Vector2(p.X - leg.Top.X, p.Z - leg.Top.Z).Length());
            Assert.True(nearest > reach, $"a leg stands {nearest:0.0} m from the corner's road");
        }
    }

    [Fact]
    public void TheCrossingsLegsStandOnBothSidesOfTheRoadBelow()
    {
        LaidTrack laid = Of(UnderItself()).Lay(Catalogue.Value);
        IReadOnlyList<Leg> legs = laid.Legs;
        Assert.Equal(4, legs.Count);

        // Under the deck, either side of the road north up column 10, which runs down x = 10's middle.
        float middle = TrackGrid.Middle(Crossing, 0).X;
        Assert.All(legs, l => Assert.True(MathF.Abs(l.Top.X - middle) >= 9.9f, $"a leg at x {l.Top.X - middle:0.0} m from the road below's middle"));
        Assert.All(legs, l => Assert.Equal(TrackGrid.Middle(Crossing, 2).Y - Stacking.DeckM, l.Top.Y, 3));
    }

    /// <summary>Two pieces entered from the same side of one tile, one above the other, each meet the road at their own level.</summary>
    [Fact]
    public void WaysInAtTwoLevelsOnOneTileMeetTheirOwnRoads()
    {
        IReadOnlyList<string> problems = Of(
            P("straight", 10, 15), P("straight", 10, 14),
            P("straight", 10, 15, level: 2), P("straight", 10, 14, level: 2)).Lay(Catalogue.Value).Problems;
        Assert.DoesNotContain(problems, p => p.Contains("isn't entered from there", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("leaves at level", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("both cover", StringComparison.Ordinal));
    }

    /// <summary>A deck lays no bed (fact 7): under the crossing, the ground is the lower road's bed, not buried at the deck's.</summary>
    [Fact]
    public void TheBedUnderACrossingIsTheLowerRoads()
    {
        LaidTrack laid = Of(UnderItself()).Lay(Catalogue.Value);
        Terrain bedded = Bed.Lay(new Terrain(1UL, new TerrainSettings()), laid);
        Vector3 middle = TrackGrid.Middle(Crossing, 0);
        Assert.Equal(middle.Y - Bed.UnderRoadM, bedded.HeightAt(middle.X, middle.Z), 2);
    }

    /// <summary>The editor's rule is the track's: the crossing fits, one level short is refused saying which way, and Delete picks by level.</summary>
    [Fact]
    public void TheEditorPlacesAndErasesByLevel()
    {
        TrackPiece[] all = UnderItself();
        TrackPiece over = all.Single(p => p.At[0] == Crossing.Column && p.At[1] == Crossing.Row && p.Level == 2);
        var editor = new TrackEditor(Of([.. all.Where(p => p != over)]), Catalogue.Value);

        string? low = editor.Fits(over.ToPlacement() with { Level = 1 });
        Assert.NotNull(low);
        Assert.Contains("would cross over straight at (10, 20)", low, StringComparison.Ordinal);
        Assert.Contains("1 level higher would do it", low, StringComparison.Ordinal);

        Assert.Null(editor.Place(over.ToPlacement()));
        Assert.Empty(editor.Laid.Problems);
        Assert.Equal(2, editor.PiecesAt(Crossing).Count);
        Assert.Equal(2, editor.PieceAt(Crossing)!.Placement.Level);
        Assert.Equal(0, editor.PieceAt(Crossing, 0)!.Placement.Level);

        Assert.Null(editor.Erase(Crossing, 0));
        Assert.Equal(2, editor.PiecesAt(Crossing).Single().Placement.Level);
        Assert.DoesNotContain(editor.Laid.Placed, p => p.Pillars);
    }

    /// <summary>An end whose next tile has only road two levels away under it is open: the road can go on over it.</summary>
    [Fact]
    public void AnEndOverARoadIsOpen()
    {
        TrackPiece[] all = UnderItself();
        TrackPiece[] upToCrossing = [.. all.TakeWhile(p => !(p.At[0] == Crossing.Column && p.At[1] == Crossing.Row && p.Level == 2))];
        var editor = new TrackEditor(Of(upToCrossing), Catalogue.Value);
        WorldConnector end = editor.OpenEnds()[^1];
        Assert.Equal(Crossing, end.Beyond);
        Placement? next = editor.Next(end, "base:straight", false, out _);
        Assert.NotNull(next);
        Assert.Null(editor.Fits(next));

        // At one level, where the road can't go on over, it isn't.
        var low = new TrackEditor(Of([.. upToCrossing.Select(p => p.Level == 2 && p.At[1] == Crossing.Row ? p with { Level = 1 } : p)]), Catalogue.Value);
        Assert.DoesNotContain(low.OpenEnds(), e => e.Beyond == Crossing);
    }

    /// <summary>
    /// The crossover, the game's own crossing track, laid in the editor onto the road's end alone, as M5's chain mode lays
    /// track: over its own road included, it comes out the same track, sound, and back round to its line.
    /// </summary>
    [Fact]
    public void NextAloneLaysTheCrossover()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "base", "tracks", "crossover.json");
        LaidTrack track = Track.Load(File.ReadAllText(path), Catalogue.Value);
        Assert.Single(track.Placed, p => p.Pillars);
        Assert.NotEmpty(track.Legs);

        IReadOnlyList<PlacedPiece> route = track.Route;
        var editor = new TrackEditor(TrackEditor.Empty() with { Laps = track.Track.Laps }, Catalogue.Value);
        Assert.Null(editor.Place(route[0].Placement));
        for (int k = 1; k < route.Count; k++)
        {
            WorldConnector end = editor.OpenEnds()[^1];
            Placement? next = editor.Next(end, route[k].Def.Id, route[k].Placement.Mirrored, out string? why);
            Assert.True(next == route[k].Placement, $"piece {k}: {next} for {route[k].Placement} ({why})");
            Assert.Null(editor.Place(next!));
        }

        Assert.Empty(editor.Laid.Problems);
        Assert.Empty(editor.OpenEnds());
        Assert.Equal(route.Select(p => p.Placement), editor.Laid.Route.Select(p => p.Placement));
    }

    /// <summary>
    /// What's pinned (PIECES-PLAN, "Pinned"): the game's own tracks keep their fingerprints, so his ghosts and sheets
    /// stay theirs. Seed 5's is pinned in <see cref="TrackCodeTests"/>; the bench's <c>--fingerprints</c> checks his
    /// saved ones.
    /// </summary>
    [Fact]
    public void TheShakedownKeepsItsFingerprint()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "base", "tracks", "shakedown.json");
        LaidTrack laid = Track.Load(File.ReadAllText(path), Catalogue.Value);
        Assert.Equal("8a6323aa", TrackFingerprint.Of(laid.Track, Catalogue.Value));
        Assert.DoesNotContain(laid.Placed, p => p.Pillars);
        Assert.Empty(laid.Legs);
    }
}
