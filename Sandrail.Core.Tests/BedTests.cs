using System.Numerics;
using Sandrail.Core.Cars;
using Sandrail.Core.Generation;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Tests;

/// <summary>M3d's bed: the ground set under the road, with 25° banks and cuttings either side (M3-PLAN, M3d's checks).</summary>
public class BedTests
{
    private static readonly Lazy<PieceCatalogue> Catalogue = new(() => PieceCatalogue.Parse(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "content", "base", "pieces"), "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))));

    private static readonly Lazy<(Generated Made, LaidTrack Laid, Terrain Original)[]> Tracks = new(() =>
        new ulong[] { 1, 2, 5 }.Select(seed =>
        {
            Generated g = TrackGenerator.Generate(seed, new GenSettings(), Catalogue.Value);
            return (g, g.Track.Lay(Catalogue.Value), new Terrain(seed, new TerrainSettings()));
        }).ToArray());

    /// <summary>Points along the route's road: its middle and both edges, every few metres, with which way is out from each edge.</summary>
    private static IEnumerable<(Vector3 Middle, Vector3 Left, Vector3 Right, Vector3 Out)> Along(LaidTrack track, float step)
    {
        foreach (PlacedPiece piece in track.Route)
        {
            float half = piece.Def.WidthM / 2.0f;
            foreach (ICentreline c in piece.Road().Pieces)
            {
                for (float s = step / 2.0f; s < c.Length; s += step)
                {
                    Frame f = c.FrameAt(s);
                    Vector3 flatRight = Vector3.Normalize(new Vector3(f.Right.X, 0.0f, f.Right.Z));
                    yield return (f.Position, f.Across(-half, 0.0f), f.Across(half, 0.0f), flatRight);
                }
            }
        }
    }

    /// <summary>Under the road the ground is just below it, hidden by the slab and not poking through.</summary>
    [Fact]
    public void UnderTheRoadTheGroundIsJustBelowIt()
    {
        foreach ((Generated made, LaidTrack laid, _) in Tracks.Value)
        {
            foreach ((Vector3 middle, _, _, _) in Along(laid, 3.0f))
            {
                float gap = middle.Y - made.Terrain.HeightAt(middle.X, middle.Z);
                Assert.InRange(gap, Bed.UnderRoadM - 0.15f, Bed.UnderRoadM + 0.15f);
            }
        }
    }

    /// <summary>
    /// His ask: a car that leaves the road can get back on. Just beyond each edge the ground meets the road
    /// within a little of its height, and going out from it never climbs or falls steeper than the bank, or
    /// than the ground already did there.
    /// </summary>
    [Fact]
    public void BesideTheRoadTheGroundMeetsItAndFallsAwayNoSteeperThanTheBank()
    {
        float tan = MathF.Tan(Bed.BankDeg * (MathF.PI / 180.0f));
        foreach ((Generated made, LaidTrack laid, Terrain original) in Tracks.Value)
        {
            Terrain t = made.Terrain;
            foreach ((_, Vector3 left, Vector3 right, Vector3 outward) in Along(laid, 5.0f))
            {
                foreach ((Vector3 edge, float side) in new[] { (left, -1.0f), (right, 1.0f) })
                {
                    Vector3 o = outward * side;
                    Vector3 near = edge + (o * 1.0f);
                    if (Inside(laid, near))
                    {
                        continue;
                    }

                    // Meets the road: 1 m out, within the bed's depth and one metre's worth of bank.
                    float drop = edge.Y - t.HeightAt(near.X, near.Z);
                    Assert.InRange(drop, -0.6f, Bed.UnderRoadM + tan + 0.3f);

                    // Then no steeper than the bank, or than the ground already was.
                    for (float d = 2.0f; d <= 8.0f; d += 2.0f)
                    {
                        Vector3 a = edge + (o * d);
                        Vector3 b = edge + (o * (d + 1.0f));
                        if (Inside(laid, a) || Inside(laid, b))
                        {
                            break;
                        }

                        float bedded = MathF.Abs(t.HeightAt(b.X, b.Z) - t.HeightAt(a.X, a.Z));
                        float natural = MathF.Abs(original.HeightAt(b.X, b.Z) - original.HeightAt(a.X, a.Z));
                        Assert.True(bedded <= MathF.Max(tan, natural) + 0.12f,
                            $"{made.Track.Id}: {d} m out from the edge at ({edge.X:F0}, {edge.Z:F0}) the ground changes {bedded:F2} m in a metre");
                    }
                }
            }
        }
    }

    [Fact]
    public void TheBanksArePaintedDirt()
    {
        foreach ((Generated made, LaidTrack laid, Terrain original) in Tracks.Value)
        {
            int banks = 0;
            foreach ((_, Vector3 left, Vector3 right, Vector3 outward) in Along(laid, 5.0f))
            {
                foreach ((Vector3 edge, float side) in new[] { (left, -1.0f), (right, 1.0f) })
                {
                    Vector3 p = edge + (outward * side * 2.0f);
                    if (MathF.Abs(made.Terrain.HeightAt(p.X, p.Z) - original.HeightAt(p.X, p.Z)) > 1.0f)
                    {
                        Assert.Same(Surfaces.Dirt, made.Terrain.SurfaceAt(p.X, p.Z));
                        banks++;
                    }
                }
            }

            Assert.True(banks > 0, $"{made.Track.Id} has no bank a metre tall to look at");
        }
    }

    /// <summary>Well away from the road the ground is as the terrain made it.</summary>
    [Fact]
    public void FarFromTheRoadTheGroundIsUntouched()
    {
        foreach ((Generated made, LaidTrack laid, Terrain original) in Tracks.Value)
        {
            Vector3[] road = Along(laid, 2.0f).Select(a => a.Middle).ToArray();
            int checkedPoints = 0;
            for (float z = -500.0f; z <= 500.0f; z += 37.0f)
            {
                for (float x = -500.0f; x <= 500.0f; x += 37.0f)
                {
                    if (road.All(r => new Vector2(r.X - x, r.Z - z).Length() > 80.0f))
                    {
                        Assert.Equal(original.HeightAt(x, z), made.Terrain.HeightAt(x, z));
                        checkedPoints++;
                    }
                }
            }

            Assert.True(checkedPoints > 100);
        }
    }

    [Fact]
    public void TheSameTrackGetsTheSameBed()
    {
        (Generated made, LaidTrack laid, Terrain original) = Tracks.Value[0];
        Terrain again = Bed.Lay(original, laid);
        Assert.Equal(made.Terrain.Heights, again.Heights);
    }

    /// <summary>Whether a point is on (or right beside) any stretch of the route's road: where a check from another stretch's edge doesn't apply.</summary>
    private static bool Inside(LaidTrack track, Vector3 p)
    {
        foreach ((Vector3 middle, _, _, _) in Along(track, 2.0f))
        {
            if (new Vector2(middle.X - p.X, middle.Z - p.Z).Length() < 6.5f)
            {
                return true;
            }
        }

        return false;
    }
}
