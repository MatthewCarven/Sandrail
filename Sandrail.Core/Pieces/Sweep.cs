using System.Numerics;

namespace Sandrail.Core.Pieces;

/// <summary>
/// Turns a centreline and a cross-section into a closed mesh: the cross-section placed at frames along
/// the centreline, joined into quads, with a cap on each end (M1-PLAN, M1c).
/// </summary>
/// <remarks>
/// <para>
/// <b>The same triangles are the collision mesh</b>, so the road you see is the road the wheels hit.
/// </para>
/// <para>
/// Each edge of the outline gets its own strip of vertices with that edge's normal, so the slab's
/// corners stay sharp under light while its length is smooth. A straight is one segment long; a curve
/// gets one segment per few degrees of turn.
/// </para>
/// </remarks>
public static class Sweep
{
    /// <summary>The texture u a cap is given, beyond any road, so the shader draws it as concrete.</summary>
    public const float CapU = 1000.0f;

    public static MeshData Build(IEnumerable<ICentreline> pieces, Profile profile, float maxTurnDeg = 3.0f)
    {
        var mesh = new MeshData();
        foreach (ICentreline piece in pieces)
        {
            mesh.Append(Build(piece, profile, maxTurnDeg));
        }

        return mesh;
    }

    public static MeshData Build(ICentreline line, Profile profile, float maxTurnDeg = 3.0f)
    {
        int segments = Math.Max(1, (int)MathF.Ceiling(line.TurnDeg / maxTurnDeg));
        var frames = new Frame[segments + 1];
        var distances = new float[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            distances[i] = line.Length * i / segments;
            frames[i] = line.FrameAt(distances[i]);
        }

        IReadOnlyList<Vector2> outline = profile.Points;
        int n = outline.Count;

        // Which way round the outline runs decides which way its edges' normals point, and which way
        // round the triangles go to face out.
        bool clockwise = profile.SignedArea2 < 0.0f;
        var mesh = new MeshData();
        float u = 0.0f;

        for (int j = 0; j < n; j++)
        {
            Vector2 a = outline[j];
            Vector2 b = outline[(j + 1) % n];
            Vector2 edge = b - a;
            float edgeLength = edge.Length();
            Vector2 normal = Vector2.Normalize(clockwise ? new Vector2(-edge.Y, edge.X) : new Vector2(edge.Y, -edge.X));

            int first = mesh.Positions.Count;
            for (int i = 0; i <= segments; i++)
            {
                Frame f = frames[i];
                Vector3 n3 = Vector3.Normalize((f.Right * normal.X) + (f.Up * normal.Y));
                mesh.AddVertex(f.Across(a.X, a.Y), n3, new Vector2(u, distances[i]));
                mesh.AddVertex(f.Across(b.X, b.Y), n3, new Vector2(u + edgeLength, distances[i]));
            }

            for (int i = 0; i < segments; i++)
            {
                int a0 = first + (2 * i);
                int b0 = a0 + 1;
                int a1 = a0 + 2;
                int b1 = a0 + 3;
                if (clockwise)
                {
                    mesh.AddTriangle(a0, b0, b1);
                    mesh.AddTriangle(a0, b1, a1);
                }
                else
                {
                    mesh.AddTriangle(a0, b1, b0);
                    mesh.AddTriangle(a0, a1, b1);
                }
            }

            u += edgeLength;
        }

        AddCap(mesh, frames[0], outline, -frames[0].Forward, facingBack: true, clockwise);
        AddCap(mesh, frames[segments], outline, frames[segments].Forward, facingBack: false, clockwise);
        return mesh;
    }

    /// <summary>A fan from the outline's centroid, which is fine for the convex outlines the slabs are.</summary>
    private static void AddCap(MeshData mesh, Frame frame, IReadOnlyList<Vector2> outline, Vector3 normal, bool facingBack, bool clockwise)
    {
        var centroid = Vector2.Zero;
        foreach (Vector2 p in outline)
        {
            centroid += p;
        }

        centroid /= outline.Count;
        var uv = new Vector2(CapU, 0.0f);
        int centre = mesh.AddVertex(frame.Across(centroid.X, centroid.Y), normal, uv);
        int first = mesh.Positions.Count;
        foreach (Vector2 p in outline)
        {
            mesh.AddVertex(frame.Across(p.X, p.Y), normal, uv);
        }

        // Seen from behind, the outline runs the way it was written; seen from in front, mirrored.
        bool reverse = facingBack == clockwise;
        for (int j = 0; j < outline.Count; j++)
        {
            int a = first + j;
            int b = first + ((j + 1) % outline.Count);
            if (reverse)
            {
                mesh.AddTriangle(centre, b, a);
            }
            else
            {
                mesh.AddTriangle(centre, a, b);
            }
        }
    }
}
