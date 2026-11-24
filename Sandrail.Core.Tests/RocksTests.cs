using System.Numerics;
using Sandrail.Core.Generation;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>The rest of M3d: rocks off the road, never in the way of driving the track or of getting back on it.</summary>
public class RocksTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static readonly Lazy<(Generated Made, LaidTrack Laid)[]> Tracks = new(() =>
        new ulong[] { 1, 2, 5, 8 }.Select(seed =>
        {
            Generated g = TrackGenerator.Generate(seed, new GenSettings(), Catalogue.Value);
            return (g, g.Track.Lay(Catalogue.Value));
        }).ToArray());

    [Fact]
    public void EveryTrackHasRocks()
    {
        Assert.All(Tracks.Value, t => Assert.True(t.Made.Rocks.Count >= 40, $"{t.Made.Track.Id} has {t.Made.Rocks.Count} rocks"));
    }

    /// <summary>A rock keeps 12 m from every road's edge, its own size allowed for: the racing line never meets one.</summary>
    [Fact]
    public void RocksKeepClearOfTheRoad()
    {
        float clear = new RockSettings().ClearM;
        foreach ((Generated made, LaidTrack laid) in Tracks.Value)
        {
            var road = new List<(Vector2 At, float Half)>();
            foreach (PlacedPiece p in laid.Route)
            {
                foreach (ICentreline c in p.Road().Pieces)
                {
                    for (float s = 0.0f; s <= c.Length; s += 1.0f)
                    {
                        Vector3 at = c.FrameAt(MathF.Min(s, c.Length)).Position;
                        road.Add((new Vector2(at.X, at.Z), p.Def.WidthM / 2.0f));
                    }
                }
            }

            foreach (Rock rock in made.Rocks)
            {
                var at = new Vector2(rock.Position.X, rock.Position.Z);
                float reach = MathF.Max(rock.Size.X, rock.Size.Z) / 2.0f;
                float nearest = road.Min(r => Vector2.Distance(at, r.At) - r.Half);
                Assert.True(nearest >= clear + reach - 1.0f, $"{made.Track.Id}: a rock {nearest:F1} m from the road's edge");
            }
        }
    }

    /// <summary>Never on the bed (the road's banks and cuttings, a jump's gap, a loop's pad), so a car can always drive back up.</summary>
    [Fact]
    public void RocksStayOffTheBed()
    {
        foreach ((Generated made, _) in Tracks.Value)
        {
            Assert.DoesNotContain(made.Rocks, r => made.Terrain.PaintedByBed(r.Position.X, r.Position.Z));
        }
    }

    /// <summary>Each sits on the ground, a little sunk into it.</summary>
    [Fact]
    public void RocksSitOnTheGround()
    {
        foreach ((Generated made, _) in Tracks.Value)
        {
            foreach (Rock r in made.Rocks)
            {
                float ground = made.Terrain.HeightAt(r.Position.X, r.Position.Z);
                Assert.InRange(r.Position.Y, ground - r.Size.Y, ground);
            }
        }
    }

    /// <summary>A pure function of the seed and the track: the same each time, and not in the track file.</summary>
    [Fact]
    public void RocksComeFromTheSeedAndNotTheFile()
    {
        Generated a = TrackGenerator.Generate(5UL, new GenSettings(), Catalogue.Value);
        Generated b = TrackGenerator.Generate(5UL, new GenSettings(), Catalogue.Value);
        Assert.Equal(a.Rocks, b.Rocks);

        Generated none = TrackGenerator.Generate(5UL, new GenSettings { Rocks = new RockSettings { PerKm = 0.0f } }, Catalogue.Value);
        Assert.Empty(none.Rocks);
        Assert.Equal(a.Track.Save(), none.Track.Save());

        LaidTrack laid = a.Track.Lay(Catalogue.Value);
        Assert.Equal(a.Rocks, Rocks.Scatter(5UL, new RockSettings(), laid, a.Terrain));
    }

    [Fact]
    public void UnsoundRockSettingsAreRefused()
    {
        Assert.NotEmpty(new RockSettings { PerKm = -1.0f }.Problems());
        Assert.NotEmpty(new RockSettings { ClearM = 2.0f }.Problems());
        Assert.NotEmpty(new RockSettings { SmallestM = 3.0f, LargestM = 1.0f }.Problems());
    }
}
