using System.Numerics;

namespace Sandrail.Core.Pieces;

/// <summary>
/// A road's cross-section: a closed outline in (right, up), with the driving surface along y = 0 so the
/// centreline is where the wheels are.
/// </summary>
/// <param name="Points">The outline in order, closed implicitly from the last point back to the first.</param>
/// <param name="SurfaceWidth">
/// The flat width of the driving surface, which the outline starts with: the first points run across
/// it from left to right, so the sweep's texture coordinate across, u, is 0..SurfaceWidth on the road
/// and beyond it on the lips, sides and underneath. The road shader paints lines only inside it.
/// </param>
public sealed record Profile(IReadOnlyList<Vector2> Points, float SurfaceWidth)
{
    /// <summary>
    /// A slab: a flat top with rounded lips, straight sides and a flat bottom. Thick enough and it reads
    /// as earthworks down to the ground; thin, as a road in the air.
    /// </summary>
    /// <param name="lipRadius">
    /// Rounds the top edges, so a wheel ray running off the side meets a curve rather than a corner
    /// (M1-PLAN fact 4).
    /// </param>
    public static Profile Slab(float width, float thickness, float lipRadius = 0.15f, int lipSegments = 3)
    {
        float half = width / 2.0f;
        var points = new List<Vector2>
        {
            new(-half + lipRadius, 0.0f),
            new(half - lipRadius, 0.0f),
        };

        // Right lip, from the top round to the side.
        var rightCentre = new Vector2(half - lipRadius, -lipRadius);
        for (int k = 1; k <= lipSegments; k++)
        {
            float a = (MathF.PI / 2.0f) * (1.0f - (k / (float)lipSegments));
            points.Add(rightCentre + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * lipRadius));
        }

        points.Add(new Vector2(half, -thickness));
        points.Add(new Vector2(-half, -thickness));
        points.Add(new Vector2(-half, -lipRadius));

        // Left lip, from the side round to the top; its last point is the first, so it's left out.
        var leftCentre = new Vector2(-half + lipRadius, -lipRadius);
        for (int k = 1; k < lipSegments; k++)
        {
            float a = MathF.PI - ((MathF.PI / 2.0f) * (k / (float)lipSegments));
            points.Add(leftCentre + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * lipRadius));
        }

        return new Profile(points, width - (2.0f * lipRadius));
    }

    /// <summary>Twice the outline's area, positive if it runs counter-clockwise in (right, up).</summary>
    public float SignedArea2
    {
        get
        {
            float sum = 0.0f;
            for (int i = 0; i < Points.Count; i++)
            {
                Vector2 a = Points[i];
                Vector2 b = Points[(i + 1) % Points.Count];
                sum += (a.X * b.Y) - (b.X * a.Y);
            }

            return sum;
        }
    }
}
