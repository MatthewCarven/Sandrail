using System.Linq;
using Godot;
using Sandrail.Core.Cars;
using Sandrail.Core.Driving;
using Sandrail.Core.Pieces;
using Num = System.Numerics;

namespace Sandrail.Game.World;

/// <summary>
/// Builds the test ground Core lays out: each road run swept into one mesh and one collider, and the
/// bump patch as a height field (M1-PLAN, M1c).
/// </summary>
/// <remarks>
/// <para>
/// <b>Core's triangles face out counter-clockwise and Godot's front faces are clockwise</b>, so each
/// triangle is reversed on the way in. Colliders collide on both faces anyway, so a winding slip can
/// never let the car fall through a road.
/// </para>
/// <para>
/// The bump patch is 129 × 129 heights, 128 one-metre cells, a power of two each way: the size height
/// fields are happiest at.
/// </para>
/// </remarks>
public static class TestGround
{
    private const string RoadShader = """
        shader_type spatial;

        uniform float road_width = 9.7;

        void fragment() {
            float u = UV.x;
            float v = UV.y;
            vec3 colour = vec3(0.56, 0.54, 0.5);
            if (u <= road_width + 0.001) {
                colour = vec3(0.2, 0.2, 0.22);
                float edge = min(u, road_width - u);
                if (edge > 0.12 && edge < 0.3) {
                    colour = vec3(0.92);
                }
                if (abs(u - road_width * 0.5) < 0.09 && fract(v / 6.0) < 0.5) {
                    colour = vec3(0.95, 0.8, 0.2);
                }
                // A faint band every 10 m, so speed reads on the road as it does on the plain.
                if (fract(v / 10.0) < 0.04) {
                    colour *= 0.82;
                }
            }
            ALBEDO = colour;
            ROUGHNESS = 0.9;
        }
        """;

    private const int BumpSamples = 129;

    public static void Build(Node3D parent, TestGroundLayout layout, Shader groundShader)
    {
        var shader = new Shader { Code = RoadShader };
        foreach (RoadRun run in layout.Runs)
        {
            MeshData mesh = Sweep.Build(run.Pieces, run.Profile);
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("road_width", run.Profile.SurfaceWidth);
            parent.AddChild(Body(run.Name, ToMesh(mesh), Collider(mesh), material, Surfaces.Road));
        }

        parent.AddChild(BumpPatch(layout, groundShader));
    }

    /// <summary>A Core vector as Godot's.</summary>
    public static Vector3 ToGodot(this Num.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>A Godot vector as Core's.</summary>
    public static Num.Vector3 ToCore(this Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// The corner lap's racing line for a car: the line that turns least while keeping its tyres on the
    /// flat of the road, half its track and half a tyre in from each edge (his ask, 2026-11-23).
    /// </summary>
    public static RacingLine CornerLapLine(TestGroundLayout layout, CarSpec spec)
    {
        RoadRun lap = layout.Runs.Single(r => r.Name == "corners");
        return new RacingLine(lap.Pieces, closed: true)
            .MinimumCurvature(lap.Profile.SurfaceWidth / 2.0f, (spec.TrackM / 2.0f) + 0.15f, 0.5f);
    }

    private static StaticBody3D Body(string name, Mesh mesh, Shape3D shape, Material material, Surface surface)
    {
        var body = new StaticBody3D { Name = name };
        body.SetMeta("surface", surface.Id);
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material });
        body.AddChild(new CollisionShape3D { Shape = shape });
        return body;
    }

    private static ArrayMesh ToMesh(MeshData data)
    {
        var vertices = new Vector3[data.Positions.Count];
        var normals = new Vector3[data.Positions.Count];
        var uvs = new Vector2[data.Positions.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = data.Positions[i].ToGodot();
            normals[i] = data.Normals[i].ToGodot();
            uvs[i] = new Vector2(data.Uvs[i].X, data.Uvs[i].Y);
        }

        var indices = new int[data.Indices.Count];
        for (int t = 0; t < indices.Length; t += 3)
        {
            indices[t] = data.Indices[t];
            indices[t + 1] = data.Indices[t + 2];
            indices[t + 2] = data.Indices[t + 1];
        }

        return BuildMesh(vertices, normals, uvs, indices);
    }

    private static ConcavePolygonShape3D Collider(MeshData data)
    {
        var faces = new Vector3[data.Indices.Count];
        for (int i = 0; i < faces.Length; i++)
        {
            faces[i] = data.Positions[data.Indices[i]].ToGodot();
        }

        var shape = new ConcavePolygonShape3D { BackfaceCollision = true };
        shape.SetFaces(faces);
        return shape;
    }

    private static StaticBody3D BumpPatch(TestGroundLayout layout, Shader groundShader)
    {
        const int N = BumpSamples;
        float half = (N - 1) / 2.0f;
        var heights = new float[N * N];
        for (int z = 0; z < N; z++)
        {
            for (int x = 0; x < N; x++)
            {
                heights[(z * N) + x] = layout.Bumps.HeightAt(x, z);
            }
        }

        var vertices = new Vector3[N * N];
        var normals = new Vector3[N * N];
        var uvs = new Vector2[N * N];
        for (int z = 0; z < N; z++)
        {
            for (int x = 0; x < N; x++)
            {
                float h = heights[(z * N) + x];
                float dx = heights[(z * N) + Mathf.Min(x + 1, N - 1)] - heights[(z * N) + Mathf.Max(x - 1, 0)];
                float dz = heights[(Mathf.Min(z + 1, N - 1) * N) + x] - heights[(Mathf.Max(z - 1, 0) * N) + x];
                vertices[(z * N) + x] = new Vector3(x - half, h, z - half);
                normals[(z * N) + x] = new Vector3(-dx / 2.0f, 1.0f, -dz / 2.0f).Normalized();
                uvs[(z * N) + x] = new Vector2(x, z);
            }
        }

        // Clockwise seen from above, which is Godot's front.
        var indices = new int[(N - 1) * (N - 1) * 6];
        int k = 0;
        for (int z = 0; z < N - 1; z++)
        {
            for (int x = 0; x < N - 1; x++)
            {
                int a = (z * N) + x;
                int b = a + 1;
                int c = a + N + 1;
                int d = a + N;
                indices[k++] = a;
                indices[k++] = b;
                indices[k++] = c;
                indices[k++] = a;
                indices[k++] = c;
                indices[k++] = d;
            }
        }

        var material = new ShaderMaterial { Shader = groundShader };
        material.SetShaderParameter("base_colour", new Color(0.5f, 0.38f, 0.26f));
        var shape = new HeightMapShape3D { MapWidth = N, MapDepth = N, MapData = heights };

        StaticBody3D body = Body("bumps", BuildMesh(vertices, normals, uvs, indices), shape, material, Surfaces.Dirt);
        body.Position = layout.BumpCentre.ToGodot();
        return body;
    }

    private static ArrayMesh BuildMesh(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Godot.Mesh.ArrayType.Normal] = normals;
        arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Godot.Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
