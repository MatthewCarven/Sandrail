using System;
using System.Linq;
using Godot;
using Sandrail.Core.Driving;

namespace Sandrail.Game.World;

/// <summary>
/// The racing line drawn on the road: a thin strip coloured by the speed the autodriver plans there, red
/// where it's slowest through green where it's fastest. L shows and hides it.
/// </summary>
/// <remarks>
/// <para>
/// Unshaded, so it reads the same in sun or shadow, and drawn from both sides, so the strip's winding
/// never matters. Lifted a few centimetres along the road's own up, so it lies on a bank as it does on
/// the flat.
/// </para>
/// <para>
/// <b>The colours are scaled to a reference plan</b>, the autodriver's default, not to whichever plan is
/// drawn. So turning its aggression up turns the corners greener and down turns them redder, which is
/// the point of showing it.
/// </para>
/// </remarks>
public static class RacingLineView
{
    private const float HalfWidth = 0.18f;
    private const float Lift = 0.04f;

    /// <param name="speeds">The plan drawn, m/s for each sample.</param>
    /// <param name="reference">The plan whose slowest is red and fastest green; speeds outside it clamp.</param>
    public static MeshInstance3D Build(RacingLine line, float[] speeds, float[] reference)
    {
        int n = line.Count;
        float slowest = reference.Min();
        float fastest = reference.Max();
        var vertices = new Vector3[n * 2];
        var colours = new Color[n * 2];

        for (int i = 0; i < n; i++)
        {
            Vector3 up = line.UpAt(i).ToGodot();
            Vector3 forward = line.ForwardAt(i).ToGodot();
            Vector3 right = forward.Cross(up).Normalized();
            Vector3 centre = line.PositionAt(i).ToGodot() + (up * Lift);
            float t = fastest > slowest ? Math.Clamp((speeds[i] - slowest) / (fastest - slowest), 0.0f, 1.0f) : 1.0f;
            Color colour = t < 0.5f
                ? new Color(0.95f, 0.15f, 0.1f).Lerp(new Color(1.0f, 0.85f, 0.1f), t * 2.0f)
                : new Color(1.0f, 0.85f, 0.1f).Lerp(new Color(0.2f, 0.9f, 0.3f), (t - 0.5f) * 2.0f);

            vertices[2 * i] = centre - (right * HalfWidth);
            vertices[(2 * i) + 1] = centre + (right * HalfWidth);
            colours[2 * i] = colour;
            colours[(2 * i) + 1] = colour;
        }

        int segments = line.Closed ? n : n - 1;
        var indices = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            int a = 2 * i;
            int b = 2 * ((i + 1) % n);
            indices[(6 * i) + 0] = a;
            indices[(6 * i) + 1] = b + 1;
            indices[(6 * i) + 2] = a + 1;
            indices[(6 * i) + 3] = a;
            indices[(6 * i) + 4] = b;
            indices[(6 * i) + 5] = b + 1;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        return new MeshInstance3D { Name = "racing line", Mesh = mesh, MaterialOverride = material };
    }
}
