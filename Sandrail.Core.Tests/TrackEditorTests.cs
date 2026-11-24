using Sandrail.Core.Generation;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>M5a: the track editor's core (M5-PLAN, M5a's checks).</summary>
public class TrackEditorTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static readonly Lazy<LaidTrack> Shakedown = new(() => Track.Load(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "tracks", "shakedown.json")), Catalogue.Value));

    private static TrackEditor New() => new(TrackEditor.Empty(), Catalogue.Value);

    private static readonly Placement Start = new("base:start", new Tile(5, 0), 0, (int)Compass.South, false);

    [Fact]
    public void AnEmptyTrackSaysSo()
    {
        TrackEditor editor = New();
        Assert.Empty(editor.Track.Pieces);
        Assert.Contains(editor.Laid.Problems, p => p.Contains("pieces is empty", StringComparison.Ordinal));
        Assert.Empty(editor.OpenEnds());
    }

    [Fact]
    public void PiecesArePlacedAndTakenAway()
    {
        TrackEditor editor = New();
        Assert.Null(editor.Place(Start));
        Assert.Single(editor.Track.Pieces);
        Assert.Equal("base:start", editor.PieceAt(new Tile(5, 0))?.Def.Id);

        Assert.Null(editor.Erase(new Tile(5, 0)));
        Assert.Empty(editor.Track.Pieces);
        Assert.Null(editor.PieceAt(new Tile(5, 0)));
    }

    /// <summary>A change that can't be made is refused, and the refusal says why.</summary>
    [Fact]
    public void WhatCantBePlacedIsRefusedInWords()
    {
        TrackEditor editor = New();
        editor.Place(Start);

        Assert.Contains("in the way", editor.Place(Start with { Piece = "base:straight" }));
        Assert.Contains("off the grid", editor.Place(new Placement("base:straight", new Tile(32, 5), 0, (int)Compass.South, false)) ?? string.Empty);
        Assert.Contains("off the grid", editor.Place(new Placement("base:corner-50", new Tile(31, 10), 0, (int)Compass.North, false)) ?? string.Empty);
        Assert.Contains("no piece called", editor.Place(Start with { Piece = "base:helter-skelter", At = new Tile(9, 9) }));
        Assert.Contains("below the plain", editor.Place(Start with { At = new Tile(9, 9), Level = -1 }));
        Assert.Contains("nothing at", editor.Erase(new Tile(20, 20)));
        Assert.Single(editor.Track.Pieces);
    }

    [Fact]
    public void ChangesAreUndoneAndRedone()
    {
        TrackEditor editor = New();
        Assert.False(editor.CanUndo);
        editor.Place(Start);
        editor.Place(new Placement("base:straight", new Tile(5, 1), 0, (int)Compass.South, false));
        string two = editor.Track.Save();
        editor.Erase(new Tile(5, 0));
        Assert.Single(editor.Track.Pieces);

        Assert.True(editor.Undo());
        Assert.Equal(two, editor.Track.Save());
        Assert.True(editor.Undo());
        Assert.True(editor.Undo());
        Assert.Empty(editor.Track.Pieces);
        Assert.False(editor.Undo());

        Assert.True(editor.Redo());
        Assert.True(editor.Redo());
        Assert.Equal(two, editor.Track.Save());

        // A new change after undoing forgets what was undone.
        editor.Undo();
        editor.Place(new Placement("base:corner-30", new Tile(5, 1), 0, (int)Compass.South, false));
        Assert.False(editor.CanRedo);
    }

    /// <summary>
    /// <see cref="TrackEditor.Next"/> from every piece's way out, in every turn and mirror, with every piece that
    /// can follow it, both ways round: the two meet exactly, as <see cref="Track.Lay"/> judges it.
    /// </summary>
    [Fact]
    public void NextMeetsEveryWayOut()
    {
        // What would be wrong between them: anything said from the first piece's way out (or of the two
        // covering a tile, which is said from the first), the second's way in unmet, or the second in the
        // first's gap. The first's own way in and the second's own way out are loose ends, as they should be.
        static bool Wrong(string p) =>
            (p.StartsWith("pieces[0]", StringComparison.Ordinal) && !p.Contains("is a landing that no jump", StringComparison.Ordinal)
                && !p.Contains("circuit", StringComparison.Ordinal))
            || p.Contains("nothing leads into pieces[1]", StringComparison.Ordinal)
            || p.Contains("pieces[1] (base:", StringComparison.Ordinal) && p.Contains("is in the gap", StringComparison.Ordinal)
            || p.Contains("pieces[1]", StringComparison.Ordinal) && p.Contains("a landing that no jump", StringComparison.Ordinal);
        int pairs = 0;
        foreach (PieceDef a in Catalogue.Value.All.Where(d => d.Out.Kind is ConnectorKind.Road or ConnectorKind.Lip))
        {
            for (int turn = 0; turn < 4; turn++)
            {
                foreach (bool mirrorA in new[] { false, true })
                {
                    foreach (PieceDef b in Catalogue.Value.All)
                    {
                        foreach (bool mirrorB in new[] { false, true })
                        {
                            TrackEditor editor = New();
                            Assert.Null(editor.Place(new Placement(a.Id, new Tile(14, 14), 2, turn, mirrorA)));
                            WorldConnector end = Assert.Single(editor.OpenEnds());
                            Placement? next = editor.Next(end, b.Id, mirrorB, out string? why);
                            bool follows = end.Kind == ConnectorKind.Lip ? b.In == ConnectorKind.Landing : b.In == ConnectorKind.Road;
                            if (!follows)
                            {
                                Assert.Null(next);
                                Assert.False(string.IsNullOrEmpty(why));
                                continue;
                            }

                            string label = $"{b.Id}{(mirrorB ? " mirrored" : string.Empty)} after {a.Id}{(mirrorA ? " mirrored" : string.Empty)} turned {turn}";
                            Assert.True(next is not null, $"{label}: {why}");
                            Assert.Null(editor.Place(next));
                            IReadOnlyList<string> wrong = editor.Laid.Problems.Where(Wrong).ToList();
                            Assert.True(wrong.Count == 0, $"{label}: {string.Join("; ", wrong)}");
                            Assert.DoesNotContain(editor.OpenEnds(), e => e == end);
                            pairs++;
                        }
                    }
                }
            }
        }

        Assert.True(pairs > 500, $"{pairs} pairs tried");
    }

    /// <summary>A track built in the editor from nothing, placement by placement, is the shakedown.</summary>
    [Fact]
    public void TheShakedownCanBeBuiltPieceByPiece()
    {
        Track shakedown = Shakedown.Value.Track;
        TrackEditor editor = New();
        foreach (TrackPiece piece in shakedown.Pieces)
        {
            Assert.Null(editor.Place(piece.ToPlacement()));
        }

        Assert.Empty(editor.Laid.Problems);
        Assert.Equal(shakedown.Save(), (editor.Track with { Id = shakedown.Id, Name = shakedown.Name }).Save());
    }

    /// <summary>
    /// Laying track by <see cref="TrackEditor.Next"/> alone, the gate and each piece and its mirror named in
    /// the order they're driven, makes the shakedown's route, and a generated circuit's round to its line.
    /// </summary>
    [Fact]
    public void NextAloneLaysWholeTracks()
    {
        Generated circuit = TrackGenerator.Generate(8UL, new GenSettings(), Catalogue.Value);
        foreach (LaidTrack track in new[] { Shakedown.Value, circuit.Track.Lay(Catalogue.Value) })
        {
            IReadOnlyList<PlacedPiece> route = track.Route;
            var editor = new TrackEditor(TrackEditor.Empty() with { Ground = track.Track.Ground, Laps = track.Track.Laps }, Catalogue.Value);
            Assert.Null(editor.Place(route[0].Placement));
            for (int k = 1; k < route.Count; k++)
            {
                WorldConnector end = editor.OpenEnds()[^1];
                Placement? next = editor.Next(end, route[k].Def.Id, route[k].Placement.Mirrored, out string? why);
                Assert.True(next == route[k].Placement, $"{track.Track.Id} piece {k}: {next} for {route[k].Placement} ({why})");
                Assert.Null(editor.Place(next!));
            }

            Assert.Empty(editor.Laid.Problems);
            Assert.Equal(route.Select(p => p.Placement), editor.Laid.Route.Select(p => p.Placement));
        }
    }

    /// <summary>Laying a start-finish line makes a circuit of three laps; laps change and undo; taking the line away makes a sprint (M5d, decision 7).</summary>
    [Fact]
    public void ALineMakesACircuitAndTakingItAwayASprint()
    {
        TrackEditor editor = New();
        Assert.Contains("lay a start-finish line", editor.SetLaps(5));
        Assert.Null(editor.Place(new Placement("base:start-finish", new Tile(14, 14), 0, (int)Compass.North, false)));
        Assert.Equal(TrackEditor.DefaultLaps, editor.Track.Laps);
        Assert.Equal(3, editor.Track.Format);

        Assert.Null(editor.SetLaps(5));
        Assert.Equal(5, editor.Track.Laps);
        Assert.NotNull(editor.SetLaps(0));
        editor.Undo();
        Assert.Equal(3, editor.Track.Laps);

        Assert.Null(editor.Erase(new Tile(14, 14)));
        Assert.Equal(0, editor.Track.Laps);
        Assert.Equal(1, editor.Track.FormatNeeded);
    }

    /// <summary>A circuit laid by <see cref="TrackEditor.Next"/> from its line, round and back to it, closes: sound, three laps, nothing left open.</summary>
    [Fact]
    public void ACircuitLaidByNextCloses()
    {
        TrackEditor editor = New();
        editor.Place(new Placement("base:start-finish", new Tile(14, 20), 0, (int)Compass.North, false));
        foreach (string piece in Circuit)
        {
            WorldConnector end = editor.OpenEnds()[^1];
            Placement next = editor.Next(end, piece, false, out string? why) ?? throw new InvalidOperationException(why);
            Assert.True(editor.Place(next) is null, $"{piece} at {next.At}: {editor.Fits(next)}");
        }

        Assert.Empty(editor.Laid.Problems);
        Assert.Empty(editor.OpenEnds());
        Assert.True(editor.Track.Circuit);
        Assert.Equal(Circuit.Length + 1, editor.Laid.Route.Count);
    }

    /// <summary>The circuit the editor's checks lay: round to the right by four 50 m corners, a straight on each side.</summary>
    public static readonly string[] Circuit =
    [
        "base:straight", "base:corner-50", "base:straight", "base:straight", "base:corner-50",
        "base:straight", "base:straight", "base:corner-50", "base:straight", "base:straight", "base:corner-50",
    ];

    /// <summary>A generated track edited, saved and read back is the same track, and its ground beds the same (M5d).</summary>
    [Fact]
    public void AnEditedGeneratedTrackSavesAndLoadsOnItsGround()
    {
        Generated g = TrackGenerator.Generate(5UL, new GenSettings(), Catalogue.Value);
        var editor = new TrackEditor(g.Track, Catalogue.Value);
        Tile corner = editor.Laid.Route.First(p => p.Def.Path.Any(s => s.Turn is not null)).Placement.At;
        Assert.Null(editor.Erase(corner));
        editor.Rename("user:seed-5-edited", "Seed 5, edited");

        string file = editor.Track.Save();
        TrackEditor back = TrackEditor.Open(file, Catalogue.Value);
        Assert.Equal(file, back.Track.Save());
        Assert.Equal(editor.Laid.Problems, back.Laid.Problems);

        TrackGround ground = back.Track.Ground!;
        Terrain bare = new(ground.Seed, ground.Terrain);
        Assert.Equal(Bed.Lay(bare, editor.Laid.Placed).Heights, Bed.Lay(bare, back.Laid.Placed).Heights);
    }

    /// <summary>The editor's way in keeps a track's problems where loading it would refuse it (fact 2).</summary>
    [Fact]
    public void AnUnfinishedTrackOpensWithItsProblems()
    {
        TrackEditor editor = New();
        editor.Place(Start);
        editor.Place(new Placement("base:straight", new Tile(5, 1), 0, (int)Compass.South, false));
        string file = editor.Track.Save();

        Assert.Throws<InvalidDataException>(() => Track.Load(file, Catalogue.Value));
        TrackEditor again = TrackEditor.Open(file, Catalogue.Value);
        Assert.Equal(editor.Laid.Problems, again.Laid.Problems);
        Assert.Equal(file, again.Track.Save());
    }
}
