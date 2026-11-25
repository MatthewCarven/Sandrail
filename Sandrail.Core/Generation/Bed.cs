using System.Numerics;
using Sandrail.Core.Pieces;
using Sandrail.Core.Tracks;

namespace Sandrail.Core.Generation;

/// <summary>
/// The road's bed in the ground (M3-PLAN, stage 4, M3d; PLAN suggestion 5): the terrain set just under
/// the road wherever it runs, built up into a bank beside raised road and cut down into a cutting beside
/// sunk road, both at <see cref="BankDeg"/>, so a car that leaves the road can drive back onto it.
/// </summary>
/// <remarks>
/// <para>
/// <b>25°, at his word</b> (2026-11-23: banks rather than sheer sides, "it would be nice if a player who has
/// gone off the track can get back on again", and 25° over 45° so it climbs from a standstill). The banks
/// and the bed are painted dirt, which the `climb` oracle measured good for 25.3° from rest.
/// </para>
/// <para>
/// <b>How:</b> the route's road is walked every <see cref="SampleM"/>, and at each step its cross-section
/// stamps the ground: under the road, the road's own height (its tilt on a bank included) less
/// <see cref="UnderRoadM"/>; beside it, at a distance d from the nearer edge, never lower than the edge less
/// d tan 25° (the bank) and never higher than the edge plus d tan 25° (the cutting). Where several roads
/// stamp one spot, the highest bed, the highest bank and the lowest cutting win. Out of reach of every
/// road, the ground is as it was.
/// </para>
/// <para>
/// A pure function of the terrain and the track, so a generated track loaded from its file gets the same bed.
/// </para>
/// </remarks>
public static class Bed
{
    /// <summary>The banks' and cuttings' slope, degrees: the dirt climbs it from rest.</summary>
    public const float BankDeg = 25.0f;

    /// <summary>How far under the road's surface the ground is set: hidden by the slab, low enough not to show through it.</summary>
    public const float UnderRoadM = 0.2f;

    /// <summary>How far along the road between stamps, metres: close enough that no sample under the road is missed on a corner's outside.</summary>
    private const float SampleM = 0.4f;

    /// <summary>The longest bank, metres from the road's edge: about 30 m of fill.</summary>
    private const float MaxReachM = 64.0f;

    /// <summary>Ground the bed moved by more than this, metres, is painted as bank.</summary>
    private const float PaintM = 0.25f;

    /// <summary>tan 15°: the ground beside a road is taken to slope away at up to this when working out how far its bank reaches.</summary>
    private const float NaturalTan = 0.2679f;

    /// <summary>The terrain with the route's bed laid in it.</summary>
    public static Terrain Lay(Terrain terrain, LaidTrack track) => Lay(terrain, track.Route);

    /// <summary>
    /// The terrain with a bed laid under some pieces: the route's, in the game; in the editor (M5d), every
    /// piece placed, since a track being built has no route yet and its ground should still meet its road.
    /// </summary>
    public static Terrain Lay(Terrain terrain, IEnumerable<PlacedPiece> pieces)
    {
        const int N = Terrain.Samples;
        float half = Terrain.SizeM / 2.0f;
        float tan = MathF.Tan(BankDeg * (MathF.PI / 180.0f));

        float[] under = new float[N * N];
        float[] fill = new float[N * N];
        float[] cut = new float[N * N];
        Array.Fill(under, float.NegativeInfinity);
        Array.Fill(fill, float.NegativeInfinity);
        Array.Fill(cut, float.PositiveInfinity);

        foreach (PlacedPiece piece in pieces)
        {
            // A piece on pillars lays no bed (PIECES-PLAN fact 7): the highest bed wins, and it would bury the road
            // under it. Its legs stand on whatever is laid below.
            if (piece.Pillars)
            {
                continue;
            }

            float halfWidth = piece.Def.WidthM / 2.0f;
            foreach (ICentreline c in piece.Road().Pieces)
            {
                // A loop stands on a flat pad at its foot's height (M3c): its road, upside down and all, stamped
                // straight down onto it. Generated ground under a loop would otherwise come up through it.
                Vector3 baseAt = piece.Start.Position;
                bool loop = c is LoopCentreline;
                int steps = Math.Max(1, (int)MathF.Ceiling(c.Length / SampleM));
                for (int k = 0; k <= steps; k++)
                {
                    Frame f = c.FrameAt(c.Length * k / steps);
                    if (loop)
                    {
                        f = Frame.Of(new Vector3(f.Position.X, baseAt.Y, f.Position.Z), piece.Start.Forward, Vector3.UnitY);
                    }

                    Stamp(f, halfWidth, banks: k % 2 == 0 || k == steps, alongM: loop ? 1.0f : SampleM * 0.75f);
                }
            }

            // A jump's gap is ground at the launch's foot (M3c), as the plain is under the shakedown's jumps: a
            // hill there would stand in the car's way, and a hollow is a hole to fall in short of the landing.
            if (piece.Out.Kind == ConnectorKind.Lip)
            {
                WorldConnector lip = piece.Out;
                Frame gapStart = TrackGrid.Edge(lip.Tile, lip.Facing, piece.Placement.Level, lip.Facing);
                float gapM = lip.Gap * TrackGrid.TileM;
                int steps = Math.Max(1, (int)MathF.Ceiling(gapM / SampleM));
                for (int k = 0; k <= steps; k++)
                {
                    Frame f = gapStart with { Position = gapStart.Position + (gapStart.Forward * (gapM * k / steps)) };
                    Stamp(f, halfWidth, banks: k % 2 == 0 || k == steps, alongM: SampleM * 0.75f);
                }
            }
        }

        float[] heights = new float[N * N];
        byte[] paint = new byte[N * N];
        for (int s = 0; s < heights.Length; s++)
        {
            float ground = terrain.Heights[s];
            if (!float.IsNegativeInfinity(under[s]))
            {
                heights[s] = under[s];
                paint[s] = 1;
                continue;
            }

            float shaped = MathF.Min(MathF.Max(ground, fill[s]), cut[s]);
            heights[s] = shaped;
            paint[s] = MathF.Abs(shaped - ground) > PaintM ? (byte)1 : (byte)0;
        }

        return new Terrain(terrain, heights, paint);

        // One step's cross-section stamped into the ground: the bed under it, and every 0.8 m the banks either side.
        void Stamp(Frame f, float halfWidth, bool banks, float alongM)
        {
            var flatForward = new Vector2(f.Forward.X, f.Forward.Z);
            var flatRight = new Vector2(f.Right.X, f.Right.Z);
            float rightLength = flatRight.Length();
            if (flatForward.LengthSquared() < 1e-6f || rightLength < 1e-3f)
            {
                return;
            }

            flatForward = Vector2.Normalize(flatForward);
            Vector2 across = flatRight / rightLength;
            Vector3 left = f.Across(-halfWidth, 0.0f);
            Vector3 right = f.Across(halfWidth, 0.0f);

            // How far a bank might need to reach: the taller of the two edges' heights over the ground there,
            // over what the bank gains on ground itself sloping away at up to 15°. Stopped short, a bank ends
            // in a step where it meets ground still falling (or a cutting, ground still rising).
            float reach = halfWidth + 1.0f;
            if (banks)
            {
                float tallest = MathF.Max(
                    MathF.Abs(left.Y - terrain.HeightAt(left.X, left.Z)),
                    MathF.Abs(right.Y - terrain.HeightAt(right.X, right.Z)));
                reach = halfWidth + MathF.Min(MaxReachM, ((tallest + 1.0f) / (tan - NaturalTan)) + 2.0f);
            }

            int i0 = Math.Max(0, (int)MathF.Floor(f.Position.X - reach + half));
            int i1 = Math.Min(N - 1, (int)MathF.Ceiling(f.Position.X + reach + half));
            int j0 = Math.Max(0, (int)MathF.Floor(f.Position.Z - reach + half));
            int j1 = Math.Min(N - 1, (int)MathF.Ceiling(f.Position.Z + reach + half));
            for (int j = j0; j <= j1; j++)
            {
                float z = j - half;
                for (int i = i0; i <= i1; i++)
                {
                    float x = i - half;
                    var offset = new Vector2(x - f.Position.X, z - f.Position.Z);
                    float lateral = Vector2.Dot(offset, across);
                    float along = Vector2.Dot(offset, flatForward);
                    int s = (j * N) + i;

                    if (MathF.Abs(lateral) <= halfWidth && MathF.Abs(along) <= alongM)
                    {
                        // Under the road: its surface at this offset, tilt and all.
                        float surface = f.Position.Y + (f.Right.Y * lateral / rightLength);
                        under[s] = MathF.Max(under[s], surface - UnderRoadM);
                        continue;
                    }

                    if (!banks)
                    {
                        continue;
                    }

                    Vector3 edge = lateral < 0.0f ? left : right;
                    float d = new Vector2(x - edge.X, z - edge.Z).Length();
                    if (d > reach)
                    {
                        continue;
                    }

                    float top = edge.Y - UnderRoadM;
                    fill[s] = MathF.Max(fill[s], top - (d * tan));
                    cut[s] = MathF.Min(cut[s], top + (d * tan));
                }
            }
        }
    }
}
