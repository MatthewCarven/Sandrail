using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Generation;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>M3b: the route walker, its levels and banks, and the crest caps the autodriver reads (M3-PLAN, M3b's checks).</summary>
public class TrackGeneratorTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static readonly Lazy<Generated[]> Sweep = new(() =>
        Enumerable.Range(1, 30).Select(s => TrackGenerator.Generate((ulong)s, new GenSettings(), Catalogue.Value)).ToArray());

    /// <summary>A generated track <see cref="Track.Lay"/> refuses is a generator bug (the bench sweeps a thousand; this, thirty).</summary>
    [Fact]
    public void EveryTrackInTheSweepLaysClean()
    {
        Assert.All(Sweep.Value, g => Assert.Empty(g.Track.Lay(Catalogue.Value).Problems));
    }

    [Fact]
    public void TheSameSeedMakesTheSameTrack()
    {
        Generated a = TrackGenerator.Generate(7UL, new GenSettings(), Catalogue.Value);
        Generated b = TrackGenerator.Generate(7UL, new GenSettings(), Catalogue.Value);
        Assert.Equal(a.Track.Save(), b.Track.Save());
    }

    /// <summary>
    /// Seeds 1 to 8 as M3b and M3d made them, pinned before M3c (2026-11-24): with stunts and checkpoints
    /// off, every later stage must leave them be, piece for piece. Seed 5 is the one he's learning.
    /// </summary>
    [Fact]
    public void TodaysRoutesStayPut()
    {
        string pinned = "1:e8e484038f421dc9 2:0eb05112beb14551 3:ab98f857e9efacf5 4:005be927f200bf5c 5:7145f3135182f092 6:03151ca73ad0feed 7:8ee39dc43cbd38fc 8:dca607145e5308fa";
        string now = string.Join(" ", Enumerable.Range(1, 8).Select(s =>
            $"{s}:{Fnv(AsBefore(TrackGenerator.Generate((ulong)s, Bare, Catalogue.Value).Track, s)):x16}"));
        Assert.True(pinned == now, now);
    }

    /// <summary>
    /// Seeds 1, 3, 5 and 7 as M3c made them, stunts and checkpoints and all, pinned before M3g (2026-11-24):
    /// they stay sprints when circuits come (M3g fact 7), and seed 5 is the one he called "the most fun i've
    /// had in a driving game for a long time".
    /// </summary>
    [Fact]
    public void TheSprintsHeDroveStayPut()
    {
        string pinned = "1:de2b073f250145a9 3:290e5a5e5f98d178 5:7e770c4ce25cb77f 7:40feabfbc80e87f2";
        string now = string.Join(" ", new[] { 1, 3, 5, 7 }.Select(s =>
            $"{s}:{Fnv(AsBefore(TrackGenerator.Generate((ulong)s, new GenSettings(), Catalogue.Value).Track, s)):x16}"));
        Assert.True(pinned == now, now);
    }

    /// <summary>
    /// A track's file with the ID and name it had when these were pinned (<c>gen:5</c>, "Seed 5"): codes (M4a)
    /// changed what a generated track is called, and nothing else about it.
    /// </summary>
    private static string AsBefore(Track track, int seed) => (track with { Id = $"gen:{seed}", Name = $"Seed {seed}" }).Save();

    private static ulong Fnv(string text)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in text)
            {
                h = (h ^ c) * 1099511628211UL;
            }

            return h;
        }

    /// <summary>The generator with nothing after the route's own stages: no stunts, no checkpoints.</summary>
    private static readonly GenSettings Bare = new()
    {
        Route = new RouteSettings { CircuitShare = 0.0f },
        Stunts = new StuntSettings { Density = 0.0f, CheckpointEveryM = 0.0f },
    };

    [Fact]
    public void AnotherSeedMakesAnotherTrack()
    {
        Assert.Equal(Sweep.Value.Length, Sweep.Value.Select(g => g.Track.Save()).Distinct().Count());
    }

    /// <summary>Decision 4: a sprint is 1 to 2 km. The last piece may carry it a little over the top; a jump's gap isn't road.</summary>
    [Fact]
    public void RoutesAreOneToTwoKilometres()
    {
        Generated[] sprints = Sweep.Value.Where(g => !g.Stats.Circuit).ToArray();
        Assert.NotEmpty(sprints);
        Assert.All(sprints, g => Assert.InRange(g.Stats.LengthM, 1000.0f, 2200.0f));
        Assert.All(sprints, g => Assert.InRange(g.Track.Lay(Catalogue.Value).LengthM, 900.0f, 2200.0f));
    }

    /// <summary>A sprint: a start on one edge heading in, a finish on the opposite edge heading out.</summary>
    [Fact]
    public void RoutesCrossTheGridFromEdgeToEdge()
    {
        foreach (Generated g in Sweep.Value.Where(g => !g.Stats.Circuit))
        {
            TrackPiece start = g.Track.Pieces[0];
            TrackPiece finish = g.Track.Pieces[^1];
            Assert.Equal("base:start", start.Piece);
            Assert.Equal("base:finish", finish.Piece);
            Assert.Equal(start.Facing, finish.Facing);
            Assert.True(OnEdge(start.At, TrackGrid.Opposite(start.Facing)), $"{g.Track.Id}'s start isn't on its edge");
            Assert.True(OnEdge(finish.At, finish.Facing), $"{g.Track.Id}'s finish isn't on its edge");
        }

        static bool OnEdge(int[] at, Compass edge) => edge switch
        {
            Compass.North => at[1] == 0,
            Compass.South => at[1] == TrackGrid.Size - 1,
            Compass.East => at[0] == TrackGrid.Size - 1,
            _ => at[0] == 0,
        };
    }

    /// <summary>It carries its ground, so the track and the terrain are one thing (M3-PLAN fact 8).</summary>
    [Fact]
    public void AGeneratedTrackCarriesItsGround()
    {
        Generated g = Sweep.Value[0];
        Assert.Equal(1UL, g.Track.Ground!.Seed);
        Assert.Equal(2, g.Track.FormatNeeded);
        Assert.Equal(g.Track.Save(), Track.Parse(g.Track.Save()).Save());
    }

    /// <summary>A generated track comes with its bed laid: the ground it carries is the bedded one (M3d).</summary>
    [Fact]
    public void AGeneratedTrackComesBedded()
    {
        Assert.All(Sweep.Value, g => Assert.True(g.Terrain.Bedded));
    }

    [Fact]
    public void TheWalkUsesTheWholeSet()
    {
        var used = Sweep.Value.SelectMany(g => g.Track.Pieces).Select(p => p.Piece + (p.Mirrored ? " left" : string.Empty)).ToHashSet();
        foreach (string piece in new[]
        {
            "base:straight", "base:corner-30", "base:corner-30 left", "base:corner-50", "base:corner-50 left",
            "base:corner-30-banked", "base:corner-50-banked", "base:s-bend", "base:s-bend left", "base:chicane",
            "base:slope-up", "base:slope-down",
        })
        {
            Assert.Contains(piece, used);
        }
    }

    [Fact]
    public void UnsoundRouteSettingsAreRefused()
    {
        Assert.NotEmpty(new RouteSettings { MinLengthM = 1500.0f, MaxLengthM = 1400.0f }.Problems());
        Assert.NotEmpty(new RouteSettings { Turniness = 0.0f }.Problems());
        Assert.Throws<ArgumentException>(() => TrackGenerator.Generate(1UL, new GenSettings { Route = new RouteSettings { Turniness = -1.0f } }, Catalogue.Value));
    }

    /// <summary>
    /// The route line holds the car under a crest's take-off speed on ordinary road (a slope), and leaves
    /// the stunts to their hints (worklog, M3b: the autodriver flew off humps it didn't know were there).
    /// </summary>
    [Fact]
    public void TheRouteLineCapsCrestsButNotStunts()
    {
        LaidTrack shakedown = Track.Load(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "tracks", "shakedown.json")), Catalogue.Value);
        CarSpec spec = CarSpec.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "base", "cars", "buggy.json")));
        RacingLine line = shakedown.RouteLine(spec);
        (_, IReadOnlyList<int> owner) = shakedown.RouteRoad();

        var slopeCaps = new List<float>();
        for (int i = 0; i < line.Count; i++)
        {
            PieceDef def = shakedown.Route[owner[line.PieceAt(i)]].Def;
            if (def.SpeedKmh is not null || def.In == ConnectorKind.Landing)
            {
                Assert.True(float.IsPositiveInfinity(line.CapAt(i)));
            }
            else if (def.Id is "base:slope-up" or "base:slope-down" && !float.IsPositiveInfinity(line.CapAt(i)))
            {
                slopeCaps.Add(line.CapAt(i));
            }
        }

        // The slope's crest arcs are 160.625 m: 0.8 of √(g R) is about 31.8 m/s, 114 km/h.
        Assert.NotEmpty(slopeCaps);
        Assert.InRange(slopeCaps.Min(), 28.0f, 35.0f);
    }
}
