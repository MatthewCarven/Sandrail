using System.Numerics;

namespace Sandrail.Core.Pieces;

/// <summary>
/// Triangles as plain arrays, with no Godot types: what the sweep hands to Game, the way Overburden's
/// mesher hands it buffers.
/// </summary>
/// <remarks>
/// <b>Triangles are wound counter-clockwise seen from outside</b>, so <c>(b − a) × (c − a)</c> points
/// out. Godot's front faces are clockwise, so Game reverses each triangle as it converts.
/// </remarks>
public sealed class MeshData
{
    public List<Vector3> Positions { get; } = new();

    public List<Vector3> Normals { get; } = new();

    /// <summary>Metres: u across the profile, v along the centreline.</summary>
    public List<Vector2> Uvs { get; } = new();

    public List<int> Indices { get; } = new();

    public int TriangleCount => Indices.Count / 3;

    public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        Positions.Add(position);
        Normals.Add(normal);
        Uvs.Add(uv);
        return Positions.Count - 1;
    }

    public void AddTriangle(int a, int b, int c)
    {
        Indices.Add(a);
        Indices.Add(b);
        Indices.Add(c);
    }

    public void Append(MeshData other)
    {
        int offset = Positions.Count;
        Positions.AddRange(other.Positions);
        Normals.AddRange(other.Normals);
        Uvs.AddRange(other.Uvs);
        foreach (int index in other.Indices)
        {
            Indices.Add(index + offset);
        }
    }

    /// <summary>
    /// The enclosed volume, by the divergence theorem: positive for a closed mesh wound outward, which
    /// is how a test knows the winding is right without looking.
    /// </summary>
    public float SignedVolume()
    {
        double sum = 0.0;
        for (int i = 0; i < Indices.Count; i += 3)
        {
            Vector3 a = Positions[Indices[i]];
            Vector3 b = Positions[Indices[i + 1]];
            Vector3 c = Positions[Indices[i + 2]];
            sum += Vector3.Dot(a, Vector3.Cross(b, c));
        }

        return (float)(sum / 6.0);
    }
}
