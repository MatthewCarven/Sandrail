using System.Numerics;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>M2c: the track file, its validation and its route (M2-PLAN, M2c's checks).</summary>
public class TrackTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static string ShakedownPath => Path.Combine(AppContext.BaseDirectory, "content", "base", "tracks", "shakedown.json");

    private static LaidTrack Shakedown() => Track.Load(File.ReadAllText(ShakedownPath), Catalogue.Value);

    private static TrackPiece P(string piece, int column, int row, Compass facing = Compass.North, int level = 0, bool mirrored = false) =>
        new() { Piece = $"base:{piece}", At = [column, row], Facing = facing, Level = level, Mirrored = mirrored };

    private static Track Of(params TrackPiece[] pieces) => new() { Id = "test:track", Name = "Test", Pieces = pieces };

    /// <summary>The smallest sound track: a start, a straight and a finish, north up the middle.</summary>
    private static TrackPiece[] Short() => [P("start", 10, 12), P("straight", 10, 11), P("finish", 10, 10)];

    private static IReadOnlyList<string> Problems(params TrackPiece[] pieces) => Of(pieces).Lay(Catalogue.Value).Problems;

    private static void Refused(string expected, params TrackPiece[] pieces)
    {
        IReadOnlyList<string> problems = Problems(pieces);
        Assert.True(problems.Any(p => p.Contains(expected, StringComparison.Ordinal)),
            $"Expected a problem containing \"{expected}\"; got: {(problems.Count == 0 ? "none" : string.Join(" | ", problems))}");
    }

    [Fact]
    public void TheShortTrackIsSound()
    {
        LaidTrack laid = Of(Short()).Lay(Catalogue.Value);
        Assert.Empty(laid.Problems);
        Assert.Equal(["base:start", "base:straight", "base:finish"], laid.Route.Select(p => p.Def.Id));
    }

    [Fact]
    public void TheShakedownIsSound()
    {
        LaidTrack laid = Shakedown();
        Assert.Empty(laid.Problems);
        Assert.Equal("base:shakedown", laid.Track.Id);
    }

    /// <summary>The route runs from the start to the finish through every piece once (M2-PLAN, M2c).</summary>
    [Fact]
    public void TheShakedownsRouteRunsStartToFinishThroughEveryPieceOnce()
    {
        LaidTrack laid = Shakedown();
        Assert.Equal(GateKind.Start, laid.Route[0].Def.Gate);
        Assert.Equal(GateKind.Finish, laid.Route[^1].Def.Gate);
        Assert.Equal(laid.Placed.Count, laid.Route.Count);
        Assert.Equal(laid.Placed.Count, laid.Route.Distinct().Count());
        Assert.Equal(laid.Track.Pieces.Length, laid.Route.Count);
    }

    /// <summary>
    /// Each piece's road ends where the next one's starts, to M1c's millimetre, so the route's centrelines
    /// are one road; across a jump the landing starts the gap's width further on, level with the lip's landing.
    /// </summary>
    [Fact]
    public void TheShakedownsRoadsMeetEndToEnd()
    {
        IReadOnlyList<PlacedPiece> route = Shakedown().Route;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            Frame end = route[i].Road().End;
            Frame start = route[i + 1].Road().Pieces[0].FrameAt(0.0f);
            WorldConnector o = route[i].Out;
            if (o.Kind == ConnectorKind.Lip)
            {
                Vector3 across = start.Position - end.Position;
                Assert.Equal(o.Gap * TrackGrid.TileM, new Vector2(across.X, across.Z).Length(), 3);
                Assert.Equal(o.Level * TrackGrid.LevelM, start.Position.Y - TrackGrid.Origin.Y, 3);
            }
            else
            {
                Assert.True(Vector3.Distance(end.Position, start.Position) < PieceDef.FitM,
                    $"{route[i].Def.Id} at {route[i].Placement.At} ends {Vector3.Distance(end.Position, start.Position):0.####} m from {route[i + 1].Def.Id}'s start");
                Assert.True(Vector3.Dot(end.Forward, start.Forward) > 0.99999f);
            }
        }
    }

    /// <summary>M2b's starter set, which the shakedown was written to use every piece of; the S-bend came with M3b.</summary>
    private static readonly string[] StarterSet =
    [
        "straight", "corner-30", "corner-50", "corner-30-banked", "corner-50-banked", "slope-up", "slope-down",
        "launch-short", "launch-long", "landing", "loop", "chicane", "start", "checkpoint", "finish",
    ];

    /// <summary>What M2c asks of the shakedown: every piece of the starter set, both corner sizes both ways, a gap jump, the loop, a climb and back, two checkpoints.</summary>
    [Fact]
    public void TheShakedownHasWhatM2cAsksFor()
    {
        LaidTrack laid = Shakedown();
        var used = laid.Placed.Select(p => p.Def.Id).ToHashSet();
        Assert.All(StarterSet, piece => Assert.Contains($"base:{piece}", used));

        foreach (string size in new[] { "30", "50" })
        {
            var corners = laid.Placed.Where(p => p.Def.Id.StartsWith($"base:corner-{size}", StringComparison.Ordinal)).ToList();
            Assert.Contains(corners, p => p.Placement.Mirrored);
            Assert.Contains(corners, p => !p.Placement.Mirrored);
        }

        Assert.Equal(2, laid.Placed.Count(p => p.Def.Gate == GateKind.Checkpoint));
        Assert.Contains(laid.Placed, p => p.Def.Id == "base:slope-up");
        Assert.Contains(laid.Placed, p => p.Placement.Level > 0 && p.Def.In == ConnectorKind.Road && p.Def.Path.Any(s => s.Turn is not null));
        Assert.Contains(laid.Placed, p => p.Def.Id == "base:loop");
        Assert.Contains(laid.Placed, p => p.Def.Out.Kind == ConnectorKind.Lip);
    }

    /// <summary>The game's own track is written by hand in the layout <see cref="Track.Save"/> writes, so a load and a save leave its text as it is.</summary>
    [Fact]
    public void TheShakedownFileIsWhatSaveWrites()
    {
        string text = File.ReadAllText(ShakedownPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(text, Track.Parse(text).Save());
    }

    [Fact]
    public void ARoundTripChangesNothing()
    {
        var track = new Track
        {
            Id = "test:round-trip",
            Name = "A \"quoted\" name, with a comma: and Ünïcödé",
            Pieces =
            [
                P("start", 3, 4),
                P("corner-30", 3, 3, Compass.East, level: 2, mirrored: true),
                P("loop", 7, 9, Compass.South),
                P("finish", 20, 1, Compass.West, level: 1),
            ],
        };

        Track back = Track.Parse(track.Save());
        Assert.Equal(track.Save(), back.Save());
        Assert.Equal(track.Id, back.Id);
        Assert.Equal(track.Name, back.Name);
        Assert.Equal(track.Pieces.Select(p => p.ToPlacement()), back.Pieces.Select(p => p.ToPlacement()));
    }

    [Fact]
    public void PlacementsSurviveThePieceRecord()
    {
        var placement = new Placement("base:corner-50", new Tile(5, 6), 3, 3, true);
        Assert.Equal(placement, TrackPiece.Of(placement).ToPlacement());
        Assert.Equal(Compass.West, TrackPiece.Of(placement).Facing);
    }

    [Fact]
    public void AnEmptyTrackSavesAndReadsBack()
    {
        var track = new Track { Id = "test:empty", Name = "Empty" };
        Assert.Empty(Track.Parse(track.Save()).Pieces);
        Assert.Contains("pieces is empty", track.Lay(Catalogue.Value).Problems);
    }

    [Fact]
    public void AnUnknownFieldIsRefusedByName()
    {
        string text = Of(Short()).Save().Replace("\"facing\": \"north\" }", "\"facing\": \"north\", \"rotaton\": 1 }", StringComparison.Ordinal);
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Track.Parse(text));
        Assert.Contains("rotaton", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownFacingIsRefused()
    {
        string text = Of(Short()).Save().Replace("\"north\"", "\"up\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => Track.Parse(text));
    }

    [Fact]
    public void APlacementWithoutAtIsRefused()
    {
        string text = Of(Short()).Save().Replace("\"at\": [10, 11], ", string.Empty, StringComparison.Ordinal);
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Track.Parse(text));
        Assert.Contains("at", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWithoutAFormatIsRefused()
    {
        string text = Of(Short()).Save().Replace("  \"format\": 1,\n", string.Empty, StringComparison.Ordinal);
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Track.Parse(text));
        Assert.Contains("no format number", e.Message, StringComparison.Ordinal);
    }

    /// <summary>A newer file is refused for its format, not for the fields this build has never heard of.</summary>
    [Fact]
    public void ANewerFormatIsRefusedAsNewer()
    {
        string text = Of(Short()).Save()
            .Replace("\"format\": 1", $"\"format\": {Track.CurrentFormat + 1}", StringComparison.Ordinal)
            .Replace("\"size\": 32,", "\"size\": 32, \"weather\": \"rain\",", StringComparison.Ordinal);
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Track.Parse(text));
        Assert.Contains("newer Sandrail", e.Message, StringComparison.Ordinal);
    }

    // M3b: a track that carries its own ground (M3-PLAN fact 8).

    private static Track OnGround(params TrackPiece[] pieces) =>
        Of(pieces) with { Ground = new TrackGround { Seed = 18_446_744_073_709_551_615UL, Terrain = new() { HillsM = 40.0f } } };

    [Fact]
    public void ATrackOnThePlainIsStillFormatOne()
    {
        Assert.Contains("\"format\": 1,", Of(Short()).Save(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATrackWithGroundIsFormatTwoAndComesBackWhole()
    {
        Track track = OnGround(Short());
        string text = track.Save();
        Assert.Contains("\"format\": 2,", text, StringComparison.Ordinal);
        Track back = Track.Parse(text);
        Assert.Equal(text, back.Save());
        Assert.Equal(track.Ground, back.Ground);
        Assert.Equal(18_446_744_073_709_551_615UL, back.Ground!.Seed);
    }

    [Fact]
    public void OnGroundAPieceMayGoBelowThePlain()
    {
        TrackPiece[] low = [P("start", 10, 12, level: -3), P("straight", 10, 11, level: -3), P("finish", 10, 10, level: -3)];
        Assert.Empty(OnGround(low).Lay(Catalogue.Value).Problems);
        Assert.Contains(Of(low).Lay(Catalogue.Value).Problems, p => p.Contains("must be from 0 (the plain)", StringComparison.Ordinal));
    }

    [Fact]
    public void AFormatOneFileWithGroundIsRefused()
    {
        string text = OnGround(Short()).Save().Replace("\"format\": 2", "\"format\": 1", StringComparison.Ordinal);
        Assert.Contains("a format 1 track has no ground; ground came with format 2", Track.Parse(text).Lay(Catalogue.Value).Problems);
    }

    [Fact]
    public void UnsoundGroundIsRefused()
    {
        Track track = Of(Short()) with { Ground = new TrackGround { Seed = 1, Terrain = new() { Octaves = 0 } } };
        Assert.Contains(track.Lay(Catalogue.Value).Problems, p => p.StartsWith("ground.terrain: octaves", StringComparison.Ordinal));
    }

    [Fact]
    public void NotJsonIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => Track.Parse("{ \"format\": 1, "));
        Assert.Throws<InvalidDataException>(() => Track.Parse("[1, 2]"));
    }

    [Fact]
    public void LoadSaysEveryProblem()
    {
        string text = Of(P("start", 10, 12), P("strait", 10, 11), P("finish", 10, 10)).Save();
        InvalidDataException e = Assert.Throws<InvalidDataException>(() => Track.Load(text, Catalogue.Value));
        Assert.Contains("test:track refused", e.Message, StringComparison.Ordinal);
        Assert.Contains("\"base:strait\"", e.Message, StringComparison.Ordinal);
    }

    // Each rule broken on a small track written to break it (M2-PLAN, "The model": the refusals).

    [Fact]
    public void AnUnknownPieceIsRefusedByName() =>
        Refused("there's no piece called \"base:strait\"", P("start", 10, 12), P("strait", 10, 11), P("finish", 10, 10));

    [Fact]
    public void APieceOffTheGridIsRefused() =>
        Refused("covers (32, 6), off the 32 × 32 grid", P("start", 31, 7), P("corner-30", 31, 6), P("finish", 33, 5, Compass.East));

    [Fact]
    public void TwoPiecesOnOneTileAreRefused() =>
        Refused("pieces[1] (base:straight at (10, 11)) and pieces[3] (base:corner-30 at (9, 11)) both cover (10, 11)", [.. Short(), P("corner-30", 9, 11)]);

    [Fact]
    public void RoadLeadingOntoEmptyGroundIsRefused() =>
        Refused("leads onto empty ground at (10, 10)", P("start", 10, 12), P("straight", 10, 11), P("finish", 10, 9));

    [Fact]
    public void RoadLeadingOffTheGridIsRefused() =>
        Refused("leads off the grid at (10, -1)", P("start", 10, 1), P("straight", 10, 0), P("finish", 11, 0, Compass.East));

    [Fact]
    public void RoadRunningIntoThesideOfAPieceIsRefused() =>
        Refused("which isn't entered from there", P("start", 10, 12), P("straight", 10, 11), P("finish", 10, 10, Compass.East));

    [Fact]
    public void RoadMeetingAnotherLevelIsRefused() =>
        Refused("leaves at level 0, and pieces[2] (base:finish at (10, 10)) is entered at level 1",
            P("start", 10, 12), P("straight", 10, 11), P("finish", 10, 10, level: 1));

    [Fact]
    public void NothingLeadingIntoAPieceIsRefused() =>
        Refused("nothing leads into pieces[3] (base:straight at (20, 20)): the tile behind it, (20, 21), is empty", [.. Short(), P("straight", 20, 20)]);

    [Fact]
    public void APieceBehindThatLeadsElsewhereIsSaidSo() =>
        Refused("nothing leads into pieces[3] (base:straight at (20, 20)): pieces[4] (base:straight at (20, 21)), behind it, doesn't lead there",
            [.. Short(), P("straight", 20, 20), P("straight", 20, 21, Compass.East)]);

    [Fact]
    public void ALipWithNoLandingIsRefused() =>
        Refused("jumps 1 tile toward (10, 9), and there's no landing there",
            P("start", 10, 12), P("launch-short", 10, 11), P("finish", 10, 6));

    [Fact]
    public void ALandingThatIsntInLineIsRefused() =>
        Refused("isn't a landing facing it",
            P("start", 10, 12), P("launch-short", 10, 11), P("landing", 10, 9, Compass.East, level: 1), P("finish", 10, 5));

    [Fact]
    public void ALandingAtTheWrongLevelIsRefused() =>
        Refused("lands at level 1, and pieces[2] (base:landing at (10, 9)) is at level 0",
            P("start", 10, 12), P("launch-short", 10, 11), P("landing", 10, 9), P("finish", 10, 6));

    [Fact]
    public void APieceInAJumpsGapIsRefused() =>
        Refused("is in the gap",
            P("start", 10, 12), P("launch-short", 10, 11), P("straight", 10, 10, Compass.East),
            P("landing", 10, 9, level: 1), P("finish", 10, 6));

    [Fact]
    public void ALandingNoJumpReachesIsRefused() =>
        Refused("is a landing that no jump comes down on",
            P("start", 10, 12), P("straight", 10, 11), P("finish", 10, 10), P("landing", 20, 20, level: 1));

    [Fact]
    public void AJumpOverItsGapToItsLandingIsSound()
    {
        LaidTrack laid = Of(P("start", 10, 12), P("launch-short", 10, 11), P("landing", 10, 9, level: 1), P("finish", 10, 6)).Lay(Catalogue.Value);
        Assert.Empty(laid.Problems);
        Assert.Equal(4, laid.Route.Count);
    }

    [Fact]
    public void NoStartIsRefused() => Refused("there's no start gate", P("straight", 10, 11), P("finish", 10, 10));

    [Fact]
    public void NoFinishIsRefused() => Refused("there's no finish gate", P("start", 10, 12), P("straight", 10, 11));

    [Fact]
    public void TwoStartsAreRefused() =>
        Refused("there are 2 start gates", [.. Short(), P("start", 20, 12), P("finish", 20, 11)]);

    [Fact]
    public void AFinishTheStartDoesntReachIsRefused() =>
        Refused("there's no route from the start to the finish",
            P("start", 10, 12), P("straight", 10, 11), P("finish", 20, 10));

    /// <summary>
    /// A ring of four corners with a checkpoint and a straight in it, beside a sound start and finish:
    /// every connector meets, but the checkpoint is off the route.
    /// </summary>
    [Fact]
    public void ACheckpointOffTheRouteIsRefused()
    {
        TrackPiece[] ring =
        [
            P("corner-30", 0, 3), P("checkpoint", 2, 2, Compass.East), P("corner-30", 3, 2, Compass.East),
            P("corner-30", 4, 4, Compass.South), P("straight", 2, 5, Compass.West), P("corner-30", 1, 5, Compass.West),
        ];
        IReadOnlyList<string> problems = Problems([.. Short(), .. ring]);
        Assert.Contains(problems, p => p.Contains("(base:checkpoint at (2, 2)) is a checkpoint off the route", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("5 pieces aren't on the route", StringComparison.Ordinal));
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void ALevelBelowThePlainIsRefused() =>
        Refused("level is -1, and must be from 0", P("start", 10, 12, level: -1), P("straight", 10, 11, level: -1), P("finish", 10, 10, level: -1));

    [Fact]
    public void AnotherGridSizeIsRefused()
    {
        Track track = Of(Short()) with { Size = 16 };
        Assert.Contains(track.Lay(Catalogue.Value).Problems, p => p.Contains("size is 16", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIdWithoutANamespaceIsRefused()
    {
        Track track = Of(Short()) with { Id = "shakedown" };
        Assert.Contains(track.Lay(Catalogue.Value).Problems, p => p.Contains("must be namespaced", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnsoundTrackHasNoRoute()
    {
        LaidTrack laid = Of(P("start", 10, 12), P("straight", 10, 11), P("finish", 10, 9)).Lay(Catalogue.Value);
        Assert.NotEmpty(laid.Problems);
        Assert.Empty(laid.Route);
    }
}
