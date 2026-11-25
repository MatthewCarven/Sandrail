using System.Numerics;
using Sandrail.Core.Pieces;

namespace Sandrail.Core.Tracks;

/// <summary>How high a piece's road runs over one of its tiles, edges and banks included.</summary>
/// <param name="LowM">The lowest point of its road surface over the tile, metres in the world.</param>
/// <param name="HighM">The highest.</param>
/// <param name="Road">Whether any of its road is on the tile; if not (the inside of a big corner), the span is its levels.</param>
public readonly record struct TileSpan(float LowM, float HighM, bool Road);

/// <summary>Two pieces on one tile, the higher and the lower, and how far apart they are there.</summary>
/// <param name="Upper">The one whose road is higher over the tile.</param>
/// <param name="Lower">The other.</param>
/// <param name="GapM">From the lower road's highest point up to the upper's underside, its deck's thickness below its road.</param>
/// <param name="Apart">Whether the two are at different heights at all: the upper's road all above the lower's.</param>
/// <param name="Loop">Whether either is a loop, which nothing stacks with.</param>
public readonly record struct Stack(PlacedPiece Upper, PlacedPiece Lower, float GapM, bool Apart, bool Loop)
{
    /// <summary>One runs clear over the other: they may share the tile.</summary>
    public bool Clear => !Loop && Apart && GapM >= Stacking.ClearanceM - 1e-3f;

    /// <summary>The two at one height, as M2 meant by two pieces covering one tile.</summary>
    public bool Overlap => Loop || !Apart;
}

/// <summary>A post under a deck: its top, under the deck's edge, and how wide it is.</summary>
public readonly record struct Leg(Vector3 Top, float SizeM);

/// <summary>
/// Crossings (PIECES-PLAN, the model): when two pieces may share a tile, one above the other, and the legs that
/// hold up a piece with road under it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two levels clear</b> (fact 3): the car is about 2 m tall and a deck is <see cref="DeckM"/> thick, so the
/// lower road's highest point and the upper's underside are at least <see cref="ClearanceM"/> apart. Two level
/// roads two levels apart have 4.5 m; one level apart, 2.0 m.
/// </para>
/// <para>
/// <b>Measured on the road, over the tile they share</b> (fact 4): a banked corner's outer edge and a slope's
/// climb count, so a bank under a deck may need three levels where flat road needs two.
/// </para>
/// </remarks>
public static class Stacking
{
    /// <summary>From the lower road's highest point to the upper road's underside, metres.</summary>
    public const float ClearanceM = 4.0f;

    /// <summary>How thick a road on pillars is, and the loop's: the slab with nothing under it.</summary>
    public const float DeckM = 0.5f;

    /// <summary>A leg's width, metres: a square post.</summary>
    public const float LegM = 0.8f;

    /// <summary>How far in from the road's edge a leg stands, under the deck.</summary>
    public const float LegInsetM = 1.0f;

    /// <summary>About how far apart legs stand along the road: a tile, so they come at the seams on a straight.</summary>
    public const float LegSpacingM = 20.0f;

    /// <summary>How close a leg may stand to the edge of a road below it before it's left out and the deck spans over.</summary>
    public const float LegClearM = 1.0f;

    /// <summary>How far along a road its height is sampled for a span, metres.</summary>
    private const float SampleM = 1.0f;

    /// <summary>Whether a piece has a loop, which is taller than any clearance and never stacks.</summary>
    public static bool Loops(PieceDef def) => def.Path.Any(s => s.Loop is not null);

    /// <summary>How high a piece's road runs over a tile: its centre and both edges, sampled along it, wherever they're over the tile.</summary>
    public static TileSpan Span(PlacedPiece piece, Tile tile)
    {
        float x0 = TrackGrid.Origin.X + (tile.Column * TrackGrid.TileM) - 1e-3f;
        float z0 = TrackGrid.Origin.Z + (tile.Row * TrackGrid.TileM) - 1e-3f;
        float x1 = x0 + TrackGrid.TileM + 2e-3f;
        float z1 = z0 + TrackGrid.TileM + 2e-3f;
        float half = piece.Def.WidthM / 2.0f;
        float low = float.PositiveInfinity;
        float high = float.NegativeInfinity;
        foreach (ICentreline line in piece.Road().Pieces)
        {
            int steps = Math.Max(1, (int)MathF.Ceiling(line.Length / SampleM));
            for (int k = 0; k <= steps; k++)
            {
                Frame f = line.FrameAt(line.Length * k / steps);
                foreach (Vector3 at in (ReadOnlySpan<Vector3>)[f.Position, f.Across(-half, 0.0f), f.Across(half, 0.0f)])
                {
                    if (at.X >= x0 && at.X <= x1 && at.Z >= z0 && at.Z <= z1)
                    {
                        low = MathF.Min(low, at.Y);
                        high = MathF.Max(high, at.Y);
                    }
                }
            }
        }

        if (low <= high)
        {
            return new TileSpan(low, high, true);
        }

        // No road over this tile (the inside of a corner): the piece is there at its levels.
        float a = TrackGrid.Origin.Y + (piece.In.Level * TrackGrid.LevelM);
        float b = TrackGrid.Origin.Y + (piece.Out.Level * TrackGrid.LevelM);
        return new TileSpan(MathF.Min(a, b), MathF.Max(a, b), false);
    }

    /// <summary>Two pieces on a tile, which is higher, and whether one runs clear over the other.</summary>
    public static Stack Of(PlacedPiece a, PlacedPiece b, Tile tile)
    {
        TileSpan sa = Span(a, tile);
        TileSpan sb = Span(b, tile);
        bool aUp = sa.LowM > sb.LowM;
        (PlacedPiece upper, TileSpan su, PlacedPiece lower, TileSpan sl) = aUp ? (a, sa, b, sb) : (b, sb, a, sa);
        return new Stack(upper, lower, su.LowM - DeckM - sl.HighM, su.LowM > sl.HighM, Loops(a.Def) || Loops(b.Def));
    }

    /// <summary>
    /// The legs under a piece on pillars: under both edges of its deck, <see cref="LegInsetM"/> in, about every
    /// <see cref="LegSpacingM"/> along its road from its way in to its way out, each left out where it would stand
    /// on or beside the road of another piece below it, so the deck spans over that road (fact 5).
    /// </summary>
    /// <param name="deck">The piece.</param>
    /// <param name="others">The pieces that might be under it: the track's, it among them or not.</param>
    public static IReadOnlyList<Leg> Legs(PlacedPiece deck, IEnumerable<PlacedPiece> others)
    {
        var below = others.Where(o => !ReferenceEquals(o, deck) && o.Placement != deck.Placement).Select(Centre).ToList();
        IReadOnlyList<ICentreline> road = deck.Road().Pieces;
        float length = road.Sum(c => c.Length);
        int spans = Math.Max(1, (int)MathF.Round(length / LegSpacingM));
        float inset = (deck.Def.WidthM / 2.0f) - LegInsetM;
        var legs = new List<Leg>();
        for (int k = 0; k <= spans; k++)
        {
            Frame f = FrameAlong(road, length * k / spans);
            foreach (float side in (ReadOnlySpan<float>)[-inset, inset])
            {
                Vector3 top = f.Across(side, -DeckM);
                if (!below.Any(b => StandsOn(top, b)))
                {
                    legs.Add(new Leg(top, LegM));
                }
            }
        }

        return legs;
    }

    /// <summary>A piece's road down its middle, sampled every metre, and its half width.</summary>
    private static (Vector3[] Points, float Half) Centre(PlacedPiece piece)
    {
        var points = new List<Vector3>();
        foreach (ICentreline line in piece.Road().Pieces)
        {
            int steps = Math.Max(1, (int)MathF.Ceiling(line.Length / SampleM));
            for (int k = 0; k <= steps; k++)
            {
                points.Add(line.FrameAt(line.Length * k / steps).Position);
            }
        }

        return (points.ToArray(), piece.Def.WidthM / 2.0f);
    }

    /// <summary>Whether a leg would stand on or beside a road that runs below its top.</summary>
    private static bool StandsOn(Vector3 top, (Vector3[] Points, float Half) road)
    {
        float reach = road.Half + LegClearM + (LegM / 2.0f);
        foreach (Vector3 p in road.Points)
        {
            if (p.Y < top.Y && ((p.X - top.X) * (p.X - top.X)) + ((p.Z - top.Z) * (p.Z - top.Z)) < reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    private static Frame FrameAlong(IReadOnlyList<ICentreline> road, float distance)
    {
        foreach (ICentreline line in road)
        {
            if (distance <= line.Length)
            {
                return line.FrameAt(distance);
            }

            distance -= line.Length;
        }

        ICentreline last = road[^1];
        return last.FrameAt(last.Length);
    }
}
