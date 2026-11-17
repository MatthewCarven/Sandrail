using System.Numerics;
using Sandrail.Core.Generation;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tests;

public class SweepTests
{
    private static readonly Frame Origin = Frame.Level(Vector3.Zero, 0.0f);

    private static readonly Profile Road = Profile.Slab(10.0f, 0.5f);

    private static IEnumerable<(string Name, MeshData Mesh)> Meshes()
    {
        yield return ("straight", Sweep.Build(new LineCentreline(Origin, 40.0f), Road));
        // One piece at a time: in a chain, one piece's end cap meets the next one's start cap, and the
        // edges they share belong to four triangles. Each piece is closed on its own.
        Chain ramp = new Chain(Origin).Pitch(20.0f, 15.0f).Line(6.0f);
        yield return ("ramp's foot", Sweep.Build(ramp.Pieces[0], Road));
        yield return ("ramp's face", Sweep.Build(ramp.Pieces[1], Road));
        yield return ("banked turn", Sweep.Build(new TurnCentreline(Origin, 180.0f, 35.0f, 30.0f, 5.0f), Profile.Slab(10.0f, 7.0f)));
        yield return ("loop", Sweep.Build(new LoopCentreline(Origin, 18.0f, 7.0f, 13.0f), Profile.Slab(8.0f, 0.5f, 0.1f)));
    }

    [Fact]
    public void EveryPieceIsClosed()
    {
        // Welded by position, every edge of a closed mesh belongs to exactly two triangles. A hole or a
        // flap would show up as an edge with one.
        foreach ((string name, MeshData mesh) in Meshes())
        {
            var uses = new Dictionary<(Key, Key), int>();
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    Key a = Weld(mesh.Positions[mesh.Indices[t + e]]);
                    Key b = Weld(mesh.Positions[mesh.Indices[t + ((e + 1) % 3)]]);
                    var edge = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                    uses[edge] = uses.GetValueOrDefault(edge) + 1;
                }
            }

            var open = uses.Where(u => u.Value != 2).ToList();
            Assert.True(open.Count == 0, $"{name}: {open.Count} edges not shared by exactly two triangles");
        }
    }

    [Fact]
    public void EveryPieceIsWoundOutward()
    {
        foreach ((string name, MeshData mesh) in Meshes())
        {
            Assert.True(mesh.SignedVolume() > 0.0f, $"{name}: volume {mesh.SignedVolume()} is not positive");

            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                Vector3 a = mesh.Positions[mesh.Indices[t]];
                Vector3 b = mesh.Positions[mesh.Indices[t + 1]];
                Vector3 c = mesh.Positions[mesh.Indices[t + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (face.LengthSquared() < 1e-10f)
                {
                    continue;
                }

                Assert.True(
                    Vector3.Dot(face, mesh.Normals[mesh.Indices[t]]) > 0.0f,
                    $"{name}: triangle {t / 3} faces against its normal");
            }
        }
    }

    [Fact]
    public void AStraightSlabHasTheVolumeOfItsCrossSection()
    {
        // 10 m by 0.5 m, less the four little corners the two lips round off.
        MeshData mesh = Sweep.Build(new LineCentreline(Origin, 40.0f), Road);
        float area = MathF.Abs(Road.SignedArea2) / 2.0f;

        Assert.Equal(area * 40.0f, mesh.SignedVolume(), 1);
        Assert.InRange(area, 4.95f, 5.0f);
    }

    [Fact]
    public void TheRoadSurfaceIsUpAndAtTheCentreline()
    {
        MeshData mesh = Sweep.Build(new LineCentreline(Origin, 40.0f), Road);

        // Every vertex with a road texture coordinate is on top: y = 0, facing straight up.
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            if (mesh.Uvs[i].X < Road.SurfaceWidth - 0.001f && mesh.Normals[i].Y > 0.99f)
            {
                Assert.Equal(0.0f, mesh.Positions[i].Y, 4);
            }
        }

        Assert.Equal(10.0f - (2.0f * 0.15f), Road.SurfaceWidth, 4);
    }

    [Fact]
    public void AStraightIsOneSegmentAndACurveIsMany()
    {
        int straight = Sweep.Build(new LineCentreline(Origin, 100.0f), Road).TriangleCount;
        int turn = Sweep.Build(new TurnCentreline(Origin, 90.0f, 30.0f), Road).TriangleCount;

        Assert.True(turn > 20 * straight / 2, $"a turn has {turn} triangles and a straight {straight}");
    }

    [Fact]
    public void TheBumpsNeverDipBelowThePlainAndFadeToNothingAtTheBorder()
    {
        var field = new BumpField(seed: 7);
        float highest = 0.0f;
        for (float x = 0.0f; x <= field.SizeM; x += 1.0f)
        {
            for (float z = 0.0f; z <= field.SizeM; z += 1.0f)
            {
                float h = field.HeightAt(x, z);
                Assert.InRange(h, 0.0f, field.AmplitudeM);
                highest = MathF.Max(highest, h);
            }

            Assert.Equal(0.0f, field.HeightAt(x, 0.0f));
            Assert.Equal(0.0f, field.HeightAt(0.0f, x));
        }

        Assert.True(highest > field.AmplitudeM / 2.0f, $"the highest bump is only {highest} m");
        Assert.Equal(field.HeightAt(60.5f, 40.25f), new BumpField(seed: 7).HeightAt(60.5f, 40.25f));
    }

    private readonly record struct Key(long X, long Y, long Z) : IComparable<Key>
    {
        public int CompareTo(Key other) => (X, Y, Z).CompareTo((other.X, other.Y, other.Z));
    }

    private static Key Weld(Vector3 p) =>
        new((long)MathF.Round(p.X * 1000.0f), (long)MathF.Round(p.Y * 1000.0f), (long)MathF.Round(p.Z * 1000.0f));
}
