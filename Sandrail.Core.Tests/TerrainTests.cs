using Sandrail.Core.Cars;
using Sandrail.Core.Generation;

namespace Sandrail.Core.Tests;

/// <summary>M3a: the terrain, a pure function of a seed and its dials (M3-PLAN, M3a's checks).</summary>
public class TerrainTests
{
    private static readonly Lazy<Terrain> One = new(() => new Terrain(1UL, new TerrainSettings()));

    /// <summary>Every sample's bits, folded: FNV-1a over the heights, so any change anywhere shows.</summary>
    private static ulong Digest(Terrain terrain)
    {
        ulong hash = 14695981039346656037UL;
        foreach (float h in terrain.Heights)
        {
            hash ^= (uint)BitConverter.SingleToInt32Bits(h);
            hash *= 1099511628211UL;
        }

        return hash;
    }

    /// <summary>
    /// The known answer, as <c>Hash64</c>'s: seed 1 at the shipped dials, every sample to the last bit.
    /// If this moves, every generated track moves with it, and that's a version change (PLAN, track codes).
    /// </summary>
    [Fact]
    public void SeedOneMakesTheKnownTerrain()
    {
        Assert.Equal(KnownDigest, Digest(One.Value));
    }

    private const ulong KnownDigest = 17732728842014489761UL;

    [Fact]
    public void TheSameSeedMakesTheSameTerrain()
    {
        Assert.Equal(Digest(One.Value), Digest(new Terrain(1UL, new TerrainSettings())));
    }

    [Fact]
    public void AnotherSeedMakesAnotherTerrain()
    {
        Assert.NotEqual(Digest(One.Value), Digest(new Terrain(2UL, new TerrainSettings())));
    }

    [Fact]
    public void TheSamplesAreTheFieldAtTheirPoints()
    {
        Terrain t = One.Value;
        foreach ((int i, int j) in new[] { (0, 0), (512, 512), (100, 900), (1024, 1024), (333, 77) })
        {
            float x = (i * Terrain.SpacingM) - (Terrain.SizeM / 2.0f);
            float z = (j * Terrain.SpacingM) - (Terrain.SizeM / 2.0f);
            Assert.Equal(t.Field(x, z), t.At(i, j));
            Assert.Equal(t.At(i, j), t.HeightAt(x, z));
        }
    }

    [Fact]
    public void HeightsBetweenSamplesLieBetweenThem()
    {
        Terrain t = One.Value;
        float a = t.HeightAt(10.0f, 20.0f);
        float b = t.HeightAt(11.0f, 20.0f);
        float mid = t.HeightAt(10.5f, 20.0f);
        Assert.InRange(mid, MathF.Min(a, b) - 1e-4f, MathF.Max(a, b) + 1e-4f);
    }

    /// <summary>The ground flattens past the grid (M3-PLAN decision 6): flat at the edge, all there on the grid.</summary>
    [Fact]
    public void TheGroundIsFlatAtItsEdgeAndWholeOnTheGrid()
    {
        Terrain t = One.Value;
        for (int k = 0; k < Terrain.Samples; k += 16)
        {
            Assert.Equal(0.0f, t.At(k, 0));
            Assert.Equal(0.0f, t.At(0, k));
            Assert.Equal(0.0f, t.At(k, Terrain.Samples - 1));
            Assert.Equal(0.0f, t.At(Terrain.Samples - 1, k));
        }

        Assert.Equal(1.0f, Terrain.EdgeFade(Terrain.GridHalfM, -Terrain.GridHalfM));
        Assert.Equal(1.0f, Terrain.EdgeFade(0.0f, 0.0f));
        Assert.InRange(Terrain.EdgeFade(400.0f, 0.0f), 0.01f, 0.99f);
    }

    /// <summary>Not flat: the shipped dials make hills on the grid, which the bench measured (worklog, M3a).</summary>
    [Fact]
    public void TheGridHasRelief()
    {
        Terrain t = One.Value;
        float low = float.MaxValue;
        float high = float.MinValue;
        for (float z = -300.0f; z <= 300.0f; z += 10.0f)
        {
            for (float x = -300.0f; x <= 300.0f; x += 10.0f)
            {
                low = MathF.Min(low, t.HeightAt(x, z));
                high = MathF.Max(high, t.HeightAt(x, z));
            }
        }

        Assert.True(high - low > 2.0f, $"The grid only spans {high - low:F2} m");
    }

    [Fact]
    public void FlatSettingsMakeFlatGround()
    {
        var flat = new Terrain(1UL, new TerrainSettings { HillsM = 0.0f, PlainsM = 0.0f });
        Assert.All(flat.Heights, h => Assert.Equal(0.0f, h));
    }

    /// <summary>Dirt on the high ground, grass in the hollows, sand between, steep faces included.</summary>
    [Fact]
    public void HeightPaintsTheGround()
    {
        Terrain t = One.Value;
        var seen = new HashSet<Surface>();
        for (float z = -300.0f; z <= 300.0f; z += 7.0f)
        {
            for (float x = -300.0f; x <= 300.0f; x += 7.0f)
            {
                float h = t.HeightAt(x, z);
                Surface expected = h > Terrain.DirtAboveM ? Surfaces.Dirt : h < Terrain.GrassBelowM ? Surfaces.Grass : Surfaces.Sand;
                Assert.Same(expected, t.SurfaceAt(x, z));
                seen.Add(expected);
            }
        }

        Assert.Contains(Surfaces.Sand, seen);
    }

    [Fact]
    public void ReliefRampsInAboveItsThreshold()
    {
        Assert.Equal(0.0f, Terrain.ReliefShare(-1.0f, 0.3f));
        Assert.Equal(1.0f, Terrain.ReliefShare(0.6f, 0.3f));
        Assert.Equal(1.0f, Terrain.ReliefShare(1.0f, 0.3f));
        Assert.True(Terrain.ReliefShare(0.2f, 0.3f) > Terrain.ReliefShare(0.2f, 0.8f), "Rarer relief is less hilly at the same sample.");
    }

    [Fact]
    public void EachStageHasItsOwnSeed()
    {
        ulong[] stages = [GenStage.Terrain, GenStage.Route, GenStage.Stunts, GenStage.Ground];
        Assert.Equal(stages.Length, stages.Select(s => GenStage.Seed(1UL, s)).Distinct().Count());
    }

    [Theory]
    [InlineData(-1.0f, 3.0f, 200.0f, 3, 0.45f, 500.0f, 0.3f)]
    [InlineData(50.0f, 60.0f, 200.0f, 3, 0.45f, 500.0f, 0.3f)]
    [InlineData(50.0f, 3.0f, 5.0f, 3, 0.45f, 500.0f, 0.3f)]
    [InlineData(50.0f, 3.0f, 200.0f, 0, 0.45f, 500.0f, 0.3f)]
    [InlineData(50.0f, 3.0f, 200.0f, 3, 1.0f, 500.0f, 0.3f)]
    [InlineData(50.0f, 3.0f, 200.0f, 3, 0.45f, 100.0f, 0.3f)]
    [InlineData(50.0f, 3.0f, 200.0f, 3, 0.45f, 500.0f, 1.5f)]
    [InlineData(float.NaN, 3.0f, 200.0f, 3, 0.45f, 500.0f, 0.3f)]
    public void UnsoundSettingsAreRefused(float hills, float plains, float feature, int octaves, float gain, float region, float rarity)
    {
        var settings = new TerrainSettings
        {
            HillsM = hills, PlainsM = plains, FeatureM = feature, Octaves = octaves, Gain = gain, RegionM = region, ReliefRarity = rarity,
        };
        Assert.NotEmpty(settings.Problems());
        Assert.Throws<ArgumentException>(() => new Terrain(1UL, settings));
    }
}
