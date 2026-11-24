using Sandrail.Core.Generation;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>M4a: track codes, the generator's version, and the fingerprint a ghost names its track by (M4-PLAN, M4a's checks).</summary>
public class TrackCodeTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static readonly Lazy<Generated> Five = new(() => TrackGenerator.Generate(5UL, new GenSettings(), Catalogue.Value));

    [Theory]
    [InlineData("5v1", 5UL, 1)]
    [InlineData(" 5V1 ", 5UL, 1)]
    [InlineData("483920v12", 483920UL, 12)]
    [InlineData("0v1", 0UL, 1)]
    public void CodesAreReadAsTyped(string text, ulong seed, int version)
    {
        Assert.True(TrackCode.TryParse(text, out TrackCode code, out string problem), problem);
        Assert.Equal(new TrackCode(seed, version), code);
    }

    [Fact]
    public void ABareSeedMeansThisBuildsVersion()
    {
        Assert.True(TrackCode.TryParse("5", out TrackCode code, out _));
        Assert.Equal(TrackCode.Of(5UL), code);
        Assert.True(code.Current);
        Assert.Equal($"5v{TrackGenerator.Version}", code.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("seed five")]
    [InlineData("-5")]
    [InlineData("5v")]
    [InlineData("v1")]
    [InlineData("5v0")]
    [InlineData("5v1x")]
    [InlineData("5.1")]
    [InlineData("99999999999999999999")]
    public void NonsenseIsRefusedInWords(string text)
    {
        Assert.False(TrackCode.TryParse(text, out _, out string problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void ACodeWritesAndReadsBack()
    {
        var code = new TrackCode(5UL, 1);
        Assert.Equal("5v1", code.ToString());
        Assert.Equal("gen:5v1", code.TrackId);
        Assert.True(TrackCode.TryParse(code.ToString(), out TrackCode back, out _));
        Assert.Equal(code, back);
    }

    /// <summary>A track's ID names its code; one saved before codes (<c>gen:5</c>) was made by version 1.</summary>
    [Fact]
    public void IdsNameTheirCodes()
    {
        Assert.Equal(new TrackCode(5UL, 1), TrackCode.FromId("gen:5v1"));
        Assert.Equal(new TrackCode(5UL, 1), TrackCode.FromId("gen:5"));
        Assert.Equal(new TrackCode(7UL, 3), TrackCode.FromId("gen:7v3"));
        Assert.Null(TrackCode.FromId("base:shakedown"));
        Assert.Null(TrackCode.FromId("gen:5v1" + TrackGenerator.OwnDialsSuffix));
        Assert.Null(TrackCode.FromId("gen:"));
        Assert.Null(TrackCode.FromId("gen:v1"));
    }

    [Fact]
    public void AGeneratedTrackCarriesItsCode()
    {
        Assert.Equal(TrackCode.Of(5UL).TrackId, Five.Value.Track.Id);
        Assert.Equal("Seed 5", Five.Value.Track.Name);

        // Other dials make another track, which has no code: its ID says so.
        Generated own = TrackGenerator.Generate(5UL, new GenSettings { Stunts = new StuntSettings { Density = 0.0f } }, Catalogue.Value);
        Assert.Null(TrackCode.FromId(own.Track.Id));
        Assert.EndsWith(TrackGenerator.OwnDialsSuffix, own.Track.Id, StringComparison.Ordinal);
    }

    private static string Print(Generated g) => TrackFingerprint.Of(g.Track, Catalogue.Value, g.Terrain, g.Rocks);

    [Fact]
    public void AFingerprintIsTheSameEachTimeAndIgnoresTheName()
    {
        Generated again = TrackGenerator.Generate(5UL, new GenSettings(), Catalogue.Value);
        Assert.Equal(Print(Five.Value), Print(again));
        Assert.Matches("^[0-9a-f]{8}$", Print(Five.Value));

        // The same track saved before codes, as gen:5, is the same track.
        Track renamed = Five.Value.Track with { Id = "gen:5", Name = "My favourite" };
        Assert.Equal(Print(Five.Value), TrackFingerprint.Of(renamed, Catalogue.Value, Five.Value.Terrain, Five.Value.Rocks));
    }

    /// <summary>A piece, a height or a rock that moves makes another track.</summary>
    [Fact]
    public void AFingerprintChangesWithWhatsDriven()
    {
        Generated g = Five.Value;
        string print = Print(g);

        TrackPiece[] pieces = g.Track.Pieces.ToArray();
        pieces[3] = pieces[3] with { Level = pieces[3].Level + 1 };
        Assert.NotEqual(print, TrackFingerprint.Of(g.Track with { Pieces = pieces }, Catalogue.Value, g.Terrain, g.Rocks));

        // The ground before its bed was laid: the same seed and dials, other heights under the road.
        Assert.NotEqual(print, TrackFingerprint.Of(g.Track, Catalogue.Value, new Terrain(5UL, new TerrainSettings()), g.Rocks));

        Assert.NotEqual(print, TrackFingerprint.Of(g.Track, Catalogue.Value, g.Terrain, g.Rocks.Skip(1).ToArray()));
        Assert.NotEqual(print, TrackFingerprint.Of(g.Track, Catalogue.Value, g.Terrain));
    }

    /// <summary>
    /// The generator's version is held to its output (M4-PLAN, "The model"): each version's seeds 1 to 10, as
    /// fingerprints, pinned and kept. A change that moves any of them fails here until
    /// <see cref="TrackGenerator.Version"/> goes up and the new ones are pinned beside the old, so a code always
    /// means one track. A change to the terrain, the bed or the rocks fails here too: it reaches tracks shipped
    /// and saved as files as well, and wants thinking about before it's pinned.
    /// </summary>
    [Fact]
    public void TheGeneratorVersionPinsItsTracks()
    {
        var pinned = new Dictionary<int, string>
        {
            // As M3 passed, 2026-11-24.
            [1] = "1:0ee16529 2:4a09abdd 3:2d5bd980 4:c834727f 5:0070ecd8 6:1280f63f 7:08cb7713 8:605be908 9:f8c18769 10:6f10bf9e",
        };

        string now = string.Join(" ", Enumerable.Range(1, 10).Select(s =>
            $"{s}:{Print(TrackGenerator.Generate((ulong)s, new GenSettings(), Catalogue.Value))}"));
        Assert.True(pinned.TryGetValue(TrackGenerator.Version, out string? these),
            $"Version {TrackGenerator.Version} has no pins yet. Pin it as: {now}");
        Assert.True(these == now,
            $"The generator's output changed under version {TrackGenerator.Version}. If that's meant, raise TrackGenerator.Version and pin: {now}");
    }

    /// <summary>
    /// Seed 5 as version 1 made it ships with the game (decision 3), so no change to the generator can take it
    /// away: its file loads clean, carries its code, and is the same track, ground and rocks and all.
    /// </summary>
    [Fact]
    public void SeedFiveShipsAsVersionOne()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "content", "base", "tracks", "gen-5v1.json");
        LaidTrack laid = Track.Load(File.ReadAllText(path), Catalogue.Value);
        Assert.Equal("gen:5v1", laid.Track.Id);

        TrackGround ground = laid.Track.Ground!;
        Terrain bedded = Bed.Lay(new Terrain(ground.Seed, ground.Terrain), laid);
        IReadOnlyList<Rock> rocks = Rocks.Scatter(ground.Seed, new RockSettings(), laid, bedded);
        string shipped = TrackFingerprint.Of(laid.Track, Catalogue.Value, bedded, rocks);

        // Version 1's seed 5, from the pins: true for as long as the file and the ground's code stand.
        Assert.Equal(VersionOneSeedFive, shipped);
    }

    private const string VersionOneSeedFive = "0070ecd8";
}
