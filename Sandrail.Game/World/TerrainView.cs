using Godot;
using Sandrail.Core.Cars;
using Sandrail.Core.Generation;

namespace Sandrail.Game.World;

/// <summary>
/// The generated ground in the world (M3-PLAN, M3a): a mesh to look at, a height field to drive on, and
/// its surface painted by height and slope, read by the buggy where its wheels touch.
/// </summary>
/// <remarks>
/// <para>
/// <b>The collider is a <see cref="HeightMapShape3D"/> at a metre</b>, 1025 × 1025, square and a power of
/// two and one, which Jolt takes as a height field of its own (M3-PLAN fact 6). <b>The mesh is every
/// other sample</b>, a vertex every 2 m: 263,000 vertices is plenty to look at, and the surface it shows
/// is within a few centimetres of the one the wheels meet on ground this gentle.
/// </para>
/// <para>
/// <b>One body, many surfaces</b> (fact 7): the body is tagged <see cref="PaintedTag"/>, and the buggy
/// asks the terrain what's under each wheel instead of reading one surface off the body.
/// </para>
/// </remarks>
public static class TerrainView
{
    /// <summary>The surface tag that means "ask by position": the terrain's.</summary>
    public const string PaintedTag = "painted";

    private const int MeshStep = 2;

    private const string Shader = """
        shader_type spatial;

        varying vec3 world_position;

        float grid_line(vec2 coordinate, float sharpness) {
            vec2 width = max(fwidth(coordinate), vec2(1e-4));
            vec2 distance = abs(fract(coordinate - 0.5) - 0.5) / width;
            return 1.0 - clamp(min(distance.x, distance.y) * sharpness, 0.0, 1.0);
        }

        void vertex() {
            world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        }

        void fragment() {
            float minor = grid_line(world_position.xz / 10.0, 1.0);
            float major = grid_line(world_position.xz / 100.0, 0.5);
            ALBEDO = COLOR.rgb * (1.0 - (0.18 * minor) - (0.3 * major));
            ROUGHNESS = 0.95;
        }
        """;

    /// <summary>The colour a surface is painted, as the plain's own.</summary>
    public static Color Colour(Surface surface) => surface.Id switch
    {
        "base:sand" => new Color(0.76f, 0.64f, 0.45f),
        "base:dirt" => new Color(0.5f, 0.38f, 0.26f),
        "base:grass" => new Color(0.33f, 0.48f, 0.24f),
        _ => new Color(0.36f, 0.36f, 0.38f),
    };

    /// <summary>The terrain as a body with its mesh and its height field, named for its seed.</summary>
    public static StaticBody3D Build(Terrain terrain, ulong seed)
    {
        var body = new StaticBody3D { Name = $"Terrain {seed}", CollisionLayer = Cars.Buggy.WorldLayer };
        body.SetMeta("surface", PaintedTag);

        float[] heights = new float[Terrain.Samples * Terrain.Samples];
        for (int k = 0; k < heights.Length; k++)
        {
            heights[k] = terrain.Heights[k];
        }

        body.AddChild(new CollisionShape3D
        {
            Shape = new HeightMapShape3D { MapWidth = Terrain.Samples, MapDepth = Terrain.Samples, MapData = heights },
        });
        body.AddChild(new MeshInstance3D
        {
            Mesh = Mesh(terrain),
            MaterialOverride = new ShaderMaterial { Shader = new Shader { Code = Shader } },
        });
        return body;
    }

    private static ArrayMesh Mesh(Terrain terrain)
    {
        int side = ((Terrain.Samples - 1) / MeshStep) + 1;
        var vertices = new Vector3[side * side];
        var normals = new Vector3[side * side];
        var colours = new Color[side * side];
        float half = Terrain.SizeM / 2.0f;

        for (int b = 0; b < side; b++)
        {
            for (int a = 0; a < side; a++)
            {
                int i = a * MeshStep;
                int j = b * MeshStep;
                float x = (i * Terrain.SpacingM) - half;
                float z = (j * Terrain.SpacingM) - half;
                int k = (b * side) + a;
                vertices[k] = new Vector3(x, terrain.At(i, j), z);
                float dx = (terrain.At(i + 1, j) - terrain.At(i - 1, j)) / (2.0f * Terrain.SpacingM);
                float dz = (terrain.At(i, j + 1) - terrain.At(i, j - 1)) / (2.0f * Terrain.SpacingM);
                normals[k] = new Vector3(-dx, 1.0f, -dz).Normalized();
                // Steep ground drawn darker, so it reads as steep: the colour only, not the grip.
                float steep = Mathf.Clamp((terrain.SlopeDegAt(x, z) - 6.0f) / 40.0f, 0.0f, 0.35f);
                colours[k] = Colour(terrain.SurfaceAt(x, z)).Darkened(steep).SrgbToLinear();
            }
        }

        var indices = new int[(side - 1) * (side - 1) * 6];
        int n = 0;
        for (int b = 0; b < side - 1; b++)
        {
            for (int a = 0; a < side - 1; a++)
            {
                int k = (b * side) + a;
                int right = k + 1;
                int down = k + side;
                int diagonal = down + 1;

                // Wound so the faces look up (Godot's front faces are clockwise seen from the front).
                indices[n++] = k;
                indices[n++] = right;
                indices[n++] = down;
                indices[n++] = right;
                indices[n++] = diagonal;
                indices[n++] = down;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Godot.Mesh.ArrayType.Normal] = normals;
        arrays[(int)Godot.Mesh.ArrayType.Color] = colours;
        arrays[(int)Godot.Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
